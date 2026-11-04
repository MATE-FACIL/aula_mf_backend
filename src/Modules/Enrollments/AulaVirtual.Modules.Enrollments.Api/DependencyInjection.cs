using AulaVirtual.Modules.Enrollments.Api.Controllers;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AulaVirtual.Modules.Enrollments.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddEnrollmentsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllers().AddApplicationPart(typeof(EnrollmentsController).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<EnrollmentUseCases>();
        services.AddScoped<StudentScheduleUseCases>();
        services.AddScoped<FamilyUseCases>();

        services.AddDbContext<EnrollmentsDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be provided externally.");
            EnrollmentsDbOptions.Configure(options, connectionString);
        });

        return services;
    }
}
