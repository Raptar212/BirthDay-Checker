using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using AndroidX.Core.App;
using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// The phone's reminder: one notification for all due events, checked by an inexact alarm
/// (no exact-alarm permission) at the time from <see cref="DailyReminder.Next"/>. Each check books the next one.
/// </summary>
public static class Reminders
{
	/// <summary>Text after the name and date until the user ticks columns in the settings; the list always shows these.</summary>
	public static readonly string[] DefaultColumns = ["Через, дн.", "Событие", "Возраст"];

	/// <summary>The phone's own settings.json (not shared with the PC); MainPage is its only writer.</summary>
	internal static string SettingsPath => AppLayout.SettingsPath(Root);

	/// <summary>0 = as the phone, 1 = light, 2 = dark; applied at start and when changed.</summary>
	public static int Theme
	{
		get => Math.Clamp(Preferences.Default.Get("Theme", 0), 0, 2);
		set => Preferences.Default.Set("Theme", value);
	}

	internal const string ActionCheck = "com.zlatous.birthdaychecker.CHECK";
	internal const string ActionLater = "com.zlatous.birthdaychecker.LATER";
	internal const string ActionNotToday = "com.zlatous.birthdaychecker.NOT_TODAY";
	internal const string ActionCheckNow = "com.zlatous.birthdaychecker.CHECK_NOW";
	private const string ExtraIds = "ids";
	private const string Channel = "reminders";
	private const int NotificationId = 1;
	private static readonly TimeSpan SnoozeTime = TimeSpan.FromHours(1); // as on the PC

	private static string Root => FileSystem.AppDataDirectory;
	private static string StatePath => AppLayout.StatePath(Root);

	/// <summary>Stored as minutes since midnight.</summary>
	public static TimeOnly Time
	{
		get => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(
			Preferences.Default.Get("ReminderTime", (int)DailyReminder.DefaultTime.ToTimeSpan().TotalMinutes), 0, 24 * 60 - 1)));
		set => Preferences.Default.Set("ReminderTime", (int)value.ToTimeSpan().TotalMinutes);
	}

	/// <summary>Stored as whole hours, 0 = once a day; anything else falls back to the default.</summary>
	public static TimeSpan Interval
	{
		get
		{
			var i = TimeSpan.FromHours(Preferences.Default.Get("ReminderIntervalHours", (int)DailyReminder.DefaultInterval.TotalHours));
			return DailyReminder.Intervals.Contains(i) ? i : DailyReminder.DefaultInterval;
		}
		set => Preferences.Default.Set("ReminderIntervalHours", (int)value.TotalHours);
	}

	/// <summary>Books the next check, replacing the booked one (same intent).</summary>
	public static void Schedule(Context context)
	{
		var next = DailyReminder.Next(TrayState.Load(StatePath), DateTime.Now, Time, Interval);
		var ms = new DateTimeOffset(next).ToUnixTimeMilliseconds();
		var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
		var intent = Broadcast(context, ActionCheck, []);
		if (OperatingSystem.IsAndroidVersionAtLeast(23))
			alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, ms, intent); // inexact, but also fires in Doze
		else
			alarms.Set(AlarmType.RtcWakeup, ms, intent);
	}

	/// <summary>The alarm's check, or the widget's "check now" (<paramref name="manual"/>, <see cref="DailyReminder.CheckNow"/>).</summary>
	internal static void Check(Context context, bool manual = false)
	{
		IReadOnlyList<Core.Entry> entries;
		try
		{
			entries = EntryStorage.Load(AppLayout.EntriesPath(Root));
		}
		catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
		{
			Show(context, $"Список не открыт. {MainPage.Explain(e, AppLayout.EntriesPath(Root), "entries.json")}", []);
			return;
		}

		var (due, state) = manual
			? DailyReminder.CheckNow(entries, TrayState.Load(StatePath), DateTime.Now)
			: DailyReminder.Check(entries, TrayState.Load(StatePath), DateTime.Now, Time);
		if (due.Count == 0)
		{
			Manager(context).Cancel(NotificationId);
			if (manual)
				Toast.MakeText(context, entries.Count == 0 ? "Список пуст" : "Событий нет", ToastLength.Short)!.Show();
		}
		else
		{
			if (manual && !Manager(context).AreNotificationsEnabled())
				Toast.MakeText(context, "Уведомления выключены", ToastLength.Short)!.Show(); // Show would do nothing
			var columns = AppSettings.Load(SettingsPath).ReminderColumns ?? DefaultColumns;
			// The bullet marks where each event starts: the shade drops empty lines and bold, so only a visible mark works (BDC-52).
			// 🎉 marks today's events, as on the PC; the shade drops color spans, so no plate as in the PC popup (BDC-71).
			Show(context, ReminderText.Notification(due, columns, 2000, "• ", " 🎉"), [.. due.Select(o => o.Entry.Id)]);
		}
		SaveState(state); // after showing, as on the PC: a failed show keeps the catch-up window
	}

	/// <summary>"Напомнить позже" and "Не напоминать сегодня" — for all the events in the notification.</summary>
	internal static void Answer(Context context, Intent intent)
	{
		var state = TrayState.Load(StatePath);
		var now = DateTime.Now;
		var today = DateOnly.FromDateTime(now);
		foreach (var s in intent.GetStringArrayExtra(ExtraIds) ?? [])
			if (Guid.TryParse(s, out var id))
				state = intent.Action == ActionLater ? state.Snooze(id, now + SnoozeTime, now) : state.Dismiss(id, today);
		SaveState(state);
		Manager(context).Cancel(NotificationId);
	}

	/// <remarks>ponytail: state is a convenience (catch-up, "not today"); a failed write must not crash the receiver.</remarks>
	private static void SaveState(TrayState state)
	{
		try
		{
			TrayState.Save(StatePath, state);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	private static void Show(Context context, string text, Guid[] ids)
	{
		var manager = Manager(context);
		if (!manager.AreNotificationsEnabled())
			return; // permission denied or turned off in the system settings
		CreateChannel(manager);

		var open = PendingIntent.GetActivity(context, 0, new Intent(context, typeof(MainActivity)), Flags)!;
		var style = new NotificationCompat.BigTextStyle();
		style.BigText(text);
		// The binding marks every builder call as nullable, so no chaining.
		var builder = new NotificationCompat.Builder(context, Channel);
		builder.SetSmallIcon(Resource.Drawable.ic_notification);
		builder.SetColor(unchecked((int)0xFFD94540)); // the app icon's red, not the system accent
		builder.SetContentTitle(AppInfo.Current.Name);
		builder.SetContentText(text);
		builder.SetStyle(style);
		builder.SetCategory(NotificationCompat.CategoryReminder);
		builder.SetContentIntent(open);
		builder.SetAutoCancel(true); // no SetOnlyAlertOnce: each repeat check sounds again, even over a shown one (BDC-51)
		if (ids.Length > 0)
		{
			builder.AddAction(0, "Напомнить позже", Broadcast(context, ActionLater, ids));
			builder.AddAction(0, "Не напоминать сегодня", Broadcast(context, ActionNotToday, ids)); // the full PC label is cut off
		}
		manager.Notify(NotificationId, builder.Build());
	}

	private static void CreateChannel(NotificationManagerCompat manager)
	{
		var channel = new NotificationChannelCompat.Builder(Channel, NotificationManagerCompat.ImportanceDefault);
		channel.SetName("Напоминания");
		manager.CreateNotificationChannel(channel.Build()); // no-op before Android 8 and when it exists
	}

	/// <summary>
	/// "Звук уведомления…": the system screen of the reminder channel (sound, vibration, "Do not disturb").
	/// From Android 8 only the user changes a channel's sound, so the app opens this screen instead of keeping its own choice (BDC-54).
	/// </summary>
	internal static void OpenSoundSettings(Context context)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
			return; // no channels: the settings page hides the button
		CreateChannel(Manager(context)); // the screen of a channel not created yet would not open
		var intent = new Intent(Android.Provider.Settings.ActionChannelNotificationSettings);
		intent.PutExtra(Android.Provider.Settings.ExtraAppPackage, context.PackageName);
		intent.PutExtra(Android.Provider.Settings.ExtraChannelId, Channel);
		intent.AddFlags(ActivityFlags.NewTask);
		context.StartActivity(intent);
	}

	/// <summary>The widget's tap.</summary>
	internal static PendingIntent CheckNowIntent(Context context) => Broadcast(context, ActionCheckNow, []);

	private static NotificationManagerCompat Manager(Context context) => NotificationManagerCompat.From(context)!;

	private static PendingIntent Broadcast(Context context, string action, Guid[] ids)
	{
		var intent = new Intent(context, typeof(ReminderReceiver)).SetAction(action)
			.PutExtra(ExtraIds, ids.Select(id => id.ToString()).ToArray());
		return PendingIntent.GetBroadcast(context, 0, intent, Flags | PendingIntentFlags.UpdateCurrent)!;
	}

	private static PendingIntentFlags Flags =>
		OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0;
}

/// <summary>Our own alarm, notification buttons and widget tap; not exported, so other apps can't send them.</summary>
[BroadcastReceiver(Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (context is null || intent is null)
			return;
		if (intent.Action is Reminders.ActionCheck or Reminders.ActionCheckNow)
			Reminders.Check(context, manual: intent.Action == Reminders.ActionCheckNow);
		else if (intent.Action is Reminders.ActionLater or Reminders.ActionNotToday)
			Reminders.Answer(context, intent);
		Reminders.Schedule(context);
	}
}

/// <summary>Alarms are lost on reboot and app update, and a clock or time zone change moves "9:00": book again.</summary>
[BroadcastReceiver(Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged])]
public sealed class RescheduleReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (context is not null)
			Reminders.Schedule(context);
	}
}

/// <summary>The 1x1 "Проверить" widget (BDC-50): a tap runs the manual check, as the PC tray's «Проверить сейчас».</summary>
[BroadcastReceiver(Exported = false, Label = "Проверить сейчас")] // the system still delivers APPWIDGET_UPDATE
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/check_widget_info")]
public sealed class CheckWidget : AppWidgetProvider
{
	public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
	{
		if (context is null || manager is null || ids is null)
			return;
		var views = new RemoteViews(context.PackageName, Resource.Layout.check_widget);
		views.SetOnClickPendingIntent(Resource.Id.check_widget_root, Reminders.CheckNowIntent(context));
		manager.UpdateAppWidget(ids, views);
	}
}
