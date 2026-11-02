using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace AulaVirtual.Modules.Auth.Tests;

public sealed class AuthApiTests
{
    private const string Issuer = "AulaVirtual.Api";
    private const string Audience = "AulaVirtual.Web";
    private static readonly string SigningKey = Convert.ToBase64String(
        RandomNumberGenerator.GetBytes(32));
    private static readonly string InvalidSigningKey = Convert.ToBase64String(
        RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task StudentLogin_ReturnsAccessTokenAndSecureCookieAttributes()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/student/login",
            new
            {
                username = "  g77340061 ",
                password = "valid-password"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(body);
        Assert.Equal(3, body.AccessToken.Split('.').Length);
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal(900, body.ExpiresIn);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("aula_virtual_refresh=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "refreshToken",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshCookie_IsSecureOutsideDevelopment()
    {
        await using var factory = new AuthApiFactory(
            environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/student/login",
            new
            {
                username = "G77340061",
                password = "valid-password"
            });

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"));

        Assert.Contains(
            "secure",
            cookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdministratorLogin_UsesEmailAndRejectsInvalidCredentialsGenerically()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var success = await client.PostAsJsonAsync(
            "/api/v1/auth/admin/login",
            new
            {
                email = " Admin@Empresa.com ",
                password = "valid-password"
            });
        var invalid = await client.PostAsJsonAsync(
            "/api/v1/auth/admin/login",
            new
            {
                email = "admin@empresa.com",
                password = "wrong"
            });

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        var problem = await invalid.Content.ReadAsStringAsync();
        Assert.Contains(
            "Usuario o contraseña incorrectos.",
            problem);
        Assert.DoesNotContain(
            "admin@empresa.com",
            problem,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InactiveAccount_CannotLogin()
    {
        await using var factory = new AuthApiFactory(
            deactivateStudent: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/student/login",
            new
            {
                username = "G77340061",
                password = "valid-password"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginRateLimit_AllowsTenRequestsPerMinutePerIp()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt < 11; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/student/login",
                new
                {
                    username = "unknown",
                    password = "wrong"
                });
        }

        using (lastResponse)
        {
            Assert.NotNull(lastResponse);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                lastResponse.StatusCode);
        }
    }

    [Fact]
    public async Task Cors_AllowsConfiguredOriginWithCredentials()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/auth/refresh");

        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add(
            "Access-Control-Request-Method",
            "POST");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(
                response.Headers.GetValues(
                    "Access-Control-Allow-Origin")));
        Assert.Equal(
            "true",
            Assert.Single(
                response.Headers.GetValues(
                    "Access-Control-Allow-Credentials")));
    }

    [Fact]
    public async Task ProtectedEndpoints_RequireBearerAuthentication()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var meResponse = await client.GetAsync(
            "/api/v1/auth/me");
        using var logoutAllResponse = await client.PostAsync(
            "/api/v1/auth/logout-all",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            logoutAllResponse.StatusCode);
    }

    [Fact]
    public async Task LoginToken_AuthorizesMeAndLogoutAll()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/student/login",
            new
            {
                username = "G77340061",
                password = "valid-password"
            });
        var login = await loginResponse.Content
            .ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(login);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var meResponse = await client.GetAsync(
            "/api/v1/auth/me");
        var currentAccount = await meResponse.Content
            .ReadFromJsonAsync<CurrentAccountBody>();
        using var logoutAllResponse = await client.PostAsync(
            "/api/v1/auth/logout-all",
            content: null);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.NotNull(currentAccount);
        Assert.Equal(factory.StudentAccount.Id, currentAccount.AuthAccountId);
        Assert.Equal(factory.StudentAccount.UserId, currentAccount.UserId);
        Assert.Equal(["username", "email"], currentAccount.LoginTypes);
        Assert.Equal("active", currentAccount.Status);
        Assert.Equal([Roles.Student], currentAccount.Roles);
        Assert.Equal("Ana Quispe", currentAccount.DisplayName);
        Assert.Equal(
            HttpStatusCode.NoContent,
            logoutAllResponse.StatusCode);
        Assert.All(
            factory.StudentAccount.Sessions,
            session => Assert.NotNull(session.RevokedAt));
    }

    [Fact]
    public async Task Refresh_RotatesCookieAndReturnsAnotherValidJwt()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/student/login",
            new
            {
                username = "G77340061",
                password = "valid-password"
            });
        var login = await loginResponse.Content
            .ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(login);

        using var refreshResponse = await client.PostAsync(
            "/api/v1/auth/refresh",
            content: null);
        var refresh = await refreshResponse.Content
            .ReadFromJsonAsync<LoginBody>();
        Assert.NotNull(refresh);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.NotEqual(login.AccessToken, refresh.AccessToken);
        Assert.Equal("Bearer", refresh.TokenType);
        Assert.Equal(900, refresh.ExpiresIn);
        Assert.Contains(
            "aula_virtual_refresh=",
            Assert.Single(refreshResponse.Headers.GetValues("Set-Cookie")));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", refresh.AccessToken);

        using var meResponse = await client.GetAsync(
            "/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expiration")]
    public async Task Me_RejectsInvalidBearerTokens(string invalidPart)
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var expires = invalidPart == "expiration"
            ? DateTimeOffset.UtcNow.AddMinutes(-1)
            : DateTimeOffset.UtcNow.AddMinutes(15);
        var token = CreateAccessToken(
            factory.StudentAccount.Id,
            invalidPart == "issuer" ? "WrongIssuer" : Issuer,
            invalidPart == "audience" ? "WrongAudience" : Audience,
            invalidPart == "signature" ? InvalidSigningKey : SigningKey,
            expires);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            "/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string CreateAccessToken(
        Guid authAccountId,
        string issuer,
        string audience,
        string signingKey,
        DateTimeOffset expires)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [new Claim(JwtRegisteredClaimNames.Sub, authAccountId.ToString())],
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record LoginBody(
        string AccessToken,
        string TokenType,
        int ExpiresIn);

    private sealed record CurrentAccountBody(
        Guid AuthAccountId,
        Guid UserId,
        IReadOnlyList<string> LoginTypes,
        string Status,
        IReadOnlyList<string> Roles,
        string? DisplayName);

    private sealed class AuthApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakeAuthRepository repository = new();
        private readonly FakeRoleDirectory roles = new();
        private readonly FakeUserDirectory users = new();
        private readonly string environment;

        public AuthAccount StudentAccount { get; }

        public AuthApiFactory(
            bool deactivateStudent = false,
            string environment = "Development")
        {
            this.environment = environment;
            var now = DateTimeOffset.UtcNow;
            StudentAccount = AuthAccount.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "encoded",
                now);
            StudentAccount.AddIdentifier(
                Guid.NewGuid(),
                LoginType.Username,
                "G77340061",
                now);
            StudentAccount.AddIdentifier(
                Guid.NewGuid(),
                LoginType.Email,
                "student@empresa.com",
                now);
            var administrator = AuthAccount.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "encoded",
                now);
            administrator.AddIdentifier(
                Guid.NewGuid(),
                LoginType.Email,
                "admin@empresa.com",
                now);

            if (deactivateStudent)
            {
                StudentAccount.Deactivate(now);
            }

            repository.Accounts.Add(StudentAccount);
            repository.Accounts.Add(administrator);
            roles.RolesByUser[StudentAccount.UserId] = [Roles.Student];
            users.Users.Add(new UserSummary(
                StudentAccount.UserId, "Ana", "Quispe", "Mamani", null, "DNI", "77340061", now, 0));
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder
                .UseEnvironment(environment)
                .UseSetting("Authentication:Jwt:Issuer", Issuer)
                .UseSetting("Authentication:Jwt:Audience", Audience)
                .UseSetting(
                    "Authentication:Jwt:SigningKey",
                    SigningKey);

            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            "Host=localhost;Database=not_used;Username=not_used",
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthRepository>();
                services.RemoveAll<IPasswordHasher>();

                services.RemoveAll<IRoleDirectory>();
                services.RemoveAll<IUserDirectory>();

                services.AddSingleton<IAuthRepository>(repository);
                services.AddSingleton<IRoleDirectory>(roles);
                services.AddSingleton<IUserDirectory>(users);
                services.AddSingleton<IPasswordHasher>(
                    new StubPasswordHasher("valid-password"));
            });
        }
    }
}
