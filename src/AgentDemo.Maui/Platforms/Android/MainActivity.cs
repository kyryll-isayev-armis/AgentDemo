using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using AgentDemo.Web.Services;
using Graphics = Android.Graphics;

namespace AgentDemo.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Unspecified,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var dark = Graphics.Color.ParseColor("#07060F");
#pragma warning disable CA1422 // Needed for API < 35; ignored on newer versions where edge-to-edge is enforced.
        Window?.SetStatusBarColor(dark);
        Window?.SetNavigationBarColor(dark);
#pragma warning restore CA1422

        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
    }

    private sealed class BackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override async void HandleOnBackPressed()
        {
            var lifecycle = MauiProgram.Services?.GetService<AppLifecycle>();
            var handled = false;
            try
            {
                handled = lifecycle is not null && await lifecycle.HandleBackAsync();
            }
            catch (Exception)
            {
            }

            // Nothing left to go back to: send the app to the background rather than killing it.
            if (!handled) activity.MoveTaskToBack(true);
        }
    }
}
