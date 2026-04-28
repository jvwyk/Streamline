using Streamline.Domain.Batches;
using Streamline.Domain.Transforms;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// Test transformer that returns pre-canned <see cref="TransformOutcome"/>
/// values from a queue. Each <see cref="ExecuteAsync"/> call dequeues
/// the next outcome. Configurable via the constructor (initial set
/// of outcomes) and via <see cref="Enqueue"/> for tests that want
/// to add outcomes incrementally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why list-of-outcomes (Q4 of the 1g plan).</b> Tests need
/// predictable, deterministic transformer behaviour. A computed-
/// outcome variant
/// (<c>Func&lt;TransformReference, BatchContext, TransformOutcome&gt;</c>)
/// is more flexible but harder to reason about. A list of outcomes
/// returned in invocation order is enough for v1; the computed
/// variant can be added later if a real test needs it.
/// </para>
/// <para>
/// <b>Exhaustion.</b> If the queue runs out and another invocation
/// is requested, <see cref="ExecuteAsync"/> throws
/// <see cref="InvalidOperationException"/> with a message that
/// names the fake — silent defaults would mask script-exhaustion
/// bugs in 1h tests.
/// </para>
/// <para>
/// <b>BatchContext.</b> The transformer doesn't inspect the context
/// in v1; it just records the invocation count. Tests that want to
/// verify the transformer was called with a specific context can
/// read <see cref="ForTestingOnly_InvocationCount"/> and
/// <see cref="ForTestingOnly_LastContext"/>.
/// </para>
/// </remarks>
public sealed class ScriptedTransformer : ITransformer
{
    private readonly object _lock = new();
    private readonly Queue<TransformOutcome> _outcomes = new();
    private long _invocationCount;
    private BatchContext? _lastContext;

    public ScriptedTransformer(IEnumerable<TransformOutcome>? outcomes = null)
    {
        if (outcomes is not null)
        {
            foreach (var outcome in outcomes)
            {
                ArgumentNullException.ThrowIfNull(outcome);
                _outcomes.Enqueue(outcome);
            }
        }
    }

    /// <summary>
    /// Add an outcome to the queue for a future invocation. Allows
    /// tests to set up outcomes incrementally rather than all
    /// up-front.
    /// </summary>
    public void Enqueue(TransformOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_lock)
        {
            _outcomes.Enqueue(outcome);
        }
    }

    public Task<TransformOutcome> ExecuteAsync(
        BatchContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        lock (_lock)
        {
            if (_outcomes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"ScriptedTransformer exhausted after {_invocationCount} invocation(s). " +
                    "Configure additional outcomes via the constructor or Enqueue.");
            }
            _invocationCount++;
            _lastContext = context;
            return Task.FromResult(_outcomes.Dequeue());
        }
    }

    /// <summary>
    /// Test-only: number of times <see cref="ExecuteAsync"/> has
    /// been called. Useful for asserting "the transformer was
    /// invoked once for this batch."
    /// </summary>
    public long ForTestingOnly_InvocationCount
    {
        get
        {
            lock (_lock)
            {
                return _invocationCount;
            }
        }
    }

    /// <summary>
    /// Test-only: the <see cref="BatchContext"/> from the most
    /// recent invocation, or null if the transformer has never been
    /// invoked.
    /// </summary>
    public BatchContext? ForTestingOnly_LastContext
    {
        get
        {
            lock (_lock)
            {
                return _lastContext;
            }
        }
    }
}
