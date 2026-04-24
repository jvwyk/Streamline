using System.Reflection;
using AwesomeAssertions;
using Streamline.Core.Observations;
using Xunit;

namespace Streamline.Core.Tests.Observations;

public class ObservationCodesTests
{
    /// <summary>
    /// The canonical catalog. This list is redundant with the
    /// ObservationCodes class by design: changing the catalog requires
    /// changing two places, which surfaces additions and removals in
    /// PR review rather than letting them slip silently. Order mirrors
    /// docs/OBSERVATIONS.md section-by-section.
    /// </summary>
    private static readonly string[] ExpectedCodes =
    [
        // Ingestion
        "FILE_INGESTED",
        "FILE_SKIPPED_DUPLICATE",
        "FILE_READ_FAILED",
        "FILE_ENCODING_DETECTED",
        "FILE_EMPTY",
        "HEADER_NOT_FOUND",

        // Schema Drift
        "SCHEMA_DRIFT_NEW_COLUMN",
        "SCHEMA_DRIFT_MISSING_REQUIRED",
        "SCHEMA_DRIFT_MISSING_OPTIONAL",
        "SCHEMA_DRIFT_BLOCKED",

        // Validation
        "MISSING_REQUIRED",
        "INVALID_TYPE",
        "INVALID_FORMAT",
        "INVALID_LENGTH",
        "INVALID_RANGE",
        "FK_VIOLATION",
        "PK_VIOLATION",
        "VALIDATION_FAILED",

        // FK Resolution
        "FK_CACHE_EMPTY",
        "FK_CACHE_LOADED",

        // Transformation
        "TRANSFORMER_INVOKED",
        "TRANSFORMER_COMPLETED",
        "TRANSFORMER_PARTIAL_FAILURE",
        "TRANSFORMER_THREW",
        "TRANSFORMER_NOT_FOUND",

        // Destination Writes (Replication case)
        "UPSERT_COMPLETED",
        "UPSERT_FAILED",
        "SAVEPOINT_ROLLED_BACK",

        // Reconciliation
        "RECONCILIATION_PASSED",
        "RECONCILIATION_MISMATCH",
        "RECONCILIATION_CHECK_FAILED",

        // Container Handling (zip)
        "ZIP_UNPACKED",
        "ZIP_ENTRY_UNMATCHED",
        "ZIP_ENTRY_CORRUPTED",
        "ZIP_NESTING_EXCEEDED",

        // Retry and Recovery
        "BATCH_RETRIED",
        "BATCH_RECONCILED",
        "QUARANTINE_RESOLVED",

        // Batch Lifecycle
        "BATCH_STARTED",
        "BATCH_COMPLETED",
        "BATCH_FAILED",
        "ADVISORY_LOCK_CONTENDED",

        // Catch-all
        "UNKNOWN",
    ];

    [Fact]
    public void Every_constant_value_matches_its_identifier()
    {
        // Guards against accidental drift between the C# identifier and
        // the wire value. e.g. someone mis-types and gets
        //   public const string FILE_INGESTED = "FILE_INGESTD";
        // This test catches that at compile-time of the test.
        var fields = ObservationCodeFields();

        foreach (var field in fields)
        {
            var literalValue = (string?)field.GetRawConstantValue();
            literalValue.Should().Be(
                field.Name,
                $"constant {field.Name} must equal its own identifier");
        }
    }

    [Fact]
    public void Catalog_matches_the_canonical_catalog_exactly()
    {
        // Tripwire for additions and removals. If a code is added to
        // ObservationCodes without updating OBSERVATIONS.md and this
        // test's ExpectedCodes list, the tripwire fires.
        var actualCodes = ObservationCodeFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var expectedCodes = ExpectedCodes
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        actualCodes.Should().Equal(expectedCodes);
    }

    [Fact]
    public void No_duplicate_code_values()
    {
        var codes = ObservationCodeFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_code_is_upper_snake_case()
    {
        foreach (var code in ExpectedCodes)
        {
            code.Should().MatchRegex(
                "^[A-Z][A-Z0-9_]*[A-Z0-9]$",
                $"code '{code}' must be UPPER_SNAKE_CASE per OBSERVATIONS.md convention");
        }
    }

    private static FieldInfo[] ObservationCodeFields() =>
        typeof(ObservationCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false })
            .Where(f => f.FieldType == typeof(string))
            .ToArray();
}
