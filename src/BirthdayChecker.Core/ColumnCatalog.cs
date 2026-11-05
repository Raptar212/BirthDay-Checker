namespace BirthdayChecker.Core;

/// <summary>The main table's columns: the built-in ones, and the user's text columns kept in settings.json with values in entries.</summary>
public static class ColumnCatalog
{
    private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>Headers of the built-in columns, also the keys of their saved layout.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = ["Имя", "Дата", "Событие", "Теги", "Возраст", "Через, дн.", "Телефон", "Комментарий"];

    /// <summary>Columns a reminder shows until the user chooses: all it ever showed. New columns start switched off.</summary>
    public static readonly IReadOnlyList<string> ReminderDefault = ["Событие", "Возраст", "Через, дн.", "Телефон", "Комментарий"];

    /// <summary>These can't be hidden: without them the rows can't be told apart.</summary>
    public static bool IsLocked(string name) => Ci.Equals(name, "Имя") || Ci.Equals(name, "Дата");

    /// <summary>
    /// A name is also a column header in exported files: not one of the fixed ones, no line breaks or tabs, no spaces
    /// around it (import trims headers), no first character that Excel would run as a formula or a Markdown divider.
    /// </summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name == name.Trim() && !name.Any(char.IsControl)
        && !"=+-@'".Contains(name[0]) && !BuiltIn.Contains(name, Ci) && !EntryCsv.Header.Contains(name, Ci);

    /// <summary>What is wrong with a new name, null if fine. <paramref name="renaming"/> may change its own case.</summary>
    public static string? NameProblem(string name, IReadOnlyList<string> columns, string? renaming = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Укажите название столбца.";
        if (!IsValidName(name))
            return "Такое название занято программой или начинается с недопустимого символа (= + - @ ').";
        return columns.Any(c => Ci.Equals(c, name) && !Ci.Equals(c, renaming)) ? "Такой столбец уже есть." : null;
    }

    /// <summary>The saved list, then names only entries know, e.g. from an import or a restored copy: their values must stay visible.</summary>
    public static IReadOnlyList<string> Merge(IReadOnlyList<string>? saved, IEnumerable<Entry> entries)
    {
        var known = saved ?? [];
        var missing = entries.SelectMany(e => e.CustomFields?.Keys ?? [])
            .Where(n => !known.Contains(n, Ci))
            .Distinct(Ci)
            .Order(Ci);
        return [.. known, .. missing];
    }

    public static IReadOnlyList<string> Rename(IEnumerable<string> columns, string name, string newName) =>
        columns.Select(c => Ci.Equals(c, name) ? newName : c).Distinct(Ci).ToList();

    public static IReadOnlyList<string> Remove(IEnumerable<string> columns, string name) =>
        columns.Where(c => !Ci.Equals(c, name)).ToList();

    public static string? Value(Entry entry, string column) =>
        entry.CustomFields?.FirstOrDefault(f => Ci.Equals(f.Key, column)).Value;

    /// <summary>A blank value removes the field. The same entry when nothing changes: an equal entry is not re-saved.</summary>
    public static Entry With(Entry entry, string column, string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value;
        if (Value(entry, column) == value)
            return entry;
        var fields = (entry.CustomFields ?? new Dictionary<string, string>())
            .Where(f => !Ci.Equals(f.Key, column))
            .ToDictionary();
        if (value is not null)
            fields[column] = value;
        return entry with { CustomFields = fields.Count > 0 ? fields : null };
    }

    public static List<Entry> RenameIn(IEnumerable<Entry> entries, string name, string newName) =>
        entries.Select(e => Value(e, name) is { } value ? With(With(e, name, null), newName, value) : e).ToList();

    public static List<Entry> RemoveFrom(IEnumerable<Entry> entries, string name) =>
        entries.Select(e => With(e, name, null)).ToList();

    /// <summary>For the strict file check: what the app can't write itself must not load.</summary>
    internal static bool AreValid(IReadOnlyDictionary<string, string> fields) =>
        fields.All(f => IsValidName(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
        && fields.Keys.Distinct(Ci).Count() == fields.Count;
}
