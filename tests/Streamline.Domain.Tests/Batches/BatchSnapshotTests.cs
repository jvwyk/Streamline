using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Domain.Tests.Batches;

public class BatchSnapshotTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var snapshot = new BatchSnapshot(
            AnyBatch, "/data/incoming", BatchStatus.Processing, AnyTime, CompletedAt: null);

        snapshot.BatchId.Should().Be(AnyBatch);
        snapshot.Source.Should().Be("/data/incoming");
        snapshot.Status.Should().Be(BatchStatus.Processing);
        snapshot.StartedAt.Should().Be(AnyTime);
        snapshot.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Construct_DefaultBatchId_Throws()
    {
        var act = () => new BatchSnapshot(default, "src", BatchStatus.Created, AnyTime, null);
        act.Should().Throw<ArgumentException>().WithMessage("*default(BatchId)*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_BlankSource_Throws(string source)
    {
        var act = () => new BatchSnapshot(AnyBatch, source, BatchStatus.Created, AnyTime, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_CompletedAtBeforeStartedAt_Throws()
    {
        var act = () => new BatchSnapshot(
            AnyBatch, "src", BatchStatus.Failed,
            StartedAt: AnyTime,
            CompletedAt: AnyTime.AddMinutes(-1));
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedAt*StartedAt*");
    }

    [Fact]
    public void Construct_CompletedAtEqualToStartedAt_Succeeds()
    {
        // A degenerate but legal case: a batch that completed at the
        // same instant it started (in-memory tests with mocked clocks
        // can hit this; don't reject it).
        var snapshot = new BatchSnapshot(
            AnyBatch, "src", BatchStatus.Completed, AnyTime, CompletedAt: AnyTime);

        snapshot.CompletedAt.Should().Be(AnyTime);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new BatchSnapshot(AnyBatch, "src", BatchStatus.Created, AnyTime, null);
        var b = new BatchSnapshot(AnyBatch, "src", BatchStatus.Created, AnyTime, null);
        var different = new BatchSnapshot(AnyBatch, "src", BatchStatus.Processing, AnyTime, null);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }
}
