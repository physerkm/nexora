using Microsoft.AspNetCore.Identity;

namespace Nexora.Domain.Entities;

/// <summary>
/// Identity user. <see cref="TenantId"/> is null only for platform administrators.
/// Password hashing (PBKDF2), lockout and security stamps come from ASP.NET Core Identity.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public Guid? TenantId { get; set; }

    /// <summary>When set, the user may only operate on transactions of this branch.</summary>
    public Guid? BranchId { get; set; }

    public string FullName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
