using Nexora.Domain.Common;

namespace Nexora.Domain.Rules;

/// <summary>
/// The single source of truth for transaction status transitions.
/// <code>
/// Pending  -> Approved | Rejected | Cancelled | Expired
/// Approved -> Cancelled   (reversal, restricted to approvers)
/// Rejected, Cancelled, Expired are terminal.
/// </code>
/// </summary>
public static class TransactionStateMachine
{
    private static readonly Dictionary<TransactionStatus, TransactionStatus[]> Transitions = new()
    {
        [TransactionStatus.Pending] =
            [TransactionStatus.Approved, TransactionStatus.Rejected, TransactionStatus.Cancelled, TransactionStatus.Expired],
        [TransactionStatus.Approved] = [TransactionStatus.Cancelled],
        [TransactionStatus.Rejected] = [],
        [TransactionStatus.Cancelled] = [],
        [TransactionStatus.Expired] = [],
    };

    public static IReadOnlyList<TransactionStatus> NextStates(TransactionStatus from) => Transitions[from];

    public static bool CanTransition(TransactionStatus from, TransactionStatus to) => Transitions[from].Contains(to);

    public static bool IsTerminal(TransactionStatus status) => Transitions[status].Length == 0;

    public static void EnsureCanTransition(TransactionStatus from, TransactionStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidStateTransitionException("Transaction", from.ToString(), to.ToString());
        }
    }
}
