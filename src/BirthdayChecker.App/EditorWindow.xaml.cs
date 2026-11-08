using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Add / edit one entry. Writes nothing itself: the caller saves <see cref="Result"/>.</summary>
public partial class EditorWindow : Window
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] Months = Ru.DateTimeFormat.MonthNames.Take(12).Select(m => Ru.TextInfo.ToTitleCase(m)).ToArray();

    // Monday first, as Russian users expect; mapped to DayOfWeek by index.
    private static readonly string[] WeekdayNames = ["понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье"];
    private static readonly string[] NthNames = ["1-й", "2-й", "3-й", "4-й", "последний"];

    /// <summary>Default of the legacy app: 1, 2, 3, 5 days, 1 week, 1 month.</summary>
    private const Reminder DefaultReminders =
        Reminder.Day1 | Reminder.Day2 | Reminder.Day3 | Reminder.Day5 | Reminder.Week1 | Reminder.Month1;

    private readonly Entry? _original;
    private readonly ReminderChecks _reminders;
    private readonly TagPicker _tags;
    private readonly List<(string Column, TextBox Box)> _custom = [];
    private string? _color;

    public Entry? Result { get; private set; }

    public EditorWindow(Entry? entry, IReadOnlyList<string> eventTypes, IReadOnlyList<TagDefinition> tags, IReadOnlyList<string> customColumns,
        int? month = null)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        _original = entry;
        Title = entry is null ? "Новая запись" : "Изменить запись";

        TypeBox.ItemsSource = eventTypes;
        DayBox.ItemsSource = Enumerable.Range(1, 31).ToList();
        MonthBox.ItemsSource = Months;
        NthMonthBox.ItemsSource = Months;
        NthBox.ItemsSource = NthNames;
        WeekdayBox.ItemsSource = WeekdayNames;
        _tags = new TagPicker(TagsButton, tags, entry?.Tags ?? []);
        _reminders = new ReminderChecks(RemindersGrid);
        foreach (var column in customColumns)
        {
            var box = new TextBox { Margin = new Thickness(0, 2, 0, 8), Text = entry is null ? "" : ColumnCatalog.Value(entry, column) ?? "" };
            CustomPanel.Children.Add(new TextBlock { Text = column });
            CustomPanel.Children.Add(box);
            _custom.Add((column, box));
        }
        Fill(entry);
        if (entry is null && month is { } m) // "Добавить событие" from the calendar: the 1st of that month
        {
            DayBox.SelectedItem = 1;
            MonthBox.SelectedIndex = NthMonthBox.SelectedIndex = m - 1;
        }
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        if (ColorPick.Pick(_color) is { } c)
            SetColor(c);
    }

    private void OnClearColor(object sender, RoutedEventArgs e) => SetColor(null);

    private void SetColor(string? color)
    {
        _color = color;
        ColorSwatch.Background = ColorPick.Brush(color);
    }

    private void Fill(Entry? e)
    {
        var today = DateTime.Today;
        NameBox.Text = e?.Name ?? "";
        TypeBox.Text = e?.EventType ?? TypeBox.Items.OfType<string>().FirstOrDefault() ?? "";
        PhoneBox.Text = e?.Phone ?? "";
        CommentBox.Text = e?.Comment ?? "";

        // Sensible values in every panel, so switching the kind never shows empty boxes.
        DayBox.SelectedItem = today.Day;
        MonthBox.SelectedIndex = NthMonthBox.SelectedIndex = today.Month - 1;
        NthBox.SelectedIndex = 1;
        WeekdayBox.SelectedIndex = 6;
        OffsetBox.Text = "0";

        switch (e?.Date)
        {
            case NthWeekdayDate n:
                NthRadio.IsChecked = true;
                NthBox.SelectedIndex = n.Nth == NthWeekdayDate.Last ? 4 : n.Nth - 1;
                WeekdayBox.SelectedIndex = ((int)n.Weekday + 6) % 7;
                NthMonthBox.SelectedIndex = n.Month - 1;
                break;
            case EasterDate x:
                EasterRadio.IsChecked = true;
                OffsetBox.Text = x.OffsetDays.ToString(CultureInfo.InvariantCulture);
                break;
            case FixedDate f:
                FixedRadio.IsChecked = true;
                DayBox.SelectedItem = f.Day;
                MonthBox.SelectedIndex = f.Month - 1;
                YearBox.Text = f.Year?.ToString(CultureInfo.InvariantCulture) ?? "";
                break;
            default:
                FixedRadio.IsChecked = true;
                break;
        }

        _reminders.Value = e?.Reminders ?? DefaultReminders;
        SetColor(e?.Color);
    }

    /// <summary>The calendar starts at the year in the box; "Без года" is on if an existing entry has none.</summary>
    private void OnPickDate(object sender, RoutedEventArgs e)
    {
        string text = YearBox.Text.Trim();
        int year = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int y) && y is >= CalendarView.MinYear and <= CalendarView.MaxYear
            ? y : DateTime.Today.Year;
        if (DatePickWindow.Pick(this, year, noYear: text.Length == 0 && _original is not null) is not { } picked)
            return;
        DayBox.SelectedItem = picked.Date.Day;
        MonthBox.SelectedIndex = picked.Date.Month - 1;
        YearBox.Text = picked.NoYear ? "" : picked.Date.Year.ToString(CultureInfo.InvariantCulture);
    }

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        FixedPanel.Visibility = FixedRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        NthPanel.Visibility = NthRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        EasterPanel.Visibility = EasterRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim(), type = TypeBox.Text.Trim();
        if (name.Length == 0) { Error("Укажите имя.", NameBox); return; }
        if (type.Length == 0) { Error("Укажите событие.", TypeBox); return; }
        if (ReadDate() is not { } date) return;

        var withCustom = _custom.Aggregate(_original ?? new Entry(name, type, date), (x, c) => ColumnCatalog.With(x, c.Column, NullIfBlank(c.Box.Text)));
        Result = withCustom with
        {
            Name = name,
            EventType = type,
            Date = date,
            Phone = NullIfBlank(PhoneBox.Text),
            Comment = NullIfBlank(CommentBox.Text),
            Tags = _tags.Selected,
            Reminders = _reminders.Value,
            Color = _color,
        };
        DialogResult = true;
    }

    private EventDate? ReadDate()
    {
        try
        {
            if (NthRadio.IsChecked == true)
                return new NthWeekdayDate(NthMonthBox.SelectedIndex + 1, (DayOfWeek)((WeekdayBox.SelectedIndex + 1) % 7),
                    NthBox.SelectedIndex == 4 ? NthWeekdayDate.Last : NthBox.SelectedIndex + 1);

            if (EasterRadio.IsChecked == true)
            {
                if (!int.TryParse(OffsetBox.Text.Trim().Replace('−', '-'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int offset))
                {
                    Error("Смещение от Пасхи — целое число дней, например 49 или -48.", OffsetBox);
                    return null;
                }
                return new EasterDate(offset);
            }

            int? year = null;
            if (YearBox.Text.Trim() is { Length: > 0 } y)
            {
                if (!int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed is < 1800 or > 9999)
                {
                    Error("Год — число от 1800 до 9999 или пусто, если неизвестен.", YearBox);
                    return null;
                }
                year = parsed;
            }
            return new FixedDate(MonthBox.SelectedIndex + 1, (int)DayBox.SelectedItem, year);
        }
        catch (ArgumentOutOfRangeException)
        {
            Error(EasterRadio.IsChecked == true
                ? $"Смещение от Пасхи — не больше {EasterDate.MaxOffset} дней в любую сторону."
                : "Такой даты не бывает (например, 31 апреля или 29 февраля невисокосного года).", null);
            return null;
        }
    }

    private void Error(string message, Control? focus)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        focus?.Focus();
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
