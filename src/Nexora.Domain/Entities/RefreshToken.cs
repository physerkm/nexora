using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

/// <summary>
/// Refresh tokens are stored only as SHA-256 hashes. Every refresh rotates the token; all tokens descending
/// from one login share a <see cref="FamilyId"/> so that reuse of a rotated token can revoke the whole chain.
/// </summary>
public sealed class RefreshToken : Entity, IVersioned
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }
    public uint Version { get; private set; }

    public static RefreshToken Issue(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, string? ip) => new()
    {
        UserId = userId,
        FamilyId = familyId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
        CreatedByIp = ip,
    };

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(string reason, DateTimeOffset now, Guid? replacedBy = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
        ReplacedByTokenId = replacedBy;
    }
}
