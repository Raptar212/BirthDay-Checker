using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class EventCatalogTests
{
    private static Entry Of(string type) => new("Test Person", type, new FixedDate(6, 10));

    private static readonly EventDefinition[] Types = [new("Праздник", "#111111", MarkerShape.Drop), new("Поминки", "#222222")];

    [Fact]
    public void Merge_DefaultsUntilEdited_ThenUsedTypesLast()
    {
        var merged = EventCatalog.Merge(null, [Of("Свой тип"), Of("праздник")]);
        Assert.Equal("День Рождения", merged[0].Name);
        Assert.Equal(6, merged.Count);
        Assert.Equal(new EventDefinition("Свой тип"), merged[^1]);
    }

    [Fact]
    public void Merge_DeletedDefaultStaysDeleted() =>
        Assert.Equal(Types, EventCatalog.Merge(Types, [Of("ПРАЗДНИК")]));

    [Fact]
    public void Find_CaseInsensitive_UnknownGetsDefaultLook()
    {
        Assert.Equal(MarkerShape.Drop, EventCatalog.Find(Types, "праздник").Shape);
        Assert.Equal(new EventDefinition("Нет такого"), EventCatalog.Find(Types, "Нет такого"));
    }

    [Theory]
    [InlineData("", null, true)]
    [InlineData("ПРАЗДНИК", null, true)]
    [InlineData("ПРАЗДНИК", "поминки", true)]
    [InlineData("ПРАЗДНИК", "праздник", false)]
    [InlineData("Встреча", null, false)]
    public void NameProblem(string name, string? renaming, bool problem) =>
        Assert.Equal(problem, EventCatalog.NameProblem(name, Types, renaming) is not null);

    [Fact]
    public void Rename_KeepsLookAndPlace() =>
        Assert.Equal(new EventDefinition("Торжество", "#111111", MarkerShape.Drop), EventCatalog.Rename(Types, "праздник", "Торжество")[0]);

    [Fact]
    public void Remove_And_Change_OnTheList()
    {
        Assert.Equal("Поминки", Assert.Single(EventCatalog.Remove(Types, "ПРАЗДНИК")).Name);
        Assert.Equal(MarkerShape.Square, EventCatalog.Change(Types, new("Поминки", "#222222", MarkerShape.Square))[1].Shape);
    }

    [Fact]
    public void RenameIn_ChangesOnlyEntriesOfTheType()
    {
        var other = Of("Поминки");
        var result = EventCatalog.RenameIn([Of("ПРАЗДНИК"), other], "праздник", "Именины");
        Assert.Equal("Именины", result[0].EventType);
        Assert.Same(other, result[1]);
    }

    [Fact]
    public void Settings_RoundTrip_AndResetBadValues()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                {"eventTypes":[{"name":"Праздник","color":"#111111","shape":"Drop"},{"name":"праздник"},{"name":" "},null,{"name":"Поминки","color":"red"}],
                 "todayColor":"red","severalColor":"#123456","customColors":["#ABCDEF","blue"],"calendarWidth":5}
                """);
            var loaded = AppSettings.Load(path);
            Assert.Equal([new EventDefinition("Праздник", "#111111", MarkerShape.Drop), new EventDefinition("Поминки")], loaded.EventTypes!);
            Assert.Equal(EventCatalog.DefaultTodayColor, loaded.TodayColor);
            Assert.Equal("#123456", loaded.SeveralColor);
            Assert.Equal(["#ABCDEF"], loaded.CustomColors!);
            Assert.Null(loaded.CalendarWidth);

            AppSettings.Save(path, loaded);
            Assert.Equal(loaded.EventTypes!, AppSettings.Load(path).EventTypes!);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ColorMathTests
{
    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#FF0000")]
    [InlineData("#00FF00")]
    [InlineData("#0000FF")]
    [InlineData("#FF00FF")]
    [InlineData("#3F7FBF")]
    [InlineData("#D98A1F")]
    [InlineData("#808080")]
    public void Hex_RoundTrip(string hex)
    {
        Assert.True(ColorMath.TryParseHex(hex, out var color));
        Assert.Equal(hex, ColorMath.ToHex(color));
    }

    [Fact]
    public void EveryColorOfACoarseGrid_RoundTrips()
    {
        for (int r = 0; r <= 255; r += 15)
            for (int g = 0; g <= 255; g += 15)
                for (int b = 0; b <= 255; b += 15)
                    Assert.Equal(((byte)r, (byte)g, (byte)b), ColorMath.ToRgb(ColorMath.ToHsv((byte)r, (byte)g, (byte)b)));
    }

    [Fact]
    public void Hue360_IsRed() => Assert.Equal("#FF0000", ColorMath.ToHex(new Hsv(360, 1, 1)));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void TryParseHex_RefusesGarbage(string? text) => Assert.False(ColorMath.TryParseHex(text, out _));

    [Fact]
    public void TryParseHex_AllowsNoHash()
    {
        Assert.True(ColorMath.TryParseHex(" ff8000 ", out var color));
        Assert.Equal("#FF8000", ColorMath.ToHex(color));
    }
}
