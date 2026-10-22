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
