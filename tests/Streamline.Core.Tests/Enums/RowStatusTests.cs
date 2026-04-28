using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class RowStatusTests
{
    /// <summary>
    /// Guards against accidental renames or removals. Persistence (Phase 2)
    /// and the row state machine (sub-phase 1d) both depend on this stable
    /// set of names.
    /// </summary>
    [Fact]
    public void Values_are_the_five_documented_row_states()
    {
        Enum.GetNames<RowStatus>().Should().BeEquivalentTo(
        [
            nameof(RowStatus.Pending),
            nameof(RowStatus.Processing),
            nameof(RowStatus.Committed),
            nameof(RowStatus.RolledBack),
            nameof(RowStatus.Quarantined),
        ]);
    }
}
