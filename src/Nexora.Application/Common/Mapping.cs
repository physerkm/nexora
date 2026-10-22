using System.Text.Json;
using Nexora.Application.DTOs;
using Nexora.Domain.Entities;
using Nexora.Domain.Rules;

namespace Nexora.Application.Common;

public static class Mapping
{
    public static BranchDto ToDto(this Branch b) =>
        new(b.Id, b.TenantId, b.Code, b.Name, b.City, b.IsActive, b.CreatedAt, b.Version);

    public static FeeRuleDto ToDto(this FeeRule r) =>
        new(r.Id, r.TenantId, r.TransactionType, r.Currency, r.Percentage, r.FixedAmount, r.MinFee, r.MaxFee, r.IsActive, r.CreatedAt, r.Version);

    public static TenantSettingsDto ToSettingsDto(this Tenant t) =>
        new(t.Id, t.Name, t.Slug, t.Status, t.WebhookUrl, t.WebhookSecret, t.Version);

    public static TransactionDto ToDto(this Transaction t, string branchName) =>
        new(t.Id, t.TenantId, t.BranchId, branchName, t.Reference, t.Type, t.Amount, t.Currency, t.Fee, t.NetAmount,
            t.Status, t.Description, t.CounterpartyName, t.StatusReason, t.CreatedByUserId, t.CreatedAt,
            t.ReviewedByUserId, t.ReviewedAt, t.CancelledByUserId, t.CancelledAt, t.Version,
            TransactionStateMachine.NextStates(t.Status));

    public static AuditLogDto ToDto(this AuditLog a) =>
        new(a.Id, a.TenantId, a.UserId, a.UserEmail, a.Action, a.EntityType, a.EntityId,
            a.Data is null ? null : JsonDocument.Parse(a.Data).RootElement.Clone(),
            a.CorrelationId, a.IpAddress, a.CreatedAt);
}
