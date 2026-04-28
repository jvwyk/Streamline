using System.Collections.Immutable;
using AwesomeAssertions;
using Streamline.Core.ValueTypes;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Fakes;

public class FakeFileReaderTests
{
    private static readonly IReadOnlyList<string> Headers = ["broker_id", "name"];

    [Fact]
    public async Task GetHeadersAsync_ReturnsConfiguredHeaders()
    {
        var reader = FakeFileReader.WithRecords(Headers, []);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");

        var headers = await reader.GetHeadersAsync(mapping, "/tmp/any.csv");

        headers.Should().Equal("broker_id", "name");
    }

    [Fact]
    public async Task ReadRowsAsync_YieldsRowsInOrder()
    {
        var rows = new[] { Row(0, 1, "Acme"), Row(1, 2, "Globex") };
        var reader = FakeFileReader.WithRecords(Headers, rows);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");

        var collected = new List<Record>();
        await foreach (var row in reader.ReadRowsAsync(mapping, "/tmp/any.csv"))
        {
            collected.Add(row);
        }

        collected.Should().Equal(rows);
    }

    [Fact]
    public async Task ReadRowsAsync_StreamOverload_YieldsSameRows()
    {
        var rows = new[] { Row(0, 1, "Acme") };
        var reader = FakeFileReader.WithRecords(Headers, rows);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");
        using var stream = new MemoryStream();

        var collected = new List<Record>();
        await foreach (var row in reader.ReadRowsAsync(mapping, stream, "logical"))
        {
            collected.Add(row);
        }

        collected.Should().Equal(rows);
    }

    [Fact]
    public void CanRead_ReturnsTrueForMatchingFormat()
    {
        var reader = FakeFileReader.WithRecords(Headers, [], format: "xlsx");
        var mapping = new FileMapping(@"any\.xlsx", "broker", "xlsx");

        reader.CanRead(mapping, "/tmp/any.xlsx").Should().BeTrue();
    }

    [Fact]
    public void CanRead_ReturnsFalseForNonMatchingFormat()
    {
        var reader = FakeFileReader.WithRecords(Headers, [], format: "delimited");
        var mapping = new FileMapping(@"any\.xlsx", "broker", "xlsx");

        reader.CanRead(mapping, "/tmp/any.xlsx").Should().BeFalse();
    }

    [Fact]
    public async Task ThatThrowsOn_StopsAtConfiguredIndex()
    {
        var rows = new[] { Row(0, 1, "Acme"), Row(1, 2, "Globex"), Row(2, 3, "Initech") };
        var reader = FakeFileReader.ThatThrowsOn(Headers, rows, throwAtRowIndex: 1);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");

        var collected = new List<Record>();
        var act = async () =>
        {
            await foreach (var row in reader.ReadRowsAsync(mapping, "/tmp/any.csv"))
            {
                collected.Add(row);
            }
        };

        await act.Should().ThrowAsync<IOException>().WithMessage("*row index 1*");
        collected.Should().HaveCount(1);  // Only the first row was yielded.
    }

    [Fact]
    public async Task HangsAfter_HonorsCancellationToken()
    {
        var rows = new[] { Row(0, 1, "Acme"), Row(1, 2, "Globex") };
        var reader = FakeFileReader.HangsAfter(Headers, rows, hangAfterRowIndex: 0);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var collected = new List<Record>();
        var act = async () =>
        {
            await foreach (var row in reader.ReadRowsAsync(mapping, "/tmp/any.csv", cts.Token))
            {
                collected.Add(row);
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        collected.Should().HaveCount(1);
    }

    [Fact]
    public async Task WithMismatchedRecords_SkipsValidation()
    {
        // Record key "extra" isn't in declared headers — would fail
        // the Debug.Assert in normal construction. WithMismatchedRecords
        // is the path tests use for intentional mismatches (drift
        // detection scenarios).
        var rows = new[]
        {
            new Record("any.csv", 0,
                ImmutableDictionary<string, object?>.Empty
                    .Add("broker_id", 1)
                    .Add("extra", "bonus column")),
        };
        var reader = FakeFileReader.WithMismatchedRecords(Headers, rows);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");

        var collected = new List<Record>();
        await foreach (var row in reader.ReadRowsAsync(mapping, "/tmp/any.csv"))
        {
            collected.Add(row);
        }

        collected.Should().Equal(rows);
    }

    [Fact]
    public async Task ReadRowsAsync_PreCancelledToken_Throws()
    {
        var reader = FakeFileReader.WithRecords(Headers, [Row(0, 1, "Acme")]);
        var mapping = new FileMapping(@"any\.csv", "broker", "delimited");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in reader.ReadRowsAsync(mapping, "/tmp/any.csv", cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Record Row(long index, int brokerId, string name) =>
        new("any.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", brokerId)
                .Add("name", name));
}
