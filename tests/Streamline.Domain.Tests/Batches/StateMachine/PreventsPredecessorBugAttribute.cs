namespace Streamline.Domain.Tests.Batches.StateMachine;

/// <summary>
/// Marks a test as a regression guard against a specific predecessor-
/// project bug. The meta-test in
/// <c>RowStateMachineRegressionTests</c> asserts that every
/// <c>[Fact]</c> and <c>[Theory]</c> method in that class carries
/// this attribute, so a regression test cannot lose its context over
/// time. The attribute serves the runtime-verifiable role the user
/// described as "doc comment with 'predecessor bug' substring";
/// XML doc comments are not available at runtime in this test
/// project (<c>GenerateDocumentationFile=false</c>), so the attribute
/// is the structured signal.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class PreventsPredecessorBugAttribute : Attribute
{
    /// <summary>Stable identifier (e.g. <c>"SM-1a"</c>).</summary>
    public string Id { get; }

    /// <summary>One-sentence description of what went wrong.</summary>
    public string Description { get; }

    public PreventsPredecessorBugAttribute(string id, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Id = id;
        Description = description;
    }
}
