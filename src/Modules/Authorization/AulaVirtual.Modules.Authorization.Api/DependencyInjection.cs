using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Authorization.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AulaVirtual.Modules.Authorization.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthorizationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddApplicationPart(typeof(AuthorizationController).Assembly);
        services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddScoped<IRoleDirectory, RoleDirectory>();
        services.AddScoped<AuthorizationUseCases>();
        services.AddScoped<CatalogProvisioner>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();
        services.AddDbContext<AuthorizationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be provided externally.");
            options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "authorization"))
                .ReplaceService<IHistoryRepository, SnakeCaseNpgsqlHistoryRepository>();
        });
        return services;
    }
}
