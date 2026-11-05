using System.Globalization;
using BirthdayChecker.Core;
using Entry = BirthdayChecker.Core.Entry; // MAUI has its own Entry (a text box)

namespace BirthdayChecker.Mobile;

/// <summary>
/// Add / edit / delete one entry, like the PC's EditorWindow. Color and the user's columns aren't shown
/// and are kept as they are. Writes nothing itself: <c>save</c> and <c>delete</c>
/// change the list, and return false (after telling the user) when the file wasn't written.
/// </summary>
public partial class EditorPage : ContentPage
{
	internal static readonly string[] Months =
		["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];

	// Monday first, as on the PC; mapped to DayOfWeek by index.
	private static readonly string[] WeekdayNames = ["понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье"];
	private static readonly string[] NthNames = ["1-й", "2-й", "3-й", "4-й", "последний"];
	private static readonly string[] Kinds = ["Число", "День недели", "От Пасхи"];

	private static readonly string[] ReminderLabels =
	[
		"за 1 день", "за 2 дня", "за 3 дня", "за 4 дня", "за 5 дней",
		"за 1 неделю", "за 2 недели", "за 3 недели", "за 4 недели", "за 5 недель",
		"за 1 месяц", "за 2 месяца", "за 3 месяца",
	];

	/// <summary>As on the PC (the legacy app's default): 1, 2, 3, 5 days, 1 week, 1 month.</summary>
	private const Reminder DefaultReminders =
		Reminder.Day1 | Reminder.Day2 | Reminder.Day3 | Reminder.Day5 | Reminder.Week1 | Reminder.Month1;

	private readonly Entry? _original;
	private readonly Func<Entry, Task<bool>> _save;
	private readonly Func<Task<bool>>? _delete;
	private readonly CheckBox[] _checks = new CheckBox[ReminderLabels.Length];
	private readonly List<(string Name, CheckBox Box)> _tags = [];
	private bool _busy; // a second tap while saving would add the entry twice
	private readonly string _shown; // the form as opened: leaving with anything else asks first

	/// <param name="tags">The known tags in catalog order; the entry's own are among them.</param>
	public EditorPage(Entry? entry, IReadOnlyList<string> eventTypes, IReadOnlyList<string> tags,
		Func<Entry, Task<bool>> save, Func<Task<bool>>? delete)
	{
		InitializeComponent();
		_original = entry;
		_save = save;
		_delete = delete;
		Title = entry is null ? "Новая запись" : "Изменить запись";
		DeleteButton.IsVisible = delete is not null;

		TypeBox.ItemsSource = eventTypes.ToList();
		KindBox.ItemsSource = Kinds;
		DayBox.ItemsSource = Enumerable.Range(1, 31).Select(d => d.ToString(CultureInfo.InvariantCulture)).ToList();
		MonthBox.ItemsSource = Months;
		NthMonthBox.ItemsSource = Months;
		NthBox.ItemsSource = NthNames;
		WeekdayBox.ItemsSource = WeekdayNames;
		for (int i = 0; i < _checks.Length; i++)
		{
			_checks[i] = new CheckBox();
			var row = new HorizontalStackLayout { _checks[i], new Label { Text = ReminderLabels[i], VerticalOptions = LayoutOptions.Center } };
			RemindersGrid.Add(row, column: i / 5, row: i < 10 ? i % 5 : i - 10);
		}
		foreach (var tag in tags)
		{
			// The entry's own spelling, so saving unchanged doesn't rewrite "семья" as the catalog's "Семья".
			var own = entry?.Tags.FirstOrDefault(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase));
			AddTag(own ?? tag, own is not null);
		}
		Fill(entry, eventTypes);
		_shown = Form();
		// The bar's arrow; the phone's back button and gesture come to OnBackButtonPressed.
		Shell.SetBackButtonBehavior(this, new BackButtonBehavior { Command = new Command(async () => await Leave()) });
	}

	/// <summary>Everything the user can change, as one string to compare.</summary>
	private string Form() => string.Join("\n",
		NameBox.Text, TypeBox.SelectedIndex, KindBox.SelectedIndex, DayBox.SelectedIndex, MonthBox.SelectedIndex, YearBox.Text,
		NthBox.SelectedIndex, WeekdayBox.SelectedIndex, NthMonthBox.SelectedIndex, OffsetBox.Text, PhoneBox.Text, CommentBox.Text,
		string.Concat(_checks.Select(c => c.IsChecked ? '1' : '0')),
		string.Join(",", _tags.Select(t => t.Box.IsChecked ? t.Name : "-" + t.Name)));

	protected override bool OnBackButtonPressed()
	{
		if (Form() == _shown)
			return base.OnBackButtonPressed();
		Dispatcher.Dispatch(async () => await Leave());
		return true;
	}

	/// <summary>Back with unsaved changes asks, instead of losing them silently (BDC-55).</summary>
	private async Task Leave()
	{
		if (_busy)
			return;
		if (Form() == _shown)
		{
			await Navigation.PopAsync();
			return;
		}
		switch (await DisplayActionSheetAsync("Сохранить изменения?", "Отмена", null, "Сохранить", "Не сохранять"))
		{
			case "Сохранить":
				await Save();
				break;
			case "Не сохранять":
				await Navigation.PopAsync();
				break;
		}
	}

	private void AddTag(string name, bool on)
	{
		var box = new CheckBox { IsChecked = on };
		var label = new Label { Text = name, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 12, 0) };
		// A tap on the name ticks too, not only the small box.
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => box.IsChecked = !box.IsChecked;
		label.GestureRecognizers.Add(tap);
		TagsPanel.Add(new HorizontalStackLayout { box, label });
		_tags.Add((name, box));
	}

	/// <summary>Adds a tag to this entry, ticked; it joins the shared list once an entry carries it.</summary>
	private async void OnNewTag(object? sender, EventArgs e)
	{
		var name = (await DisplayPromptAsync("Новый тег", "Название тега", "Добавить", "Отмена"))?.Trim();
		if (string.IsNullOrEmpty(name))
			return;
		var known = _tags.Select(t => new TagDefinition(t.Name)).ToList();
		if (TagCatalog.NameProblem(name, known) is { } problem)
		{
			// The same name in any case: just tick the existing one.
			if (_tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase)) is { Box: { } box })
				box.IsChecked = true;
			else
				await Error(problem);
			return;
		}
		AddTag(name, on: true);
	}

	private void Fill(Entry? e, IReadOnlyList<string> eventTypes)
	{
		var today = DateTime.Today;
		NameBox.Text = e?.Name ?? "";
		TypeBox.SelectedIndex = e is null ? 0 : eventTypes.ToList().FindIndex(t => string.Equals(t, e.EventType, StringComparison.CurrentCultureIgnoreCase));
		PhoneBox.Text = e?.Phone ?? "";
		CommentBox.Text = e?.Comment ?? "";

		// Sensible values in every panel, so switching the kind never shows empty boxes.
		DayBox.SelectedIndex = today.Day - 1;
		MonthBox.SelectedIndex = NthMonthBox.SelectedIndex = today.Month - 1;
		NthBox.SelectedIndex = 1;
		WeekdayBox.SelectedIndex = 6;
		OffsetBox.Text = "0";

		switch (e?.Date)
		{
			case NthWeekdayDate n:
				KindBox.SelectedIndex = 1;
				NthBox.SelectedIndex = n.Nth == NthWeekdayDate.Last ? 4 : n.Nth - 1;
				WeekdayBox.SelectedIndex = ((int)n.Weekday + 6) % 7;
				NthMonthBox.SelectedIndex = n.Month - 1;
				break;
			case EasterDate x:
				KindBox.SelectedIndex = 2;
				OffsetBox.Text = x.OffsetDays.ToString(CultureInfo.InvariantCulture);
				break;
			case FixedDate f:
				KindBox.SelectedIndex = 0;
				DayBox.SelectedIndex = f.Day - 1;
				MonthBox.SelectedIndex = f.Month - 1;
				YearBox.Text = f.Year?.ToString(CultureInfo.InvariantCulture) ?? "";
				break;
			default:
				KindBox.SelectedIndex = 0;
				break;
		}

		var reminders = e?.Reminders ?? DefaultReminders;
		for (int i = 0; i < _checks.Length; i++)
			_checks[i].IsChecked = reminders.HasFlag((Reminder)(1 << i));
	}

	private void OnKindChanged(object? sender, EventArgs e)
	{
		FixedPanel.IsVisible = KindBox.SelectedIndex == 0;
		NthPanel.IsVisible = KindBox.SelectedIndex == 1;
		EasterPanel.IsVisible = KindBox.SelectedIndex == 2;
	}

	private void OnToggleAll(object? sender, EventArgs e)
	{
		bool on = !_checks.All(c => c.IsChecked);
		foreach (var c in _checks)
			c.IsChecked = on;
	}

	private async void OnSave(object? sender, EventArgs e) => await Save();

	private async Task Save()
	{
		var name = (NameBox.Text ?? "").Trim();
		if (name.Length == 0) { await Error("Укажите имя."); return; }
		if (TypeBox.SelectedItem is not string type) { await Error("Укажите событие."); return; }
		if (await ReadDate() is not { } date) return;

		var reminders = Reminder.None;
		for (int i = 0; i < _checks.Length; i++)
			if (_checks[i].IsChecked)
				reminders |= (Reminder)(1 << i);

		// "with" on the original keeps what this page doesn't show: Id, tags, color, the user's columns.
		var result = (_original ?? new Entry(name, type, date)) with
		{
			Name = name,
			EventType = type,
			Date = date,
			Phone = NullIfBlank(PhoneBox.Text),
			Comment = NullIfBlank(CommentBox.Text),
			Reminders = reminders,
			Tags = [.. _tags.Where(t => t.Box.IsChecked).Select(t => t.Name)],
		};
		await Finish(() => _save(result));
	}

	private async void OnDelete(object? sender, EventArgs e)
	{
		if (_delete is null || _original is null)
			return;
		if (await DisplayAlertAsync("Удалить запись", $"Удалить «{_original.Name}» — {_original.EventType}?", "Удалить", "Отмена"))
			await Finish(_delete);
	}

	/// <summary>Writes once and closes the page; stays open when the write failed (the caller has said why).</summary>
	private async Task Finish(Func<Task<bool>> write)
	{
		if (_busy)
			return;
		_busy = true;
		try
		{
			if (await write())
				await Navigation.PopAsync();
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task<EventDate?> ReadDate()
	{
		try
		{
			if (KindBox.SelectedIndex == 1)
				return new NthWeekdayDate(NthMonthBox.SelectedIndex + 1, (DayOfWeek)((WeekdayBox.SelectedIndex + 1) % 7),
					NthBox.SelectedIndex == 4 ? NthWeekdayDate.Last : NthBox.SelectedIndex + 1);

			if (KindBox.SelectedIndex == 2)
			{
				if (!int.TryParse((OffsetBox.Text ?? "").Trim().Replace('−', '-'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int offset))
				{
					await Error("Смещение от Пасхи — целое число дней, например 49 или -48.");
					return null;
				}
				return new EasterDate(offset);
			}

			int? year = null;
			if ((YearBox.Text ?? "").Trim() is { Length: > 0 } y)
			{
				if (!int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed is < 1800 or > 9999)
				{
					await Error("Год — число от 1800 до 9999 или пусто, если неизвестен.");
					return null;
				}
				year = parsed;
			}
			return new FixedDate(MonthBox.SelectedIndex + 1, DayBox.SelectedIndex + 1, year);
		}
		catch (ArgumentOutOfRangeException)
		{
			await Error(KindBox.SelectedIndex == 2
				? $"Смещение от Пасхи — не больше {EasterDate.MaxOffset} дней в любую сторону."
				: "Такой даты не бывает (например, 31 апреля или 29 февраля невисокосного года).");
			return null;
		}
	}

	private Task Error(string message) => DisplayAlertAsync(Title, message, "OK");

	private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
