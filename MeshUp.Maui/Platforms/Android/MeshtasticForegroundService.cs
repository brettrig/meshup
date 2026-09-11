using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace MeshUp.Maui;

/// <summary>
/// A minimal Android foreground service whose only purpose is to keep this process alive
/// (and therefore keep the BLE GATT connection/callbacks running) while the phone is locked
/// or the app is in the background. Android aggressively suspends background work otherwise,
/// which stops inbound Meshtastic messages from ever reaching the app to be notified about.
/// </summary>
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeConnectedDevice)]
public class MeshtasticForegroundService : Service
{
    private const string ChannelId = "meshtastic_connection";
    private const int NotificationId = 1000;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForeground(NotificationId, BuildNotification());
        return StartCommandResult.Sticky;
    }

    private Notification BuildNotification()
    {
        var notificationManager = (NotificationManager)GetSystemService(NotificationService)!;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O
            && notificationManager.GetNotificationChannel(ChannelId) is null)
        {
            var channel = new NotificationChannel(ChannelId, "Meshtastic connection", NotificationImportance.Low)
            {
                Description = "Keeps the app connected to your Meshtastic radio while running in the background.",
            };
            notificationManager.CreateNotificationChannel(channel);
        }

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Meshtastic")
            .SetContentText("Connected - listening for messages")
            .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .Build();
    }
}
