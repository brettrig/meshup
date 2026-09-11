using Microsoft.Extensions.Logging;
using MeshUp.Maui.Services;
using Plugin.LocalNotification;

namespace MeshUp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseLocalNotification()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<MeshtasticBleService>();
		builder.Services.AddSingleton<MeshtasticChatService>();
		builder.Services.AddSingleton<AppForegroundState>();
		builder.Services.AddSingleton<ChatNotificationService>();
		builder.Services.AddSingleton<ContactNameLookupService>();
		builder.Services.AddSingleton<ChatHistoryStore>();
#if ANDROID
		builder.Services.AddSingleton<IForegroundConnectionService, AndroidForegroundConnectionService>();
#else
		builder.Services.AddSingleton<IForegroundConnectionService, NoOpForegroundConnectionService>();
#endif
		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddTransient<ChatsListPage>();
		builder.Services.AddTransient<SetupPage>();
		builder.Services.AddTransient<ChatPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
