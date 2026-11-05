namespace BirthdayChecker.Core;

/// <summary>Next occurrence of an entry. <see cref="Years"/> is age / anniversary number, null if the year is unknown.</summary>
public sealed record Occurrence(Entry Entry, DateOnly Date, int DaysLeft, int? Years);

public static class Upcoming
{
    private static readonly (Reminder Flag, Func<DateOnly, DateOnly> Before)[] Offsets =
    [
        (Reminder.Day1, d => d.AddDays(-1)), (Reminder.Day2, d => d.AddDays(-2)),
        (Reminder.Day3, d => d.AddDays(-3)), (Reminder.Day4, d => d.AddDays(-4)),
        (Reminder.Day5, d => d.AddDays(-5)),
        (Reminder.Week1, d => d.AddDays(-7)), (Reminder.Week2, d => d.AddDays(-14)),
        (Reminder.Week3, d => d.AddDays(-21)), (Reminder.Week4, d => d.AddDays(-28)),
        (Reminder.Week5, d => d.AddDays(-35)),
        (Reminder.Month1, d => d.AddMonths(-1)), (Reminder.Month2, d => d.AddMonths(-2)),
        (Reminder.Month3, d => d.AddMonths(-3)),
    ];

    public static Occurrence Next(Entry entry, DateOnly today)
    {
        var date = entry.Date.NextOccurrence(today);
        int? years = entry.Date is FixedDate { Year: int born } && date.Year >= born ? date.Year - born : null;
        return new Occurrence(entry, date, date.DayNumber - today.DayNumber, years);
    }

    /// <summary>The occurrence in a given year, past ones included (negative DaysLeft); null before a known birth year.</summary>
    public static Occurrence? InYear(Entry entry, int year, DateOnly today)
    {
        int? born = (entry.Date as FixedDate)?.Year;
        if (year < born)
            return null;
        var date = entry.Date.OccurrenceIn(year);
        return new Occurrence(entry, date, date.DayNumber - today.DayNumber, year - born);
    }

    /// <summary>Longest offset is 3 months; older missed days cannot matter.</summary>
    private const int MaxCatchUpDays = 93;

    /// <summary>
    /// Due on the event day itself, and on each day that matches a checked offset
    /// (Day3|Day2|Day1 reminds three days in a row, like the legacy program).
    /// Offset days after <paramref name="lastChecked"/> that were missed (PC off) are caught up,
    /// as long as the event itself is still ahead; a missed event day is not.
    /// </summary>
    public static bool IsDue(Entry entry, DateOnly today, DateOnly? lastChecked = null)
    {
        var date = entry.Date.NextOccurrence(today);
        if (date == today)
            return true;

        // Exclusive lower bound: by default only today; after a gap, every day since the last check.
        var from = lastChecked is { } last && last < today
            ? DateOnly.FromDayNumber(Math.Max(last.DayNumber, today.DayNumber - MaxCatchUpDays))
            : today.AddDays(-1);
        return Offsets.Any(o =>
        {
            if (!entry.Reminders.HasFlag(o.Flag))
                return false;
            var day = o.Before(date);
            return day > from && day <= today;
        });
    }

    /// <summary>Entries to show in the reminder window, nearest first.</summary>
    public static IReadOnlyList<Occurrence> Due(IEnumerable<Entry> entries, DateOnly today, DateOnly? lastChecked = null) =>
        entries.Where(e => IsDue(e, today, lastChecked))
            .Select(e => Next(e, today))
            .OrderBy(o => o.DaysLeft)
            .ToList();

    /// <summary>Today lies between the entry's earliest checked offset and the event itself, whichever offset days have passed.</summary>
    public static bool IsInReach(Entry entry, DateOnly today)
    {
        var date = entry.Date.NextOccurrence(today);
        return date == today || Offsets.Any(o => entry.Reminders.HasFlag(o.Flag) && o.Before(date) <= today);
    }

    /// <summary>What a manual check shows: everything in reach, not only the entries due exactly today.</summary>
    public static IReadOnlyList<Occurrence> InReach(IEnumerable<Entry> entries, DateOnly today) =>
        entries.Where(e => IsInReach(e, today))
            .Select(e => Next(e, today))
            .OrderBy(o => o.DaysLeft)
            .ToList();
}
