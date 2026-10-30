using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Nexora.Application;

namespace Nexora.Api.Endpoints;

public static class HealthEndpoints
{
    /// <summary>
    /// /health/live: the process is up (no dependencies, used for restarts).
    /// /health/ready: database and cache reachable (used to route traffic).
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription().AllowAnonymous();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync })
            .AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteAsync })
            .AllowAnonymous();

        // Lets the sign-in page offer the demo accounts only where they are meant to be used.
        app.MapGet("/api/v1/demo", (IOptions<DemoOptions> demo) => TypedResults.Ok(new DemoStatus(demo.Value.Enabled)))
            .WithTags("Auth")
            .AllowAnonymous();

        return app;
    }

    private static Task WriteAsync(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(http.Response.Body, new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(e => e.Key, e => new
            {
                status = e.Value.Status.ToString(),
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                description = e.Value.Description,
            }),
        });
    }
}

public sealed record DemoStatus(bool Enabled);
