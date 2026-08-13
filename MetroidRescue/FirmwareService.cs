using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MetroidRescue;

internal sealed class FirmwareService
{
    public static readonly string[] RescuePartitions = ["boot", "init_boot", "dtbo", "recovery", "vendor_boot", "vbmeta"];
    private readonly Action<string> _log;
    private readonly string _dumperPath;
    public string OutputDirectory { get; }
    private string DumperPath => _dumperPath;

    public FirmwareService(Action<string> log)
    {
        _log = log;
        _dumperPath = ToolPaths.PayloadDumper;
        OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "images");
    }

#if METROID_RESCUE_TESTS
    internal FirmwareService(Action<string> log, string dumperPath, string outputDirectory)
    {
        _log = log;
        _dumperPath = dumperPath;
        OutputDirectory = outputDirectory;
    }
#endif

    public async Task<FirmwareInfo> InspectAsync(string otaPath, CancellationToken token = default)
    {
        if (!File.Exists(otaPath)) throw new FileNotFoundException("OTA file not found.", otaPath);
        var fileName = Path.GetFileNameWithoutExtension(otaPath);

        string metadataText = "";
        bool hasPayload;
        using (var archive = ZipFile.OpenRead(otaPath))
        {
            hasPayload = archive.Entries.Any(entry => entry.FullName.Equals("payload.bin", StringComparison.OrdinalIgnoreCase));
            var metadata = archive.GetEntry("META-INF/com/android/metadata")
                ?? throw new InvalidOperationException("OTA metadata is missing; target identity cannot be verified.");
            using var reader = new StreamReader(metadata.Open());
            metadataText = await reader.ReadToEndAsync(token);
        }
        if (!hasPayload) throw new InvalidOperationException("This archive does not contain payload.bin.");
        var otaType = MetadataValue(metadataText, "ota-type");
        if (!string.Equals(otaType, "AB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OTA metadata does not identify an A/B payload package.");
        var incremental = metadataText.Split('\n').Any(line => line.StartsWith("pre-build=", StringComparison.Ordinal) || line.StartsWith("pre-build-incremental=", StringComparison.Ordinal));
        var postBuild = MetadataValue(metadataText, "post-build")
            ?? throw new InvalidOperationException("OTA metadata has no post-build fingerprint.");
        var postBuildIncremental = MetadataValue(metadataText, "post-build-incremental")
            ?? throw new InvalidOperationException("OTA metadata has no post-build-incremental identifier.");
        if (!Regex.IsMatch(postBuild, @"^Nothing/Metroid(?:EEA|IND|JPN|TUR)?/Metroid:", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("OTA metadata does not identify a Metroid target.");

        _log("Computing firmware SHA-256...");
        await using var stream = File.OpenRead(otaPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
        return new FirmwareInfo(otaPath, fileName, postBuildIncremental, hash, new FileInfo(otaPath).Length, incremental, hasPayload, postBuild);
    }

    public static void VerifyCatalogIdentity(FirmwareInfo info, FirmwareCatalogEntry source)
    {
        var timestamp = Regex.Match(source.Build, @"(\d{6})-(\d{4})$");
        if (!timestamp.Success) throw new InvalidOperationException("Catalog build identifier is malformed.");
        var expectedIncremental = timestamp.Groups[1].Value + timestamp.Groups[2].Value;
        if (!info.Build.Equals(expectedIncremental, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"OTA metadata build '{info.Build}' does not match catalog build '{source.Build}'.");
    }

    public async Task VerifyPublishedChecksumAsync(FirmwareInfo info, FirmwareCatalogEntry source, CancellationToken token = default)
    {
        if (source.ExpectedSizeBytes is not null && info.SizeBytes != source.ExpectedSizeBytes)
            throw new InvalidOperationException($"Full OTA size {info.SizeBytes} does not match reviewed size {source.ExpectedSizeBytes}.");
        if (string.IsNullOrWhiteSpace(source.ExpectedSha1) && string.IsNullOrWhiteSpace(source.ExpectedMd5))
            throw new InvalidOperationException("Archive.org object checksum is unavailable; automatic writes are blocked.");
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

    private static string? MetadataValue(string metadata, string key) => metadata.Split('\n')
        .FirstOrDefault(line => line.StartsWith(key + "=", StringComparison.Ordinal))?.Split('=', 2)[1].Trim();

    private static string FormatBytes(long value) => $"{value / 1024d / 1024d / 1024d:F1} GB";
}

internal sealed record FirmwareInfo(string Path, string Name, string Build, string Sha256, long SizeBytes, bool IsIncremental, bool HasPayload, string PostBuild)
{
    public string ShortHash => Sha256[..12];
}
