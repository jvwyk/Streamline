using System.Collections.Frozen;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;

namespace Streamline.Domain.Validation;

/// <summary>
/// Per-batch FK preload-and-validate. The orchestrator constructs a
/// fresh resolver at the start of processing, calls
/// <see cref="LoadAsync"/> for each <see cref="FkReference"/> the
/// batch needs (sequentially), then calls <see cref="Validate"/>
/// per row per FK column. Discarded when the batch ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> Per-batch instance, not a DI singleton. The
/// FK cache for one batch must not leak into the next; predecessor
/// flag #11 (cache persists across scopes) caused real production
/// issues. Sub-phase 1g decides where to construct (orchestrator
/// startup) and where to discard (orchestrator teardown).
/// </para>
/// <para>
/// <b>Sequential preload.</b> <see cref="LoadAsync"/> queries one FK
/// at a time. Postgres transactions are single-threaded by
/// convention; firing parallel queries on the same scope risks
/// weird interactions. Phase 4 revisits if perf testing shows this
/// matters; until then, simplicity beats theoretical concurrency.
/// </para>
/// <para>
/// <b>Comparison plane.</b> The cache stores raw strings — exactly
/// what <see cref="ITransactionScope.GetDistinctColumnValuesAsync"/>
/// returns. <see cref="Validate"/> compares the supplied raw string
/// directly. The orchestrator must call this on the pre-validation
/// raw <see cref="Record"/>, NOT on a typed
/// the typed <c>ValidationResult.ValidatedRecord</c>: round-tripping
/// typed values through string conversion would introduce format
/// mismatches (DateOnly.ToString() vs Postgres date rendering,
/// decimal trailing zeros, etc.).
/// </para>
/// <para>
/// <b>Enforcement modes.</b> <see cref="FkEnforcementMode.Always"/>
/// requires the value to be in the cache; an empty cache fails
/// every value. <see cref="FkEnforcementMode.WhenParentPopulated"/>
/// passes when the cache is empty (the orchestrator separately
/// emits <c>FK_CACHE_EMPTY</c> at <c>Warning</c>); when populated,
/// it behaves like Always. <see cref="FkEnforcementMode.Never"/>
/// always passes.
/// </para>
/// <para>
/// <b>Null values pass.</b> A null value (nullable FK column with
/// no foreign reference) is not a violation regardless of mode.
/// Null-vs-required is a separate concern handled by
/// <see cref="RowValidator"/>'s MISSING_REQUIRED check.
/// </para>
/// <para>
/// <b>Programmer-error throw.</b> <see cref="Validate"/> throws
/// <see cref="InvalidOperationException"/> when called for an
/// FK that hasn't been loaded. Silent skip would mask orchestrator
/// preload bugs (predecessor flag #6). Treat un-loaded FKs as
/// "the orchestrator forgot to call LoadAsync" — fail loudly.
/// </para>
/// </remarks>
public sealed class FkResolver
{
    private readonly Dictionary<FkReference, FrozenSet<string>> _cache = new();

    /// <summary>
    /// Populate the cache for <paramref name="fk"/> by querying the
    /// distinct values of the parent column under
    /// <paramref name="parentSchema"/>. Re-loading an already-loaded
    /// FK overwrites the previous cache (the orchestrator may want
    /// fresh values mid-batch in some scenarios; cheap and predictable).
    /// </summary>
    public async Task LoadAsync(
        FkReference fk,
        string parentSchema,
        ITransactionScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fk);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSchema);
        ArgumentNullException.ThrowIfNull(scope);

        var values = await scope
            .GetDistinctColumnValuesAsync(parentSchema, fk.ParentTable, fk.ParentColumn, cancellationToken)
            .ConfigureAwait(false);

        _cache[fk] = values.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="fk"/> has been loaded — even if the
    /// load returned zero values.
    /// </summary>
    public bool IsLoaded(FkReference fk)
    {
        ArgumentNullException.ThrowIfNull(fk);
        return _cache.ContainsKey(fk);
    }

    /// <summary>
    /// True when <paramref name="fk"/> has been loaded and the parent
    /// column had zero distinct values. Useful for the orchestrator
    /// deciding whether to emit <c>FK_CACHE_EMPTY</c>. Returns false
    /// (not throw) for un-loaded FKs.
    /// </summary>
    public bool IsEmpty(FkReference fk)
    {
        ArgumentNullException.ThrowIfNull(fk);
        return _cache.TryGetValue(fk, out var set) && set.Count == 0;
    }

    /// <summary>
    /// Validate one column value against this FK reference. Returns
    /// true when the value passes the FK check (in cache, or skipped
    /// per enforcement policy, or null), false when it fails.
    /// Throws <see cref="InvalidOperationException"/> if the FK has
    /// not been loaded.
    /// </summary>
    public bool Validate(FkReference fk, string? value)
    {
        ArgumentNullException.ThrowIfNull(fk);

        if (!_cache.TryGetValue(fk, out var cache))
        {
            throw new InvalidOperationException(
                $"FK reference {fk.ParentTable}.{fk.ParentColumn} has not been loaded. " +
                $"Call LoadAsync before Validate. (Orchestrator preload bug.)");
        }

        // Never: skip enforcement. Documented in the registry but
        // declared FK references can still exist for lineage.
        if (fk.EnforcementMode == FkEnforcementMode.Never)
        {
            return true;
        }

        // Null value: a nullable FK column with no foreign reference.
        // Required-vs-null is a separate concern in RowValidator.
        if (value is null)
        {
            return true;
        }

        // WhenParentPopulated with empty cache: the orchestrator emits
        // FK_CACHE_EMPTY separately; this resolver passes the value.
        if (fk.EnforcementMode == FkEnforcementMode.WhenParentPopulated && cache.Count == 0)
        {
            return true;
        }

        // Always (or WhenParentPopulated with populated cache): the
        // value must be in the cache.
        return cache.Contains(value);
    }
}
