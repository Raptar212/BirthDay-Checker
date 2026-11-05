using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Group mode: one change for all selected entries, with a live preview. Unticked parts stay as they are.</summary>
/// <remarks>Name and date stay out on purpose: the same value on several entries is a mistake, not an edit.</remarks>
internal sealed class GroupEditWindow : Window
{
    private readonly IReadOnlyList<Entry> _selected;
    private readonly IReadOnlyList<TagDefinition> _tags;
    private readonly CheckBox _changeType = new() { Content = "Изменить событие:" };
    private readonly ComboBox _type = new() { IsEditable = true, Margin = new Thickness(0, 2, 0, 8) };
    private readonly CheckBox _changePhone = new() { Content = "Изменить телефон (галочка и пусто — убрать):" };
    private readonly TextBox _phone = new() { Margin = new Thickness(0, 2, 0, 8) };
    private readonly CheckBox _changeComment = new() { Content = "Изменить комментарий (галочка и пусто — убрать):" };
    private readonly TextBox _comment = new() { Margin = new Thickness(0, 2, 0, 8), Height = 44, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly List<(string Column, CheckBox Change, TextBox Box)> _custom = [];
    private readonly IReadOnlyList<string> _customColumns;
    private readonly CheckBox _changeReminders = new() { Content = "Заменить «Напомнить заранее» на:", Margin = new Thickness(0, 0, 0, 4) };
    private readonly ReminderChecks _reminders;
    private readonly TagPicker _addTags;
    private readonly TagPicker _removeTags;
    private readonly CheckBox _changeColor = new() { Content = "Изменить цвет строки:", VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _swatch = new() { Width = 40, Height = 18, Margin = new Thickness(6, 0, 6, 0), BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1) };
    private readonly DataGrid _preview;
    private string? _color;

    public GroupEdit? Result { get; private set; }

    /// <param name="selected">The entries to change, in list order.</param>
    /// <param name="eventTypes">Choices for the event type.</param>
    /// <param name="tags">The shared tag list.</param>
    /// <param name="customColumns">The user's own columns.</param>
    /// <param name="rowStyle">The main list's row style, so the preview colors rows exactly like it.</param>
    public GroupEditWindow(IReadOnlyList<Entry> selected, IReadOnlyList<string> eventTypes, IReadOnlyList<TagDefinition> tags, IReadOnlyList<string> customColumns, Style rowStyle)
    {
        _selected = selected;
        _tags = tags;
        _customColumns = customColumns;
        Title = $"Групповая правка — записей: {selected.Count}";
        Width = Math.Min(1240, SystemParameters.WorkArea.Width);
        Height = Math.Min(790, SystemParameters.WorkArea.Height);
        MinWidth = 800;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var addButton = new Button { Margin = new Thickness(0, 2, 6, 8), HorizontalContentAlignment = HorizontalAlignment.Left };
        var removeButton = new Button { Margin = new Thickness(0, 2, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left };
        addButton.SetBinding(MinHeightProperty, new Binding(nameof(ActualHeight)) { Source = _phone });
        removeButton.SetBinding(MinHeightProperty, new Binding(nameof(ActualHeight)) { Source = _phone });
        _addTags = new TagPicker(addButton, tags, []);
        _removeTags = new TagPicker(removeButton, tags, []);
        _addTags.Changed += ShowPreview;
        _removeTags.Changed += ShowPreview;

        _preview = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, AlternationCount = 2, RowStyle = rowStyle,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.All,
            CanUserSortColumns = false,
        };
        _preview.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "GridLine");
        _preview.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "GridLine");
        foreach (var (header, path, width) in new[]
        {
            ("Имя", nameof(MainWindow.Row.Name), 170.0), ("Дата", nameof(MainWindow.Row.DateText), 90.0),
            ("Событие", nameof(MainWindow.Row.Type), 110.0), ("Теги", nameof(MainWindow.Row.Tags), 110.0),
            ("Напомнить", nameof(MainWindow.Row.RemindersText), 150.0), ("Телефон", nameof(MainWindow.Row.Phone), 110.0),
            ("Комментарий", nameof(MainWindow.Row.Comment), 150.0),
        })
            _preview.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });
        for (int i = 0; i < customColumns.Count; i++)
        {
            _preview.Columns.Add(new DataGridTextColumn { Header = customColumns[i], Binding = new Binding($"{nameof(MainWindow.Row.Custom)}[{i}]"), Width = 110 });
            var change = new CheckBox { Content = $"Изменить «{customColumns[i]}» (галочка и пусто — убрать):" };
            var box = new TextBox { Margin = new Thickness(0, 2, 0, 8) };
            box.TextChanged += (_, _) => Edited(change, box.Text);
            change.Checked += (_, _) => ShowPreview();
            change.Unchecked += (_, _) => ShowPreview();
            _custom.Add((customColumns[i], change, box));
        }
        _preview.Columns[^1].MinWidth = 100;
        _preview.Columns[^1].Width = new DataGridLength(1, DataGridLengthUnitType.Star); // nothing empty after the last column, as in the main list
        foreach (var column in _preview.Columns)
            ((DataGridTextColumn)column).ElementStyle = Ui.CenteredCell;
        _preview.SelectionChanged += (_, _) => _preview.UnselectAll(); // a selected row would hide the color being previewed

        _type.ItemsSource = eventTypes;
        _type.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Edited(_changeType, _type.Text)));
        _phone.TextChanged += (_, _) => Edited(_changePhone, _phone.Text);
        _comment.TextChanged += (_, _) => Edited(_changeComment, _comment.Text);
        foreach (var check in new[] { _changeType, _changePhone, _changeComment, _changeColor })
        {
            check.Checked += (_, _) => ShowPreview();
            check.Unchecked += (_, _) => ShowPreview();
        }

        var remindersGrid = new Grid { IsEnabled = false };
        _reminders = new ReminderChecks(remindersGrid);
        _reminders.Changed += ShowPreview;
        _changeReminders.Checked += (_, _) => { remindersGrid.IsEnabled = true; ShowPreview(); };
        _changeReminders.Unchecked += (_, _) => { remindersGrid.IsEnabled = false; ShowPreview(); };

        var pick = new Button { Content = "Выбрать…", VerticalAlignment = VerticalAlignment.Center }.Styled("SmallButton");
        pick.Click += (_, _) =>
        {
            if (ColorPick.Pick(_color) is { } c) SetColor(c);
        };
        var clear = new Button { Content = "Без цвета", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Styled("SmallButton");
        clear.Click += (_, _) => SetColor(null);
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var c in new UIElement[] { _changeColor, _swatch, pick, clear }) colorRow.Children.Add(c);

        var tagsRow = new Grid();
        tagsRow.ColumnDefinitions.Add(new ColumnDefinition());
        tagsRow.ColumnDefinitions.Add(new ColumnDefinition());
        var addTags = new StackPanel { Children = { new TextBlock { Text = "Добавить теги" }, addButton } };
        var removeTags = new StackPanel { Children = { new TextBlock { Text = "Убрать теги" }, removeButton } };
        Grid.SetColumn(removeTags, 1);
        tagsRow.Children.Add(addTags);
        tagsRow.Children.Add(removeTags);

        var settings = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; // room for the scroll bar: it would lie over the fields
        foreach (var element in (UIElement[])
        [
            _changeType, _type, _changePhone, _phone, _changeComment, _comment,
            .. _custom.SelectMany(c => new UIElement[] { c.Change, c.Box }),
            _changeReminders, new Border { Child = remindersGrid }.Styled("Card"),
            tagsRow,
            colorRow, ColorPick.Palette(SetColor),
        ])
            settings.Children.Add(element);

        var title = new TextBlock { Text = $"Что будет с выбранными записями ({selected.Count}):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        var hint = new TextBlock { Text = "Подсветка сегодняшних и ближайших 7 дней важнее цвета строки, как и в основном списке.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }.Hinted();
        var previewPanel = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        previewPanel.Children.Add(title);
        previewPanel.Children.Add(hint);
        previewPanel.Children.Add(_preview);

        var save = new Button { Content = "Применить", IsDefault = true, Margin = new Thickness(0, 0, 8, 0) }.Styled("MainButton");
        save.Click += (_, _) => Apply();
        var cancel = new Button { Content = "Отмена", IsCancel = true }.Styled("MainButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var scroll = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 440 };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(scroll, Dock.Left);
        root.Children.Add(buttons);
        root.Children.Add(scroll);
        root.Children.Add(previewPanel);
        Content = root;
        ShowPreview();
    }

    /// <summary>Typing in a field means changing it; a slip erased back to empty must not wipe it on every entry, so clearing takes a manual tick.</summary>
    private void Edited(CheckBox change, string text)
    {
        change.IsChecked = !string.IsNullOrWhiteSpace(text);
        ShowPreview();
    }

    private void SetColor(string? color)
    {
        _color = color;
        _swatch.Background = ColorPick.Brush(color);
        _changeColor.IsChecked = true;
        ShowPreview();
    }

    private GroupEdit Current() => new()
    {
        EventType = _changeType.IsChecked == true && _type.Text.Trim() is { Length: > 0 } type ? type : null,
        ChangePhone = _changePhone.IsChecked == true,
        Phone = NullIfBlank(_phone.Text),
        ChangeComment = _changeComment.IsChecked == true,
        Comment = NullIfBlank(_comment.Text),
        Reminders = _changeReminders.IsChecked == true ? _reminders.Value : null,
        AddTags = _addTags.Selected,
        RemoveTags = _removeTags.Selected,
        ChangeColor = _changeColor.IsChecked == true,
        Color = _color,
        Custom = _custom.Where(c => c.Change.IsChecked == true).ToDictionary(c => c.Column, c => NullIfBlank(c.Box.Text)),
    };

    private void ShowPreview()
    {
        var edit = Current();
        var today = DateOnly.FromDateTime(DateTime.Today);
        _preview.ItemsSource = _selected.Select(edit.Apply)
            .Select(e => new MainWindow.Row(Upcoming.Next(e, today), TagCatalog.RowColor(e, _tags), _customColumns))
            .ToList();
    }

    private void Apply()
    {
        if (_changeType.IsChecked == true && string.IsNullOrWhiteSpace(_type.Text))
        {
            MessageBox.Show(this, "Укажите событие или снимите галочку «Изменить событие».", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _type.Focus();
            return;
        }
        Result = Current();
        DialogResult = true;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Read-only report text with copy and save.</summary>
internal sealed class ReportWindow : Window
{
    public ReportWindow(string text)
    {
        Title = "Отчёт — список грядущих праздников";
        Width = 760;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var copy = new Button { Content = "Копировать", Margin = new Thickness(0, 0, 8, 0) }.Styled("MainButton");
        copy.Click += (_, _) => Clipboard.SetText(text);
        var save = new Button { Content = "Сохранить в файл…", Margin = new Thickness(0, 0, 8, 0) }.Styled("MainButton");
        save.Click += (_, _) => Save(text);
        var close = new Button { Content = "Закрыть", IsCancel = true }.Styled("MainButton");
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var b in new[] { copy, save, close }) buttons.Children.Add(b);
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(box);
        Content = root;
    }

    private void Save(string text)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"праздники-{DateTime.Today:yyyy-MM-dd}.txt",
            Filter = "Текст (*.txt)|*.txt",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Не удалось сохранить: {e.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
