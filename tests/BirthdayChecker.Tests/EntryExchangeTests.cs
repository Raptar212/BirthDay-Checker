using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public sealed class EntryExchangeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bdc-exchange").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly List<Entry> Entries =
    [
        new("Иван Тестов", "День Рождения", new FixedDate(5, 14, 1990))
        {
            Phone = "+7 000 000-00-00", Comment = "Первая строка\r\nвторая строка", Tags = ["семья", "друзья"],
            Reminders = Reminder.Day1 | Reminder.Week2 | Reminder.Month3, Color = "#FFE0A0",
        },
        new("Без года", "Именины", new FixedDate(2, 29)),
        new("День Матери", "Праздник", new NthWeekdayDate(5, DayOfWeek.Sunday, 2)),
        new("Последнее", "Праздник", new NthWeekdayDate(11, DayOfWeek.Sunday, NthWeekdayDate.Last)),
        new("Троица", "Свой тип", new EasterDate(49)),
        new("Радоница минус", "Праздник", new EasterDate(-3)),
        new("С | чертой", "Тип | тип", new FixedDate(1, 1)) { Comment = "a|b \\| c\\d <br> e\tтаб" },
        new("=СУММ(1)", "+формула", new FixedDate(1, 2)) { Comment = "'=не формула", Phone = "=\"7\"" },
    ];

    /// <summary>Records compare lists by reference, so compare what the user would see.</summary>
    private static string Show(Entry e) => string.Join(" ~ ", e.Id, e.Name, e.EventType, e.Date, e.Phone, e.Comment,
        string.Join(",", e.Tags), e.Reminders, e.Color);

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    [InlineData("a.json")]
    [InlineData("a.TXT")]
    public void RoundTrip_KeepsEveryField(string name)
    {
        var path = Path.Combine(_dir, name);
        EntryExchange.Write(path, Entries);
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Issues);
        Assert.Equal(Entries.Select(Show), result.Entries.Select(Show));
    }

    [Fact]
    public void RoundTrip_Csv_KeepsEveryFieldButQuotesInPhone()
    {
        var path = Path.Combine(_dir, "a.csv");
        EntryExchange.Write(path, Entries);
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Issues);
        Assert.Equal(Entries.Select(e => Show(e with { Phone = e.Phone?.Replace("\"", "") })), result.Entries.Select(Show));
        Assert.Equal((byte)0xEF, File.ReadAllBytes(path)[0]); // BOM for Excel
    }

    [Fact]
    public void Txt_UnicodeLineSeparatorStaysInsideTheField()
    {
        var path = Path.Combine(_dir, "a.txt");
        var entry = new Entry("Из Word", "Праздник", new FixedDate(1, 3)) { Comment = "до" + (char)0x2028 + "после" + (char)0x85 + "конец" };
        EntryExchange.Write(path, [entry]);
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Issues);
        Assert.Equal(entry.Comment, Assert.Single(result.Entries).Comment);
    }

    [Theory]
    [InlineData("a.txt", "\t")]
    [InlineData("a.md", " | ")]
    public void ShiftedRow_IsReported_NotImportedWithWrongFields(string name, string separator)
    {
        var path = Path.Combine(_dir, name);
        EntryExchange.Write(path, [new Entry("Сдвиг", "Праздник", new FixedDate(1, 3)) { Comment = "начало" }]);
        File.WriteAllText(path, File.ReadAllText(path).Replace("начало", "начало" + separator + "конец"));
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Entries);
        Assert.Contains("колонок", Assert.Single(result.Issues).Message);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    public void ShortRow_IsReported_SoItCannotBlankAGoodEntry(string name)
    {
        var path = Path.Combine(_dir, name);
        EntryExchange.Write(path, [Entries[0]]);
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text[..text.LastIndexOf(name.EndsWith("md") ? '|' : '\t', text.LastIndexOf(Entries[0].Color!))]);
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Entries);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Markdown_LineBrokenInsideTheTable_IsReported()
    {
        var path = Path.Combine(_dir, "a.md");
        EntryExchange.Write(path, Entries.Take(2));
        var lines = File.ReadAllLines(path).ToList();
        lines[2] = lines[2].TrimStart('|', ' '); // the first row lost its leading pipe
        File.WriteAllLines(path, lines);
        var result = EntryExchange.Read(path);
        Assert.Equal(Entries[1].Id, Assert.Single(result.Entries).Id);
        Assert.Equal(3, Assert.Single(result.Issues).Line);
    }

    [Theory]
    [InlineData("entries.json", true)]
    [InlineData("ENTRIES.JSON", true)]
    [InlineData("entries.json.bak", true)]
    [InlineData("settings.json", true)]
    [InlineData("state.json", true)]
    [InlineData("мой-экспорт.json", false)]
    public void AppFiles_AreNotExportTargets(string name, bool expected) =>
        Assert.Equal(expected, EntryExchange.IsAppFile(Path.Combine(_dir, name), _dir));

    [Theory]
    [InlineData("entries.json.bak")]
    [InlineData("state.json")]
    public void AppFiles_InServiceFolder_AreNotExportTargets(string name) =>
        Assert.True(EntryExchange.IsAppFile(Path.Combine(AppLayout.DataFolder(_dir), name), _dir));

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    public void HandTypedBackslash_IsKept(string name)
    {
        var path = Path.Combine(_dir, name);
        EntryExchange.Write(path, [new Entry("Путь", "Праздник", new FixedDate(1, 3)) { Comment = "заглушка" }]);
        File.WriteAllText(path, File.ReadAllText(path).Replace("заглушка", @"C:\dir\file"));
        Assert.Equal(@"C:\dir\file", Assert.Single(EntryExchange.Read(path).Entries).Comment);
    }

    [Fact]
    public void AppFile_WithRelativeSegmentsOrTrailingSeparator_IsStillRefused()
    {
        Assert.True(EntryExchange.IsAppFile(Path.Combine(_dir, "x", "..", "entries.json"), _dir));
        Assert.True(EntryExchange.IsAppFile(Path.Combine(_dir, "entries.json"), _dir + Path.DirectorySeparatorChar));
        Assert.True(EntryExchange.IsAppFile(@"C:\entries.json", @"C:\"));
    }

    [Fact]
    public void MissingColumns_PointAtTheHeaderLine()
    {
        var path = Path.Combine(_dir, "a.md");
        File.WriteAllText(path, "# заголовок\r\n\r\n| a | b |\r\n| --- | --- |\r\n");
        Assert.Equal(3, Assert.Single(EntryExchange.Read(path).Issues).Line);
    }

    [Fact]
    public void SameNameInAnotherFolder_IsAllowed() =>
        Assert.False(EntryExchange.IsAppFile(Path.Combine(_dir, "копии", "entries.json"), _dir));

    [Fact]
    public void Write_LeavesNoTempFile_AndFailureKeepsTheOldExport()
    {
        var path = Path.Combine(_dir, "a.txt");
        EntryExchange.Write(path, Entries.Take(1));
        Assert.Equal([path], Directory.GetFiles(_dir));

        var before = File.ReadAllText(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) // locked: the move must fail
            Assert.ThrowsAny<Exception>(() => EntryExchange.Write(path, Entries));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    public void TextFormats_HaveNoExcelGuards(string name)
    {
        var path = Path.Combine(_dir, name);
        EntryExchange.Write(path, [Entries[0]]);
        var text = File.ReadAllText(path);
        Assert.Contains("+7 000 000-00-00", text);
        Assert.DoesNotContain("=\"+7", text);
    }

    [Fact]
    public void Json_ExportLeavesNoBackupBeside()
    {
        var path = Path.Combine(_dir, "a.json");
        EntryExchange.Write(path, Entries);
        EntryExchange.Write(path, Entries);
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Json_BrokenFileThrows_NothingIsImported()
    {
        var path = Path.Combine(_dir, "a.json");
        File.WriteAllText(path, "{ not json");
        Assert.Throws<InvalidDataException>(() => EntryExchange.Read(path));
    }

    [Fact]
    public void Markdown_IgnoresTextAroundTheTable()
    {
        var path = Path.Combine(_dir, "a.md");
        EntryExchange.Write(path, [Entries[0]]);
        File.WriteAllText(path, "# Мой список\r\n\r\nзаметка\r\n\r\n" + File.ReadAllText(path) + "\r\nконец\r\n");
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Issues);
        Assert.Equal(Show(Entries[0]), Show(Assert.Single(result.Entries)));
    }

    [Fact]
    public void Markdown_RowWithoutClosingPipe_StillReads()
    {
        var path = Path.Combine(_dir, "a.md");
        EntryExchange.Write(path, [Entries[1]]);
        var lines = File.ReadAllLines(path);
        lines[2] = lines[2].TrimEnd().TrimEnd('|');
        File.WriteAllLines(path, lines);
        Assert.Equal(Entries[1].Id, Assert.Single(EntryExchange.Read(path).Entries).Id);
    }

    [Theory]
    [InlineData("a.txt", "просто текст\r\nещё строка\r\n")]
    [InlineData("a.md", "# без таблицы\r\n")]
    [InlineData("a.txt", "")]
    public void NotAnExport_NoEntries_OneIssue(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        var result = EntryExchange.Read(path);
        Assert.Empty(result.Entries);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Txt_BadRowIsSkippedWithItsLine_OthersKept()
    {
        var path = Path.Combine(_dir, "a.txt");
        EntryExchange.Write(path, Entries.Take(2));
        var lines = File.ReadAllLines(path);
        lines[1] = lines[1].Replace("\t5\t14\t", "\t13\t14\t");
        File.WriteAllLines(path, lines);
        var result = EntryExchange.Read(path);
        Assert.Equal(Entries[1].Id, Assert.Single(result.Entries).Id);
        Assert.Equal(2, Assert.Single(result.Issues).Line);
    }

    [Fact]
    public void UnknownExtension_IsCsv()
    {
        var path = Path.Combine(_dir, "a.dat");
        EntryExchange.Write(path, Entries.Take(1));
        Assert.Equal(Entries[0].Id, Assert.Single(EntryExchange.Read(path).Entries).Id);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.md")]
    [InlineData("a.json")]
    public void Reimport_MergesAsDuplicates_NothingLost(string name)
    {
        var path = Path.Combine(_dir, name);
        EntryExchange.Write(path, Entries);
        var merged = EntryCsv.Merge(Entries, EntryExchange.Read(path).Entries);
        Assert.Equal((0, Entries.Count, Entries.Count), (merged.Added, merged.Skipped, merged.Entries.Count));
    }
}
