using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Registry;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Domain.Tests.Registry;

/// <summary>
/// RegistryValidator's tests focus on AGGREGATION and CROSS-rules
/// — the rules that are not tested at the individual-type level.
/// We deliberately don't re-test ColumnDefinition or RegistryEntry
/// invariants here (those are 1a / 1c tests); we test that
/// RegistryValidator surfaces them via aggregation.
/// </summary>
public class RegistryValidatorTests
{
    private static SchemaDefinition AnySchema(params ColumnDefinition[] extra)
    {
        var defaults = new[]
        {
            new ColumnDefinition("id", ColumnTypeCode.BigInt)
            {
                IsRequired = true,
                IsPrimaryKey = true,
                TargetColumn = "id",
            },
            new ColumnDefinition("name", ColumnTypeCode.String)
            {
                TargetColumn = "name",
            },
        };
        return new SchemaDefinition(defaults.Concat(extra));
    }

    private static RegistryEntry MakeEntry(
        string tableName = "broker",
        string? targetSchema = "intembeko",
        SchemaDefinition? schema = null,
        params string[] dependsOn) =>
        new RegistryEntry(tableName, schema ?? AnySchema())
        {
            TargetSchema = targetSchema,
            DependsOn = [.. dependsOn],
            ValidFrom = new DateOnly(2025, 1, 1),
        };

    // ---- single-entry happy path ---------------------------------------

    [Fact]
    public void Validate_ValidEntry_ReturnsNoViolations()
    {
        var entry = MakeEntry();
        RegistryValidator.Validate(entry).Should().BeEmpty();
    }

    // ---- delegation to RegistryEntry invariants -------------------------

    [Fact]
    public void Validate_SurfacesEntryLevelInvariants()
    {
        // No TargetSchema in replication mode — already checked by
        // RegistryEntry.DescribeInvariantViolations (1c). The
        // validator surfaces it.
        var entry = new RegistryEntry("broker", AnySchema());
        // TargetSchema null, Transform null → replication mode missing target.

        var violations = RegistryValidator.Validate(entry).ToArray();

        violations.Should().Contain(v => v.Contains("TargetSchema"));
    }

    // ---- delegation to ColumnDefinition invariants ----------------------

    [Fact]
    public void Validate_SurfacesColumnLevelInvariants_PrefixedWithTableName()
    {
        // Column with invalid regex Pattern — caught by
        // ColumnDefinition.DescribeInvariantViolations (1a). The
        // validator surfaces it with table-name prefix so multi-entry
        // aggregation can attribute the violation.
        var entry = MakeEntry(schema: new SchemaDefinition(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt)
                { IsRequired = true, IsPrimaryKey = true, TargetColumn = "id" },
            new ColumnDefinition("code", ColumnTypeCode.String)
                { Pattern = "[unterminated", TargetColumn = "code" },
        ]));

        var violations = RegistryValidator.Validate(entry).ToArray();

        violations.Should().Contain(v =>
            v.StartsWith("broker.", StringComparison.Ordinal)
            && v.Contains("Pattern"));
    }

    // ---- new cross-column rules ----------------------------------------

    [Fact]
    public void Validate_PkColumnNotRequired_Reports()
    {
        var entry = MakeEntry(schema: new SchemaDefinition(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt)
            {
                IsPrimaryKey = true,
                IsRequired = false,  // VIOLATION — PK columns must be required
                TargetColumn = "id",
            },
        ]));

        var violations = RegistryValidator.Validate(entry).ToArray();

        violations.Should().Contain(v =>
            v.Contains("primary-key columns must be IsRequired"));
    }

    [Fact]
    public void Validate_TwoColumnsMappingToSameTarget_Reports()
    {
        var entry = MakeEntry(schema: new SchemaDefinition(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt)
                { IsRequired = true, IsPrimaryKey = true, TargetColumn = "id" },
            new ColumnDefinition("BrokerCode", ColumnTypeCode.String)
                { TargetColumn = "broker_code" },
            new ColumnDefinition("BrokerCodeAlias", ColumnTypeCode.String)
                { TargetColumn = "broker_code" },  // VIOLATION — same target
        ]));

        var violations = RegistryValidator.Validate(entry).ToArray();

        violations.Should().Contain(v =>
            v.Contains("broker_code")
            && v.Contains("BrokerCode")
            && v.Contains("BrokerCodeAlias"));
    }

    [Fact]
    public void Validate_NullTargetColumns_AreNotConflicting()
    {
        // Multiple columns with TargetColumn=null (transform mode) is
        // legitimate. The unique-target rule applies only to set
        // targets.
        var entry = MakeEntry(schema: new SchemaDefinition(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt)
                { IsRequired = true, IsPrimaryKey = true, TargetColumn = "id" },
            new ColumnDefinition("a", ColumnTypeCode.String) { TargetColumn = null },
            new ColumnDefinition("b", ColumnTypeCode.String) { TargetColumn = null },
        ]));

        var violations = RegistryValidator.Validate(entry).ToArray();

        violations.Should().NotContain(v => v.Contains("mapped from multiple"));
    }

    // ---- combinatorial single-entry test --------------------------------

    [Fact]
    public void Validate_MaximallyBrokenEntry_SurfacesAllViolations()
    {
        // Combine multiple rule violations and confirm the validator
        // emits all of them rather than short-circuiting on the first.
        var entry = new RegistryEntry("broken", new SchemaDefinition(
        [
            new ColumnDefinition("a", ColumnTypeCode.String)
            {
                IsPrimaryKey = true,    // VIOLATION 1: PK not required
                IsRequired = false,
                MaxLength = -5,         // VIOLATION 2: negative length (column-level)
                TargetColumn = "x",
            },
            new ColumnDefinition("b", ColumnTypeCode.String)
            {
                Pattern = "[bad",       // VIOLATION 3: invalid regex (column-level)
                TargetColumn = "x",     // VIOLATION 4: duplicate target with column 'a'
            },
        ]))
        {
            // Entry-level violations:
            // VIOLATION 5: replication mode without TargetSchema
            // VIOLATION 6: ValidTo before ValidFrom
            // VIOLATION 7: blank DependsOn entry
            ValidFrom = new DateOnly(2025, 6, 1),
            ValidTo = new DateOnly(2025, 1, 1),
            DependsOn = [""],
        };

        var violations = RegistryValidator.Validate(entry).ToArray();

        // Don't assert exact count — the per-column-prefix duplicates
        // some entries, and we may add rules later. Assert each
        // violation category is represented.
        violations.Should().Contain(v => v.Contains("primary-key columns must be IsRequired"));
        violations.Should().Contain(v => v.Contains("MaxLength must be non-negative"));
        violations.Should().Contain(v => v.Contains("not a valid regex"));
        violations.Should().Contain(v => v.Contains("mapped from multiple"));
        violations.Should().Contain(v => v.Contains("TargetSchema"));
        violations.Should().Contain(v => v.Contains("ValidTo"));
        violations.Should().Contain(v => v.Contains("blank entry"));
    }

    // ---- multi-entry: DependsOn references must exist ------------------

    [Fact]
    public void Validate_DependsOnReferenceMissing_Reports()
    {
        var entries = new[]
        {
            MakeEntry(tableName: "broker", dependsOn: new[] { "region" }),
            MakeEntry(tableName: "trade", dependsOn: new[] { "broker", "currency" }),
            // 'region' and 'currency' don't exist in the registry.
        };

        var violations = RegistryValidator.Validate(entries).ToArray();

        violations.Should().Contain(v => v.Contains("'region'") && v.Contains("not a known"));
        violations.Should().Contain(v => v.Contains("'currency'") && v.Contains("not a known"));
    }

    [Fact]
    public void Validate_DependsOnReferencesAllExist_NoMissingReferenceViolation()
    {
        var entries = new[]
        {
            MakeEntry(tableName: "region"),
            MakeEntry(tableName: "broker", dependsOn: "region"),
        };

        var violations = RegistryValidator.Validate(entries).ToArray();

        violations.Should().NotContain(v => v.Contains("not a known registry entry"));
    }

    // ---- multi-entry: cycle detection ----------------------------------

    [Fact]
    public void Validate_TwoNodeCycle_Reports()
    {
        // A → B → A
        var entries = new[]
        {
            MakeEntry(tableName: "a", dependsOn: "b"),
            MakeEntry(tableName: "b", dependsOn: "a"),
        };

        var violations = RegistryValidator.Validate(entries).ToArray();

        violations.Should().ContainSingle(v => v.Contains("Circular DependsOn"));
    }

    [Fact]
    public void Validate_ThreeNodeCycle_Reports()
    {
        // A → B → C → A
        var entries = new[]
        {
            MakeEntry(tableName: "a", dependsOn: "b"),
            MakeEntry(tableName: "b", dependsOn: "c"),
            MakeEntry(tableName: "c", dependsOn: "a"),
        };

        var violations = RegistryValidator.Validate(entries).ToArray();

        violations.Should().ContainSingle(v => v.Contains("Circular DependsOn"));
    }

    [Fact]
    public void Validate_AcyclicGraph_NoCycleReports()
    {
        // Diamond: A → B, A → C, B → D, C → D — acyclic.
        var entries = new[]
        {
            MakeEntry(tableName: "d"),
            MakeEntry(tableName: "b", dependsOn: "d"),
            MakeEntry(tableName: "c", dependsOn: "d"),
            MakeEntry(tableName: "a", dependsOn: new[] { "b", "c" }),
        };

        var violations = RegistryValidator.Validate(entries).ToArray();

        violations.Should().NotContain(v => v.Contains("Circular DependsOn"));
    }

    [Fact]
    public void Validate_SelfLoop_DetectedByEntryInvariantNotByCycleCheck()
    {
        // Self-loops are caught by RegistryEntry.DescribeInvariantViolations
        // (1c). The cycle detector skips self-references to avoid
        // duplicate reporting.
        var entries = new[]
        {
            MakeEntry(tableName: "a", dependsOn: "a"),
        };

        var violations = RegistryValidator.Validate(entries).ToArray();

        violations.Should().Contain(v => v.Contains("references itself"));
        violations.Should().NotContain(v => v.Contains("Circular DependsOn"));
    }

    // ---- argument validation -------------------------------------------

    [Fact]
    public void Validate_NullEntry_Throws()
    {
        var act = () => RegistryValidator.Validate((RegistryEntry)null!).ToArray();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_NullEntries_Throws()
    {
        var act = () => RegistryValidator.Validate((IEnumerable<RegistryEntry>)null!).ToArray();
        act.Should().Throw<ArgumentNullException>();
    }
}
