using System.Globalization;
using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// The phone's update check (BDC-61): once a day when the app opens (a setting) and from the ⋮ menu.
/// BDC-67: the app downloads the APK itself, checks its SHA-256 and hands it to Android's installer. With
/// <see cref="Silent"/> on (Android 12+, Wi-Fi, "install unknown apps" allowed) the automatic check asks nothing:
/// the update installs when the user leaves the app. Otherwise a dialog offers it; without the permission, the browser.
/// </summary>
internal static class Updates
{
	internal const string CheckedKey = "UpdatesCheckedOn";

	/// <summary>The automatic check on opening; off leaves only the menu's.</summary>
	public static bool Enabled
	{
		get => Preferences.Default.Get("CheckUpdates", true);
		set => Preferences.Default.Set("CheckUpdates", value);
	}

	/// <summary>The automatic check installs a new version without the dialog (where Android allows it).</summary>
	public static bool Silent
	{
		get => Preferences.Default.Get("SilentUpdates", true);
		set => Preferences.Default.Set("SilentUpdates", value);
	}

	// One client for the app's lifetime, created on the first check. No overall timeout: the APK's download has its own.
	private static readonly Lazy<HttpClient> Client = new(() =>
	{
		var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"BirthDayChecker-Mobile/{Current.ToString(3)}"); // GitHub's API requires one
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return client;
	});

	private static bool _running;

	/// <summary>The installed version (ApplicationDisplayVersion).</summary>
	private static Version Current => Version.TryParse(AppInfo.Current.VersionString, out var v) ? v : new Version(0, 0, 0);

	/// <summary>
	/// Automatic: at most once a day (a successful check counts), silent unless there is a new version.
	/// Manual: always, and says what it found.
	/// </summary>
	public static async Task CheckAsync(Page page, bool manual)
	{
		var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		if (_running || (!manual && (!Enabled || Preferences.Default.Get(CheckedKey, "") == today)))
			return;
		_running = true;
		try
		{
			string json;
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
				json = await Client.Value.GetStringAsync(UpdateCheck.ReleasesUrl, cts.Token);
			var release = UpdateCheck.ParseMobile(json);
			bool newer = release is not null && UpdateCheck.IsNewer(release.Version, Current);
			// Silent: no dialog, so an open editor doesn't matter; installed once the user leaves the app (MainActivity).
			if (!manual && newer && release is not null && Silent && SelfUpdate.SilentSupported && SelfUpdate.CanInstall
				&& SelfUpdate.Unmetered && !SelfUpdate.Staged)
			{
				await SelfUpdate.StageAsync(Client.Value, release);
				Preferences.Default.Set(CheckedKey, today);
				if (!MainActivity.OnScreen) // the user left while it downloaded: no leave left to wait for
					SelfUpdate.CommitIfIdle();
				return;
			}
			// The fetch takes a while: an editor or the calendar may be open by now. Not over it — and not counted, so the next opening offers it.
			if (!manual && newer && (page.Navigation.NavigationStack.Count > 1 || page.Navigation.ModalStack.Count > 0))
				return;
			Preferences.Default.Set(CheckedKey, today);
			if (newer && release is not null)
			{
				if (await page.DisplayAlertAsync($"Доступна версия {release.Version.ToString(3)}",
						$"Установлена {Current.ToString(3)}. «Обновить» скачает её (около 30 МБ) и установит; приложение " +
						"закроется на несколько секунд. Записи и настройки сохранятся.", "Обновить", "Позже"))
					await InstallAsync(page, release);
			}
			else if (manual)
			{
				var (title, text) = UpdateCheck.MobileTagVersion(json) is { } tagged && UpdateCheck.IsNewer(tagged, Current)
					? ("Обновление недоступно", $"У версии {tagged.ToString(3)} нет файла APK. Попробуйте позже.")
					: ("Обновлений нет", $"Установлена последняя версия ({Current.ToString(3)}).");
				await page.DisplayAlertAsync(title, text, "OK");
			}
		}
		// Any exception: the callers are async void handlers; Android's network errors come as Java exceptions too.
		catch (Exception e)
		{
			if (manual) // automatic: no network is normal, the next opening tries again
				try
				{
					await page.DisplayAlertAsync("Не удалось проверить обновления", Reason(e), "OK");
				}
				catch (Exception) { } // the page is going away (activity recreated): nothing to tell, nothing to crash
		}
		finally
		{
			_running = false;
		}
	}

	/// <summary>
	/// The dialog's «Обновить»: download, check and install now (Android 12+ silently, older ones with the system's
	/// dialog). Without "install unknown apps" it offers that switch once, or the browser as before BDC-67.
	/// </summary>
	private static async Task InstallAsync(Page page, UpdateRelease release)
	{
		try
		{
			if (!SelfUpdate.CanInstall)
			{
				if (OperatingSystem.IsAndroidVersionAtLeast(26) && await page.DisplayAlertAsync("Нужно разрешение",
						"Чтобы приложение само ставило свои обновления, включите для него «Установку неизвестных приложений» " +
						"(один раз). Потом снова ⋮ → «Проверить обновления».", "Открыть настройки", "Через браузер"))
					SelfUpdate.AskPermission();
				else
					await Launcher.Default.OpenAsync(release.Installer);
				return;
			}
			Android.Widget.Toast.MakeText(Platform.AppContext, "Загружаю обновление…", Android.Widget.ToastLength.Short)!.Show();
			if (!SelfUpdate.Staged)
				await SelfUpdate.StageAsync(Client.Value, release);
			SelfUpdate.Commit();
		}
		catch (Exception e)
		{
			try
			{
				await page.DisplayAlertAsync("Обновление не загружено", Reason(e), "OK");
			}
			catch (Exception) { } // the page is going away
		}
	}

	/// <summary>In Russian: the network's own messages come in English.</summary>
	private static string Reason(Exception e) => e switch
	{
		TaskCanceledException => "GitHub не ответил вовремя.",
		HttpRequestException { StatusCode: { } code } => $"GitHub ответил {(int)code}.",
		HttpRequestException => "Нет связи с GitHub.",
		_ => e.Message,
	};
}
