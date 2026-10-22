using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

public sealed class Branch : Entity, ITenantOwned, IVersioned
{
    private Branch() { }

    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? City { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Branch Create(Guid tenantId, string code, string name, string? city, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        Code = code.Trim().ToUpperInvariant(),
        Name = name.Trim(),
        City = city?.Trim(),
        IsActive = true,
        CreatedAt = now,
    };

    public void Update(string name, string? city, bool isActive, DateTimeOffset now)
    {
        Name = name.Trim();
        City = city?.Trim();
        IsActive = isActive;
        UpdatedAt = now;
    }
}
