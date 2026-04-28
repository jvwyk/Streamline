namespace Streamline.Application.Tests.Regression;

/// <summary>
/// Marks a test as a regression guard against a specific predecessor-
/// project bug. The meta-test in
/// <see cref="CrossComponentRegressionTests"/> asserts that every
/// <c>[Fact]</c> and <c>[Theory]</c> method in the
/// <c>Application.Tests/Regression/</c> classes carries this
/// attribute, so a regression test cannot lose its context over time.
/// </summary>
/// <remarks>
/// Duplicate of the same-named attribute in
/// <c>Streamline.Domain.Tests.Batches.StateMachine</c>. The two are
/// distinct types from the runtime's perspective (different
/// assemblies); the <see cref="PreventsPredecessorBugAttribute"/>
/// name is matched by-attribute-presence in each assembly's meta-
/// test, so callers don't need a shared test-utilities project.
/// Per Q4 of the 1i plan: per-assembly meta-tests rather than
/// centralized cross-assembly scans.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class PreventsPredecessorBugAttribute : Attribute
{
    /// <summary>Stable identifier (e.g. <c>"PB-1"</c>).</summary>
    public string Id { get; }

    /// <summary>One-sentence description of what went wrong in the predecessor.</summary>
    public string Description { get; }

    public PreventsPredecessorBugAttribute(string id, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Id = id;
        Description = description;
    }
}
