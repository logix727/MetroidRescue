using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MetroidRescue;

internal sealed class FirmwareService
{
    public static readonly string[] RescuePartitions = ["boot", "init_boot", "dtbo", "vendor_boot", "vbmeta"];
    public static readonly string[] BootPartitions = ["boot", "dtbo", "init_boot", "recovery", "vendor_boot"];
    public static readonly string[] FirmwarePartitions = [
        "abl", "aop", "aop_config", "bluetooth", "cpucp", "cpucp_dtb", "devcfg", "dsp", "featenabler", "hyp",
        "imagefv", "keymaster", "modem", "multiimgoem", "multiimgqti", "pvmfw", "qupfw", "shrm", "soccp_dcd",
        "soccp_debug", "tz", "uefi", "uefisecapp", "xbl", "xbl_config", "xbl_ramdump"
    ];
    public static readonly string[] LogicalPartitions = ["odm", "product", "system", "system_dlkm", "system_ext", "vendor", "vendor_dlkm"];
    public static readonly string[] OtherVbmetaPartitions = ["vbmeta_system", "vbmeta_vendor"];
    public static readonly string[] FullRestorePartitions = BootPartitions.Concat(["vbmeta"]).Concat(FirmwarePartitions).Concat(LogicalPartitions).Concat(OtherVbmetaPartitions).ToArray();
    private readonly Action<string> _log;
    public string OutputDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "images");
    private string DumperPath => ToolPaths.PayloadDumper;

    public FirmwareService(Action<string> log) => _log = log;

    public async Task<FirmwareInfo> InspectAsync(string otaPath, CancellationToken token = default)
    {
        if (!File.Exists(otaPath)) throw new FileNotFoundException("OTA file not found.", otaPath);
        var fileName = Path.GetFileNameWithoutExtension(otaPath);
        var buildMatch = Regex.Match(fileName, @"(?i)^Metroid[_-](.+)$");
        if (!buildMatch.Success) throw new InvalidOperationException("Filename is not a Metroid firmware package.");

        string metadataText = "";
        bool hasPayload;
        using (var archive = ZipFile.OpenRead(otaPath))
        {
            hasPayload = archive.Entries.Any(entry => entry.FullName.Equals("payload.bin", StringComparison.OrdinalIgnoreCase));
            var metadata = archive.GetEntry("META-INF/com/android/metadata");
            if (metadata is not null)
            {
                using var reader = new StreamReader(metadata.Open());
                metadataText = await reader.ReadToEndAsync(token);
            }
        }
        var incremental = metadataText.Split('\n').Any(line => line.StartsWith("pre-build=", StringComparison.Ordinal) || line.StartsWith("pre-build-incremental=", StringComparison.Ordinal));
        var postBuild = MetadataValue(metadataText, "post-build") ?? "";
        if (!string.IsNullOrEmpty(postBuild) && !postBuild.Contains("metroid", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OTA metadata does not identify a Metroid target.");

        _log("Computing firmware SHA-256...");
        await using var stream = File.OpenRead(otaPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
        return new FirmwareInfo(otaPath, fileName, buildMatch.Groups[1].Value, hash, new FileInfo(otaPath).Length, incremental, hasPayload, postBuild);
    }

    public async Task VerifyPublishedChecksumAsync(FirmwareInfo info, FirmwareCatalogEntry source, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(source.ExpectedSha1) && string.IsNullOrWhiteSpace(source.ExpectedMd5))
        {
            _log("Publisher archive checksum is unavailable; SHA-256 is recorded locally but not publisher-verified.");
            return;
        }
        await using var stream = File.OpenRead(info.Path);
        if (!string.IsNullOrWhiteSpace(source.ExpectedSha1))
        {
            var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).ToLowerInvariant();
            if (!actual.Equals(source.ExpectedSha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Full OTA failed Archive.org published SHA-1 verification.");
            _log("Full OTA matches Archive.org published SHA-1.");
            return;
        }
        var md5 = Convert.ToHexString(await MD5.HashDataAsync(stream, token)).ToLowerInvariant();
        if (!md5.Equals(source.ExpectedMd5, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Full OTA failed Archive.org published MD5 verification.");
        _log("Full OTA matches Archive.org published MD5.");
    }

    public async Task ExtractAsync(string otaPath, CancellationToken token = default)
        => await ExtractPartitionsAsync(otaPath, RescuePartitions, "boot-chain", token);

    public async Task ExtractFullAsync(string otaPath, CancellationToken token = default)
        => await ExtractPartitionsAsync(otaPath, FullRestorePartitions, "complete stock", token);

    private async Task ExtractPartitionsAsync(string otaPath, string[] partitions, string label, CancellationToken token)
    {
        var info = await InspectAsync(otaPath, token);
        if (info.IsIncremental) throw new InvalidOperationException("Incremental OTA detected. Rescue extraction requires a full OTA.");
        if (!info.HasPayload) throw new InvalidOperationException("This archive does not contain payload.bin.");

        var drive = new DriveInfo(Path.GetPathRoot(OutputDirectory)!);
        var required = Math.Max(8L * 1024 * 1024 * 1024, info.SizeBytes * 3);
        if (drive.AvailableFreeSpace < required)
            throw new InvalidOperationException($"Insufficient disk space. Need at least {FormatBytes(required)} free; available {FormatBytes(drive.AvailableFreeSpace)}.");
        _log($"Firmware {info.Build}; SHA-256 {info.Sha256}");

        Directory.CreateDirectory(OutputDirectory);
        foreach (var partition in partitions)
        {
            var oldImage = ImagePath(partition);
            if (File.Exists(oldImage)) File.Delete(oldImage);
        }
        _log($"Extracting {label} images. This can take several minutes...");
        var result = await CommandRunner.RunAsync(DumperPath, ["-o", OutputDirectory, "-p", string.Join(',', partitions), otaPath], _log, token);
        if (!result.Success) throw new InvalidOperationException("payload-dumper-go failed. See the rescue log.");
        var missing = partitions.Where(partition => !File.Exists(ImagePath(partition))).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"OTA is missing required images: {string.Join(", ", missing)}");
    }

    public string ImagePath(string partition) => Path.Combine(OutputDirectory, partition + ".img");
    public bool ImagesReady => RescuePartitions.All(partition => File.Exists(ImagePath(partition)));
    public bool FullImagesReady => FullRestorePartitions.All(partition => File.Exists(ImagePath(partition)));

    private static string? MetadataValue(string metadata, string key) => metadata.Split('\n')
        .FirstOrDefault(line => line.StartsWith(key + "=", StringComparison.Ordinal))?.Split('=', 2)[1].Trim();

    private static string FormatBytes(long value) => $"{value / 1024d / 1024d / 1024d:F1} GB";
}

internal sealed record FirmwareInfo(string Path, string Name, string Build, string Sha256, long SizeBytes, bool IsIncremental, bool HasPayload, string PostBuild)
{
    public string ShortHash => Sha256[..12];
}
