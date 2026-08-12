using System.IO.Compression;
using MetroidRescue;

namespace MetroidRescue.Tests;

public class FirmwareInspectionTests
{
    [Fact]
    public async Task AcceptsValidMetroidMetadataRegardlessOfFilename()
    {
        var path = CreateOta("renamed-package.zip", "ota-type=AB\npost-build=Nothing/Metroid/Metroid:16/test\npost-build-incremental=2604141846\n");
        try
        {
            var info = await new FirmwareService(_ => { }).InspectAsync(path);
            Assert.Equal("2604141846", info.Build);
            Assert.False(info.IsIncremental);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("ota-type=AB\npost-build=Nothing/Spacewar/Spacewar:16/test\npost-build-incremental=1\n")]
    [InlineData("ota-type=BLOCK\npost-build=Nothing/Metroid/Metroid:16/test\npost-build-incremental=1\n")]
    public async Task RejectsMissingOrWrongTargetMetadata(string metadata)
    {
        var path = CreateOta("Metroid_fake.zip", metadata, includeMetadata: metadata.Length > 0);
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => new FirmwareService(_ => { }).InspectAsync(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DetectsIncrementalPackageFromPreBuildMetadata()
    {
        var path = CreateOta("any.zip", "ota-type=AB\npre-build=old\npost-build=Nothing/Metroid/Metroid:16/test\npost-build-incremental=2\n");
        try { Assert.True((await new FirmwareService(_ => { }).InspectAsync(path)).IsIncremental); }
        finally { File.Delete(path); }
    }

    private static string CreateOta(string name, string metadata, bool includeMetadata = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MetroidRescueTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (archive.CreateEntry("payload.bin").Open()) { }
        if (includeMetadata)
        {
            var entry = archive.CreateEntry("META-INF/com/android/metadata");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(metadata);
        }
        return path;
    }
}
