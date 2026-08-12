using MetroidRescue;

namespace MetroidRescue.Tests;

public class LiveCatalogTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task CurrentCatalogProvidesAuthenticatedBootChainForEveryFullOta()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("METROID_RESCUE_LIVE_TESTS"), "true", StringComparison.OrdinalIgnoreCase)) return;

        var catalog = new FirmwareCatalogService(_ => { });
        var entries = await catalog.GetAsync();

        Assert.NotEmpty(entries);
        Assert.Equal(entries.Max(entry => FirmwareCatalogService.BuildTimestamp(entry.Build)), FirmwareCatalogService.BuildTimestamp(entries[0].Build));
        foreach (var entry in entries)
        {
            Assert.StartsWith("https://archive.org/download/nothing-archive/spike0en/fullota/metroid/", entry.Url);
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedSha1) && string.IsNullOrWhiteSpace(entry.ExpectedMd5));
            var hashes = await catalog.GetPublishedImageHashesAsync(entry);
            foreach (var partition in FirmwareService.RescuePartitions) Assert.True(hashes.ContainsKey(partition), $"{entry.Build} has no published {partition}.img hash");
        }
    }
}
