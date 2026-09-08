using System.IO;
using System.Text.Json;

namespace MarkdownViewer.Desktop;

/// <summary>
/// Persists a small "recently opened files" list (most-recent-first) to a JSON file under
/// the user's local app data folder, so File > Open can offer quick access to recent work.
/// </summary>
internal static class RecentFilesStore
{
    private const int MaxEntries = 8;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarkdownViewer", "recent-files.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            var entries = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return entries.Where(File.Exists).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Add(string fullPath)
    {
        try
        {
            var entries = Load();
            entries.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, fullPath);
            if (entries.Count > MaxEntries)
                entries = entries[..MaxEntries];

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch { /* best-effort; recent files is a convenience feature */ }
    }
}
