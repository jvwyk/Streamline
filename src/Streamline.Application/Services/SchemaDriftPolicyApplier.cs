using System.Collections.Immutable;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Domain.Detection;
using Streamline.Domain.Registry;

namespace Streamline.Application.Services;

/// <summary>
/// Applies a <see cref="RegistryEntry"/>'s drift policy to a
/// <see cref="DriftReport"/> produced by
/// <see cref="SchemaDriftDetector"/>. Decides which observations
/// to emit at which severity, and whether the file should be
/// blocked from ingestion.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure logic.</b> No I/O, no observations emitted directly —
/// returns a <see cref="SchemaDriftDecision"/> that the orchestrator
/// uses to either reject the file (with a Critical observation) or
/// proceed (with Warning / Info observations as appropriate).
/// Splitting the decision from emission keeps this class trivially
/// testable.
/// </para>
/// <para>
/// <b>Severity rules per drift dimension.</b>
/// <list type="bullet">
///   <item><b>NewColumns</b> — severity from
///     <see cref="RegistryEntry.NewColumnsDriftPolicy"/>.
///     <see cref="DriftPolicy.Block"/> blocks the file at
///     <c>SCHEMA_DRIFT_BLOCKED</c> Critical;
///     <see cref="DriftPolicy.Warn"/> emits
///     <c>SCHEMA_DRIFT_NEW_COLUMN</c> Warning;
///     <see cref="DriftPolicy.Ignore"/> emits the same code at
///     Info.</item>
///   <item><b>MissingRequiredColumns</b> — always Critical regardless
///     of policy. A required column missing from the source file is
///     not negotiable.
///     <see cref="RegistryEntry.MissingRequiredDriftPolicy"/>'s
///     <see cref="DriftPolicy.Block"/> is the engine default; Warn
///     and Ignore are unusual choices and the policy applier
///     respects them by demoting the severity, but the operator
///     should expect rows to fail validation downstream regardless.</item>
///   <item><b>MissingOptionalColumns</b> — severity from
///     <see cref="RegistryEntry.MissingOptionalDriftPolicy"/>.
///     Block on a missing-optional is unusual but legal.</item>
/// </list>
/// </para>
/// </remarks>
public static class SchemaDriftPolicyApplier
{
    public static SchemaDriftDecision Apply(DriftReport report, RegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(entry);

        var emissions = ImmutableArray.CreateBuilder<DriftObservationEmission>();
        var blocked = false;

        // NewColumns
        if (!report.NewColumns.IsDefaultOrEmpty)
        {
            var policy = entry.NewColumnsDriftPolicy;
            if (policy == DriftPolicy.Block)
            {
                emissions.Add(new DriftObservationEmission(
                    Code: ObservationCodes.SCHEMA_DRIFT_BLOCKED,
                    Severity: ObservationSeverity.Critical,
                    Message: $"New columns present in file but blocked by drift policy: {string.Join(", ", report.NewColumns)}.",
                    ContextDimension: "new_columns",
                    AffectedColumns: report.NewColumns));
                blocked = true;
            }
            else
            {
                var severity = policy == DriftPolicy.Warn
                    ? ObservationSeverity.Warning
                    : ObservationSeverity.Info;
                emissions.Add(new DriftObservationEmission(
                    Code: ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN,
                    Severity: severity,
                    Message: $"File contains columns not declared in registry: {string.Join(", ", report.NewColumns)}.",
                    ContextDimension: "new_columns",
                    AffectedColumns: report.NewColumns));
            }
        }

        // MissingRequiredColumns
        if (!report.MissingRequiredColumns.IsDefaultOrEmpty)
        {
            var policy = entry.MissingRequiredDriftPolicy;
            var severity = policy switch
            {
                DriftPolicy.Block => ObservationSeverity.Critical,
                DriftPolicy.Warn => ObservationSeverity.Warning,
                _ => ObservationSeverity.Info,
            };
            emissions.Add(new DriftObservationEmission(
                Code: ObservationCodes.SCHEMA_DRIFT_MISSING_REQUIRED,
                Severity: severity,
                Message: $"Required columns missing from file: {string.Join(", ", report.MissingRequiredColumns)}.",
                ContextDimension: "missing_required_columns",
                AffectedColumns: report.MissingRequiredColumns));

            if (policy == DriftPolicy.Block)
            {
                blocked = true;
            }
        }

        // MissingOptionalColumns
        if (!report.MissingOptionalColumns.IsDefaultOrEmpty)
        {
            var policy = entry.MissingOptionalDriftPolicy;
            if (policy == DriftPolicy.Block)
            {
                emissions.Add(new DriftObservationEmission(
                    Code: ObservationCodes.SCHEMA_DRIFT_BLOCKED,
                    Severity: ObservationSeverity.Critical,
                    Message: $"Optional columns missing from file but blocked by drift policy: {string.Join(", ", report.MissingOptionalColumns)}.",
                    ContextDimension: "missing_optional_columns",
                    AffectedColumns: report.MissingOptionalColumns));
                blocked = true;
            }
            else
            {
                var severity = policy == DriftPolicy.Warn
                    ? ObservationSeverity.Warning
                    : ObservationSeverity.Info;
                emissions.Add(new DriftObservationEmission(
                    Code: ObservationCodes.SCHEMA_DRIFT_MISSING_OPTIONAL,
                    Severity: severity,
                    Message: $"Optional columns missing from file: {string.Join(", ", report.MissingOptionalColumns)}.",
                    ContextDimension: "missing_optional_columns",
                    AffectedColumns: report.MissingOptionalColumns));
            }
        }

        return new SchemaDriftDecision(blocked, emissions.ToImmutable());
    }
}

/// <summary>
/// The decision produced by <see cref="SchemaDriftPolicyApplier"/>.
/// The orchestrator emits each <see cref="Emissions"/> entry as an
/// observation; if <see cref="FileBlocked"/> is true, the file is
/// rejected before any rows are staged.
/// </summary>
public sealed record class SchemaDriftDecision(
    bool FileBlocked,
    ImmutableArray<DriftObservationEmission> Emissions);

/// <summary>
/// One observation the orchestrator should emit on behalf of a
/// drift detection. Carries enough structured context that the
/// orchestrator can build the observation's
/// <see cref="Observation.Context"/> dictionary.
/// </summary>
public sealed record class DriftObservationEmission(
    string Code,
    ObservationSeverity Severity,
    string Message,
    string ContextDimension,
    ImmutableArray<string> AffectedColumns);
