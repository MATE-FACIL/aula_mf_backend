using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Application.Instructors;
using AulaVirtual.Modules.Courses.Application.Courses;
using AulaVirtual.Modules.Courses.Infrastructure.Persistence;
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
        services.AddScoped<ICourseRepository, CourseRepository>();
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
