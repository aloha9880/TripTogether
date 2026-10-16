using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using TripTogether.App.Services;
namespace TripTogether.App;
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "triptogether", DataHost = "join")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Intent?.Action == Android.Content.Intent.ActionView) InvitationInbox.Receive(Intent.DataString);
    }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.Action == Android.Content.Intent.ActionView) InvitationInbox.Receive(intent.DataString);
    }
}
