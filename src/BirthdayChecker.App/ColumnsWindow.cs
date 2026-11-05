using System.Windows;
using System.Windows.Controls;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Show or hide table columns; add, rename and delete the user's own. Every action applies at once, as in <see cref="TagsWindow"/>.</summary>
internal sealed class ColumnsWindow : Window
{
    private readonly Func<IReadOnlyList<string>> _custom;
    private readonly Func<IReadOnlyList<string>> _hidden;
    private readonly Func<string, int> _usage;
    private readonly Action<string> _add;
    private readonly Action<string, string> _rename;
    private readonly Action<string> _remove;
    private readonly Action<string, bool> _setVisible;
    private readonly ListBox _list = new() { MaxHeight = SystemParameters.WorkArea.Height - 280 }; // as tall as its items: scrolls only past the screen
    private readonly TextBox _name = new() { MaxLength = 40, Margin = new Thickness(0, 2, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };

    /// <param name="custom">The user's own columns.</param>
    /// <param name="hidden">Headers of the hidden columns.</param>
    /// <param name="usage">How many entries have a value in a column.</param>
    /// <param name="add">Adds a column.</param>
    /// <param name="rename">Renames a column here and in all entries.</param>
    /// <param name="remove">Deletes a column and its values.</param>
    /// <param name="setVisible">Shows or hides a column.</param>
    public ColumnsWindow(Func<IReadOnlyList<string>> custom, Func<IReadOnlyList<string>> hidden, Func<string, int> usage,
        Action<string> add, Action<string, string> rename, Action<string> remove, Action<string, bool> setVisible)
    {
        _custom = custom;
        _hidden = hidden;
        _usage = usage;
        _add = add;
        _rename = rename;
        _remove = remove;
        _setVisible = setVisible;
        Title = "Столбцы таблицы";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _list.SelectionChanged += (_, _) =>
        {
            if (SelectedCustom is { } name)
                _name.Text = name;
        };

        var hint = new TextBlock { Text = "Галочка — столбец виден. Порядок и ширина меняются прямо в таблице, перетаскиванием.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var label = new TextBlock { Text = "Название своего столбца:", Margin = new Thickness(0, 8, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var child in new UIElement[] { Action("Добавить", OnAdd, isDefault: true), Action("Переименовать", OnRename), Action("Удалить", OnRemove) })
            row.Children.Add(child);
        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }.Styled("MainButton");

        var root = new StackPanel { Margin = new Thickness(12) };
        foreach (var element in new UIElement[] { hint, _list, label, _name, row, close })
            root.Children.Add(element);
        Content = root;
        Fill(null);
    }

    private string? SelectedCustom => (_list.SelectedItem as ListBoxItem)?.Tag as string;

    private void Fill(string? select)
    {
        _list.Items.Clear();
        var hidden = _hidden();
        foreach (var name in ColumnCatalog.BuiltIn)
            AddLine(name, name, null);
        foreach (var name in _custom())
            AddLine($"{name} ({_usage(name)})", name, name);

        void AddLine(string text, string name, string? custom)
        {
            bool locked = ColumnCatalog.IsLocked(name);
            var check = new CheckBox
            {
                Content = locked ? text + " — всегда виден" : text, IsEnabled = !locked, MinHeight = 0,
                IsChecked = !hidden.Contains(name, StringComparer.CurrentCultureIgnoreCase),
            };
            check.Checked += (_, _) => _setVisible(name, true);
            check.Unchecked += (_, _) => _setVisible(name, false);
            var item = new ListBoxItem { Content = check, Tag = custom, MinHeight = 0, Padding = new Thickness(6, 3, 6, 3) };
            _list.Items.Add(item);
            if (custom is not null && custom == select)
                _list.SelectedItem = item;
        }
    }

    private void OnAdd()
    {
        var name = _name.Text.Trim();
        if (ColumnCatalog.NameProblem(name, _custom()) is { } problem)
        {
            Warn(problem);
            return;
        }
        _add(name);
        Fill(name);
    }

    private void OnRename()
    {
        if (Chosen() is not { } old)
            return;
        var name = _name.Text.Trim();
        if (ColumnCatalog.NameProblem(name, _custom(), renaming: old) is { } problem)
            Warn(problem);
        else if (name != old)
        {
            _rename(old, name);
            Fill(name);
        }
    }

    private void OnRemove()
    {
        if (Chosen() is not { } name)
            return;
        int count = _usage(name);
        var question = count > 0
            ? $"Удалить столбец «{name}» и его значения в записях ({count})? Вернуть их можно будет только из резервной копии."
            : $"Удалить столбец «{name}»?";
        if (MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _remove(name);
        _name.Clear();
        Fill(null);
    }

    private string? Chosen()
    {
        if (SelectedCustom is null)
            Warn("Выберите свой столбец в списке. Встроенные столбцы можно только скрыть.");
        return SelectedCustom;
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
