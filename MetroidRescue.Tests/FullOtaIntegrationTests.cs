using MetroidRescue;

namespace MetroidRescue.Tests;

public class FullOtaIntegrationTests
{
    [Fact]
    [Trait("Category", "FullOta")]
    public async Task NewestPinnedOtaInspectsExtractsAndMatchesEveryBootChainHash()
    {
        var path = Environment.GetEnvironmentVariable("METROID_RESCUE_FULL_OTA");
        if (string.IsNullOrWhiteSpace(path)) return;
        var tools = Environment.GetEnvironmentVariable("METROID_RESCUE_INTEGRATION_TOOLS");
        if (string.IsNullOrWhiteSpace(tools)) throw new InvalidOperationException("METROID_RESCUE_INTEGRATION_TOOLS is required for full OTA extraction.");

        var output = Path.Combine(Path.GetTempPath(), "MetroidRescueTests", Guid.NewGuid().ToString("N"));
        var dumper = Path.Combine(tools, OperatingSystem.IsWindows() ? "payload-dumper-go.exe" : "payload-dumper-go");
        var firmware = new FirmwareService(_ => { }, dumper, output);
        try
        {
            var catalog = new FirmwareCatalogService(_ => { });
            var source = Assert.Single(await catalog.GetAsync(), entry => entry.Build == "Metroid_B4.1-260414-1846");
            var info = await firmware.InspectAsync(path);

            FirmwareService.VerifyCatalogIdentity(info, source);
            await firmware.VerifyPublishedChecksumAsync(info, source);
            Assert.False(info.IsIncremental);
            Assert.True(info.HasPayload);

            await firmware.ExtractAsync(path);
            var published = await catalog.GetPublishedImageHashesAsync(source);
            var provenance = new ProvenanceService(firmware);
            var manifest = await provenance.CreateAsync(info, source, FirmwareService.RescuePartitions, published);
            foreach (var image in FirmwareService.RescuePartitions)
            {
                await provenance.VerifyImageAsync(image, manifest);
                Assert.True(new FileInfo(firmware.ImagePath(image)).Length > 0);
            }
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}
