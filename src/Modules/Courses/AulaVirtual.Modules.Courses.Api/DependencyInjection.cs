using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Application.Instructors;
using AulaVirtual.Modules.Courses.Application.Courses;
using AulaVirtual.Modules.Courses.Infrastructure.Persistence;
using AulaVirtual.SharedKernel.Application.Storage;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AulaVirtual.Modules.Courses.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddCoursesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<ICourseOfferingRepository, CourseOfferingRepository>();
        services.AddScoped<IOfferingDirectory, OfferingDirectory>();
        services.AddScoped<IOfferingScheduleDirectory, OfferingScheduleDirectory>();
        // Teacher panel (SCRUM-69) and practice (SCRUM-59/60): small read contracts over courses.
        services.AddScoped<TeachingDirectory>();
        services.AddScoped<ITeachingDirectory>(provider => provider.GetRequiredService<TeachingDirectory>());
        services.AddScoped<ILessonDirectory>(provider => provider.GetRequiredService<TeachingDirectory>());
        services.AddScoped<ICourseRepository, CourseRepository>();
        // Lessons (SCRUM-55): one repository serves the administration and the read contract for students.
        services.AddScoped<CourseContentRepository>();
        services.AddScoped<ICourseContentRepository>(provider => provider.GetRequiredService<CourseContentRepository>());
        // Files (SCRUM-58): storage is optional per environment; without it, file uploads answer 503.
        services.AddScoped(provider => new LessonFiles(provider.GetService<IFileStorage>()));
        services.AddScoped<ICourseContentDirectory, SignedCourseContentDirectory>();
        services.AddScoped<CourseContentUseCases>();
        // Mathematics levels (SCRUM-39).
        services.AddScoped<IMathLevelRepository, MathLevelRepository>();
        services.AddScoped<MathLevelUseCases>();
        services.AddScoped<IMathLevelDirectory>(provider => provider.GetRequiredService<MathLevelUseCases>());
        services.AddScoped<IPublicCourseCatalogRepository, PublicCourseCatalogRepository>();
        services.AddScoped<PublicCourseCatalogUseCases>();
        services.AddScoped<CourseUseCases>();
        if (configuration.GetValue<bool>("Storage:R2:Enabled"))
            services.AddScoped<CourseCoverUseCases>();
        services.AddSingleton<IValidator<SaveCourseCommand>, SaveCourseValidator>();
        var coverOptions = new CourseCoverOptions();
        configuration.GetSection(CourseCoverOptions.SectionName).Bind(coverOptions);
        coverOptions.Validate();
        services.AddSingleton(coverOptions);
        services.AddScoped<CourseOfferingUseCases>();
        services.AddScoped<IInstructorRepository, InstructorRepository>();
        services.AddScoped<InstructorUseCases>();
        services.AddScoped<CourseOfferingCommercialUseCases>();
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<CoursesDbContext>(options =>
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
                    "courses"))
                .ReplaceService<IHistoryRepository, SnakeCaseNpgsqlHistoryRepository>();
        });

        return services;
    }
}
