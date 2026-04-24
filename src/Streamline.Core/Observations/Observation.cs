using System.Collections.Immutable;

namespace Streamline.Core.Observations;

/// <summary>
/// A structured event recorded by the engine during a batch. Observations
/// sit between logs (developer-facing) and exceptions (failure
/// signals): they are the operator-facing record of what the engine
/// noticed, consistent enough to be queried, counted, or alerted on.
/// See docs/OBSERVATIONS.md for the model and the full code catalog.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BatchId"/>, <see cref="Severity"/>, <see cref="Code"/>,
/// <see cref="Message"/>, and <see cref="RaisedAt"/> are required.
/// <see cref="Context"/> is an immutable property bag; entries may have
/// null values but not null keys.
/// </para>
/// <para>
/// The three optional scope fields (<see cref="FileLogId"/>,
/// <see cref="IncomingId"/>, <see cref="TableName"/>) narrow an
/// observation's context from batch-level to file/row/table-level
/// when applicable. A <c>BATCH_STARTED</c> observation has none set;
/// a <c>FILE_INGESTED</c> has <see cref="FileLogId"/>; a row-level
/// <c>MISSING_REQUIRED</c> has <see cref="IncomingId"/> and
/// <see cref="TableName"/>.
/// </para>
/// <para>
/// <see cref="BatchId"/> is a plain string here so Core need not
/// depend on Domain's typed <c>BatchId</c>. Domain's typed wrapper
/// bottoms out on the same string value.
/// </para>
/// </remarks>
public sealed record class Observation
{
    public ObservationSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string BatchId { get; }
    public DateTimeOffset RaisedAt { get; }
    public ImmutableDictionary<string, object?> Context { get; }

    private readonly long? _fileLogId;
    private readonly long? _incomingId;
    private readonly string? _tableName;

    public long? FileLogId
    {
        get => _fileLogId;
        init
        {
            if (value is < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "FileLogId, if provided, must be non-negative.");
            }
            _fileLogId = value;
        }
    }

    public long? IncomingId
    {
        get => _incomingId;
        init
        {
            if (value is < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "IncomingId, if provided, must be non-negative.");
            }
            _incomingId = value;
        }
    }

    public string? TableName
    {
        get => _tableName;
        init
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "TableName, if provided, must not be blank.", nameof(value));
            }
            _tableName = value;
        }
    }

    public Observation(
        ObservationSeverity severity,
        string code,
        string message,
        string batchId,
        DateTimeOffset raisedAt,
        IEnumerable<KeyValuePair<string, object?>>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId, nameof(batchId));

        Severity = severity;
        Code = code;
        Message = message;
        BatchId = batchId;
        RaisedAt = raisedAt;

        var builder = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        if (context is not null)
        {
            foreach (var kvp in context)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new ArgumentException(
                        "Context keys must not be null or blank.", nameof(context));
                }
                if (builder.ContainsKey(kvp.Key))
                {
                    throw new ArgumentException(
                        $"Duplicate context key: {kvp.Key}.", nameof(context));
                }
                builder.Add(kvp.Key, kvp.Value);
            }
        }
        Context = builder.ToImmutable();
    }

    public bool Equals(Observation? other) =>
        other is not null
        && Severity == other.Severity
        && Code == other.Code
        && Message == other.Message
        && BatchId == other.BatchId
        && RaisedAt == other.RaisedAt
        && FileLogId == other.FileLogId
        && IncomingId == other.IncomingId
        && TableName == other.TableName
        && ContextEqual(Context, other.Context);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Severity);
        hash.Add(Code);
        hash.Add(Message);
        hash.Add(BatchId);
        hash.Add(RaisedAt);
        hash.Add(FileLogId);
        hash.Add(IncomingId);
        hash.Add(TableName);
        foreach (var kvp in Context.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        return hash.ToHashCode();
    }

    private static bool ContextEqual(
        ImmutableDictionary<string, object?> a,
        ImmutableDictionary<string, object?> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        foreach (var (key, valueA) in a)
        {
            if (!b.TryGetValue(key, out var valueB) || !Equals(valueA, valueB))
            {
                return false;
            }
        }
        return true;
    }
}
