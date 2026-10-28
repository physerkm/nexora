using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.DTOs;
using Nexora.Application.Security;
using Nexora.Domain;
using Nexora.Domain.Entities;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed partial class PasswordResetTests(NexoraApiFactory factory)
{
    private const string NewPassword = "Reset#Pass2026";

    private TestTenant Tenant => factory.TenantA;

    [Fact]
    public async Task Reset_link_sets_a_new_password_and_works_only_once()
    {
        var email = await CreateUserAsync();

        var response = await factory.Anonymous().PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        await response.EnsureStatusAsync(HttpStatusCode.Accepted);

        var message = Assert.Single(factory.Emails.To(email));
        Assert.Contains("https://app.nexora.test/reset-password?token=", message.Body);
        var token = TokenFrom(message.Body);

        var reset = await factory.Anonymous().PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        await reset.EnsureStatusAsync(HttpStatusCode.NoContent);

        var oldPassword = await factory.Anonymous().PostAsync("/api/v1/auth/login", new LoginRequest(email, NexoraApiFactory.Password));
        await oldPassword.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials");
        await factory.LoginAsync(email, NewPassword);

        var reused = await factory.Anonymous().PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "Another#Pass2026"));
        await reused.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid_reset_token");
    }

    [Fact]
    public async Task Unknown_email_gets_the_same_answer_and_no_email()
    {
        var email = $"nobody-{Guid.NewGuid():N}@nowhere.test";

        var response = await factory.Anonymous().PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));

        await response.EnsureStatusAsync(HttpStatusCode.Accepted);
        Assert.Empty(factory.Emails.To(email));
    }

    [Fact]
    public async Task Only_one_email_per_minute_is_sent()
    {
        var email = await CreateUserAsync();

        for (var i = 0; i < 3; i++)
        {
            var response = await factory.Anonymous().PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
            await response.EnsureStatusAsync(HttpStatusCode.Accepted);
        }

        Assert.Single(factory.Emails.To(email));
    }

    [Fact]
    public async Task Reset_ends_existing_sessions()
    {
        var email = await CreateUserAsync();
        var signedIn = await factory.LoginAsync(email);

        await factory.Anonymous().PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        var token = TokenFrom(Assert.Single(factory.Emails.To(email)).Body);
        var reset = await factory.Anonymous().PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        await reset.EnsureStatusAsync(HttpStatusCode.NoContent);

        var me = await signedIn.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var refresh = await factory.Anonymous().PostAsync("/api/v1/auth/refresh", new RefreshRequest(signedIn.Session!.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Expired_link_is_rejected()
    {
        var email = await CreateUserAsync();
        var rawToken = TokenHasher.NewOpaqueToken();
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            db.PasswordResetTokens.Add(PasswordResetToken.Issue(
                user.Id, TokenHasher.Sha256(rawToken), DateTimeOffset.UtcNow.AddHours(-2), TimeSpan.FromHours(1), null));
            return await db.SaveChangesAsync();
        });

        var response = await factory.Anonymous().PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(rawToken, NewPassword));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid_reset_token");
    }

    [Fact]
    public async Task Weak_password_is_rejected_and_the_link_stays_usable()
    {
        var email = await CreateUserAsync();
        await factory.Anonymous().PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        var token = TokenFrom(Assert.Single(factory.Emails.To(email)).Body);

        var weak = await factory.Anonymous().PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "short"));
        await weak.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        var strong = await factory.Anonymous().PostAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        await strong.EnsureStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reset_tokens_are_stored_only_as_hashes()
    {
        var email = await CreateUserAsync();
        await factory.Anonymous().PostAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email));
        var token = TokenFrom(Assert.Single(factory.Emails.To(email)).Body);
        var hash = TokenHasher.Sha256(token);

        var (plainStored, hashStored) = await factory.WithDbAsync(async db => (
            await db.PasswordResetTokens.AnyAsync(t => t.TokenHash == token),
            await db.PasswordResetTokens.AnyAsync(t => t.TokenHash == hash)));

        Assert.False(plainStored);
        Assert.True(hashStored);
    }

    private async Task<string> CreateUserAsync()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var email = $"reset-{Guid.NewGuid():N}@{Tenant.Slug}.test";
        await TestTenant.CreateUserAsync(admin, email, Roles.Auditor, null);
        return email;
    }

    private static string TokenFrom(string body) => ResetToken().Match(body).Groups[1].Value;

    [GeneratedRegex("token=([A-Za-z0-9_-]+)")]
    private static partial Regex ResetToken();
}
