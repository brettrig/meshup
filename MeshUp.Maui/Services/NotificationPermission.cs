namespace MeshUp.Maui.Services;

/// <summary>
/// Custom permission for Android's runtime POST_NOTIFICATIONS permission (required on API 33+
/// for local notifications to actually be shown). .NET MAUI's built-in Permissions API doesn't
/// expose this one out of the box, so it's defined here following the documented pattern for
/// custom platform permissions. A no-op (always granted) on platforms other than Android, where
/// this app's local notifications don't need an explicit runtime request through this API.
/// </summary>
public class NotificationPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        new List<(string androidPermission, bool isRuntime)>
        {
            (Android.Manifest.Permission.PostNotifications, true),
        }.ToArray();
#endif
}
