using Nexora.Domain.Entities;

namespace Nexora.Domain.Rules;

public static class FeeCalculator
{
    /// <summary>
    /// fee = amount * percentage / 100 + fixed, clamped to [min, max] and to the amount itself,
    /// rounded to 2 decimals (away from zero, the usual commercial rounding).
    /// </summary>
    public static decimal Calculate(decimal amount, FeeRule? rule)
    {
        if (rule is null || amount <= 0)
        {
            return 0m;
        }

        var fee = amount * rule.Percentage / 100m + rule.FixedAmount;

        if (fee < rule.MinFee)
        {
            fee = rule.MinFee;
        }

        if (rule.MaxFee is { } max && fee > max)
        {
            fee = max;
        }

        fee = Math.Round(fee, 2, MidpointRounding.AwayFromZero);
        return Math.Min(fee, amount);
    }

    /// <summary>Picks the most specific active rule: an exact currency match wins over a currency-agnostic rule.</summary>
    public static FeeRule? SelectRule(IEnumerable<FeeRule> rules, TransactionType type, string currency) =>
        rules.Where(r => r.IsActive && r.TransactionType == type && (r.Currency == null || r.Currency == currency))
             .OrderByDescending(r => r.Currency != null)
             .FirstOrDefault();
}
