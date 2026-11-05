using System.Text;
using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

/// <remarks>All names and data are fictional.</remarks>
public class LegacyImportTests
{
    private const string DefaultFlags = "1110110000100"; // 1,2,3,5 days, 1 week, 1 month

    private static string Line(string date, string name, string type, string comment = "", string phone = "",
        string mail = "", string flags = DefaultFlags, string onceInYear = "False") =>
        string.Join(';', [date, name, type, comment, phone, mail, "", "",
            .. flags.Select(c => c == '1' ? "True" : "False"), "FF8000", onceInYear, "True"]);

    private static ImportResult Parse(params string[] lines) => LegacyImport.Parse(string.Join("\r\n", lines));

    [Fact]
    public void MapsFields_MonthFirstDate_AndThirteenFlags()
    {
        var r = Parse(Line("01.14.1990", "Иван Тестов", "День Рождения", "комментарий", "+7 000"));

        var e = Assert.Single(r.Entries);
        Assert.Equal(new FixedDate(1, 14, 1990), e.Date);
        Assert.Equal(("Иван Тестов", "День Рождения", "+7 000", "комментарий"), (e.Name, e.EventType, e.Phone, e.Comment));
        Assert.Equal(Reminder.Day1 | Reminder.Day2 | Reminder.Day3 | Reminder.Day5 | Reminder.Week1 | Reminder.Month1, e.Reminders);
        Assert.Empty(e.Tags);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Year0001_IsUnknown_Silently_Year1000_IsUnknownWithIssue()
    {
        var r = Parse(Line("06.22.0001", "A", "Именины"), Line("07.22.1000", "B", "Именины"));

        Assert.All(r.Entries, e => Assert.Null(((FixedDate)e.Date).Year));
        Assert.Equal(2, Assert.Single(r.Issues).Line);
    }

    [Fact]
    public void Whitespace_IsNormalized_SoTypesDoNotSplit()
    {
        var r = Parse(Line("01.01.0001", "  Иван   Тестов ", "Именины "), Line("02.01.0001", "B", "Именины"));

        Assert.Equal("Иван Тестов", r.Entries[0].Name);
        Assert.Single(r.Entries.Select(e => e.EventType).Distinct());
        Assert.Null(r.Entries[0].Comment);
        Assert.Null(r.Entries[0].Phone);
    }

    [Theory]
    [InlineData("2 воскресенье майя", "05.10.1999", 5, 2)]          // real legacy typo "майя"
    [InlineData("третье воскресенье июня", "06.21.0001", 6, 3)]
    [InlineData("последнее воскресенье ноября", "11.29.0001", 11, NthWeekdayDate.Last)]
    [InlineData("Отмечаем 1-е воскресенье марта", "03.05.0001", 3, 1)]
    public void HolidayFloatingComment_SameMonth_BecomesNthWeekday(string comment, string date, int month, int nth)
    {
        var r = Parse(Line(date, "Holiday", "Праздник", comment));

        Assert.Equal(new NthWeekdayDate(month, DayOfWeek.Sunday, nth), r.Entries[0].Date);
        Assert.Equal(comment, r.Entries[0].Comment);
        var issue = Assert.Single(r.Issues).Message;
        Assert.Contains("плавающ", issue);
        Assert.Contains($"было {date}", issue); // original date stays recoverable
    }

    [Theory]
    [InlineData("05.10.1990", "День Рождения", "познакомились в первое воскресенье мая")] // birthdays never convert
    [InlineData("06.10.0001", "Праздник", "2 воскресенье мая")]                           // month mismatch
    public void FloatingLookingComment_KeepsFixedDate_WithIssue(string date, string type, string comment)
    {
        var r = Parse(Line(date, "X", type, comment));

        Assert.IsType<FixedDate>(r.Entries[0].Date);
        Assert.Contains("оставлена фиксированная", Assert.Single(r.Issues).Message);
    }

    [Theory]
    [InlineData("12 воскресенье мая")]
    [InlineData("воскресенье мая")]
    [InlineData("купить подарок")]
    public void OtherComments_StayFixed_Silently(string comment)
    {
        var r = Parse(Line("05.10.0001", "X", "Праздник", comment));
        Assert.IsType<FixedDate>(r.Entries[0].Date);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void RecentYear_IsFlagged_ForAnyType_ButKept()
    {
        var r = Parse(Line("02.20.2022", "A", "День Рождения"), Line("08.30.2026", "B", "Именины"),
            Line("09.01.1990", "C", "Праздник"));

        Assert.Equal(2022, ((FixedDate)r.Entries[0].Date).Year);
        Assert.Equal(["A", "B"], r.Issues.Select(i => i.Name));
    }

    [Fact]
    public void FlagValues_AcceptOneZeroAndAnyCase()
    {
        var line = Line("01.01.0001", "X", "Праздник").Replace(";True;True;True;False;", ";1;true;TRUE;0;");
        var r = Parse(line);
        Assert.Equal(Reminder.Day1 | Reminder.Day2 | Reminder.Day3 | Reminder.Day5 | Reminder.Week1 | Reminder.Month1,
            r.Entries[0].Reminders);
    }

    [Fact]
    public void DuplicateCheck_IgnoresTypeCase()
    {
        var r = Parse(Line("06.22.0001", "A", "День Рождения"), Line("06.22.0001", "B", "день рождения"));
        Assert.Contains("дубль", Assert.Single(r.Issues).Message);
    }

    [Fact]
    public void SameDayAndType_FlaggedAsPossibleDuplicate_BothImported()
    {
        var r = Parse(Line("06.22.0001", "Alexey", "Именины"), Line("06.22.0001", "Alexei", "Именины"),
            Line("06.22.0001", "Other", "Праздник"));

        Assert.Equal(3, r.Entries.Count);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(("Alexei", 2), (issue.Name, issue.Line));
        Assert.Contains("Alexey", issue.Message);
    }

    [Fact]
    public void OnceInYear_IsReported_MailMovedToComment()
    {
        var r = Parse(Line("03.01.0001", "A", "Праздник", "note", mail: "a@example.com", onceInYear: "True"));

        Assert.Equal("note; Почта: a@example.com", r.Entries[0].Comment);
        Assert.Equal(2, r.Issues.Count);
    }

    [Theory]
    [InlineData("01.01.2000;OnlyName;Тип")]
    [InlineData("13.45.2000;X;Тип;;;;;;True;True;True;True;True;True;True;True;True;True;True;True;True;0;False;False")]
    [InlineData("2000-01-01;X;Тип;;;;;;True;True;True;True;True;True;True;True;True;True;True;True;True;0;False;False")]
    [InlineData("01.01.2000;;Тип;;;;;;True;True;True;True;True;True;True;True;True;True;True;True;True;0;False;False")]
    // ';' inside the comment shifts the flag block: must not import with shifted reminders.
    [InlineData("01.01.2000;X;Тип;a;b;;;;;True;True;True;True;True;True;True;True;True;True;True;True;True;0;False;False")]
    [InlineData("01.01.2000;X;Тип;;;;;;yes;True;True;True;True;True;True;True;True;True;True;True;True;0;False;False")]
    // Impossible date is rejected even when the comment would make it floating.
    [InlineData("02.30.0001;X;Праздник;2 воскресенье февраля;;;;;True;True;True;True;True;True;True;True;True;True;True;True;True;0;False;False")]
    public void BadLine_IsSkippedWithIssue_OthersStillImported(string bad)
    {
        var r = Parse(Line("01.02.0001", "Good", "Праздник"), bad, "", Line("01.03.0001", "Good2", "Праздник"));

        Assert.Equal(["Good", "Good2"], r.Entries.Select(e => e.Name));
        var issue = Assert.Single(r.Issues);
        Assert.Equal(2, issue.Line);
        Assert.StartsWith("пропущена", issue.Message);
    }

    [Fact]
    public void ReadFile_DecodesCp1251_AndResultSavesThroughStrictStorage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dir = Directory.CreateTempSubdirectory("bdc-import-").FullName;
        try
        {
            var legacy = Path.Combine(dir, "Import.txt");
            File.WriteAllText(legacy, Line("09.20.1976", "Пётр Тестов", "День Рождения", "2 воскресенье мая"),
                Encoding.GetEncoding(1251));

            var r = LegacyImport.ReadFile(legacy);
            Assert.Equal("Пётр Тестов", r.Entries[0].Name);

            var json = Path.Combine(dir, "entries.json");
            EntryStorage.Save(json, r.Entries);
            Assert.Equivalent(r.Entries, EntryStorage.Load(json), strict: true);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
