using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Observations;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Commands;

public class IngestBatchHandlerTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    [Fact]
    public void Construct_NullStaging_Throws()
    {
        var act = () => new IngestBatchHandler(
            null!,
            BuildOrchestrator(),
            Substitute.For<IObservationSink>(),
            Substitute.For<ILogger<ResilientObservationSink>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullOrchestrator_Throws()
    {
        var act = () => new IngestBatchHandler(
            Substitute.For<IStagingRepository>(),
            null!,
            Substitute.For<IObservationSink>(),
            Substitute.For<ILogger<ResilientObservationSink>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullSink_Throws()
    {
        var act = () => new IngestBatchHandler(
            Substitute.For<IStagingRepository>(),
            BuildOrchestrator(),
            null!,
            Substitute.For<ILogger<ResilientObservationSink>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullLogger_Throws()
    {
        var act = () => new IngestBatchHandler(
            Substitute.For<IStagingRepository>(),
            BuildOrchestrator(),
            Substitute.For<IObservationSink>(),
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        var handler = BuildHandler(out _, out _, _ => []);
        var act = async () => await handler.HandleAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_StartsBatchAndPassesIdToAggregate()
    {
        var handler = BuildHandler(out var staging, out _, dir => []);
        staging.StartBatchAsync("/data", Arg.Any<CancellationToken>())
            .Returns(AnyBatch);

        var result = await handler.HandleAsync(new IngestBatchCommand("/data"));

        await staging.Received(1).StartBatchAsync("/data", Arg.Any<CancellationToken>());
        result.Files.Should().BeEmpty();
        // BATCH_STARTED carries the batch id in its message context;
        // verify the aggregate was created with the staging-assigned id.
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.BATCH_STARTED && o.BatchId == AnyBatch.Value);
    }

    [Fact]
    public async Task Handle_FeedsEnumeratedFilesIntoOrchestrator()
    {
        // Wire a mapping repo that returns null for everything so the
        // orchestrator processes the files but produces no outcomes —
        // we only care that file enumeration was invoked.
        var enumerated = new List<string>();
        var handler = BuildHandler(
            out var staging,
            out var fileMappings,
            dir => { enumerated.AddRange(["/data/a.csv", "/data/b.csv"]); return ["/data/a.csv", "/data/b.csv"]; });
        staging.StartBatchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AnyBatch);

        await handler.HandleAsync(new IngestBatchCommand("/data"));

        enumerated.Should().Equal("/data/a.csv", "/data/b.csv");
        await fileMappings.Received(1).ResolveAsync("a.csv", Arg.Any<CancellationToken>());
        await fileMappings.Received(1).ResolveAsync("b.csv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesDefaultEnumerator_WhenOmitted()
    {
        // Smoke test the default path: create a real temp dir with a
        // file, ensure the handler enumerates it. We don't care about
        // the orchestrator outcome — the mapping returns null so the
        // file is silently skipped.
        var dir = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "streamline-ingest-handler-" + Guid.NewGuid().ToString("N")));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "x.csv"), "noop");

            var staging = Substitute.For<IStagingRepository>();
            staging.StartBatchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(AnyBatch);
            var fileMappings = Substitute.For<IFileMappingRepository>();
            var registry = Substitute.For<IRegistryRepository>();
            var readers = Substitute.For<IFileReaderRegistry>();
            var orchestrator = new IngestionOrchestrator(fileMappings, registry, readers, staging);
            var sink = Substitute.For<IObservationSink>();
            sink.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var logger = Substitute.For<ILogger<ResilientObservationSink>>();

            var handler = new IngestBatchHandler(staging, orchestrator, sink, logger);
            // Should not throw; default enumerator picks up x.csv;
            // mapping returns null so it's skipped.
            await handler.HandleAsync(new IngestBatchCommand(dir.FullName));

            await fileMappings.Received(1).ResolveAsync("x.csv", Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    // ---- helpers ------------------------------------------------------

    private static IngestionOrchestrator BuildOrchestrator() =>
        new(
            Substitute.For<IFileMappingRepository>(),
            Substitute.For<IRegistryRepository>(),
            Substitute.For<IFileReaderRegistry>(),
            Substitute.For<IStagingRepository>());

    private static IngestBatchHandler BuildHandler(
        out IStagingRepository staging,
        out IFileMappingRepository fileMappings,
        Func<string, IReadOnlyList<string>> enumerate)
    {
        staging = Substitute.For<IStagingRepository>();
        fileMappings = Substitute.For<IFileMappingRepository>();
        var registry = Substitute.For<IRegistryRepository>();
        var readers = Substitute.For<IFileReaderRegistry>();
        var orchestrator = new IngestionOrchestrator(fileMappings, registry, readers, staging);
        var sink = Substitute.For<IObservationSink>();
        sink.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();
        return new IngestBatchHandler(staging, orchestrator, sink, logger, enumerate);
    }
}
