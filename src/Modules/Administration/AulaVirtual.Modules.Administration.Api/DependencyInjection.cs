using AulaVirtual.Modules.Administration.Api.Controllers;
using AulaVirtual.Modules.Administration.Application.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AulaVirtual.Modules.Administration.Api;

public static class DependencyInjection
{
    // Has no persistence of its own: it relies on the Users, Auth and Authorization modules
    // being registered as well (their contracts are resolved at request time).
    public static IServiceCollection AddAdministrationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllers().AddApplicationPart(typeof(AdminUsersController).Assembly);
        services.AddScoped<AdminUserUseCases>();
        services.AddScoped<AdminInstructorUseCases>();

        return services;
    }
}
