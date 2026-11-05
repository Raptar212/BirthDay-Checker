using System.IO;
using System.Windows;
using BirthdayChecker.Core;
using Forms = System.Windows.Forms;

namespace BirthdayChecker.App;

/// <summary>Tray-only app: no main window, lives until "Выход" in the tray menu.</summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayApp? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\BirthdayChecker", out bool first);
        if (!first)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        // Tray menu handlers run through WinForms, not the WPF dispatcher: catch both.
        Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException);
        Forms.Application.ThreadException += (_, args) => ReportCrash(args.Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception);
            args.Handled = true;
        };

        // Every window (list, editor, settings, popups) gets the app icon without setting it one by one.
        var icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        icon.Freeze();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is Window { Icon: null } w)
                w.Icon = icon;
        }));

        // Installer updates and Windows logoff ask running apps to close: exit cleanly instead of being killed.
        SessionEnding += (_, _) => Shutdown();

        // Before TrayApp reads state.json from its new place.
        try
        {
            AppLayout.MoveOldFiles(AppContext.BaseDirectory);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            // ponytail: the worst case is a repeated catch-up and an old .bak left in the root; retried on the next start
        }

        _tray = new TrayApp();
        _tray.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static bool _reporting;

    /// <summary>A tray app must not vanish silently on a bug: log details, show a short message, keep running.</summary>
    private static void ReportCrash(Exception ex)
    {
        var log = AppLayout.ErrorLogPath(AppContext.BaseDirectory);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, $"{DateTime.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // nothing else to do; the message box below still informs the user
        }

        // The dialog pumps messages: a bug that repeats on every timer tick must not stack dialogs.
        if (_reporting)
            return;
        _reporting = true;
        try
        {
            TrayApp.Show($"Непредвиденная ошибка: {ex.Message}\nПодробности — в data\\errors.log в папке программы.",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _reporting = false;
        }
    }
}
