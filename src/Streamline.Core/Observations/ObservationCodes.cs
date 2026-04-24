using System.Diagnostics.CodeAnalysis;

namespace Streamline.Core.Observations;

/// <summary>
/// The stable catalog of observation codes the engine emits. Codes are
/// <c>UPPER_SNAKE_CASE</c> strings that never change semantics between
/// releases — adding a code is fine, renaming or repurposing is a
/// breaking change. See docs/OBSERVATIONS.md for the normative
/// definitions of severity, context shape, and emission point per
/// code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Flat layout, not nested.</b> The UPPER_SNAKE prefixes (<c>FILE_</c>,
/// <c>SCHEMA_DRIFT_</c>, <c>TRANSFORMER_</c>, <c>UPSERT_</c>, <c>ZIP_</c>,
/// <c>BATCH_</c>) already group codes visually; nested static classes
/// would duplicate the prefix. The <c>#region</c> names match the
/// section headers in <c>docs/OBSERVATIONS.md</c> one-to-one so the
/// document and the code stay aligned.
/// </para>
/// <para>
/// <b>Not a closed set.</b> The engine emits these codes; consumers
/// are expected to use them rather than invent their own. The
/// <see cref="Observation"/> constructor does not cross-check
/// <c>Code</c> against this catalog — validation against the
/// catalog is the caller's responsibility, not Core's, to avoid
/// circular dependencies and to leave room for consumer-specific
/// extensions when we ever support them.
/// </para>
/// <para>
/// <b>Identifier == wire value.</b> Each field uses
/// <c>= nameof(FOO)</c> so the C# identifier and the string value
/// cannot drift; a test asserts the equality for every constant. The
/// underscored identifiers are intentionally the wire-protocol names
/// (CA1707 is suppressed for this reason).
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "Code constants are wire-protocol values; the UPPER_SNAKE_CASE " +
                    "form is the persisted identifier and must match the catalog in " +
                    "docs/OBSERVATIONS.md exactly. Renaming to PascalCase would decouple " +
                    "the C# identifier from the wire value and lose the nameof() " +
                    "self-consistency guarantee.")]
public static class ObservationCodes
{
    #region Ingestion

    public const string FILE_INGESTED = nameof(FILE_INGESTED);
    public const string FILE_SKIPPED_DUPLICATE = nameof(FILE_SKIPPED_DUPLICATE);
    public const string FILE_READ_FAILED = nameof(FILE_READ_FAILED);
    public const string FILE_ENCODING_DETECTED = nameof(FILE_ENCODING_DETECTED);
    public const string FILE_EMPTY = nameof(FILE_EMPTY);
    public const string HEADER_NOT_FOUND = nameof(HEADER_NOT_FOUND);

    #endregion

    #region Schema Drift

    public const string SCHEMA_DRIFT_NEW_COLUMN = nameof(SCHEMA_DRIFT_NEW_COLUMN);
    public const string SCHEMA_DRIFT_MISSING_REQUIRED = nameof(SCHEMA_DRIFT_MISSING_REQUIRED);
    public const string SCHEMA_DRIFT_MISSING_OPTIONAL = nameof(SCHEMA_DRIFT_MISSING_OPTIONAL);
    public const string SCHEMA_DRIFT_BLOCKED = nameof(SCHEMA_DRIFT_BLOCKED);

    #endregion

    #region Validation

    public const string MISSING_REQUIRED = nameof(MISSING_REQUIRED);
    public const string INVALID_TYPE = nameof(INVALID_TYPE);
    public const string INVALID_FORMAT = nameof(INVALID_FORMAT);
    public const string INVALID_LENGTH = nameof(INVALID_LENGTH);
    public const string INVALID_RANGE = nameof(INVALID_RANGE);
    public const string FK_VIOLATION = nameof(FK_VIOLATION);
    public const string PK_VIOLATION = nameof(PK_VIOLATION);
    public const string VALIDATION_FAILED = nameof(VALIDATION_FAILED);

    #endregion

    #region FK Resolution

    public const string FK_CACHE_EMPTY = nameof(FK_CACHE_EMPTY);
    public const string FK_CACHE_LOADED = nameof(FK_CACHE_LOADED);

    #endregion

    #region Transformation

    public const string TRANSFORMER_INVOKED = nameof(TRANSFORMER_INVOKED);
    public const string TRANSFORMER_COMPLETED = nameof(TRANSFORMER_COMPLETED);
    public const string TRANSFORMER_PARTIAL_FAILURE = nameof(TRANSFORMER_PARTIAL_FAILURE);
    public const string TRANSFORMER_THREW = nameof(TRANSFORMER_THREW);
    public const string TRANSFORMER_NOT_FOUND = nameof(TRANSFORMER_NOT_FOUND);

    #endregion

    #region Destination Writes (Replication case)

    public const string UPSERT_COMPLETED = nameof(UPSERT_COMPLETED);
    public const string UPSERT_FAILED = nameof(UPSERT_FAILED);
    public const string SAVEPOINT_ROLLED_BACK = nameof(SAVEPOINT_ROLLED_BACK);

    #endregion

    #region Reconciliation

    public const string RECONCILIATION_PASSED = nameof(RECONCILIATION_PASSED);
    public const string RECONCILIATION_MISMATCH = nameof(RECONCILIATION_MISMATCH);
    public const string RECONCILIATION_CHECK_FAILED = nameof(RECONCILIATION_CHECK_FAILED);

    #endregion

    #region Container Handling (zip)

    public const string ZIP_UNPACKED = nameof(ZIP_UNPACKED);
    public const string ZIP_ENTRY_UNMATCHED = nameof(ZIP_ENTRY_UNMATCHED);
    public const string ZIP_ENTRY_CORRUPTED = nameof(ZIP_ENTRY_CORRUPTED);
    public const string ZIP_NESTING_EXCEEDED = nameof(ZIP_NESTING_EXCEEDED);

    #endregion

    #region Retry and Recovery

    public const string BATCH_RETRIED = nameof(BATCH_RETRIED);
    public const string BATCH_RECONCILED = nameof(BATCH_RECONCILED);
    public const string QUARANTINE_RESOLVED = nameof(QUARANTINE_RESOLVED);

    #endregion

    #region Batch Lifecycle

    public const string BATCH_STARTED = nameof(BATCH_STARTED);
    public const string BATCH_COMPLETED = nameof(BATCH_COMPLETED);
    public const string BATCH_FAILED = nameof(BATCH_FAILED);
    public const string ADVISORY_LOCK_CONTENDED = nameof(ADVISORY_LOCK_CONTENDED);

    #endregion

    #region Catch-all

    public const string UNKNOWN = nameof(UNKNOWN);

    #endregion
}
