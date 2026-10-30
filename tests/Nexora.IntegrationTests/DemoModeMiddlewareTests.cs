using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexora.Api.Middleware;
using Nexora.Application;
using Nexora.Application.Security;

namespace Nexora.IntegrationTests;

/// <summary>Which requests the demo mode lets through. Needs no database, so it does not use the shared API fixture.</summary>
public sealed class DemoModeMiddlewareTests
{
    private const string Owner = "owner@example.com";
    private const string Visitor = "manager@acme.test";

    [Theory]
    [InlineData("POST", "/api/v1/branches")]
    [InlineData("PUT", "/api/v1/users/0198a0b4-0000-7000-8000-000000000001")]
    [InlineData("PATCH", "/api/v1/platform/tenants/0198a0b4-0000-7000-8000-000000000001/status")]
    [InlineData("DELETE", "/api/v1/anything")]
    [InlineData("POST", "/api/v1/transactions/0198a0b4-0000-7000-8000-000000000001/approve")]
    [InlineData("POST", "/api/v1/auth/change-password")]
    [InlineData("POST", "/api/v1/auth/logout-all")] // would sign every other visitor out of the shared account
    public async Task Refuses_changes_by_a_demo_account(string method, string path)
    {
        var response = await SendAsync(new DemoOptions { Enabled = true, OwnerEmails = Owner }, method, path, Visitor);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Contains(DemoModeMiddleware.ErrorCode, response.Body);
    }

    [Theory]
    [InlineData("GET", "/api/v1/transactions")]
    [InlineData("HEAD", "/api/v1/transactions")]
    [InlineData("POST", "/api/v1/auth/login")]
    [InlineData("POST", "/api/v1/auth/refresh")]
    [InlineData("POST", "/api/v1/auth/logout")]
    [InlineData("POST", "/api/v1/auth/forgot-password")]
    [InlineData("POST", "/api/v1/auth/reset-password")]
    [InlineData("POST", "/api/v1/fees/quote")]
    public async Task Lets_a_demo_account_read_and_manage_its_own_session(string method, string path)
    {
        var response = await SendAsync(new DemoOptions { Enabled = true, OwnerEmails = Owner }, method, path, Visitor);

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    [Theory]
    [InlineData(" OWNER@example.com ")] // case and spaces in the setting do not matter
    [InlineData("first@example.com, OWNER@example.com")]
    public async Task Owner_accounts_keep_full_access(string ownerEmails)
    {
        var response = await SendAsync(new DemoOptions { Enabled = true, OwnerEmails = ownerEmails }, "POST", "/api/v1/branches", Owner);

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task Anonymous_writes_reach_the_endpoint_which_asks_for_a_sign_in()
    {
        var response = await SendAsync(new DemoOptions { Enabled = true }, "POST", "/api/v1/branches", email: null);

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task Does_nothing_when_demo_mode_is_off()
    {
        var response = await SendAsync(new DemoOptions { Enabled = false }, "POST", "/api/v1/branches", Visitor);

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    /// <summary>Runs the middleware in front of an endpoint that always answers 200, as the given user (or anonymously).</summary>
    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(DemoOptions demo, string method, string path, string? email)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddProblemDetails();
                    services.AddSingleton(Microsoft.Extensions.Options.Options.Create(demo));
                })
                .Configure(app =>
                {
                    app.Use((http, next) =>
                    {
                        if (email is not null)
                        {
                            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(NexoraClaims.Email, email)], "Test"));
                        }

                        return next(http);
                    });
                    app.UseMiddleware<DemoModeMiddleware>();
                    app.Run(http => http.Response.WriteAsync("reached"));
                }))
            .StartAsync();

        using var client = host.GetTestServer().CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
