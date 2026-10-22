using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace Nexora.Api.Middleware;

/// <summary>
/// Accepts a caller supplied <c>X-Correlation-ID</c> (or creates one), echoes it on the response, and attaches it
/// to every log event, the current trace activity, ProblemDetails bodies and audit records.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = !string.IsNullOrEmpty(incoming) && SafeId().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeId();
}
