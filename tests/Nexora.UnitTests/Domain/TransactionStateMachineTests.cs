using Nexora.Domain;
using Nexora.Domain.Common;
using Nexora.Domain.Rules;

namespace Nexora.UnitTests.Domain;

public class TransactionStateMachineTests
{
    public static TheoryData<TransactionStatus, TransactionStatus, bool> AllTransitions()
    {
        var allowed = new HashSet<(TransactionStatus, TransactionStatus)>
        {
            (TransactionStatus.Pending, TransactionStatus.Approved),
            (TransactionStatus.Pending, TransactionStatus.Rejected),
            (TransactionStatus.Pending, TransactionStatus.Cancelled),
            (TransactionStatus.Pending, TransactionStatus.Expired),
            (TransactionStatus.Approved, TransactionStatus.Cancelled),
        };

        var data = new TheoryData<TransactionStatus, TransactionStatus, bool>();
        foreach (var from in Enum.GetValues<TransactionStatus>())
        {
            foreach (var to in Enum.GetValues<TransactionStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Every_transition_matches_the_specification(TransactionStatus from, TransactionStatus to, bool expected)
    {
        Assert.Equal(expected, TransactionStateMachine.CanTransition(from, to));

        if (expected)
        {
            TransactionStateMachine.EnsureCanTransition(from, to);
        }
        else
        {
            var ex = Assert.Throws<InvalidStateTransitionException>(() => TransactionStateMachine.EnsureCanTransition(from, to));
            Assert.Equal(from.ToString(), ex.From);
            Assert.Equal(to.ToString(), ex.To);
        }
    }

    [Theory]
    [InlineData(TransactionStatus.Rejected)]
    [InlineData(TransactionStatus.Cancelled)]
    [InlineData(TransactionStatus.Expired)]
    public void Terminal_states_have_no_exits(TransactionStatus status)
    {
        Assert.True(TransactionStateMachine.IsTerminal(status));
        Assert.Empty(TransactionStateMachine.NextStates(status));
    }

    [Fact]
    public void Every_status_is_covered_by_the_transition_table()
    {
        foreach (var status in Enum.GetValues<TransactionStatus>())
        {
            Assert.NotNull(TransactionStateMachine.NextStates(status));
        }
    }
}
