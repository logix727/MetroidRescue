using MetroidRescue;

namespace MetroidRescue.Tests;

public class TransportParsingTests
{
    [Fact]
    public void ParsesOnlyExactFastbootRows()
    {
        var serials = FastbootService.ParseDeviceSerials("ABC\tfastboot\nABC-extra\tfastboot\nnoise\nABC-device\tdevice\n");
        Assert.Equal(["ABC", "ABC-extra"], serials);
    }

    [Fact]
    public void AdbTransportRequiresExactSerialAndState()
    {
        const string output = "ABC-extra\tdevice\nABC\tunauthorized\n";
        Assert.False(RecoveryMonitor.HasTransport(output, "ABC", "device"));
        Assert.True(RecoveryMonitor.HasTransport(output, "ABC", "unauthorized"));
    }
}
