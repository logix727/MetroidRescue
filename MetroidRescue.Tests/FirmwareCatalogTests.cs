using MetroidRescue;

namespace MetroidRescue.Tests;

public class FirmwareCatalogTests
{
    [Fact]
    public void ParsesOnlyMetroidFullOtaArchiveColumn()
    {
        const string markdown = """
        <summary><span class="summary-title">Phone (3)</span><span class="summary-subtitle">Metroid</span></summary>
        | 4.0 | Metroid-B4.0-251117-1909 | old -> [incremental](https://android.googleapis.com/a.zip) | [Archive](https://archive.org/download/nothing-archive/spike0en/fullota/metroid/Metroid_B4.0-251117-1909.zip) | [GitHub](x) |
        | 4.1 | Metroid-B4.1-260603-1221 | old -> [incremental](https://android.googleapis.com/b.zip) | N/A | [GitHub](x) |
        </details>
        """;
        var entries = FirmwareCatalogService.ParseMetroidFullOtas(markdown);
        var entry = Assert.Single(entries);
        Assert.Equal("Metroid_B4.0-251117-1909", entry.Build);
        Assert.Contains("fullota/metroid", entry.Url);
    }

    [Fact]
    public void ParsesPublishedImageChecksumManifest()
    {
        var hashes = FirmwareCatalogService.ParseImageManifest("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  boot.img\nBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB *system.img\n");
        Assert.Equal(new string('a', 64), hashes["boot"]);
        Assert.Equal(new string('b', 64), hashes["system"]);
    }
}
