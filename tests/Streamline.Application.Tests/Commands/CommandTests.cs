using AwesomeAssertions;
using Streamline.Application.Commands;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Commands;

public class CommandTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    // ---- IngestBatchCommand ------------------------------------------

    [Fact]
    public void IngestBatchCommand_PreservesSourceDirectory()
    {
        var cmd = new IngestBatchCommand("/data/incoming");
        cmd.SourceDirectory.Should().Be("/data/incoming");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void IngestBatchCommand_BlankSource_Throws(string source)
    {
        var act = () => new IngestBatchCommand(source);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IngestBatchCommand_Equality_IsStructural()
    {
        new IngestBatchCommand("/a")
            .Should().Be(new IngestBatchCommand("/a"));
        new IngestBatchCommand("/a")
            .Should().NotBe(new IngestBatchCommand("/b"));
    }

    // ---- ProcessBatchCommand -----------------------------------------

    [Fact]
    public void ProcessBatchCommand_PreservesBatchId()
    {
        var cmd = new ProcessBatchCommand(AnyBatch);
        cmd.BatchId.Should().Be(AnyBatch);
    }

    [Fact]
    public void ProcessBatchCommand_DefaultBatchId_Throws()
    {
        var act = () => new ProcessBatchCommand(default);
        act.Should().Throw<ArgumentException>().WithMessage("*default(BatchId)*");
    }

    [Fact]
    public void ProcessBatchCommand_Equality_IsStructural()
    {
        new ProcessBatchCommand(AnyBatch)
            .Should().Be(new ProcessBatchCommand(AnyBatch));
        new ProcessBatchCommand(AnyBatch)
            .Should().NotBe(new ProcessBatchCommand(new BatchId("other")));
    }

    // ---- RetryBatchCommand --------------------------------------------

    [Fact]
    public void RetryBatchCommand_PreservesBatchId()
    {
        var cmd = new RetryBatchCommand(AnyBatch);
        cmd.BatchId.Should().Be(AnyBatch);
    }

    [Fact]
    public void RetryBatchCommand_DefaultBatchId_Throws()
    {
        var act = () => new RetryBatchCommand(default);
        act.Should().Throw<ArgumentException>().WithMessage("*default(BatchId)*");
    }
}
