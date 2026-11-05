using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class DailyReminderTests
{
    private static readonly TimeOnly At = new(9, 0);
    private static readonly DateOnly Today = new(2026, 6, 10);
    private static readonly TimeSpan ThreeHours = TimeSpan.FromHours(3);
    private static readonly TrayState CheckedToday = new() { LastChecked = Today };

    private static DateTime T(int h, int m = 0, int day = 10) => new(2026, 6, day, h, m, 0);

    private static Entry Birthday(int m, int d, Reminder r = Reminder.None) =>
        new("Test Person", "Birthday", new FixedDate(m, d, null)) { Reminders = r };

    [Fact]
    public void BeforeTime_ShowsNothing_AndKeepsState()
    {
        var state = new TrayState { LastChecked = Today.AddDays(-1) };
        var (due, after) = DailyReminder.Check([Birthday(6, 10)], state, T(8, 59), At);
        Assert.Empty(due);
        Assert.Same(state, after);
    }

    [Fact]
    public void AfterTime_ShowsDue_AndStartsDay()
    {
        var entry = Birthday(6, 10);
        var (due, state) = DailyReminder.Check([entry, Birthday(7, 1)], new TrayState(), T(9), At);
        Assert.Equal(entry, Assert.Single(due).Entry);
        Assert.Equal(Today, state.LastChecked);
    }

    [Fact]
    public void Dismissed_AndSnoozed_AreHidden()
    {
        var dismissed = Birthday(6, 10);
        var snoozed = Birthday(6, 11, Reminder.Day1);
        var state = CheckedToday.Dismiss(dismissed.Id, Today).Snooze(snoozed.Id, T(11), T(10));
        Assert.Empty(DailyReminder.Check([dismissed, snoozed], state, T(10, 30), At).Due);
        Assert.Equal(snoozed, Assert.Single(DailyReminder.Check([dismissed, snoozed], state, T(11, 1), At).Due).Entry);
    }

    [Fact]
    public void CheckNow_BeforeTime_ShowsInReach_IgnoresSnooze_KeepsNotToday()
    {
        var today = Birthday(6, 10);
        var soon = Birthday(6, 15, Reminder.Week1); // in reach, but not due exactly today
        var dismissed = Birthday(6, 12, Reminder.Day5);
        var far = Birthday(8, 1, Reminder.Day1);
        var state = new TrayState { LastChecked = Today.AddDays(-1) }
            .Snooze(today.Id, T(9), T(7)).Dismiss(dismissed.Id, Today);
        var (due, after) = DailyReminder.CheckNow([today, soon, dismissed, far], state, T(8));
        Assert.Equal([today, soon], due.Select(o => o.Entry));
        Assert.Equal(Today, after.LastChecked);
        Assert.Equal(Today.AddDays(-1), after.CatchUpSince);
    }

    [Fact]
    public void Next_BeforeTime_IsTheTime() =>
        Assert.Equal(T(9), DailyReminder.Next(new TrayState(), T(7), At, ThreeHours));

    [Fact]
    public void Next_MissedToday_IsNow() =>
        Assert.Equal(T(14, 20), DailyReminder.Next(new TrayState { LastChecked = Today.AddDays(-2) }, T(14, 20), At, ThreeHours));

    [Theory]
    [InlineData(9, 0, 12)]
    [InlineData(11, 59, 12)]
    [InlineData(12, 0, 15)]
    [InlineData(20, 5, 21)]
    public void Next_CheckedToday_FollowsGridFromTime(int h, int m, int expected) =>
        Assert.Equal(T(expected), DailyReminder.Next(CheckedToday, T(h, m), At, ThreeHours));

    [Fact]
    public void Next_PastLastStep_IsTomorrowsTime() =>
        Assert.Equal(T(9, day: 11), DailyReminder.Next(CheckedToday, T(21, 30), At, ThreeHours));

    [Fact]
    public void Next_OnceADay_IsTomorrowsTime() =>
        Assert.Equal(T(9, day: 11), DailyReminder.Next(CheckedToday, T(9, 1), At, TimeSpan.Zero));

    [Fact]
    public void Next_EarlierSnoozeEnd_Wins()
    {
        var state = CheckedToday.Snooze(Guid.NewGuid(), T(10, 15), T(9, 15));
        Assert.Equal(T(10, 15), DailyReminder.Next(state, T(9, 15), At, ThreeHours));
    }

    [Fact]
    public void EndedSnooze_NotYetChecked_IsRightAway_ThenDropped()
    {
        var entry = Birthday(6, 10);
        var state = CheckedToday.Snooze(entry.Id, T(10, 15), T(9, 15));
        Assert.Equal(T(10, 30), DailyReminder.Next(state, T(10, 30), At, TimeSpan.Zero)); // not tomorrow

        var (due, after) = DailyReminder.Check([entry], state, T(10, 30), At);
        Assert.Single(due);
        Assert.Empty(after.Snoozed);
        Assert.Equal(T(12), DailyReminder.Next(after, T(10, 30), At, ThreeHours));
    }

    [Fact]
    public void EndedSnooze_IsDropped_EvenBeforeTime()
    {
        // A snooze from late last night ends before the set time: dropped, so Next doesn't loop on it.
        var state = new TrayState { LastChecked = Today.AddDays(-1) }.Snooze(Guid.NewGuid(), T(0, 30), T(23, 30, day: 9));
        var after = DailyReminder.Check([], state, T(1), At).State;
        Assert.Empty(after.Snoozed);
        Assert.Null(after.CatchUpSince);
        Assert.Equal(T(9), DailyReminder.Next(after, T(1), At, ThreeHours));
    }
}
