using System.Globalization;

namespace BirthdayChecker.Core;

/// <summary>Periodic copy of entries.json: one file backup-BDC-yyyy-MM-dd.json in the folder, the older one is replaced.</summary>
public static class AutoBackup
{
    private const string Prefix = "backup-BDC-";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Copies when <paramref name="periodDays"/> have passed since the date in the existing copy's name.
    /// The only copy is replaced by a checked one: a broken or empty list never overwrites it.
    /// </summary>
    /// <returns>The new copy's path; null when nothing was due.</returns>
    /// <exception cref="InvalidDataException">The entries file is broken; the existing copy stays.</exception>
    public static string? RunIfDue(string entriesPath, string folder, int periodDays, DateOnly today)
    {
        if (periodDays <= 0 || !File.Exists(entriesPath) || !IsDue(folder, periodDays, today))
            return null;

        Directory.CreateDirectory(folder);
        var tmp = Path.Combine(folder, "backup.tmp");
        try
        {
            File.Copy(entriesPath, tmp, overwrite: true);
            if (EntryStorage.Load(tmp).Count == 0)
                return null;

            var target = Path.Combine(folder, Prefix + today.ToString(DateFormat, CultureInfo.InvariantCulture) + ".json");
            File.Move(tmp, target, overwrite: true);
            foreach (var old in Existing(folder))
                if (DateOf(old) is not null && !string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) // only our own names: a hand-named file stays
                    File.Delete(old);
            return target;
        }
        finally
        {
            File.Delete(tmp); // no-op after the move
        }
    }

    private static bool IsDue(string folder, int periodDays, DateOnly today)
    {
        var dates = Existing(folder).Select(DateOf).OfType<DateOnly>().ToList();
        if (dates.Count == 0)
            return true;
        var last = dates.Max();
        return last > today || today.DayNumber - last.DayNumber >= periodDays; // a future date means the clock was wrong
    }

    private static string[] Existing(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, Prefix + "*.json") : [];

    private static DateOnly? DateOf(string path) =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path)[Prefix.Length..], DateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
