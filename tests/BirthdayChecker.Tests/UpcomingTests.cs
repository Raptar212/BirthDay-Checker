using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class UpcomingTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static Entry Birthday(int m, int d, int? y = null, Reminder r = Reminder.None) =>
        new("Test Person", "Birthday", new FixedDate(m, d, y)) { Reminders = r };

    [Fact]
    public void EventDay_IsAlwaysDue_WithAge()
    {
        var e = Birthday(6, 10, 1990);
        Assert.True(Upcoming.IsDue(e, D(2026, 6, 10)));
        var next = Upcoming.Next(e, D(2026, 6, 10));
        Assert.Equal(0, next.DaysLeft);
        Assert.Equal(36, next.Years);
    }

    [Fact]
    public void UnknownYear_HasNoAge() =>
        Assert.Null(Upcoming.Next(Birthday(6, 10), D(2026, 1, 1)).Years);

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(6, false)]
    [InlineData(5, false)]
    public void ConsecutiveDayReminders_FireEachDay(int day, bool due)
    {
        var e = Birthday(6, 10, r: Reminder.Day3 | Reminder.Day2 | Reminder.Day1);
        Assert.Equal(due, Upcoming.IsDue(e, D(2026, 6, day)));
    }

    [Theory]
    [InlineData(5, 26, false)] // the day before the earliest offset
    [InlineData(5, 27, true)]  // Week2 exactly
    [InlineData(6, 1, true)]   // between offsets: not due, but in reach
    [InlineData(6, 10, true)]  // the event itself
    [InlineData(6, 11, false)] // next occurrence is a year away
    public void InReach_FromEarliestOffsetUntilEvent(int month, int day, bool expected)
    {
        var e = Birthday(6, 10, r: Reminder.Week1 | Reminder.Week2);
        Assert.Equal(expected, Upcoming.IsInReach(e, D(2026, month, day)));
    }

    [Fact]
    public void InReach_WithoutReminders_OnlyEventDay()
    {
        Assert.True(Upcoming.IsInReach(Birthday(6, 10), D(2026, 6, 10)));
        Assert.False(Upcoming.IsInReach(Birthday(6, 10), D(2026, 6, 9)));
    }

    [Fact]
    public void InReach_CrossesNewYear() =>
        Assert.True(Upcoming.IsInReach(Birthday(1, 5, r: Reminder.Week2), D(2026, 12, 30)));

    [Fact]
    public void InReach_ListsNearestFirst_AndGoesBeyondDue()
    {
        var far = Birthday(6, 10, r: Reminder.Week2);
        var near = Birthday(6, 5, r: Reminder.Week1);
        var outOfReach = Birthday(6, 20, r: Reminder.Day1);
        var today = D(2026, 6, 1);

        Assert.Empty(Upcoming.Due([far, near, outOfReach], today));
        Assert.Equal([near, far], Upcoming.InReach([far, near, outOfReach], today).Select(o => o.Entry));
    }

    [Fact]
    public void WeekAndMonthReminders()
    {
        Assert.True(Upcoming.IsDue(Birthday(6, 10, r: Reminder.Week1), D(2026, 6, 3)));
        Assert.True(Upcoming.IsDue(Birthday(3, 31, r: Reminder.Month1), D(2026, 2, 28)));
        Assert.False(Upcoming.IsDue(Birthday(6, 10), D(2026, 6, 3)));
    }

    [Fact]
    public void Reminder_AcrossNewYear()
    {
        var e = Birthday(1, 2, 2000, Reminder.Day2);
        Assert.True(Upcoming.IsDue(e, D(2026, 12, 31)));
        var next = Upcoming.Next(e, D(2026, 12, 31));
        Assert.Equal(2, next.DaysLeft);
        Assert.Equal(27, next.Years);
    }

    [Fact]
    public void Feb29Birthday_NonLeapAndLeapYears()
    {
        var e = Birthday(2, 29, 2000, Reminder.Day1);
        var next = Upcoming.Next(e, D(2026, 2, 27));
        Assert.Equal(D(2026, 2, 28), next.Date);
        Assert.Equal(26, next.Years);
        Assert.True(Upcoming.IsDue(e, D(2026, 2, 27)));
        Assert.True(Upcoming.IsDue(e, D(2028, 2, 28)));
        Assert.Equal(D(2029, 2, 28), Upcoming.Next(e, D(2028, 3, 1)).Date);
    }

    [Theory]
    [InlineData(3, 31, Reminder.Month1, 2028, 2, 29)] // leap February
    [InlineData(3, 30, Reminder.Month1, 2026, 2, 28)] // clamps onto the same day as Mar 31
    [InlineData(1, 31, Reminder.Month1, 2026, 12, 31)]
    [InlineData(2, 1, Reminder.Month3, 2026, 11, 1)]  // across the year boundary
    public void MonthReminders_EdgeDays(int m, int d, Reminder r, int ty, int tm, int td) =>
        Assert.True(Upcoming.IsDue(Birthday(m, d, r: r), D(ty, tm, td)));

    [Fact]
    public void EasterBasedEntries()
    {
        var trinity = new Entry("Trinity", "Holiday", new EasterDate(49)) { Reminders = Reminder.Day1 };
        Assert.True(Upcoming.IsDue(trinity, D(2026, 5, 30)));
        Assert.Equal(D(2027, 6, 20), Upcoming.Next(trinity, D(2026, 6, 1)).Date); // Easter 2027 is May 2

        var lentStart = new EasterDate(-48); // Clean Monday 2026: Apr 12 - 48 days
        Assert.Equal(D(2026, 2, 23), lentStart.OccurrenceIn(2026));
    }

    [Fact]
    public void FutureBirthYear_HasNoAge() =>
        Assert.Null(Upcoming.Next(Birthday(5, 1, 2030), D(2026, 1, 1)).Years);

    [Fact]
    public void MissedReminderDay_IsCaughtUp_WhileEventIsAhead()
    {
        var e = Birthday(6, 10, r: Reminder.Day3); // reminder day: June 7
        Assert.False(Upcoming.IsDue(e, D(2026, 6, 9)));                          // no catch-up info: exact day only
        Assert.True(Upcoming.IsDue(e, D(2026, 6, 9), lastChecked: D(2026, 6, 5))); // PC was off on June 7
        Assert.False(Upcoming.IsDue(e, D(2026, 6, 9), lastChecked: D(2026, 6, 7))); // June 7 was already checked
        Assert.False(Upcoming.IsDue(e, D(2026, 6, 9), lastChecked: D(2026, 6, 9))); // later check the same day
    }

    [Fact]
    public void MissedEventDay_IsNotCaughtUp()
    {
        var e = Birthday(6, 10, r: Reminder.Day1);
        Assert.False(Upcoming.IsDue(e, D(2026, 6, 11), lastChecked: D(2026, 6, 1)));
    }

    [Fact]
    public void CatchUp_IsCappedAndIgnoresFutureLastChecked()
    {
        var e = Birthday(6, 10, r: Reminder.Month3); // reminder day: March 10
        Assert.True(Upcoming.IsDue(e, D(2026, 3, 12), lastChecked: D(2020, 1, 1)));  // ancient last check is capped, still caught
        Assert.False(Upcoming.IsDue(e, D(2026, 3, 12), lastChecked: D(2026, 12, 1))); // clock moved back: exact day only
    }

    [Fact]
    public void Due_FiltersAndSortsNearestFirst()
    {
        var today = D(2026, 6, 8);
        var inTwo = Birthday(6, 10, r: Reminder.Day2);
        var todayEvent = Birthday(6, 8);
        var notDue = Birthday(6, 20);

        var due = Upcoming.Due([inTwo, notDue, todayEvent], today);

        Assert.Equal([todayEvent, inTwo], due.Select(o => o.Entry));
    }
}
