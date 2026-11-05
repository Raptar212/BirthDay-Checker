using System.Globalization;
using System.Text;

namespace BirthdayChecker.Core;

/// <summary>Group mode: the same change applied to several entries. Null / empty parts leave the field as is.</summary>
public sealed record GroupEdit
{
    private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

    public Reminder? Reminders { get; init; }
    public IReadOnlyList<string> AddTags { get; init; } = [];
    public IReadOnlyList<string> RemoveTags { get; init; } = [];

    /// <summary>With <see cref="ChangeColor"/>, a null <see cref="Color"/> clears the row color.</summary>
    public bool ChangeColor { get; init; }
    public string? Color { get; init; }

    public string? EventType { get; init; }

    /// <summary>With <see cref="ChangePhone"/>, a null <see cref="Phone"/> clears the phone.</summary>
    public bool ChangePhone { get; init; }
    public string? Phone { get; init; }

    /// <summary>With <see cref="ChangeComment"/>, a null <see cref="Comment"/> clears the comment.</summary>
    public bool ChangeComment { get; init; }
    public string? Comment { get; init; }

    /// <summary>The user's columns to change, by name; a null value clears that column.</summary>
    public IReadOnlyDictionary<string, string?> Custom { get; init; } = new Dictionary<string, string?>();

    public Entry Apply(Entry e) => Custom.Aggregate(e with
    {
        EventType = EventType ?? e.EventType,
        Phone = ChangePhone ? Phone : e.Phone,
        Comment = ChangeComment ? Comment : e.Comment,
        Reminders = Reminders ?? e.Reminders,
        Tags = NewTags(e),
        Color = ChangeColor ? Color : e.Color,
    }, (x, c) => ColumnCatalog.With(x, c.Key, c.Value));

    /// <summary>The same list object when nothing changes: entries compare it by reference, and an equal entry is not re-saved.</summary>
    private IReadOnlyList<string> NewTags(Entry e)
    {
        var tags = e.Tags.Where(t => !RemoveTags.Contains(t, Ci)).Concat(AddTags).Distinct(Ci).ToList();
        return tags.SequenceEqual(e.Tags) ? e.Tags : tags;
    }
}

/// <summary>"Список грядущих праздников" — plain text, in the order the list shows.</summary>
public static class Report
{
    public static string Build(IEnumerable<Occurrence> items, DateOnly today)
    {
        var text = new StringBuilder()
            .AppendLine($"Список грядущих праздников на {today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}")
            .AppendLine();
        foreach (var o in items)
        {
            var line = $"{o.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}  {ReminderText.When(o.DaysLeft),-16}{o.Entry.Name} — {o.Entry.EventType}";
            if (o.Years is int y and > 0)
                line += $", {y} {ReminderText.Plural(y, "год", "года", "лет")}";
            if (!string.IsNullOrWhiteSpace(o.Entry.Phone))
                line += $", тел. {o.Entry.Phone}";
            foreach (var field in (o.Entry.CustomFields ?? new Dictionary<string, string>()).OrderBy(f => f.Key, StringComparer.CurrentCultureIgnoreCase))
                line += $", {field.Key}: {field.Value.ReplaceLineEndings(" ")}";
            text.AppendLine(line);
        }
        return text.ToString();
    }
}
