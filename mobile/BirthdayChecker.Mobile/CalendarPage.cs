using System.Globalization;
using BirthdayChecker.Core;
using Entry = BirthdayChecker.Core.Entry; // MAUI has its own Entry (a text box)

namespace BirthdayChecker.Mobile;

/// <summary>
/// The year calendar, as on the PC: 12 months two per row with a marker on each day that has events, season-colored
/// frames and the legend. A tap on a month goes back to the list filtered by it (<see cref="MainPage.Filter"/>);
/// a swipe either way (seen by MainActivity) goes back to the list as is.
/// </summary>
public sealed class CalendarPage : ContentPage
{
	internal const int MinYear = 1900, MaxYear = 2100;

	private readonly MainPage _owner;
	private bool _closing;
	private readonly Label _title = new() { FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center };
	private readonly Button _back = new() { Text = "Текущий год" };
	private readonly Grid _months = new() { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8, RowSpacing = 8 };
	private readonly FlexLayout _legend = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 8, 0, 0) };
	private readonly ScrollView _scroll = new();

	public CalendarPage(MainPage owner)
	{
		_owner = owner;
		Title = "Календарь";

		var previous = Arrow("‹", "Предыдущий год");
		previous.Clicked += (_, _) => Step(-1);
		var next = Arrow("›", "Следующий год");
		next.Clicked += (_, _) => Step(1);
		_back.Clicked += (_, _) => Step(DateTime.Today.Year - Year);
		var header = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
		header.Add(previous, 0);
		header.Add(_title, 1);
		header.Add(next, 2);

		var root = new VerticalStackLayout { Padding = 12, Spacing = 8, Children = { header, _back, _months, _legend } };
		_scroll.Content = root;
		Content = _scroll;
		Fill();
		Loaded += async (_, _) => await ScrollToFocus();
	}

	/// <summary>Flat, so the arrows don't outweigh the months (BDC-57).</summary>
	private static Button Arrow(string text, string description)
	{
		var arrow = new Button { Text = text, FontSize = 32, Padding = new Thickness(16, 0), BackgroundColor = Colors.Transparent };
		arrow.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
		SemanticProperties.SetDescription(arrow, description);
		return arrow;
	}

	/// <summary>Opens at the month in focus, or at the current one, not at January (BDC-57).</summary>
	private async Task ScrollToFocus()
	{
		var today = DateTime.Today;
		int? month = _owner.Filter.Month ?? (Year == today.Year ? today.Month : null);
		if (month is int m && m > 2)
			await _scroll.ScrollToAsync((Element)_months.Children[m - 1], ScrollToPosition.Center, animated: false);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		MainActivity.HorizontalSwipe -= OnSwipe;
		MainActivity.HorizontalSwipe += OnSwipe;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		MainActivity.HorizontalSwipe -= OnSwipe;
	}

	/// <summary>Either way back to the list; the year is stepped by the arrows.</summary>
	private async void OnSwipe(bool right) => await Close();

	/// <summary>Once: a second fling or tap during the closing animation is ignored.</summary>
	private async Task Close()
	{
		if (_closing || Navigation.NavigationStack.LastOrDefault() != this)
			return;
		_closing = true;
		await Navigation.PopAsync();
	}

	private int Year => _owner.CalendarYear;

	private void Step(int step)
	{
		if (Year + step is >= MinYear and <= MaxYear)
			_owner.CalendarYear = Year + step;
		Fill();
	}

	/// <summary>The list shows this month of the calendar's year (<see cref="EntryList.InYear"/>).</summary>
	private async void ShowMonth(int month)
	{
		if (_closing)
			return;
		_owner.Filter = _owner.Filter with { Month = month };
		await Close();
	}

	private void Fill()
	{
		var today = DateOnly.FromDateTime(DateTime.Now);
		var settings = _owner.Settings;
		var look = new Look(
			EntryList.InYear(_owner.Entries, Year, today, _owner.Filter with { Month = null }).ToLookup(o => o.Date),
			today,
			_owner.EventList,
			Color.FromArgb(settings.TodayColor),
			Color.FromArgb(settings.SeveralColor),
			Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black);

		_months.Clear();
		_months.RowDefinitions.Clear();
		_title.Text = Year.ToString(CultureInfo.InvariantCulture);
		_back.IsVisible = Year != today.Year;
		for (int m = 1; m <= 12; m++)
		{
			if (m % 2 == 1)
				_months.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			var view = new GraphicsView { HeightRequest = 190, Drawable = new MonthDrawable(Year, m, look) };
			int month = m;
			var tap = new TapGestureRecognizer();
			tap.Tapped += (_, _) => ShowMonth(month);
			view.GestureRecognizers.Add(tap);
			_months.Add(view, (m - 1) % 2, (m - 1) / 2);
		}
		FillLegend(look);
	}

	private void FillLegend(Look look)
	{
		_legend.Clear();
		var first = look.Types.FirstOrDefault() ?? new EventDefinition("");
		_legend.Add(LegendItem((c, r) => Marker.Draw(c, r, look.TodayColor, "15", MarkerShape.Circle, null), "Сегодня", look.Text));
		_legend.Add(LegendItem((c, r) => Marker.Draw(c, r, Color.FromArgb(first.Color), "А", first.Shape, look.TodayColor), "Сегодня есть событие", look.Text));
		_legend.Add(LegendItem((c, r) => Marker.Draw(c, r, look.SeveralColor, "2", MarkerShape.Circle, null), "Несколько в день", look.Text));
		foreach (var type in look.Types)
			_legend.Add(LegendItem((c, r) => Marker.Draw(c, r, Color.FromArgb(type.Color), "А", type.Shape, null), type.Name, look.Text));
	}

	private static HorizontalStackLayout LegendItem(Action<ICanvas, RectF> draw, string text, Color textColor) => new()
	{
		Spacing = 6,
		Margin = new Thickness(0, 0, 14, 8),
		Children =
		{
			new GraphicsView { WidthRequest = 26, HeightRequest = 26, Drawable = new Drawn(draw) },
			new Label { Text = text, VerticalOptions = LayoutOptions.Center, TextColor = textColor },
		},
	};

	/// <summary>Everything a month needs to draw itself.</summary>
	private sealed record Look(
		ILookup<DateOnly, Occurrence> ByDate, DateOnly Today, IReadOnlyList<EventDefinition> Types,
		Color TodayColor, Color SeveralColor, Color Text);

	private sealed class Drawn(Action<ICanvas, RectF> draw) : IDrawable
	{
		public void Draw(ICanvas canvas, RectF dirtyRect) => draw(canvas, dirtyRect);
	}

	/// <summary>One month: the season-colored frame, its name, weekdays (Monday first) and the days with their markers.</summary>
	private sealed class MonthDrawable(int year, int month, Look look) : IDrawable
	{
		private static readonly string[] Weekdays = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

		public void Draw(ICanvas canvas, RectF rect)
		{
			var frame = rect.Inflate(-1.5f, -1.5f);
			canvas.StrokeColor = SeasonColor(month);
			canvas.StrokeSize = 2;
			canvas.DrawRoundedRectangle(frame, 6);

			var inner = frame.Inflate(-4, -4);
			float titleHeight = inner.Height * 0.13f, headHeight = inner.Height * 0.1f;
			float rowHeight = (inner.Height - titleHeight - headHeight) / 6, cellWidth = inner.Width / 7;

			canvas.FontColor = look.Text;
			canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
			canvas.FontSize = titleHeight * 0.65f;
			canvas.DrawString(Months.Name(month), inner.X + 4, inner.Y, inner.Width, titleHeight, HorizontalAlignment.Left, VerticalAlignment.Center);

			canvas.Font = Microsoft.Maui.Graphics.Font.Default;
			canvas.FontSize = headHeight * 0.6f;
			for (int i = 0; i < 7; i++)
				canvas.DrawString(Weekdays[i], inner.X + i * cellWidth, inner.Y + titleHeight, cellWidth, headHeight, HorizontalAlignment.Center, VerticalAlignment.Center);

			var first = new DateOnly(year, month, 1);
			int offset = ((int)first.DayOfWeek + 6) % 7; // Monday first
			float size = Math.Min(cellWidth, rowHeight) * 0.9f;
			for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
			{
				int slot = offset + day - 1;
				var cell = new RectF(inner.X + slot % 7 * cellWidth, inner.Y + titleHeight + headHeight + slot / 7 * rowHeight, cellWidth, rowHeight);
				var box = new RectF(cell.Center.X - size / 2, cell.Center.Y - size / 2, size, size);
				Day(canvas, box, cell, new DateOnly(year, month, day));
			}
		}

		/// <summary>As the PC's CalendarView.Day: a plain number, today's filled circle, one event's figure with its letter,
		/// or several events' circle with their count; today rings any marker.</summary>
		private void Day(ICanvas canvas, RectF box, RectF cell, DateOnly date)
		{
			var items = look.ByDate[date].ToList();
			string number = date.Day.ToString(CultureInfo.InvariantCulture);
			bool isToday = date == look.Today;
			if (items.Count == 0)
			{
				if (isToday)
					Marker.Draw(canvas, box, look.TodayColor, number, MarkerShape.Circle, null);
				else
				{
					canvas.FontColor = look.Text;
					canvas.Font = Microsoft.Maui.Graphics.Font.Default;
					canvas.FontSize = box.Height * 0.55f;
					canvas.DrawString(number, cell, HorizontalAlignment.Center, VerticalAlignment.Center);
				}
				return;
			}
			Color? ring = isToday ? look.TodayColor : null;
			if (items.Count > 1)
				Marker.Draw(canvas, box, look.SeveralColor, items.Count.ToString(CultureInfo.InvariantCulture), MarkerShape.Circle, ring);
			else
			{
				var entry = items[0].Entry;
				var type = EventCatalog.Find(look.Types, entry.EventType);
				var color = entry.Color is { } own && Color.TryParse(own, out var c) ? c : Color.FromArgb(type.Color);
				Marker.Draw(canvas, box, color, Letter(entry), type.Shape, ring);
			}
		}

		/// <summary>The first whole character of the name, an emoji included.</summary>
		private static string Letter(Entry entry) =>
			StringInfo.GetNextTextElement(entry.Name.TrimStart()).ToUpper(CultureInfo.GetCultureInfo("ru-RU"));

		private static Color SeasonColor(int month) => month switch
		{
			12 or 1 or 2 => Color.FromArgb("#6EC6F5"),
			3 or 4 or 5 => Color.FromArgb("#F0567A"),
			6 or 7 or 8 => Color.FromArgb("#43A047"),
			_ => Color.FromArgb("#F28C28"),
		};
	}
}

/// <summary>Month names in the nominative, as the editor shows them.</summary>
internal static class Months
{
	public static string Name(int month) => EditorPage.Months[month - 1];
}

/// <summary>The PC's rounded marker figures (CalendarView.Figures), drawn with Microsoft.Maui.Graphics.</summary>
internal static class Marker
{
	/// <summary>Clockwise corners in a 0..1 box and the rounding, as a part of the box — the PC's values.</summary>
	private static (PointF[] Corners, float Radius) Polygon(MarkerShape shape) => shape switch
	{
		MarkerShape.Diamond => ([new(.5f, 0), new(.9f, .5f), new(.5f, 1), new(.1f, .5f)], .1f),
		MarkerShape.Square => ([new(.05f, .05f), new(.95f, .05f), new(.95f, .95f), new(.05f, .95f)], .22f),
		MarkerShape.Hexagon => ([new(.27f, .05f), new(.73f, .05f), new(1, .5f), new(.73f, .95f), new(.27f, .95f), new(0, .5f)], .12f),
		MarkerShape.Pentagon => ([new(.5f, 0), new(1, .38f), new(.81f, 1), new(.19f, 1), new(0, .38f)], .14f),
		_ => ([new(.5f, 0), new(1.04f, .95f), new(-.04f, .95f)], .13f), // Triangle: a little wider than the box, or a letter won't fit
	};

	public static void Draw(ICanvas canvas, RectF box, Color fill, string text, MarkerShape shape, Color? ring)
	{
		var path = Figure(shape, box);
		canvas.FillColor = fill;
		canvas.FillPath(path);
		if (ring is not null)
		{
			canvas.StrokeColor = ring;
			canvas.StrokeSize = 2;
			canvas.DrawPath(path);
		}
		canvas.FontColor = IsLight(fill) ? Colors.Black : Colors.White;
		canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
		canvas.FontSize = box.Height * 0.5f;
		// The figures narrow upward, so a letter sits below their middle (the PC's shifts at its 23 px).
		float shift = box.Height / 23 * shape switch { MarkerShape.Triangle => 4, MarkerShape.Drop => 5, MarkerShape.Pentagon => 1, _ => 0 };
		canvas.DrawString(text, new RectF(box.X, box.Y + shift, box.Width, box.Height), HorizontalAlignment.Center, VerticalAlignment.Center);
	}

	private static PathF Figure(MarkerShape shape, RectF box)
	{
		var path = new PathF();
		PointF At(float x, float y) => new(box.X + x * box.Width, box.Y + y * box.Height);
		switch (shape)
		{
			case MarkerShape.Circle:
				path.AppendEllipse(box);
				return path;
			case MarkerShape.Drop:
				path.MoveTo(At(.5f, 0));
				path.CurveTo(At(.7f, .28f), At(.9f, .46f), At(.9f, .62f));
				path.CurveTo(At(.9f, .85f), At(.72f, 1), At(.5f, 1));
				path.CurveTo(At(.28f, 1), At(.1f, .85f), At(.1f, .62f));
				path.CurveTo(At(.1f, .46f), At(.3f, .28f), At(.5f, 0));
				path.Close();
				return path;
		}
		// Each corner cut by a curve through it, from a point on one side to a point on the next.
		var (corners, radius) = Polygon(shape);
		var points = corners.Select(c => At(c.X, c.Y)).ToArray();
		float cut = radius * box.Width;
		for (int i = 0; i < points.Length; i++)
		{
			var corner = points[i];
			var before = Toward(corner, points[(i + points.Length - 1) % points.Length], cut);
			var after = Toward(corner, points[(i + 1) % points.Length], cut);
			if (i == 0)
				path.MoveTo(before);
			else
				path.LineTo(before);
			path.QuadTo(corner, after);
		}
		path.Close();
		return path;
	}

	private static PointF Toward(PointF from, PointF to, float distance)
	{
		float dx = to.X - from.X, dy = to.Y - from.Y, length = MathF.Sqrt(dx * dx + dy * dy);
		return length == 0 ? from : new PointF(from.X + dx / length * distance, from.Y + dy / length * distance);
	}

	private static bool IsLight(Color c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue > 150 / 255.0;
}
