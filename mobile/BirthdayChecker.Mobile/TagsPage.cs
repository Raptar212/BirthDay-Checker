using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// "Теги", as the PC's TagsWindow: add, rename (in the entries too), delete (from the entries too) and the color
/// of the rows that carry the tag. Entries are written first; the tag list is saved only when they were.
/// </summary>
public sealed class TagsPage : ContentPage
{
	private static readonly StringComparer Ci = StringComparer.CurrentCultureIgnoreCase;

	private readonly MainPage _owner;
	private readonly VerticalStackLayout _list = new() { Padding = 16, Spacing = 4 };
	private bool _busy; // one dialog at a time: a double tap must not rename or delete twice

	public TagsPage(MainPage owner)
	{
		_owner = owner;
		Title = "Теги";
		ToolbarItems.Add(new ToolbarItem("Добавить", null, () => Run(Add)));
		Content = new ScrollView { Content = _list };
		Fill();
	}

	private void Fill()
	{
		_list.Clear();
		_list.Add(new Label { Text = "Тап по квадрату — цвет строк с тегом, по названию — переименовать или удалить.", FontSize = 12 });
		foreach (var tag in _owner.TagList)
		{
			int count = _owner.Entries.Count(e => e.Tags.Contains(tag.Name, Ci));
			var swatch = SettingsPage.Swatch(tag.Color);
			var swatchTap = new TapGestureRecognizer();
			swatchTap.Tapped += (_, _) => Run(() => Recolor(tag));
			swatch.GestureRecognizers.Add(swatchTap);

			var name = SettingsPage.Text(count == 0 ? tag.Name : $"{tag.Name} ({count})");
			var nameTap = new TapGestureRecognizer();
			nameTap.Tapped += (_, _) => Run(() => Edit(tag.Name, count));
			name.GestureRecognizers.Add(nameTap);

			var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12, Padding = new Thickness(0, 6) };
			row.Add(swatch, 0);
			row.Add(name, 1);
			_list.Add(row);
		}
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

	private Task<bool> SaveTags(IReadOnlyList<TagDefinition> tags) => _owner.ChangeSettings(s => s with { Tags = tags });

	private async Task Add()
	{
		var name = (await DisplayPromptAsync("Новый тег", "Название тега", "Добавить", "Отмена"))?.Trim();
		if (string.IsNullOrEmpty(name))
			return;
		var tags = _owner.TagList;
		if (TagCatalog.NameProblem(name, tags) is { } problem)
			await DisplayAlertAsync(Title, problem, "OK");
		else
			await SaveTags(TagCatalog.Add(tags, name));
	}

	private async Task Recolor(TagDefinition tag)
	{
		var (chosen, color) = await PalettePage.PickAsync(Navigation, $"Цвет тега «{tag.Name}»", tag.Color, allowNone: true);
		if (chosen)
			await SaveTags(TagCatalog.Recolor(_owner.TagList, tag.Name, color));
	}

	private async Task Edit(string name, int count)
	{
		const string Rename = "Переименовать", Delete = "Удалить";
		var pick = await DisplayActionSheetAsync(name, "Отмена", null, Rename, Delete);
		if (pick == Rename)
			await RenameTag(name);
		else if (pick == Delete)
			await DeleteTag(name, count);
	}

	private async Task RenameTag(string old)
	{
		var name = (await DisplayPromptAsync("Переименовать тег", "Новое название", "Сохранить", "Отмена", initialValue: old))?.Trim();
		if (string.IsNullOrEmpty(name) || name == old)
			return;
		var tags = _owner.TagList;
		if (TagCatalog.NameProblem(name, tags, renaming: old) is { } problem)
		{
			await DisplayAlertAsync(Title, problem, "OK");
			return;
		}
		if (_owner.Entries.Any(e => e.Tags.Contains(old, Ci)) && !await _owner.ChangeEntries(TagCatalog.RenameIn(_owner.Entries, old, name)))
			return;
		await SaveTags(TagCatalog.Rename(tags, old, name));
	}

	private async Task DeleteTag(string name, int count)
	{
		var question = count == 0 ? $"Удалить тег «{name}»?" : $"Удалить тег «{name}»? Он снимется с записей: {count}. Сами записи останутся.";
		if (!await DisplayAlertAsync("Удалить тег", question, "Удалить", "Отмена"))
			return;
		if (count > 0 && !await _owner.ChangeEntries(TagCatalog.RemoveFrom(_owner.Entries, name)))
			return;
		await SaveTags(TagCatalog.Remove(_owner.TagList, name));
	}
}
