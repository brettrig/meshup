namespace MeshUp.Maui.Services;

/// <summary>
/// Starts/stops a platform-level mechanism to keep the app's BLE connection alive while
/// backgrounded or the screen is locked. On Android this runs a foreground service; on other
/// platforms this is a no-op since background suspension isn't a concern there.
/// </summary>
public interface IForegroundConnectionService
{
    void Start();

    void Stop();

    /// <summary>
    /// Prompts the user (if needed) to exempt this app from battery optimization/Doze,
    /// which can otherwise throttle BLE scanning and GATT callbacks even while a
    /// foreground service is running. No-op on platforms where this doesn't apply.
    /// </summary>
    void RequestIgnoreBatteryOptimizations();
}
