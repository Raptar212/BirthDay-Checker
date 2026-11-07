using System.IO;
using System.Media;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BirthdayChecker.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BirthdayChecker.App;

/// <summary>
/// Tray icon, periodic check, reminder popup and legacy import.
/// Reads entries.json and writes it only on import; owns settings.json and state.json.
/// </summary>
internal sealed class TrayApp : IDisposable
{
    public const string Title = "BirthDay Checker";

    /// <summary>How long "Напомнить позже" hides an event.</summary>
    private static readonly TimeSpan SnoozeTime = TimeSpan.FromHours(1);

    private static readonly string EntriesPath = AppLayout.EntriesPath(AppContext.BaseDirectory);
    private static readonly string SettingsPath = AppLayout.SettingsPath(AppContext.BaseDirectory);
    private static readonly string StatePath = AppLayout.StatePath(AppContext.BaseDirectory);
    private static readonly string BackupFolder = AppLayout.BackupFolder(AppContext.BaseDirectory);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.NotifyIcon _icon;
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _midnight = new();
    private readonly DispatcherTimer _snoozeEnd = new();
    private readonly DispatcherTimer _updateTimer = new();
    private MediaPlayer? _player;   // created on first custom sound: the WPF media stack isn't free
    private AppSettings _settings = AppSettings.Load(SettingsPath);
    private TrayState _state = TrayState.Load(StatePath);
    private ReminderWindow? _window;
    private ReminderWindow? _sample;
    private MainWindow? _list;
    private List<Occurrence> _shown = [];
    private DateOnly _shownDay;      // "not today" applies to the day the popup was built, even if clicked after midnight
    private bool _busy;             // a modal dialog pumps messages: block re-entrant check / import
    private bool _windowInReach;    // the popup shows a manual check's wider list
    private bool _checkPending;     // an automatic check that hit _busy runs right after the dialog closes
    private string? _lastAutoError; // an automatic check reports the same error once, not on every tick
    private string? _lastBackupError; // same for the automatic copy
    private bool _updating;          // an update check or download is under way
    private UpdateRelease? _balloonUpdate; // the release the last balloon offers; null when it said something else
    private (UpdateRelease Release, string Path)? _downloaded; // verified installer waiting for open dialogs to close

    /// <summary>Construction only; nothing here can fail after the icon is visible. Call <see cref="Start"/> next.</summary>
    public TrayApp()
    {
        _menu = BuildMenu();
        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = Title,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => OpenList();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonUpdate is { } release)
                OfferUpdate(release);
        };
        _updateTimer.Tick += (_, _) =>
        {
            ScheduleUpdateCheck();
            CheckForUpdates(manual: false);
        };
        ColorPick.LoadCustom = () => _settings.CustomColors ?? [];
        ColorPick.SaveCustom = colors => ChangeSettings(s => s with { CustomColors = colors });
        _timer.Tick += (_, _) => Check(manual: false);
        _midnight.Tick += (_, _) => OnNewDay();
        _snoozeEnd.Tick += (_, _) =>
        {
            _snoozeEnd.Stop();
            Check(manual: false);
        };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    /// <summary>The tray-sized frame of app.ico (a scaled 32 px one would look blurry).</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    /// <param name="openList">Started by the installer (install or update): show the list; autostart stays in the tray.</param>
    public void Start(bool openList)
    {
        ApplyTheme();
        _icon.Visible = true;
        ApplyInterval();
        ScheduleMidnight();
        Check(manual: false); // check right away on start
        if (_settings.CheckUpdates)
        {
            // A minute later: right after logon the network may not be up yet, and the reminder comes first.
            _updateTimer.Interval = TimeSpan.FromMinutes(1);
            _updateTimer.Start();
        }
        if (openList)
            OpenList();
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged; // static events: would leak the handlers
        SystemEvents.TimeChanged -= OnTimeChanged;
        _timer.Stop();
        _midnight.Stop();
        _snoozeEnd.Stop();
        _updateTimer.Stop();
        _player?.Close();
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
    }

    // ---- menu -----------------------------------------------------------------------------

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var open = menu.Items.Add("Открыть список", null, (_, _) => OpenList());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); // the double-click action
        menu.Items.Add("Проверить сейчас", null, (_, _) => Check(manual: true));
        menu.Items.Add("Проверить обновления", null, (_, _) => CheckForUpdates(manual: true));
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Application.Current.Shutdown());
        return menu;
    }

    internal static string IntervalLabel(int minutes) => minutes < 60
        ? $"{minutes} мин"
        : $"{minutes / 60} {ReminderText.Plural(minutes / 60, "час", "часа", "часов")}";

    /// <summary>Modal; checks wait (Guarded) and run right after it closes.</summary>
    internal void OpenSettings() => Guarded(() =>
    {
        var columns = ColumnCatalog.Merge(_settings.CustomColumns, LoadEntries(manual: false) ?? []);
        var window = new SettingsWindow(_settings, columns, ShowSample) { Owner = _list };
        bool saved = window.ShowDialog() == true;
        _sample?.Close(); // a preview may show values that were just cancelled
        if (saved && window.Result is { } result)
        {
            var old = _settings;
            ChangeSettings(s => result with { Columns = s.Columns, ListWidth = s.ListWidth, ListHeight = s.ListHeight, CalendarWidth = s.CalendarWidth, CustomColors = s.CustomColors }); // a list opened and closed meanwhile saved its layout; the color window saves its own colors
            bool sameColumns = (old.ReminderColumns ?? ColumnCatalog.ReminderDefault).SequenceEqual(_settings.ReminderColumns ?? ColumnCatalog.ReminderDefault);
            if (_window is not null && (old.Notification != _settings.Notification || !sameColumns))
            {
                _window.Close(); // it keeps the settings it was built with
                _checkPending = true; // shown again the new way as soon as this dialog is out of the way
            }
        }
    });

    // ---- settings / state -----------------------------------------------------------------

    private void ChangeSettings(Func<AppSettings, AppSettings> change)
    {
        int oldInterval = _settings.CheckIntervalMinutes;
        bool oldTheme = _settings.DarkTheme;
        var oldUpdates = (_settings.CheckUpdates, _settings.UpdateHour);
        _settings = change(_settings);
        try
        {
            AppSettings.Save(SettingsPath, _settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn($"Не удалось сохранить настройки: {e.Message}");
        }
        if (_settings.CheckIntervalMinutes != oldInterval)
            ApplyInterval();
        if (_settings.DarkTheme != oldTheme)
            ApplyTheme();
        if ((_settings.CheckUpdates, _settings.UpdateHour) != oldUpdates)
            ScheduleUpdateCheck();
    }

    /// <summary>Both are WPF's built-in Fluent theme, so only colors change, never sizes. Open windows follow.</summary>
    private void ApplyTheme()
    {
        bool dark = _settings.DarkTheme;
        var app = Application.Current;
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        Forms.Application.SetColorMode(dark ? Forms.SystemColorMode.Dark : Forms.SystemColorMode.Classic); // the tray menu
        app.Resources["RowBack"] = ColorPick.Brush(dark ? "#2B2B2B" : "#FFFFFF");
        app.Resources["RowAltBack"] = ColorPick.Brush(dark ? "#333333" : "#F2F2F2");
        app.Resources["GridLine"] = ColorPick.Brush(dark ? "#505050" : "#D0D0D0");
        // Opaque, unlike the Fluent drop-down lists: a reminder's picture must not show through a button.
        app.Resources["ButtonBack"] = ColorPick.Brush(dark ? "#383838" : "#E6E6E6");
        app.Resources["ButtonText"] = ColorPick.Brush(dark ? "#FFFFFF" : "#000000");
        app.Resources["ButtonBorder"] = ColorPick.Brush(dark ? "#4A4A4A" : "#D0D0D0");
        // Translucent over the fill, so palette buttons keep their color.
        app.Resources["ButtonHoverShade"] = new SolidColorBrush(dark ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x00, 0x00, 0x00));
        app.Resources["ButtonPressShade"] = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
        app.Resources["ButtonFocus"] = ColorPick.Brush(dark ? "#6CB0FF" : "#0067C0");
        app.Resources["Hint"] = ColorPick.Brush(dark ? "#A8A8A8" : "#5F5F5F");
    }

    private void SaveState(TrayState state)
    {
        if (state == _state)
            return;
        _state = state;
        try
        {
            TrayState.Save(StatePath, _state);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // ponytail: state is a convenience (catch-up, "not today"); losing it must not nag the user
        }
    }

    // ---- scheduling -----------------------------------------------------------------------

    private void ApplyInterval()
    {
        _timer.Stop(); // restart the countdown
        _timer.Interval = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
        _timer.Start();
    }

    // A birthday must show up right after midnight and right after waking from sleep,
    // not only when the (up to 24 h) interval happens to elapse.
    private void ScheduleMidnight()
    {
        _midnight.Stop();
        _midnight.Interval = DateTime.Today.AddDays(1).AddSeconds(5) - DateTime.Now;
        _midnight.Start();
    }

    private void ScheduleSnoozeEnd()
    {
        _snoozeEnd.Stop();
        var now = DateTime.Now;
        if (_state.NextSnoozeEnd(now) is not { } end)
            return;
        // Past the end: the check compares with "now". Capped: a hand-edited far date would overflow the timer.
        var wait = end - now + TimeSpan.FromSeconds(1);
        _snoozeEnd.Interval = wait < TimeSpan.FromDays(1) ? wait : TimeSpan.FromDays(1);
        _snoozeEnd.Start();
    }

    private void OnNewDay()
    {
        ScheduleMidnight();
        _list?.Refresh(); // "today" highlights and days-left of an open list
        Check(manual: false);
    }

    // Both raised on a system-events thread.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _dispatcher.BeginInvoke(OnNewDay);
    }

    private void OnTimeChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(OnNewDay);

    // ---- main window ----------------------------------------------------------------------

    private void OpenList()
    {
        if (_list is not null)
        {
            if (_list.WindowState == WindowState.Minimized)
                _list.WindowState = WindowState.Maximized;
            _list.Activate();
            return;
        }
        var list = new MainWindow(EntriesPath, () => _settings, ChangeSettings, () => Check(manual: true), OpenSettings);
        list.Closed += (_, _) =>
        {
            if (ReferenceEquals(_list, list))
                _list = null;
        };
        _list = list;
        list.Show();
        list.Activate();
    }

    // ---- check and popup ------------------------------------------------------------------

    /// <summary>Re-reads the file every time, so hand edits and imports are picked up without restart.</summary>
    private IReadOnlyList<Entry>? LoadEntries(bool manual)
    {
        try
        {
            var entries = EntryStorage.Load(EntriesPath);
            _lastAutoError = null;
            return entries;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (manual || e.Message != _lastAutoError)
                Warn($"Файл записей не прочитан, программа его не изменит:\n{e.Message}");
            if (!manual)
                _lastAutoError = e.Message;
            return null;
        }
    }

    private void Check(bool manual)
    {
        if (_busy)
        {
            _checkPending |= !manual; // e.g. the midnight check must not wait a whole interval
            return;
        }
        _checkPending = false;
        _busy = true;
        try
        {
            var entries = LoadEntries(manual);
            if (entries is null)
                return;

            var today = DateOnly.FromDateTime(DateTime.Today);
            var now = DateTime.Now;
            var state = _state.StartDay(today);
            // A timer tick must not shrink or close the list a manual check has just opened; midnight may.
            bool inReach = manual || (_windowInReach && _window is not null && _shownDay == today);
            var due = (inReach ? Upcoming.InReach(entries, today) : Upcoming.Due(entries, today, state.CatchUpSince))
                .Where(o => !state.IsDismissed(o.Entry.Id, today) && (manual || !state.IsSnoozed(o.Entry.Id, now)))
                .ToList();

            if (due.Count == 0)
            {
                if (manual)
                    Balloon(3000, entries.Count == 0
                        ? "Список пуст — добавьте записи в главном окне."
                        : "Грядущих событий не обнаружено.", Forms.ToolTipIcon.Info);
                _window?.Close(); // e.g. after midnight yesterday's popup is stale
            }
            else
            {
                ShowReminder(due, today);
                _windowInReach = inReach;
            }

            // Only after the popup is up: if showing failed, the next check still has the catch-up window.
            SaveState(state);
            ScheduleSnoozeEnd();
            BackupIfDue(today); // last: its error dialog must not hold back the reminder
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The same error is reported once.</summary>
    private void BackupIfDue(DateOnly today)
    {
        try
        {
            AutoBackup.RunIfDue(EntriesPath, BackupFolder, _settings.BackupPeriodDays, today);
            _lastBackupError = null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (e.Message != _lastBackupError)
                Warn($"Не удалось обновить резервную копию:\n{e.Message}");
            _lastBackupError = e.Message;
        }
    }

    private void ShowReminder(List<Occurrence> due, DateOnly today)
    {
        // Beep for a new popup or a newly due event, not on every periodic re-check of the same list.
        bool news = _window is null || due.Any(o => _shown.All(s => s.Entry.Id != o.Entry.Id));
        _shown = due;
        _shownDay = today;
        if (_settings.Notification == NotificationMode.Windows)
        {
            Notify(due, _settings);
            return;
        }
        if (_window is not null)
            _window.Update(due);
        else
        {
            var window = new ReminderWindow(_settings, due, DismissToday, SnoozeLater);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            };
            _window = window;
            window.Show();
        }
        if (news)
            PlaySound();
    }

    /// <summary>
    /// ponytail: the tray icon's own notification, one at a time and without buttons. Real toasts (one per event, buttons)
    /// need another target framework and an app id in the installer's shortcut.
    /// </summary>
    private void Notify(IReadOnlyList<Occurrence> items, AppSettings settings) =>
        Balloon(10000, ReminderText.Notification(items, settings.ReminderColumns), Forms.ToolTipIcon.None);

    /// <summary>The tray's notification; a click on it offers <paramref name="update"/> when given.</summary>
    private void Balloon(int milliseconds, string text, Forms.ToolTipIcon icon, UpdateRelease? update = null)
    {
        _balloonUpdate = update;
        _icon.ShowBalloonTip(milliseconds, Title, text, icon);
    }

    private void DismissToday(Guid id)
    {
        SaveState(_state.Dismiss(id, _shownDay));
        RemoveFromPopup(id);
    }

    private void SnoozeLater(Guid id)
    {
        var now = DateTime.Now;
        SaveState(_state.Snooze(id, now + SnoozeTime, now));
        ScheduleSnoozeEnd();
        RemoveFromPopup(id);
    }

    private void RemoveFromPopup(Guid id)
    {
        _shown = _shown.Where(o => o.Entry.Id != id).ToList();
        if (_shown.Count == 0)
            _window?.Close();
        else
            _window?.Update(_shown);
    }

    /// <summary>Preview of a look and sound (possibly not saved yet); uses made-up entries and touches no files.</summary>
    private void ShowSample(AppSettings settings)
    {
        // Built directly (not via Upcoming): any "today", including Feb 29, must work.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var someDate = new FixedDate(1, 1);
        var sample = new List<Occurrence>
        {
            new(new Entry("Пример: Иван Петров", "День Рождения", someDate)
            {
                Phone = "+7 000 000-00-00", Comment = "Позвонить вечером", Tags = ["семья"],
                CustomFields = settings.ReminderColumns?.Except(ColumnCatalog.BuiltIn, StringComparer.CurrentCultureIgnoreCase).ToDictionary(c => c, _ => "значение"),
            }, today, 0, 30),
            new(new Entry("Пример: праздник", "Праздник", someDate), today.AddDays(3), 3, null),
        };
        _sample?.Close(); // one preview at a time, picking up the latest settings
        if (settings.Notification == NotificationMode.Windows)
        {
            Notify(sample, settings);
            return;
        }
        void Remove(Guid id)
        {
            sample.RemoveAll(o => o.Entry.Id == id);
            if (sample.Count == 0) _sample?.Close(); else _sample?.Update(sample);
        }
        var window = new ReminderWindow(settings, sample, Remove, Remove);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_sample, window))
                _sample = null;
        };
        _sample = window;
        window.Show();
        PlaySound(settings);
    }

    private void PlaySound() => PlaySound(_settings);

    private void PlaySound(AppSettings settings)
    {
        if (settings.Sound == SoundMode.Off)
            return;
        if (settings is { Sound: SoundMode.File, SoundFile: { } file } && File.Exists(file))
        {
            try
            {
                _player ??= CreatePlayer();
                _player.Open(new Uri(file));
                _player.Play();
                return;
            }
            catch (Exception e) when (e is UriFormatException or ArgumentException or InvalidOperationException)
            {
                // fall through to the system sound
            }
        }
        SystemSounds.Asterisk.Play(); // System, or a custom file that is missing or unplayable
    }

    private static MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer();
        player.MediaEnded += (_, _) => player.Close(); // release the file so the user can replace it
        player.MediaFailed += (_, _) =>
        {
            player.Close();
            SystemSounds.Asterisk.Play();
        };
        return player;
    }

    /// <summary>Modal dialogs pump messages; keep timer checks and other dialogs out meanwhile.</summary>
    private void Guarded(Action action)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            action();
        }
        finally
        {
            _busy = false;
        }
        if (_checkPending)
            Check(manual: false);
    }

    // ---- updates --------------------------------------------------------------------------

    private void ScheduleUpdateCheck()
    {
        _updateTimer.Stop();
        if (!_settings.CheckUpdates)
            return;
        var now = DateTime.Now;
        // A minute ahead: a tick that comes a hair early must not find "today at the hour" still ahead and fire twice.
        _updateTimer.Interval = UpdateCheck.NextDaily(now.AddMinutes(1), _settings.UpdateHour) - now;
        _updateTimer.Start();
    }

    /// <summary>
    /// Automatic: only a balloon, and only when there is a new version; a click on it asks.
    /// Manual: asks right away, otherwise says why there is nothing.
    /// </summary>
    private async void CheckForUpdates(bool manual)
    {
        if (_updating)
        {
            if (manual)
                Balloon(3000, "Обновление уже проверяется или скачивается.", Forms.ToolTipIcon.Info);
            return;
        }
        _updating = true;
        UpdateRelease? release;
        try
        {
            release = await Updater.FindNewer();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            if (manual) // automatic: no network is normal, try again tomorrow
                Balloon(5000, $"Не удалось проверить обновления: {Reason(e)}.", Forms.ToolTipIcon.Warning);
            return;
        }
        finally
        {
            _updating = false;
        }
        if (release is null)
        {
            if (manual)
                Balloon(3000, $"Установлена последняя версия ({Updater.Current.ToString(3)}).", Forms.ToolTipIcon.Info);
        }
        else if (manual)
            OfferUpdate(release);
        else
            OfferLater(release);
    }

    private void OfferLater(UpdateRelease release) =>
        Balloon(10000, $"Доступна версия {release.Version.ToString(3)} — нажмите, чтобы обновить.", Forms.ToolTipIcon.Info, release);

    private void OfferUpdate(UpdateRelease release)
    {
        if (_updating)
            return;
        if (_busy)
        {
            OfferLater(release); // another dialog is open: ask once it is out of the way
            return;
        }
        bool yes = false;
        Guarded(() => yes = AskUpdate(release));
        if (yes)
            InstallUpdate(release);
    }

    private bool AskUpdate(UpdateRelease release)
    {
        var window = new Window
        {
            Title = Title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, // a tray app's dialog must not open behind others
        };
        var update = new Button { Content = "Обновить", IsDefault = true, Margin = new Thickness(0, 0, 8, 0) }.Styled("MainButton");
        update.Click += (_, _) => window.DialogResult = true;
        var later = new Button { Content = "Позже", IsCancel = true }.Styled("MainButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(update);
        buttons.Children.Add(later);
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Доступна новая версия {release.Version.ToString(3)} (установлена {Updater.Current.ToString(3)}).\n\n" +
                   "«Обновить» скачает установщик и запустит его, программа закроется. Записи и настройки сохранятся.",
        };
        var root = new StackPanel { Margin = new Thickness(16), MaxWidth = 420 };
        root.Children.Add(text);
        root.Children.Add(buttons);
        window.Content = root;
        return window.ShowDialog() == true;
    }

    /// <summary>
    /// Downloads in the background; exits right after the installer starts, so its "close applications" page has nothing to close.
    /// Not while a dialog is open (a download takes minutes, an editor may be opened meanwhile): exiting would drop its input.
    /// </summary>
    private async void InstallUpdate(UpdateRelease release)
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            if (_downloaded is not { } d || d.Release != release)
            {
                Balloon(5000, $"Скачивается версия {release.Version.ToString(3)}…", Forms.ToolTipIcon.Info);
                _downloaded = (release, await Updater.Download(release));
            }
            if (_busy || DialogOpen())
            {
                Balloon(10000, "Установщик скачан. Закройте окна программы и нажмите здесь, чтобы обновить.", Forms.ToolTipIcon.Info, release);
                return;
            }
            Updater.Run(_downloaded.Value.Path);
        }
        catch (Exception e) // async void: anything left would crash the app; nothing here is worse than "not installed"
        {
            _downloaded = null;
            Balloon(5000, $"Обновление не установлено: {Reason(e)}.", Forms.ToolTipIcon.Warning);
            return;
        }
        finally
        {
            _updating = false;
        }
        Application.Current.Shutdown();
    }

    /// <summary>A window other than the list and the reminders: an editor, settings, a picker — each may hold unsaved input.</summary>
    private bool DialogOpen() =>
        Application.Current.Windows.OfType<Window>().Any(w => w != _list && w is not ReminderWindow);

    /// <summary>In Russian: the network's own messages come in English.</summary>
    private static string Reason(Exception e) => e switch
    {
        TaskCanceledException => "GitHub не ответил вовремя",
        HttpRequestException { StatusCode: { } code } => $"GitHub ответил {(int)code}",
        HttpRequestException => "нет связи с GitHub",
        _ => e.Message,
    };

    private static void Warn(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>A tray app has no window to own dialogs; DefaultDesktopOnly keeps them from opening behind others.</summary>
    internal static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image) =>
        MessageBox.Show(message, Title, buttons, image, MessageBoxResult.None, MessageBoxOptions.DefaultDesktopOnly);
}
