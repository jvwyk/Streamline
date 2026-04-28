namespace Streamline.Core.Enums;

/// <summary>
/// When the engine invokes a transformer relative to staging. Plan
/// §15 Appendix C documents <c>per_batch</c> as v1 and notes
/// per-row invocation as deferred. Single-value enum today; adding
/// <c>PerRow</c> later is additive.
/// </summary>
public enum TransformInvocation
{
    /// <summary>
    /// Invoke the transformer once per batch, after every row has
    /// been staged. The transformer reads from staging and writes to
    /// destination tables. v1 default.
    /// </summary>
    PerBatch,
}
