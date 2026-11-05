using System.Text;
using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public sealed class EntryStorageTests : IDisposable
{
    private const string Id1 = "11111111-1111-1111-1111-111111111111";
    private const string Easter = """{ "kind": "easter", "offsetDays": 0 }""";

    private readonly string _dir = Directory.CreateTempSubdirectory("bdc-test-").FullName;
    private string FilePath => Path.Combine(_dir, "entries.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static List<Entry> Sample() =>
    [
        new("Иван Тестов", "День Рождения", new FixedDate(2, 29, 1992))
        {
            Phone = "+7 000 000-00-00",
            Comment = "fictional person",
            Tags = ["семья"],
            Reminders = Reminder.Day1 | Reminder.Week1 | Reminder.Month3,
        },
        new("Mother's Day", "Праздник", new NthWeekdayDate(11, DayOfWeek.Sunday, NthWeekdayDate.Last)),
        new("Trinity", "Праздник", new EasterDate(49)) { Tags = ["религиозный", "регулярный"] },
        new("Clean Monday", "Праздник", new EasterDate(-48)),
        new("Unknown year", "Именины", new FixedDate(12, 7)) { Phone = "" },
    ];

    private static string OneEntry(string body) =>
        $$"""{ "formatVersion": 1, "entries": [ { {{body}} } ] }""";

    [Fact]
    public void RoundTrip_AllDateKindsAndFields()
    {
        var entries = Sample();
        EntryStorage.Save(FilePath, entries);
        Assert.Equivalent(entries, EntryStorage.Load(FilePath), strict: true);
    }

    [Fact]
    public void File_IsReadableUtf8_WithDiscriminator()
    {
        EntryStorage.Save(FilePath, Sample());
        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"name\": \"Иван Тестов\"", text);
        Assert.Contains("\"kind\": \"easter\"", text);
        Assert.Contains("\"reminders\": \"Day1, Week1, Month3\"", text);
    }

    [Fact]
    public void MissingFile_LoadsEmpty() => Assert.Empty(EntryStorage.Load(FilePath));

    [Theory]
    [InlineData(".bak")]
    [InlineData(".tmp")]
    public void MissingFile_WithLeftover_RefusesInsteadOfEmpty(string suffix)
    {
        var leftover = suffix == ".bak" ? EntryStorage.BackupPath(FilePath) : FilePath + suffix;
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        File.WriteAllText(leftover, "{}");
        Assert.Throws<InvalidDataException>(() => EntryStorage.Load(FilePath));
        if (suffix == ".bak")
            Assert.Throws<InvalidDataException>(() => EntryStorage.Save(FilePath, Sample()));
    }

    [Fact]
    public void Save_RotatesOneBackupLevel_AndLeavesNoTmp()
    {
        EntryStorage.Save(FilePath, Sample().Take(1));
        EntryStorage.Save(FilePath, Sample().Take(2));
        EntryStorage.Save(FilePath, Sample());

        Assert.Equal(2, EntryStorage.Load(EntryStorage.BackupPath(FilePath)).Count);
        Assert.False(File.Exists(FilePath + ".bak"));
        Assert.Equal(5, EntryStorage.Load(FilePath).Count);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    public static TheoryData<Entry?> InvalidEntries() =>
    [
        null,
        new Entry(" ", "T", new FixedDate(1, 1)),
        new Entry("X", "", new FixedDate(1, 1)),
        new Entry("X", "T", new FixedDate(1, 1)) { Id = Guid.Empty },
        new Entry("X", "T", new FixedDate(1, 1)) { Tags = ["a", " "] },
        new Entry("X", "T", new FixedDate(1, 1)) { Reminders = (Reminder)(1 << 13) },
    ];

    [Theory]
    [MemberData(nameof(InvalidEntries))]
    public void Save_InvalidEntry_Throws_AndLeavesFilesUntouched(Entry? bad)
    {
        EntryStorage.Save(FilePath, Sample().Take(1));
        EntryStorage.Save(FilePath, Sample());
        var main = File.ReadAllBytes(FilePath);
        var bak = File.ReadAllBytes(EntryStorage.BackupPath(FilePath));

        Assert.Throws<InvalidDataException>(() => EntryStorage.Save(FilePath, [.. Sample(), bad!]));

        Assert.Equal(main, File.ReadAllBytes(FilePath));
        Assert.Equal(bak, File.ReadAllBytes(EntryStorage.BackupPath(FilePath)));
    }

    [Fact]
    public void Save_DuplicateId_Throws()
    {
        var e = Sample()[0];
        Assert.Throws<InvalidDataException>(() => EntryStorage.Save(FilePath, [e, e with { Name = "Copy" }]));
    }

    [Fact]
    public void Save_WhileFileLocked_KeepsOriginal_AndCleansTmp()
    {
        EntryStorage.Save(FilePath, Sample().Take(1));
        using (File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => EntryStorage.Save(FilePath, Sample()));

        Assert.Single(EntryStorage.Load(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_DoesNotBlockConcurrentSave()
    {
        EntryStorage.Save(FilePath, Sample().Take(1));
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            EntryStorage.Save(FilePath, Sample());
        Assert.Equal(5, EntryStorage.Load(FilePath).Count);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "formatVersion": 4, "entries": [] }""")]
    [InlineData($$"""{ "formatVersion": 3, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "customFields": { "Телефон": "1" } } ] }""")]
    [InlineData($$"""{ "formatVersion": 3, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "customFields": { "Город": " " } } ] }""")]
    [InlineData($$"""{ "formatVersion": 3, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "customFields": { "Город": null } } ] }""")]
    [InlineData($$"""{ "formatVersion": 3, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "customFields": { "Город": "a", "город": "b" } } ] }""")]
    [InlineData("""{ "formatVersion": 0, "entries": [] }""")]
    [InlineData($$"""{ "formatVersion": 2, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "color": "red" } ] }""")]
    [InlineData("""{ "formatVersion": 1 }""")]
    [InlineData("""{ "formatVersion": 1, "entries": [], }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": { "kind": "fixed", "month": 13, "day": 1 } } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": " ", "eventType": "T", "date": {{Easter}} } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T" } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "reminders": 16384 } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": { "kind": "moon" } } ] }""")]
    // Missing ctor fields must not silently become 0 / Sunday.
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": { "kind": "easter" } } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": { "kind": "nthWeekday", "month": 5, "nth": 2 } } ] }""")]
    // Identity.
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "name": "X", "eventType": "T", "date": {{Easter}} } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "00000000-0000-0000-0000-000000000000", "name": "X", "eventType": "T", "date": {{Easter}} } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}} }, { "id": "{{Id1}}", "name": "Y", "eventType": "T", "date": {{Easter}} } ] }""")]
    // Hand-edit mistakes that would otherwise be dropped on the next save.
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "phnoe": "1" } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "Phone": "1" } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "name": "Y", "eventType": "T", "date": {{Easter}} } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "tags": null } ] }""")]
    [InlineData($$"""{ "formatVersion": 1, "entries": [ { "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}}, "tags": ["a", null] } ] }""")]
    public void InvalidFile_Throws_AndIsNotModified(string content)
    {
        File.WriteAllText(FilePath, content);
        Assert.Throws<InvalidDataException>(() => EntryStorage.Load(FilePath));
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void NewerFormat_ReportsVersion_NotUnknownKind()
    {
        File.WriteAllText(FilePath, """{ "formatVersion": 4, "entries": [ { "date": { "kind": "lunar" } } ] }""");
        var e = Assert.Throws<InvalidDataException>(() => EntryStorage.Load(FilePath));
        Assert.Contains("формате 4", e.Message);
    }

    [Fact]
    public void Version1File_StillLoads_AndIsSavedAsCurrentWithColor()
    {
        File.WriteAllText(FilePath, OneEntry($$""" "id": "{{Id1}}", "name": "X", "eventType": "T", "date": {{Easter}} """));
        var entries = EntryStorage.Load(FilePath);
        Assert.Null(entries[0].Color);

        EntryStorage.Save(FilePath, [entries[0] with { Color = "#FFCC00" }]);
        Assert.Contains($"\"formatVersion\": {EntryStorage.FormatVersion}", File.ReadAllText(FilePath));
        Assert.Equal("#FFCC00", EntryStorage.Load(FilePath)[0].Color);
    }

    [Fact]
    public void HandEditedFile_DiscriminatorNotFirst_WithBom_Loads()
    {
        var json = OneEntry($$""" "id": "{{Id1}}", "name": "X", "eventType": "T", "date": { "month": 5, "day": 1, "kind": "fixed" } """);
        File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var entry = EntryStorage.Load(FilePath)[0];
        Assert.Equal(new FixedDate(5, 1), entry.Date);
        Assert.Equal(Guid.Parse(Id1), entry.Id);
    }
}
