using Clipify.Core.Extensions;
using Clipify.Core.Interfaces;
using Clipify.Maui.Services;
using Microsoft.Extensions.Logging;

namespace Clipify.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		// 注册Core项目中的服务
		builder.Services.AddClipifyCore();
		
		// 注册MAUI特定的服务实现
		builder.Services.AddSingleton<IHostingEnvironment, HostingEnvironmentImpl>();
		builder.Services.AddSingleton<IDialogService, DialogServiceImpl>();
		builder.Services.AddSingleton<IVideoService, VideoServiceImpl>();
		builder.Services.AddSingleton<IMessageService, MessageServiceImpl>();

		return builder.Build();
	}
}
