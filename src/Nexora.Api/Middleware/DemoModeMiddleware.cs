using Microsoft.Extensions.Options;
using Nexora.Application;
using Nexora.Application.Security;

namespace Nexora.Api.Middleware;

/// <summary>
/// Public demo: refuses every change a demo account asks for, whatever the client sends. Reads stay open, and so do
/// signing in and out, password recovery and the fee calculator. Owner accounts (Demo:OwnerEmails) are not affected,
/// and anonymous writes pass through so the endpoint's own authorization answers them (401).
/// </summary>
public sealed class DemoModeMiddleware(RequestDelegate next, IOptions<DemoOptions> options)
{
    public const string ErrorCode = "demo_read_only";

    private static readonly PathString[] AlwaysAllowed =
    [
        "/api/v1/auth/login",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password",
        "/api/v1/fees/quote",
    ];

    public async Task InvokeAsync(HttpContext http)
    {
        if (IsRefused(http, options.Value))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = http,
                ProblemDetails =
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Forbidden",
                    Detail = "This is a read-only demo: changes are disabled.",
                    Extensions = { ["code"] = ErrorCode },
                },
            });
            return;
        }

        await next(http);
    }

    private static bool IsRefused(HttpContext http, DemoOptions demo)
    {
        var method = http.Request.Method;
        if (!demo.Enabled || HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return false;
        }

        if (AlwaysAllowed.Any(path => http.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return http.User.Identity?.IsAuthenticated == true && demo.IsReadOnly(http.User.FindFirst(NexoraClaims.Email)?.Value);
    }
}
