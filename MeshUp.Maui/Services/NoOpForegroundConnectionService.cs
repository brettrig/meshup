namespace MeshUp.Maui.Services;

/// <summary>
/// Default no-op implementation of <see cref="IForegroundConnectionService"/> used on platforms
/// (e.g. Windows) where background execution isn't suspended the way it is on Android.
/// </summary>
public class NoOpForegroundConnectionService : IForegroundConnectionService
{
    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void RequestIgnoreBatteryOptimizations()
    {
    }
}
