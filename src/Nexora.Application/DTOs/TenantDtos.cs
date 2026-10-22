using Nexora.Domain;

namespace Nexora.Application.DTOs;

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    string? WebhookUrl,
    DateTimeOffset CreatedAt,
    uint Version,
    int UserCount,
    int BranchCount);

public sealed record TenantListQuery(int? Page, int? PageSize, string? Search, TenantStatus? Status);

public sealed record CreateTenantRequest(
    string Name,
    string Slug,
    string AdminEmail,
    string AdminFullName,
    string AdminPassword);

public sealed record ChangeTenantStatusRequest(TenantStatus Status, uint Version);

public sealed record UpdateTenantSettingsRequest(string Name, string? WebhookUrl, bool RotateWebhookSecret, uint Version);

/// <summary>Returned to tenant admins only. The webhook secret is shown so receivers can verify HMAC signatures.</summary>
public sealed record TenantSettingsDto(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    string? WebhookUrl,
    string? WebhookSecret,
    uint Version);
