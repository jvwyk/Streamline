using AwesomeAssertions;
using NSubstitute;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Validation;
using Xunit;

namespace Streamline.Domain.Tests.Validation;

public class FkResolverTests
{
    private static FkReference Fk(
        string parent = "broker",
        string column = "broker_id",
        FkEnforcementMode mode = FkEnforcementMode.Always) =>
        new(parent, column, mode);

    private static ITransactionScope ScopeReturning(params string[] values)
    {
        var scope = Substitute.For<ITransactionScope>();
        scope.GetDistinctColumnValuesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(values.AsReadOnlyCollection());
        return scope;
    }

    // ---- LoadAsync ---------------------------------------------------

    [Fact]
    public async Task LoadAsync_QueriesParentSchemaTableColumn_AndStoresResult()
    {
        var scope = ScopeReturning("BRK-001", "BRK-002");
        var fk = Fk(parent: "broker", column: "broker_id");
        var resolver = new FkResolver();

        await resolver.LoadAsync(fk, parentSchema: "intembeko", scope);

        await scope.Received(1).GetDistinctColumnValuesAsync(
            "intembeko", "broker", "broker_id", Arg.Any<CancellationToken>());
        resolver.IsLoaded(fk).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_AcceptsZeroValues()
    {
        var scope = ScopeReturning();
        var fk = Fk();
        var resolver = new FkResolver();

        await resolver.LoadAsync(fk, "intembeko", scope);

        resolver.IsLoaded(fk).Should().BeTrue();
        resolver.IsEmpty(fk).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_ReloadOverwritesCache()
    {
        var scope1 = ScopeReturning("OLD");
        var scope2 = ScopeReturning("NEW");
        var fk = Fk();
        var resolver = new FkResolver();

        await resolver.LoadAsync(fk, "s", scope1);
        await resolver.LoadAsync(fk, "s", scope2);

        resolver.Validate(fk, "OLD").Should().BeFalse();
        resolver.Validate(fk, "NEW").Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task LoadAsync_BlankSchema_Throws(string schema)
    {
        var scope = ScopeReturning();
        var resolver = new FkResolver();

        var act = async () => await resolver.LoadAsync(Fk(), schema, scope);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_NullScope_Throws()
    {
        var resolver = new FkResolver();
        var act = async () => await resolver.LoadAsync(Fk(), "s", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- IsLoaded / IsEmpty ------------------------------------------

    [Fact]
    public void IsLoaded_NotLoaded_False()
    {
        new FkResolver().IsLoaded(Fk()).Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_NotLoaded_False()
    {
        // Documented contract: not-loaded returns false (not throw).
        new FkResolver().IsEmpty(Fk()).Should().BeFalse();
    }

    [Fact]
    public async Task IsEmpty_LoadedWithValues_False()
    {
        var resolver = new FkResolver();
        await resolver.LoadAsync(Fk(), "s", ScopeReturning("X"));

        resolver.IsEmpty(Fk()).Should().BeFalse();
    }

    // ---- Validate: throws on un-loaded -------------------------------

    [Fact]
    public void Validate_UnloadedFk_ThrowsInvalidOperation()
    {
        var resolver = new FkResolver();
        var act = () => resolver.Validate(Fk(), "any");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has not been loaded*");
    }

    // ---- Validate matrix: 3 modes × scenarios ------------------------

    public static IEnumerable<object[]> ValidateMatrix() =>
    [
        // Always mode
        [FkEnforcementMode.Always,             /*populated*/ true,  /*value*/ "BRK-001",  /*expected*/ true],
        [FkEnforcementMode.Always,             /*populated*/ true,  /*value*/ "MISSING",  /*expected*/ false],
        [FkEnforcementMode.Always,             /*populated*/ false, /*value*/ "ANY",      /*expected*/ false],

        // WhenParentPopulated mode
        [FkEnforcementMode.WhenParentPopulated, /*populated*/ true,  /*value*/ "BRK-001",  /*expected*/ true],
        [FkEnforcementMode.WhenParentPopulated, /*populated*/ true,  /*value*/ "MISSING",  /*expected*/ false],
        [FkEnforcementMode.WhenParentPopulated, /*populated*/ false, /*value*/ "ANY",      /*expected*/ true],

        // Never mode
        [FkEnforcementMode.Never,              /*populated*/ true,  /*value*/ "MISSING",  /*expected*/ true],
        [FkEnforcementMode.Never,              /*populated*/ false, /*value*/ "ANY",      /*expected*/ true],
    ];

    [Theory]
    [MemberData(nameof(ValidateMatrix))]
    public async Task Validate_HonorsEnforcementMode(
        FkEnforcementMode mode, bool populated, string value, bool expected)
    {
        var fk = Fk(mode: mode);
        var scope = populated ? ScopeReturning("BRK-001", "BRK-002") : ScopeReturning();
        var resolver = new FkResolver();
        await resolver.LoadAsync(fk, "intembeko", scope);

        resolver.Validate(fk, value).Should().Be(expected);
    }

    // ---- Null value: passes regardless of mode -----------------------

    [Theory]
    [InlineData(FkEnforcementMode.Always)]
    [InlineData(FkEnforcementMode.WhenParentPopulated)]
    [InlineData(FkEnforcementMode.Never)]
    public async Task Validate_NullValue_PassesRegardlessOfMode(FkEnforcementMode mode)
    {
        var fk = Fk(mode: mode);
        var resolver = new FkResolver();
        await resolver.LoadAsync(fk, "s", ScopeReturning("BRK-001"));

        resolver.Validate(fk, null).Should().BeTrue();
    }

    // ---- Cache isolation across multiple FK references ---------------

    [Fact]
    public async Task Cache_DoesNotBleedBetweenFkReferences()
    {
        var fkA = Fk(parent: "broker", column: "broker_id");
        var fkB = Fk(parent: "region", column: "region_code");

        var scopeA = ScopeReturning("BRK-001", "BRK-002");
        var scopeB = ScopeReturning("EU", "US");

        var resolver = new FkResolver();
        await resolver.LoadAsync(fkA, "s", scopeA);
        await resolver.LoadAsync(fkB, "s", scopeB);

        // FK A's cache values should NOT match FK B's reference, and
        // vice versa.
        resolver.Validate(fkA, "BRK-001").Should().BeTrue();
        resolver.Validate(fkA, "EU").Should().BeFalse();

        resolver.Validate(fkB, "EU").Should().BeTrue();
        resolver.Validate(fkB, "BRK-001").Should().BeFalse();
    }

    [Fact]
    public async Task Cache_LookupIsCaseSensitive()
    {
        // Postgres text columns are case-sensitive by default; cache
        // mirrors that. "brk-001" must not match "BRK-001".
        var resolver = new FkResolver();
        await resolver.LoadAsync(Fk(), "s", ScopeReturning("BRK-001"));

        resolver.Validate(Fk(), "BRK-001").Should().BeTrue();
        resolver.Validate(Fk(), "brk-001").Should().BeFalse();
    }
}

internal static class TestExtensions
{
    public static IReadOnlyCollection<string> AsReadOnlyCollection(this string[] values) =>
        values;
}
