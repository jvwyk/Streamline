using AwesomeAssertions;
using Streamline.Core.Observations;
using Xunit;

namespace Streamline.Core.Tests.Observations;

public class ObservationTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construct_WithRequiredFieldsOnly_Succeeds()
    {
        var obs = new Observation(
            severity: ObservationSeverity.Info,
            code: "FILE_INGESTED",
            message: "File staged successfully.",
            batchId: "batch-1",
            raisedAt: FixedTime);

        obs.Severity.Should().Be(ObservationSeverity.Info);
        obs.Code.Should().Be("FILE_INGESTED");
        obs.Message.Should().Be("File staged successfully.");
        obs.BatchId.Should().Be("batch-1");
        obs.RaisedAt.Should().Be(FixedTime);
        obs.Context.Should().BeEmpty();
        obs.FileLogId.Should().BeNull();
        obs.IncomingId.Should().BeNull();
        obs.TableName.Should().BeNull();
    }

    [Fact]
    public void Construct_WithContext_PreservesEntries()
    {
        var obs = new Observation(
            ObservationSeverity.Warning,
            "SCHEMA_DRIFT_NEW_COLUMN",
            "New column detected.",
            "batch-1",
            FixedTime,
            new Dictionary<string, object?>
            {
                ["new_column_count"] = 2,
                ["source_file"] = "brokers.csv",
            });

        obs.Context.Should().HaveCount(2);
        obs.Context["source_file"].Should().Be("brokers.csv");
        obs.Context["new_column_count"].Should().Be(2);
    }

    [Fact]
    public void Construct_WithNullContextValue_Accepts()
    {
        // Null values are legitimate "this field is absent" sentinels.
        var obs = new Observation(
            ObservationSeverity.Error,
            "MISSING_REQUIRED",
            "required field was null",
            "batch-1",
            FixedTime,
            new Dictionary<string, object?> { ["column"] = "amount", ["value"] = null });

        obs.Context["value"].Should().BeNull();
        obs.Context.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankCode_Throws(string code)
    {
        var act = () => new Observation(
            ObservationSeverity.Info, code, "m", "batch-1", FixedTime);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankMessage_Throws(string message)
    {
        var act = () => new Observation(
            ObservationSeverity.Info, "CODE", message, "batch-1", FixedTime);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankBatchId_Throws(string batchId)
    {
        var act = () => new Observation(
            ObservationSeverity.Info, "CODE", "m", batchId, FixedTime);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankContextKey_Throws(string key)
    {
        var ctx = new[] { KeyValuePair.Create<string, object?>(key, "value") };

        var act = () => new Observation(
            ObservationSeverity.Info, "C", "m", "b", FixedTime, ctx);
        act.Should().Throw<ArgumentException>().WithMessage("*blank*");
    }

    [Fact]
    public void Construct_WithDuplicateContextKey_Throws()
    {
        var ctx = new[]
        {
            KeyValuePair.Create<string, object?>("k", 1),
            KeyValuePair.Create<string, object?>("k", 2),
        };

        var act = () => new Observation(
            ObservationSeverity.Info, "C", "m", "b", FixedTime, ctx);
        act.Should().Throw<ArgumentException>().WithMessage("*k*");
    }

    [Fact]
    public void Init_ScopeFieldsPropagate()
    {
        var obs = new Observation(ObservationSeverity.Error, "C", "m", "b", FixedTime)
        {
            FileLogId = 10,
            IncomingId = 42,
            TableName = "broker",
        };

        obs.FileLogId.Should().Be(10);
        obs.IncomingId.Should().Be(42);
        obs.TableName.Should().Be("broker");
    }

    [Fact]
    public void Init_NegativeFileLogId_Throws()
    {
        var act = () => new Observation(ObservationSeverity.Info, "C", "m", "b", FixedTime)
        {
            FileLogId = -1,
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Init_NegativeIncomingId_Throws()
    {
        var act = () => new Observation(ObservationSeverity.Info, "C", "m", "b", FixedTime)
        {
            IncomingId = -1,
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Init_BlankTableName_Throws(string tableName)
    {
        var act = () => new Observation(ObservationSeverity.Info, "C", "m", "b", FixedTime)
        {
            TableName = tableName,
        };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new Observation(ObservationSeverity.Info, "C", "m", "b", FixedTime)
        {
            TableName = "broker",
        };
        var b = new Observation(ObservationSeverity.Info, "C", "m", "b", FixedTime)
        {
            TableName = "broker",
        };
        var different = new Observation(ObservationSeverity.Info, "C", "m", "b", FixedTime)
        {
            TableName = "trade",
        };

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }

    /// <summary>
    /// Regression guard: context equality must not depend on insertion
    /// order of the underlying ImmutableDictionary — same rationale as
    /// RecordTests and FileMappingTests.
    /// </summary>
    [Fact]
    public void Equality_IgnoresInsertionOrderOfContext()
    {
        var ascending = new Observation(
            ObservationSeverity.Warning, "X", "m", "b", FixedTime,
            new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, ["c"] = 3 });
        var descending = new Observation(
            ObservationSeverity.Warning, "X", "m", "b", FixedTime,
            new Dictionary<string, object?> { ["c"] = 3, ["b"] = 2, ["a"] = 1 });

        ascending.Should().Be(descending);
        ascending.GetHashCode().Should().Be(descending.GetHashCode());
    }
}
