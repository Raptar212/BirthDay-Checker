namespace BirthdayChecker.Core;

/// <summary>
/// Phone reminders: the first check of the day at a set time, then every <c>interval</c> until midnight,
/// plus "later" snooze ends. Nothing is shown before the set time.
/// </summary>
public static class DailyReminder
{
    public static readonly TimeOnly DefaultTime = new(9, 0);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(3);

    /// <summary>Repeat choices; zero means once a day.</summary>
    public static readonly TimeSpan[] Intervals =
        [TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12)];

    /// <summary>
    /// What to show now, and the state to save once it is shown. Ended snoozes are dropped here, so
    /// <see cref="Next"/> knows they have been checked. Before <paramref name="at"/> the day is not started,
    /// so a later wake still counts as the day's first check (catch-up stays right).
    /// </summary>
    public static (IReadOnlyList<Occurrence> Due, TrayState State) Check(
        IEnumerable<Entry> entries, TrayState state, DateTime now, TimeOnly at)
    {
        if (state.Snoozed.Values.Any(until => until <= now))
            state = state with { Snoozed = state.Snoozed.Where(s => s.Value > now).ToDictionary() };
        if (TimeOnly.FromDateTime(now) < at)
            return ([], state);
        var today = DateOnly.FromDateTime(now);
        state = state.StartDay(today);
        var due = Upcoming.Due(entries, today, state.CatchUpSince)
            .Where(o => !state.IsDismissed(o.Entry.Id, today) && !state.IsSnoozed(o.Entry.Id, now))
            .ToList();
        return (due, state);
    }

    /// <summary>
    /// The widget's "check now", as the PC's manual check: any time of day, everything in reach
    /// (<see cref="Upcoming.InReach"/>), snoozes ignored, "not today" kept. It counts as the day's check.
    /// </summary>
    public static (IReadOnlyList<Occurrence> Due, TrayState State) CheckNow(
        IEnumerable<Entry> entries, TrayState state, DateTime now)
    {
        if (state.Snoozed.Values.Any(until => until <= now))
            state = state with { Snoozed = state.Snoozed.Where(s => s.Value > now).ToDictionary() };
        var today = DateOnly.FromDateTime(now);
        state = state.StartDay(today);
        var due = Upcoming.InReach(entries, today).Where(o => !state.IsDismissed(o.Entry.Id, today)).ToList();
        return (due, state);
    }

    /// <summary>
    /// When to check next: the set time; right away if it has passed with no check today (phone off,
    /// app installed later, time moved back); else the next step of the interval grid from the set time,
    /// or tomorrow's set time. An earlier snooze end wins — one already past means right away: its alarm
    /// was replaced before it fired (app opened, clock changed) and no check has dropped it yet.
    /// </summary>
    public static DateTime Next(TrayState state, DateTime now, TimeOnly at, TimeSpan interval)
    {
        var today = DateOnly.FromDateTime(now);
        var start = today.ToDateTime(at);
        DateTime next;
        if (now < start)
            next = start;
        else if (state.LastChecked != today)
            next = now;
        else
        {
            next = today.AddDays(1).ToDateTime(at);
            if (interval > TimeSpan.Zero)
            {
                var step = start + interval * (Math.Floor((now - start) / interval) + 1);
                if (step < today.AddDays(1).ToDateTime(TimeOnly.MinValue))
                    next = step;
            }
        }
        if (state.Snoozed.Count == 0)
            return next;
        var snooze = state.Snoozed.Values.Min();
        return snooze <= now ? now : snooze < next ? snooze : next;
    }
}
