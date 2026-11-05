using System.Globalization;

namespace BirthdayChecker.Core;

/// <summary>User-facing reminder text (the app is Russian-only).</summary>
public static class ReminderText
{
    public const string Separator = "--------";

    /// <summary>Russian plural: 1 день, 2 дня, 5 дней, 11 дней, 21 день.</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        int n100 = Math.Abs(n % 100), n10 = n100 % 10;
        if (n100 is >= 11 and <= 14) return many;
        return n10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }

    public static string When(int daysLeft) => daysLeft switch
    {
        0 => "Сегодня",
        1 => "Завтра",
        < 0 => $"{-daysLeft} {Plural(daysLeft, "день", "дня", "дней")} назад",
        _ => $"Через {daysLeft} {Plural(daysLeft, "день", "дня", "дней")}",
    };

    /// <summary>The "days left" table cell: a bare number, or "17 дн. назад" for a past event of an opened month.</summary>
    public static string DaysColumn(int daysLeft) => daysLeft < 0 ? $"{-daysLeft} дн. назад" : daysLeft.ToString(CultureInfo.InvariantCulture);

    /// <summary>Longest text a Windows notification takes.</summary>
    public const int MaxNotificationLength = 255;

    /// <summary>
    /// The one order of all reminders: name, date, then the ticked columns in table order, the user's own last.
    /// The first line is always the name. Null columns: <see cref="ColumnCatalog.ReminderDefault"/>.
    /// </summary>
    public static IReadOnlyList<string> Lines(Occurrence o, IReadOnlyList<string>? columns = null)
    {
        var shown = columns ?? ColumnCatalog.ReminderDefault;
        bool On(string column) => shown.Contains(column, StringComparer.CurrentCultureIgnoreCase);

        var date = o.Date.ToString("dd.MM", CultureInfo.InvariantCulture);
        var lines = new List<string> { o.Entry.Name, On("Через, дн.") ? $"{date}, {When(o.DaysLeft).ToLowerInvariant()}" : date };

        var kind = new List<string>();
        if (On("Событие")) kind.Add(o.Entry.EventType);
        if (On("Возраст") && o.Years is int y and > 0) kind.Add($"{y} {Plural(y, "год", "года", "лет")}");
        if (kind.Count > 0) lines.Add(string.Join(", ", kind));

        if (On("Теги") && o.Entry.Tags.Count > 0) lines.Add($"Теги: {string.Join(", ", o.Entry.Tags)}");
        if (On("Телефон") && !string.IsNullOrWhiteSpace(o.Entry.Phone)) lines.Add($"Тел.: {o.Entry.Phone}");
        if (On("Комментарий") && !string.IsNullOrWhiteSpace(o.Entry.Comment)) lines.Add(o.Entry.Comment);
        foreach (var column in shown.Except(ColumnCatalog.BuiltIn, StringComparer.CurrentCultureIgnoreCase))
            if (ColumnCatalog.Value(o.Entry, column) is { } value)
                lines.Add($"{column}: {value}");
        return lines;
    }

    public static string Describe(Occurrence o, IReadOnlyList<string>? columns = null) =>
        string.Join(Environment.NewLine, Lines(o, columns));

    /// <summary>
    /// Text of the one Windows notification: one event line by line, several one line each,
    /// and "и ещё N" for those that don't fit into <paramref name="maxLength"/>.
    /// <paramref name="bullet"/> starts each of several events, so a wrapped line doesn't run into the next one.
    /// </summary>
    public static string Notification(IReadOnlyList<Occurrence> items, IReadOnlyList<string>? columns = null, int maxLength = MaxNotificationLength, string bullet = "")
    {
        var nl = Environment.NewLine;
        var text = "";
        for (int i = 0; i < items.Count; i++)
        {
            var line = items.Count == 1
                ? Describe(items[i], columns)
                : bullet + string.Join(", ", Lines(items[i], columns)).ReplaceLineEndings(" ");
            int rest = items.Count - i - 1;
            var tail = rest > 0 ? $"{nl}и ещё {rest}" : "";
            if (i == 0 && line.Length + tail.Length > maxLength)
                return string.Concat(line.AsSpan(0, maxLength - tail.Length - 1), "…", tail); // even the first one is too long
            var next = i == 0 ? line : text + nl + line;
            if (next.Length + tail.Length > maxLength)
                return $"{text}{nl}и ещё {rest + 1}"; // fits: the previous step reserved exactly this
            text = next;
        }
        return text;
    }

    /// <summary>Ticked "remind before" boxes in one short line: "1, 2 дн.; 1 нед.; 3 мес.", "нет" when none.</summary>
    public static string ShortReminders(Reminder r)
    {
        // Same order as the flags: five days, five weeks, three months.
        string? Part(int first, int count, string unit)
        {
            var ticked = Enumerable.Range(0, count).Where(i => r.HasFlag((Reminder)(1 << (first + i)))).Select(i => i + 1).ToList();
            return ticked.Count == 0 ? null : $"{string.Join(", ", ticked)} {unit}";
        }
        var parts = new[] { Part(0, 5, "дн."), Part(5, 5, "нед."), Part(10, 3, "мес.") }.OfType<string>().ToList();
        return parts.Count == 0 ? "нет" : string.Join("; ", parts);
    }

    private static readonly string[] MonthsGenitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    // Indexed like DayOfWeek (Sunday = 0); gender picks the ordinal ending: 2-й понедельник, 2-я среда, 2-е воскресенье.
    private static readonly (string Name, char Gender)[] Weekdays =
        [("воскресенье", 'n'), ("понедельник", 'm'), ("вторник", 'm'), ("среда", 'f'), ("четверг", 'm'), ("пятница", 'f'), ("суббота", 'f')];

    /// <summary>The rule as the user entered it: "14.05.1990", "14.05", "2-е воскресенье мая", "Пасха + 49 дн.".</summary>
    public static string DescribeDate(EventDate date) => date switch
    {
        FixedDate { Year: int y } d => $"{d.Day:00}.{d.Month:00}.{y}",
        FixedDate d => $"{d.Day:00}.{d.Month:00}",
        NthWeekdayDate d => $"{Ordinal(d.Nth, Weekdays[(int)d.Weekday].Gender)} {Weekdays[(int)d.Weekday].Name} {MonthsGenitive[d.Month - 1]}",
        EasterDate { OffsetDays: 0 } => "Пасха",
        EasterDate { OffsetDays: > 0 } e => $"Пасха + {e.OffsetDays} дн.",
        EasterDate e => $"Пасха − {-e.OffsetDays} дн.",
        _ => date.ToString(),
    };

    /// <summary>One entry in a pick list after its name: "14.05.1990, День Рождения".</summary>
    public static string Brief(Entry e) => $"{DescribeDate(e.Date)}, {e.EventType}";

    private static string Ordinal(int nth, char gender) => (nth, gender) switch
    {
        (NthWeekdayDate.Last, 'm') => "последний",
        (NthWeekdayDate.Last, 'f') => "последняя",
        (NthWeekdayDate.Last, _) => "последнее",
        (_, 'm') => $"{nth}-й",
        (_, 'f') => $"{nth}-я",
        _ => $"{nth}-е",
    };

    public static string Describe(IEnumerable<Occurrence> occurrences) =>
        string.Join($"{Environment.NewLine}{Separator}{Environment.NewLine}", occurrences.Select(o => Describe(o)));
}
