using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Add, rename and delete event types, and choose their markers. Every action applies at once; entries change before the list does.</summary>
internal sealed class EventsWindow : Window
{
    private readonly Func<IReadOnlyList<EventDefinition>> _types;
    private readonly Func<AppSettings> _settings;
    private readonly Func<string, int> _usage;
    private readonly Action<Func<AppSettings, AppSettings>> _change;
    private readonly Func<string, string, bool> _renameEntries;
    private readonly ListBox _list = new() { MaxHeight = (SystemParameters.WorkArea.Height - 340) / 2 };
    private readonly TextBox _name = new() { Margin = new Thickness(0, 2, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel _markers = new();
    private bool _filling;

    /// <param name="types">Current list.</param>
    /// <param name="settings">Current settings, for the two common colors.</param>
    /// <param name="usage">How many entries have a type.</param>
    /// <param name="change">Stores changed settings.</param>
    /// <param name="renameEntries">Moves all entries of a type to another name; false if not saved.</param>
    public EventsWindow(Func<IReadOnlyList<EventDefinition>> types, Func<AppSettings> settings, Func<string, int> usage,
        Action<Func<AppSettings, AppSettings>> change, Func<string, string, bool> renameEntries)
    {
        _types = types;
        _settings = settings;
        _usage = usage;
        _change = change;
        _renameEntries = renameEntries;
        Title = "События";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _list.SelectionChanged += (_, _) =>
        {
            if (!_filling && Selected is { } name) // a redraw must not wipe a name being typed
                _name.Text = name;
        };

        var events = new StackPanel();
        events.Children.Add(new TextBlock { Text = "События" }.Styled("CardTitle"));
        events.Children.Add(_list);
        events.Children.Add(new TextBlock { Text = "Название события:", Margin = new Thickness(0, 8, 0, 0) });
        events.Children.Add(_name);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Action("Добавить", OnAdd, isDefault: true));
        actions.Children.Add(Action("Переименовать", OnRename));
        actions.Children.Add(Action("Удалить", OnRemove));
        events.Children.Add(actions);

        var markers = new StackPanel();
        markers.Children.Add(new TextBlock { Text = "Обозначения" }.Styled("CardTitle"));
        markers.Children.Add(new ScrollViewer
        {
            Content = _markers, MaxHeight = (SystemParameters.WorkArea.Height - 340) / 2,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right }.Styled("MainButton");
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Border { Child = events }.Styled("Card"));
        root.Children.Add(new Border { Child = markers }.Styled("Card"));
        root.Children.Add(close);
        Content = root;
        Fill(null);
    }

    private string? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as string;

    private void Fill(string? select)
    {
        _filling = true;
        _list.Items.Clear();
        _markers.Children.Clear();
        var settings = _settings();
        foreach (var type in _types())
        {
            // TextBlock: a plain string would swallow "_" in a name as an access key.
            var item = new ListBoxItem
            {
                Content = new TextBlock { Text = $"{type.Name} ({_usage(type.Name)})" }, Tag = type.Name,
                MinHeight = 0, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0), // as low as the tag list
            };
            _list.Items.Add(item);
            if (type.Name == select)
                _list.SelectedItem = item;
            _markers.Children.Add(TypeRow(type));
        }
        _markers.Children.Add(CommonRow("Сегодня", settings.TodayColor, c => _change(s => s with { TodayColor = c })));
        _markers.Children.Add(CommonRow("Несколько событий в день", settings.SeveralColor, c => _change(s => s with { SeveralColor = c })));
        _filling = false;
    }

    private DockPanel TypeRow(EventDefinition type)
    {
        var preview = new ContentControl { Focusable = false, Content = Preview(type.Color, type.Shape) };
        var shapes = new ComboBox { Width = 180, VerticalAlignment = VerticalAlignment.Center };
        foreach (var shape in Enum.GetValues<MarkerShape>())
            shapes.Items.Add(ShapeItem(shape));
        shapes.SelectedIndex = (int)type.Shape;
        shapes.SelectionChanged += (_, _) =>
        {
            var shape = (MarkerShape)shapes.SelectedIndex;
            preview.Content = Preview(type.Color, shape);
            // Not Fill: this combo box is still handling its selection.
            SaveTypes(EventCatalog.Change(_types(), EventCatalog.Find(_types(), type.Name) with { Shape = shape }));
        };
        var color = ColorPick.MenuButton(type.Color, c =>
        {
            SaveTypes(EventCatalog.Change(_types(), EventCatalog.Find(_types(), type.Name) with { Color = c! }));
            Fill(Selected);
        }, null);
        return MarkerRow(preview, type.Name, color, shapes);
    }

    private DockPanel CommonRow(string title, string current, Action<string> save)
    {
        var color = ColorPick.MenuButton(current, c =>
        {
            save(c!);
            Fill(Selected);
        }, null);
        return MarkerRow(new ContentControl { Focusable = false, Content = Preview(current, MarkerShape.Circle) }, title, color, null);
    }

    private static DockPanel MarkerRow(UIElement preview, string title, Button color, ComboBox? shapes)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), MinHeight = 32 };
        DockPanel.SetDock(preview, Dock.Left);
        row.Children.Add(preview);
        if (shapes is not null)
        {
            DockPanel.SetDock(shapes, Dock.Right);
            row.Children.Add(shapes);
        }
        color.VerticalAlignment = VerticalAlignment.Center;
        color.Margin = new Thickness(8, 0, 8, 0);
        DockPanel.SetDock(color, Dock.Left);
        row.Children.Add(color);
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title });
        return row;
    }

    private static Grid Preview(string color, MarkerShape shape)
    {
        var marker = CalendarView.Marker(ColorPick.Brush(color)?.Color ?? Colors.Gray, "А", shape, null);
        marker.VerticalAlignment = VerticalAlignment.Center;
        return marker;
    }

    private static ComboBoxItem ShapeItem(MarkerShape shape)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var figure = new Path { Data = Figures.Geometry(shape, 14), Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        figure.SetResourceReference(Shape.FillProperty, "ButtonText");
        line.Children.Add(figure);
        line.Children.Add(new TextBlock { Text = Figures.Names[(int)shape] });
        return new ComboBoxItem { Content = line };
    }

    private void SaveTypes(IReadOnlyList<EventDefinition> types) => _change(s => s with { EventTypes = types });

    private void OnAdd()
    {
        var name = _name.Text.Trim();
        if (EventCatalog.NameProblem(name, _types()) is { } problem)
        {
            Warn(problem);
            return;
        }
        SaveTypes([.. _types(), new EventDefinition(name)]);
        Fill(name);
    }

    private void OnRename()
    {
        if (Chosen() is not { } old)
            return;
        var name = _name.Text.Trim();
        var types = _types();
        if (EventCatalog.NameProblem(name, types, renaming: old) is { } problem)
            Warn(problem);
        else if (name != old && _renameEntries(old, name))
        {
            SaveTypes(EventCatalog.Rename(types, old, name));
            Fill(name);
        }
    }

    /// <summary>An entry must have a type: those of the deleted one move to another, chosen here.</summary>
    private void OnRemove()
    {
        if (Chosen() is not { } name)
            return;
        var types = _types();
        int count = _usage(name);
        if (count == 0)
        {
            if (MessageBox.Show(this, $"Удалить событие «{name}»?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }
        else
        {
            var others = EventCatalog.Remove(types, name).Select(t => t.Name).ToList();
            if (others.Count == 0)
            {
                Warn($"Записей с событием «{name}»: {count}, а другого события для них нет. Сначала добавьте его.");
                return;
            }
            if (AskReplacement(name, count, others) is not { } replacement || !_renameEntries(name, replacement))
                return;
        }
        SaveTypes(EventCatalog.Remove(types, name));
        _name.Clear();
        Fill(null);
    }

    private string? AskReplacement(string name, int count, List<string> others)
    {
        var dialog = new Window
        {
            Title = "Удаление события", Owner = this, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var choice = new ComboBox { ItemsSource = others, SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 12) };
        var ok = new Button { Content = "Удалить и заменить", IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => dialog.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Отмена", IsCancel = true });
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = $"Записей с событием «{name}»: {count}. Сами записи останутся. Какое событие им назначить?", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(choice);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog.ShowDialog() == true ? choice.SelectedItem as string : null;
    }

    private string? Chosen()
    {
        if (Selected is null)
            Warn("Выберите событие в списке.");
        return Selected;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private static Button Action(string text, Action onClick, bool isDefault = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }
}
