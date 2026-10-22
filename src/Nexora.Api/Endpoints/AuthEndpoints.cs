using Nexora.Api.Filters;
using Nexora.Api.RateLimiting;
using Nexora.Application.DTOs;
using Nexora.Application.Security;
using Nexora.Application.Services;

namespace Nexora.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshCookieName = "nexora_refresh";
    private const string RefreshCookiePath = "/api/v1/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var response = await auth.LoginAsync(request, ct);
                SetRefreshCookie(http, response);
                return TypedResults.Ok(response);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitSetup.AuthPolicy)
            .WithValidation<LoginRequest>()
            .WithSummary("Sign in with email and password")
            .WithDescription("Returns a short-lived access token and a rotating refresh token (also set as an HttpOnly cookie).")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (RefreshRequest? request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var token = request?.RefreshToken ?? http.Request.Cookies[RefreshCookieName];
                try
                {
                    var response = await auth.RefreshAsync(token, ct);
                    SetRefreshCookie(http, response);
                    return TypedResults.Ok(response);
                }
                catch
                {
                    ClearRefreshCookie(http);
                    throw;
                }
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitSetup.AuthPolicy)
            .WithSummary("Rotate the refresh token")
            .WithDescription("Each refresh token is single-use. Re-using a rotated token revokes the whole token family.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (RefreshRequest? request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                var expires = long.TryParse(http.User.FindFirst("exp")?.Value, out var exp) ? DateTimeOffset.FromUnixTimeSeconds(exp) : (DateTimeOffset?)null;
                await auth.LogoutAsync(
                    request?.RefreshToken ?? http.Request.Cookies[RefreshCookieName],
                    http.User.FindFirst(NexoraClaims.Jti)?.Value,
                    expires,
                    ct);
                ClearRefreshCookie(http);
                return TypedResults.NoContent();
            })
            .WithSummary("Sign out: revokes the refresh token family and the current access token");

        group.MapPost("/logout-all", async (AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                await auth.LogoutEverywhereAsync(ct);
                ClearRefreshCookie(http);
                return TypedResults.NoContent();
            })
            .WithSummary("Revoke every session of the current user on every device");

        group.MapPost("/change-password", async (ChangePasswordRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                await auth.ChangePasswordAsync(request, ct);
                ClearRefreshCookie(http);
                return TypedResults.NoContent();
            })
            .WithValidation<ChangePasswordRequest>()
            .WithSummary("Change password (ends all sessions)");

        group.MapGet("/me", async (AuthService auth, CancellationToken ct) => TypedResults.Ok(await auth.GetProfileAsync(ct)))
            .WithSummary("Current user profile");

        return app;
    }

    private static void SetRefreshCookie(HttpContext http, AuthResponse response) =>
        http.Response.Cookies.Append(RefreshCookieName, response.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = response.RefreshTokenExpiresAt,
            IsEssential = true,
        });

    private static void ClearRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath, Secure = true, SameSite = SameSiteMode.Strict });
}
