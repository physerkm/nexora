using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

public sealed class Tenant : Entity, IVersioned
{
    private Tenant() { }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public TenantStatus Status { get; private set; }
    public string? WebhookUrl { get; private set; }
    public string? WebhookSecret { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Tenant Create(string name, string slug, DateTimeOffset now) => new()
    {
        Name = name.Trim(),
        Slug = slug.Trim().ToLowerInvariant(),
        Status = TenantStatus.Active,
        CreatedAt = now,
    };

    public void Rename(string name, DateTimeOffset now)
    {
        Name = name.Trim();
        UpdatedAt = now;
    }

    public void ChangeStatus(TenantStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            throw new InvalidStateTransitionException("Tenant", Status.ToString(), status.ToString());
        }

        Status = status;
        UpdatedAt = now;
    }

    public void ConfigureWebhook(string? url, string? secret, DateTimeOffset now)
    {
        WebhookUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        WebhookSecret = WebhookUrl is null ? null : secret;
        UpdatedAt = now;
    }
}
