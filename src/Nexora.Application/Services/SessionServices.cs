using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Domain;

namespace Nexora.Application.Services;

public sealed record UserSessionState(string? SecurityStamp, bool IsActive);

/// <summary>
/// Called for every authenticated request (JwtBearer OnTokenValidated). Makes stateless JWTs revocable:
/// a token is rejected if its jti was logged out, or if the user's security stamp changed
/// (password change, "log out everywhere", deactivation). The lookup is cached for a few seconds.
/// </summary>
public sealed class SessionValidator(IServiceScopeFactory scopes, HybridCache cache, ITokenRevocationStore revocations)
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(15),
    };

    public async Task<bool> IsValidAsync(Guid userId, string? securityStamp, string? jti, CancellationToken ct)
    {
        if (jti is not null && await revocations.IsRevokedAsync(jti, ct))
        {
            return false;
        }

        var state = await cache.GetOrCreateAsync(
            CacheKeys.UserSession(userId),
            async token => await scopes.RunAsync(null, sp => sp.GetRequiredService<IApplicationDbContext>().Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new UserSessionState(u.SecurityStamp, u.IsActive))
                .FirstOrDefaultAsync(token)),
            CacheOptions,
            [CacheKeys.UserTag(userId)],
            ct);

        return state is { IsActive: true } && string.Equals(state.SecurityStamp, securityStamp, StringComparison.Ordinal);
    }

    public ValueTask InvalidateAsync(Guid userId, CancellationToken ct = default) =>
        cache.RemoveByTagAsync(CacheKeys.UserTag(userId), ct);
}

public sealed class TenantStatusProvider(IServiceScopeFactory scopes, HybridCache cache)
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    public async Task<TenantStatus?> GetStatusAsync(Guid tenantId, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.TenantStatus(tenantId),
            async token => await scopes.RunAsync(null, sp => sp.GetRequiredService<IApplicationDbContext>().Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => (TenantStatus?)t.Status)
                .FirstOrDefaultAsync(token)),
            CacheOptions,
            [CacheKeys.TenantTag(tenantId)],
            ct);
    }

    public ValueTask InvalidateAsync(Guid tenantId, CancellationToken ct = default) =>
        cache.RemoveByTagAsync(CacheKeys.TenantTag(tenantId), ct);
}
