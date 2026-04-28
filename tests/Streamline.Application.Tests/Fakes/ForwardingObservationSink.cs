using Streamline.Core.Observations;
using Streamline.Domain.Abstractions;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// Adapter that forwards <see cref="IObservationSink.RecordAsync"/>
/// calls to an underlying <see cref="IObservationRepository"/>. Lets
/// integration tests wire the orchestrator's per-batch sink directly
/// to the in-memory observation repository so observations emitted
/// during a flow are queryable later via
/// <c>InspectBatchHandler</c>.
/// </summary>
/// <remarks>
/// Test infrastructure. If Phase 2's Postgres infrastructure ends up
/// needing the same shape (a sink that persists to a repository),
/// promote this to <c>Streamline.Application</c> as a production
/// type then.
/// </remarks>
public sealed class ForwardingObservationSink : IObservationSink
{
    private readonly IObservationRepository _repository;

    public ForwardingObservationSink(IObservationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public Task RecordAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return _repository.RecordAsync(observation, cancellationToken);
    }
}
