using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nexora.Api.Endpoints;
using Nexora.Api.Middleware;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

/// <summary>The public demo against the real API and database: visitors can look around but not change anything.</summary>
[Collection(ApiCollection.Name)]
public sealed class DemoModeTests(NexoraApiFactory factory)
{
    private const string WrongPassword = "Wrong#Pass2026";

    private TestTenant Tenant => factory.TenantA;

    [Fact]
    public async Task A_demo_account_can_look_but_not_change_anything()
    {
        await using var demo = DemoApi(owners: "owner@example.com");
        var manager = await LoginAsync(demo, Tenant.ManagerEmail);

        Assert.True(manager.Session!.User.ReadOnly);
        await (await manager.GetAsync("/api/v1/transactions")).EnsureStatusAsync(HttpStatusCode.OK);

        var change = await manager.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest(NexoraApiFactory.Password, "Taken#Over2026"));
        await change.AssertProblemAsync(HttpStatusCode.Forbidden, DemoModeMiddleware.ErrorCode);

        // The password is unchanged, so the next visitor can still sign in.
        await LoginAsync(demo, Tenant.ManagerEmail);
    }

    [Fact]
    public async Task An_owner_account_keeps_full_access()
    {
        await using var demo = DemoApi(owners: Tenant.AdminEmail);
        var admin = await LoginAsync(demo, Tenant.AdminEmail);

        Assert.False(admin.Session!.User.ReadOnly);
        // An invalid branch: the request got past demo mode and was judged by the endpoint itself (nothing is created).
        var response = await admin.PostAsync("/api/v1/branches", new CreateBranchRequest("", "", null));
        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Failed_sign_ins_never_lock_a_demo_account()
    {
        var email = await CreateUserAsync("demo-lock");
        await using var demo = DemoApi();
        var anonymous = Client(demo);

        for (var i = 0; i < 6; i++)
        {
            var failed = await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(email, WrongPassword));
            await failed.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");
        }

        await LoginAsync(demo, email);
    }

    [Fact]
    public async Task Demo_accounts_get_no_password_reset_email()
    {
        var email = await CreateUserAsync("demo-reset");
        await using var demo = DemoApi();

        var response = await Client(demo).PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));

        await response.EnsureStatusAsync(HttpStatusCode.Accepted);
        Assert.Empty(factory.Emails.To(email));
    }

    [Fact]
    public async Task The_sign_in_page_can_tell_whether_demo_mode_is_on()
    {
        await using var demo = DemoApi();

        Assert.True((await Client(demo).GetJsonAsync<DemoStatus>("/api/v1/demo")).Enabled);
        Assert.False((await factory.Anonymous().GetJsonAsync<DemoStatus>("/api/v1/demo")).Enabled);
    }

    /// <summary>The same API and database with demo mode switched on.</summary>
    private WebApplicationFactory<Program> DemoApi(string owners = "") =>
        factory.WithWebHostBuilder(web => web.UseSetting("Demo:Enabled", "true").UseSetting("Demo:OwnerEmails", owners));

    private static ApiClient Client(WebApplicationFactory<Program> api) =>
        new(api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));

    private static async Task<ApiClient> LoginAsync(WebApplicationFactory<Program> api, string email)
    {
        var client = Client(api);
        await client.LoginAsync(email, NexoraApiFactory.Password);
        return client;
    }

    private async Task<string> CreateUserAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@{Tenant.Slug}.test";
        await TestTenant.CreateUserAsync(await factory.LoginAsync(Tenant.AdminEmail), email, Roles.Auditor, null);
        return email;
    }
}
