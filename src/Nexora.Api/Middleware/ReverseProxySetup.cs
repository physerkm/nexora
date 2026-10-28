using Microsoft.AspNetCore.HttpOverrides;

namespace Nexora.Api.Middleware;

public sealed class ReverseProxyOptions
{
    public const string Section = "ReverseProxy";

    /// <summary>
    /// Number of proxies in front of the API: 0 = none (direct), 1 = nginx (docker-compose.yml),
    /// 2 = Caddy + nginx (docker-compose.prod.yml).
    /// </summary>
    public int ForwardLimit { get; set; }

    /// <summary>Only proxies in these networks are trusted to report the client IP. Loopback is always trusted.</summary>
    public string[] TrustedNetworks { get; set; } = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
}

/// <summary>
/// Behind a reverse proxy every request comes from the proxy's address, so rate limits and audit logs
/// would treat all users as one client. This restores the real client IP from <c>X-Forwarded-For</c>.
/// </summary>
public static class ReverseProxySetup
{
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ReverseProxyOptions.Section).Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            o.ForwardLimit = options.ForwardLimit;
            foreach (var network in options.TrustedNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }

    public static IApplicationBuilder UseReverseProxySupport(this IApplicationBuilder app, IConfiguration configuration) =>
        configuration.GetValue<int>($"{ReverseProxyOptions.Section}:{nameof(ReverseProxyOptions.ForwardLimit)}") > 0
            ? app.UseForwardedHeaders()
            : app;
}
