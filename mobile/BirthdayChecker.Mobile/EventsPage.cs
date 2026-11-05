using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// "События", as the PC's EventsWindow: add, rename (in the entries too), delete (its entries get another type,
/// chosen here; the only type with entries can't go) and the calendar marker: color and figure.
/// Entries are written first; the type list is saved only when they were.
/// </summary>
public sealed class EventsPage : ContentPage
{
	/// <summary>As the PC's Figures.Names, in <see cref="MarkerShape"/> order.</summary>
	internal static readonly string[] ShapeNames = ["Круг", "Ромб", "Квадрат", "Шестиугольник", "Пятиугольник", "Капля", "Треугольник"];

	private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

	private readonly MainPage _owner;
	private readonly VerticalStackLayout _list = new() { Padding = 16, Spacing = 4 };
	private bool _busy; // one dialog at a time: a double tap must not rename or delete twice

	public EventsPage(MainPage owner)
	{
		_owner = owner;
		Title = "События";
		ToolbarItems.Add(new ToolbarItem("Добавить", null, () => Run(Add)));
		Content = new ScrollView { Content = _list };
		Fill();
	}

	private void Fill()
	{
		_list.Clear();
		_list.Add(new Label { Text = "Тап по квадрату — цвет на календаре, по фигуре — её вид, по названию — переименовать или удалить.", FontSize = 12 });
		foreach (var type in _owner.EventList)
		{
			int count = _owner.Entries.Count(e => Ci.Equals(e.EventType, type.Name));
			var swatch = SettingsPage.Swatch(type.Color);
			Tap(swatch, () => Recolor(type));
			var name = SettingsPage.Text(count == 0 ? type.Name : $"{type.Name} ({count})");
			Tap(name, () => Edit(type.Name, count));
			var shape = new Button { Text = ShapeNames[(int)type.Shape], VerticalOptions = LayoutOptions.Center };
			shape.Clicked += (_, _) => Run(() => Reshape(type));

			var row = new Grid
			{
				ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
				ColumnSpacing = 12,
				Padding = new Thickness(0, 4),
			};
			row.Add(swatch, 0);
			row.Add(name, 1);
			row.Add(shape, 2);
			_list.Add(row);
		}
	}

	private void Tap(View view, Func<Task> action)
	{
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => Run(action);
		view.GestureRecognizers.Add(tap);
	}

	private async void Run(Func<Task> action)
	{
		if (_busy)
			return;
		_busy = true;
		try
		{
			await action();
			Fill();
		}
		finally
		{
			_busy = false;
		}
	}

	private Task<bool> SaveTypes(IReadOnlyList<EventDefinition> types) => _owner.ChangeSettings(s => s with { EventTypes = types });

	private async Task Add()
	{
		var name = (await DisplayPromptAsync("Новое событие", "Название события", "Добавить", "Отмена"))?.Trim();
		if (string.IsNullOrEmpty(name))
			return;
		var types = _owner.EventList;
		if (EventCatalog.NameProblem(name, types) is { } problem)
			await DisplayAlertAsync(Title, problem, "OK");
		else
			await SaveTypes([.. types, new EventDefinition(name)]);
	}

	private async Task Recolor(EventDefinition type)
	{
		var (chosen, color) = await PalettePage.PickAsync(Navigation, $"Цвет «{type.Name}»", type.Color, allowNone: false);
		if (chosen && color is not null)
			await SaveTypes(EventCatalog.Change(_owner.EventList, type with { Color = color }));
	}

	private async Task Reshape(EventDefinition type)
	{
		var pick = await DisplayActionSheetAsync($"Фигура «{type.Name}»", "Отмена", null, ShapeNames);
		int index = Array.IndexOf(ShapeNames, pick);
		if (index >= 0)
			await SaveTypes(EventCatalog.Change(_owner.EventList, type with { Shape = (MarkerShape)index }));
	}

	private async Task Edit(string name, int count)
	{
		const string Rename = "Переименовать", Delete = "Удалить";
		var pick = await DisplayActionSheetAsync(name, "Отмена", null, Rename, Delete);
		if (pick == Rename)
			await RenameType(name);
		else if (pick == Delete)
			await DeleteType(name, count);
	}

	private async Task RenameType(string old)
	{
		var name = (await DisplayPromptAsync("Переименовать событие", "Новое название", "Сохранить", "Отмена", initialValue: old))?.Trim();
		if (string.IsNullOrEmpty(name) || name == old)
			return;
		var types = _owner.EventList;
		if (EventCatalog.NameProblem(name, types, renaming: old) is { } problem)
		{
			await DisplayAlertAsync(Title, problem, "OK");
			return;
		}
		if (_owner.Entries.Any(e => Ci.Equals(e.EventType, old)) && !await _owner.ChangeEntries(EventCatalog.RenameIn(_owner.Entries, old, name)))
			return;
		await SaveTypes(EventCatalog.Rename(types, old, name));
	}

	/// <summary>An entry must have a type: those of the deleted one move to another, chosen here.</summary>
	private async Task DeleteType(string name, int count)
	{
		var types = _owner.EventList;
		if (count == 0)
		{
			if (!await DisplayAlertAsync("Удалить событие", $"Удалить событие «{name}»?", "Удалить", "Отмена"))
				return;
		}
		else
		{
			var others = EventCatalog.Remove(types, name).Select(t => t.Name).ToArray();
			if (others.Length == 0)
			{
				await DisplayAlertAsync(Title, $"Записей с событием «{name}»: {count}, а другого события для них нет. Сначала добавьте его.", "OK");
				return;
			}
			// Short: an action sheet cuts its title to two lines.
			var replacement = await DisplayActionSheetAsync($"Новое событие для записей ({count})", "Отмена", null, others);
			if (replacement is null || !others.Contains(replacement) || !await _owner.ChangeEntries(EventCatalog.RenameIn(_owner.Entries, name, replacement)))
				return;
		}
		await SaveTypes(EventCatalog.Remove(types, name));
	}
}
