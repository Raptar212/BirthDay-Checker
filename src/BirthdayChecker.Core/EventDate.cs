using System.Text.Json.Serialization;

namespace BirthdayChecker.Core;

/// <summary>Rule that says on which day an event falls in a given year.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FixedDate), "fixed")]
[JsonDerivedType(typeof(NthWeekdayDate), "nthWeekday")]
[JsonDerivedType(typeof(EasterDate), "easter")]
public abstract record EventDate
{
    public abstract DateOnly OccurrenceIn(int year);

    /// <summary>First occurrence on or after <paramref name="today"/>.</summary>
    public DateOnly NextOccurrence(DateOnly today)
    {
        var date = OccurrenceIn(today.Year);
        return date >= today ? date : OccurrenceIn(today.Year + 1);
    }
}

/// <summary>Same day every year. <see cref="Year"/> is null when unknown (legacy year 0001).</summary>
public sealed record FixedDate : EventDate
{
    public int Month { get; }
    public int Day { get; }
    public int? Year { get; }

    public FixedDate(int month, int day, int? year = null)
    {
        // Throws on an impossible date; leap year 2000 lets Feb 29 through when the year is unknown.
        _ = new DateOnly(year ?? 2000, month, day);
        Month = month;
        Day = day;
        Year = year;
    }

    /// <remarks>Feb 29 falls on Feb 28 in non-leap years.</remarks>
    public override DateOnly OccurrenceIn(int year) =>
        new(year, Month, Math.Min(Day, DateTime.DaysInMonth(year, Month)));
}

/// <summary>N-th (1..4) or last (<see cref="Last"/>) weekday of a month, e.g. 2nd Sunday of May.</summary>
public sealed record NthWeekdayDate : EventDate
{
    public const int Last = -1;

    public int Month { get; }
    public DayOfWeek Weekday { get; }
    public int Nth { get; }

    public NthWeekdayDate(int month, DayOfWeek weekday, int nth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        if (!Enum.IsDefined(weekday))
            throw new ArgumentOutOfRangeException(nameof(weekday), weekday, null);
        if (nth is not (Last or >= 1 and <= 4))
            throw new ArgumentOutOfRangeException(nameof(nth), nth, "Expected 1..4 or Last.");
        Month = month;
        Weekday = weekday;
        Nth = nth;
    }

    public override DateOnly OccurrenceIn(int year)
    {
        if (Nth == Last)
        {
            var last = new DateOnly(year, Month, DateTime.DaysInMonth(year, Month));
            return last.AddDays(-(((int)last.DayOfWeek - (int)Weekday + 7) % 7));
        }
        var first = new DateOnly(year, Month, 1);
        return first.AddDays(((int)Weekday - (int)first.DayOfWeek + 7) % 7 + 7 * (Nth - 1));
    }
}

/// <summary>Orthodox Easter plus <see cref="OffsetDays"/>: Easter +0, Radonitsa +9, Trinity +49.</summary>
public sealed record EasterDate : EventDate
{
    /// <summary>Keeps the occurrence inside Easter's own year (Easter is Apr 4..May 8), which NextOccurrence relies on.</summary>
    public const int MaxOffset = 90;

    public int OffsetDays { get; }

    public EasterDate(int offsetDays)
    {
        // Not Math.Abs: it overflows on int.MinValue.
        if (offsetDays is < -MaxOffset or > MaxOffset)
            throw new ArgumentOutOfRangeException(nameof(offsetDays), offsetDays, $"Expected -{MaxOffset}..{MaxOffset}.");
        OffsetDays = offsetDays;
    }

    public override DateOnly OccurrenceIn(int year) => OrthodoxEaster(year).AddDays(OffsetDays);

    /// <summary>Meeus' Julian algorithm, shifted to the Gregorian calendar.</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        int a = year % 4, b = year % 7, c = year % 19;
        int d = (19 * c + 15) % 30;
        int e = (2 * a + 4 * b - d + 34) % 7;
        int julianToGregorian = year / 100 - year / 400 - 2;
        return new DateOnly(year, (d + e + 114) / 31, (d + e + 114) % 31 + 1).AddDays(julianToGregorian);
    }
}
