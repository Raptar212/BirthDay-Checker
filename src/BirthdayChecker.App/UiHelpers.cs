using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Shared looks for elements created in code; the styles and brushes live in App.xaml and TrayApp.ApplyTheme.</summary>
internal static class Ui
{
    /// <summary>Table cell text in the middle of the row: the dark theme's rows are taller than a line.</summary>
    public static readonly Style CenteredCell = new(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle)
    {
        Setters = { new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center) },
    };

    public static T Styled<T>(this T element, string key) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, key);
        return element;
    }

    public static TextBlock Hinted(this TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "Hint");
        return text;
    }
}

/// <summary>"#RRGGBB" ⇄ the color window and WPF brushes.</summary>
internal static class ColorPick
{
    /// <summary>The user's own colors of the color window; the tray connects them to the settings.</summary>
    public static Func<IReadOnlyList<string>> LoadCustom { get; set; } = () => [];
    public static Action<IReadOnlyList<string>> SaveCustom { get; set; } = _ => { };

    /// <summary>Null when the user cancels.</summary>
    public static string? Pick(string? current)
    {
        var dialog = new ColorWindow(current)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    /// <summary>A color square; a click offers ready colors, any other color, and <paramref name="noneText"/> if given.</summary>
    public static Button MenuButton(string? color, Action<string?> set, string? noneText)
    {
        var button = new Button
        {
            Width = 22, Height = 18, MinHeight = 0, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush(color) ?? Brushes.Transparent, // null would let clicks through
            BorderThickness = new Thickness(1), // an empty square must still show
            ToolTip = "Изменить цвет",
        };
        button.SetResourceReference(Control.BorderBrushProperty, "GridLine");
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(new MenuItem
            {
                StaysOpenOnClick = true, // only the colors themselves choose
                Header = Palette(hex =>
                {
                    menu.IsOpen = false;
                    set(hex);
                }),
            });
            var other = new MenuItem { Header = "Другой цвет…" };
            other.Click += (_, _) =>
            {
                if (Pick(color) is { } picked)
                    set(picked);
            };
            menu.Items.Add(other);
            if (noneText is not null)
            {
                var none = new MenuItem { Header = noneText };
                none.Click += (_, _) => set(null);
                menu.Items.Add(none);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    public static SolidColorBrush? Brush(string? hex) =>
        Entry.IsColor(hex) ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex!)) : null;

    /// <summary>Small square of a tag's color, empty when it has none.</summary>
    public static Border Swatch(string? hex) => new()
    {
        Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0),
        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brush(hex),
    };

    public static readonly string[] PaletteColors =
        ["#FF8080", "#FFB366", "#FFE680", "#A8E6A1", "#80D4FF", "#A6B8FF", "#D9A6FF", "#C8C8C8"];

    /// <summary>A row of ready colors, light enough for the black text of the list.</summary>
    public static WrapPanel Palette(Action<string> pick)
    {
        var palette = new WrapPanel();
        foreach (var hex in PaletteColors)
        {
            var button = new Button { Width = 24, Height = 20, MinHeight = 0, Margin = new Thickness(0, 0, 4, 0), Background = Brush(hex), ToolTip = hex };
            button.Click += (_, _) => pick(hex);
            palette.Children.Add(button);
        }
        return palette;
    }
}

/// <summary>Turns a button into a drop-down of tags with checkmarks; several can be ticked.</summary>
internal sealed class TagPicker
{
    private readonly Button _button;
    private readonly List<(string Name, MenuItem Item)> _items = [];

    public event Action? Changed;

    public TagPicker(Button button, IReadOnlyList<TagDefinition> tags, IEnumerable<string> selected)
    {
        _button = button;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var tag in tags)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(ColorPick.Swatch(tag.Color));
            header.Children.Add(new TextBlock { Text = tag.Name });
            var item = new MenuItem
            {
                Header = header, IsCheckable = true, StaysOpenOnClick = true,
                IsChecked = selected.Contains(tag.Name, StringComparer.CurrentCultureIgnoreCase),
            };
            item.Click += (_, _) =>
            {
                ShowSelected();
                Changed?.Invoke();
            };
            menu.Items.Add(item);
            _items.Add((tag.Name, item));
        }
        if (tags.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Тегов нет — добавьте их кнопкой «Теги» в списке", IsEnabled = false });
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            menu.MinWidth = button.ActualWidth;
            menu.IsOpen = true;
        };
        ShowSelected();
    }

    public IReadOnlyList<string> Selected => _items.Where(i => i.Item.IsChecked).Select(i => i.Name).ToList();

    private void ShowSelected() =>
        _button.Content = (Selected.Count == 0 ? "Не выбраны" : string.Join(", ", Selected)) + " ▾";
}

/// <summary>The 13 "remind before" checkboxes laid out like the legacy editor: days, weeks, months.</summary>
internal sealed class ReminderChecks
{
    private static readonly string[] Labels =
    [
        "за 1 день", "за 2 дня", "за 3 дня", "за 4 дня", "за 5 дней",
        "за 1 неделю", "за 2 недели", "за 3 недели", "за 4 недели", "за 5 недель",
        "за 1 месяц", "за 2 месяца", "за 3 месяца",
    ];

    private readonly CheckBox[] _checks = new CheckBox[Labels.Length];

    public event Action? Changed;

    public ReminderChecks(Grid grid)
    {
        for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 5; r++) grid.RowDefinitions.Add(new RowDefinition());

        for (int i = 0; i < _checks.Length; i++)
        {
            _checks[i] = new CheckBox { Content = Labels[i], Margin = new Thickness(0, 2, 12, 2) };
            _checks[i].Checked += (_, _) => Changed?.Invoke();
            _checks[i].Unchecked += (_, _) => Changed?.Invoke();
            Place(_checks[i], row: i < 10 ? i % 5 : i - 10, column: i / 5);
        }

        var all = new Button { Content = "Вкл/Откл все", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }.Styled("SmallButton");
        all.Click += (_, _) =>
        {
            bool on = !_checks.All(c => c.IsChecked == true);
            foreach (var c in _checks) c.IsChecked = on;
        };
        Place(all, row: 4, column: 2);

        void Place(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }

    public Reminder Value
    {
        get => _checks.Select((c, i) => c.IsChecked == true ? (Reminder)(1 << i) : Reminder.None)
            .Aggregate(Reminder.None, (a, b) => a | b);
        set
        {
            for (int i = 0; i < _checks.Length; i++)
                _checks[i].IsChecked = value.HasFlag((Reminder)(1 << i));
        }
    }
}
