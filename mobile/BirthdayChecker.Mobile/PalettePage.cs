namespace BirthdayChecker.Mobile;

/// <summary>
/// The phone's color choice: the PC's 8 quick colors and the 48 basic ones, plus "Без цвета" where a color is optional.
/// No spectrum or HEX (BDC-39); returns the chosen "#RRGGBB" through <see cref="PickAsync"/>.
/// </summary>
public sealed class PalettePage : ContentPage
{
	/// <summary>The PC's quick palette (ColorPick.PaletteColors).</summary>
	private static readonly string[] Quick = ["#FF8080", "#FFB366", "#FFE680", "#A8E6A1", "#80D4FF", "#A6B8FF", "#D9A6FF", "#C8C8C8"];

	/// <summary>The 48 basic colors of the PC's ColorWindow, by rows of 12.</summary>
	private static readonly string[] Basic =
	[
		"#FF8080", "#FF0000", "#804040", "#800000", "#400000", "#80FFFF", "#00FFFF", "#0080C0", "#0000FF", "#0000A0", "#004080", "#000080",
		"#FFFF80", "#FFFF00", "#FF8040", "#FF8000", "#804000", "#808000", "#8080FF", "#8000FF", "#0080FF", "#000040", "#800080", "#400040",
		"#80FF80", "#80FF00", "#00FF00", "#00FF40", "#00FF80", "#808040", "#FF80C0", "#FF80FF", "#FF00FF", "#FF0080", "#8080C0", "#800040",
		"#008000", "#008040", "#008080", "#408080", "#004000", "#004040", "#400080", "#000000", "#808080", "#C0C0C0", "#E0E0E0", "#FFFFFF",
	];

	// Asynchronous continuations: the caller (a save, maybe an alert) runs after the pop, not inside it.
	private readonly TaskCompletionSource<(bool Chosen, string? Color)> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private PalettePage(string title, string? current, bool allowNone)
	{
		Title = title;
		var root = new VerticalStackLayout { Padding = 16, Spacing = 12 };
		root.Add(new Label { Text = "Быстрые цвета" });
		root.Add(Grid(Quick, 8, current));
		root.Add(new Label { Text = "Базовые цвета" });
		root.Add(Grid(Basic, 6, current));
		if (allowNone)
		{
			var none = new Button { Text = "Без цвета" };
			none.Clicked += (_, _) => Done(true, null);
			root.Add(none);
		}
		Content = new ScrollView { Content = root };
	}

	/// <summary>Opens the palette; Chosen is false when the user went back without a choice.</summary>
	public static async Task<(bool Chosen, string? Color)> PickAsync(INavigation navigation, string title, string? current, bool allowNone)
	{
		var page = new PalettePage(title, current, allowNone);
		await navigation.PushAsync(page);
		return await page._result.Task;
	}

	// Back button: no choice. Not when the whole app only went to the background — the page is still open then.
	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		if (!Navigation.NavigationStack.Contains(this))
			_result.TrySetResult((false, null));
	}

	private Grid Grid(string[] colors, int columns, string? current)
	{
		var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
		for (int c = 0; c < columns; c++)
			grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		for (int i = 0; i < colors.Length; i++)
		{
			if (i % columns == 0)
				grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			var color = colors[i];
			bool on = string.Equals(color, current, StringComparison.OrdinalIgnoreCase);
			var swatch = new Border
			{
				HeightRequest = 40,
				BackgroundColor = Color.FromArgb(color),
				StrokeThickness = on ? 3 : 1,
				Stroke = on ? Colors.DodgerBlue : Colors.Gray,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
			};
			var tap = new TapGestureRecognizer();
			tap.Tapped += (_, _) => Done(true, color);
			swatch.GestureRecognizers.Add(tap);
			grid.Add(swatch, i % columns, i / columns);
		}
		return grid;
	}

	private async void Done(bool chosen, string? color)
	{
		if (_result.TrySetResult((chosen, color))) // a second tap must not pop the page under this one
			await Navigation.PopAsync();
	}
}
