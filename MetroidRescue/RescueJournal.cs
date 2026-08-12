using System.Text.Json;

namespace MetroidRescue;

internal sealed record JournalEntry(DateTime Timestamp, string SessionId, string Step, string Status, string Serial, string Slot, string FirmwareHash, string Detail);

internal sealed class RescueJournal
{
    private readonly string _directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroidRescue");
    private string JournalPath => System.IO.Path.Combine(_directory, "rescue-journal.jsonl");
    public string SessionId { get; } = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

    public void Record(string step, string status, FastbootDevice? device = null, FirmwareInfo? firmware = null, string detail = "")
    {
        Directory.CreateDirectory(_directory);
        var entry = new JournalEntry(DateTime.UtcNow, SessionId, step, status, device?.Serial ?? "", device?.Slot ?? "", firmware?.Sha256 ?? "", detail);
        File.AppendAllText(JournalPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }

    public HashSet<string> ResumableSteps(string serial, string firmwareHash)
    {
        if (!File.Exists(JournalPath)) return [];
        var entries = File.ReadLines(JournalPath)
            .Select(line => { try { return JsonSerializer.Deserialize<JournalEntry>(line); } catch { return null; } })
            .Where(entry => entry is not null && entry.Serial == serial && entry.FirmwareHash == firmwareHash)
            .Select(entry => entry!)
            .ToList();
        var session = entries.GroupBy(entry => entry.SessionId)
            .OrderByDescending(group => group.Max(entry => entry.Timestamp))
            .FirstOrDefault(group => group.Any(entry => entry.Step == "session" && entry.Status == "started") && !group.Any(entry => entry.Step == "session" && entry.Status == "completed"));
        return session?.Where(entry => entry.Status == "completed")
            .Select(entry => entry.Step)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    public string Path => JournalPath;
}
