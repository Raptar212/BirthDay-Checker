namespace BirthdayChecker.Core;

/// <summary>Main-window filters; null / blank = no filtering on that field.</summary>
public sealed record ListFilter(int? Month = null, string? EventType = null, string? Tag = null, string? Search = null);

/// <summary>What the main window shows: filtering, search, default order and the choice lists.</summary>
public static class EntryList
{
    public static readonly string[] DefaultTags = ["семья", "друзья", "коллеги", "именины", "частный", "регулярный"];

    private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>Month filter uses the next occurrence, so floating and Easter dates land in the month they fall in now.</summary>
    public static IReadOnlyList<Occurrence> Filter(IEnumerable<Entry> entries, DateOnly today, ListFilter f) =>
        entries.Where(e => Passes(e, f))
            .Select(e => Upcoming.Next(e, today))
            .Where(o => f.Month is null || o.Date.Month == f.Month)
            .OrderBy(o => o.DaysLeft)
            .ThenBy(o => o.Entry.Name, Ci)
            .ToList();

    /// <summary>What the calendar shows: occurrences of one year, past ones included, by date.</summary>
    public static IReadOnlyList<Occurrence> InYear(IEnumerable<Entry> entries, int year, DateOnly today, ListFilter f) =>
        entries.Where(e => Passes(e, f))
            .Select(e => Upcoming.InYear(e, year, today))
            .OfType<Occurrence>()
            .Where(o => f.Month is null || o.Date.Month == f.Month)
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Entry.Name, Ci)
            .ToList();

    private static bool Passes(Entry e, ListFilter f) =>
        (string.IsNullOrWhiteSpace(f.EventType) || Ci.Equals(e.EventType, f.EventType))
        && (string.IsNullOrWhiteSpace(f.Tag) || e.Tags.Contains(f.Tag, Ci))
        && (string.IsNullOrWhiteSpace(f.Search) || Matches(e, f.Search.Trim()));

    private static bool Matches(Entry e, string text) =>
        new[] { e.Name, e.Comment, e.Phone }.Any(s => s?.Contains(text, StringComparison.CurrentCultureIgnoreCase) == true);

    /// <summary>"семья, друзья;коллеги" → trimmed, non-empty, without duplicates (case-insensitive).</summary>
    public static IReadOnlyList<string> ParseTags(string? text) =>
        (text ?? "").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(Ci)
            .ToList();
}
