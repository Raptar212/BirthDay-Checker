using System.Text.Json.Serialization;

namespace BirthdayChecker.Core;

/// <summary>"Remind N before" checkboxes; same 13 slots and order as the legacy program.</summary>
[Flags]
public enum Reminder
{
    None = 0,
    Day1 = 1 << 0, Day2 = 1 << 1, Day3 = 1 << 2, Day4 = 1 << 3, Day5 = 1 << 4,
    Week1 = 1 << 5, Week2 = 1 << 6, Week3 = 1 << 7, Week4 = 1 << 8, Week5 = 1 << 9,
    Month1 = 1 << 10, Month2 = 1 << 11, Month3 = 1 << 12,
}

public sealed record Entry(string Name, string EventType, EventDate Date)
{
    /// <summary>Stable identity across saves (editing, "don't remind again today"). Required in the file.</summary>
    [JsonRequired]
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Phone { get; init; }
    public string? Comment { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public Reminder Reminders { get; init; }

    /// <summary>Row color in the list, "#RRGGBB"; null = default alternating rows.</summary>
    public string? Color { get; init; }

    /// <summary>Values of the user's own columns by column name; null = none. See <see cref="ColumnCatalog"/>.</summary>
    public IReadOnlyDictionary<string, string>? CustomFields { get; init; }

    public static bool IsColor(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") == false;
}
