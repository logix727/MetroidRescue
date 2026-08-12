using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetroidRescue;

internal sealed record FirmwareCatalogEntry(
    string Build,
    string Url,
    string Region,
    string Source,
    string? ExpectedSha1 = null,
    string? ExpectedMd5 = null,
    string? ImageManifestUrl = null,
    string? ExpectedImageManifestSha256 = null,
    long? ExpectedSizeBytes = null)
{
    public override string ToString() => $"{Build} ({Region})";
}

internal sealed class FirmwareCatalogService
{
    public const string CatalogUrl = "https://raw.githubusercontent.com/spike0en/nothing_archive/main/website/docs/firmware.md";
    private const string ArchiveMetadataUrl = "https://archive.org/metadata/nothing-archive";
    private readonly HttpClient _http = new();
    private readonly Action<string> _log;
    private readonly string _cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue", "firmware-catalog.md");
    private static readonly IReadOnlyDictionary<string, (string Sha1, string ManifestSha256, long SizeBytes)> TrustedBuilds =
        new Dictionary<string, (string, string, long)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Metroid_B4.1-260414-1846"] = ("1bf56015af924574712ab1bf91fcb3131ff87c3b", "ba36e15822a4b9bcc1cecb51446033a69e2545887bebb6397b093f901ce89e22", 6904968674),
            ["Metroid_B4.0-251117-1909"] = ("db3bd05ac6bc4f69fa038f2579bcdf1c6c9feba3", "d1ea977a76b4f72d4f75d9e6ac07e217217f759e49059e249a197b818df1ef5d", 6701514204),
            ["Metroid_V3.5-250923-1421"] = ("4001f5cd7c719a3d56b3c4628c9f0c03f2ef35b5", "a420044c75a02ab50fbf094dffa4d46829132b65f9a1ce05b0beb107f12cb250", 6550122382),
            ["Metroid_V3.5-250829-1700"] = ("44b98e342d548f8beb429260415793497f5eee26", "4d0c85430c173e5db98b1cafb68f15ee75d922118b87f30f2605545e9fe8f8f2", 6522415504),
        };

    public FirmwareCatalogService(Action<string> log)
    {
        _log = log;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MetroidRescue", "1.0"));
    }

    public async Task<IReadOnlyList<FirmwareCatalogEntry>> GetAsync(CancellationToken token = default)
    {
        string markdown;
        try
        {
            _log("Fetching maintained Metroid full-OTA catalog from Nothing Archive...");
            markdown = await _http.GetStringAsync(CatalogUrl, token);
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(_cachePath, markdown, token);
        }
        catch when (File.Exists(_cachePath))
        {
            _log("Catalog fetch failed; using the cached Nothing Archive catalog.");
            markdown = await File.ReadAllTextAsync(_cachePath, token);
        }

        var entries = ParseMetroidFullOtas(markdown);
        if (entries.Count == 0) throw new InvalidOperationException("Nothing Archive catalog contained no parseable Metroid full OTAs.");
        try { entries = await AddArchiveChecksumsAsync(entries, token); }
        catch (Exception ex) { _log("Archive checksum metadata unavailable: " + ex.Message); }
        return entries;
    }

    public async Task<Dictionary<string, string>> GetPublishedImageHashesAsync(FirmwareCatalogEntry entry, CancellationToken token = default)
    {
        var url = entry.ImageManifestUrl ?? $"https://github.com/spike0en/nothing_archive/releases/download/{entry.Build}/{entry.Build}-hash.sha256";
        try
        {
            var bytes = await _http.GetByteArrayAsync(url, token);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(entry.ExpectedImageManifestSha256) || !digest.Equals(entry.ExpectedImageManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Published image manifest does not match the reviewed release-pinned SHA-256.");
            var text = Encoding.UTF8.GetString(bytes);
            return ParseImageManifest(text);
        }
        catch (HttpRequestException)
        {
            _log("Matching GitHub image checksum manifest is not published for this full OTA build.");
            return [];
        }
    }

    public async Task DownloadAsync(FirmwareCatalogEntry entry, string destination, IProgress<double>? progress = null, CancellationToken token = default)
    {
        using var response = await _http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[1024 * 1024];
        long written = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, token);
            if (count == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            written += count;
            if (total > 0) progress?.Report(written * 100d / total.Value);
        }
    }

    internal static List<FirmwareCatalogEntry> ParseMetroidFullOtas(string markdown)
    {
        var start = markdown.IndexOf("<summary><span class=\"summary-title\">Phone (3)</", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return [];
        var end = markdown.IndexOf("</details>", start, StringComparison.OrdinalIgnoreCase);
        var section = end > start ? markdown[start..end] : markdown[start..];
        const string pattern = @"\|\s*[^|]+\|\s*(Metroid[-_][^|\s]+)\s*\|.*?\|\s*\[Archive\]\((https://archive\.org/download/nothing-archive/spike0en/fullota/metroid/[^)]+\.zip)\)\s*\|";
        return Regex.Matches(section, pattern, RegexOptions.IgnoreCase)
            .Select(match => (Build: match.Groups[1].Value.Replace("Metroid-", "Metroid_", StringComparison.OrdinalIgnoreCase), Url: match.Groups[2].Value))
            .Where(item => TrustedBuilds.ContainsKey(item.Build))
            .Select(item =>
            {
                var trusted = TrustedBuilds[item.Build];
                var expectedUrl = $"https://archive.org/download/nothing-archive/spike0en/fullota/metroid/{item.Build}.zip";
                if (!item.Url.Equals(expectedUrl, StringComparison.Ordinal)) throw new InvalidOperationException($"Catalog URL changed for reviewed build {item.Build}.");
                return new FirmwareCatalogEntry(item.Build, item.Url, "Unified", CatalogUrl, trusted.Sha1, null,
                    $"https://github.com/spike0en/nothing_archive/releases/download/{item.Build}/{item.Build}-hash.sha256", trusted.ManifestSha256, trusted.SizeBytes);
            })
            .DistinctBy(entry => entry.Build, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(entry => BuildTimestamp(entry.Build))
            .ToList();
    }

    internal static long BuildTimestamp(string build)
    {
        var match = Regex.Match(build, @"(\d{6})-(\d{4})$");
        return match.Success && long.TryParse(match.Groups[1].Value + match.Groups[2].Value, out var value) ? value : 0;
    }

    private async Task<List<FirmwareCatalogEntry>> AddArchiveChecksumsAsync(List<FirmwareCatalogEntry> entries, CancellationToken token)
    {
        using var document = JsonDocument.Parse(await _http.GetStringAsync(ArchiveMetadataUrl, token));
        var files = document.RootElement.GetProperty("files").EnumerateArray().ToArray();
        return entries.Select(entry =>
        {
            var archiveName = Uri.UnescapeDataString(new Uri(entry.Url).AbsolutePath).Split("/nothing-archive/", 2).Last();
            var file = files.FirstOrDefault(item => item.TryGetProperty("name", out var name) && name.GetString() == archiveName);
            if (file.ValueKind == JsonValueKind.Undefined) return entry;
            string? Get(string key) => file.TryGetProperty(key, out var value) ? value.GetString() : null;
            var archiveSha1 = Get("sha1");
            if (!string.IsNullOrWhiteSpace(archiveSha1) && !archiveSha1.Equals(entry.ExpectedSha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Archive.org metadata changed for reviewed build {entry.Build}.");
            return entry;
        }).ToList();
    }

    internal static Dictionary<string, string> ParseImageManifest(string text)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+?)(?:\.img)?$");
            if (!match.Success) continue;
            var name = Path.GetFileName(match.Groups[2].Value);
            if (name.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            hashes[name] = match.Groups[1].Value.ToLowerInvariant();
        }
        return hashes;
    }
}
