using AulaVirtual.Modules.Exams.Api.Controllers;
using AulaVirtual.Modules.Exams.Application.Diagnostics;
using AulaVirtual.Modules.Exams.Application.Practice;
using AulaVirtual.Modules.Exams.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AulaVirtual.Modules.Exams.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddExamsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Practice with immediate correction (SCRUM-59) and attempts and progress (SCRUM-60).
        services.AddControllers().AddApplicationPart(typeof(StudentPracticeController).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPracticeRepository, PracticeRepository>();
        services.AddScoped<PracticeAdminUseCases>();
        services.AddScoped<StudentPracticeUseCases>();
        services.AddScoped<IPracticeProgressDirectory>(provider => provider.GetRequiredService<StudentPracticeUseCases>());

        // Entry diagnostic: question bank (SCRUM-47), the student's test (SCRUM-48/49) and the teacher's review.
        services.AddScoped<IDiagnosticRepository, DiagnosticRepository>();
        services.AddScoped<DiagnosticBankUseCases>();
        services.AddScoped<StudentDiagnosticUseCases>();
        services.AddScoped<DiagnosticReviewUseCases>();

        services.AddDbContext<ExamsDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be provided externally.");
            ExamsDbOptions.Configure(options, connectionString);
        });

        return services;
    }
}
