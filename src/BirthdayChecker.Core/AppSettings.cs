using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BirthdayChecker.Core;

public enum PopupPosition { BottomRight, BottomLeft, TopRight, TopLeft, Center }

public enum SoundMode { Off, System, File }

/// <summary>How a reminder appears: the app's own styled window, or a plain Windows notification.</summary>
public enum NotificationMode { Window, Windows }

public sealed record ColumnLayout(string Key, int Index, double Width);

/// <summary>App settings in settings.json next to the exe — separate from entries.json (single-writer rule).</summary>
public sealed partial record AppSettings
{
    public const int DefaultInterval = 180;

    /// <summary>Same choices as the legacy app: 5–60 minutes, 3–24 hours.</summary>
    public static readonly int[] IntervalChoices = [5, 10, 20, 40, 60, 180, 360, 720, 1440];

    /// <summary>Upper limit of <see cref="AutoCloseSeconds"/>.</summary>
    public const int MaxAutoCloseSeconds = 3600;

    /// <summary>Upper limit of <see cref="BackupPeriodDays"/>.</summary>
    public const int MaxBackupPeriodDays = 365;

    /// <summary>Days between automatic copies of the entries; 0 = no copies.</summary>
    public int BackupPeriodDays { get; init; } = 7;

    public int CheckIntervalMinutes { get; init; } = DefaultInterval;

    /// <summary>Look for a new version a minute after start; off leaves only the tray menu's check.</summary>
    public bool CheckUpdates { get; init; } = true;
    public NotificationMode Notification { get; init; } = NotificationMode.Window;

    /// <summary>Ticked columns of a reminder, name and date are always there; null = <see cref="ColumnCatalog.ReminderDefault"/>.</summary>
    public IReadOnlyList<string>? ReminderColumns { get; init; }

    public PopupPosition Position { get; init; } = PopupPosition.BottomRight;
    /// <summary>0 = the popup stays until closed.</summary>
    public int AutoCloseSeconds { get; init; }
    public SoundMode Sound { get; init; } = SoundMode.System;
    public string? SoundFile { get; init; }

    /// <summary>"#RRGGBB"; null = system default.</summary>
    public string? TextColor { get; init; }

    /// <summary>"#RRGGBB"; null = system default.</summary>
    public string? BackgroundColor { get; init; }

    public const string DefaultReminderTodayColor = "#FFD9D9";

    /// <summary>"#RRGGBB" plate behind today's events in the reminder popup.</summary>
    public string ReminderTodayColor { get; init; } = DefaultReminderTodayColor;

    public string? BackgroundImage { get; init; }

    public bool DarkTheme { get; init; }

    /// <summary>Main window column order and widths, keyed by header; null = defaults.</summary>
    public IReadOnlyList<ColumnLayout>? Columns { get; init; }

    /// <summary>Names of the user's own columns, see <see cref="ColumnCatalog"/>.</summary>
    public IReadOnlyList<string>? CustomColumns { get; init; }

    /// <summary>Headers of the columns switched off in the settings.</summary>
    public IReadOnlyList<string>? HiddenColumns { get; init; }

    public double? ListWidth { get; init; }
    public double? ListHeight { get; init; }

    /// <summary>The shared tag list with colors; null = defaults, until first edited.</summary>
    public IReadOnlyList<TagDefinition>? Tags { get; init; }

    /// <summary>The shared event type list with its calendar markers; null = defaults, until first edited.</summary>
    public IReadOnlyList<EventDefinition>? EventTypes { get; init; }

    /// <summary>"#RRGGBB" of today's marker and ring.</summary>
    public string TodayColor { get; init; } = EventCatalog.DefaultTodayColor;

    /// <summary>"#RRGGBB" of the circle on a day with several events.</summary>
    public string SeveralColor { get; init; } = EventCatalog.DefaultSeveralColor;

    /// <summary>The user's own colors of the color window.</summary>
    public IReadOnlyList<string>? CustomColors { get; init; }

    /// <summary>Width of the calendar set with its divider; null = by the window's width.</summary>
    public double? CalendarWidth { get; init; }

    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <remarks>
    /// ponytail: a missing or unparsable file (incl. a wrong type or enum name) silently falls back to defaults;
    /// a parsable file has each out-of-range value reset on its own — settings are cheap to redo.
    /// </remarks>
    public static AppSettings Load(string path) =>
        (JsonFile.LoadOrNull<AppSettings>(path, Options) ?? new AppSettings()).Sanitized();

    public static void Save(string path, AppSettings settings) => JsonFile.Save(path, settings, Options);

    /// <summary>Digits only, 0..<see cref="MaxAutoCloseSeconds"/>: what the settings window accepts.</summary>
    public static bool TryParseAutoClose(string? text, out int seconds) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds <= MaxAutoCloseSeconds;

    /// <summary>Digits only, 0..<see cref="MaxBackupPeriodDays"/>: what the settings window accepts.</summary>
    public static bool TryParseBackupPeriod(string? text, out int days) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out days) && days <= MaxBackupPeriodDays;

    private AppSettings Sanitized()
    {
        var d = new AppSettings();
        return this with
        {
            CheckIntervalMinutes = IntervalChoices.Contains(CheckIntervalMinutes) ? CheckIntervalMinutes : d.CheckIntervalMinutes,
            AutoCloseSeconds = AutoCloseSeconds is >= 0 and <= MaxAutoCloseSeconds ? AutoCloseSeconds : d.AutoCloseSeconds,
            BackupPeriodDays = BackupPeriodDays is >= 0 and <= MaxBackupPeriodDays ? BackupPeriodDays : d.BackupPeriodDays,
            Notification = Enum.IsDefined(Notification) ? Notification : d.Notification,
            ReminderColumns = ReminderColumns?.Where(c => !string.IsNullOrWhiteSpace(c) && !ColumnCatalog.IsLocked(c)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
            Position = Enum.IsDefined(Position) ? Position : d.Position,
            Sound = Enum.IsDefined(Sound) ? Sound : d.Sound,
            TextColor = IsColor(TextColor) ? TextColor : null,
            BackgroundColor = IsColor(BackgroundColor) ? BackgroundColor : null,
            ReminderTodayColor = IsColor(ReminderTodayColor) ? ReminderTodayColor : DefaultReminderTodayColor,
            SoundFile = IsFullPath(SoundFile) ? SoundFile : null,
            BackgroundImage = IsFullPath(BackgroundImage) ? BackgroundImage : null,
            Columns = Columns?.Where(c => c is { Key.Length: > 0, Index: >= 0, Width: > 10 and < 3000 }).ToList(),
            CustomColumns = CustomColumns?.Where(ColumnCatalog.IsValidName).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
            HiddenColumns = HiddenColumns?.Where(c => !string.IsNullOrWhiteSpace(c) && !ColumnCatalog.IsLocked(c)).ToList(),
            ListWidth = ListWidth is >= 400 and <= 10000 ? ListWidth : null,
            ListHeight = ListHeight is >= 250 and <= 10000 ? ListHeight : null,
            Tags = Tags?.Where(t => t is { Name: { } name } && TagCatalog.IsValidName(name))
                .DistinctBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => t with { Color = IsColor(t.Color) ? t.Color : null })
                .ToList(),
            EventTypes = EventTypes?.Where(t => t is { Name: { } name } && !string.IsNullOrWhiteSpace(name))
                .DistinctBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => t with
                {
                    Color = IsColor(t.Color) ? t.Color : EventCatalog.OtherColor,
                    Shape = Enum.IsDefined(t.Shape) ? t.Shape : MarkerShape.Circle,
                })
                .ToList(),
            TodayColor = IsColor(TodayColor) ? TodayColor : d.TodayColor,
            SeveralColor = IsColor(SeveralColor) ? SeveralColor : d.SeveralColor,
            CustomColors = CustomColors?.Where(IsColor).ToList(),
            CalendarWidth = CalendarWidth is >= 100 and <= 10000 ? CalendarWidth : null,
        };
    }

    // A relative path would resolve against whatever the current directory is, and new Uri() rejects it.
    private static bool IsFullPath(string? s) => s is not null && Path.IsPathFullyQualified(s);

    private static bool IsColor(string? s) => s is not null && ColorRegex().IsMatch(s);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorRegex();
}

/// <summary>State of the reminder popup in state.json: check days (for catch-up), "not today" dismissals and "later" snoozes.</summary>
public sealed record TrayState
{
    private static readonly IReadOnlyDictionary<Guid, DateTime> NoSnoozes = new Dictionary<Guid, DateTime>();

    public DateOnly? LastChecked { get; init; }

    /// <summary>
    /// The last check day before today. Fixed for the whole day, so reminders caught up at the first
    /// check stay due on every later check that day (until dismissed), not just once.
    /// </summary>
    public DateOnly? CatchUpSince { get; init; }

    public DateOnly? DismissedOn { get; init; }
    public IReadOnlyList<Guid> Dismissed { get; init; } = [];

    /// <summary>Events hidden by "later", with the moment each one comes back.</summary>
    public IReadOnlyDictionary<Guid, DateTime> Snoozed { get; init; } = NoSnoozes;

    /// <summary>Call before each check; only the first check of a new day moves the catch-up window.</summary>
    public TrayState StartDay(DateOnly today) =>
        LastChecked == today ? this : this with { CatchUpSince = LastChecked, LastChecked = today };

    public bool IsDismissed(Guid id, DateOnly today) => DismissedOn == today && Dismissed.Contains(id);

    /// <summary>Dismissals only live for one day; a new day starts a fresh list.</summary>
    public TrayState Dismiss(Guid id, DateOnly today) => this with
    {
        DismissedOn = today,
        Dismissed = DismissedOn == today ? [.. Dismissed, id] : [id],
    };

    public bool IsSnoozed(Guid id, DateTime now) => Snoozed.TryGetValue(id, out var until) && until > now;

    /// <summary>Expired snoozes are dropped here, so the list doesn't grow.</summary>
    public TrayState Snooze(Guid id, DateTime until, DateTime now) => this with
    {
        Snoozed = Snoozed.Where(s => s.Value > now && s.Key != id).Append(new(id, until)).ToDictionary(),
    };

    /// <summary>When the next snoozed event comes back; null when none is waiting.</summary>
    public DateTime? NextSnoozeEnd(DateTime now) =>
        Snoozed.Values.Where(until => until > now).Select(until => (DateTime?)until).Min();

    /// <remarks>ponytail: a broken state file just means one possibly repeated or missed catch-up — defaults are fine.</remarks>
    public static TrayState Load(string path) =>
        JsonFile.LoadOrNull<TrayState>(path, AppSettings.Options) is { } s
            ? s with { Dismissed = s.Dismissed ?? [], Snoozed = s.Snoozed ?? NoSnoozes } // an explicit null in a hand-edited file
            : new TrayState();

    public static void Save(string path, TrayState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        JsonFile.Save(path, state, AppSettings.Options);
    }
}

/// <summary>Lenient JSON file for non-critical data (settings, popup state). Entries use the strict EntryStorage.</summary>
internal static class JsonFile
{
    public static T? LoadOrNull<T>(string path, JsonSerializerOptions options) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Save<T>(string path, T value, JsonSerializerOptions options) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, options));
}
