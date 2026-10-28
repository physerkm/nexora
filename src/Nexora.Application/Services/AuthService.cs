using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Application.Security;
using Nexora.Domain;
using Nexora.Domain.Entities;

namespace Nexora.Application.Services;

public sealed class AuthService(
    UserManager<AppUser> users,
    IApplicationDbContext db,
    ITokenService tokens,
    ITokenRevocationStore revocations,
    SessionValidator sessions,
    IOptions<JwtOptions> jwtOptions,
    IOptions<PasswordResetOptions> resetOptions,
    IEmailQueue emails,
    TimeProvider clock,
    AuditTrail audit,
    RequestContext context,
    ILogger<AuthService> logger)
{
    private static readonly UnauthorizedException InvalidCredentials = new("invalid_credentials", "Invalid email or password.");
    private static readonly TimeSpan ResetRequestInterval = TimeSpan.FromMinutes(1);
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly PasswordResetOptions _reset = resetOptions.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Spend roughly the same time as a real password check so response timing does not reveal valid emails.
            users.PasswordHasher.HashPassword(new AppUser(), request.Password);
            audit.Record("auth.login_failed", "User", request.Email.ToLowerInvariant(), new { reason = "unknown_email" }, actorEmail: request.Email);
            await db.SaveChangesAsync(ct);
            throw InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException("account_locked", "The account is temporarily locked after repeated failed sign-in attempts.");
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            audit.Record("auth.login_failed", "User", user.Id, new { reason = "bad_password" }, user.TenantId, user.Id, user.Email);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Failed sign-in for user {UserId}", user.Id);
            throw InvalidCredentials;
        }

        await EnsureCanSignInAsync(user, ct);

        await users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);

        audit.Record("auth.login", "User", user.Id, null, user.TenantId, user.Id, user.Email);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), previous: null, ct);
    }

    /// <summary>
    /// Rotates the refresh token. Presenting an already rotated/revoked token is treated as theft:
    /// the whole token family is revoked and the user has to sign in again.
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedException("invalid_refresh_token", "A refresh token is required.");
        }

        var hash = TokenHasher.Sha256(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new UnauthorizedException("invalid_refresh_token", "The refresh token is invalid.");

        var now = clock.GetUtcNow();

        if (stored.IsRevoked)
        {
            var revoked = await RevokeFamilyAsync(stored.FamilyId, "reuse_detected", ct);
            var owner = await users.FindByIdAsync(stored.UserId.ToString());
            audit.Record("auth.refresh_token_reuse_detected", "User", stored.UserId,
                new { familyId = stored.FamilyId, revokedTokens = revoked }, owner?.TenantId, stored.UserId, owner?.Email);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Refresh token reuse detected for user {UserId}, family {FamilyId} revoked", stored.UserId, stored.FamilyId);
            throw new UnauthorizedException("refresh_token_reused", "The refresh token was already used. All sessions of this login were revoked.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new UnauthorizedException("refresh_token_expired", "The refresh token has expired.");
        }

        var user = await users.FindByIdAsync(stored.UserId.ToString())
                   ?? throw new UnauthorizedException("invalid_refresh_token", "The refresh token is invalid.");

        try
        {
            await EnsureCanSignInAsync(user, ct);
        }
        catch (UnauthorizedException)
        {
            await RevokeFamilyAsync(stored.FamilyId, "sign_in_not_allowed", ct);
            await db.SaveChangesAsync(ct);
            throw;
        }

        return await IssueTokensAsync(user, stored.FamilyId, stored, ct);
    }

    public async Task LogoutAsync(string? refreshToken, string? accessTokenJti, DateTimeOffset? accessTokenExpiresAt, CancellationToken ct)
    {
        var userId = context.RequireUserId();

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = TokenHasher.Sha256(refreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);
            if (stored is not null)
            {
                await RevokeFamilyAsync(stored.FamilyId, "logout", ct);
            }
        }

        if (accessTokenJti is not null && accessTokenExpiresAt is not null)
        {
            await revocations.RevokeAsync(accessTokenJti, accessTokenExpiresAt.Value, ct);
        }

        audit.Record("auth.logout", "User", userId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Revokes every refresh token and, by rotating the security stamp, every access token of the user.</summary>
    public async Task LogoutEverywhereAsync(CancellationToken ct)
    {
        var user = await FindCurrentUserAsync();
        await RevokeAllUserSessionsAsync(user, "logout_all", ct);
        audit.Record("auth.logout_all", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await FindCurrentUserAsync();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new BadRequestException("password_change_failed", string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // ChangePasswordAsync already rotated the security stamp; also kill refresh tokens.
        await RevokeRefreshTokensAsync(user.Id, "password_changed", ct);
        await sessions.InvalidateAsync(user.Id, ct);
        audit.Record("auth.password_changed", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "Forgot password": emails a single-use reset link. The caller always gets the same answer, whether or not
    /// the address belongs to an account, so the endpoint cannot be used to find out which emails are registered.
    /// </summary>
    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var user = await users.FindByEmailAsync(request.Email);
        var skipReason = user is null ? "unknown_email" : await ResetSkipReasonAsync(user, now, ct);

        if (user is null || skipReason is not null)
        {
            audit.Record("auth.password_reset_requested", "User", user?.Id.ToString() ?? request.Email.ToLowerInvariant(),
                new { sent = false, reason = skipReason }, user?.TenantId, user?.Id, user?.Email ?? request.Email);
            await db.SaveChangesAsync(ct);
            return;
        }

        var rawToken = TokenHasher.NewOpaqueToken();
        var lifetime = TimeSpan.FromMinutes(_reset.TokenMinutes);
        db.PasswordResetTokens.Add(PasswordResetToken.Issue(user.Id, TokenHasher.Sha256(rawToken), now, lifetime, context.IpAddress));
        audit.Record("auth.password_reset_requested", "User", user.Id, new { sent = true }, user.TenantId, user.Id, user.Email);
        await db.SaveChangesAsync(ct);

        var link = $"{_reset.PublicUrl.TrimEnd('/')}/reset-password?token={rawToken}";
        emails.Enqueue(new EmailMessage(user.Email!, "Nexora parola sıfırlama", $"""
            Merhaba {user.FullName},

            Nexora hesabınız için parola sıfırlama isteği aldık. Yeni parola belirlemek için bu bağlantıyı açın:

            {link}

            Bağlantı {_reset.TokenMinutes} dakika geçerlidir ve yalnızca bir kez kullanılabilir.

            Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz. Parolanız değişmez.
            """));
    }

    /// <summary>Sets a new password from a reset link and ends every session of the user.</summary>
    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var invalidToken = new BadRequestException("invalid_reset_token", "The password reset link is invalid or has expired.");
        var now = clock.GetUtcNow();

        var hash = TokenHasher.Sha256(request.Token);
        var token = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || !token.IsUsable(now))
        {
            throw invalidToken;
        }

        var user = await users.FindByIdAsync(token.UserId.ToString()) ?? throw invalidToken;
        try
        {
            await EnsureCanSignInAsync(user, ct);
        }
        catch (UnauthorizedException)
        {
            throw invalidToken;
        }

        // The same password rules Identity applies everywhere else.
        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, request.NewPassword);
            errors.AddRange(result.Errors.Select(e => e.Description));
        }

        if (errors.Count > 0)
        {
            throw new BadRequestException("password_reset_failed", string.Join(" ", errors));
        }

        // Void this link and any other outstanding one.
        var outstanding = await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync(ct);
        outstanding.ForEach(t => t.MarkUsed(now));

        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.NewPassword);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await RevokeRefreshTokensAsync(user.Id, "password_reset", ct);
        audit.Record("auth.password_reset", "User", user.Id, null, user.TenantId, user.Id, user.Email);

        try
        {
            // Renews the security stamp (invalidating every access token) and saves all of the changes above.
            // Two requests racing with the same link: the second one fails on the user's concurrency stamp.
            var updated = await users.UpdateSecurityStampAsync(user);
            if (!updated.Succeeded)
            {
                throw invalidToken;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            throw invalidToken;
        }

        await sessions.InvalidateAsync(user.Id, ct);
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct)
    {
        var user = await FindCurrentUserAsync();
        var roles = await users.GetRolesAsync(user);
        return await ToProfileAsync(user, roles, ct);
    }

    /// <summary>Shared with UserService: used when a user is deactivated or their role changes.</summary>
    internal async Task RevokeAllUserSessionsAsync(AppUser user, string reason, CancellationToken ct)
    {
        await RevokeRefreshTokensAsync(user.Id, reason, ct);
        await users.UpdateSecurityStampAsync(user);
        await sessions.InvalidateAsync(user.Id, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(AppUser user, Guid familyId, RefreshToken? previous, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var roles = (await users.GetRolesAsync(user)).ToArray();
        var access = tokens.CreateAccessToken(user, roles);

        var rawRefreshToken = TokenHasher.NewOpaqueToken();
        var refresh = RefreshToken.Issue(user.Id, familyId, TokenHasher.Sha256(rawRefreshToken), now,
            TimeSpan.FromDays(_jwt.RefreshTokenDays), context.IpAddress);

        db.RefreshTokens.Add(refresh);
        previous?.Revoke("rotated", now, refresh.Id);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            // Two refresh calls raced with the same token; only the first one wins.
            throw new UnauthorizedException("refresh_token_reused", "The refresh token was already used.");
        }

        return new AuthResponse(access.Token, access.ExpiresAt, rawRefreshToken, refresh.ExpiresAt,
            await ToProfileAsync(user, roles, ct));
    }

    private async Task EnsureCanSignInAsync(AppUser user, CancellationToken ct)
    {
        if (!user.IsActive)
        {
            throw new UnauthorizedException("account_disabled", "The account is disabled.");
        }

        if (user.TenantId is { } tenantId)
        {
            var status = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (TenantStatus?)t.Status).FirstOrDefaultAsync(ct);
            if (status != TenantStatus.Active)
            {
                throw new UnauthorizedException("tenant_suspended", "The company account is suspended.");
            }
        }
    }

    private async Task<string?> ResetSkipReasonAsync(AppUser user, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await EnsureCanSignInAsync(user, ct);
        }
        catch (UnauthorizedException ex)
        {
            return ex.Code;
        }

        if (string.IsNullOrWhiteSpace(_reset.PublicUrl))
        {
            logger.LogError("Password reset email not sent: {Setting} is not configured", $"{PasswordResetOptions.Section}:{nameof(PasswordResetOptions.PublicUrl)}");
            return "public_url_not_configured";
        }

        // At most one email per minute per account, so the form cannot be used to flood someone's inbox.
        var since = now - ResetRequestInterval;
        return await db.PasswordResetTokens.AnyAsync(t => t.UserId == user.Id && t.CreatedAt > since, ct) ? "too_frequent" : null;
    }

    private async Task<int> RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(reason, now));
        return active.Count;
    }

    private async Task RevokeRefreshTokensAsync(Guid userId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(reason, now));
    }

    private async Task<AppUser> FindCurrentUserAsync() =>
        await users.FindByIdAsync(context.RequireUserId().ToString())
        ?? throw new UnauthorizedException("unauthenticated", "The user no longer exists.");

    private async Task<UserProfileDto> ToProfileAsync(AppUser user, IEnumerable<string> roles, CancellationToken ct)
    {
        var tenantName = user.TenantId is { } tenantId
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;

        return new UserProfileDto(user.Id, user.Email!, user.FullName, roles.ToArray(), user.TenantId, tenantName, user.BranchId);
    }
}
