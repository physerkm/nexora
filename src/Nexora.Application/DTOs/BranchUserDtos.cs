namespace Nexora.Application.DTOs;

public sealed record BranchDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string? City,
    bool IsActive,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record BranchListQuery(int? Page, int? PageSize, string? Search, bool? IsActive);

public sealed record CreateBranchRequest(string Code, string Name, string? City);

public sealed record UpdateBranchRequest(string Name, string? City, bool IsActive, uint Version);

public sealed record UserDto(
    Guid Id,
    Guid? TenantId,
    string Email,
    string FullName,
    string? Role,
    Guid? BranchId,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserListQuery(int? Page, int? PageSize, string? Search, string? Role, bool? IsActive);

public sealed record CreateUserRequest(string Email, string FullName, string Password, string Role, Guid? BranchId);

public sealed record UpdateUserRequest(string FullName, string Role, Guid? BranchId, bool IsActive);
