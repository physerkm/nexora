namespace Nexora.Domain;

public enum TenantStatus
{
    Active,
    Suspended
}

public enum TransactionType
{
    Payment,
    Refund,
    Transfer,
    Payout
}

public enum TransactionStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Expired
}

public enum OutboxStatus
{
    Pending,
    Processed,
    Failed
}

public enum IdempotencyStatus
{
    InProgress,
    Completed
}

public static class Roles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Manager = "Manager";
    public const string Operator = "Operator";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlyList<string> All = [PlatformAdmin, TenantAdmin, Manager, Operator, Auditor];

    /// <summary>Roles that a tenant admin may assign inside their own tenant.</summary>
    public static readonly IReadOnlyList<string> TenantRoles = [TenantAdmin, Manager, Operator, Auditor];
}

public static class SupportedCurrencies
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { "TRY", "USD", "EUR", "GBP" };
}
