using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>The user's choices for the markers.</summary>
public sealed record CalendarLook(IReadOnlyList<EventDefinition> Types, string TodayColor, string SeveralColor);

/// <summary>Rounded marker figures.</summary>
internal static class Figures
{
    public static readonly string[] Names = ["Круг", "Ромб", "Квадрат", "Шестиугольник", "Пятиугольник", "Капля", "Треугольник"];

    /// <summary>Clockwise corners in a 0..1 box and the rounding, as a part of the box.</summary>
    private static (Point[] Corners, double Radius) Polygon(MarkerShape shape) => shape switch
    {
        MarkerShape.Diamond => ([new(.5, 0), new(.9, .5), new(.5, 1), new(.1, .5)], .1),
        MarkerShape.Square => ([new(.05, .05), new(.95, .05), new(.95, .95), new(.05, .95)], .22),
        MarkerShape.Hexagon => ([new(.27, .05), new(.73, .05), new(1, .5), new(.73, .95), new(.27, .95), new(0, .5)], .12),
        MarkerShape.Pentagon => ([new(.5, 0), new(1, .38), new(.81, 1), new(.19, 1), new(0, .38)], .14),
        _ => ([new(.5, 0), new(1.04, .95), new(-.04, .95)], .13), // Triangle: a little wider than the box, or a letter won't fit
    };

    public static Geometry Geometry(MarkerShape shape, double size)
    {
        switch (shape)
        {
            case MarkerShape.Circle:
                return new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
            case MarkerShape.Drop:
                var drop = System.Windows.Media.Geometry.Parse("M44,7 Q50,-1 56,7 C70,28 90,46 90,62 A40,38 0 1 1 10,62 C10,46 30,28 44,7 Z").Clone();
                drop.Transform = new ScaleTransform(size / 100, size / 100);
                return drop;
        }
        var (corners, radius) = Polygon(shape);
        return Rounded(corners.Select(c => new Point(c.X * size, c.Y * size)).ToArray(), radius * size);
    }

    /// <summary>Lines between the corners, each corner replaced by an arc that touches both of its sides.</summary>
    private static PathGeometry Rounded(Point[] corners, double radius)
    {
        var figure = new PathFigure { IsClosed = true };
        for (int i = 0; i < corners.Length; i++)
        {
            var corner = corners[i];
            var toPrevious = corners[(i + corners.Length - 1) % corners.Length] - corner;
            var toNext = corners[(i + 1) % corners.Length] - corner;
            double half = Vector.AngleBetween(toPrevious, toNext) * Math.PI / 360;
            double cut = radius / Math.Tan(Math.Abs(half));
            toPrevious.Normalize();
            toNext.Normalize();
            var start = corner + toPrevious * cut;
            if (i == 0)
                figure.StartPoint = start;
            else
                figure.Segments.Add(new LineSegment(start, true));
            figure.Segments.Add(new ArcSegment(corner + toNext * cut, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        }
        return new PathGeometry([figure]);
    }

    /// <summary>The figures narrow upward, so a letter sits below their middle.</summary>
    public static double TextShift(MarkerShape shape) => shape switch
    {
        MarkerShape.Triangle => 4,
        MarkerShape.Drop => 5,
        MarkerShape.Pentagon => 1,
        _ => 0,
    };
}

/// <summary>Calendar of a year, or of one month in focus, with a marker on each day that has events.</summary>
public sealed class CalendarView : DockPanel
{
    public const int MinYear = 1900, MaxYear = 2100;
    private const double CellHeight = 26, MonthWidth = 224, MonthSpacing = 20, MinListWidth = 640, ArrowSpace = 32, MarkerSize = 23;
    private const double Slot = MonthWidth + MonthSpacing, FocusScale = 2, FocusMargin = 8;
    private const int MaxAutoColumns = 3;

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] Weekdays = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];
    private static readonly Color Winter = Color.FromRgb(0x6E, 0xC6, 0xF5);
    private static readonly Color Spring = Color.FromRgb(0xF0, 0x56, 0x7A);
    private static readonly Color Summer = Color.FromRgb(0x43, 0xA0, 0x47);
    private static readonly Color Autumn = Color.FromRgb(0xF2, 0x8C, 0x28);

    private readonly TextBlock _title = new()
    {
        FontSize = 16, FontWeight = FontWeights.SemiBold, Width = 60, TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly WrapPanel _months = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly WrapPanel _legend = new() { Margin = new Thickness(0, 8, 0, 0), ItemWidth = Slot, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer _year = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Grid _overlay = new() { Background = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly System.Windows.Media.Effects.BlurEffect _blur = new() { Radius = 8 };
    private CalendarLook _look = new([], EventCatalog.DefaultTodayColor, EventCatalog.DefaultSeveralColor);
    private int? _focus;

    public int Year { get; private set; } = DateTime.Today.Year;

    /// <summary>The width set with the divider; null = by the window's width.</summary>
    public double? WantedWidth { get; set; }

    public event Action? YearChanged;

    public event Action<int>? MonthClicked;

    /// <summary>An arrow beside the month in focus: -1 or +1.</summary>
    public event Action<int>? MonthStepped;

    /// <summary>"Текущий год" was pressed; raised before the year changes.</summary>
    public event Action? FocusLeft;

    /// <summary>"Добавить событие" in a month's menu.</summary>
    public event Action<int>? AddRequested;

    /// <summary>The year switcher; the window places it on its toolbar row.</summary>
    public StackPanel Nav { get; } = new() { Orientation = Orientation.Horizontal };

    public CalendarView()
    {
        Nav.Children.Add(NavButton("◀", () => Go(Year - 1)));
        Nav.Children.Add(_title);
        Nav.Children.Add(NavButton("▶", () => Go(Year + 1)));
        Nav.Children.Add(NavButton("Текущий год", () =>
        {
            FocusLeft?.Invoke();
            Go(DateTime.Today.Year);
        }));
        var content = new StackPanel();
        content.Children.Add(_months);
        content.Children.Add(_legend); // scrolls with the months: pinned, it would squeeze them out of a low window
        content.Children.Add(new TextBlock
        {
            Text = "В значке — первая буква имени. Свой цвет записи заменяет цвет события.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        }.Hinted());
        _year.Content = content;
        // A click on the dimmed year beside the month in focus leaves the focus, as a second click on the month does.
        _overlay.MouseLeftButtonUp += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _overlay) && _focus is { } month)
                MonthClicked?.Invoke(month);
        };
        var layers = new Grid();
        layers.Children.Add(_year);
        layers.Children.Add(_overlay);
        Children.Add(layers);
    }

    private void FillLegend()
    {
        var first = _look.Types.FirstOrDefault() ?? new EventDefinition("");
        _legend.Children.Clear();
        _legend.Children.Add(LegendItem(Marker(Hex(_look.TodayColor), "15", MarkerShape.Circle, null), "Сегодня"));
        _legend.Children.Add(LegendItem(Marker(Hex(first.Color), "А", first.Shape, Hex(_look.TodayColor)), "Сегодня есть событие"));
        _legend.Children.Add(LegendItem(Marker(Hex(_look.SeveralColor), "2", MarkerShape.Circle, null), "Несколько событий в день"));
        foreach (var type in _look.Types)
            _legend.Children.Add(LegendItem(Marker(Hex(type.Color), "А", type.Shape, null), type.Name));
    }

    private static StackPanel LegendItem(FrameworkElement marker, string text)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        item.Children.Add(marker);
        item.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return item;
    }

    private static Button NavButton(string text, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    public void Go(int year)
    {
        Year = Math.Clamp(year, MinYear, MaxYear);
        YearChanged?.Invoke();
    }

    /// <param name="items">Occurrences of the whole year.</param>
    /// <param name="today">Today's date.</param>
    /// <param name="focus">The month to show alone; null = the whole year.</param>
    /// <param name="look">The user's markers.</param>
    public void Show(IReadOnlyList<Occurrence> items, DateOnly today, int? focus, CalendarLook look)
    {
        _look = look;
        _title.Text = Year.ToString(CultureInfo.InvariantCulture);
        var byDate = items.ToLookup(o => o.Date);
        _months.Children.Clear();
        for (int month = 1; month <= 12; month++)
            _months.Children.Add(Month(month, byDate, today, false));
        _focus = focus;
        _overlay.Children.Clear();
        if (focus is { } only)
            _overlay.Children.Add(FocusRow(Month(only, byDate, today, true)));
        _overlay.Visibility = focus is null ? Visibility.Collapsed : Visibility.Visible;
        _year.Effect = focus is null ? null : _blur;
        _year.IsHitTestVisible = focus is null;
        FillLegend();
        ResizeMonths();
    }

    /// <summary>The month enlarged between its arrows, in the middle of the panel; shrinks only if the panel is too low for it.</summary>
    private Viewbox FocusRow(Border month)
    {
        month.LayoutTransform = new ScaleTransform(FocusScale, FocusScale);
        var row = new DockPanel();
        var previous = Arrow("◀", -1, new Thickness(0, 0, 4, 0));
        var next = Arrow("▶", +1, new Thickness(4, 0, 0, 0));
        SetDock(previous, Dock.Left);
        SetDock(next, Dock.Right);
        row.Children.Add(previous);
        row.Children.Add(next);
        row.Children.Add(month);
        return new Viewbox
        {
            Child = row, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(FocusMargin),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Button Arrow(string text, int step, Thickness margin)
    {
        var arrow = new Button
        {
            Content = text, Width = ArrowSpace - 4, Padding = new Thickness(0), Margin = margin, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = step < 0 ? "Предыдущий месяц" : "Следующий месяц",
        };
        arrow.Click += (_, _) => MonthStepped?.Invoke(step);
        return arrow;
    }

    /// <summary>The enlarged month with its arrows must fit, whatever the divider and the window say.</summary>
    private static double SmallestWidth => FocusScale * MonthWidth + 2 * (ArrowSpace + FocusMargin);

    /// <summary>The wanted width, or as many whole months as leave the list next to it readable; the list always keeps its minimum if it can.</summary>
    public void FitBeside(double available)
    {
        double wanted = WantedWidth
            ?? Math.Clamp((int)((available - MinListWidth) / Slot), 1, MaxAutoColumns) * Slot + SystemParameters.VerticalScrollBarWidth;
        Width = Math.Clamp(wanted, SmallestWidth, Math.Max(SmallestWidth, available - MinListWidth));
        ResizeMonths();
    }

    /// <summary>Months keep their size; the width only decides how many fit in a row, and the rows sit in the middle.</summary>
    private void ResizeMonths()
    {
        double inner = (double.IsNaN(Width) ? SmallestWidth : Width) - SystemParameters.VerticalScrollBarWidth;
        _months.Width = _legend.Width = Math.Max(1, (int)(inner / Slot)) * Slot;
    }

    private Border Month(int month, ILookup<DateOnly, Occurrence> byDate, DateOnly today, bool focused)
    {
        var panel = new StackPanel();
        var frame = new Border
        {
            Width = MonthWidth, Margin = focused ? new Thickness(0) : new Thickness(MonthSpacing / 2, 0, MonthSpacing / 2, 16),
            Padding = new Thickness(4),
            BorderBrush = new SolidColorBrush(SeasonColor(month)), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, // transparent, not null: empty cells must take the click too
            Child = panel,
        };
        if (focused) // opaque over the blurred year, and in the window's own color in either theme
            frame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Window.Background))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1),
            });
        frame.MouseLeftButtonUp += (_, _) => MonthClicked?.Invoke(month);
        var add = new MenuItem { Header = "Добавить событие" };
        add.Click += (_, _) => AddRequested?.Invoke(month);
        frame.ContextMenu = new ContextMenu { Items = { add } };
        panel.Children.Add(new TextBlock
        {
            Text = Ru.TextInfo.ToTitleCase(Ru.DateTimeFormat.MonthNames[month - 1]),
            FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 4),
        });

        var header = new UniformGrid { Columns = 7 };
        foreach (var name in Weekdays)
            header.Children.Add(new TextBlock
            {
                Text = name, FontSize = 12, TextAlignment = TextAlignment.Center,
            }.Hinted());
        panel.Children.Add(header);

        var first = new DateOnly(Year, month, 1);
        var days = new UniformGrid
        {
            Columns = 7, Rows = 6, Height = 6 * CellHeight,
            FirstColumn = ((int)first.DayOfWeek + 6) % 7, // Monday first
        };
        for (int day = 1; day <= DateTime.DaysInMonth(Year, month); day++)
        {
            var date = new DateOnly(Year, month, day);
            days.Children.Add(Day(day, byDate[date].ToList(), date == today));
        }
        panel.Children.Add(days);
        return frame;
    }

    private static Color SeasonColor(int month) => month switch
    {
        12 or 1 or 2 => Winter,
        3 or 4 or 5 => Spring,
        6 or 7 or 8 => Summer,
        _ => Autumn,
    };

    private FrameworkElement Day(int day, List<Occurrence> items, bool isToday)
    {
        string number = day.ToString(CultureInfo.InvariantCulture);
        if (items.Count == 0)
            return isToday
                ? Marker(Hex(_look.TodayColor), number, MarkerShape.Circle, null)
                : new TextBlock { Text = number, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        Color? ring = isToday ? Hex(_look.TodayColor) : null;
        var marker = items.Count > 1
            ? Marker(Hex(_look.SeveralColor), items.Count.ToString(CultureInfo.InvariantCulture), MarkerShape.Circle, ring)
            : Single(items[0].Entry, ring);
        marker.ToolTip = ReminderText.Describe(items);
        return marker;
    }

    private Grid Single(Entry entry, Color? ring)
    {
        var type = EventCatalog.Find(_look.Types, entry.EventType);
        var letter = new StringInfo(entry.Name.TrimStart()).SubstringByTextElements(0, 1).ToUpper(Ru); // whole emoji, not half of it
        return Marker(ColorPick.Brush(entry.Color)?.Color ?? Hex(type.Color), letter, type.Shape, ring);
    }

    internal static Grid Marker(Color back, string text, MarkerShape shape, Color? ring)
    {
        var marker = new Grid { Width = MarkerSize, Height = MarkerSize };
        marker.Children.Add(new Path
        {
            Data = Figures.Geometry(shape, MarkerSize),
            Fill = new SolidColorBrush(back),
            Stroke = ring is { } color ? new SolidColorBrush(color) : null,
            StrokeThickness = 2,
        });
        marker.Children.Add(new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = IsLight(back) ? Brushes.Black : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Figures.TextShift(shape), 0, 0),
        });
        return marker;
    }

    private static Color Hex(string hex) => ColorPick.Brush(hex)?.Color ?? Colors.Gray;

    private static bool IsLight(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150;
}
