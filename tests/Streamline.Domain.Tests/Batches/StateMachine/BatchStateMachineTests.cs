using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches.StateMachine;
using Xunit;

namespace Streamline.Domain.Tests.Batches.StateMachine;

/// <summary>
/// Smaller matrix than RowStateMachine (six states, six legal
/// transitions). Per session feedback we use a couple of well-named
/// path tests plus a parameterised illegal-cell theory rather than a
/// full 36-cell readout — batches don't have the same blast radius
/// as rows.
/// </summary>
public class BatchStateMachineTests
{
    private static readonly HashSet<(BatchStatus From, BatchStatus To)> ExpectedLegal =
    [
        (BatchStatus.Created,    BatchStatus.Ingesting),
        (BatchStatus.Ingesting,  BatchStatus.Ingested),
        (BatchStatus.Ingested,   BatchStatus.Processing),
        (BatchStatus.Processing, BatchStatus.Completed),
        (BatchStatus.Processing, BatchStatus.Failed),
        (BatchStatus.Failed,     BatchStatus.Processing),
    ];

    [Fact]
    public void LegalTransitionCount_IsExactlySix()
    {
        BatchStateMachine.LegalTransitionCount.Should().Be(6);
        ExpectedLegal.Should().HaveCount(6);
    }

    [Fact]
    public void Happy_path_through_full_lifecycle_is_legal()
    {
        // Created → Ingesting → Ingested → Processing → Completed
        BatchStateMachine.IsLegal(BatchStatus.Created, BatchStatus.Ingesting).Should().BeTrue();
        BatchStateMachine.IsLegal(BatchStatus.Ingesting, BatchStatus.Ingested).Should().BeTrue();
        BatchStateMachine.IsLegal(BatchStatus.Ingested, BatchStatus.Processing).Should().BeTrue();
        BatchStateMachine.IsLegal(BatchStatus.Processing, BatchStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void Failed_to_Processing_is_legal_for_retry()
    {
        BatchStateMachine.IsLegal(BatchStatus.Failed, BatchStatus.Processing).Should().BeTrue();
    }

    [Fact]
    public void Processing_to_Failed_is_legal()
    {
        BatchStateMachine.IsLegal(BatchStatus.Processing, BatchStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void Failed_to_Completed_directly_is_illegal()
    {
        // A failed batch must re-enter processing before it can complete;
        // it cannot jump straight to Completed.
        BatchStateMachine.IsLegal(BatchStatus.Failed, BatchStatus.Completed).Should().BeFalse();
    }

    [Fact]
    public void Completed_to_anything_is_illegal()
    {
        foreach (var to in Enum.GetValues<BatchStatus>())
        {
            BatchStateMachine.IsLegal(BatchStatus.Completed, to).Should().BeFalse(
                $"Completed is terminal; {BatchStatus.Completed} -> {to} must be illegal");
        }
    }

    [Fact]
    public void Processing_to_Created_is_illegal()
    {
        BatchStateMachine.IsLegal(BatchStatus.Processing, BatchStatus.Created).Should().BeFalse();
    }

    [Fact]
    public void Self_transitions_are_all_illegal()
    {
        foreach (var status in Enum.GetValues<BatchStatus>())
        {
            BatchStateMachine.IsLegal(status, status).Should().BeFalse(
                $"self-transition {status} -> {status} must be illegal");
        }
    }

    public static IEnumerable<object[]> AllPairs() =>
        from f in Enum.GetValues<BatchStatus>()
        from t in Enum.GetValues<BatchStatus>()
        select new object[] { f, t };

    [Theory, MemberData(nameof(AllPairs))]
    public void IsLegal_MatchesIndependentCanonicalSet(BatchStatus from, BatchStatus to)
    {
        var expected = ExpectedLegal.Contains((from, to));
        BatchStateMachine.IsLegal(from, to).Should().Be(expected);
    }

    [Fact]
    public void RequireLegal_IllegalCell_Throws()
    {
        var act = () => BatchStateMachine.RequireLegal(
            BatchStatus.Completed, BatchStatus.Processing, reason: "batch_id=abc");

        act.Should().Throw<IllegalStateTransitionException>()
            .Which.Should().Match<IllegalStateTransitionException>(ex =>
                ex.Subject == "batch"
                && ex.From == "Completed"
                && ex.To == "Processing");
    }

    [Fact]
    public void RequireLegal_LegalCell_DoesNotThrow()
    {
        var act = () => BatchStateMachine.RequireLegal(
            BatchStatus.Failed, BatchStatus.Processing);
        act.Should().NotThrow();
    }
}
