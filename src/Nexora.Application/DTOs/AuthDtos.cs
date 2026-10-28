namespace Nexora.Application.DTOs;

public sealed record LoginRequest(string Email, string Password);

/// <summary>The refresh token may come in the body (API clients) or in the HttpOnly cookie (browser).</summary>
public sealed record RefreshRequest(string? RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ForgotPasswordRequest(string Email);

/// <summary>The token comes from the link in the reset email.</summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record UserProfileDto(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles,
    Guid? TenantId,
    string? TenantName,
    Guid? BranchId);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfileDto User);
