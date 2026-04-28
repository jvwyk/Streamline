using System.Collections.Immutable;
using AwesomeAssertions;
using NSubstitute;
using Streamline.Core.Enums;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryTransformerRegistryTests
{
    private static readonly TransformReference AnyRef = new(
        TransformKind.SqlFunction, "domain.calc", "intembeko.dim_broker", TransformInvocation.PerBatch);

    [Fact]
    public async Task InvokeAsync_UnknownReference_Throws()
    {
        var registry = new InMemoryTransformerRegistry();
        var act = async () => await registry.InvokeAsync(AnyRef, MakeContext());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*domain.calc*");
    }

    [Fact]
    public async Task InvokeAsync_RegisteredReference_DispatchesToTransformer()
    {
        var registry = new InMemoryTransformerRegistry();
        var transformer = new ScriptedTransformer([
            new TransformOutcome(10, 0, 0, 0, TimeSpan.FromMilliseconds(5))]);
        registry.Register(AnyRef.Reference, transformer);

        var outcome = await registry.InvokeAsync(AnyRef, MakeContext());

        outcome.RowsInserted.Should().Be(10);
        transformer.ForTestingOnly_InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task Register_SameReferenceTwice_ReplacesPreviousBinding()
    {
        var registry = new InMemoryTransformerRegistry();
        var first = new ScriptedTransformer([
            new TransformOutcome(1, 0, 0, 0, TimeSpan.Zero)]);
        var second = new ScriptedTransformer([
            new TransformOutcome(2, 0, 0, 0, TimeSpan.Zero)]);
        registry.Register(AnyRef.Reference, first);
        registry.Register(AnyRef.Reference, second);

        var outcome = await registry.InvokeAsync(AnyRef, MakeContext());

        outcome.RowsInserted.Should().Be(2);
        first.ForTestingOnly_InvocationCount.Should().Be(0);
        second.ForTestingOnly_InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_PassesContextThrough()
    {
        var registry = new InMemoryTransformerRegistry();
        var transformer = new ScriptedTransformer([
            new TransformOutcome(0, 0, 0, 0, TimeSpan.Zero)]);
        registry.Register(AnyRef.Reference, transformer);
        var context = MakeContext();

        await registry.InvokeAsync(AnyRef, context);

        transformer.ForTestingOnly_LastContext.Should().BeSameAs(context);
    }

    [Fact]
    public async Task InvokeAsync_PreCancelledToken_Throws()
    {
        var registry = new InMemoryTransformerRegistry();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await registry.InvokeAsync(AnyRef, MakeContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BatchContext MakeContext() =>
        new(
            new BatchId("batch-1"),
            ImmutableArray<long>.Empty,
            "staging",
            Substitute.For<ILineageWriter>());
}
