namespace BirthdayChecker.Core;

/// <summary>The figure of an event on the calendar.</summary>
public enum MarkerShape { Circle, Diamond, Square, Hexagon, Pentagon, Drop, Triangle }

/// <summary>An event type of the shared list with its calendar marker; <see cref="Color"/> is "#RRGGBB".</summary>
public sealed record EventDefinition(string Name, string Color = EventCatalog.OtherColor, MarkerShape Shape = MarkerShape.Circle);

/// <summary>The shared event type list kept in settings.json, and what changing it does to entries.</summary>
public static class EventCatalog
{
    public const string OtherColor = "#7A6FB5";
    public const string DefaultTodayColor = "#D93030";
    public const string DefaultSeveralColor = "#444444";

    private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

    private static readonly EventDefinition[] Defaults =
    [
        new("День Рождения", "#3F7FBF"),
        new("Годовщина свадьбы", "#C2559B"),
        new("Именины", "#3E9E6B"),
        new("Праздник", "#D98A1F"),
        new("Поминки", "#6B6B6B"),
    ];

    /// <summary>The saved list (defaults until first edited), then types only entries know, e.g. from an import.</summary>
    public static IReadOnlyList<EventDefinition> Merge(IReadOnlyList<EventDefinition>? saved, IEnumerable<Entry> entries)
    {
        var known = saved ?? Defaults;
        var missing = entries.Select(e => e.EventType)
            .Where(t => !known.Any(d => Ci.Equals(d.Name, t)))
            .Distinct(Ci)
            .Order(Ci)
            .Select(t => new EventDefinition(t));
        return [.. known, .. missing];
    }

    public static EventDefinition Find(IReadOnlyList<EventDefinition> types, string name) =>
        types.FirstOrDefault(d => Ci.Equals(d.Name, name)) ?? new EventDefinition(name);

    /// <summary>What is wrong with a new name, null if fine. <paramref name="renaming"/> may change its own case.</summary>
    public static string? NameProblem(string name, IReadOnlyList<EventDefinition> types, string? renaming = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Укажите название события.";
        return types.Any(d => Ci.Equals(d.Name, name) && !Ci.Equals(d.Name, renaming)) ? "Такое событие уже есть." : null;
    }

    public static IReadOnlyList<EventDefinition> Rename(IReadOnlyList<EventDefinition> types, string name, string newName) =>
        types.Select(d => Ci.Equals(d.Name, name) ? d with { Name = newName } : d).ToList();

    public static IReadOnlyList<EventDefinition> Remove(IReadOnlyList<EventDefinition> types, string name) =>
        types.Where(d => !Ci.Equals(d.Name, name)).ToList();

    public static IReadOnlyList<EventDefinition> Change(IReadOnlyList<EventDefinition> types, EventDefinition changed) =>
        types.Select(d => Ci.Equals(d.Name, changed.Name) ? changed : d).ToList();

    /// <summary>For a rename and for a removal with a replacement: the entries keep everything else.</summary>
    public static List<Entry> RenameIn(IEnumerable<Entry> entries, string name, string newName) =>
        entries.Select(e => Ci.Equals(e.EventType, name) ? e with { EventType = newName } : e).ToList();
}
