using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace BirthdayChecker.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	/// <summary>A horizontal swipe anywhere on the screen: true = the finger went left to right.</summary>
	/// <remarks>Seen here, before the views: MAUI's SwipeGestureRecognizer gets nothing inside a scrolling CollectionView.</remarks>
	public static event Action<bool>? HorizontalSwipe;

	private GestureDetector? _swipes;

	public override bool DispatchTouchEvent(MotionEvent? e)
	{
		if (e is not null)
		{
			_swipes ??= new GestureDetector(this, new SwipeListener(Resources?.DisplayMetrics?.Density ?? 1));
			_swipes.OnTouchEvent(e);
		}
		return base.DispatchTouchEvent(e);
	}

	/// <summary>A fling at least 80 dp long and mostly sideways; the touch still goes on to the views.</summary>
	private sealed class SwipeListener(float density) : GestureDetector.SimpleOnGestureListener
	{
		public override bool OnFling(MotionEvent? e1, MotionEvent e2, float velocityX, float velocityY)
		{
			if (e1 is null)
				return false;
			float dx = e2.GetX() - e1.GetX(), dy = e2.GetY() - e1.GetY();
			if (Math.Abs(dx) < 80 * density || Math.Abs(dx) < 2 * Math.Abs(dy))
				return false;
			HorizontalSwipe?.Invoke(dx > 0);
			return true;
		}
	}
}
