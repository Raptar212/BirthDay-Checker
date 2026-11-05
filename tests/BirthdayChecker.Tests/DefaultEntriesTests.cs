using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

/// <summary>installer\default-entries.json: the installer puts it as entries.json on a first install.</summary>
public sealed class DefaultEntriesTests
{
    [Fact]
    public void LoadsStrictly_WithRussianHolidayDates()
    {
        var entries = EntryStorage.Load(Path.Combine(AppContext.BaseDirectory, "default-entries.json"));

        var dates2027 = entries.ToDictionary(e => e.Name, e => e.Date.OccurrenceIn(2027));
        Assert.Equal(new Dictionary<string, DateOnly>
        {
            ["Новый год"] = new(2027, 1, 1),
            ["Рождество Христово"] = new(2027, 1, 7),
            ["Пасха"] = new(2027, 5, 2),
            ["День отца"] = new(2027, 10, 17),
            ["День матери"] = new(2027, 11, 28),
        }, dates2027);
        Assert.All(entries, e => Assert.Equal("Праздник", e.EventType));
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
    }
}
