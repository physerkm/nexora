using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.Domain.Entities;
using Nexora.Domain.Rules;

namespace Nexora.Application.Services;

public sealed class FeeRuleService(IApplicationDbContext db, AuditTrail audit, RequestContext context, TimeProvider clock)
{
    public async Task<IReadOnlyList<FeeRuleDto>> ListAsync(CancellationToken ct)
    {
        var rules = await db.FeeRules.AsNoTracking()
            .OrderBy(r => r.TransactionType).ThenBy(r => r.Currency)
            .ToListAsync(ct);
        return rules.Select(r => r.ToDto()).ToList();
    }

    public async Task<FeeRuleDto> CreateAsync(CreateFeeRuleRequest request, CancellationToken ct)
    {
        var tenantId = context.RequireTenantId();
        var rule = FeeRule.Create(tenantId, request.TransactionType, request.Currency, request.Percentage,
            request.FixedAmount, request.MinFee, request.MaxFee, clock.GetUtcNow());

        db.FeeRules.Add(rule);
        audit.Record("fee_rule.created", "FeeRule", rule.Id, request);
        await SaveAsync(ct);
        return rule.ToDto();
    }

    public async Task<FeeRuleDto> UpdateAsync(Guid id, UpdateFeeRuleRequest request, CancellationToken ct)
    {
        var rule = await db.FeeRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("FeeRule", id);
        db.UseExpectedVersion(rule, request.Version);

        var before = rule.ToDto();
        rule.Update(request.Percentage, request.FixedAmount, request.MinFee, request.MaxFee, request.IsActive, clock.GetUtcNow());
        audit.Record("fee_rule.updated", "FeeRule", rule.Id, new { before, after = request }, rule.TenantId);
        await SaveAsync(ct);
        return rule.ToDto();
    }

    public async Task<FeeQuoteDto> QuoteAsync(FeeQuoteRequest request, CancellationToken ct)
    {
        context.RequireTenantId();
        var rule = await FindRuleAsync(request.Type, request.Currency, ct);
        var fee = FeeCalculator.Calculate(request.Amount, rule);
        return new FeeQuoteDto(request.Amount, fee, request.Amount - fee, request.Currency, rule?.Id);
    }

    internal async Task<FeeRule?> FindRuleAsync(TransactionType type, string currency, CancellationToken ct)
    {
        var candidates = await db.FeeRules.AsNoTracking()
            .Where(r => r.IsActive && r.TransactionType == type && (r.Currency == null || r.Currency == currency))
            .ToListAsync(ct);
        return FeeCalculator.SelectRule(candidates, type, currency);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintException)
        {
            throw new ConflictException("fee_rule_exists", "An active fee rule already exists for this transaction type and currency.");
        }
    }
}
