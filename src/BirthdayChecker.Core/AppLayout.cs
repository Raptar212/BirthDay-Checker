namespace BirthdayChecker.Core;

/// <summary>
/// Where files live: entries.json and settings.json next to the exe, service files in data\, automatic copies in Backup\.
/// </summary>
public static class AppLayout
{
    private const string DataFolderName = "data";

    public static string EntriesPath(string root) => Path.Combine(root, "entries.json");
    public static string SettingsPath(string root) => Path.Combine(root, "settings.json");
    public static string DataFolder(string root) => Path.Combine(root, DataFolderName);
    public static string StatePath(string root) => Path.Combine(DataFolder(root), "state.json");
    public static string ErrorLogPath(string root) => Path.Combine(DataFolder(root), "errors.log");
    public static string BackupFolder(string root) => Path.Combine(root, "Backup");

    /// <summary>Moves service files of versions before 1.11 out of the root. A file already in data\ is never overwritten.</summary>
    public static void MoveOldFiles(string root)
    {
        var data = DataFolder(root);
        Directory.CreateDirectory(data);
        // .bak first: Load's "leftover .bak" refusal looks only in data\, and a locked log must not keep it in the root.
        foreach (var pattern in new[] { "entries.json.bak*", "state.json", "errors.log" })
            foreach (var file in Directory.GetFiles(root, pattern))
            {
                var target = Path.Combine(data, Path.GetFileName(file));
                if (!File.Exists(target))
                    File.Move(file, target);
            }
    }
}
