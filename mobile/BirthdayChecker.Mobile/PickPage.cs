using BirthdayChecker.Core;
using Entry = BirthdayChecker.Core.Entry; // MAUI has its own Entry (a text box)

namespace BirthdayChecker.Mobile;

/// <summary>
/// "Добавить из файла": the file's entries that are not in the list yet, all ticked, as on the PC's PickWindow.
/// Writes nothing itself: <c>add</c> merges the ticked ones and returns false (after telling the user) when the file wasn't written.
/// </summary>
public sealed class PickPage : ContentPage
{
	private readonly List<(Entry Entry, CheckBox Box)> _rows;
	private readonly Button _add = new();
	private readonly Func<IReadOnlyList<Entry>, Task<bool>> _commit;
	private bool _busy; // a second tap while saving would add the entries twice

	/// <param name="skipped">How many of the file's entries are already in the list.</param>
	public PickPage(IReadOnlyList<Entry> unique, int skipped, Func<IReadOnlyList<Entry>, Task<bool>> add)
	{
		_commit = add;
		Title = "Новые записи";
		_rows = [.. unique.Select(e => (e, new CheckBox { IsChecked = true }))];

		var list = new VerticalStackLayout();
		foreach (var (entry, box) in _rows)
		{
			box.CheckedChanged += (_, _) => Count();
			var text = new VerticalStackLayout
			{
				VerticalOptions = LayoutOptions.Center,
				Children =
				{
					new Label { Text = entry.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 },
					new Label { Text = ReminderText.Brief(entry) },
				},
			};
			var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], Padding = new Thickness(0, 6) };
			row.Add(box, 0);
			row.Add(text, 1);
			// A tap on the text ticks too, not only the small box.
			var tap = new TapGestureRecognizer();
			tap.Tapped += (_, _) => box.IsChecked = !box.IsChecked;
			text.GestureRecognizers.Add(tap);
			list.Add(row);
		}

		var hint = new Label
		{
			Text = $"Новых записей в файле: {unique.Count}." +
				(skipped > 0 ? $" Уже есть в списке и пропущены: {skipped} (тот же Id или то же имя, событие и дата)." : ""),
		};
		var all = new Button { Text = "Отметить все" };
		all.Clicked += (_, _) => Tick(true);
		var none = new Button { Text = "Снять все" };
		none.Clicked += (_, _) => Tick(false);
		_add.Clicked += OnAdd;

		var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], Padding = new Thickness(16, 8), RowSpacing = 8 };
		grid.Add(hint, 0, 0);
		grid.Add(new ScrollView { Content = list }, 0, 1);
		grid.Add(new HorizontalStackLayout { Spacing = 8, Children = { all, none, _add } }, 0, 2);
		Content = grid;
		Count();
	}

	private void Tick(bool on)
	{
		foreach (var (_, box) in _rows)
			box.IsChecked = on;
	}

	private void Count()
	{
		int n = _rows.Count(r => r.Box.IsChecked);
		_add.Text = $"Добавить ({n})";
		_add.IsEnabled = n > 0;
	}

	private async void OnAdd(object? sender, EventArgs e)
	{
		if (_busy)
			return;
		_busy = true;
		try
		{
			if (await _commit([.. _rows.Where(r => r.Box.IsChecked).Select(r => r.Entry)]))
				await Navigation.PopAsync();
		}
		finally
		{
			_busy = false;
		}
	}
}
