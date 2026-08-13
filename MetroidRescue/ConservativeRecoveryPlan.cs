namespace MetroidRescue;

internal static class ConservativeRecoveryPlan
{
    public const string TargetSlot = "a";
    public static IReadOnlyList<(string Target, string Image)> Writes { get; } = FirmwareService.RescuePartitions
        .Select(image => (Target: image + "_" + TargetSlot, Image: image))
        .ToArray();
    public static string ImagePath(string image) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "images", image + ".img");

    public static async Task ExecuteAsync(Func<string, string, Task> flash, Func<Task> activateSlot)
    {
        foreach (var write in Writes) await flash(write.Target, write.Image);
        await activateSlot();
    }
}
