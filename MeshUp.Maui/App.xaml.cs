using MeshUp.Maui.Services;

namespace MeshUp.Maui;

public partial class App : Application
{
	private readonly AppForegroundState _foregroundState;

	public App(AppShell appShell, AppForegroundState foregroundState)
	{
		InitializeComponent();

		_foregroundState = foregroundState;

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
