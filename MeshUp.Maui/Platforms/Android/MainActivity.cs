using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace MeshUp.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    // Use AdjustPan instead of the default AdjustResize: resizing the window while a CollectionView
    // is on screen (e.g. ChatPage) can cause it to mis-calculate its scroll offset when the keyboard
    // opens/closes, making the top item appear to jump off-screen. Panning avoids relaying out the
    // CollectionView at all when the keyboard shows.
    WindowSoftInputMode = SoftInput.AdjustPan)]
public class MainActivity : MauiAppCompatActivity
{
}
