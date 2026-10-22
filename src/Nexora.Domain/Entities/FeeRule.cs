using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

public sealed class FeeRule : Entity, ITenantOwned, IVersioned
{
    private FeeRule() { }

    public Guid TenantId { get; private set; }
    public TransactionType TransactionType { get; private set; }

    /// <summary>Null means the rule applies to every currency.</summary>
    public string? Currency { get; private set; }

    /// <summary>Percentage of the amount, e.g. 1.75 means 1.75 %.</summary>
    public decimal Percentage { get; private set; }

    public decimal FixedAmount { get; private set; }
    public decimal MinFee { get; private set; }
    public decimal? MaxFee { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static FeeRule Create(
        Guid tenantId, TransactionType type, string? currency, decimal percentage,
        decimal fixedAmount, decimal minFee, decimal? maxFee, DateTimeOffset now)
    {
        var rule = new FeeRule { TenantId = tenantId, TransactionType = type, Currency = currency, IsActive = true, CreatedAt = now };
        rule.SetPricing(percentage, fixedAmount, minFee, maxFee);
        return rule;
    }

    public void Update(decimal percentage, decimal fixedAmount, decimal minFee, decimal? maxFee, bool isActive, DateTimeOffset now)
    {
        SetPricing(percentage, fixedAmount, minFee, maxFee);
        IsActive = isActive;
        UpdatedAt = now;
    }

    private void SetPricing(decimal percentage, decimal fixedAmount, decimal minFee, decimal? maxFee)
    {
        if (percentage is < 0 or > 100)
        {
            throw new DomainException("invalid_fee_rule", "Percentage must be between 0 and 100.");
        }

        if (fixedAmount < 0 || minFee < 0 || maxFee < 0)
        {
            throw new DomainException("invalid_fee_rule", "Fee amounts cannot be negative.");
        }

        if (maxFee is { } max && max < minFee)
        {
            throw new DomainException("invalid_fee_rule", "Maximum fee cannot be lower than the minimum fee.");
        }

        Percentage = percentage;
        FixedAmount = fixedAmount;
        MinFee = minFee;
        MaxFee = maxFee;
    }
}
