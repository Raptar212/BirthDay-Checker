using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class EntryToolsTests
{
    private static Entry Sample(string name = "Иван Тестов") =>
        new(name, "День Рождения", new FixedDate(5, 14, 1990))
        {
            Phone = "+7 000", Comment = "любит; \"чай\"\nи торт", Tags = ["семья", "друзья"],
            Reminders = Reminder.Day1 | Reminder.Week1, Color = "#FFCC00",
        };

    [Fact]
    public void GroupEdit_ChangesOnlyRequestedParts()
    {
        var e = Sample();
        var changed = new GroupEdit { AddTags = ["коллеги", "СЕМЬЯ"], RemoveTags = ["друзья"] }.Apply(e);
        Assert.Equal(["семья", "коллеги"], changed.Tags);
        Assert.Equal((e.Reminders, e.Color, e.Id), (changed.Reminders, changed.Color, changed.Id));

        var recolored = new GroupEdit { Reminders = Reminder.Month1, ChangeColor = true }.Apply(e);
        Assert.Equal((Reminder.Month1, (string?)null), (recolored.Reminders, recolored.Color));
    }

    [Fact]
    public void GroupEdit_EventTypePhoneComment_OnlyWhenAsked()
    {
        var e = Sample();
        Assert.Equal(e, new GroupEdit().Apply(e));

        var changed = new GroupEdit { EventType = "Праздник", ChangePhone = true, Phone = "+7 111", ChangeComment = true, Comment = "новый" }.Apply(e);
        Assert.Equal(("Праздник", "+7 111", "новый"), (changed.EventType, changed.Phone, changed.Comment));
        Assert.Equal((e.Name, e.Date, e.Id), (changed.Name, changed.Date, changed.Id));

        var cleared = new GroupEdit { ChangePhone = true, ChangeComment = true }.Apply(e);
        Assert.Equal(((string?)null, (string?)null, e.EventType), (cleared.Phone, cleared.Comment, cleared.EventType));
    }

    [Fact]
    public void GroupEdit_NoRealChange_GivesAnEqualEntry()
    {
        var e = Sample();
        Assert.Equal(e, new GroupEdit { AddTags = ["СЕМЬЯ"], RemoveTags = ["коллеги"], EventType = e.EventType }.Apply(e));
        Assert.NotEqual(e, new GroupEdit { RemoveTags = ["семья"] }.Apply(e));
    }

    [Theory]
    [InlineData(Reminder.None, "нет")]
    [InlineData(Reminder.Day1 | Reminder.Day2 | Reminder.Day5, "1, 2, 5 дн.")]
    [InlineData(Reminder.Day1 | Reminder.Week2 | Reminder.Month3, "1 дн.; 2 нед.; 3 мес.")]
    public void ShortReminders(Reminder r, string expected) =>
        Assert.Equal(expected, ReminderText.ShortReminders(r));

    [Fact]
    public void Report_ListsInGivenOrder_WithAgeAndPhone()
    {
        var today = new DateOnly(2026, 5, 12);
        var text = Report.Build([Upcoming.Next(Sample(), today)], today);
        Assert.StartsWith("Список грядущих праздников на 12.05.2026", text);
        Assert.Contains("14.05.2026  Через 2 дня", text);
        Assert.Contains("Иван Тестов — День Рождения, 36 лет, тел. +7 000", text);
    }

    [Fact]
    public void Csv_RoundTripsEveryKindAndField()
    {
        List<Entry> entries =
        [
            Sample(),
            new("Mother's Day", "Праздник", new NthWeekdayDate(11, DayOfWeek.Sunday, NthWeekdayDate.Last)),
            new("Троица", "Праздник", new EasterDate(49)) { Tags = ["религиозный"] },
            new("Clean Monday", "Праздник", new EasterDate(-48)),
            new("  Spaces  ", "Именины", new FixedDate(2, 29)) { Comment = " lead" },
        ];
        var result = EntryCsv.Parse(EntryCsv.Export(entries));
        Assert.Empty(result.Issues);
        // Names and comments are trimmed on import, like everywhere else.
        Assert.Equivalent(entries.Select(e => e with { Name = e.Name.Trim(), Comment = e.Comment?.Trim() }), result.Entries, strict: true);
    }

    [Fact]
    public void Csv_ExcelResave_WithBomAndReorderedColumns_Imports()
    {
        var csv = "﻿Имя;Тип даты;Событие;Месяц;День;Год;День недели;Номер;Смещение от Пасхи;Телефон;Комментарий;Теги;Напомнить;Цвет\r\n" +
                  "Пётр;число;День Рождения;3;8;;;;;;;семья, дача;Day1, Day3;\r\n";
        var e = Assert.Single(EntryCsv.Parse(csv).Entries);
        Assert.Equal(("Пётр", (EventDate)new FixedDate(3, 8), Reminder.Day1 | Reminder.Day3), (e.Name, e.Date, e.Reminders));
        Assert.Equal(["семья", "дача"], e.Tags);
        Assert.NotEqual(Guid.Empty, e.Id); // no Id column: a new one
    }

    [Theory]
    [InlineData("A;число;Т;2;30;;", "несуществующая")]          // Feb 30
    [InlineData("A;луна;Т;;;;", "тип даты")]
    [InlineData(";число;Т;1;1;;", "пустое имя")]
    [InlineData("A;число;Т;x;1;;", "не число")]
    public void Csv_BadRow_SkippedWithIssue_OthersImported(string bad, string message)
    {
        const string header = "Имя;Тип даты;Событие;Месяц;День;Год;День недели;Номер;Смещение от Пасхи;Телефон;Комментарий;Теги;Напомнить;Цвет";
        var r = EntryCsv.Parse($"{header}\n{bad}\nB;пасха;Т;;;;;;9;;;;;\n");
        Assert.Equal("B", Assert.Single(r.Entries).Name);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(2, issue.Line);
        Assert.Contains(message, issue.Message);
    }

    [Theory]
    [InlineData("Day1, 4096")]   // numbers are not accepted as flags
    [InlineData("Daily")]
    public void Csv_UnknownReminder_IsAnIssue(string reminders)
    {
        var exported = EntryCsv.Export([Sample()]);
        Assert.Contains(";Day1, Week1;", exported); // no ";" inside, so the field is not quoted
        var csv = exported.Replace(";Day1, Week1;", $";{reminders};");
        Assert.Contains("Напомнить", Assert.Single(EntryCsv.Parse(csv).Issues).Message);
    }

    [Fact]
    public void Csv_NotOurFile_FailsAsAWhole() =>
        Assert.Contains("нет колонок", Assert.Single(EntryCsv.Parse("a;b;c\n1;2;3\n").Issues).Message);

    [Fact]
    public void Merge_AddsNew_SkipsDuplicates_KeepsIds()
    {
        var existing = Sample();
        var sameByNameAndDate = new Entry("иван тестов", "день рождения", existing.Date) { Comment = "from csv" };
        var sameId = existing with { Comment = "changed elsewhere" };
        var fresh = new Entry("Новый", "Праздник", new FixedDate(1, 1));

        Assert.Equal([fresh], EntryCsv.Unique([existing], [sameId, fresh]));
        Assert.Equal([fresh], EntryCsv.Unique([existing], [fresh, sameByNameAndDate]));

        var merged = EntryCsv.Merge([existing], [sameByNameAndDate, fresh]);
        Assert.Equal([existing, fresh], merged.Entries);
        Assert.Equal((1, 1), (merged.Added, merged.Skipped));
    }

    [Fact]
    public void Unique_SameNameAndDate_DifferentEvent_IsNew()
    {
        // A birthday and a name day of the same person on the same day are two entries.
        var birthday = new Entry("Иван", "День Рождения", new FixedDate(1, 7));
        var nameDay = new Entry("Иван", "Именины", new FixedDate(1, 7));
        Assert.Equal([nameDay], EntryCsv.Unique([birthday], [nameDay]));
    }

    [Fact]
    public void Merge_IncomingIdCollision_GetsNewId()
    {
        var existing = Sample();
        var other = new Entry("Другой", "Праздник", new FixedDate(2, 2)) { Id = existing.Id }; // same id, but matched first by id…
        var third = new Entry("Третий", "Праздник", new FixedDate(3, 3)) { Id = existing.Id }; // …so this one is new, id taken
        var merged = EntryCsv.Merge([existing], [other, third]);
        Assert.Equal(2, merged.Entries.Count);
        Assert.Equal("Третий", merged.Entries[1].Name);
        EntryStorage_AcceptsIds(merged.Entries);
    }

    [Fact]
    public void Unique_IdMatchWins_EvenWhenANameTwinComesFirst()
    {
        var a = new Entry("Анна", "День Рождения", new FixedDate(3, 3));
        var newTwin = new Entry("Анна", "День Рождения", new FixedDate(3, 3)) { Comment = "new row" }; // fresh id
        var aRenamed = a with { Name = "Анна Петрова" };                                                  // a's own row, renamed

        Assert.Equal([newTwin], EntryCsv.Unique([a], [newTwin, aRenamed]));
    }

    [Fact]
    public void Merge_OnListChangedAfterPick_SkipsWhatAppearedMeanwhile()
    {
        var existing = Sample();
        var fresh = new Entry("Новый", "Праздник", new FixedDate(1, 1));
        var picked = EntryCsv.Unique([existing], [fresh]);
        var meanwhile = new Entry("новый", "праздник", fresh.Date); // added in the editor before "Добавить"

        var merged = EntryCsv.Merge([existing, meanwhile], picked);

        Assert.Equal([existing, meanwhile], merged.Entries);
        Assert.Equal((0, 1), (merged.Added, merged.Skipped));
    }

    [Fact]
    public void Unique_TwoRowsMatchingOneEntry_SecondIsNew()
    {
        var existing = Sample();
        var twin = new Entry(existing.Name, existing.EventType, existing.Date);
        Assert.Equal([twin], EntryCsv.Unique([existing], [existing, twin]));
    }

    private static void EntryStorage_AcceptsIds(IReadOnlyList<Entry> entries) =>
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());

    private const string CsvHeader = "Имя;Тип даты;Событие;Месяц;День;Год;День недели;Номер;Смещение от Пасхи;Телефон;Комментарий;Теги;Напомнить;Цвет";

    [Fact]
    public void Csv_DuplicateRowsInFile_SecondSkippedWithIssue()
    {
        var r = EntryCsv.Parse($"{CsvHeader}\nA;число;Т;1;1;;;;;;;;;\nA;число;Т;1;1;;;;;;;;;\nA;число;Другое;1;1;;;;;;;;;\n");
        Assert.Equal(2, r.Entries.Count);
        Assert.Contains("дубль строки 2", Assert.Single(r.Issues).Message);
    }

    [Fact]
    public void Csv_UnclosedQuote_FailsWholeFile()
    {
        var r = EntryCsv.Parse($"{CsvHeader}\nA;число;Т;1;1;;;;;;\"oops;;;\nB;число;Т;2;2;;;;;;;;;\n");
        Assert.Empty(r.Entries);
        Assert.Contains("незакрытая кавычка", Assert.Single(r.Issues).Message);
    }

    [Fact]
    public void Csv_PhonesSurviveExcel_FormulaLikeTextIsNeutralized()
    {
        var e = new Entry("=HYPERLINK(\"x\")", "+праздник", new FixedDate(1, 1)) { Phone = "+79161234567", Comment = "-минус" };
        var csv = EntryCsv.Export([e]);
        Assert.Contains(";\"=\"\"+79161234567\"\"\";", csv); // ="+7..." , RFC-quoted because it contains quotes
        Assert.Contains("'=HYPERLINK", csv);
        Assert.Equivalent(e, Assert.Single(EntryCsv.Parse(csv).Entries), strict: true);

        // What Excel writes back for a number cell: plain digits are fine, scientific notation is refused.
        Assert.Equal("89161234567", Assert.Single(EntryCsv.Parse($"{CsvHeader}\nA;число;Т;1;1;;;;;89161234567;;;;\n").Entries).Phone);
        Assert.Contains("научной записи", Assert.Single(EntryCsv.Parse($"{CsvHeader}\nA;число;Т;1;1;;;;;7,92E+10;;;;\n").Issues).Message);
    }

    [Fact]
    public void Csv_Cp1251AndSepLine_AreRead()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var bytes = System.Text.Encoding.GetEncoding(1251).GetBytes($"sep=;\r\n\r\n{CsvHeader}\r\nПётр;число;Т;1;1;;;;;;;;;\r\n");
        Assert.Equal("Пётр", Assert.Single(EntryCsv.Parse(EntryCsv.Decode(bytes)).Entries).Name);
    }
}
