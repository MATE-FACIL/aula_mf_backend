using Asp.Versioning;
using AulaVirtual.Api.ExceptionHandling;
using AulaVirtual.Api.Public;
using AulaVirtual.Modules.Administration.Api;
using AulaVirtual.Modules.Attendance.Api;
using AulaVirtual.Modules.Auth.Api;
using AulaVirtual.Modules.Authorization.Api;
using AulaVirtual.Modules.Certificates.Api;
using AulaVirtual.Modules.Courses.Api;
using AulaVirtual.Modules.Enrollments.Api;
using AulaVirtual.Modules.Exams.Api;
using AulaVirtual.Modules.Users.Api;
using AulaVirtual.SharedKernel.Infrastructure.Storage;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, loggerConfiguration) =>
    loggerConfiguration.WriteTo.Console());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddControllers();
builder.Services.AddPublicApi(builder.Configuration);
builder.Services.AddOpenApi("v1");
builder.Services.AddFileStorage(builder.Configuration);

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

var allowedOrigins =
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services
    .AddAuthModule(builder.Configuration)
    .AddUsersModule(builder.Configuration)
    .AddAuthorizationModule(builder.Configuration)
    .AddCoursesModule(builder.Configuration)
    .AddEnrollmentsModule(builder.Configuration)
    .AddExamsModule(builder.Configuration)
    .AddAttendanceModule(builder.Configuration)
    .AddCertificatesModule(builder.Configuration)
    .AddAdministrationModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseCors("DevelopmentCors");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
