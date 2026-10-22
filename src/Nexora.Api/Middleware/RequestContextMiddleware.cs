using Nexora.Application.Common;
using Nexora.Application.Security;
using Nexora.Application.Services;
using Serilog;
using Serilog.Context;

namespace Nexora.Api.Middleware;

/// <summary>
/// Turns the validated JWT into the scoped <see cref="RequestContext"/> that drives tenant query filters.
/// The tenant always comes from the signed token; only platform admins may pick a tenant with <c>X-Tenant-Id</c>.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public const string TenantHeader = "X-Tenant-Id";

    public async Task InvokeAsync(HttpContext http, RequestContext context, TenantStatusProvider tenants, IDiagnosticContext diagnostics)
    {
        context.SetRequest(CorrelationIdMiddleware.Get(http), http.Connection.RemoteIpAddress?.ToString());

        var principal = http.User;
        if (principal.Identity?.IsAuthenticated == true && Guid.TryParse(principal.FindFirst(NexoraClaims.Subject)?.Value, out var userId))
        {
            context.SetUser(
                userId,
                principal.FindFirst(NexoraClaims.Email)?.Value,
                ParseGuid(principal.FindFirst(NexoraClaims.TenantId)?.Value),
                ParseGuid(principal.FindFirst(NexoraClaims.BranchId)?.Value),
                principal.FindAll(NexoraClaims.Role).Select(c => c.Value));

            var requested = http.Request.Headers[TenantHeader].ToString();

            if (context.IsPlatformAdmin)
            {
                if (string.IsNullOrEmpty(requested))
                {
                    context.SelectTenant(null);
                }
                else if (Guid.TryParse(requested, out var selected) && await tenants.GetStatusAsync(selected, http.RequestAborted) is not null)
                {
                    context.SelectTenant(selected);
                }
                else
                {
                    await WriteProblemAsync(http, StatusCodes.Status400BadRequest, "unknown_tenant", $"The {TenantHeader} header does not reference an existing tenant.");
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(requested) && !string.Equals(requested, context.TenantId?.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                await WriteProblemAsync(http, StatusCodes.Status403Forbidden, "cross_tenant_access", "You cannot access another tenant's data.");
                return;
            }

            diagnostics.Set("UserId", context.UserId);
            diagnostics.Set("TenantId", context.TenantId);
        }

        using (LogContext.PushProperty("UserId", context.UserId))
        using (LogContext.PushProperty("TenantId", context.TenantId))
        {
            await next(http);
        }
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;

    private static async Task WriteProblemAsync(HttpContext http, int status, string code, string detail)
    {
        http.Response.StatusCode = status;
        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = { Status = status, Title = ReasonPhrase(status), Detail = detail, Extensions = { ["code"] = code } },
        });
    }

    private static string ReasonPhrase(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
}
