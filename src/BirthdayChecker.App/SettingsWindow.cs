using System.IO;
using System.Windows;
using System.Windows.Controls;
using BirthdayChecker.Core;
using Microsoft.Win32;

namespace BirthdayChecker.App;

/// <summary>All settings in one place (were tray submenus until BDC-9). Applies nothing until "Сохранить".</summary>
internal sealed class SettingsWindow : Window
{
    private static readonly (string Label, PopupPosition Value)[] Positions =
    [
        ("Справа снизу", PopupPosition.BottomRight), ("Слева снизу", PopupPosition.BottomLeft),
        ("Справа сверху", PopupPosition.TopRight), ("Слева сверху", PopupPosition.TopLeft), ("По центру", PopupPosition.Center),
    ];

    private readonly Action<AppSettings> _preview;
    private AppSettings _s;

    private readonly ComboBox _interval = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _autostart = new() { Content = "Запускать вместе с Windows" };
    private readonly CheckBox _darkTheme = new() { Content = "Тёмная тема" };
    private readonly TextBlock _autostartHint = new TextBlock { TextWrapping = TextWrapping.Wrap }.Hinted();
    private readonly ComboBox _position = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    // 90: with focus the dark theme's field keeps 56 for its padding and clear button.
    private readonly TextBox _autoClose = new() { Width = 90, MaxLength = 4, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _backupPeriod = new() { Width = 90, MaxLength = 3, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly RadioButton _soundOff = new() { Content = "Выключен", GroupName = "Sound", MinWidth = 0, Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton _soundSystem = new() { Content = "Системный", GroupName = "Sound", MinWidth = 0, Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton _soundFile = new() { Content = "Свой файл", GroupName = "Sound", MinWidth = 0 };
    private readonly TextBlock _soundPath = PathLabel();
    private readonly Border _textSwatch = Swatch();
    private readonly Border _backSwatch = Swatch();
    private readonly TextBlock _imagePath = PathLabel();
    private readonly RadioButton _viewWindow = new() { Content = "Окно программы", GroupName = "View", MinWidth = 0, Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton _viewWindows = new() { Content = "Уведомление Windows", GroupName = "View", MinWidth = 0 };
    private readonly List<(string Name, CheckBox Box)> _reminderColumns = [];

    public AppSettings? Result { get; private set; }

    /// <param name="settings">Current values.</param>
    /// <param name="customColumns">The user's own columns, offered for the reminder.</param>
    /// <param name="preview">Shows a sample reminder.</param>
    public SettingsWindow(AppSettings settings, IReadOnlyList<string> customColumns, Action<AppSettings> preview)
    {
        _s = settings;
        _preview = preview;
        Title = "Настройки — BirthDay Checker";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _interval.ItemsSource = AppSettings.IntervalChoices.Select(TrayApp.IntervalLabel).ToList();
        _interval.SelectedIndex = Array.IndexOf(AppSettings.IntervalChoices, _s.CheckIntervalMinutes);
        _autostart.IsChecked = Autostart.IsEnabled;
        _darkTheme.IsChecked = _s.DarkTheme;
        if (!Autostart.CanEnable)
        {
            _autostart.IsEnabled = false;
            _autostartHint.Text = "Программа запущена через dotnet — автозапуск можно включить только из BirthdayChecker.exe.";
        }
        else if (Autostart.DisabledInTaskManager)
            _autostartHint.Text = "Автозапуск выключен в Диспетчере задач — отметьте галочку и «Сохранить», чтобы включить снова.";
        else if (Autostart.PointsElsewhere)
            _autostartHint.Text = "Автозапуск сейчас указывает на другую копию программы — «Сохранить» направит его на эту.";
        _position.ItemsSource = Positions.Select(p => p.Label).ToList();
        _position.SelectedIndex = Math.Max(0, Array.FindIndex(Positions, p => p.Value == _s.Position));
        _autoClose.Text = _s.AutoCloseSeconds.ToString();
        _backupPeriod.Text = _s.BackupPeriodDays.ToString();
        (_s.Sound switch { SoundMode.Off => _soundOff, SoundMode.File => _soundFile, _ => _soundSystem }).IsChecked = true;
        ShowLook();

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(Section("Настройки программы",
            Row("Интервал проверки:", _interval),
            Row("Бэкап раз в:", Line(_backupPeriod, new TextBlock
            {
                Text = "дн. (0 — не копировать), в папку Backup", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            })),
            Row("", _autostart),
            Row("", _autostartHint, visible: _autostartHint.Text.Length > 0),
            Row("", _darkTheme)));
        // Only the program's own window has these; a Windows notification looks and sounds as Windows sets it.
        var windowRows = new StackPanel();
        foreach (var row in new[]
        {
            Row("Позиция:", _position),
            Row("Автозакрытие:", Line(_autoClose, new TextBlock
            {
                Text = "с (0 — не закрывать)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            })),
            Row("Звук:", Line(_soundOff, _soundSystem, _soundFile)),
            Row("", Line(Btn("Выбрать файл…", PickSound), _soundPath)),
            Row("Цвет текста:", Line(_textSwatch, Btn("Выбрать…", () => PickColor(text: true)), Btn("Как в системе", () => SetLook(_s with { TextColor = null })))),
            Row("Цвет фона:", Line(_backSwatch, Btn("Выбрать…", () => PickColor(text: false)), Btn("Как в системе", () => SetLook(_s with { BackgroundColor = null })))),
            Row("Картинка фона:", Line(Btn("Выбрать…", PickImage), Btn("Убрать", () => SetLook(_s with { BackgroundImage = null })), _imagePath)),
        })
            windowRows.Children.Add(row);
        void ShowView(object? sender, RoutedEventArgs e) =>
            windowRows.Visibility = _viewWindows.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        _viewWindow.Checked += ShowView;
        _viewWindows.Checked += ShowView;
        (_s.Notification == NotificationMode.Windows ? _viewWindows : _viewWindow).IsChecked = true;

        root.Children.Add(Section("Напоминание",
            Row("Вид:", Line(_viewWindow, _viewWindows)),
            Row("Показывать:", ReminderColumnChecks(customColumns)),
            windowRows,
            Row("", Line(Btn("Показать пример", () =>
            {
                if (Collect() is { } current)
                    _preview(current);
            })))));

        var save = new Button { Content = "Сохранить", IsDefault = true, Margin = new Thickness(0, 0, 8, 0) }.Styled("MainButton");
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", IsCancel = true }.Styled("MainButton");
        var buttons = Line(save, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>Name and date are always in a reminder, so their boxes are ticked for good.</summary>
    private WrapPanel ReminderColumnChecks(IReadOnlyList<string> customColumns)
    {
        var panel = new WrapPanel();
        var ticked = _s.ReminderColumns ?? ColumnCatalog.ReminderDefault;
        foreach (var name in ColumnCatalog.BuiltIn.Concat(customColumns))
        {
            bool locked = ColumnCatalog.IsLocked(name);
            var box = new CheckBox
            {
                Content = name, MinWidth = 0, Margin = new Thickness(0, 2, 12, 2), IsEnabled = !locked,
                IsChecked = locked || ticked.Contains(name, StringComparer.CurrentCultureIgnoreCase),
            };
            panel.Children.Add(box);
            if (!locked)
                _reminderColumns.Add((name, box));
        }
        return panel;
    }

    // ---- layout helpers ---------------------------------------------------------------------

    private static TextBlock PathLabel() => new TextBlock
    {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220,
    }.Hinted();

    /// <summary>Says "система" while no color is chosen: an empty box says nothing.</summary>
    private static Border Swatch() => new()
    {
        Width = 70, Height = 20, Margin = new Thickness(0, 0, 8, 0),
        BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = "система", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static void ShowColor(Border swatch, string? color)
    {
        swatch.Background = ColorPick.Brush(color);
        swatch.Child.Visibility = color is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Border Section(string title, params UIElement[] rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title }.Styled("CardTitle"));
        foreach (var r in rows) panel.Children.Add(r);
        return new Border { Child = panel }.Styled("Card");
    }

    /// <summary>All rows alike, whatever they hold: the dark theme's fields are taller than its small buttons.</summary>
    private static Grid Row(string label, FrameworkElement content, bool visible = true)
    {
        var grid = new Grid { MinHeight = 34, Margin = new Thickness(0, 1, 0, 1), Visibility = visible ? Visibility.Visible : Visibility.Collapsed };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        content.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(content, 1);
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(content);
        return grid;
    }

    private static StackPanel Line(params UIElement[] items)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) line.Children.Add(i);
        return line;
    }

    private static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) }.Styled("SmallButton");
        b.Click += (_, _) => click();
        return b;
    }

    // ---- values -----------------------------------------------------------------------------

    private void SetLook(AppSettings s)
    {
        _s = s;
        ShowLook();
    }

    private void ShowLook()
    {
        ShowColor(_textSwatch, _s.TextColor);
        ShowColor(_backSwatch, _s.BackgroundColor);
        _soundPath.Text = _s.SoundFile is { } f ? Path.GetFileName(f) : "файл не выбран";
        _soundPath.ToolTip = _s.SoundFile;
        _imagePath.Text = _s.BackgroundImage is { } i ? Path.GetFileName(i) : "нет";
        _imagePath.ToolTip = _s.BackgroundImage;
    }

    private void PickColor(bool text)
    {
        if (ColorPick.Pick(text ? _s.TextColor : _s.BackgroundColor) is { } c)
            SetLook(text ? _s with { TextColor = c } : _s with { BackgroundColor = c });
    }

    private void PickSound()
    {
        var dialog = new OpenFileDialog { Filter = "Звук (*.mp3;*.wav;*.wma)|*.mp3;*.wav;*.wma" };
        if (dialog.ShowDialog(this) != true)
            return;
        SetLook(_s with { SoundFile = dialog.FileName });
        _soundFile.IsChecked = true;
    }

    private void PickImage()
    {
        var dialog = new OpenFileDialog { Filter = "Картинка (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif" };
        if (dialog.ShowDialog(this) == true)
            SetLook(_s with { BackgroundImage = dialog.FileName });
    }

    /// <summary>Null (after a message) when the auto-close or backup period field is invalid.</summary>
    private AppSettings? Collect()
    {
        bool ownWindow = _viewWindow.IsChecked == true;
        int autoClose = _s.AutoCloseSeconds; // a hidden field can't be corrected, so it isn't checked
        if (ownWindow && !AppSettings.TryParseAutoClose(_autoClose.Text, out autoClose))
        {
            MessageBox.Show(this, $"Автозакрытие: введите целое число секунд от 0 до {AppSettings.MaxAutoCloseSeconds}.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _autoClose.Focus();
            _autoClose.SelectAll();
            return null;
        }
        if (!AppSettings.TryParseBackupPeriod(_backupPeriod.Text, out int backupPeriod))
        {
            MessageBox.Show(this, $"Резервная копия: введите целое число дней от 0 до {AppSettings.MaxBackupPeriodDays}.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _backupPeriod.Focus();
            _backupPeriod.SelectAll();
            return null;
        }
        return _s with
        {
            BackupPeriodDays = backupPeriod,
            Notification = ownWindow ? NotificationMode.Window : NotificationMode.Windows,
            ReminderColumns = _reminderColumns.Where(c => c.Box.IsChecked == true).Select(c => c.Name).ToList(),
            DarkTheme = _darkTheme.IsChecked == true,
            CheckIntervalMinutes = AppSettings.IntervalChoices[Math.Max(0, _interval.SelectedIndex)],
            Position = Positions[Math.Max(0, _position.SelectedIndex)].Value,
            AutoCloseSeconds = autoClose,
            Sound = _soundOff.IsChecked == true ? SoundMode.Off : _soundFile.IsChecked == true ? SoundMode.File : SoundMode.System,
        };
    }

    private void Save()
    {
        if (Collect() is not { } result)
            return;
        if (result is { Notification: NotificationMode.Window, Sound: SoundMode.File, SoundFile: null })
        {
            MessageBox.Show(this, "Выберите звуковой файл или другой вариант звука.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            bool wanted = _autostart.IsChecked == true;
            if (Autostart.CanEnable && (wanted != Autostart.IsEnabled || (wanted && Autostart.PointsElsewhere)))
                Autostart.Set(wanted);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageBox.Show(this, $"Автозапуск не изменён: {e.Message}\nОстальные настройки сохранены.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Result = result;
        DialogResult = true;
    }
}
