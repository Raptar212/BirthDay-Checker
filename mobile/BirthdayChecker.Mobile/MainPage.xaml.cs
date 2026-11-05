using BirthdayChecker.Core;
using Entry = BirthdayChecker.Core.Entry; // MAUI has its own Entry (a text box)

namespace BirthdayChecker.Mobile;

/// <summary>
/// One list entry: the name and when on the first line, date, event, age and tags on the second.
/// The row's color fills it in the light theme and is a stripe on the left in the dark one; no color = the theme's.
/// </summary>
public sealed record Row(Guid Id, string Name, string When, string Details, Color? Back, Color? Stripe)
{
	/// <summary>A colored row is light in any theme, so its text is black, as on the PC.</summary>
	public bool Colored => Back is not null;
}

/// <summary>
/// The list, nearest first. entries.json lives in the app's private folder; "Открыть файл" replaces it
/// with a file the user picks (checked by the same strict Load as on the PC, then saved atomically),
/// "Добавить из файла" adds the picked file's new entries, "Поделиться" sends entries.json to the PC.
/// A tap opens the entry in <see cref="EditorPage"/>, "Добавить" a new one. This page is the only writer of
/// entries.json and of the phone's own settings.json (tags, event types, notification columns, calendar colors):
/// <see cref="SettingsPage"/>, <see cref="TagsPage"/> and <see cref="EventsPage"/> change them through it.
/// </summary>
public partial class MainPage : ContentPage
{
	private static readonly string EntriesPath = AppLayout.EntriesPath(FileSystem.AppDataDirectory);

	/// <summary>The list's second line is fixed (days left are on the right): the notification's columns are a setting, the list's are not.</summary>
	private static readonly string[] ListColumns = ["Событие", "Возраст", "Теги"];

	// The PC's highlight of the nearest events, stronger than the entry's own color.
	private static readonly Color TodayBack = Color.FromArgb("#FFD9D9");
	private static readonly Color SoonBack = Color.FromArgb("#FFF4CC");

	private IReadOnlyList<Entry> _entries = [];
	private string? _loadError; // EntryStorage contract: no Save after a failed Load
	private ListFilter _filter = new();

	/// <summary>The month filter is also the calendar's month in focus, wherever it was set (BDC-38 on the PC).</summary>
	internal ListFilter Filter
	{
		get => _filter;
		set => _filter = value; // the list is rebuilt in OnAppearing when the calendar closes
	}

	/// <summary>The year the calendar shows; a month filter means that month of this year.</summary>
	internal int CalendarYear { get; set; } = DateTime.Today.Year;
	private AppSettings _settings = AppSettings.Load(Reminders.SettingsPath); // never throws: a bad file gives defaults

	public MainPage()
	{
		InitializeComponent();
		// The phone's own theme switch while the list is open: fill or stripe again.
		Application.Current!.RequestedThemeChanged += (_, _) => Dispatcher.Dispatch(Refresh);
		try
		{
			_entries = EntryStorage.Load(EntriesPath);
		}
		catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
		{
			_loadError = Explain(e, EntriesPath, "entries.json");
			EmptyText.Text = $"Список не открыт. {_loadError}";
		}
	}

	internal IReadOnlyList<Entry> Entries => _entries;
	internal AppSettings Settings => _settings;

	/// <summary>The saved tag list (defaults until first changed), then tags only entries have.</summary>
	internal IReadOnlyList<TagDefinition> TagList => TagCatalog.Merge(_settings.Tags, _entries);

	/// <summary>The saved event types (the PC's five until first changed), then types only entries have.</summary>
	internal IReadOnlyList<EventDefinition> EventList => EventCatalog.Merge(_settings.EventTypes, _entries);

	/// <summary>
	/// Saves settings.json through a temp file and a move: the phone is the only holder of its tag and event lists,
	/// and a file cut by a killed app would load as defaults. On failure tells the user and keeps the old settings.
	/// </summary>
	internal async Task<bool> ChangeSettings(Func<AppSettings, AppSettings> change)
	{
		var next = change(_settings);
		var path = Reminders.SettingsPath;
		try
		{
			AppSettings.Save(path + ".tmp", next);
			File.Move(path + ".tmp", path, overwrite: true);
		}
		// Any exception: the callers are async void handlers, as for Commit.
		catch (Exception e)
		{
			await DisplayAlertAsync("Настройки не сохранены", e.Message, "OK");
			return false;
		}
		_settings = next;
		return true;
	}

	/// <summary>For a rename or removal of a tag or event type: the whole list, written once, only if it was read.</summary>
	internal async Task<bool> ChangeEntries(List<Entry> next) => await CanEdit() && await Commit(next);

	private async void OnSettings(object? sender, EventArgs e) => await Navigation.PushAsync(new SettingsPage(this));

	private async void OnCheckUpdates(object? sender, EventArgs e) => await Updates.CheckAsync(this, manual: true);

	// A swipe either way opens the calendar (BDC-49, BDC-51); only while this page is on top.
	private async void OnSwipe(bool right)
	{
		// Shell lists its root page as null in the stack, so "on top" = nothing pushed over it.
		if (Navigation.NavigationStack.Count <= 1 && Navigation.ModalStack.Count == 0)
		{
			MainActivity.HorizontalSwipe -= OnSwipe; // a double fling must not push two calendars before OnDisappearing
			await Navigation.PushAsync(new CalendarPage(this));
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		MainActivity.HorizontalSwipe -= OnSwipe;
	}

	/// <summary>Android 13+ asks for the notification permission; older ones grant it on install.</summary>
	private static async Task AskNotificationsAsync()
	{
		try
		{
			if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
				await Permissions.RequestAsync<Permissions.PostNotifications>();
		}
		catch (Exception) { } // ponytail: without the permission the list still works, the notification just isn't shown
	}

	// Recomputed on every return to the page, so "days left" doesn't go stale past midnight.
	protected override void OnAppearing()
	{
		base.OnAppearing();
		MainActivity.HorizontalSwipe -= OnSwipe; // once, however often the page appears
		MainActivity.HorizontalSwipe += OnSwipe;
		Refresh();
		// Each opening books the alarm again: a force-stop or a missing boot broadcast drops it.
		Reminders.Schedule(Platform.AppContext);
		_ = AskNotificationsAsync();
		_ = Updates.CheckAsync(this, manual: false); // once a day at most; catches its own errors
	}

	/// <summary>The list through the search and filters, nearest first; the filter button shows how many are on.</summary>
	private void Refresh()
	{
		var today = DateOnly.FromDateTime(DateTime.Now);
		var tags = TagList;
		bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
		// A month is that month of the calendar's year, as on the PC, so the list matches its markers.
		var occurrences = _filter.Month is not null
			? EntryList.InYear(_entries, CalendarYear, today, _filter)
			: EntryList.Filter(_entries, today, _filter);
		List.ItemsSource = occurrences
			.Select(o =>
			{
				var lines = ReminderText.Lines(o, ListColumns);
				// As on the PC: today, then the next 7 days, then the entry's own color, then its tag's.
				var back = o.DaysLeft == 0 ? TodayBack
					: o.DaysLeft is > 0 and <= 7 ? SoonBack // past days of a month in focus are not "soon"
					: TagCatalog.RowColor(o.Entry, tags) is { } hex && Color.TryParse(hex, out var c) ? c
					: null;
				return new Row(o.Entry.Id, lines[0], ReminderText.When(o.DaysLeft), string.Join(" · ", lines.Skip(1)),
					dark ? null : back, dark ? back : null);
			})
			.ToList();
		int on = new object?[] { _filter.Month, _filter.EventType, _filter.Tag }.Count(v => v is not null);
		FilterButton.Text = on == 0 ? "Фильтр" : $"Фильтр ({on})";
		EmptyOpenButton.IsVisible = _entries.Count == 0;
		if (_loadError is null)
			EmptyText.Text = _entries.Count == 0
				? "Список пуст. Нажмите «+», чтобы добавить запись, или перенесите entries.json с компьютера на телефон и откройте его."
				: "Ничего не найдено. Измените поиск или сбросьте фильтр.";
	}

	private void OnSearch(object? sender, TextChangedEventArgs e)
	{
		_filter = _filter with { Search = e.NewTextValue };
		Refresh();
	}

	private const string All = "все";

	/// <summary>Month, event and tag, as in the PC's "Фильтр" menu; "Сбросить" clears them but keeps the search.</summary>
	private async void OnFilter(object? sender, EventArgs e)
	{
		FilterButton.IsEnabled = false; // a double tap would queue a second menu with stale labels
		try
		{
			await ChooseFilter();
		}
		finally
		{
			FilterButton.IsEnabled = true;
		}
		Refresh();
	}

	private async Task ChooseFilter()
	{
		const string Reset = "Сбросить";
		string month = $"Месяц: {(_filter.Month is { } m ? EditorPage.Months[m - 1] : All)}";
		string type = $"Событие: {_filter.EventType ?? All}";
		string tag = $"Тег: {_filter.Tag ?? All}";
		var pick = await DisplayActionSheetAsync("Фильтр", "Отмена", null, month, type, tag, Reset);
		if (pick == month && await Choose("Месяц", EditorPage.Months) is { } mm)
		{
			_filter = _filter with { Month = mm == All ? null : Array.IndexOf(EditorPage.Months, mm) + 1 };
			CalendarYear = DateTime.Today.Year; // a month from the menu is this year's, not one left in the calendar
		}
		else if (pick == type && await Choose("Событие", EventTypes()) is { } t)
			_filter = _filter with { EventType = t == All ? null : t };
		else if (pick == tag && await Choose("Тег", Tags()) is { } g)
			_filter = _filter with { Tag = g == All ? null : g };
		else if (pick == Reset)
		{
			_filter = new ListFilter(Search: _filter.Search);
			CalendarYear = DateTime.Today.Year;
		}
	}

	/// <summary>One value or <see cref="All"/>; null when cancelled.</summary>
	private async Task<string?> Choose(string title, IReadOnlyList<string> values)
	{
		var pick = await DisplayActionSheetAsync(title, "Отмена", null, [All, .. values]);
		return pick is null || pick == "Отмена" ? null : pick;
	}

	private async void OnAdd(object? sender, EventArgs e)
	{
		if (await CanEdit())
			await Navigation.PushAsync(new EditorPage(null, EventTypes(), Tags(), entry => Commit([.. _entries, entry]), delete: null));
	}

	// A tap on the row itself, not the list's selection: a swipe that starts on a row must open the calendar, not the entry.
	private async void OnPick(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is not Row row)
			return;
		if (_entries.FirstOrDefault(x => x.Id == row.Id) is not { } entry || !await CanEdit())
			return;
		await Navigation.PushAsync(new EditorPage(entry, EventTypes(), Tags(),
			async changed => await Exists(entry.Id) && await Commit([.. _entries.Select(x => x.Id == entry.Id ? changed : x)]),
			async () => await Exists(entry.Id) && await Commit([.. _entries.Where(x => x.Id != entry.Id)])));
	}

	/// <summary>A double tap can open two editors of one entry; once one deletes it, the other must not report a save.</summary>
	private async Task<bool> Exists(Guid id)
	{
		if (_entries.Any(x => x.Id == id))
			return true;
		await DisplayAlertAsync("Запись удалена", "Эта запись уже удалена. Закройте редактор.", "OK");
		return false;
	}

	/// <summary>The PC's five standard types, then the ones only entries have; new types are added on the PC.</summary>
	private IReadOnlyList<string> EventTypes() => [.. EventList.Select(t => t.Name)];

	private IReadOnlyList<string> Tags() => [.. TagList.Select(t => t.Name)];

	private async Task<bool> CanEdit()
	{
		if (_loadError is null)
			return true;
		await DisplayAlertAsync("Список не открыт", $"{_loadError}{Environment.NewLine}Менять его нельзя, чтобы не потерять записи.", "OK");
		return false;
	}

	/// <summary>Saves the changed list; on failure tells the user and keeps the old one (the editor stays open).</summary>
	private async Task<bool> Commit(List<Entry> next)
	{
		try
		{
			EntryStorage.Save(EntriesPath, next);
		}
		// Any exception: the callers are async void handlers. Save is atomic, so a failure keeps the old file.
		catch (Exception e)
		{
			await DisplayAlertAsync("Не сохранено", e.Message, "OK");
			return false;
		}
		_entries = next;
		return true; // the list is rebuilt in OnAppearing when the editor closes
	}

	private async void OnOpenFile(object? sender, EventArgs e)
	{
		if (!await CanEdit() || await ReadPickedAsync() is not (var picked, var name))
			return;
		if (_entries.Count > 0 && !await DisplayAlertAsync("Заменить список",
				$"Заменить {Count(_entries.Count)} на {Count(picked.Count)} из файла «{name}»?", "Заменить", "Отмена"))
			return;
		if (await Commit([.. picked]))
			Refresh();
	}

	/// <summary>Adds the picked file's entries that are not in the list yet, ticked on <see cref="PickPage"/>; the file stays as it was.</summary>
	private async void OnAddFromFile(object? sender, EventArgs e)
	{
		if (!await CanEdit() || await ReadPickedAsync() is not (var picked, var name))
			return;
		var unique = EntryCsv.Unique(_entries, picked);
		if (unique.Count == 0)
		{
			await DisplayAlertAsync("Нечего добавить",
				$"Все записи из файла «{name}» ({picked.Count}) уже есть в списке (тот же Id или то же имя, событие и дата).", "OK");
			return;
		}
		await Navigation.PushAsync(new PickPage(unique, picked.Count - unique.Count, async selected =>
		{
			// Merged into the list as it is now: an entry added meanwhile in the editor is skipped, not doubled.
			var merged = EntryCsv.Merge(_entries, selected);
			if (!await Commit(merged.Entries))
				return false;
			await DisplayAlertAsync("Добавлено", $"Добавлено: {Count(merged.Added)}.", "OK");
			return true;
		}));
	}

	/// <summary>Sends a copy of entries.json through Android's share sheet: messenger, mail, cloud — to bring it to the PC.</summary>
	private async void OnShare(object? sender, EventArgs e)
	{
		if (!File.Exists(EntriesPath))
		{
			await DisplayAlertAsync("Нечего отправить", "Список пуст: добавьте запись или откройте файл.", "OK");
			return;
		}
		try
		{
			await Share.Default.RequestAsync(new ShareFileRequest("entries.json", new ShareFile(EntriesPath, "application/json")));
		}
		catch (Exception ex) // async void: an unhandled exception would close the app
		{
			await DisplayAlertAsync("Не отправлено", ex.Message, "OK");
		}
	}

	/// <summary>Lets the user pick a file and reads it with the same strict Load as the PC; null when cancelled or unreadable (the user is told).</summary>
	private async Task<(IReadOnlyList<Entry> Entries, string Name)?> ReadPickedAsync()
	{
		FileResult? file;
		try
		{
			file = await FilePicker.Default.PickAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Файл не выбран", ex.Message, "OK");
			return null;
		}
		if (file is null)
			return null;

		// The picked file may be a content stream with no path; Load needs one, so copy it to the cache first.
		var copy = Path.Combine(FileSystem.CacheDirectory, $"picked-{Guid.NewGuid():N}.json");
		try
		{
			await using (var source = await file.OpenReadAsync())
			await using (var target = File.Create(copy))
				await source.CopyToAsync(target);
			return (EntryStorage.Load(copy), file.FileName);
		}
		// Any exception: Android content streams throw Java exceptions (FileNotFound, Security) outside System.IO.
		// Safe — nothing has been saved yet.
		catch (Exception ex)
		{
			await DisplayAlertAsync("Файл не открыт", Explain(ex, copy, file.FileName), "OK");
			return null;
		}
		finally
		{
			try { File.Delete(copy); } catch (IOException) { } catch (UnauthorizedAccessException) { }
		}
	}

	/// <summary>
	/// A failed load in the user's words: the file's name instead of the app's private path, and no
	/// serializer English — "не файл записей или повреждён", with the entry number when it is known (BDC-55).
	/// </summary>
	internal static string Explain(Exception e, string path, string name)
	{
		if (e is InvalidDataException { InnerException: System.Text.Json.JsonException json })
		{
			var at = System.Text.RegularExpressions.Regex.Match(json.Path ?? "", @"\[(\d+)\]");
			return $"«{name}» — не файл записей BirthDay Checker или он повреждён"
				+ (at.Success ? $": запись №{int.Parse(at.Groups[1].Value) + 1}." : ".");
		}
		if (e is InvalidDataException { InnerException: not null })
			return $"«{name}» — не файл записей BirthDay Checker или он повреждён.";
		if (e is UnauthorizedAccessException)
			return $"Нет доступа к «{name}».";
		// Our own Russian messages (format, empty name, ...) and the rest: just hide the private folder.
		return e.Message.Replace(path, name).Replace(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar, "");
	}

	private static string Count(int n) => $"{n} {ReminderText.Plural(n, "запись", "записи", "записей")}";
}
