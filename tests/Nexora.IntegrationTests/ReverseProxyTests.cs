using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nexora.Api.Middleware;

namespace Nexora.IntegrationTests;

/// <summary>The client IP behind nginx / Caddy. Needs no database, so it does not use the shared API fixture.</summary>
public sealed class ReverseProxyTests
{
    [Theory]
    [InlineData(0, "10.0.0.5", "203.0.113.7", "10.0.0.5")]                     // no proxy configured: header ignored
    [InlineData(1, "10.0.0.5", "203.0.113.7", "203.0.113.7")]                  // nginx
    [InlineData(1, "10.0.0.5", "198.51.100.1, 203.0.113.7", "203.0.113.7")]    // a spoofed entry beyond the limit is ignored
    [InlineData(2, "10.0.0.5", "203.0.113.7, 10.0.0.9", "203.0.113.7")]        // Caddy + nginx
    [InlineData(1, "::ffff:10.0.0.5", "203.0.113.7", "203.0.113.7")]           // IPv4-mapped address (dual-stack Kestrel)
    [InlineData(1, "198.51.100.20", "203.0.113.7", "198.51.100.20")]           // sender is not a trusted proxy
    public async Task Client_ip_comes_from_trusted_proxies_only(int forwardLimit, string remoteIp, string forwardedFor, string expected)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ReverseProxy:ForwardLimit"] = forwardLimit.ToString(),
                }))
                .ConfigureServices((ctx, services) => services.AddReverseProxySupport(ctx.Configuration))
                .Configure((ctx, app) =>
                {
                    app.UseReverseProxySupport(ctx.Configuration);
                    app.Run(http => http.Response.WriteAsync(http.Connection.RemoteIpAddress?.ToString() ?? ""));
                }))
            .StartAsync();

        var response = await host.GetTestServer().SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        });

        using var reader = new StreamReader(response.Response.Body);
        Assert.Equal(expected, await reader.ReadToEndAsync());
    }
}
