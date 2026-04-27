using AwesomeAssertions;
using NSubstitute;
using Streamline.Domain.Abstractions;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryFileReaderRegistryTests
{
    [Fact]
    public void ResolveReader_UnknownFormat_ReturnsNull()
    {
        var registry = new InMemoryFileReaderRegistry();
        registry.ResolveReader("delimited").Should().BeNull();
    }

    [Fact]
    public void ResolveReader_RegisteredFormat_ReturnsRegisteredReader()
    {
        var registry = new InMemoryFileReaderRegistry();
        var reader = FakeFileReader.WithRecords(["broker_id"], [], format: "delimited");
        registry.Register(reader);

        registry.ResolveReader("delimited").Should().BeSameAs(reader);
    }

    [Fact]
    public void Register_SameFormatTwice_Replaces()
    {
        var registry = new InMemoryFileReaderRegistry();
        var first = FakeFileReader.WithRecords(["a"], [], format: "delimited");
        var second = FakeFileReader.WithRecords(["b"], [], format: "delimited");
        registry.Register(first);
        registry.Register(second);

        registry.ResolveReader("delimited").Should().BeSameAs(second);
    }

    [Fact]
    public void IsContainer_NoDispatcher_ReturnsFalse()
    {
        var registry = new InMemoryFileReaderRegistry();
        registry.IsContainer("zip").Should().BeFalse();
    }

    [Fact]
    public void IsContainer_DispatcherRegistered_ReturnsTrue()
    {
        var registry = new InMemoryFileReaderRegistry();
        var dispatcher = Substitute.For<IFileDispatcher>();
        dispatcher.Format.Returns("zip");
        registry.Register(dispatcher);

        registry.IsContainer("zip").Should().BeTrue();
        registry.ResolveDispatcher("zip").Should().BeSameAs(dispatcher);
    }
}
