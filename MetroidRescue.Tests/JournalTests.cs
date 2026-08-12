using MetroidRescue;

namespace MetroidRescue.Tests;

public class JournalTests
{
    [Fact]
    public void CompletedSessionIsNotResumed()
    {
        var journal = new RescueJournal();
        var device = new FastbootDevice("test-complete", "metroid", "a", true, FastbootMode.Bootloader, "bl", "");
        var firmware = new FirmwareInfo("file", "Metroid_Test", "Test", "hash-complete", 1, false, true, "");
        journal.Record("session", "started", device, firmware);
        journal.Record("flash:boot_a", "completed", device, firmware);
        journal.Record("session", "completed", device, firmware);
        Assert.Empty(journal.ResumableSteps(device.Serial, firmware.Sha256));
    }

    [Fact]
    public void InterruptedSessionReturnsCompletedFlashSteps()
    {
        var journal = new RescueJournal();
        var device = new FastbootDevice("test-interrupted", "metroid", "a", true, FastbootMode.Bootloader, "bl", "");
        var firmware = new FirmwareInfo("file", "Metroid_Test", "Test", "hash-interrupted", 1, false, true, "");
        journal.Record("session", "started", device, firmware);
        journal.Record("flash:boot_a", "completed", device, firmware);
        Assert.Contains("flash:boot_a", journal.ResumableSteps(device.Serial, firmware.Sha256));
    }
}
