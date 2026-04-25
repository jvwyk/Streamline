using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches;
using Xunit;

// Avoid the Xunit.Record collision; production type lives in Core.ValueTypes.
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Domain.Tests.Batches;

public class StagedRowTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    private static Record AnyRecord() =>
        new("file.csv", 0, new Dictionary<string, object?> { ["id"] = 1 });

    [Fact]
    public void Construct_WithValidArguments_Succeeds()
    {
        var record = AnyRecord();
        var row = new StagedRow(
            incomingId: 7,
            batchId: AnyBatch,
            targetTable: "broker",
            record: record,
            status: RowStatus.Pending,
            fileLogId: 3);

        row.IncomingId.Should().Be(7);
        row.BatchId.Should().Be(AnyBatch);
        row.TargetTable.Should().Be("broker");
        row.Record.Should().BeSameAs(record);
        row.Status.Should().Be(RowStatus.Pending);
        row.FileLogId.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construct_WithNonPositiveIncomingId_Throws(long incomingId)
    {
        var act = () => new StagedRow(incomingId, AnyBatch, "t", AnyRecord(), RowStatus.Pending, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_WithDefaultBatchId_Throws()
    {
        var act = () => new StagedRow(1, default, "t", AnyRecord(), RowStatus.Pending, 1);
        act.Should().Throw<ArgumentException>().WithMessage("*default(BatchId)*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_WithBlankTargetTable_Throws(string target)
    {
        var act = () => new StagedRow(1, AnyBatch, target, AnyRecord(), RowStatus.Pending, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithNullRecord_Throws()
    {
        var act = () => new StagedRow(1, AnyBatch, "t", null!, RowStatus.Pending, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construct_WithNonPositiveFileLogId_Throws(long fileLogId)
    {
        var act = () => new StagedRow(1, AnyBatch, "t", AnyRecord(), RowStatus.Pending, fileLogId);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var record = AnyRecord();
        var a = new StagedRow(1, AnyBatch, "t", record, RowStatus.Pending, 1);
        var b = new StagedRow(1, AnyBatch, "t", record, RowStatus.Pending, 1);
        var different = new StagedRow(2, AnyBatch, "t", record, RowStatus.Pending, 1);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }
}
