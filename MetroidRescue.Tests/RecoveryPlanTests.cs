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

    [Fact]
    public async Task ActivatesSlotOnlyAfterEveryImageSucceedsInOrder()
    {
        var events = new List<string>();

        await ConservativeRecoveryPlan.ExecuteAsync(
            (target, _) => { events.Add("flash:" + target); return Task.CompletedTask; },
            () => { events.Add("activate:a"); return Task.CompletedTask; });

        Assert.Equal([
            "flash:boot_a", "flash:init_boot_a", "flash:dtbo_a", "flash:recovery_a", "flash:vendor_boot_a", "flash:vbmeta_a", "activate:a"
        ], events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task FailureAtAnyImageNeverActivatesSlot(int failureIndex)
    {
        var attempt = 0;
        var activated = false;

        await Assert.ThrowsAsync<IOException>(() => ConservativeRecoveryPlan.ExecuteAsync(
            (_, _) => attempt++ == failureIndex ? throw new IOException("simulated disconnect") : Task.CompletedTask,
            () => { activated = true; return Task.CompletedTask; }));

        Assert.False(activated);
        Assert.Equal(failureIndex + 1, attempt);
    }

    [Fact]
    public async Task RetryAfterFailureReplaysAllImages()
    {
        var firstAttempt = 0;
        await Assert.ThrowsAsync<IOException>(() => ConservativeRecoveryPlan.ExecuteAsync(
            (_, _) => firstAttempt++ == 2 ? throw new IOException("simulated disconnect") : Task.CompletedTask,
            () => Task.CompletedTask));

        var replayed = new List<string>();
        await ConservativeRecoveryPlan.ExecuteAsync(
            (target, _) => { replayed.Add(target); return Task.CompletedTask; },
            () => Task.CompletedTask);

        Assert.Equal(ConservativeRecoveryPlan.Writes.Select(write => write.Target), replayed);
    }
}
