using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class EventDateTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2021, 5, 2)]
    [InlineData(2023, 4, 16)]
    [InlineData(2024, 5, 5)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    [InlineData(2027, 5, 2)]
    public void OrthodoxEaster_KnownYears(int year, int month, int day) =>
        Assert.Equal(D(year, month, day), EasterDate.OrthodoxEaster(year));

    [Fact]
    public void Easter_Offsets_RadonitsaAndTrinity()
    {
        Assert.Equal(D(2026, 4, 21), new EasterDate(9).OccurrenceIn(2026));
        Assert.Equal(D(2026, 5, 31), new EasterDate(49).OccurrenceIn(2026));
    }

    [Theory]
    [InlineData(5, DayOfWeek.Sunday, 2, 2026, 5, 10)]                     // 2nd Sunday of May
    [InlineData(11, DayOfWeek.Sunday, NthWeekdayDate.Last, 2026, 11, 29)] // last Sunday of November
    [InlineData(9, DayOfWeek.Monday, 1, 2025, 9, 1)]                      // month starts on that weekday
    [InlineData(10, DayOfWeek.Friday, NthWeekdayDate.Last, 2025, 10, 31)] // month ends on that weekday
    public void NthWeekday(int month, DayOfWeek weekday, int nth, int year, int em, int ed) =>
        Assert.Equal(D(year, em, ed), new NthWeekdayDate(month, weekday, nth).OccurrenceIn(year));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-2)]
    public void NthWeekday_RejectsBadNth(int nth) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NthWeekdayDate(5, DayOfWeek.Sunday, nth));

    [Fact]
    public void NthWeekday_RejectsUndefinedWeekday() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NthWeekdayDate(5, (DayOfWeek)9, 2));

    [Theory]
    [InlineData(91)]
    [InlineData(-91)]
    [InlineData(int.MinValue)] // Math.Abs would overflow
    [InlineData(int.MaxValue)]
    public void Easter_RejectsOffsetsLeavingTheYear(int offset) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new EasterDate(offset));

    [Fact]
    public void Fixed_Feb29_FallsOnFeb28InNonLeapYears()
    {
        var leapDay = new FixedDate(2, 29);
        Assert.Equal(D(2026, 2, 28), leapDay.OccurrenceIn(2026));
        Assert.Equal(D(2028, 2, 29), leapDay.OccurrenceIn(2028));
    }

    [Theory]
    [InlineData(13, 1, null)]
    [InlineData(2, 30, null)]
    [InlineData(2, 29, 2023)]
    public void Fixed_RejectsImpossibleDates(int month, int day, int? year) =>
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new FixedDate(month, day, year));

    [Fact]
    public void NextOccurrence_TodayCountsAsNext_PastMovesToNextYear()
    {
        var date = new FixedDate(3, 15);
        Assert.Equal(D(2026, 3, 15), date.NextOccurrence(D(2026, 3, 15)));
        Assert.Equal(D(2027, 3, 15), date.NextOccurrence(D(2026, 3, 16)));
    }
}
