namespace BirthdayChecker.Core;

/// <summary>A tag of the shared list; <see cref="Color"/> is "#RRGGBB" for the rows that carry the tag, null = none.</summary>
public sealed record TagDefinition(string Name, string? Color = null);

/// <summary>The shared tag list kept in settings.json, and what changing it does to entries.</summary>
public static class TagCatalog
{
    private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>The saved list (defaults until first edited), then tags only entries know, e.g. from an import.</summary>
    public static IReadOnlyList<TagDefinition> Merge(IReadOnlyList<TagDefinition>? saved, IEnumerable<Entry> entries)
    {
        var known = saved ?? EntryList.DefaultTags.Select(t => new TagDefinition(t)).ToList();
        var missing = entries.SelectMany(e => e.Tags)
            .Where(t => !known.Any(d => Ci.Equals(d.Name, t)))
            .Distinct(Ci)
            .Order(Ci)
            .Select(t => new TagDefinition(t));
        return [.. known, .. missing];
    }

    /// <summary>The entry's own color wins; among its tags, the one listed first in the catalog.</summary>
    public static string? RowColor(Entry entry, IReadOnlyList<TagDefinition> tags) =>
        entry.Color ?? tags.FirstOrDefault(d => d.Color is not null && entry.Tags.Contains(d.Name, Ci))?.Color;

    /// <summary>Comma and semicolon separate tags in exported files.</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny([',', ';']) < 0;

    /// <summary>What is wrong with a new name, null if fine. <paramref name="renaming"/> may change its own case.</summary>
    public static string? NameProblem(string name, IReadOnlyList<TagDefinition> tags, string? renaming = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Укажите название тега.";
        if (!IsValidName(name))
            return "В названии тега не может быть запятой и точки с запятой.";
        return tags.Any(d => Ci.Equals(d.Name, name) && !Ci.Equals(d.Name, renaming)) ? "Такой тег уже есть." : null;
    }

    public static IReadOnlyList<TagDefinition> Add(IReadOnlyList<TagDefinition> tags, string name) =>
        [.. tags, new TagDefinition(name)];

    public static IReadOnlyList<TagDefinition> Recolor(IReadOnlyList<TagDefinition> tags, string name, string? color) =>
        tags.Select(d => Ci.Equals(d.Name, name) ? d with { Color = color } : d).ToList();

    public static IReadOnlyList<TagDefinition> Rename(IReadOnlyList<TagDefinition> tags, string name, string newName) =>
        tags.Select(d => Ci.Equals(d.Name, name) ? d with { Name = newName } : d).ToList();

    public static IReadOnlyList<TagDefinition> Remove(IReadOnlyList<TagDefinition> tags, string name) =>
        tags.Where(d => !Ci.Equals(d.Name, name)).ToList();

    public static List<Entry> RenameIn(IEnumerable<Entry> entries, string name, string newName) =>
        entries.Select(e => e.Tags.Contains(name, Ci)
            ? e with { Tags = e.Tags.Select(t => Ci.Equals(t, name) ? newName : t).Distinct(Ci).ToList() }
            : e).ToList();

    public static List<Entry> RemoveFrom(IEnumerable<Entry> entries, string name) =>
        entries.Select(e => e.Tags.Contains(name, Ci)
            ? e with { Tags = e.Tags.Where(t => !Ci.Equals(t, name)).ToList() }
            : e).ToList();
}
