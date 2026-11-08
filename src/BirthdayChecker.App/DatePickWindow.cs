using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>The date of an entry chosen in the calendar: year, then month, then the day in the enlarged month.</summary>
internal static class DatePickWindow
{
    /// <summary>No event types: an empty calendar, only today marked.</summary>
    private static readonly CalendarLook Plain = new([], EventCatalog.DefaultTodayColor, EventCatalog.DefaultSeveralColor);

    /// <summary>Null when the window is closed without a day.</summary>
    /// <param name="noYear">"Без года" is on at the start: the day and month are filled, the year is left empty.</param>
    public static (DateOnly Date, bool NoYear)? Pick(Window owner, int year, bool noYear)
    {
        var calendar = new CalendarView { Picking = true, Width = CalendarView.WidthFor(4) }; // 3 rows: the whole year fits without scrolling
        calendar.Go(year);
        var window = new Window
        {
            Title = "Выбор даты", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight, MaxHeight = SystemParameters.WorkArea.Height,
        };
        // One text line inside the box: a separate block beside it sits lower than the box's own text.
        var label = new TextBlock { Text = "Без года " };
        var note = new System.Windows.Documents.Run("(только число и месяц)");
        note.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Hint");
        label.Inlines.Add(note);
        var withoutYear = new CheckBox
        {
            Content = label, IsChecked = noYear, MinWidth = 0, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        int? focus = null;
        (DateOnly, bool)? result = null;
        void Refresh() => calendar.Show([], DateOnly.FromDateTime(DateTime.Today), focus, Plain);
        calendar.YearChanged += Refresh;
        calendar.FocusLeft += () => focus = null;
        calendar.MonthClicked += month =>
        {
            focus = focus == month ? null : month;
            Refresh();
        };
        calendar.MonthStepped += step =>
        {
            if (focus is { } month && CalendarView.Step(calendar.Year, month, step) is { } next)
            {
                focus = next.Month;
                calendar.Go(next.Year);
            }
        };
        calendar.DayClicked += date =>
        {
            result = (date, withoutYear.IsChecked == true);
            window.DialogResult = true;
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(calendar.Nav);
        bar.Children.Add(withoutYear);
        DockPanel.SetDock(bar, Dock.Top);
        window.Content = new DockPanel { Margin = new Thickness(12), Children = { bar, calendar } };
        window.Loaded += (_, _) => Refresh();
        window.ShowDialog();
        return result;
    }
}
