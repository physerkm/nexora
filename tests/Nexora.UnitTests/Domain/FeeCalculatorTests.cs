using Nexora.Domain;
using Nexora.Domain.Common;
using Nexora.Domain.Entities;
using Nexora.Domain.Rules;

namespace Nexora.UnitTests.Domain;

public class FeeCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static FeeRule Rule(decimal pct, decimal fix = 0, decimal min = 0, decimal? max = null, string? currency = null,
        TransactionType type = TransactionType.Payment) =>
        FeeRule.Create(Guid.NewGuid(), type, currency, pct, fix, min, max, Now);

    [Theory]
    [InlineData(1000, 1.5, 0.5, 0, null, 15.50)]      // percentage + fixed
    [InlineData(10, 1.5, 0, 1, null, 1.00)]          // minimum applies
    [InlineData(100000, 1.5, 0, 0, 250.0, 250.00)]     // maximum applies
    [InlineData(0.50, 0, 2, 0, null, 0.50)]          // never more than the amount
    [InlineData(333.33, 1, 0, 0, null, 3.33)]        // 3.3333 -> 3.33
    [InlineData(250, 1.25, 0, 0, null, 3.13)]        // 3.125 -> 3.13 (away from zero)
    public void Calculates_fee(decimal amount, decimal pct, decimal fix, decimal min, double? max, decimal expected) =>
        Assert.Equal(expected, FeeCalculator.Calculate(amount, Rule(pct, fix, min, (decimal?)max)));

    [Fact]
    public void No_rule_means_no_fee() => Assert.Equal(0m, FeeCalculator.Calculate(500m, null));

    [Fact]
    public void Currency_specific_rule_wins_over_generic_rule()
    {
        var generic = Rule(1m);
        var tryRule = Rule(0.2m, currency: "TRY");
        var otherType = Rule(5m, currency: "TRY", type: TransactionType.Payout);

        Assert.Same(tryRule, FeeCalculator.SelectRule([generic, tryRule, otherType], TransactionType.Payment, "TRY"));
        Assert.Same(generic, FeeCalculator.SelectRule([generic, tryRule, otherType], TransactionType.Payment, "USD"));
        Assert.Null(FeeCalculator.SelectRule([generic, tryRule], TransactionType.Refund, "TRY"));
    }

    [Fact]
    public void Inactive_rules_are_ignored()
    {
        var rule = Rule(1m);
        rule.Update(1m, 0, 0, null, isActive: false, Now);
        Assert.Null(FeeCalculator.SelectRule([rule], TransactionType.Payment, "TRY"));
    }

    [Theory]
    [InlineData(-1, 0, 0, null)]
    [InlineData(101, 0, 0, null)]
    [InlineData(1, -1, 0, null)]
    [InlineData(1, 0, 10, 5.0)]
    public void Invalid_rules_are_rejected(decimal pct, decimal fix, decimal min, double? max) =>
        Assert.Equal("invalid_fee_rule", Assert.Throws<DomainException>(() => Rule(pct, fix, min, (decimal?)max)).Code);
}
