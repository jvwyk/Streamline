using System.Diagnostics.CodeAnalysis;

namespace Streamline.Core.Enums;

/// <summary>
/// The value domain of a registry column. The engine coerces incoming
/// string values to the corresponding .NET type during validation; a
/// value that cannot be coerced becomes <c>INVALID_TYPE</c>. Per plan
/// §5.2 initial set.
///
/// Values are intentionally named after their mapped .NET types
/// (<c>String</c>, <c>Integer</c>, <c>Decimal</c>, etc.). The naming
/// documents the type mapping at the call site; see the CA1720
/// suppression below for the justification.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The enum names are intentionally the .NET type names they " +
                    "correspond to. Renaming (e.g. Text/Int32/Decimal128) hides " +
                    "the mapping the plan §5.2 specifies.")]
public enum ColumnTypeCode
{
    /// <summary>UTF-8 text. Optionally bounded by a max length.</summary>
    String,

    /// <summary>32-bit signed integer (<see cref="int"/>).</summary>
    Integer,

    /// <summary>64-bit signed integer (<see cref="long"/>).</summary>
    BigInt,

    /// <summary>Arbitrary-precision decimal (<see cref="decimal"/>).</summary>
    Decimal,

    /// <summary>Calendar date with no time component (<see cref="DateOnly"/>).</summary>
    Date,

    /// <summary>Instant in UTC (<see cref="DateTimeOffset"/> or <see cref="DateTime"/>).</summary>
    Timestamp,

    /// <summary>Boolean true/false (<see cref="bool"/>).</summary>
    Boolean,

    /// <summary>Universally unique identifier (<see cref="Guid"/>).</summary>
    Uuid,
}
