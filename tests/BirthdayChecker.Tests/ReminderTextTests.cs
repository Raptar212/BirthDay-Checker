using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class ReminderTextTests
{
    [Theory]
    [InlineData(1, "день")]
    [InlineData(2, "дня")]
    [InlineData(4, "дня")]
    [InlineData(5, "дней")]
    [InlineData(11, "дней")]
    [InlineData(12, "дней")]
    [InlineData(14, "дней")]
    [InlineData(21, "день")]
    [InlineData(22, "дня")]
    [InlineData(111, "дней")]
    [InlineData(0, "дней")]
    public void Plural(int n, string expected) =>
        Assert.Equal(expected, ReminderText.Plural(n, "день", "дня", "дней"));

    [Theory]
    [InlineData(0, "Сегодня")]
    [InlineData(1, "Завтра")]
    [InlineData(3, "Через 3 дня")]
    [InlineData(21, "Через 21 день")]
    public void When(int days, string expected) => Assert.Equal(expected, ReminderText.When(days));

    [Fact]
    public void Describe_JoinsWithSeparator_AndShowsAgePhoneComment()
    {
        var today = new DateOnly(2026, 6, 8);
        var a = new Entry("Иван Тестов", "День Рождения", new FixedDate(6, 10, 1985)) { Phone = "+7 000", Comment = "торт" };
        var b = new Entry("Праздник", "Праздник", new FixedDate(6, 8));

        var text = ReminderText.Describe([Upcoming.Next(b, today), Upcoming.Next(a, today)]);

        var parts = text.Split($"{Environment.NewLine}{ReminderText.Separator}{Environment.NewLine}");
        Assert.Equal(2, parts.Length);
        Assert.Equal(string.Join(Environment.NewLine, "Праздник", "08.06, сегодня", "Праздник"), parts[0]);
        Assert.Equal(string.Join(Environment.NewLine,
            "Иван Тестов", "10.06, через 2 дня", "День Рождения, 41 год", "Тел.: +7 000", "торт"), parts[1]);
    }

    private static readonly DateOnly Today = new(2026, 6, 8);

    private static Occurrence Full() => Upcoming.Next(new Entry("Иван Тестов", "День Рождения", new FixedDate(6, 10, 1985))
    {
        Phone = "+7 000", Comment = "торт", Tags = ["семья", "друзья"],
        CustomFields = new Dictionary<string, string> { ["Подарок"] = "книга" },
    }, Today);

    [Fact]
    public void Lines_NoColumns_LeavesNameAndDateOnly() =>
        Assert.Equal(["Иван Тестов", "10.06"], ReminderText.Lines(Full(), []));

    [Fact]
    public void Lines_AllColumns_KeepTableOrder_WhateverOrderTicked() =>
        Assert.Equal(
            ["Иван Тестов", "10.06, через 2 дня", "День Рождения, 41 год", "Теги: семья, друзья", "Тел.: +7 000", "торт", "Подарок: книга"],
            ReminderText.Lines(Full(), ["Подарок", "Комментарий", "Телефон", "Через, дн.", "Теги", "Возраст", "Событие"]));

    [Fact]
    public void Lines_AgeWithoutEvent_AndNotShownByDefault_TagsAndOwnColumns()
    {
        Assert.Equal(["Иван Тестов", "10.06", "41 год"], ReminderText.Lines(Full(), ["Возраст"]));
        Assert.DoesNotContain(ReminderText.Lines(Full()), l => l.StartsWith("Теги") || l.StartsWith("Подарок"));
    }

    [Fact]
    public void Notification_OneEvent_IsLineByLine() =>
        Assert.Equal(ReminderText.Describe(Full()), ReminderText.Notification([Full()]));

    [Fact]
    public void Notification_SeveralEvents_OneLineEach_CommentLineBreaksFlattened()
    {
        var note = Upcoming.Next(new Entry("Мария", "Праздник", new FixedDate(6, 8)) { Comment = "а\nб" }, Today);
        var text = ReminderText.Notification([note, Full()], ["Комментарий"]);
        Assert.Equal($"Мария, 08.06, а б{Environment.NewLine}Иван Тестов, 10.06, торт", text);
    }

    [Fact]
    public void Notification_Bullet_StartsEachOfSeveral_NotOneOrCount()
    {
        var nl = Environment.NewLine;
        var note = Upcoming.Next(new Entry("Мария", "Праздник", new FixedDate(6, 8)), Today);
        Assert.Equal($"• Мария, 08.06{nl}• Иван Тестов, 10.06", ReminderText.Notification([note, Full()], [], bullet: "• "));
        Assert.Equal(ReminderText.Describe(Full()), ReminderText.Notification([Full()], bullet: "• "));
        var text = ReminderText.Notification(Enumerable.Range(0, 40).Select(_ => Full()).ToList(), bullet: "• ");
        Assert.True(text.Length <= ReminderText.MaxNotificationLength);
        Assert.Matches($"{nl}и ещё \\d+$", text);

        var cut = ReminderText.Notification([Full(), note], maxLength: 25, bullet: "• ");
        Assert.True(cut.Length <= 25);
        Assert.StartsWith("• Иван", cut);
        Assert.EndsWith($"…{nl}и ещё 1", cut);
    }

    [Fact]
    public void Notification_TooMany_EndsWithCount_AndFits()
    {
        var items = Enumerable.Range(0, 40).Select(_ => Full()).ToList();
        var text = ReminderText.Notification(items);
        Assert.True(text.Length <= ReminderText.MaxNotificationLength);
        int shown = text.Split(Environment.NewLine).Length - 1;
        Assert.InRange(shown, 1, 39);
        Assert.EndsWith($"и ещё {40 - shown}", text);
    }

    [Fact]
    public void Notification_FirstEventTooLong_IsCut()
    {
        var o = Upcoming.Next(new Entry("Иван", "Праздник", new FixedDate(6, 8)) { Comment = new string('я', 500) }, Today);
        var one = ReminderText.Notification([o]);
        Assert.Equal(ReminderText.MaxNotificationLength, one.Length);
        Assert.EndsWith("…", one);

        var two = ReminderText.Notification([o, o]);
        Assert.True(two.Length <= ReminderText.MaxNotificationLength);
        Assert.EndsWith($"…{Environment.NewLine}и ещё 1", two);
    }
}

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bdc-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RoundTrip()
    {
        var s = new AppSettings
        {
            CheckIntervalMinutes = 60, Position = PopupPosition.TopLeft, AutoCloseSeconds = 30,
            Sound = SoundMode.File, SoundFile = @"C:\x.mp3", TextColor = "#112233", BackgroundColor = "#ABCDEF",
            BackgroundImage = @"C:\bg.png",
        };
        AppSettings.Save(FilePath, s);
        Assert.Equal(s, AppSettings.Load(FilePath));
    }

    [Fact]
    public void Notification_OldFile_DefaultsToTheWindow_AndNewValuesSurviveReload()
    {
        File.WriteAllText(FilePath, """{ "checkIntervalMinutes": 60 }""");
        var old = AppSettings.Load(FilePath);
        Assert.Equal(NotificationMode.Window, old.Notification);
        Assert.Null(old.ReminderColumns);

        AppSettings.Save(FilePath, new AppSettings { Notification = NotificationMode.Windows, ReminderColumns = ["Теги"] });
        var loaded = AppSettings.Load(FilePath);
        Assert.Equal(NotificationMode.Windows, loaded.Notification);
        Assert.Equal(["Теги"], loaded.ReminderColumns);
    }

    [Fact]
    public void ReminderColumns_DropBlankLockedAndRepeated()
    {
        File.WriteAllText(FilePath, """{ "reminderColumns": ["Теги", "теги", "Имя", " "], "notification": "Нет" }""");
        Assert.Null(AppSettings.Load(FilePath).ReminderColumns); // a bad enum name resets the whole file, as everywhere

        File.WriteAllText(FilePath, """{ "reminderColumns": ["Теги", "теги", "Имя", " "] }""");
        Assert.Equal(["Теги"], AppSettings.Load(FilePath).ReminderColumns);
    }

    [Fact]
    public void RemovedViewSetting_InOldFile_IsIgnored()
    {
        File.WriteAllText(FilePath, """{ "checkIntervalMinutes": 60, "view": "Calendar" }""");
        Assert.Equal(60, AppSettings.Load(FilePath).CheckIntervalMinutes);
    }

    [Fact]
    public void BadField_FallsBackAlone_OthersKept()
    {
        File.WriteAllText(FilePath, """
            { "checkIntervalMinutes": 60, "autoCloseSeconds": 3601, "position": 42, "textColor": "red", "backgroundColor": "#00FF00" }
            """);
        var s = AppSettings.Load(FilePath);
        Assert.Equal((60, 0, PopupPosition.BottomRight, null, "#00FF00"),
            (s.CheckIntervalMinutes, s.AutoCloseSeconds, s.Position, s.TextColor, s.BackgroundColor));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData(" 45 ", 45)]
    [InlineData("3600", 3600)]
    public void AutoClose_AcceptsWholeSecondsInRange(string text, int expected)
    {
        Assert.True(AppSettings.TryParseAutoClose(text, out int seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3601")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void AutoClose_RejectsEverythingElse(string? text) =>
        Assert.False(AppSettings.TryParseAutoClose(text, out _));

    [Fact]
    public void AutoClose_CustomValueSurvivesReload()
    {
        AppSettings.Save(FilePath, new AppSettings { AutoCloseSeconds = 7 });
        Assert.Equal(7, AppSettings.Load(FilePath).AutoCloseSeconds);
    }

    [Fact]
    public void TrayState_DismissLastsOneDay_AndRoundTrips()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var today = new DateOnly(2026, 6, 8);
        var state = new TrayState { LastChecked = today }.Dismiss(id1, today).Dismiss(id2, today);

        TrayState.Save(FilePath, state);
        var loaded = TrayState.Load(FilePath);

        Assert.True(loaded.IsDismissed(id1, today) && loaded.IsDismissed(id2, today));
        Assert.False(loaded.IsDismissed(id1, today.AddDays(1)));
        Assert.Equal([id2], loaded.Dismiss(id2, today.AddDays(1)).Dismissed);
        Assert.Equal(today, loaded.LastChecked);
    }

    [Fact]
    public void CaughtUpReminder_StaysDue_OnEveryCheckThatDay()
    {
        var e = new Entry("X", "День Рождения", new FixedDate(6, 20)) { Reminders = Reminder.Week1 }; // reminder day June 13
        var state = new TrayState { LastChecked = new DateOnly(2026, 6, 10) };                     // PC off June 11-14
        var today = new DateOnly(2026, 6, 15);

        var first = state.StartDay(today);
        var second = first.StartDay(today); // later check, same day, after state was saved

        Assert.Equal(new DateOnly(2026, 6, 10), second.CatchUpSince);
        Assert.Single(Upcoming.Due([e], today, first.CatchUpSince));
        Assert.Single(Upcoming.Due([e], today, second.CatchUpSince));
        Assert.Empty(Upcoming.Due([e], today.AddDays(1), second.StartDay(today.AddDays(1)).CatchUpSince)); // next day: window moves on
    }

    [Fact]
    public void RelativeMediaPaths_AreDropped()
    {
        File.WriteAllText(FilePath, """{ "sound": "File", "soundFile": "a.wav", "backgroundImage": "bg.png" }""");
        var s = AppSettings.Load(FilePath);
        Assert.Null(s.SoundFile);
        Assert.Null(s.BackgroundImage);
    }

    [Fact]
    public void TrayState_SnoozeHidesUntilItsEnd_AndRoundTrips()
    {
        var id = Guid.NewGuid();
        var now = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Local);
        var state = new TrayState().Snooze(id, now.AddHours(1), now);

        TrayState.Save(FilePath, state);
        var loaded = TrayState.Load(FilePath);

        Assert.True(loaded.IsSnoozed(id, now.AddMinutes(59)));
        Assert.False(loaded.IsSnoozed(id, now.AddHours(1)));
        Assert.False(loaded.IsSnoozed(Guid.NewGuid(), now));
        Assert.Equal(now.AddHours(1), loaded.NextSnoozeEnd(now));
        Assert.Null(loaded.NextSnoozeEnd(now.AddHours(1)));
    }

    [Fact]
    public void TrayState_Snooze_DropsExpired_AndExtendsSameEvent()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var now = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Local);
        var state = new TrayState().Snooze(a, now.AddHours(1), now).Snooze(a, now.AddHours(2), now);
        Assert.Equal(now.AddHours(2), Assert.Single(state.Snoozed).Value);

        state = state.Snooze(b, now.AddHours(4), now.AddHours(3));
        Assert.Equal(b, Assert.Single(state.Snoozed).Key);
    }

    [Fact]
    public void AppSettings_BackupPeriod_DefaultsAndRange()
    {
        Assert.Equal(7, new AppSettings().BackupPeriodDays);
        File.WriteAllText(FilePath, """{ "backupPeriodDays": 1000 }""");
        Assert.Equal(7, AppSettings.Load(FilePath).BackupPeriodDays);
        File.WriteAllText(FilePath, """{ "backupPeriodDays": 0 }""");
        Assert.Equal(0, AppSettings.Load(FilePath).BackupPeriodDays);
        Assert.True(AppSettings.TryParseBackupPeriod(" 30 ", out int days));
        Assert.Equal(30, days);
        Assert.False(AppSettings.TryParseBackupPeriod("366", out _));
        Assert.False(AppSettings.TryParseBackupPeriod("-1", out _));
    }

    [Fact]
    public void TrayState_ExplicitNullDismissed_IsEmptyList()
    {
        File.WriteAllText(FilePath, """{ "dismissedOn": "2026-06-08", "dismissed": null }""");
        Assert.False(TrayState.Load(FilePath).IsDismissed(Guid.NewGuid(), new DateOnly(2026, 6, 8)));
    }

    [Fact]
    public void TrayState_BrokenFile_IsEmpty()
    {
        File.WriteAllText(FilePath, "{ nope");
        Assert.Null(TrayState.Load(FilePath).LastChecked);
    }

    [Theory]
    [InlineData(null)]                                   // missing file
    [InlineData("{ broken")]
    [InlineData("""{ "checkIntervalMinutes": 7 }""")]   // not one of the menu choices
    public void BadOrMissing_FallsBackToDefault(string? content)
    {
        if (content is not null) File.WriteAllText(FilePath, content);
        Assert.Equal(AppSettings.DefaultInterval, AppSettings.Load(FilePath).CheckIntervalMinutes);
    }
}
