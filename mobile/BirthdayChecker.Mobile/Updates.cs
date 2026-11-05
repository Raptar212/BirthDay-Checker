using System.Globalization;
using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// The phone's update check (BDC-61): once a day when the app opens (a setting) and from the ⋮ menu.
/// A newer version is offered in a dialog; "Обновить" opens its APK in the browser, which downloads it, and the
/// user installs it as before. Android refuses an update signed with another key, so the app keeps no hash check.
/// </summary>
internal static class Updates
{
	private const string CheckedKey = "UpdatesCheckedOn";

	/// <summary>The automatic check on opening; off leaves only the menu's.</summary>
	public static bool Enabled
	{
		get => Preferences.Default.Get("CheckUpdates", true);
		set => Preferences.Default.Set("CheckUpdates", value);
	}

	// One client for the app's lifetime, created on the first check.
	private static readonly Lazy<HttpClient> Client = new(() =>
	{
		var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
			var json = await Client.Value.GetStringAsync(UpdateCheck.ReleasesUrl);
			var release = UpdateCheck.ParseMobile(json);
			bool newer = release is not null && UpdateCheck.IsNewer(release.Version, Current);
			// The fetch takes a while: an editor or the calendar may be open by now. Not over it — and not counted, so the next opening offers it.
			if (!manual && newer && (page.Navigation.NavigationStack.Count > 1 || page.Navigation.ModalStack.Count > 0))
				return;
			Preferences.Default.Set(CheckedKey, today);
			if (newer && release is not null)
			{
				if (await page.DisplayAlertAsync($"Доступна версия {release.Version.ToString(3)}",
						$"Установлена {Current.ToString(3)}. «Обновить» откроет загрузку APK в браузере — потом откройте скачанный файл " +
						"и установите его. Записи и настройки сохранятся.", "Обновить", "Позже"))
					await Launcher.Default.OpenAsync(release.Installer);
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

	/// <summary>In Russian: the network's own messages come in English.</summary>
	private static string Reason(Exception e) => e switch
	{
		TaskCanceledException => "GitHub не ответил вовремя.",
		HttpRequestException { StatusCode: { } code } => $"GitHub ответил {(int)code}.",
		HttpRequestException => "Нет связи с GitHub.",
		_ => e.Message,
	};
}
