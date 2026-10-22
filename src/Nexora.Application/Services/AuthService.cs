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
    TimeProvider clock,
    AuditTrail audit,
    RequestContext context,
    ILogger<AuthService> logger)
{
    private static readonly UnauthorizedException InvalidCredentials = new("invalid_credentials", "Invalid email or password.");
    private readonly JwtOptions _jwt = jwtOptions.Value;

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
