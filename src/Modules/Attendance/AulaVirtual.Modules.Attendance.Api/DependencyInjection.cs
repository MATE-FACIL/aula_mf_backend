using AulaVirtual.Modules.Attendance.Api.Controllers;
using AulaVirtual.Modules.Attendance.Application;
using AulaVirtual.Modules.Attendance.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AulaVirtual.Modules.Attendance.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddAttendanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllers().AddApplicationPart(typeof(SessionAttendanceController).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<AttendanceUseCases>();
        services.AddScoped<StudentAttendanceUseCases>();
        services.AddScoped<TeacherPanelUseCases>();

        services.AddDbContext<AttendanceDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be provided externally.");
            AttendanceDbOptions.Configure(options, connectionString);
        });

        return services;
    }
}
