using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BirthdayChecker.Core;

/// <summary>Something the user should look at after import. <see cref="Line"/> is 1-based.</summary>
public sealed record ImportIssue(int Line, string Name, string Message);

public sealed record ImportResult(IReadOnlyList<Entry> Entries, IReadOnlyList<ImportIssue> Issues);

/// <summary>
/// One-time import of the legacy Birthday v1.2.2.8 text export (Import.txt, cp1251):
/// <c>MM.DD.YYYY;name;type;comment;phone;mail;icq;photo;13×True/False;color;onceInYear;zodiac</c>.
/// Safe fixes are applied silently (whitespace, year 0001 = unknown, "N-th weekday of month" comments);
/// anything doubtful is imported as is and reported in <see cref="ImportResult.Issues"/>.
/// Colors and the zodiac flag are dropped by design.
/// </summary>
public static partial class LegacyImport
{
    private const int FlagsStart = 8;
    private const int FlagCount = 13;
    private const int OnceInYearField = FlagsStart + FlagCount + 1;
    private const int MaxFields = FlagsStart + FlagCount + 3; // + color, onceInYear, zodiac

    public static ImportResult ReadFile(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Parse(File.ReadAllText(path, Encoding.GetEncoding(1251)));
    }

    public static ImportResult Parse(string text)
    {
        var entries = new List<Entry>();
        var issues = new List<ImportIssue>();
        var seen = new Dictionary<(int, int, string), string>();
        // Delphi writes CRLF; split on \n only so line numbers stay physical.

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var f = lines[i].TrimEnd('\r').Split(';');
            string name = Clean(f.ElementAtOrDefault(1));
            void Issue(string message) => issues.Add(new ImportIssue(lineNo, name, message));

            // A ';' inside a name or comment shifts every later column; the flag block is the anchor.
            if (f.Length is < FlagsStart + FlagCount or > MaxFields
                || !f.AsSpan(FlagsStart, FlagCount).ToArray().All(v => TryParseFlag(v, out _)))
            {
                Issue($"пропущена: неожиданная раскладка полей ({f.Length})");
                continue;
            }
            string type = Clean(f[2]);
            if (name.Length == 0 || type.Length == 0)
            {
                Issue("пропущена: пустое имя или тип события");
                continue;
            }
            if (!TryParseDate(f[0], out int month, out int day, out int year))
            {
                Issue($"пропущена: неверная дата «{f[0]}»");
                continue;
            }

            string comment = Clean(f[3]);
            foreach (var (label, value) in new[] { ("Почта", f[5]), ("ICQ", f[6]), ("Фото", f[7]) })
                if (Clean(value) is { Length: > 0 } extra)
                {
                    comment = comment.Length == 0 ? $"{label}: {extra}" : $"{comment}; {label}: {extra}";
                    Issue($"{label}: перенесено в комментарий");
                }

            int? knownYear = year;
            if (year == 1)
                knownYear = null;
            else if (year <= 1000)
            {
                knownYear = null;
                Issue($"год {year} считаю неизвестным");
            }
            // ponytail: heuristic — the legacy app often stored the entry year instead of the real one.
            // Reported below, and only if the date stays fixed (a floating date has no year).
            bool recentYear = year >= 2020;

            EventDate date;
            try
            {
                date = new FixedDate(month, day, knownYear);
            }
            catch (ArgumentOutOfRangeException)
            {
                Issue($"пропущена: несуществующая дата «{f[0]}»");
                continue;
            }

            // Only a holiday whose comment names the same month is converted; anything else stays as entered.
            if (FloatingFromComment(comment) is { } floating)
            {
                if (floating.Month == month && !type.Contains("Рожд", StringComparison.OrdinalIgnoreCase))
                {
                    date = floating;
                    Issue($"дата стала плавающей по комментарию (было {f[0].Trim()})");
                }
                else
                    Issue("комментарий похож на плавающую дату — оставлена фиксированная");
            }
            if (recentYear && date is FixedDate)
                Issue($"год {year}: проверь");

            if (TryParseFlag(f.ElementAtOrDefault(OnceInYearField), out bool once) && once)
                Issue("было «разово для года», теперь напоминает каждый год");

            var key = (month, day, type.ToLowerInvariant());
            if (!seen.TryAdd(key, name))
                Issue($"возможный дубль «{seen[key]}» (тот же день и тип)");

            entries.Add(new Entry(name, type, date)
            {
                Phone = NullIfEmpty(Clean(f[4])),
                Comment = NullIfEmpty(comment),
                Reminders = ParseFlags(f.AsSpan(FlagsStart, FlagCount)),
            });
        }
        return new ImportResult(entries, issues);
    }

    private static bool TryParseDate(string s, out int month, out int day, out int year)
    {
        month = day = year = 0;
        var parts = s.Trim().Split('.');
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out day)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && year >= 1;
    }

    private static Reminder ParseFlags(ReadOnlySpan<string> flags)
    {
        var result = Reminder.None;
        for (int i = 0; i < flags.Length; i++)
            if (TryParseFlag(flags[i], out bool on) && on)
                result |= (Reminder)(1 << i);
        return result;
    }

    /// <summary>The legacy help documents both True/False and 1/0.</summary>
    private static bool TryParseFlag(string? s, out bool value)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "true" or "1": value = true; return true;
            case "false" or "0": value = false; return true;
            default: value = false; return false;
        }
    }

    // Indexed like DayOfWeek (Sunday = 0), months (January = 0) and ordinals (first = 0).
    private static readonly string[] WeekdayStems = ["воскрес", "понедел", "вторн", "сред", "четверг", "пятниц", "суббот"];
    private static readonly string[] MonthStems = ["январ", "феврал", "март", "апрел", "ма", "июн", "июл", "август", "сентябр", "октябр", "ноябр", "декабр"];
    private static readonly string[] OrdinalStems = ["перв", "втор", "трет", "четв"];

    /// <summary>"2 воскресенье мая", "третье воскресенье июня", "последнее воскресенье ноября".</summary>
    internal static NthWeekdayDate? FloatingFromComment(string comment)
    {
        var m = FloatingRegex().Match(comment);
        if (!m.Success)
            return null;

        static int Stem(string[] stems, string word) =>
            Array.FindIndex(stems, s => word.StartsWith(s, StringComparison.OrdinalIgnoreCase));

        string n = m.Groups["n"].Value;
        int nth = char.IsAsciiDigit(n[0]) ? n[0] - '0'
            : Stem(OrdinalStems, n) is var o and >= 0 ? o + 1
            : NthWeekdayDate.Last;
        return new NthWeekdayDate(
            Stem(MonthStems, m.Groups["mon"].Value) + 1,
            (DayOfWeek)Stem(WeekdayStems, m.Groups["wd"].Value),
            nth);
    }

    [GeneratedRegex(
        @"(?<!\w)(?<n>[1-4]|перв\w*|втор\w*|трет\w*|четв[её]рт\w*|последн\w*)(?:-?[еоя]{1,2})?\s+" +
        @"(?<wd>воскрес\w*|понедел\w*|вторн\w*|сред\w*|четверг\w*|пятниц\w*|суббот\w*)\s+" +
        @"(?<mon>январ\w*|феврал\w*|март\w*|апрел\w*|ма[йя]\w*|июн\w*|июл\w*|август\w*|сентябр\w*|октябр\w*|ноябр\w*|декабр\w*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FloatingRegex();

    private static string Clean(string? s) => s is null ? "" : WhitespaceRegex().Replace(s, " ").Trim();

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
