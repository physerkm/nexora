namespace Nexora.Application.Common;

public static class CacheKeys
{
    public static string TransactionsTag(Guid? tenantId) => $"tenant:{tenantId?.ToString() ?? "all"}:transactions";
    public const string AllTransactionsTag = "tenant:all:transactions";
    public static string TenantTag(Guid tenantId) => $"tenant:{tenantId}";
    public static string TenantStatus(Guid tenantId) => $"tenant-status:{tenantId}";
    public static string UserTag(Guid userId) => $"user:{userId}";
    public static string UserSession(Guid userId) => $"user-session:{userId}";
}
