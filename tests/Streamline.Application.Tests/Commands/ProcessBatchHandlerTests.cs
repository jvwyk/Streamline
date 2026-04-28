using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Commands;

public class ProcessBatchHandlerTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construct_NullStaging_Throws()
    {
        var act = () => new ProcessBatchHandler(
            null!,
            BuildOrchestrator(),
            Substitute.For<IObservationSink>(),
            Substitute.For<ILogger<ResilientObservationSink>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullOrchestrator_Throws()
    {
        var act = () => new ProcessBatchHandler(
            Substitute.For<IStagingRepository>(),
            null!,
            Substitute.For<IObservationSink>(),
            Substitute.For<ILogger<ResilientObservationSink>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullSink_Throws()
    {
        var act = () => new ProcessBatchHandler(
            Substitute.For<IStagingRepository>(),
            BuildOrchestrator(),
            null!,
            Substitute.For<ILogger<ResilientObservationSink>>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullLogger_Throws()
    {
        var act = () => new ProcessBatchHandler(
            Substitute.For<IStagingRepository>(),
            BuildOrchestrator(),
            Substitute.For<IObservationSink>(),
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_NullCommand_Throws()
    {
        var handler = BuildHandler(out _);
        var act = async () => await handler.HandleAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_BatchNotFound_ThrowsKeyNotFound()
    {
        var handler = BuildHandler(out var staging);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns((BatchSnapshot?)null);

        var act = async () => await handler.HandleAsync(new ProcessBatchCommand(AnyBatch));

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*batch-1*");
    }

    [Fact]
    public async Task Handle_RehydratesAggregateAndDelegatesToOrchestrator()
    {
        var handler = BuildHandler(out var staging);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(new BatchSnapshot(
                AnyBatch, "/data", BatchStatus.Ingested, AnyTime, CompletedAt: null));

        var result = await handler.HandleAsync(new ProcessBatchCommand(AnyBatch));

        // Empty registry → no tables but the batch should complete
        // (exercises BeginProcessing → Complete via the orchestrator).
        result.Tables.Should().BeEmpty();
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
    }

    [Fact]
    public async Task Handle_ProcessableBatch_OrchestratorTransitionsThroughProcessing()
    {
        var handler = BuildHandler(out var staging);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(new BatchSnapshot(
                AnyBatch, "/data", BatchStatus.Ingested, AnyTime, CompletedAt: null));

        // No registry entries — orchestrator runs through and
        // transitions Ingested → Processing → Completed.
        var result = await handler.HandleAsync(new ProcessBatchCommand(AnyBatch));

        result.Tables.Should().BeEmpty();
        // BATCH_COMPLETED comes from the aggregate's Complete() call
        // via the publisher; if the orchestrator didn't transition
        // properly, this observation wouldn't be emitted.
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
    }

    // ---- helpers ------------------------------------------------------

    private static ProcessingOrchestrator BuildOrchestrator() =>
        new(
            Substitute.For<IRegistryRepository>(),
            Substitute.For<IStagingRepository>(),
            Substitute.For<IDestinationAdapter>());

    private static ProcessBatchHandler BuildHandler(out IStagingRepository staging)
    {
        staging = Substitute.For<IStagingRepository>();
        var registry = Substitute.For<IRegistryRepository>();
        registry.GetLoadOrderAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var destination = Substitute.For<IDestinationAdapter>();
        var txn = Substitute.For<ITransactionScope>();
        destination.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(txn);
        var orchestrator = new ProcessingOrchestrator(registry, staging, destination);
        var sink = Substitute.For<IObservationSink>();
        sink.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();
        return new ProcessBatchHandler(staging, orchestrator, sink, logger);
    }
}
