using Nexora.Domain;
using Nexora.Domain.Common;
using Nexora.Domain.Entities;

namespace Nexora.UnitTests.Domain;

public class TransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static Transaction NewTransaction(decimal amount = 100m, decimal fee = 1.5m, string currency = "TRY") =>
        Transaction.Create(Guid.NewGuid(), Guid.NewGuid(), "TRX-1", TransactionType.Payment, amount, currency, fee,
            " desc ", " ACME ", Creator, "key-1", Now);

    [Fact]
    public void Create_starts_pending_and_computes_net_amount()
    {
        var t = NewTransaction();

        Assert.Equal(TransactionStatus.Pending, t.Status);
        Assert.Equal(98.5m, t.NetAmount);
        Assert.Equal("desc", t.Description);
        Assert.Equal("ACME", t.CounterpartyName);
        Assert.Equal(Now, t.CreatedAt);
    }

    [Theory]
    [InlineData(0, 0, "TRY", "invalid_amount")]
    [InlineData(-10, 0, "TRY", "invalid_amount")]
    [InlineData(10.123, 0, "TRY", "invalid_amount")]
    [InlineData(10, 11, "TRY", "invalid_fee")]
    [InlineData(10, -1, "TRY", "invalid_fee")]
    [InlineData(10, 0, "BTC", "unsupported_currency")]
    public void Create_enforces_invariants(decimal amount, decimal fee, string currency, string code)
    {
        var ex = Assert.Throws<DomainException>(() => NewTransaction(amount, fee, currency));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Approve_records_reviewer()
    {
        var t = NewTransaction();
        t.Approve(Reviewer, Now.AddMinutes(5));

        Assert.Equal(TransactionStatus.Approved, t.Status);
        Assert.Equal(Reviewer, t.ReviewedByUserId);
        Assert.Equal(Now.AddMinutes(5), t.ReviewedAt);
    }

    [Fact]
    public void Creator_cannot_approve_or_reject_own_transaction()
    {
        var t = NewTransaction();

        Assert.Equal("four_eyes_violation", Assert.Throws<DomainException>(() => t.Approve(Creator, Now)).Code);
        Assert.Equal("four_eyes_violation", Assert.Throws<DomainException>(() => t.Reject(Creator, "no", Now)).Code);
        Assert.Equal(TransactionStatus.Pending, t.Status);
    }

    [Fact]
    public void Reject_and_cancel_require_a_reason()
    {
        Assert.Equal("reason_required", Assert.Throws<DomainException>(() => NewTransaction().Reject(Reviewer, " ", Now)).Code);
        Assert.Equal("reason_required", Assert.Throws<DomainException>(() => NewTransaction().Cancel(Creator, "", Now)).Code);
    }

    [Fact]
    public void Approved_transaction_can_be_cancelled_but_not_approved_again()
    {
        var t = NewTransaction();
        t.Approve(Reviewer, Now);

        Assert.Throws<InvalidStateTransitionException>(() => t.Approve(Guid.NewGuid(), Now));
        Assert.Throws<InvalidStateTransitionException>(() => t.Expire(Now));

        t.Cancel(Reviewer, "chargeback", Now);
        Assert.Equal(TransactionStatus.Cancelled, t.Status);
        Assert.Equal("chargeback", t.StatusReason);
        Assert.Equal(Reviewer, t.CancelledByUserId);
    }

    [Fact]
    public void Terminal_transaction_rejects_every_change()
    {
        var t = NewTransaction();
        t.Reject(Reviewer, "missing documents", Now);

        Assert.Throws<InvalidStateTransitionException>(() => t.Approve(Reviewer, Now));
        Assert.Throws<InvalidStateTransitionException>(() => t.Cancel(Creator, "x", Now));
        Assert.Throws<InvalidStateTransitionException>(() => t.Expire(Now));
    }

    [Fact]
    public void Long_reasons_are_truncated()
    {
        var t = NewTransaction();
        t.Cancel(Creator, new string('x', 2000), Now);
        Assert.Equal(Transaction.MaxReasonLength, t.StatusReason!.Length);
    }
}
