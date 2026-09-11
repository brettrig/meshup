using MeshUp.Maui.Services;

namespace MeshUp.Maui;

public partial class App : Application
{
	private readonly AppForegroundState _foregroundState;

	public App(AppShell appShell, AppForegroundState foregroundState, ChatNotificationService notificationService)
	{
		InitializeComponent();

		_foregroundState = foregroundState;

		// ChatNotificationService is otherwise never resolved from DI (nothing else depends on
		// it), so it must be injected somewhere to force it to be constructed - its constructor
		// is what wires up the notification channel and subscribes to incoming messages.
		_ = notificationService;

		MainPage = appShell;
	}

	protected override void OnStart()
	{
		_foregroundState.SetForeground(true);
	}

	protected override void OnResume()
	{
		_foregroundState.SetForeground(true);
	}

	protected override void OnSleep()
	{
		_foregroundState.SetForeground(false);
	}
}
