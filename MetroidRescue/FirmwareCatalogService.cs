using System.Net.Http.Headers;
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
    string? ImageManifestUrl = null)
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
            var text = await _http.GetStringAsync(url, token);
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
            .Select(match => new FirmwareCatalogEntry(match.Groups[1].Value.Replace("Metroid-", "Metroid_", StringComparison.OrdinalIgnoreCase), match.Groups[2].Value, "Unified", CatalogUrl))
            .DistinctBy(entry => entry.Build, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(entry => entry.Build, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            return entry with { ExpectedSha1 = Get("sha1"), ExpectedMd5 = Get("md5"), ImageManifestUrl = $"https://github.com/spike0en/nothing_archive/releases/download/{entry.Build}/{entry.Build}-hash.sha256" };
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
