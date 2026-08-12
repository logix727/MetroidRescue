using MetroidRescue;

namespace MetroidRescue.Tests;

public class FastbootGuardTests
{
    [Fact]
    public async Task GuardRunsWriteOnlyAfterFreshExactVerification()
    {
        var commands = new List<string>();
        var service = Service(commands, product: "metroid", unlocked: "yes", userspace: "no");

        var image = ConservativeRecoveryPlan.ImagePath("boot");
        var result = await service.RunRecoveryCommandAsync("SERIAL", ["flash", "boot_a", image]);

        Assert.True(result.Success);
        Assert.Equal(["devices", "-s SERIAL getvar all", $"-s SERIAL flash boot_a {image}"], commands);
    }

    [Theory]
    [InlineData("spacewar", "yes", "no")]
    [InlineData("metroid", "no", "no")]
    [InlineData("metroid", "yes", "yes")]
    [InlineData("metroid", "yes", "")]
    public async Task GuardBlocksUnsafeDeviceStateBeforeWrite(string product, string unlocked, string userspace)
    {
        var commands = new List<string>();
        var service = Service(commands, product, unlocked, userspace);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunRecoveryCommandAsync("SERIAL", ["flash", "boot_a", ConservativeRecoveryPlan.ImagePath("boot")]));

        Assert.DoesNotContain(commands, command => command.Contains(" flash ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DetectionRejectsAmbiguousDeviceSelection()
    {
        var service = new FastbootService(_ => { }, (args, _) => Task.FromResult(
            args.SequenceEqual(["devices"])
                ? new CommandResult(0, "ONE\tfastboot\nTWO\tfastboot")
                : new CommandResult(0, "")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DetectAsync());
    }

    [Theory]
    [InlineData("erase", "userdata")]
    [InlineData("flash", "system_a", "system.img")]
    [InlineData("set_active", "b")]
    [InlineData("flashing", "unlock")]
    [InlineData("delete-logical-partition", "system_a")]
    public async Task GuardRejectsCommandsOutsideConservativePlan(params string[] command)
    {
        var commands = new List<string>();
        var service = Service(commands, "metroid", "yes", "no");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunRecoveryCommandAsync("SERIAL", command));

        Assert.Empty(commands);
    }

    [Fact]
    public async Task RawExecutorRejectsDestructiveCommands()
    {
        var service = Service([], "metroid", "yes", "no");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.RunAsync(["-s", "SERIAL", "erase", "userdata"]));
    }

    [Fact]
    public async Task GuardRejectsMismatchedImageFilename()
    {
        var service = Service([], "metroid", "yes", "no");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunRecoveryCommandAsync("SERIAL", ["flash", "boot_a", ConservativeRecoveryPlan.ImagePath("system")]));
    }

    private static FastbootService Service(List<string> commands, string product, string unlocked, string userspace) => new(_ => { }, (args, _) =>
    {
        commands.Add(string.Join(' ', args));
        if (args.SequenceEqual(["devices"])) return Task.FromResult(new CommandResult(0, "SERIAL\tfastboot"));
        if (args.SequenceEqual(["-s", "SERIAL", "getvar", "all"]))
            return Task.FromResult(new CommandResult(0, $"product: {product}\nunlocked: {unlocked}\nis-userspace: {userspace}\ncurrent-slot: a"));
        return Task.FromResult(new CommandResult(0, "OKAY"));
    });
}
