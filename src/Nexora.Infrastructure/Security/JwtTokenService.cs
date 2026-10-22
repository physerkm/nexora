using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Entities;

namespace Nexora.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken CreateAccessToken(AppUser user, IReadOnlyCollection<string> roles)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new Dictionary<string, object>
        {
            [NexoraClaims.Subject] = user.Id.ToString(),
            [NexoraClaims.Email] = user.Email!,
            [NexoraClaims.Name] = user.FullName,
            [NexoraClaims.Jti] = jti,
            [NexoraClaims.SecurityStamp] = user.SecurityStamp!,
            [NexoraClaims.Role] = roles.ToArray(),
        };

        if (user.TenantId is { } tenantId)
        {
            claims[NexoraClaims.TenantId] = tenantId.ToString();
        }

        if (user.BranchId is { } branchId)
        {
            claims[NexoraClaims.BranchId] = branchId.ToString();
        }

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(jwt.SigningKey), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, jti, expires);
    }

    public static SymmetricSecurityKey CreateSigningKey(string key) => new(Encoding.UTF8.GetBytes(key));
}

/// <summary>Access-token denylist kept in the distributed cache (Redis when configured), entries expire with the token.</summary>
internal sealed class DistributedTokenRevocationStore(IDistributedCache cache) : ITokenRevocationStore
{
    private static string Key(string jti) => $"revoked-jti:{jti}";

    public Task RevokeAsync(string jti, DateTimeOffset expiresAt, CancellationToken ct) =>
        cache.SetStringAsync(Key(jti), "1", new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt }, ct);

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct) =>
        await cache.GetStringAsync(Key(jti), ct) is not null;
}
