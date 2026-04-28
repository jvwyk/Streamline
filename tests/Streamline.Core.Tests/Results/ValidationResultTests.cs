using AwesomeAssertions;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Xunit;

// See RecordTests.cs — Xunit exposes its own Record type.
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Core.Tests.Results;

public class ValidationResultTests
{
    [Fact]
    public void Success_WithValidatedRecord_IsValidAndEmptyErrors()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["id"] = 1 });

        var result = ValidationResult.Success(record);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ValidatedRecord.Should().Be(record);
    }

    [Fact]
    public void Success_WithNullRecord_Throws()
    {
        var act = () => ValidationResult.Success(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Failure_WithErrors_IsInvalidAndCarriesNullRecord()
    {
        var errors = new[]
        {
            new ValidationError("amount", "abc", "INVALID_TYPE", "oops"),
        };

        var result = ValidationResult.Failure(errors);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.ValidatedRecord.Should().BeNull();
    }

    [Fact]
    public void Failure_WithNoErrors_Throws()
    {
        var act = () => ValidationResult.Failure([]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_WithNullErrors_Throws()
    {
        var act = () => ValidationResult.Failure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["id"] = 1 });

        var a = ValidationResult.Success(record);
        var b = ValidationResult.Success(record);
        a.Should().Be(b);

        var errors = new[] { new ValidationError("c", null, "X", "m") };
        var f1 = ValidationResult.Failure(errors);
        var f2 = ValidationResult.Failure(errors);
        f1.Should().Be(f2);
        f1.Should().NotBe(a);
    }
}
