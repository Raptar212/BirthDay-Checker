using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Reminder popup: all due events separated by a line, with shared "later" and "not today" buttons, styled per settings.</summary>
public partial class ReminderWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action<Guid> _dismiss;
    private readonly Action<Guid> _snooze;
    private readonly DispatcherTimer _autoClose = new();
    private List<Guid> _shownIds = [];

    public ReminderWindow(AppSettings settings, IReadOnlyList<Occurrence> items, Action<Guid> dismiss, Action<Guid> snooze)
    {
        InitializeComponent();
        _settings = settings;
        _dismiss = dismiss;
        _snooze = snooze;
        ApplyLook();

        // SizeChanged also fires for the first layout, so the window never shows at a default position.
        SizeChanged += (_, _) => Place();
        _autoClose.Tick += (_, _) => Close();
        // Don't close while the user is reading.
        MouseEnter += (_, _) => _autoClose.Stop();
        MouseLeave += (_, _) => RestartAutoClose();
        Closed += (_, _) => _autoClose.Stop();

        Update(items);
    }

    public void Update(IReadOnlyList<Occurrence> items)
    {
        _shownIds = items.Select(o => o.Entry.Id).ToList();
        Items.Children.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                var line = new Border { Height = 1, Opacity = 0.5, Margin = new Thickness(0, 8, 0, 8) };
                line.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = this }); // the text color: readable on any background, follows the theme
                Items.Children.Add(line);
            }

            var lines = ReminderText.Lines(items[i], _settings.ReminderColumns);
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            text.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(lines[0])));
            text.Inlines.Add(new System.Windows.Documents.LineBreak());
            text.Inlines.Add(new System.Windows.Documents.Run(string.Join(Environment.NewLine, lines.Skip(1))));
            Items.Children.Add(text);
        }
        RestartAutoClose();
    }

    private void RestartAutoClose()
    {
        _autoClose.Stop();
        if (_settings.AutoCloseSeconds <= 0 || IsMouseOver)
            return;
        _autoClose.Interval = TimeSpan.FromSeconds(_settings.AutoCloseSeconds);
        _autoClose.Start();
    }

    private void ApplyLook()
    {
        // Next to a custom color the unset one is the system's, not the theme's: black text must not land on a dark window.
        var image = LoadImage(_settings.BackgroundImage);
        if (_settings.TextColor is { } text)
            Foreground = Brush(text);
        else if (_settings.BackgroundColor is not null || image is not null)
            Foreground = SystemColors.WindowTextBrush;
        if (_settings.BackgroundColor is { } back)
            Background = Brush(back);
        else if (_settings.TextColor is not null)
            Background = SystemColors.WindowBrush;
        if (image is not null)
            Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>A missing or unreadable picture just falls back to the color — never an error popup.</summary>
    private static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    private void Place()
    {
        const double gap = 8;
        var area = SystemParameters.WorkArea;
        double right = area.Right - ActualWidth - gap, bottom = area.Bottom - ActualHeight - gap;
        (Left, Top) = _settings.Position switch
        {
            PopupPosition.BottomLeft => (area.Left + gap, bottom),
            PopupPosition.TopRight => (right, area.Top + gap),
            PopupPosition.TopLeft => (area.Left + gap, area.Top + gap),
            PopupPosition.Center => (area.Left + (area.Width - ActualWidth) / 2, area.Top + (area.Height - ActualHeight) / 2),
            _ => (right, bottom),
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnSnoozeAll(object sender, RoutedEventArgs e)
    {
        foreach (var id in _shownIds.ToList()) // each snooze calls Update, which replaces the list
            _snooze(id);
    }

    private void OnDismissAll(object sender, RoutedEventArgs e)
    {
        foreach (var id in _shownIds.ToList()) // each dismiss calls Update, which replaces the list
            _dismiss(id);
    }
}
