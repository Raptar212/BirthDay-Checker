using System.Globalization;
using System.Text;

namespace BirthdayChecker.Core;

/// <summary>
/// CSV for Excel (";"-separated, UTF-8 with BOM written by the caller). Round-trips every field, so a
/// list can be edited in Excel and imported back. Import adds entries; <see cref="Duplicates"/> finds
/// the ones that already exist so the caller can ask whether to replace them.
/// </summary>
public static class EntryCsv
{
    private const char Sep = ';';
    private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

    private const string KindFixed = "число", KindNth = "день недели", KindEaster = "пасха", Last = "последний";

    // Monday first, as in the editor; index + 1 mod 7 = DayOfWeek.
    private static readonly string[] Weekdays = ["понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье"];

    internal static readonly string[] Header =
        ["Имя", "Событие", "Тип даты", "Месяц", "День", "Год", "День недели", "Номер", "Смещение от Пасхи",
         "Телефон", "Комментарий", "Теги", "Напомнить", "Цвет", "Id"];

    public static string Export(IEnumerable<Entry> entries)
    {
        var text = new StringBuilder();
        foreach (var row in ToRows(entries, excel: true))
            AppendRow(text, row);
        return text.ToString();
    }

    /// <summary>Header plus one row per entry, shared by the text formats. <paramref name="excel"/> adds the guards only Excel needs.</summary>
    internal static IEnumerable<string[]> ToRows(IEnumerable<Entry> entries, bool excel)
    {
        var list = entries.ToList();
        var custom = ColumnCatalog.Merge(null, list);
        yield return [.. Header, .. custom];
        foreach (var e in list)
        {
            var (kind, month, day, year, weekday, nth, offset) = e.Date switch
            {
                FixedDate f => (KindFixed, Num(f.Month), Num(f.Day), f.Year is int y ? Num(y) : "", "", "", ""),
                NthWeekdayDate n => (KindNth, Num(n.Month), "", "", Weekdays[((int)n.Weekday + 6) % 7],
                    n.Nth == NthWeekdayDate.Last ? Last : Num(n.Nth), ""),
                EasterDate x => (KindEaster, "", "", "", "", "", Num(x.OffsetDays)),
                _ => throw new NotSupportedException(e.Date.GetType().Name),
            };
            string Text(string s) => excel ? Safe(s) : s;
            yield return [Text(e.Name), Text(e.EventType), kind, month, day, year, weekday, nth, offset,
                excel ? Phone(e.Phone) : e.Phone ?? "", Text(e.Comment ?? ""), Text(string.Join(", ", e.Tags)),
                e.Reminders == Reminder.None ? "" : e.Reminders.ToString(), e.Color ?? "", e.Id.ToString(),
                .. custom.Select(c => Text(ColumnCatalog.Value(e, c) ?? ""))];
        }
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    // Excel runs cells starting with = + - @ as formulas; a leading apostrophe keeps them text.
    // A value already looking like that gets one too, or Unsafe would strip its own apostrophe.
    private static string Safe(string s) => s.Length > 0 && ("=+-@".Contains(s[0]) || Unsafe(s) != s) ? "'" + s : s;

    private static string Unsafe(string s)
    {
        var rest = s.TrimStart('\'');
        return s.Length > rest.Length && rest.Length > 0 && "=+-@".Contains(rest[0]) ? s[1..] : s;
    }

    // ="..." makes Excel keep "+7..." / "8916..." as text instead of a number that loses "+" or turns into 7,9E+10.
    private static string Phone(string? phone) => string.IsNullOrEmpty(phone) ? "" : $"=\"{phone.Replace("\"", "")}\"";

    private static string UnPhone(string s) =>
        s.StartsWith("=\"", StringComparison.Ordinal) && s.EndsWith('"') && s.Length >= 3 ? s[2..^1] : s;

    /// <summary>UTF-8 (with or without BOM); anything that isn't valid UTF-8 is read as cp1251 — Excel's plain "CSV" in Russian Windows.</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    private static void AppendRow(StringBuilder text, IEnumerable<string> fields)
    {
        text.AppendJoin(Sep, fields.Select(Quote));
        text.Append("\r\n");
    }

    private static string Quote(string s) =>
        s.AsSpan().ContainsAny(";\"\r\n") ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    /// <summary>
    /// Bad rows are skipped with an issue; a missing column or an unclosed quote fails the whole file
    /// (one issue, no entries) — after a stray quote the rest of the rows can't be trusted.
    /// </summary>
    public static ImportResult Parse(string text)
    {
        var (allRows, unclosedAt) = ReadRows(text.TrimStart('﻿'));
        if (unclosedAt is int at)
            return new ImportResult([], [new ImportIssue(at, "", "незакрытая кавычка — файл не импортирован, проверьте эту строку")]);
        // Excel may put "sep=;" or blank lines above the header.
        var rows = allRows.SkipWhile(r => r.Fields.All(string.IsNullOrWhiteSpace) || r.Fields[0].StartsWith("sep=", StringComparison.OrdinalIgnoreCase)).ToList();
        return FromRows(rows, excel: true);
    }

    /// <summary>First row is the header. Bad rows are skipped with an issue; a missing column fails the whole file.</summary>
    internal static ImportResult FromRows(List<(int Line, List<string> Fields)> rows, bool excel)
    {
        var issues = new List<ImportIssue>();
        if (rows.Count == 0)
            return new ImportResult([], [new ImportIssue(1, "", "файл пуст")]);

        var columns = rows[0].Fields.Select((name, i) => (name: name.Trim(), i))
            .GroupBy(c => c.name, Ci).ToDictionary(g => g.Key, g => g.First().i, Ci);
        var missing = Header.Where(h => h != "Id" && !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            return new ImportResult([], [new ImportIssue(rows[0].Line, "", $"нет колонок: {string.Join(", ", missing)} — это не экспорт BirthDay Checker? (Import.txt старой программы импортируется из меню трея)")]);

        // Any other column is the user's own, whatever made the file: its values must not vanish on import.
        var others = columns.Keys.Where(c => c.Length > 0 && !Header.Contains(c, Ci)).ToList();
        var custom = others.Where(ColumnCatalog.IsValidName).ToList();
        issues.AddRange(others.Except(custom).Select(c => new ImportIssue(rows[0].Line, "", $"колонка «{c}» пропущена: такое название столбца недопустимо")));

        var entries = new List<Entry>();
        var ids = new HashSet<Guid>();
        var seen = new Dictionary<(string, string, EventDate), int>();
        foreach (var (line, fields) in rows.Skip(1))
        {
            if (fields.All(string.IsNullOrWhiteSpace))
                continue;
            string Get(string column)
            {
                if (!columns.TryGetValue(column, out int i) || i >= fields.Count)
                    return "";
                var value = fields[i].Trim();
                return excel ? Unsafe(value) : value;
            }
            string name = Get("Имя");
            try
            {
                // A stray separator shifts values into other columns; a short row would overwrite a good entry with blanks.
                if (!excel && fields.Count != rows[0].Fields.Count)
                    throw new FormatException($"колонок {fields.Count} вместо {rows[0].Fields.Count} — лишний разделитель или разорванная строка");
                if (name.Length == 0 || Get("Событие").Length == 0)
                    throw new FormatException("пустое имя или событие");
                var phone = excel ? UnPhone(Get("Телефон")) : Get("Телефон");
                if (System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\d+[.,]?\d*E\+\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new FormatException($"телефон «{phone}» испорчен Excel (число в научной записи)");
                var entry = new Entry(name, Get("Событие"), ParseDate(Get))
                {
                    Phone = NullIfEmpty(phone),
                    Comment = NullIfEmpty(Get("Комментарий")),
                    Tags = EntryList.ParseTags(Get("Теги")),
                    Reminders = ParseReminders(Get("Напомнить")),
                    Color = NullIfEmpty(Get("Цвет")),
                };
                if (entry.Color is { } c && !Entry.IsColor(c))
                    throw new FormatException($"цвет «{c}» не в формате #RRGGBB");
                entry = custom.Aggregate(entry, (x, column) => ColumnCatalog.With(x, column, Get(column)));

                var key = (entry.Name.ToLowerInvariant(), entry.EventType.ToLowerInvariant(), entry.Date);
                if (seen.TryGetValue(key, out int firstLine))
                    throw new FormatException($"дубль строки {firstLine} (то же имя, событие и дата)");
                seen[key] = line;

                if (Guid.TryParse(Get("Id"), out var id) && id != Guid.Empty && ids.Add(id))
                    entry = entry with { Id = id };
                else
                    ids.Add(entry.Id); // new or repeated id: keep the fresh one
                entries.Add(entry);
            }
            catch (FormatException e)
            {
                issues.Add(new ImportIssue(line, name, $"пропущена: {e.Message}"));
            }
            catch (ArgumentOutOfRangeException)
            {
                issues.Add(new ImportIssue(line, name, "пропущена: несуществующая дата или значение вне допустимого диапазона"));
            }
        }
        return new ImportResult(entries, issues);
    }

    private static EventDate ParseDate(Func<string, string> get)
    {
        int Int(string column) => int.TryParse(get(column).Replace('−', '-'), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out int v) ? v : throw new FormatException($"«{column}»: «{get(column)}» — не число");

        return get("Тип даты").ToLowerInvariant() switch
        {
            KindFixed => new FixedDate(Int("Месяц"), Int("День"), get("Год").Length == 0 ? null : Int("Год")),
            KindNth => new NthWeekdayDate(Int("Месяц"), ParseWeekday(get("День недели")),
                Ci.Equals(get("Номер"), Last) ? NthWeekdayDate.Last : Int("Номер")),
            KindEaster => new EasterDate(Int("Смещение от Пасхи")),
            var other => throw new FormatException($"тип даты «{other}» — ожидается «{KindFixed}», «{KindNth}» или «{KindEaster}»"),
        };
    }

    private static DayOfWeek ParseWeekday(string s)
    {
        int i = Array.FindIndex(Weekdays, w => Ci.Equals(w, s));
        return i >= 0 ? (DayOfWeek)((i + 1) % 7) : throw new FormatException($"день недели «{s}»");
    }

    private static Reminder ParseReminders(string s)
    {
        var result = Reminder.None;
        // Names only: Enum.TryParse would also accept arbitrary numbers.
        foreach (var part in s.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            result |= !int.TryParse(part, out _) && Enum.TryParse<Reminder>(part, ignoreCase: true, out var r) && Enum.IsDefined(r)
                ? r
                : throw new FormatException($"«Напомнить»: «{part}» — ожидается Day1…Day5, Week1…Week5, Month1…Month3");
        return result;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>RFC 4180 with ";": quoted fields may contain ";", quotes ("") and line breaks.</summary>
    /// <returns>Rows with their first line number; <c>UnclosedAt</c> = line of a quote that never closes.</returns>
    private static (List<(int Line, List<string> Fields)> Rows, int? UnclosedAt) ReadRows(string text)
    {
        var rows = new List<(int Line, List<string> Fields)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        int line = 1, rowStart = 1, quoteLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: quoted = true; quoteLine = line; break;
                case Sep: fields.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString()); field.Clear();
                    rows.Add((rowStart, fields));
                    fields = [];
                    rowStart = ++line;
                    break;
                default: field.Append(c); break;
            }
        }
        if (quoted)
            return (rows, quoteLine);
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add((rowStart, fields));
        }
        return (rows, null);
    }

    public sealed record MergeResult(List<Entry> Entries, int Added, int Skipped);

    /// <summary>
    /// The incoming entries that are not in <paramref name="current"/> yet. An incoming entry matches an existing one
    /// by id, otherwise by the same name, event type and date rule; each existing entry is matched at most once.
    /// </summary>
    public static List<Entry> Unique(IReadOnlyList<Entry> current, IEnumerable<Entry> incoming)
    {
        var rows = incoming.ToList();
        var byId = current.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var used = new HashSet<int>();
        var matched = new bool[rows.Count];

        // Two passes: every id match first, so a new row with the same name can't take an entry whose own row is further down.
        for (int k = 0; k < rows.Count; k++)
            matched[k] = byId.TryGetValue(rows[k].Id, out int i) && used.Add(i);
        for (int k = 0; k < rows.Count; k++)
            if (!matched[k])
            {
                var n = rows[k];
                int hit = current.Select((e, j) => (e, j)).FirstOrDefault(x => !used.Contains(x.j)
                    && Ci.Equals(x.e.Name, n.Name) && Ci.Equals(x.e.EventType, n.EventType) && x.e.Date == n.Date, (null!, -1)).j;
                matched[k] = hit >= 0 && used.Add(hit);
            }
        return [.. rows.Where((_, k) => !matched[k])];
    }

    /// <summary>Adds the <see cref="Unique"/> incoming entries and skips the rest; an added entry whose id is taken gets a new one.</summary>
    public static MergeResult Merge(IReadOnlyList<Entry> current, IEnumerable<Entry> incoming)
    {
        var rows = incoming.ToList();
        var unique = Unique(current, rows);
        var taken = current.Select(e => e.Id).ToHashSet();
        var result = current.ToList();
        result.AddRange(unique.Select(n => taken.Add(n.Id) ? n : n with { Id = Guid.NewGuid() }));
        return new MergeResult(result, unique.Count, rows.Count - unique.Count);
    }
}
