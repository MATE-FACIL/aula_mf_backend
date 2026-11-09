using Amazon.Runtime;
using Amazon.S3;
using AulaVirtual.SharedKernel.Application.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AulaVirtual.SharedKernel.Infrastructure.Storage;

public static class DependencyInjection
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration,
        bool isDevelopment = false)
    {
        var options = configuration.GetSection(R2StorageOptions.SectionName).Get<R2StorageOptions>()
            ?? new R2StorageOptions();
        if (!options.Enabled) return services;
        options.Validate(isDevelopment);
        services.AddSingleton(options);
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config { ServiceURL = options.Endpoint, ForcePathStyle = true,
                AuthenticationRegion = "auto" }));
        services.AddSingleton<IFileStorage, R2FileStorage>();
        return services;
    }
}
