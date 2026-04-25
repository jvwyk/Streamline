using Streamline.Core.Enums;

namespace Streamline.Domain.Batches.StateMachine;

/// <summary>
/// Pure-logic gate over <see cref="BatchStatus"/> transitions.
/// Smaller matrix than <see cref="RowStateMachine"/> and far less
/// blast radius — batches don't have the same volume or concurrency
/// surface as rows. Mirrors the batch state model in
/// docs/streamline-plan.md §5.3 and the per-value XML doc on
/// <see cref="BatchStatus"/>.
/// </summary>
/// <remarks>
/// Six states, six legal transitions:
/// <list type="bullet">
///   <item><c>Created → Ingesting</c></item>
///   <item><c>Ingesting → Ingested</c></item>
///   <item><c>Ingested → Processing</c></item>
///   <item><c>Processing → Completed</c></item>
///   <item><c>Processing → Failed</c></item>
///   <item><c>Failed → Processing</c> (retry)</item>
/// </list>
/// <para>
/// <see cref="BatchStatus.Completed"/> is terminal — every transition
/// out of it is illegal. <see cref="BatchStatus.Failed"/> is
/// retry-only-terminal: the only escape is back to
/// <see cref="BatchStatus.Processing"/> via
/// <c>RetryBatchCommand</c>; <c>Failed → Completed</c> directly is
/// illegal (a failed batch must re-enter processing before it can
/// complete).
/// </para>
/// </remarks>
public static class BatchStateMachine
{
    private static readonly HashSet<(BatchStatus From, BatchStatus To)> LegalTransitions =
    [
        (BatchStatus.Created,    BatchStatus.Ingesting),
        (BatchStatus.Ingesting,  BatchStatus.Ingested),
        (BatchStatus.Ingested,   BatchStatus.Processing),
        (BatchStatus.Processing, BatchStatus.Completed),
        (BatchStatus.Processing, BatchStatus.Failed),
        (BatchStatus.Failed,     BatchStatus.Processing),  // retry
    ];

    public static int LegalTransitionCount => LegalTransitions.Count;

    public static bool IsLegal(BatchStatus from, BatchStatus to) =>
        LegalTransitions.Contains((from, to));

    public static void RequireLegal(BatchStatus from, BatchStatus to, string? reason = null)
    {
        if (!IsLegal(from, to))
        {
            throw new IllegalStateTransitionException(
                "batch", from.ToString(), to.ToString(), reason);
        }
    }
}
