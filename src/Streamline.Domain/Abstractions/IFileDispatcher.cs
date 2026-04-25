using Streamline.Core.ValueTypes;
using Streamline.Domain.Files;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Reads a container file (e.g. zip) and yields one
/// <see cref="IngestionUnit"/> per inner entry. The orchestrator
/// hands each unit to the matching <see cref="IFileReader"/> via the
/// reader's stream overloads. Per plan §6 Phase 3b.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stream ownership.</b> The dispatcher owns the streams it puts
/// inside each <see cref="IngestionUnit.Content"/> and is responsible
/// for disposing them after the unit is consumed. The
/// <see cref="IAsyncEnumerable{T}"/> sequence is the dispatcher's
/// signal to advance lifetimes — disposing the previous unit's
/// stream before yielding the next is a valid implementation
/// strategy.
/// </para>
/// <para>
/// <b>Nesting.</b> Containers may nest (a zip inside a zip) up to a
/// configured depth cap (plan §6 Phase 3b: default 1, max 3).
/// Implementations enforce the cap and emit
/// <c>ZIP_NESTING_EXCEEDED</c> at <c>Critical</c> when violated.
/// </para>
/// </remarks>
public interface IFileDispatcher
{
    /// <summary>
    /// The format token this dispatcher handles
    /// (e.g. <c>"zip"</c>). Matches <see cref="FileMapping.Format"/>.
    /// </summary>
    string Format { get; }

    /// <summary>
    /// True when this dispatcher can handle the given mapping + file
    /// path combination. Cheap check; should not open the file.
    /// </summary>
    bool CanDispatch(FileMapping mapping, string filePath);

    /// <summary>
    /// Open the container and yield one <see cref="IngestionUnit"/>
    /// per entry. Order is implementation-defined for v1; consumers
    /// that need a specific entry order should narrow the mapping's
    /// <c>entry_filter</c> reader-config rather than relying on
    /// dispatch order.
    /// </summary>
    IAsyncEnumerable<IngestionUnit> DispatchAsync(
        FileMapping mapping,
        string filePath,
        CancellationToken cancellationToken = default);
}
