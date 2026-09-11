using Android.Content;
using Android.OS;
using MeshUp.Maui.Services;

namespace MeshUp.Maui;

/// <summary>
/// Android implementation of <see cref="IForegroundConnectionService"/>: starts/stops
/// <see cref="MeshtasticForegroundService"/> so the process (and its BLE GATT connection)
/// survives while the app is backgrounded or the phone is locked.
/// </summary>
public class AndroidForegroundConnectionService : IForegroundConnectionService
{
    public void Start()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MeshtasticForegroundService));

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public void Stop()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MeshtasticForegroundService));
        context.StopService(intent);
    }

    public void RequestIgnoreBatteryOptimizations()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
        {
            return;
        }

        var context = global::Android.App.Application.Context;
        var packageName = context.PackageName!;
        var powerManager = (global::Android.OS.PowerManager)context.GetSystemService(Context.PowerService)!;

        if (powerManager.IsIgnoringBatteryOptimizations(packageName))
        {
            return;
        }

        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{packageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch
        {
            // Some OEMs/ROMs block this intent entirely; nothing more we can do programmatically.
        }
    }
}
