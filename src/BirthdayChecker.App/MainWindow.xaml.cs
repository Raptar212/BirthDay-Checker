using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Entry list with filters, search and Ins/F2/Del editing. The only writer of entries.json besides import.</summary>
public partial class MainWindow : Window
{
    /// <summary>One grid row; properties are what the columns bind and sort on.</summary>
    public sealed record Row(Occurrence O, string? Color, IEnumerable<string> CustomColumns)
    {
        public string?[] Custom { get; } = CustomColumns.Select(c => ColumnCatalog.Value(O.Entry, c)?.ReplaceLineEndings(" ")).ToArray();

        public Guid Id => O.Entry.Id;
        public string Name => O.Entry.Name;
        public string DateText => ReminderText.DescribeDate(O.Entry.Date);
        public DateOnly SortDate => O.Date;
        public string Type => O.Entry.EventType;
        public string Tags => string.Join(", ", O.Entry.Tags);
        public string RemindersText => ReminderText.ShortReminders(O.Entry.Reminders);
        public int? Years => O.Years is > 0 ? O.Years : null;
        public int DaysLeft => O.DaysLeft;
        public string DaysText => ReminderText.DaysColumn(O.DaysLeft);
        public string? Phone => O.Entry.Phone;
        public string? Comment => O.Entry.Comment?.ReplaceLineEndings(" "); // keep rows one line high
        public string Highlight => DaysLeft == 0 ? "Today" : DaysLeft is > 0 and <= 7 ? "Soon" : "";
        public bool HasColor => Color is not null;
        public System.Windows.Media.Brush? RowBrush => ColorPick.Brush(Color);
    }

    private const string All = "Все";
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly List<string> Months =
        Ru.DateTimeFormat.MonthNames.Take(12).Select(m => Ru.TextInfo.ToTitleCase(m)).ToList();

    private readonly string _entriesPath;
    private readonly Func<AppSettings> _settings;
    private readonly Action<Func<AppSettings, AppSettings>> _changeSettings;
    private readonly Action _checkNow;
    private readonly Action _openSettings;
    private List<Entry> _entries = [];
    private bool _readOnly;       // the file could not be read: never save over it
    private int? _month;          // filters of the "Фильтр" button; null is "Все"
    private string? _type;
    private string? _tag;
    private IReadOnlyList<string> _customColumns = []; // bound by index: Row.Custom[i]
    private readonly Dictionary<DataGridColumn, double> _fixedWidths = []; // of a column while it stretches

    internal MainWindow(string entriesPath, Func<AppSettings> settings,
        Action<Func<AppSettings, AppSettings>> changeSettings, Action checkNow, Action openSettings)
    {
        InitializeComponent();
        _entriesPath = entriesPath;
        _settings = settings;
        _changeSettings = changeSettings;
        _checkNow = checkNow;
        _openSettings = openSettings;

        YearNav.Content = Calendar.Nav;
        PreviewKeyDown += OnKey;
        Closing += (_, _) => SaveLayout();
        Calendar.YearChanged += Refresh;
        Calendar.MonthClicked += month =>
        {
            _month = _month == month ? null : month; // a second click goes back to the year
            Refresh();
        };
        Calendar.MonthStepped += StepMonth;
        Calendar.AddRequested += month => New(month);
        Calendar.FocusLeft += () => _month = null;
        Calendar.WantedWidth = settings().CalendarWidth;
        SizeChanged += (_, e) => Calendar.FitBeside(e.NewSize.Width);
        Table.ColumnDisplayIndexChanged += (_, _) => StretchLastColumn();
        Table.Loaded += (_, _) => StretchLastColumn();
        Reload(); // first: it creates the user's columns the layout refers to
        RestoreLayout();
    }

    private IReadOnlyList<TagDefinition> TagList => TagCatalog.Merge(_settings().Tags, _entries);

    private IReadOnlyList<string> CustomColumnNames => ColumnCatalog.Merge(_settings().CustomColumns, _entries);

    private IReadOnlyList<EventDefinition> EventList => EventCatalog.Merge(_settings().EventTypes, _entries);

    private IReadOnlyList<string> EventNames => EventList.Select(t => t.Name).ToList();

    private void StepMonth(int step)
    {
        if (_month is not { } month || CalendarView.Step(Calendar.Year, month, step) is not { } next)
            return;
        _month = next.Month;
        Calendar.Go(next.Year); // redraws the list too
    }

    private void OnCalendarResize(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Calendar.WantedWidth = Calendar.Width - e.HorizontalChange;
        Calendar.FitBeside(ActualWidth);
        Calendar.WantedWidth = Calendar.Width; // stops at the limits, not somewhere past them
    }

    private void OnCalendarResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        _changeSettings(s => s with { CalendarWidth = Calendar.WantedWidth });

    /// <summary>Re-read the file (also after an import from the tray).</summary>
    public void Reload()
    {
        try
        {
            _entries = EntryStorage.Load(_entriesPath).ToList();
            _readOnly = false;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _entries = [];
            _readOnly = true;
            TrayApp.Show($"Файл записей не прочитан, редактирование отключено:\n{e.Message}",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        DropGoneFilters();
        Refresh();
    }

    /// <summary>An event type or tag that no longer exists stops filtering.</summary>
    private void DropGoneFilters()
    {
        if (_type is not null && !EventNames.Contains(_type))
            _type = null;
        if (_tag is not null && !TagList.Any(t => t.Name == _tag))
            _tag = null;
    }

    /// <summary>Re-filter and redraw; also called by the tray at midnight so "today" highlights stay current.</summary>
    internal void Refresh()
    {
        var selected = SelectedIds();
        SyncColumns();
        // A new ItemsSource drops the header sort while the arrow stays — carry both over.
        var sorts = Table.Items.SortDescriptions.ToList();
        var directions = Table.Columns.Select(c => c.SortDirection).ToList();
        var filter = new ListFilter(Month: _month, EventType: _type, Tag: _tag, Search: SearchBox.Text);
        var active = ActiveFilters();
        FilterButton.Content = active.Count > 0 ? $"Фильтр ({active.Count}) ▾" : "Фильтр ▾";
        FilterButton.ToolTip = active.Count > 0 ? string.Join(", ", active) : null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        // A month is that month of the calendar's year, so the list matches its markers.
        var occurrences = filter.Month is not null
            ? EntryList.InYear(_entries, Calendar.Year, today, filter)
            : EntryList.Filter(_entries, today, filter);
        var tags = TagList;
        var rows = occurrences.Select(o => new Row(o, TagCatalog.RowColor(o.Entry, tags), _customColumns)).ToList();
        Table.ItemsSource = rows;
        foreach (var sort in sorts)
            Table.Items.SortDescriptions.Add(sort);
        for (int i = 0; i < directions.Count; i++)
            Table.Columns[i].SortDirection = directions[i];
        foreach (var row in rows.Where(r => selected.Contains(r.Id)))
            Table.SelectedItems.Add(row);

        var settings = _settings();
        // The month filter is the calendar's focus, wherever it was set.
        Calendar.Show(EntryList.InYear(_entries, Calendar.Year, today, filter with { Month = null }), today, _month,
            new CalendarLook(EventList, settings.TodayColor, settings.SeveralColor));
        Status.Text = _readOnly
            ? "Файл записей не прочитан — редактирование отключено. Исправьте entries.json и откройте список заново."
            : $"Записей: {_entries.Count}, показано: {rows.Count}";
    }

    private HashSet<Guid> SelectedIds() => Table.SelectedItems.OfType<Row>().Select(r => r.Id).ToHashSet();

    private void OnFilterChanged(object sender, EventArgs e)
    {
        if (IsInitialized && Table is not null)
            Refresh();
    }

    private string? MonthName => _month is { } m ? Months[m - 1] : null;

    private List<string> ActiveFilters() => new[] { MonthName, _type, _tag }.OfType<string>().ToList();

    /// <summary>Built on every opening, so the lists of event types and tags are always current.</summary>
    private void OnFilter(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FilterButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(FilterGroup("Месяц", Months, MonthName, v => _month = v is null ? null : Months.IndexOf(v) + 1));
        menu.Items.Add(FilterGroup("Событие", EventNames, _type, v => _type = v));
        menu.Items.Add(FilterGroup("Тег", TagList.Select(t => t.Name), _tag, v => _tag = v));
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "Сбросить", IsEnabled = ActiveFilters().Count > 0 };
        reset.Click += (_, _) =>
        {
            (_month, _type, _tag) = (null, null, null);
            Refresh();
        };
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }

    private MenuItem FilterGroup(string title, IEnumerable<string> values, string? current, Action<string?> set)
    {
        var group = new MenuItem { Header = new TextBlock { Text = $"{title}: {current ?? All}" } };
        foreach (var value in new[] { All }.Concat(values))
        {
            // TextBlock: a plain string header would swallow "_" in a tag's name as an access key.
            var item = new MenuItem { Header = new TextBlock { Text = value }, IsChecked = value == (current ?? All) };
            item.Click += (_, _) =>
            {
                set(value == All ? null : value);
                Refresh();
            };
            group.Items.Add(item);
        }
        return group;
    }

    // ---- editing --------------------------------------------------------------------------

    private void OnNew(object sender, RoutedEventArgs e) => New();
    private void OnEdit(object sender, RoutedEventArgs e) => Edit();
    private void OnDelete(object sender, RoutedEventArgs e) => Delete();
    private void OnCheck(object sender, RoutedEventArgs e) => _checkNow();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Table, d) is DataGridRow)
            Edit();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool inSearch = Keyboard.FocusedElement == SearchBox;
        // Ins/Del belong to whatever input has focus (the search box).
        bool inInput = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        switch (e.Key)
        {
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Key.Escape when inSearch:
                SearchBox.Clear();
                Table.Focus();
                break;
            case Key.Insert when !inInput:
                New();
                break;
            case Key.F2:
                Edit();
                break;
            case Key.Delete when !inInput:
                Delete();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private bool CanEdit()
    {
        if (_readOnly)
            TrayApp.Show("Файл записей не прочитан — редактирование отключено.", MessageBoxButton.OK, MessageBoxImage.Warning);
        return !_readOnly;
    }

    /// <param name="month">The month from the calendar's menu; the date is set to its 1st.</param>
    private void New(int? month = null)
    {
        if (!CanEdit())
            return;
        var editor = new EditorWindow(null, EventNames, TagList, CustomColumnNames, month) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is { } entry)
            Commit(fresh => [.. fresh, entry], select: entry.Id);
    }

    private void Edit()
    {
        if (Table.SelectedItems.Count > 1)
        {
            GroupEdit();
            return;
        }
        if (!CanEdit() || Table.SelectedItem is not Row row)
            return;
        var editor = new EditorWindow(row.O.Entry, EventNames, TagList, CustomColumnNames) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not { } entry)
            return;
        Commit(fresh => fresh.Any(x => x.Id == entry.Id)
            ? fresh.Select(x => x.Id == entry.Id ? entry : x).ToList()
            : Gone("Запись уже изменена или удалена (например, импортом)"), select: entry.Id);
    }

    private void Delete()
    {
        var ids = SelectedIds();
        if (!CanEdit() || ids.Count == 0)
            return;
        var names = string.Join("\n", _entries.Where(x => ids.Contains(x.Id)).Select(x => x.Name).Take(10));
        if (MessageBox.Show(this, $"Удалить записи ({ids.Count})?\n\n{names}" + (ids.Count > 10 ? "\n…" : ""),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Commit(fresh => fresh.Any(x => ids.Contains(x.Id))
            ? fresh.Where(x => !ids.Contains(x.Id)).ToList()
            : Gone("Выбранных записей уже нет (например, после импорта)"), select: null);
    }

    // ---- "Ещё" menu: report, CSV, backup -----------------------------------------------------

    private void OnMore(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void OnSettings(object sender, RoutedEventArgs e) => _openSettings();

    private void OnEvents(object sender, RoutedEventArgs e)
    {
        if (!CanEdit())
            return;
        new EventsWindow(() => EventList,
            () => _settings(),
            name => _entries.Count(x => StringComparer.CurrentCultureIgnoreCase.Equals(x.EventType, name)),
            change =>
            {
                _changeSettings(change);
                DropGoneFilters();
                Refresh();
            },
            (name, newName) =>
            {
                bool filtered = StringComparer.CurrentCultureIgnoreCase.Equals(_type, name);
                if (!Commit(fresh => EventCatalog.RenameIn(fresh, name, newName), select: null))
                    return false;
                if (filtered)
                    _type = newName; // the filter follows the entries
                return true;
            })
        { Owner = this }.ShowDialog();
    }

    private void OnTags(object sender, RoutedEventArgs e)
    {
        if (!CanEdit())
            return;
        new TagsWindow(() => TagList,
            name => _entries.Count(x => x.Tags.Contains(name, StringComparer.CurrentCultureIgnoreCase)),
            SaveTags,
            (name, newName) => Commit(fresh => newName is null
                ? TagCatalog.RemoveFrom(fresh, name)
                : TagCatalog.RenameIn(fresh, name, newName), select: null))
        { Owner = this }.ShowDialog();
    }

    private void SaveTags(IReadOnlyList<TagDefinition> tags)
    {
        _changeSettings(s => s with { Tags = tags });
        DropGoneFilters();
        Refresh();
    }

    private void GroupEdit()
    {
        var ids = SelectedIds();
        if (!CanEdit())
            return;
        var selected = Table.Items.OfType<Row>().Where(r => ids.Contains(r.Id)).Select(r => r.O.Entry).ToList(); // as the list shows them
        var dialog = new GroupEditWindow(selected, EventNames, TagList, CustomColumnNames, Table.RowStyle) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } edit)
            return;
        int changed = 0;
        if (Commit(fresh =>
            {
                changed = fresh.Count(x => ids.Contains(x.Id) && !x.Equals(edit.Apply(x)));
                return fresh.Any(x => ids.Contains(x.Id))
                    ? fresh.Select(x => ids.Contains(x.Id) ? edit.Apply(x) : x).ToList()
                    : Gone("Выбранных записей уже нет (например, после импорта)");
            }, select: null))
            Status.Text += $" · изменено записей: {changed} из {ids.Count}";
    }

    private void OnReport(object sender, RoutedEventArgs e)
    {
        // Table.Items is the view: current filters and header sort, as the user sees it. A month of the calendar's year may hold past dates, not "upcoming".
        var items = Table.Items.OfType<Row>().Select(r => r.O).Where(o => o.DaysLeft >= 0).ToList();
        new ReportWindow(Report.Build(items, DateOnly.FromDateTime(DateTime.Today))) { Owner = this }.Show();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (LoadForExport() is not { } entries)
            return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"birthday-checker-{DateTime.Today:yyyy-MM-dd}",
            Filter = EntryExchange.Filter,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (EntryExchange.IsAppFile(dialog.FileName, Path.GetDirectoryName(_entriesPath)!))
        {
            MessageBox.Show(this, "Это рабочий файл программы, экспорт его бы заменил. Выберите другое имя или папку.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            EntryExchange.Write(dialog.FileName, entries);
            Status.Text = $"Экспортировано записей: {entries.Count}";
        }
        catch (Exception x) when (x is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Не удалось сохранить: {x.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (!CanEdit())
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Все форматы (*.csv;*.txt;*.md;*.json)|*.csv;*.txt;*.md;*.json|" + EntryExchange.Filter };
        if (dialog.ShowDialog(this) != true)
            return;

        ImportResult result;
        try
        {
            result = EntryExchange.Read(dialog.FileName);
        }
        catch (Exception x) when (x is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Не удалось прочитать файл: {x.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (result.Entries.Count == 0)
        {
            MessageBox.Show(this, "В файле нет записей для импорта.\n\n" + IssuesText(result), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Entries already in the list are always skipped; the user ticks which new ones to add. The file stays as it was.
        var unique = EntryCsv.Unique(_entries, result.Entries);
        if (unique.Count == 0)
        {
            MessageBox.Show(this, $"Все записи из файла ({result.Entries.Count}) уже есть в списке (тот же Id или то же имя, событие и дата)." +
                "\n\n" + IssuesText(result), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var pick = new PickWindow(unique, result.Entries.Count - unique.Count) { Owner = this };
        if (pick.ShowDialog() != true)
            return;

        // Counts come from the merge on the fresh list, not from the (possibly stale) window copy.
        EntryCsv.MergeResult? merged = null;
        if (!Commit(fresh => (merged = EntryCsv.Merge(fresh, pick.Selected)).Entries, select: null) || merged is null)
            return;
        MessageBox.Show(this, $"Добавлено: {merged.Added}." + (merged.Skipped > 0 ? $" Пропущено как уже существующие: {merged.Skipped}." : "") +
            "\n\n" + IssuesText(result), Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string IssuesText(ImportResult result) => result.Issues.Count == 0
        ? "Замечаний нет."
        : "Замечания:\n" + string.Join("\n", result.Issues.Select(i => $"строка {i.Line}: {i.Name} — {i.Message}"));

    private void OnBackup(object sender, RoutedEventArgs e)
    {
        if (LoadForExport() is not { } entries)
            return; // a broken file is not a backup
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"entries-{DateTime.Today:yyyy-MM-dd}.json",
            Filter = "Резервная копия (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        // Copy to a temp name, verify, then move into place: an existing good backup is only replaced by a checked one.
        var tmp = dialog.FileName + ".tmp";
        try
        {
            File.Copy(_entriesPath, tmp, overwrite: true);
            int count = EntryStorage.Load(tmp).Count;
            File.Move(tmp, dialog.FileName, overwrite: true);
            Status.Text = $"Резервная копия сохранена и проверена: {count} записей";
        }
        catch (Exception x) when (x is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (IOException) { }
            MessageBox.Show(this, $"Копия не сохранена: {x.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Works even when entries.json is broken — that is what a restore is for.</summary>
    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Резервная копия (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        IReadOnlyList<Entry> backup;
        try
        {
            backup = EntryStorage.Load(dialog.FileName); // strict: a bad copy is refused before anything changes
        }
        catch (Exception x) when (x is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Копия не прочитана, ничего не изменено:\n{x.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var bak = EntryStorage.BackupPath(_entriesPath);
        var keepBak = $"{bak}-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (MessageBox.Show(this,
                $"Заменить текущий список ({(_readOnly ? "не читается" : _entries.Count.ToString())}) копией ({backup.Count} записей)?\n" +
                @"Текущий файл сохранится в data\entries.json.bak" +
                (File.Exists(bak) ? $", а прежний entries.json.bak — в {Path.GetFileName(keepBak)}." : "."),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            // Restoring usually follows a broken file; the previous .bak may be the last good list — don't rotate it away.
            if (File.Exists(bak))
                File.Copy(bak, keepBak);
            EntryStorage.Save(_entriesPath, backup);
        }
        catch (Exception x) when (x is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Не восстановлено, файл не изменён: {x.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Reload();
    }

    /// <summary>Fresh, validated copy of the file for export / backup; null (with a message) if unreadable.</summary>
    private List<Entry>? LoadForExport()
    {
        try
        {
            return EntryStorage.Load(_entriesPath).ToList();
        }
        catch (Exception x) when (x is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Файл записей не прочитан: {x.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private List<Entry>? Gone(string what)
    {
        MessageBox.Show(this, $"{what} — изменение не сохранено. Список обновлён.", Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the list freshly read from disk, not to the in-memory copy:
    /// a dialog may have been open while the tray imported or the file was edited by hand. If the file
    /// can't be read, nothing is written. The window shows the new list only after a successful save.
    /// </summary>
    /// <returns>True when the change was applied. An import that skipped everything writes nothing: Save would rotate away the .bak.</returns>
    private bool Commit(Func<List<Entry>, List<Entry>?> change, Guid? select)
    {
        List<Entry>? entries;
        try
        {
            var fresh = EntryStorage.Load(_entriesPath).ToList();
            var before = fresh.ToList(); // change may edit the list it gets
            entries = change(fresh);
            if (entries is null)
            {
                Reload();
                return false;
            }
            if (!entries.SequenceEqual(before))
                EntryStorage.Save(_entriesPath, entries);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Изменения не сохранены, файл не изменён: {e.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _entries = entries;
        DropGoneFilters();
        Table.SelectedItems.Clear();
        Refresh();
        if (select is not { } id)
            return true;
        if (Table.ItemsSource is IEnumerable<Row> rows && rows.FirstOrDefault(r => r.Id == id) is { } row)
        {
            Table.SelectedItem = row;
            Table.ScrollIntoView(row);
        }
        else
            Status.Text += " · сохранённая запись скрыта текущим фильтром";
        return true;
    }

    // ---- columns --------------------------------------------------------------------------

    private void OnColumns(object sender, RoutedEventArgs e)
    {
        if (!CanEdit())
            return;
        new ColumnsWindow(() => CustomColumnNames, () => _settings().HiddenColumns ?? [],
            name => _entries.Count(x => ColumnCatalog.Value(x, name) is not null),
            AddColumn, RenameColumn, RemoveColumn, SetColumnVisible)
        { Owner = this }.ShowDialog();
    }

    private void AddColumn(string name)
    {
        var names = CustomColumnNames;
        _changeSettings(s => s with { CustomColumns = [.. names, name] });
        Refresh();
    }

    private void RenameColumn(string name, string newName)
    {
        var column = CustomColumn(name);
        var (width, index) = column is null ? (0, -1) : (LayoutWidth(column), column.DisplayIndex);
        if (!Commit(fresh => ColumnCatalog.RenameIn(fresh, name, newName), select: null))
            return;
        // After the commit: a name the entries still hold would come back through CustomColumnNames.
        var names = ColumnCatalog.Rename(CustomColumnNames, name, newName);
        _changeSettings(s => s with
        {
            CustomColumns = names,
            HiddenColumns = s.HiddenColumns is { } hidden ? ColumnCatalog.Rename(hidden, name, newName) : null,
            ReminderColumns = s.ReminderColumns is { } ticked ? ColumnCatalog.Rename(ticked, name, newName) : null,
        });
        Refresh();
        // A rename that isn't only of case swaps the column object: keep its place.
        if (CustomColumn(newName) is { } renamed && !ReferenceEquals(renamed, column) && index >= 0)
        {
            renamed.Width = new DataGridLength(width);
            renamed.DisplayIndex = Math.Min(index, Table.Columns.Count - 1);
            StretchLastColumn();
        }
    }

    private void RemoveColumn(string name)
    {
        if (!Commit(fresh => ColumnCatalog.RemoveFrom(fresh, name), select: null))
            return;
        var names = ColumnCatalog.Remove(CustomColumnNames, name);
        _changeSettings(s => s with
        {
            CustomColumns = names,
            HiddenColumns = s.HiddenColumns is { } hidden ? ColumnCatalog.Remove(hidden, name) : null,
            ReminderColumns = s.ReminderColumns is { } ticked ? ColumnCatalog.Remove(ticked, name) : null,
        });
        Refresh();
    }

    private void SetColumnVisible(string name, bool visible)
    {
        _changeSettings(s =>
        {
            var hidden = ColumnCatalog.Remove(s.HiddenColumns ?? [], name);
            return s with { HiddenColumns = visible ? hidden : [.. hidden, name] };
        });
        Refresh();
    }

    private DataGridColumn? CustomColumn(string name) =>
        Table.Columns.Skip(ColumnCatalog.BuiltIn.Count).FirstOrDefault(c => StringComparer.CurrentCultureIgnoreCase.Equals(c.Header, name));

    /// <summary>Brings the table in line with the settings and with the names entries hold (an import may add some).</summary>
    private void SyncColumns()
    {
        var names = CustomColumnNames;
        if (!names.SequenceEqual(_customColumns))
        {
            foreach (var gone in Table.Columns.Skip(ColumnCatalog.BuiltIn.Count).Where(c => !names.Contains((string)c.Header, StringComparer.CurrentCultureIgnoreCase)).ToList())
            {
                Table.Columns.Remove(gone);
                _fixedWidths.Remove(gone);
            }
            // The indexes in the bindings are about to change, and a sort by a stale index would sort by another column.
            foreach (var sort in Table.Items.SortDescriptions.Where(d => d.PropertyName.StartsWith(nameof(Row.Custom), StringComparison.Ordinal)).ToList())
                Table.Items.SortDescriptions.Remove(sort);
            for (int i = 0; i < names.Count; i++)
            {
                var column = CustomColumn(names[i]) as DataGridTextColumn;
                if (column is null)
                    Table.Columns.Add(column = new DataGridTextColumn { Width = 130, MinWidth = 90 });
                var path = $"{nameof(Row.Custom)}[{i}]";
                column.Header = names[i];
                column.Binding = new System.Windows.Data.Binding(path);
                column.SortMemberPath = path;
                column.SortDirection = null;
            }
            _customColumns = names;
        }
        var hidden = _settings().HiddenColumns ?? [];
        foreach (var column in Table.Columns)
        {
            column.Visibility = hidden.Contains((string)column.Header, StringComparer.CurrentCultureIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
            ((DataGridTextColumn)column).ElementStyle = Ui.CenteredCell;
        }
        StretchLastColumn();
    }

    /// <summary>The rightmost visible column takes the free space, so nothing empty stays after it; its own width comes back when it stops being last.</summary>
    private void StretchLastColumn()
    {
        if (!Table.IsLoaded)
            return; // display indexes are -1 until then; its Loaded event comes back here
        var last = Table.Columns.Where(c => c.Visibility == Visibility.Visible).MaxBy(c => c.DisplayIndex);
        foreach (var column in Table.Columns)
        {
            if (ReferenceEquals(column, last) && !column.Width.IsStar)
            {
                _fixedWidths[column] = LayoutWidth(column);
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            }
            else if (!ReferenceEquals(column, last) && column.Width.IsStar)
            {
                column.Width = new DataGridLength(LayoutWidth(column));
            }
        }
    }

    /// <summary>The width to remember: a stretching column's fill depends on the window.</summary>
    private double LayoutWidth(DataGridColumn column) =>
        column.Width.IsStar ? _fixedWidths.GetValueOrDefault(column, 130) : column.Width.Value;

    // ---- layout ---------------------------------------------------------------------------

    private void RestoreLayout()
    {
        var s = _settings();
        var area = SystemParameters.WorkArea; // a size saved on a bigger monitor must still fit
        if (s.ListWidth is { } w) Width = Math.Min(w, area.Width);
        if (s.ListHeight is { } h) Height = Math.Min(h, area.Height);
        if (s.Columns is not { Count: > 0 } saved)
            return;
        foreach (var layout in saved.OrderBy(c => c.Index))
            if (Table.Columns.FirstOrDefault(c => (string)c.Header == layout.Key) is { } column)
            {
                column.Width = new DataGridLength(Math.Max(layout.Width, column.MinWidth)); // a width saved before the minimums existed
                column.DisplayIndex = Math.Min(layout.Index, Table.Columns.Count - 1);
            }
    }

    private void SaveLayout()
    {
        // From Width, not ActualWidth: a hidden column has none, and must keep its place and width for when it comes back.
        var columns = Table.Columns.Select(c => new ColumnLayout((string)c.Header, c.DisplayIndex, LayoutWidth(c))).ToList();
        var size = WindowState == WindowState.Normal ? new Size(ActualWidth, ActualHeight) : RestoreBounds.Size;
        _changeSettings(s => s with { Columns = columns, ListWidth = size.Width, ListHeight = size.Height });
    }
}
