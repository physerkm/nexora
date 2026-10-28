using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

/// <summary>
/// A single-use "forgot password" token. Only its SHA-256 hash is stored; the raw token is sent by email.
/// </summary>
public sealed class PasswordResetToken : Entity
{
    private PasswordResetToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public string? CreatedByIp { get; private set; }

    public static PasswordResetToken Issue(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, string? ip) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
        CreatedByIp = ip,
    };

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    /// <summary>Marks the token as consumed (also used to void older tokens when a password changes).</summary>
    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
}
