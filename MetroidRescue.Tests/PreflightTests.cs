using MetroidRescue;

namespace MetroidRescue.Tests;

public class PreflightTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("none", true)]
    [InlineData("merging", false)]
    [InlineData("snapshotted", false)]
    public void SnapshotGateBlocksActiveUpdates(string? state, bool expected)
        => Assert.Equal(expected, PreflightService.IsSnapshotSafe(state));
}
