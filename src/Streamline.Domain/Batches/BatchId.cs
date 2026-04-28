namespace Streamline.Domain.Batches;

/// <summary>
/// Strongly-typed identifier for a <c>Batch</c>. Wraps a string so it
/// matches the persisted <c>batch_log.batch_id varchar</c> column in
/// Phase 2 and the plain-string <c>BatchId</c> field on
/// <see cref="Streamline.Core.Observations.Observation"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Format.</b> <see cref="New"/> generates IDs as
/// <see cref="Guid.NewGuid"/> formatted with the <c>"N"</c> specifier
/// — 32 lowercase hex characters with no hyphens, no braces. This is
/// URL-safe, log-friendly, sorts reasonably as a string, and fits the
/// predecessor schema's <c>varchar(50)</c> with room to spare.
/// External consumers may construct a <see cref="BatchId"/> from any
/// non-blank string; the format constraint applies only to
/// <see cref="New"/>.
/// </para>
/// <para>
/// <b>Default value caveat.</b> As a struct, <c>default(BatchId)</c>
/// has a null <see cref="Value"/> and bypasses the constructor.
/// Callers receiving a <see cref="BatchId"/> should treat
/// <see cref="IsValid"/> being false as a programming error (the ID
/// was never set). Repositories must never return a default
/// <see cref="BatchId"/>.
/// </para>
/// </remarks>
public readonly record struct BatchId
{
    public string Value { get; }

    public BatchId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    /// <summary>
    /// Generates a new batch identifier from a fresh <see cref="Guid"/>
    /// formatted as 32 lowercase hex characters.
    /// </summary>
    public static BatchId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// True if this <see cref="BatchId"/> was constructed via the
    /// public constructor (or <see cref="New"/>); false for a
    /// <c>default(BatchId)</c>. Use this guard at API boundaries
    /// where uninitialized values would otherwise propagate silently.
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}
