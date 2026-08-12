using MetroidRescue;

namespace MetroidRescue.Tests;

public class RecoveryPlanTests
{
    [Fact]
    public void ConservativePlanContainsOnlyExplicitSlotABootChainTargets()
    {
        Assert.Equal("a", ConservativeRecoveryPlan.TargetSlot);
        Assert.Equal(
            ["boot_a", "init_boot_a", "dtbo_a", "recovery_a", "vendor_boot_a", "vbmeta_a"],
            ConservativeRecoveryPlan.Writes.Select(write => write.Target));
        Assert.All(ConservativeRecoveryPlan.Writes, write => Assert.Contains(write.Image, FirmwareService.RescuePartitions));
    }
}
