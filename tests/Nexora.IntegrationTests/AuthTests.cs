using System.Net;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.DTOs;
using Nexora.Application.Security;
using Nexora.Domain;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(NexoraApiFactory factory)
{
    private TestTenant Tenant => factory.TenantA;

    [Fact]
    public async Task Login_returns_tokens_and_profile()
    {
        var client = factory.Anonymous();
        var session = await client.LoginAsync(Tenant.ManagerEmail, NexoraApiFactory.Password);

        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.Equal(Tenant.Id, session.User.TenantId);
        Assert.Equal([Roles.Manager], session.User.Roles);
        Assert.True(session.AccessTokenExpiresAt > DateTimeOffset.UtcNow);

        var me = await client.GetJsonAsync<UserProfileDto>("/api/v1/auth/me");
        Assert.Equal(Tenant.ManagerEmail, me.Email);
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_only_as_hashes()
    {
        var session = (await factory.LoginAsync(Tenant.AuditorEmail)).Session!;
        var hash = TokenHasher.Sha256(session.RefreshToken);

        var (plainStored, hashStored) = await factory.WithDbAsync(async db => (
            await db.RefreshTokens.AnyAsync(t => t.TokenHash == session.RefreshToken),
            await db.RefreshTokens.AnyAsync(t => t.TokenHash == hash)));

        Assert.False(plainStored);
        Assert.True(hashStored);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_a_generic_error()
    {
        var response = await factory.Anonymous().PostAsync("/api/v1/auth/login", new LoginRequest(Tenant.ManagerEmail, "Wrong#Pass2026"));
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");

        var unknown = await factory.Anonymous().PostAsync("/api/v1/auth/login", new LoginRequest("nobody@nowhere.test", "Wrong#Pass2026"));
        await unknown.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var email = $"lockout-{Guid.NewGuid():N}@{Tenant.Slug}.test";
        await TestTenant.CreateUserAsync(admin, email, Roles.Auditor, null);

        var anonymous = factory.Anonymous();
        for (var i = 0; i < 5; i++)
        {
            await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(email, "Wrong#Pass2026"));
        }

        var locked = await anonymous.PostAsync("/api/v1/auth/login", new LoginRequest(email, NexoraApiFactory.Password));
        await locked.AssertProblemAsync(HttpStatusCode.Unauthorized, "account_locked");
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_the_whole_family()
    {
        var client = await factory.LoginAsync(Tenant.AuditorEmail);
        var original = client.Session!.RefreshToken;

        var first = await client.PostAsync("/api/v1/auth/refresh", new RefreshRequest(original));
        await first.EnsureStatusAsync(HttpStatusCode.OK);
        var rotated = (await first.ReadAsync<AuthResponse>()).RefreshToken;
        Assert.NotEqual(original, rotated);

        // An attacker replays the old token: rejected, and the legitimate rotated token dies with it.
        var replay = await client.PostAsync("/api/v1/auth/refresh", new RefreshRequest(original));
        await replay.AssertProblemAsync(HttpStatusCode.Unauthorized, "refresh_token_reused");

        var afterReuse = await client.PostAsync("/api/v1/auth/refresh", new RefreshRequest(rotated));
        await afterReuse.AssertProblemAsync(HttpStatusCode.Unauthorized);

        var audited = await factory.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "auth.refresh_token_reuse_detected"));
        Assert.True(audited);
    }

    [Fact]
    public async Task Garbage_refresh_token_is_rejected()
    {
        var response = await factory.Anonymous().PostAsync("/api/v1/auth/refresh", new RefreshRequest("not-a-real-token"));
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid_refresh_token");
    }

    [Fact]
    public async Task Logout_revokes_the_access_token_and_refresh_token()
    {
        var client = await factory.LoginAsync(Tenant.AuditorEmail);
        var refresh = client.Session!.RefreshToken;

        await (await client.PostAsync("/api/v1/auth/logout", new RefreshRequest(refresh))).EnsureStatusAsync(HttpStatusCode.NoContent);

        await (await client.GetAsync("/api/v1/auth/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        await (await factory.Anonymous().PostAsync("/api/v1/auth/refresh", new RefreshRequest(refresh))).EnsureStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_everywhere_invalidates_all_sessions()
    {
        var phone = await factory.LoginAsync(Tenant.Manager2Email);
        var laptop = await factory.LoginAsync(Tenant.Manager2Email);

        await (await phone.PostAsync("/api/v1/auth/logout-all")).EnsureStatusAsync(HttpStatusCode.NoContent);

        await (await laptop.GetAsync("/api/v1/auth/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        await (await factory.Anonymous().PostAsync("/api/v1/auth/refresh", new RefreshRequest(laptop.Session!.RefreshToken)))
            .EnsureStatusAsync(HttpStatusCode.Unauthorized);

        // A fresh login still works.
        await factory.LoginAsync(Tenant.Manager2Email);
    }

    [Fact]
    public async Task Deactivating_a_user_kills_their_existing_token_immediately()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var email = $"leaver-{Guid.NewGuid():N}@{Tenant.Slug}.test";
        var userId = await TestTenant.CreateUserAsync(admin, email, Roles.Operator, null);
        var leaver = await factory.LoginAsync(email);
        await (await leaver.GetAsync("/api/v1/transactions")).EnsureStatusAsync(HttpStatusCode.OK);

        var update = await admin.PutAsync($"/api/v1/users/{userId}", new UpdateUserRequest("Leaver", Roles.Operator, null, IsActive: false));
        await update.EnsureStatusAsync(HttpStatusCode.OK);

        await (await leaver.GetAsync("/api/v1/transactions")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        var login = await factory.Anonymous().PostAsync("/api/v1/auth/login", new LoginRequest(email, NexoraApiFactory.Password));
        await login.AssertProblemAsync(HttpStatusCode.Unauthorized, "account_disabled");
    }

    [Fact]
    public async Task Changing_password_requires_the_current_one_and_ends_sessions()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var email = $"pwd-{Guid.NewGuid():N}@{Tenant.Slug}.test";
        await TestTenant.CreateUserAsync(admin, email, Roles.Auditor, null);
        var user = await factory.LoginAsync(email);

        var wrong = await user.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest("Wrong#Pass2026", "Brand#New2026x"));
        await wrong.AssertProblemAsync(HttpStatusCode.BadRequest, "password_change_failed");

        var weak = await user.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest(NexoraApiFactory.Password, "weak"));
        await weak.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        await (await user.PostAsync("/api/v1/auth/change-password", new ChangePasswordRequest(NexoraApiFactory.Password, "Brand#New2026x")))
            .EnsureStatusAsync(HttpStatusCode.NoContent);

        await (await user.GetAsync("/api/v1/auth/me")).EnsureStatusAsync(HttpStatusCode.Unauthorized);
        await factory.LoginAsync(email, "Brand#New2026x");
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var client = await factory.LoginAsync(Tenant.ManagerEmail);
        client.AccessToken = client.AccessToken![..^4] + "AAAA";
        await (await client.GetAsync("/api/v1/transactions")).AssertProblemAsync(HttpStatusCode.Unauthorized);
    }
}
