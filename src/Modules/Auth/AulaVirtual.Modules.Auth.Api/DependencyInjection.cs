using System.Threading.RateLimiting;
using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Application.Accounts.ChangeStatus;
using AulaVirtual.Modules.Auth.Application.Accounts.CreateAccount;
using AulaVirtual.Modules.Auth.Application.Authentication;
using AulaVirtual.Modules.Auth.Application.Authentication.GetCurrentAccount;
using AulaVirtual.Modules.Auth.Application.Authentication.Login;
using AulaVirtual.Modules.Auth.Application.Authentication.Logout;
using AulaVirtual.Modules.Auth.Application.Authentication.Refresh;
using AulaVirtual.Modules.Auth.Infrastructure.Persistence;
using AulaVirtual.Modules.Auth.Infrastructure.Security;
using AulaVirtual.Modules.Auth.Api.Controllers;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace AulaVirtual.Modules.Auth.Api;

public static class DependencyInjection
{
    public const string LoginRateLimitPolicy = "auth-login";

    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var authenticationSettings = configuration
            .GetSection(AuthenticationSettings.SectionName)
            .Get<AuthenticationSettings>() ?? new AuthenticationSettings();
        var passwordHashingSettings = configuration
            .GetSection(PasswordHashingSettings.SectionName)
            .Get<PasswordHashingSettings>() ?? new PasswordHashingSettings();
        var jwtSettings = configuration
            .GetSection(JwtSettings.SectionName)
            .Get<JwtSettings>() ?? new JwtSettings();
        var signingKey = jwtSettings.GetSigningKeyBytes();

        services.AddSingleton(authenticationSettings);
        services.AddSingleton(passwordHashingSettings);
        services.AddSingleton(jwtSettings);
        services.TryAddSingleton(TimeProvider.System);
        services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<
            IValidator<CreateStudentAccountCommand>,
            CreateStudentAccountCommandValidator>();
        services.AddScoped<
            IValidator<CreateAdministratorAccountCommand>,
            CreateAdministratorAccountCommandValidator>();

        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshSessionHandler>();
        services.AddScoped<LogoutCurrentSessionHandler>();
        services.AddScoped<LogoutAllSessionsHandler>();
        services.AddScoped<GetCurrentAccountHandler>();
        services.AddScoped<CreateStudentAccountHandler>();
        services.AddScoped<CreateAdministratorAccountHandler>();
        services.AddScoped<ChangeAccountStatusHandler>();

        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAccountAdministration, AccountAdministration>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IPasswordGenerator, CryptographicPasswordGenerator>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
            });
        services.AddAuthorization();

        services.AddDbContext<AuthDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection must be provided externally.");
            }

            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    "auth"))
                .ReplaceService<
                    IHistoryRepository,
                    SnakeCaseNpgsqlHistoryRepository>();
        });

        var permitLimit = configuration.GetValue(
            "Authentication:LoginRateLimit:PermitLimit",
            10);
        var windowSeconds = configuration.GetValue(
            "Authentication:LoginRateLimit:WindowSeconds",
            60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy(
                LoginRateLimitPolicy,
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ??
                        "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }
}
