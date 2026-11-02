using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Application.Users.CreateUser;
using AulaVirtual.Modules.Users.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AulaVirtual.Modules.Users.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
        services.AddScoped<ICreateUser, CreateUserHandler>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserExistence, UserExistence>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IGuardianLinks, GuardianLinkStore>();
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<UsersDbContext>(options =>
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
                    "users"))
                .ReplaceService<
                    IHistoryRepository,
                    SnakeCaseNpgsqlHistoryRepository>();
        });

        return services;
    }
}
