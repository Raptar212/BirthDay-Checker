using System.Windows;
using System.Windows.Controls;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Add, rename, recolor and delete tags. Every action applies at once; entries change before the list does.</summary>
internal sealed class TagsWindow : Window
{
    private readonly Func<IReadOnlyList<TagDefinition>> _tags;
    private readonly Func<string, int> _usage;
    private readonly Action<IReadOnlyList<TagDefinition>> _save;
    private readonly Func<string, string?, bool> _changeEntries;
    private readonly ListBox _list = new() { MaxHeight = SystemParameters.WorkArea.Height - 240 }; // as tall as its items: scrolls only past the screen
    private readonly TextBox _name = new() { Margin = new Thickness(0, 2, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };

    /// <param name="tags">Current list.</param>
    /// <param name="usage">How many entries carry a tag.</param>
    /// <param name="save">Stores the new list.</param>
    /// <param name="changeEntries">Renames a tag in all entries (null new name: removes it); false if not saved.</param>
    public TagsWindow(Func<IReadOnlyList<TagDefinition>> tags, Func<string, int> usage,
        Action<IReadOnlyList<TagDefinition>> save, Func<string, string?, bool> changeEntries)
    {
        _tags = tags;
        _usage = usage;
        _save = save;
        _changeEntries = changeEntries;
        Title = "Теги";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _list.SelectionChanged += (_, _) =>
        {
            if (Selected is { } name)
                _name.Text = name;
        };

        var label = new TextBlock { Text = "Название тега:", Margin = new Thickness(0, 8, 0, 0) };
        var actions = Row(Action("Добавить", OnAdd, isDefault: true), Action("Переименовать", OnRename), Action("Удалить", OnRemove));
        // Over the list's border and its items' padding, so the titles stand above their columns.
        var header = TwoColumns(new TextBlock { Text = "Цвет", FontWeight = FontWeights.SemiBold }, new TextBlock { Text = "Значение", FontWeight = FontWeights.SemiBold });
        header.Margin = new Thickness(7, 0, 7, 0);
        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }.Styled("MainButton");

        var root = new StackPanel { Margin = new Thickness(12) };
        foreach (var element in new UIElement[] { header, _list, label, _name, actions, close })
            root.Children.Add(element);
        Content = root;
        Fill(null);
    }

    private string? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as string;

    private void Fill(string? select)
    {
        _list.Items.Clear();
        foreach (var tag in _tags())
        {
            var line = TwoColumns(ColorButton(tag), new TextBlock { Text = $"{tag.Name} ({_usage(tag.Name)})", VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem
            {
                Content = line, Tag = tag.Name, MinHeight = 0, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, // the divider runs from row to row
            };
            _list.Items.Add(item);
            if (tag.Name == select)
                _list.SelectedItem = item;
        }
    }

    private void Save(IReadOnlyList<TagDefinition> tags, string? select)
    {
        _save(tags);
        Fill(select);
    }

    private void OnAdd()
    {
        var name = _name.Text.Trim();
        if (TagCatalog.NameProblem(name, _tags()) is { } problem)
        {
            Warn(problem);
            return;
        }
        Save(TagCatalog.Add(_tags(), name), name);
    }

    private void OnRename()
    {
        if (Chosen() is not { } old)
            return;
        var name = _name.Text.Trim();
        var tags = _tags();
        if (TagCatalog.NameProblem(name, tags, renaming: old) is { } problem)
            Warn(problem);
        else if (name != old && _changeEntries(old, name))
            Save(TagCatalog.Rename(tags, old, name), name);
    }

    private Button ColorButton(TagDefinition tag) =>
        ColorPick.MenuButton(tag.Color, color => Recolor(tag.Name, color), "Без цвета");

    private void Recolor(string name, string? color) => Save(TagCatalog.Recolor(_tags(), name, color), name);

    private void OnRemove()
    {
        if (Chosen() is not { } name)
            return;
        var tags = _tags();
        int count = _usage(name);
        var question = count > 0
            ? $"Удалить тег «{name}»? Он будет снят с записей: {count}. Сами записи останутся."
            : $"Удалить тег «{name}»?";
        if (MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
            && _changeEntries(name, null))
            Save(TagCatalog.Remove(tags, name), null);
    }

    private string? Chosen()
    {
        if (Selected is null)
            Warn("Выберите тег в списке.");
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

    /// <summary>The same two columns and the divider between them, for the titles and for every tag.</summary>
    private static Grid TwoColumns(FrameworkElement color, FrameworkElement name)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        color.Margin = new Thickness(0, 3, 0, 3);
        name.Margin = new Thickness(10, 3, 0, 3);
        var divider = new Border();
        divider.SetResourceReference(Border.BackgroundProperty, "GridLine");
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(name, 2);
        grid.Children.Add(color);
        grid.Children.Add(divider);
        grid.Children.Add(name);
        return grid;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }
}
