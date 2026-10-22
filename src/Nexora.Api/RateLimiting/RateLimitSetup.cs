using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Nexora.Application.Security;

namespace Nexora.Api.RateLimiting;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimiting";

    /// <summary>Token bucket per authenticated user.</summary>
    public int UserTokenLimit { get; set; } = 200;
    public int UserTokensPerSecond { get; set; } = 20;

    /// <summary>Fixed window per client IP for anonymous traffic.</summary>
    public int AnonymousPermitsPerMinute { get; set; } = 120;

    /// <summary>Login / refresh attempts per client IP per minute (credential stuffing protection).</summary>
    public int AuthPermitsPerMinute { get; set; } = 10;
}

public static class RateLimitSetup
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddNexoraRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (http.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var userId = http.User.FindFirst(NexoraClaims.Subject)?.Value;
                if (userId is not null)
                {
                    return RateLimitPartition.GetTokenBucketLimiter($"user:{userId}", _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.UserTokenLimit,
                        TokensPerPeriod = options.UserTokensPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
                }

                return RateLimitPartition.GetFixedWindowLimiter($"ip:{ClientIp(http)}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AnonymousPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            limiter.AddPolicy(AuthPolicy, http => RateLimitPartition.GetFixedWindowLimiter($"auth:{ClientIp(http)}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.AuthPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too Many Requests",
                        Detail = "Rate limit exceeded. Retry later.",
                        Extensions = { ["code"] = "rate_limited" },
                    },
                });
            };
        });

        return services;
    }

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
