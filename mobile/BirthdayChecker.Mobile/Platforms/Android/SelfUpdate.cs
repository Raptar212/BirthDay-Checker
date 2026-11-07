using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Widget;
using BirthdayChecker.Core;

namespace BirthdayChecker.Mobile;

/// <summary>
/// BDC-67: the APK goes to Android's own installer (PackageInstaller) instead of the browser. Android 12+ updates the
/// app without asking once "install unknown apps" is allowed for it (UPDATE_PACKAGES_WITHOUT_USER_ACTION, "updating
/// itself"); Android 8–11, or any refusal of the silent way, shows the system's "Update?" dialog instead.
/// </summary>
internal static class SelfUpdate
{
	private static int? _staged;

	/// <summary>The system's "Update?" dialog, held until the app is on screen (Android blocks it from the background).</summary>
	internal static Intent? Confirm;

	/// <summary>Android installs the update by itself from 12 on; older ones always ask.</summary>
	public static bool SilentSupported => OperatingSystem.IsAndroidVersionAtLeast(31);

	/// <summary>"Install unknown apps" is allowed for this app (Android 8+ asks per app).</summary>
	public static bool CanInstall =>
		OperatingSystem.IsAndroidVersionAtLeast(26) && Platform.AppContext.PackageManager!.CanRequestPackageInstalls();

	/// <summary>Wi-Fi or another free network: 30 MB on mobile data only when the user taps «Обновить».</summary>
	public static bool Unmetered =>
		Platform.AppContext.GetSystemService(Context.ConnectivityService) is ConnectivityManager { IsActiveNetworkMetered: false };

	/// <summary>An APK in a session waits for <see cref="Commit"/>.</summary>
	public static bool Staged => _staged is not null;

	/// <summary>Set while the app opens its own file picker or share sheet: Android calls that "leaving" too.</summary>
	internal static bool OwnActivity;

	/// <summary>
	/// The silent commit, when the user has left the app from its main list: not from a picker of its own (the
	/// install would close the app under it), not with an editor or other page open (its unsaved state would go).
	/// </summary>
	public static void CommitIfIdle()
	{
		if (!OwnActivity && Shell.Current?.Navigation is { } nav && nav.NavigationStack.Count <= 1 && nav.ModalStack.Count == 0)
			Commit();
	}

	/// <summary>Opens the "install unknown apps" switch of this app.</summary>
	public static void AskPermission()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
			return;
		var ctx = Platform.AppContext;
		var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + ctx.PackageName));
		ctx.StartActivity(intent.AddFlags(ActivityFlags.NewTask));
	}

	/// <summary>
	/// Downloads the APK, checks its SHA-256 against the release and writes it into a new install session.
	/// Nothing is installed until <see cref="Commit"/>.
	/// </summary>
	public static async Task StageAsync(HttpClient client, UpdateRelease release)
	{
		var installer = Platform.AppContext.PackageManager!.PackageInstaller;
		foreach (var old in installer.MySessions) // left by a process Android killed before it committed
			Abandon(installer, old.SessionId);
		_staged = null;
		var file = Path.Combine(FileSystem.CacheDirectory, "update.apk");
		int? id = null;
		try
		{
			using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
			await using (var download = await client.GetStreamAsync(release.Installer, cts.Token))
			await using (var output = File.Create(file))
				await download.CopyToAsync(output, cts.Token);
			await using var apk = File.OpenRead(file);
			if (!UpdateCheck.HashMatches(apk, release.Sha256))
				throw new InvalidDataException("Скачанный файл не совпал с релизом (SHA-256). Попробуйте позже.");
			apk.Position = 0;
			var options = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
			if (OperatingSystem.IsAndroidVersionAtLeast(31))
				options.SetRequireUserAction((int)PackageInstallUserAction.NotRequired);
			id = installer.CreateSession(options);
			using var session = installer.OpenSession(id.Value);
			using (var write = session.OpenWrite("app.apk", 0, apk.Length))
			{
				await apk.CopyToAsync(write);
				session.Fsync(write);
			}
			_staged = id;
		}
		catch
		{
			if (id is { } broken)
				Abandon(installer, broken);
			throw;
		}
		finally
		{
			try
			{
				File.Delete(file);
			}
			catch (IOException) { } // the cache is the system's to clear; the original error matters more
		}
	}

	private static void Abandon(PackageInstaller installer, int id)
	{
		try
		{
			installer.AbandonSession(id);
		}
		catch (Exception) { } // already committed or gone: nothing left to abandon
	}

	/// <summary>Hands the staged APK to Android: on 12+ it installs in silence and closes the app; otherwise asks.</summary>
	public static void Commit()
	{
		if (_staged is not { } id)
			return;
		_staged = null;
		var ctx = Platform.AppContext;
		try
		{
			// Mutable: Android writes the result into it. Explicit receiver: Android 14 refuses a mutable implicit one.
			var flags = (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0) | PendingIntentFlags.UpdateCurrent;
			var status = PendingIntent.GetBroadcast(ctx, 0, new Intent(ctx, typeof(InstallStatusReceiver)), flags)!;
			using var session = ctx.PackageManager!.PackageInstaller.OpenSession(id);
			session.Commit(status.IntentSender!);
		}
		// Called from the activity's lifecycle too, where a throw would crash the app; the next check tries again.
		catch (Exception e)
		{
			Abandon(ctx.PackageManager!.PackageInstaller, id);
			InstallStatusReceiver.Failed(ctx, e.Message);
		}
	}
}

/// <summary>The installer's answer: ask the user (shown now, or when the app comes back), or a failure to tell.</summary>
[BroadcastReceiver(Exported = false)]
public sealed class InstallStatusReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (context is null || intent is null)
			return;
		var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
		if (status == PackageInstallStatus.PendingUserAction)
		{
#pragma warning disable CA1422, CS0618 // the typed overload is Android 13+; this one works on all
			if (intent.GetParcelableExtra(Intent.ExtraIntent) is not Intent confirm)
				return;
#pragma warning restore CA1422, CS0618
			SelfUpdate.Confirm = confirm.AddFlags(ActivityFlags.NewTask);
			if (MainActivity.OnScreen)
				MainActivity.ShowConfirm(context);
		}
		else if (status == PackageInstallStatus.FailureAborted) // the user said no to the dialog: ask again tomorrow
			return;
		else if (status != PackageInstallStatus.Success)
			Failed(context, intent.GetStringExtra(PackageInstaller.ExtraStatusMessage));
	}

	/// <summary>Tells the user and lets the next opening check again instead of tomorrow.</summary>
	internal static void Failed(Context context, string? reason)
	{
		Preferences.Default.Remove(Updates.CheckedKey);
		Toast.MakeText(context, "Обновление не установлено: " + reason, ToastLength.Long)!.Show();
	}
}
