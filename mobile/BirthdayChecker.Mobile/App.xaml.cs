using Microsoft.Extensions.DependencyInjection;

namespace BirthdayChecker.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		ApplyTheme();
	}

	/// <summary>The theme from the settings: as the phone, light or dark.</summary>
	internal static void ApplyTheme() =>
		Current!.UserAppTheme = Reminders.Theme switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.Unspecified };

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
