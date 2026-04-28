using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Registry;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Domain.Tests.Registry;

public class RegistryEntryTests
{
    private static SchemaDefinition AnySchema() =>
        new(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt) { IsRequired = true, IsPrimaryKey = true },
            new ColumnDefinition("name", ColumnTypeCode.String),
        ]);

    [Fact]
    public void Construct_WithRequiredFields_DefaultsAreSensible()
    {
        var entry = new RegistryEntry("broker", AnySchema());

        entry.TableName.Should().Be("broker");
        entry.Schema.Should().NotBeNull();
        entry.TargetSchema.Should().BeNull();
        entry.Transform.Should().BeNull();
        entry.NewColumnsDriftPolicy.Should().Be(DriftPolicy.Warn);
        entry.MissingRequiredDriftPolicy.Should().Be(DriftPolicy.Block);
        entry.MissingOptionalDriftPolicy.Should().Be(DriftPolicy.Warn);
        entry.DependsOn.Should().BeEmpty();
        entry.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankTableName_Throws(string tableName)
    {
        var act = () => new RegistryEntry(tableName, AnySchema());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithNullSchema_Throws()
    {
        var act = () => new RegistryEntry("broker", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsReplication_TrueWhenTransformIsNull()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            TargetSchema = "intembeko",
        };

        entry.IsReplication.Should().BeTrue();
        entry.IsTransform.Should().BeFalse();
    }

    [Fact]
    public void IsTransform_TrueWhenTransformIsSet()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            Transform = new TransformReference(
                TransformKind.SqlFunction, "domain.fn", "intembeko.dim_broker", TransformInvocation.PerBatch),
        };

        entry.IsReplication.Should().BeFalse();
        entry.IsTransform.Should().BeTrue();
    }

    [Fact]
    public void DescribeInvariantViolations_ReplicationWithoutTargetSchema_Reports()
    {
        var entry = new RegistryEntry("broker", AnySchema());
        // Transform is null, TargetSchema is null → replication needs target.

        entry.DescribeInvariantViolations()
            .Should().ContainSingle()
            .Which.Should().Contain("must declare a TargetSchema");
    }

    [Fact]
    public void DescribeInvariantViolations_ReplicationWithTargetSchema_NoViolation()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            TargetSchema = "intembeko",
        };

        entry.DescribeInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void DescribeInvariantViolations_TransformWithoutTargetSchema_NoViolation()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            Transform = new TransformReference(
                TransformKind.SqlFunction, "domain.fn", "intembeko.dim_broker", TransformInvocation.PerBatch),
        };

        entry.DescribeInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void DescribeInvariantViolations_ValidToBeforeValidFrom_Reports()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            TargetSchema = "intembeko",
            ValidFrom = new DateOnly(2025, 1, 15),
            ValidTo = new DateOnly(2025, 1, 1),
        };

        entry.DescribeInvariantViolations().Should().Contain(v => v.Contains("ValidTo"));
    }

    [Fact]
    public void DescribeInvariantViolations_DependsOnSelf_Reports()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            TargetSchema = "intembeko",
            DependsOn = ["broker"],
        };

        entry.DescribeInvariantViolations().Should().Contain(v => v.Contains("references itself"));
    }

    [Fact]
    public void DescribeInvariantViolations_BlankDependsOnEntry_Reports()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            TargetSchema = "intembeko",
            DependsOn = [""],
        };

        entry.DescribeInvariantViolations().Should().Contain(v => v.Contains("blank entry"));
    }

    [Fact]
    public void DescribeInvariantViolations_AccumulatesAcrossDimensions()
    {
        var entry = new RegistryEntry("broker", AnySchema())
        {
            // No TargetSchema → replication violation
            // ValidTo before ValidFrom → date violation
            // Self-reference in DependsOn → graph violation
            ValidFrom = new DateOnly(2025, 1, 15),
            ValidTo = new DateOnly(2025, 1, 1),
            DependsOn = ["broker"],
        };

        var violations = entry.DescribeInvariantViolations().ToArray();

        violations.Should().HaveCount(3);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var schema = AnySchema();
        var a = new RegistryEntry("broker", schema)
        {
            TargetSchema = "intembeko",
            DependsOn = ["region"],
        };
        var b = new RegistryEntry("broker", schema)
        {
            TargetSchema = "intembeko",
            DependsOn = ["region"],
        };
        var differentDeps = new RegistryEntry("broker", schema)
        {
            TargetSchema = "intembeko",
            DependsOn = ["region", "country"],
        };

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(differentDeps);
    }
}
