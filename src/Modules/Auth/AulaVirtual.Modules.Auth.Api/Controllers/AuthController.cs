using System.Security.Claims;
using Asp.Versioning;
using AulaVirtual.Modules.Auth.Api.Contracts;
using AulaVirtual.Modules.Auth.Application.Authentication;
using AulaVirtual.Modules.Auth.Application.Authentication.GetCurrentAccount;
using AulaVirtual.Modules.Auth.Application.Authentication.Login;
using AulaVirtual.Modules.Auth.Application.Authentication.Logout;
using AulaVirtual.Modules.Auth.Application.Authentication.Refresh;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;

namespace AulaVirtual.Modules.Auth.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IHostEnvironment environment) : ControllerBase
{
    private const string RefreshCookieName = "aula_virtual_refresh";
    private const string InvalidCredentialsMessage =
        "Usuario o contraseña incorrectos.";

    [HttpPost("student/login")]
    [EnableRateLimiting(DependencyInjection.LoginRateLimitPolicy)]
    public Task<IActionResult> StudentLogin(
        StudentLoginRequest request,
        [FromServices] LoginHandler handler,
        CancellationToken cancellationToken)
    {
        return LoginAsync(
            new LoginCommand(
                LoginType.Username,
                request.Username,
                request.Password),
            handler,
            cancellationToken);
    }

    [HttpPost("admin/login")]
    [EnableRateLimiting(DependencyInjection.LoginRateLimitPolicy)]
    public Task<IActionResult> AdministratorLogin(
        AdministratorLoginRequest request,
        [FromServices] LoginHandler handler,
        CancellationToken cancellationToken)
    {
        return LoginAsync(
            new LoginCommand(
                LoginType.Email,
                request.Email,
                request.Password),
            handler,
            cancellationToken);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromServices] RefreshSessionHandler handler,
        CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(
            RefreshCookieName,
            out var refreshToken);

        var result = await handler.ExecuteAsync(
            refreshToken ?? string.Empty,
            cancellationToken);

        if (result.Status != AuthenticationStatus.Succeeded)
        {
            DeleteRefreshCookie();
            return UnauthorizedProblem();
        }

        SetRefreshCookie(result.RefreshToken!);
        return Ok(ToResponse(result));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromServices] LogoutCurrentSessionHandler handler,
        CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(
            RefreshCookieName,
            out var refreshToken);

        await handler.ExecuteAsync(refreshToken, cancellationToken);
        DeleteRefreshCookie();

        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(
        [FromServices] LogoutAllSessionsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthAccountId(out var authAccountId))
        {
            return UnauthorizedProblem();
        }

        await handler.ExecuteAsync(authAccountId, cancellationToken);
        DeleteRefreshCookie();

        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(
        [FromServices] GetCurrentAccountHandler handler,
        [FromServices] IRoleDirectory roles,
        [FromServices] IUserDirectory users,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthAccountId(out var authAccountId))
        {
            return UnauthorizedProblem();
        }

        var account = await handler.ExecuteAsync(
            authAccountId,
            cancellationToken);

        if (account is null)
        {
            return UnauthorizedProblem();
        }

        // Each module has its own DbContext, so both lookups can run at the same time.
        var rolesTask = roles.GetRoleCodesByUserIdsAsync([account.UserId], cancellationToken);
        var userTask = users.GetAsync(account.UserId, cancellationToken);
        await Task.WhenAll(rolesTask, userTask);

        return Ok(new CurrentAccountResponse(
            account.AuthAccountId,
            account.UserId,
            account.LoginTypes.Select(ToApiValue).ToArray(),
            ToApiValue(account.Status),
            rolesTask.Result.GetValueOrDefault(account.UserId) ?? [],
            userTask.Result is { } user ? $"{user.GivenNames} {user.FirstSurname}" : null));
    }

    private async Task<IActionResult> LoginAsync(
        LoginCommand command,
        LoginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(
            command,
            cancellationToken);

        if (result.Status == AuthenticationStatus.InvalidRequest)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request.");
        }

        if (result.Status == AuthenticationStatus.Locked)
        {
            return Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many login attempts.");
        }

        if (result.Status != AuthenticationStatus.Succeeded)
        {
            return UnauthorizedProblem(InvalidCredentialsMessage);
        }

        SetRefreshCookie(result.RefreshToken!);
        return Ok(ToResponse(result));
    }

    private static AuthenticationResponse ToResponse(
        AuthenticationResult result)
    {
        return new AuthenticationResponse(
            result.AccessToken!,
            result.TokenType!,
            result.ExpiresIn!.Value);
    }

    private static string ToApiValue(LoginType loginType)
    {
        return loginType switch
        {
            LoginType.Username => "username",
            LoginType.Email => "email",
            _ => throw new ArgumentOutOfRangeException(nameof(loginType))
        };
    }

    private static string ToApiValue(AccountStatus status)
    {
        return status switch
        {
            AccountStatus.Active => "active",
            AccountStatus.Inactive => "inactive",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    private ObjectResult UnauthorizedProblem(string? detail = null)
    {
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized.",
            detail: detail);
    }

    private bool TryGetAuthAccountId(out Guid authAccountId)
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub"),
            out authAccountId);
    }

    private void SetRefreshCookie(string refreshToken)
    {
        Response.Cookies.Append(
            RefreshCookieName,
            refreshToken,
            BuildCookieOptions());
    }

    private void DeleteRefreshCookie()
    {
        Response.Cookies.Delete(
            RefreshCookieName,
            BuildCookieOptions());
    }

    private CookieOptions BuildCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth"
        };
    }
}
