using MeshUp.Maui.Models;
using Plugin.LocalNotification;
using Plugin.LocalNotification.AndroidOption;
using Plugin.LocalNotification.EventArgs;
using Contact = MeshUp.Maui.Models.Contact;

namespace MeshUp.Maui.Services;

/// <summary>
/// Shows a local (system) notification for inbound Meshtastic messages when the user
/// wouldn't otherwise see them - i.e. when the app is backgrounded/locked, or the relevant
/// chat thread isn't the one currently on screen. Tapping a notification navigates to the
/// corresponding chat thread.
/// </summary>
public class ChatNotificationService
{
    private const int EveryoneNotificationId = 1;

    // Renamed from "messages": once an Android notification channel is created, its importance
    // is locked forever and cannot be changed by code (only by the user, in system settings).
    // Earlier builds created the "messages" channel implicitly at default/low importance before
    // this class started explicitly requesting High importance, so devices that installed those
    // earlier builds are stuck with a low-importance channel that never shows heads-up/lock-screen
    // alerts. Using a new id forces Android to create a fresh channel with the correct importance.
    private const string MessagesChannelId = "messages_v2";

    private readonly MeshtasticChatService _chatService;
    private readonly AppForegroundState _foregroundState;

    public ChatNotificationService(MeshtasticChatService chatService, AppForegroundState foregroundState)
    {
        _chatService = chatService;
        _foregroundState = foregroundState;

        // A dedicated, high-importance channel must be created explicitly on Android 8+ for the
        // notification to actually pop up/show on the lock screen - per-notification Priority
        // alone isn't enough, since the channel's importance takes precedence once the channel
        // exists. Without this, Android silently defaults new channels to a lower importance and
        // notifications are delivered quietly to the shade only (no alert while locked/backgrounded).
        // Notification channels are an Android-only concept, so this is a no-op on other platforms.
#if ANDROID
        LocalNotificationCenter.CreateNotificationChannels(new List<NotificationChannelRequest>
        {
            new()
            {
                Id = MessagesChannelId,
                Name = "Messages",
                Description = "Notifications for new Meshtastic messages",
                Importance = AndroidImportance.High,
                LockScreenVisibility = AndroidVisibilityType.Public,
            }
        });
#endif

        _chatService.EveryoneMessageReceived += OnEveryoneMessageReceived;
        _chatService.DirectMessageReceived += OnDirectMessageReceived;

        LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationActionTapped;

        // Proactively prompt for the notification permission (required on Android 13+) at
        // startup, rather than requiring the user to dig into system Settings to enable it
        // manually - most users just tap "Allow" on the OS prompt.
        _ = RequestNotificationPermissionAsync();
    }

    private static async Task RequestNotificationPermissionAsync()
    {
        try
        {
            // Permissions.CheckStatusAsync/RequestAsync must be invoked from the UI thread.
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var status = await Permissions.CheckStatusAsync<NotificationPermission>();
                if (status != PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<NotificationPermission>();
                }
            });
        }
        catch
        {
            // Best-effort only; if the platform/OS version doesn't support this permission
            // request, notifications either don't require it or the OS handles it itself.
        }
    }

    private void OnEveryoneMessageReceived(object? sender, ChatMessage message)
    {
        if (message.IsMine || _foregroundState.IsThreadVisible(isEveryone: true, contactNodeNum: 0))
        {
            return;
        }

        Show(
            notificationId: EveryoneNotificationId,
            title: message.SenderName,
            message: message.DisplayText,
            route: $"ChatPage?nodeNum=0&isEveryone=True");
    }

    private void OnDirectMessageReceived(object? sender, (Contact Contact, ChatMessage Message) e)
    {
        if (e.Message.IsMine || _foregroundState.IsThreadVisible(isEveryone: false, contactNodeNum: e.Contact.NodeNum))
        {
            return;
        }

        Show(
            notificationId: unchecked((int)e.Contact.NodeNum),
            title: e.Message.SenderName,
            message: e.Message.DisplayText,
            route: $"ChatPage?nodeNum={e.Contact.NodeNum}&isEveryone=False");
    }

    private static void Show(int notificationId, string title, string message, string route)
    {
        var request = new NotificationRequest
        {
            NotificationId = notificationId,
            Title = title,
            Description = message,
            ReturningData = route,
            Android = new Plugin.LocalNotification.AndroidOption.AndroidOptions
            {
                AutoCancel = true,
                ChannelId = MessagesChannelId,
                // High importance + public lock-screen visibility are required for the
                // notification to actually pop up/show its content while the screen is locked;
                // otherwise Android may silently deliver it to the shade only.
                VisibilityType = Plugin.LocalNotification.AndroidOption.AndroidVisibilityType.Public,
                Priority = Plugin.LocalNotification.AndroidOption.AndroidPriority.High,
            },
        };

        LocalNotificationCenter.Current.Show(request);
    }

    private static void OnNotificationActionTapped(NotificationActionEventArgs e)
    {
        var route = e.Request?.ReturningData;
        if (string.IsNullOrEmpty(route))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is not null)
            {
                await Shell.Current.GoToAsync(route);
            }
        });
    }
}
