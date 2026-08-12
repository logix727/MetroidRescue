using System.Security.Cryptography;
using System.Text.Json;

namespace MetroidRescue;

internal sealed record ImageProvenance(string Partition, string Sha256, long SizeBytes);
internal sealed record FirmwareProvenance(string FirmwareName, string Build, string FirmwareSha256, string? PublishedSha1, string? SourceUrl, DateTime CreatedUtc, IReadOnlyList<ImageProvenance> Images);

internal sealed class ProvenanceService
{
    private readonly FirmwareService _firmware;
    public ProvenanceService(FirmwareService firmware) => _firmware = firmware;
    public string ManifestPath => Path.Combine(_firmware.OutputDirectory, "provenance.json");

    public async Task<FirmwareProvenance> CreateAsync(FirmwareInfo info, FirmwareCatalogEntry? source, IEnumerable<string> partitions, IReadOnlyDictionary<string, string>? publishedImageHashes = null, CancellationToken token = default)
    {
        var requested = partitions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (source is not null)
        {
            if (publishedImageHashes is null || publishedImageHashes.Count == 0)
                throw new InvalidOperationException("Published extracted-image checksums are unavailable for this catalog OTA.");
            var missing = requested.Where(partition => !publishedImageHashes.ContainsKey(partition)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Published image manifest is incomplete: {string.Join(", ", missing)}");
        }
        var images = new List<ImageProvenance>();
        foreach (var partition in requested)
        {
            var path = _firmware.ImagePath(partition);
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            if (publishedImageHashes?.TryGetValue(partition, out var expected) == true && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Extracted {partition}.img failed Nothing Archive published SHA-256 verification.");
            images.Add(new ImageProvenance(partition, hash, stream.Length));
        }
        var manifest = new FirmwareProvenance(info.Name, info.Build, info.Sha256, source?.ExpectedSha1, source?.Url, DateTime.UtcNow, images);
        await File.WriteAllTextAsync(ManifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), token);
        return manifest;
    }

    public async Task VerifyImageAsync(string partition, FirmwareProvenance manifest, CancellationToken token = default)
    {
        var expected = manifest.Images.FirstOrDefault(image => image.Partition.Equals(partition, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Provenance has no {partition} image.");
        var path = _firmware.ImagePath(partition);
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
        if (actual != expected.Sha256 || stream.Length != expected.SizeBytes)
            throw new InvalidOperationException($"Extracted {partition}.img failed provenance verification.");
    }
}
