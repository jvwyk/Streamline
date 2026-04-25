using AwesomeAssertions;
using Streamline.Application.Services;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Detection;
using Streamline.Domain.Registry;
using Xunit;

namespace Streamline.Application.Tests.Services;

public class SchemaDriftPolicyApplierTests
{
    private static RegistryEntry MakeEntry(
        DriftPolicy newCols = DriftPolicy.Warn,
        DriftPolicy missingRequired = DriftPolicy.Block,
        DriftPolicy missingOptional = DriftPolicy.Warn) =>
        new RegistryEntry("broker", new SchemaDefinition(
            [new ColumnDefinition("id", ColumnTypeCode.BigInt) { IsRequired = true, IsPrimaryKey = true }]))
        {
            TargetSchema = "intembeko",
            NewColumnsDriftPolicy = newCols,
            MissingRequiredDriftPolicy = missingRequired,
            MissingOptionalDriftPolicy = missingOptional,
        };

    [Fact]
    public void Apply_EmptyReport_ProducesNoEmissionsAndNoBlock()
    {
        var decision = SchemaDriftPolicyApplier.Apply(DriftReport.Empty, MakeEntry());
        decision.FileBlocked.Should().BeFalse();
        decision.Emissions.Should().BeEmpty();
    }

    // ---- NewColumns ----------------------------------------------------

    [Fact]
    public void Apply_NewColumns_BlockPolicy_BlocksFile()
    {
        var report = new DriftReport(["extra"], [], []);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(newCols: DriftPolicy.Block));

        decision.FileBlocked.Should().BeTrue();
        decision.Emissions.Should().ContainSingle()
            .Which.Should().Match<DriftObservationEmission>(e =>
                e.Code == ObservationCodes.SCHEMA_DRIFT_BLOCKED
                && e.Severity == ObservationSeverity.Critical);
    }

    [Fact]
    public void Apply_NewColumns_WarnPolicy_EmitsWarning_NoBlock()
    {
        var report = new DriftReport(["extra"], [], []);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(newCols: DriftPolicy.Warn));

        decision.FileBlocked.Should().BeFalse();
        decision.Emissions.Should().ContainSingle()
            .Which.Should().Match<DriftObservationEmission>(e =>
                e.Code == ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN
                && e.Severity == ObservationSeverity.Warning);
    }

    [Fact]
    public void Apply_NewColumns_IgnorePolicy_EmitsInfo_NoBlock()
    {
        var report = new DriftReport(["extra"], [], []);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(newCols: DriftPolicy.Ignore));

        decision.FileBlocked.Should().BeFalse();
        decision.Emissions.Should().ContainSingle()
            .Which.Severity.Should().Be(ObservationSeverity.Info);
    }

    // ---- MissingRequired ----------------------------------------------

    [Fact]
    public void Apply_MissingRequired_BlockPolicy_BlocksAtCritical()
    {
        var report = new DriftReport([], ["amount"], []);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(missingRequired: DriftPolicy.Block));

        decision.FileBlocked.Should().BeTrue();
        decision.Emissions.Should().ContainSingle()
            .Which.Should().Match<DriftObservationEmission>(e =>
                e.Code == ObservationCodes.SCHEMA_DRIFT_MISSING_REQUIRED
                && e.Severity == ObservationSeverity.Critical);
    }

    [Fact]
    public void Apply_MissingRequired_WarnPolicy_EmitsWarning_NoBlock()
    {
        // Unusual policy choice but legal — operator demoted required-
        // missing to a warning.
        var report = new DriftReport([], ["amount"], []);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(missingRequired: DriftPolicy.Warn));

        decision.FileBlocked.Should().BeFalse();
        decision.Emissions.Should().ContainSingle()
            .Which.Severity.Should().Be(ObservationSeverity.Warning);
    }

    // ---- MissingOptional ----------------------------------------------

    [Fact]
    public void Apply_MissingOptional_WarnPolicy_EmitsWarning_NoBlock()
    {
        var report = new DriftReport([], [], ["notes"]);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(missingOptional: DriftPolicy.Warn));

        decision.FileBlocked.Should().BeFalse();
        decision.Emissions.Should().ContainSingle()
            .Which.Code.Should().Be(ObservationCodes.SCHEMA_DRIFT_MISSING_OPTIONAL);
    }

    [Fact]
    public void Apply_MissingOptional_BlockPolicy_BlocksFile()
    {
        // Unusual but legal: operator marked missing-optional as a
        // blocker. Apply it.
        var report = new DriftReport([], [], ["notes"]);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(missingOptional: DriftPolicy.Block));

        decision.FileBlocked.Should().BeTrue();
        decision.Emissions.Should().ContainSingle()
            .Which.Code.Should().Be(ObservationCodes.SCHEMA_DRIFT_BLOCKED);
    }

    // ---- multi-dimension -----------------------------------------------

    [Fact]
    public void Apply_MultipleDriftDimensions_EmitsOnePerDimension()
    {
        var report = new DriftReport(["extra"], ["required"], ["optional"]);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(
            newCols: DriftPolicy.Warn,
            missingRequired: DriftPolicy.Warn,
            missingOptional: DriftPolicy.Warn));

        decision.Emissions.Should().HaveCount(3);
        decision.FileBlocked.Should().BeFalse();
    }

    [Fact]
    public void Apply_BlockedAcrossMultipleDimensions_FileBlockedTrue()
    {
        var report = new DriftReport(["extra"], ["required"], []);
        var decision = SchemaDriftPolicyApplier.Apply(report, MakeEntry(
            newCols: DriftPolicy.Block,
            missingRequired: DriftPolicy.Block));

        decision.FileBlocked.Should().BeTrue();
        decision.Emissions.Should().HaveCount(2);
    }

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Apply_NullReport_Throws()
    {
        var act = () => SchemaDriftPolicyApplier.Apply(null!, MakeEntry());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_NullEntry_Throws()
    {
        var act = () => SchemaDriftPolicyApplier.Apply(DriftReport.Empty, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
