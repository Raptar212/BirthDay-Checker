using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// "Настройки": the daily notification (time, repeat, columns), the theme, the calendar's "today" and "several"
/// colors, and the way to the tag and event type lists. Every change is saved at once, as on the PC.
/// </summary>
public sealed class SettingsPage : ContentPage
{
	private static readonly string[] Themes = ["Как в телефоне", "Светлая", "Тёмная"];

	private readonly MainPage _owner;
	private bool _picking;

	public SettingsPage(MainPage owner)
	{
		_owner = owner;
		Title = "Настройки";
		var root = new VerticalStackLayout { Padding = 16, Spacing = 8 };

		root.Add(Header("Напоминание"));
		var time = new TimePicker { Format = "HH:mm", Time = Reminders.Time.ToTimeSpan() };
		var interval = new Picker
		{
			Title = "Повтор",
			ItemsSource = DailyReminder.Intervals.Select(IntervalText).ToList(),
			SelectedIndex = Array.IndexOf(DailyReminder.Intervals, Reminders.Interval),
		};
		// After the initial values, so setting them doesn't count as a change.
		time.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(TimePicker.Time) && time.Time is { } t)
				Reschedule(() => Reminders.Time = TimeOnly.FromTimeSpan(t));
		};
		interval.SelectedIndexChanged += (_, _) =>
		{
			if (interval.SelectedIndex >= 0)
				Reschedule(() => Reminders.Interval = DailyReminder.Intervals[interval.SelectedIndex]);
		};
		root.Add(new HorizontalStackLayout { Spacing = 8, Children = { Text("Напоминать в"), time, interval } });
		root.Add(new Label { Text = "Повтор идёт от этого времени до полуночи.", FontSize = 12 });
		if (OperatingSystem.IsAndroidVersionAtLeast(26)) // before Android 8 there are no channels: the phone's default sound
		{
			var sound = new Button { Text = "Звук уведомления…" };
			sound.Clicked += (_, _) => Reminders.OpenSoundSettings(Platform.AppContext);
			root.Add(sound);
			root.Add(new Label { Text = "Мелодия, вибрация, «Без звука». Свой файл: положите mp3 в папку Notifications телефона — он появится в списке мелодий.", FontSize = 12 });
		}

		root.Add(Header("В уведомлении"));
		root.Add(new Label { Text = "Имя и дата есть всегда.", FontSize = 12 });
		root.Add(Columns());

		root.Add(Header("Тема"));
		var theme = new Picker { ItemsSource = Themes, SelectedIndex = Reminders.Theme };
		theme.SelectedIndexChanged += (_, _) =>
		{
			Reminders.Theme = theme.SelectedIndex;
			App.ApplyTheme();
		};
		root.Add(theme);

		root.Add(Header("Календарь"));
		root.Add(ColorRow("Сегодня", () => _owner.Settings.TodayColor, c => s => s with { TodayColor = c }));
		root.Add(ColorRow("Несколько событий в день", () => _owner.Settings.SeveralColor, c => s => s with { SeveralColor = c }));

		root.Add(Header("Обновления"));
		var updates = new CheckBox { IsChecked = Updates.Enabled };
		updates.CheckedChanged += (_, e) => Updates.Enabled = e.Value;
		root.Add(new HorizontalStackLayout { updates, Text("Проверять при открытии (раз в день)") });
		if (SelfUpdate.SilentSupported) // Android 8–11 always ask before installing
		{
			var silent = new CheckBox { IsChecked = Updates.Silent };
			silent.CheckedChanged += (_, e) =>
			{
				Updates.Silent = e.Value;
				if (e.Value && !SelfUpdate.CanInstall)
					SelfUpdate.AskPermission();
			};
			root.Add(new HorizontalStackLayout { silent, Text("Устанавливать без вопросов (по Wi-Fi)") });
		}
		root.Add(new Label { Text = $"Версия {AppInfo.Current.VersionString}. Вручную — ⋮ → «Проверить обновления».", FontSize = 12 });

		root.Add(Header("Списки"));
		var tags = new Button { Text = "Теги…" };
		tags.Clicked += async (_, _) => await Navigation.PushAsync(new TagsPage(_owner));
		var events = new Button { Text = "События…" };
		events.Clicked += async (_, _) => await Navigation.PushAsync(new EventsPage(_owner));
		var buttons = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 16 };
		buttons.Add(tags, 0);
		buttons.Add(events, 1);
		root.Add(buttons);

		Content = new ScrollView { Content = root };
	}

	internal static string IntervalText(TimeSpan i) =>
		i == TimeSpan.Zero ? "раз в день" : i.TotalHours == 1 ? "каждый час" : $"каждые {i.TotalHours} ч";

	/// <summary>A new time or repeat books the next check at once (right away if the new time has passed today).</summary>
	private static void Reschedule(Action save)
	{
		save();
		Reminders.Schedule(Platform.AppContext);
	}

	/// <summary>A tick per built-in column but the name and date, in the table's order, as on the PC.</summary>
	private VerticalStackLayout Columns()
	{
		var shown = _owner.Settings.ReminderColumns ?? Reminders.DefaultColumns;
		var choices = ColumnCatalog.BuiltIn.Where(c => !ColumnCatalog.IsLocked(c)).ToList();
		var boxes = new List<CheckBox>();
		var list = new VerticalStackLayout();
		foreach (var column in choices)
		{
			var box = new CheckBox { IsChecked = shown.Contains(column, StringComparer.CurrentCultureIgnoreCase) };
			box.CheckedChanged += async (_, _) =>
			{
				var ticked = choices.Where((_, i) => boxes[i].IsChecked).ToList();
				await _owner.ChangeSettings(s => s with { ReminderColumns = ticked });
			};
			boxes.Add(box);
			list.Add(new HorizontalStackLayout { box, Text(column == "Через, дн." ? "Через сколько дней" : column) });
		}
		return list;
	}

	/// <summary>A swatch and a title; a tap opens the palette (a calendar color can't be "none").</summary>
	private HorizontalStackLayout ColorRow(string title, Func<string> current, Func<string, Func<AppSettings, AppSettings>> change)
	{
		var swatch = Swatch(current());
		var row = new HorizontalStackLayout { Spacing = 12, Padding = new Thickness(0, 4), Children = { swatch, Text(title) } };
		var tap = new TapGestureRecognizer();
		tap.Tapped += async (_, _) =>
		{
			if (_picking)
				return; // a double tap would push two palettes
			_picking = true;
			try
			{
				var (chosen, color) = await PalettePage.PickAsync(Navigation, title, current(), allowNone: false);
				if (chosen && color is not null && await _owner.ChangeSettings(change(color)))
					swatch.BackgroundColor = Color.FromArgb(color);
			}
			finally
			{
				_picking = false;
			}
		};
		row.GestureRecognizers.Add(tap);
		return row;
	}

	internal static Border Swatch(string? color) => new()
	{
		WidthRequest = 32,
		HeightRequest = 32,
		BackgroundColor = color is null ? Colors.Transparent : Color.FromArgb(color),
		Stroke = Colors.Gray,
		StrokeThickness = 1,
		StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
		VerticalOptions = LayoutOptions.Center,
	};

	private static Label Header(string text) => new() { Text = text, FontAttributes = FontAttributes.Bold, FontSize = 16, Margin = new Thickness(0, 12, 0, 0) };

	internal static Label Text(string text) => new() { Text = text, VerticalOptions = LayoutOptions.Center };
}
