using System.Windows;
using System.Windows.Controls;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Import: the file's entries that are not in the list yet, all ticked; the user adds the ticked ones.</summary>
internal sealed class PickWindow : Window
{
    private readonly List<(Entry Entry, CheckBox Box)> _rows;
    private readonly Button _add;

    public IReadOnlyList<Entry> Selected => [.. _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Entry)];

    /// <param name="skipped">How many of the file's entries are already in the list.</param>
    public PickWindow(IReadOnlyList<Entry> unique, int skipped)
    {
        Title = "Импорт: новые записи";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var list = new ListBox { MaxHeight = SystemParameters.WorkArea.Height - 280 };
        _rows = [.. unique.Select(e => (e, new CheckBox { Content = $"{e.Name} — {ReminderText.Brief(e)}", IsChecked = true, MinHeight = 0 }))];
        foreach (var (_, box) in _rows)
        {
            box.Click += (_, _) => Count();
            list.Items.Add(new ListBoxItem { Content = box, MinHeight = 0, Padding = new Thickness(6, 3, 6, 3) });
        }

        var hint = new TextBlock
        {
            Text = $"Новых записей в файле: {unique.Count}." +
                (skipped > 0 ? $" Уже есть в списке и пропущены: {skipped} (тот же Id или то же имя, событие и дата)." : ""),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        };
        var all = new Button { Content = "Отметить все", Margin = new Thickness(0, 0, 6, 0) };
        all.Click += (_, _) => Tick(true);
        var none = new Button { Content = "Снять все" };
        none.Click += (_, _) => Tick(false);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        marks.Children.Add(all);
        marks.Children.Add(none);

        _add = new Button { IsDefault = true, Margin = new Thickness(0, 0, 6, 0) }.Styled("MainButton");
        _add.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_add);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(12) };
        foreach (var element in new UIElement[] { hint, list, marks, buttons })
            root.Children.Add(element);
        Content = root;
        Count();
    }

    private void Tick(bool on)
    {
        foreach (var (_, box) in _rows)
            box.IsChecked = on;
        Count();
    }

    private void Count()
    {
        int n = _rows.Count(r => r.Box.IsChecked == true);
        _add.Content = $"Добавить ({n})";
        _add.IsEnabled = n > 0;
    }
}
