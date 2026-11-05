using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class EntryListTests
{
    private static readonly DateOnly Today = new(2026, 6, 8);

    private static readonly List<Entry> Entries =
    [
        new("Иван Тестов", "День Рождения", new FixedDate(6, 10, 1990)) { Tags = ["семья"], Phone = "+7 111" },
        new("Анна", "Именины", new FixedDate(1, 5)) { Comment = "Купить цветы" },
        new("Trinity", "Праздник", new EasterDate(49)) { Tags = ["религиозный"] }, // 2026-05-31 passed → 2027-06-20
        new("Сегодняшний", "Праздник", new FixedDate(6, 8)),
    ];

    private static string[] Names(ListFilter f) => EntryList.Filter(Entries, Today, f).Select(o => o.Entry.Name).ToArray();

    private static string[] NamesInYear(int year, ListFilter f) =>
        EntryList.InYear(Entries, year, Today, f).Select(o => o.Entry.Name).ToArray();

    [Fact]
    public void InYear_ByDate_IncludingPast() =>
        Assert.Equal(["Анна", "Trinity", "Сегодняшний", "Иван Тестов"], NamesInYear(2026, new ListFilter()));

    [Fact]
    public void InYear_AppliesFilters_AndMonthOfThatYear()
    {
        Assert.Equal(["Trinity"], NamesInYear(2026, new ListFilter(Month: 5))); // Next would put it in June 2027
        Assert.Equal(["Иван Тестов"], NamesInYear(2026, new ListFilter(Tag: "семья")));
        Assert.Equal(["Анна"], NamesInYear(2026, new ListFilter(Search: "цветы")));
    }

    [Fact]
    public void InYear_SkipsYearsBeforeBirth() =>
        Assert.DoesNotContain("Иван Тестов", NamesInYear(1989, new ListFilter()));

    [Fact]
    public void InYear_PastDaysAreNegative_YearsCountToThatYear()
    {
        var o = Upcoming.InYear(Entries[0], 2025, Today)!;
        Assert.Equal((new DateOnly(2025, 6, 10), -363, 35), (o.Date, o.DaysLeft, o.Years!.Value));
        Assert.Equal(0, Upcoming.InYear(Entries[0], 1990, Today)!.Years);
        Assert.Null(Upcoming.InYear(Entries[1], 2025, Today)!.Years);
    }

    [Fact]
    public void InYear_FloatingDatesMoveWithTheYear()
    {
        var leap = new Entry("Високосный", "Праздник", new FixedDate(2, 29));
        Assert.Equal(new DateOnly(2027, 2, 28), Upcoming.InYear(leap, 2027, Today)!.Date);
        Assert.Equal(new DateOnly(2028, 2, 29), Upcoming.InYear(leap, 2028, Today)!.Date);

        var mothersDay = new Entry("День Матери", "Праздник", new NthWeekdayDate(5, DayOfWeek.Sunday, 2));
        Assert.Equal(new DateOnly(2026, 5, 10), Upcoming.InYear(mothersDay, 2026, Today)!.Date);
        Assert.Equal(new DateOnly(2027, 5, 9), Upcoming.InYear(mothersDay, 2027, Today)!.Date);

        Assert.Equal(new DateOnly(2026, 5, 31), Upcoming.InYear(Entries[2], 2026, Today)!.Date);
        Assert.Equal(new DateOnly(2027, 6, 20), Upcoming.InYear(Entries[2], 2027, Today)!.Date);
    }

    [Theory]
    [InlineData(-1, "1 день назад")]
    [InlineData(-3, "3 дня назад")]
    [InlineData(-17, "17 дней назад")]
    public void When_PastDays(int days, string expected) => Assert.Equal(expected, ReminderText.When(days));

    [Theory]
    [InlineData(-17, "17 дн. назад")]
    [InlineData(0, "0")]
    [InlineData(5, "5")]
    public void DaysColumn(int days, string expected) => Assert.Equal(expected, ReminderText.DaysColumn(days));

    [Fact]
    public void NoFilter_NearestFirst() =>
        Assert.Equal(["Сегодняшний", "Иван Тестов", "Анна", "Trinity"], Names(new ListFilter()));

    [Fact]
    public void Month_UsesNextOccurrence() =>
        Assert.Equal(["Сегодняшний", "Иван Тестов", "Trinity"], Names(new ListFilter(Month: 6)));

    [Theory]
    [InlineData("праздник", null, null, new[] { "Сегодняшний", "Trinity" })]       // type, any case
    [InlineData(null, "СЕМЬЯ", null, new[] { "Иван Тестов" })]                      // tag, any case
    [InlineData(null, null, "цвет", new[] { "Анна" })]                              // search in comment
    [InlineData(null, null, " 111 ", new[] { "Иван Тестов" })]                      // search in phone, trimmed
    [InlineData(null, null, "иван", new[] { "Иван Тестов" })]                       // search in name, any case
    public void Filters(string? type, string? tag, string? search, string[] expected) =>
        Assert.Equal(expected, Names(new ListFilter(EventType: type, Tag: tag, Search: search)));

    [Fact]
    public void ParseTags_TrimsSplitsDedupes() =>
        Assert.Equal(["семья", "друзья", "дача"], EntryList.ParseTags(" семья, друзья;; Семья ,дача "));

    [Theory]
    [MemberData(nameof(DateCases))]
    public void DescribeDate(EventDate date, string expected) => Assert.Equal(expected, ReminderText.DescribeDate(date));

    public static TheoryData<EventDate, string> DateCases() => new()
    {
        { new FixedDate(5, 14, 1990), "14.05.1990" },
        { new FixedDate(5, 4), "04.05" },
        { new NthWeekdayDate(5, DayOfWeek.Sunday, 2), "2-е воскресенье мая" },
        { new NthWeekdayDate(3, DayOfWeek.Wednesday, 1), "1-я среда марта" },
        { new NthWeekdayDate(9, DayOfWeek.Monday, 3), "3-й понедельник сентября" },
        { new NthWeekdayDate(11, DayOfWeek.Sunday, NthWeekdayDate.Last), "последнее воскресенье ноября" },
        { new NthWeekdayDate(7, DayOfWeek.Friday, NthWeekdayDate.Last), "последняя пятница июля" },
        { new EasterDate(0), "Пасха" },
        { new EasterDate(49), "Пасха + 49 дн." },
        { new EasterDate(-48), "Пасха − 48 дн." },
    };
}
