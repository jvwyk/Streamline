using System.Collections.Immutable;
using AwesomeAssertions;
using NSubstitute;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class ScriptedTransformerTests
{
    [Fact]
    public async Task ExecuteAsync_DequeuesOutcomesInOrder()
    {
        var first = new TransformOutcome(1, 0, 0, 0, TimeSpan.Zero);
        var second = new TransformOutcome(2, 0, 0, 0, TimeSpan.Zero);
        var transformer = new ScriptedTransformer([first, second]);

        var a = await transformer.ExecuteAsync(MakeContext());
        var b = await transformer.ExecuteAsync(MakeContext());

        a.Should().Be(first);
        b.Should().Be(second);
    }

    [Fact]
    public async Task ExecuteAsync_Exhausted_Throws()
    {
        var transformer = new ScriptedTransformer([
            new TransformOutcome(1, 0, 0, 0, TimeSpan.Zero)]);
        await transformer.ExecuteAsync(MakeContext());

        var act = async () => await transformer.ExecuteAsync(MakeContext());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ScriptedTransformer exhausted*");
    }

    [Fact]
    public async Task Enqueue_AddsOutcomeToQueueIncrementally()
    {
        var transformer = new ScriptedTransformer();
        transformer.Enqueue(new TransformOutcome(5, 0, 0, 0, TimeSpan.Zero));

        var outcome = await transformer.ExecuteAsync(MakeContext());

        outcome.RowsInserted.Should().Be(5);
    }

    [Fact]
    public void Construct_NullOutcomeInList_Throws()
    {
        var act = () => new ScriptedTransformer([null!]);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_TracksInvocationCountAndLastContext()
    {
        var transformer = new ScriptedTransformer([
            new TransformOutcome(1, 0, 0, 0, TimeSpan.Zero),
            new TransformOutcome(2, 0, 0, 0, TimeSpan.Zero),
        ]);
        var context = MakeContext();

        await transformer.ExecuteAsync(MakeContext());
        await transformer.ExecuteAsync(context);

        transformer.ForTestingOnly_InvocationCount.Should().Be(2);
        transformer.ForTestingOnly_LastContext.Should().BeSameAs(context);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_Throws()
    {
        var transformer = new ScriptedTransformer([
            new TransformOutcome(1, 0, 0, 0, TimeSpan.Zero)]);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await transformer.ExecuteAsync(MakeContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BatchContext MakeContext() =>
        new(
            new BatchId("batch-1"),
            ImmutableArray<long>.Empty,
            "staging",
            Substitute.For<ILineageWriter>());
}
