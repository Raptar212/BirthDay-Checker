using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class ColumnCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bdc-columns-" + Guid.NewGuid());

    public ColumnCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Entry Person(params (string Column, string Value)[] fields) =>
        fields.Aggregate(new Entry("Иван Тестов", "День Рождения", new FixedDate(6, 10)), (e, f) => ColumnCatalog.With(e, f.Column, f.Value));

    [Theory]
    [InlineData("Город", true)]
    [InlineData("Любимый; цвет | и всё", true)]
    [InlineData("", false)]
    [InlineData(" Город", false)]
    [InlineData("a\tb", false)]
    [InlineData("=СУММ(1)", false)]
    [InlineData("---", false)]
    [InlineData("телефон", false)]
    [InlineData("Возраст", false)]
    [InlineData("Тип даты", false)]
    [InlineData("id", false)]
    public void IsValidName(string name, bool valid) => Assert.Equal(valid, ColumnCatalog.IsValidName(name));

    [Fact]
    public void NameProblem_DuplicateRefused_OwnCaseChangeAllowed()
    {
        Assert.NotNull(ColumnCatalog.NameProblem("ГОРОД", ["Город"]));
        Assert.Null(ColumnCatalog.NameProblem("ГОРОД", ["Город"], renaming: "Город"));
    }

    [Fact]
    public void Merge_KeepsSavedOrder_ThenNamesOnlyEntriesKnow() =>
        Assert.Equal(["Я", "Город", "Подарок"], ColumnCatalog.Merge(["Я"], [Person(("Подарок", "x"), ("я", "y")), Person(("Город", "z"), ("подарок", "w"))]));

    [Fact]
    public void With_SameValue_SameEntry_BlankRemovesLastField()
    {
        var entry = Person(("Город", "Москва"));
        Assert.Same(entry, ColumnCatalog.With(entry, "ГОРОД", "Москва"));
        Assert.Null(ColumnCatalog.With(entry, "Город", " ").CustomFields);
    }

    [Fact]
    public void RenameIn_MovesValue_RemoveFrom_DropsIt_OthersUntouched()
    {
        var entry = Person(("Город", "Москва"), ("Подарок", "книга"));
        var untouched = Person();

        var renamed = ColumnCatalog.RenameIn([entry, untouched], "город", "Столица");
        Assert.Equal("Москва", ColumnCatalog.Value(renamed[0], "Столица"));
        Assert.Null(ColumnCatalog.Value(renamed[0], "Город"));
        Assert.Same(untouched, renamed[1]);

        var removed = ColumnCatalog.RemoveFrom([entry, untouched], "Город");
        Assert.Equal(["Подарок"], removed[0].CustomFields!.Keys);
        Assert.Same(untouched, removed[1]);
    }

    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".json")]
    public void AllFormats_KeepCustomFields(string extension)
    {
        var path = Path.Combine(_dir, "export" + extension);
        var entries = new[]
        {
            Person(("Город", "=Москва; | центр"), ("Подарок", "книга")),
            new Entry("Анна", "Именины", new FixedDate(1, 5)),
        };

        EntryExchange.Write(path, entries);
        var result = EntryExchange.Read(path);

        Assert.Empty(result.Issues);
        Assert.Equal("=Москва; | центр", ColumnCatalog.Value(result.Entries[0], "Город"));
        Assert.Equal("книга", ColumnCatalog.Value(result.Entries[0], "Подарок"));
        Assert.Null(result.Entries[1].CustomFields);
    }

    [Fact]
    public void Storage_RoundTrip_AndOlderFileLoads()
    {
        var path = Path.Combine(_dir, "entries.json");
        EntryStorage.Save(path, [Person(("Город", "Москва"))]);
        Assert.Equal("Москва", ColumnCatalog.Value(EntryStorage.Load(path)[0], "Город"));

        File.WriteAllText(path, """{ "formatVersion": 2, "entries": [] }""");
        Assert.Empty(EntryStorage.Load(path));
    }

    [Fact]
    public void Csv_UnknownColumnBecomesCustom_BadNameReported()
    {
        var csv = string.Concat(
            "Имя;Событие;Тип даты;Месяц;День;Год;День недели;Номер;Смещение от Пасхи;Телефон;Комментарий;Теги;Напомнить;Цвет;Город;Возраст\r\n",
            "Иван;ДР;число;6;10;;;;;;;;;;Москва;30\r\n");

        var result = EntryCsv.Parse(csv);

        Assert.Equal("Москва", ColumnCatalog.Value(Assert.Single(result.Entries), "Город"));
        Assert.Contains("Возраст", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void GroupEdit_SetsAndClears_UnchangedStaysEqual()
    {
        var entry = Person(("Город", "Москва"), ("Подарок", "книга"));

        var changed = new GroupEdit { Custom = new Dictionary<string, string?> { ["Город"] = "Псков", ["Подарок"] = null } }.Apply(entry);
        Assert.Equal("Псков", ColumnCatalog.Value(changed, "Город"));
        Assert.Null(ColumnCatalog.Value(changed, "Подарок"));

        Assert.Equal(entry, new GroupEdit { Custom = new Dictionary<string, string?> { ["Город"] = "Москва" } }.Apply(entry));
    }

    [Fact]
    public void Report_ShowsCustomFields()
    {
        var entry = Person(("Подарок", "книга"));
        var text = Report.Build([Upcoming.Next(entry, new DateOnly(2026, 6, 1))], new DateOnly(2026, 6, 1));
        Assert.Contains("Подарок: книга", text);
    }

    [Fact]
    public void Settings_DropInvalidColumns_LockedCantBeHidden()
    {
        var path = Path.Combine(_dir, "settings.json");
        AppSettings.Save(path, new AppSettings { CustomColumns = ["Город", "город", "Имя", " "], HiddenColumns = ["Имя", "Дата", "Телефон"] });

        var loaded = AppSettings.Load(path);

        Assert.Equal(["Город"], loaded.CustomColumns);
        Assert.Equal(["Телефон"], loaded.HiddenColumns);
    }
}
