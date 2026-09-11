namespace MeshUp.Maui;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// ChatPage is a detail/modal-style page navigated to with parameters, not a
		// permanent tab/flyout item, so it's registered as a route instead of ShellContent.
		Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));
	}
}
