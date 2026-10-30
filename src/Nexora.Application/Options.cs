using System.ComponentModel.DataAnnotations;

namespace Nexora.Application;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = "nexora";
    [Required] public string Audience { get; set; } = "nexora-api";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply via environment/secret store in production.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = null!;

    [Range(1, 120)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 7;
}

public sealed class PasswordResetOptions
{
    public const string Section = "PasswordReset";

    /// <summary>
    /// Public address of the web app, used to build the link in the email (e.g. https://app.example.com).
    /// When empty, reset emails are not sent.
    /// </summary>
    public string PublicUrl { get; set; } = "";

    [Range(5, 24 * 60)] public int TokenMinutes { get; set; } = 60;
}

public sealed class TransactionOptions
{
    public const string Section = "Transactions";

    /// <summary>Pending transactions older than this are expired by the background job.</summary>
    [Range(1, 24 * 30)] public int PendingExpiryHours { get; set; } = 72;
}

public sealed class BackgroundJobOptions
{
    public const string Section = "BackgroundJobs";

    public bool Enabled { get; set; } = true;
    [Range(1, 3600)] public int OutboxIntervalSeconds { get; set; } = 5;
    [Range(1, 1440)] public int MaintenanceIntervalMinutes { get; set; } = 5;
    [Range(1, 500)] public int OutboxBatchSize { get; set; } = 25;
}

/// <summary>
/// Public demo. When enabled, every account except <see cref="OwnerEmails"/> can look at everything but change
/// nothing: the API refuses their writes (DemoModeMiddleware), and their failed sign-ins never lock the account.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Comma-separated emails that keep full access in demo mode (the operator's own accounts).</summary>
    public string OwnerEmails { get; set; } = "";

    public bool IsReadOnly(string? email) =>
        Enabled && !OwnerEmails
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(email ?? "", StringComparer.OrdinalIgnoreCase);
}
