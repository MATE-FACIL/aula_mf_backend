using Amazon.Runtime;
using Amazon.S3;
using AulaVirtual.SharedKernel.Application.Storage;
using AulaVirtual.SharedKernel.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AulaVirtual.SharedKernel.Storage.Tests;

public sealed class StorageTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Keys_Are_Server_Shaped_And_Reject_Traversal()
    {
        var key = FileObjectKeys.CourseCover(Id, Guid.NewGuid());
        FileObjectKeys.Validate(key);
        FileObjectKeys.Validate(FileObjectKeys.SessionRecording(Id, Id, Guid.NewGuid()));
        FileObjectKeys.Validate(FileObjectKeys.Certificate(Id, Guid.NewGuid()));
        FileObjectKeys.Validate(FileObjectKeys.LessonFile(Id, Id, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => FileObjectKeys.Validate($"courses/{Id:N}/lessons/{Id:N}/files/x.pdf"));
        Assert.Throws<ArgumentException>(() => FileObjectKeys.Validate("courses/../secret"));
        Assert.Throws<ArgumentException>(() => FileObjectKeys.Validate("courses/user-file.jpg"));
        Assert.Throws<ArgumentException>(() => FileObjectKeys.CourseCover(Guid.Empty, Id));
    }

    [Fact]
    public void Configuration_Rejects_Invalid_And_Selects_Separate_Buckets()
    {
        var options = Options();
        options.Validate();
        Assert.Equal("public-test", options.Bucket(FileVisibility.Public));
        Assert.Equal("private-test", options.Bucket(FileVisibility.Private));
        options.PrivateBucket = options.PublicBucket;
        Assert.Throws<InvalidOperationException>(() => options.Validate());
        options.PrivateBucket = "private-test";
        options.PublicBaseUrl = null;
        options.Validate();
        options.ValidateContent("image/png", 100, false);
        Assert.Throws<ArgumentException>(() => options.ValidateContent("video/mp4", 100, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateContent("image/png", 2001, false));
    }

    [Fact]
    public void HttpEndpoint_IsOnlyAllowedInDevelopment()
    {
        var options = Options();
        options.Endpoint = "http://host.docker.internal:9000";
        Assert.Throws<InvalidOperationException>(() => options.Validate(isDevelopment: true));
        options.AllowHttpEndpoint = true;
        options.Validate(isDevelopment: true);
        Assert.Throws<InvalidOperationException>(() => options.Validate(isDevelopment: false));
    }

    [Fact]
    public async Task SignedLinks_FollowTheEndpointScheme()
    {
        var options = Options();
        options.Endpoint = "http://host.docker.internal:9000";
        options.AllowHttpEndpoint = true;
        using var client = new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config { ServiceURL = options.Endpoint, ForcePathStyle = true, AuthenticationRegion = "auto" });
        var storage = new R2FileStorage(client, options);
        var key = FileObjectKeys.LessonFile(Id, Id, Guid.NewGuid());

        Assert.Equal("http", (await storage.GetPrivateDownloadUrlAsync(key, TimeSpan.FromMinutes(5))).Scheme);
        Assert.Equal("http", (await storage.CreateDirectUploadAsync(new DirectUploadRequest(key, FileVisibility.Private, "image/png", 10, TimeSpan.FromMinutes(5)))).Url.Scheme);
    }

    [Fact]
    public void Disabled_Storage_Does_Not_Register_Adapter()
    {
        var services = new ServiceCollection();
        services.AddFileStorage(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IFileStorage>());
    }

    [Fact]
    public async Task Signed_Grants_Are_Scoped_And_Contain_No_Secret_Key()
    {
        var options = Options();
        using var client = new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config { ServiceURL = options.Endpoint, ForcePathStyle = true, AuthenticationRegion = "auto" });
        var storage = new R2FileStorage(client, options);
        var key = FileObjectKeys.CourseCover(Id, Guid.NewGuid());
        var grant = await storage.CreateDirectUploadAsync(
            new DirectUploadRequest(key, FileVisibility.Private, "image/png", 100, TimeSpan.FromMinutes(5)));
        Assert.Equal("PUT", grant.Method);
        Assert.Equal("image/png", grant.Headers["Content-Type"]);
        Assert.Contains("/private-test/", grant.Url.AbsoluteUri);
        Assert.DoesNotContain(options.SecretAccessKey!, grant.Url.AbsoluteUri);
        var download = await storage.GetPrivateDownloadUrlAsync(key, TimeSpan.FromMinutes(5));
        Assert.Contains("/private-test/", download.AbsoluteUri);
        Assert.DoesNotContain(options.SecretAccessKey!, download.AbsoluteUri);
        Assert.Equal($"https://cdn.example.test/{key}", storage.GetPublicUrl(key)!.AbsoluteUri);
        options.PublicBaseUrl = null;
        Assert.Null(storage.GetPublicUrl(key));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateExpiration(TimeSpan.FromDays(8)));
    }

    private static R2StorageOptions Options() => new()
    {
        Enabled = true, Endpoint = "https://example.r2.cloudflarestorage.com",
        AccessKeyId = "test-access", SecretAccessKey = "test-secret",
        PublicBucket = "public-test", PrivateBucket = "private-test",
        PublicBaseUrl = "https://cdn.example.test", AllowedContentTypes = ["image/png"],
        MaxBackendUploadBytes = 1000, MaxDirectUploadBytes = 2000, MaxPresignedUrlSeconds = 3600
    };
}
