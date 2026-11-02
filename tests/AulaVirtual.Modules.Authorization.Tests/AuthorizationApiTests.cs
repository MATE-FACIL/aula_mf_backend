using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.SharedKernel.Application.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace AulaVirtual.Modules.Authorization.Tests;

public sealed class AuthorizationApiTests
{
    [Fact]
    public async Task ProtectedCourseReturns401Then403AndAuthorizationMeReportsEffectiveAccess()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/courses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/authorization/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/courses")).StatusCode);

        var before = await client.GetFromJsonAsync<AuthorizationBody>("/api/v1/authorization/me");
        Assert.NotNull(before);
        Assert.Empty(before.Permissions);
        await new CatalogProvisioner(factory.Repository).ProvisionAsync(default);
        await new AuthorizationUseCases(factory.Repository, factory.Users,
            new AuthorizationTests.FakeChecker(factory.Repository))
            .AssignFirstAdministratorAsync(factory.UserId, default);
        var after = await client.GetFromJsonAsync<AuthorizationBody>("/api/v1/authorization/me");
        Assert.Contains(Roles.Administrator, after!.Roles);
        Assert.Equal(Permissions.All.Count, after.Permissions.Count);
    }

    private sealed record AuthorizationBody(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public Guid UserId { get; } = Guid.NewGuid();
        public AuthorizationTests.FakeRepository Repository { get; } = new();
        public AuthorizationTests.FakeUsers Users { get; } = new();

        public Factory() => Users.Existing.Add(UserId);

        public string Token()
        {
            var token = new JwtSecurityToken("AulaVirtual.Api", "AulaVirtual.Web",
                [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                 new Claim("user_id", UserId.ToString())],
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development")
                .UseSetting("Authentication:Jwt:Issuer", "AulaVirtual.Api")
                .UseSetting("Authentication:Jwt:Audience", "AulaVirtual.Web")
                .UseSetting("Authentication:Jwt:SigningKey", signingKey)
                .UseSetting("Storage:R2:Enabled", "false");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=not_used;Username=not_used",
                    ["Storage:R2:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthorizationRepository>();
                services.RemoveAll<IUserExistence>();
                services.RemoveAll<IPermissionChecker>();
                services.AddSingleton<IAuthorizationRepository>(Repository);
                services.AddSingleton<IUserExistence>(Users);
                services.AddSingleton<IPermissionChecker>(new AuthorizationTests.FakeChecker(Repository));
                services.AddSingleton<IFileStorage, UnusedStorage>();
            });
        }
    }

    private sealed class UnusedStorage : IFileStorage
    {
        public Task<StoredObject> StoreAsync(StoreFileRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, FileVisibility visibility, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileObjectInfo?> GetInfoAsync(string objectKey, FileVisibility visibility, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Uri GetPublicUrl(string objectKey) => throw new NotSupportedException();
        public Task<Uri> GetPrivateDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DirectUploadGrant> CreateDirectUploadAsync(DirectUploadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
