using InStSanity.Platforms.Windows;
using InStSanity.Services.Api;
using InStSanity.Services.Instagram;
using InStSanity.Services.WebHost;
using InStSanity.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
#endif

namespace InStSanity;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
#if WINDOWS
		// Must run before the first WebView2 is created.
		WindowsWebViewEnvironment.Configure();
#endif

		var builder = MauiApp.CreateBuilder();

		builder.UseMauiApp<App>();

#if WINDOWS
		// The desktop UX owns its own close affordances (the caught-imp prompt by default,
		// or an optional in-app exit icon). Collapse the WinUI title bar so Windows does
		// not add a second, competing row of min/max/close controls above the web UI.
		builder.ConfigureLifecycleEvents(events =>
		{
			events.AddWindows(windows => windows.OnWindowCreated(window =>
			{
				var handle = WindowNative.GetWindowHandle(window);
				var id = Win32Interop.GetWindowIdFromWindow(handle);
				var titleBar = AppWindow.GetFromWindowId(id).TitleBar;
				titleBar.ExtendsContentIntoTitleBar = true;
				titleBar.PreferredHeightOption = AppWindowTitleBarHeightOption.Collapsed;
			}));
		});
#endif

		// ── Web layer hosting (see docs/hybrid-port-plan.md) ────────────────────
		// The ported UI is the original web app served by an in-process loopback host.
		// It talks to that host exactly as it talked to its own local backend, so its
		// /api/* calls never leave the machine.
		builder.Services.AddSingleton<InstagramHttp>();
		builder.Services.AddSingleton<SaveFreeClient>();
		builder.Services.AddSingleton<InstagramResolver>();
		builder.Services.AddSingleton<MediaCache>();
		builder.Services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
		builder.Services.AddSingleton<IDownloadFolderOpener, WindowsDownloadFolderOpener>();

		// /api/image and /api/download-photo share one implementation, differing only in
		// how the payload is presented.
		builder.Services.AddSingleton<IApiEndpoint>(services => new MediaProxyEndpoint(
			services.GetRequiredService<InstagramHttp>(),
			services.GetRequiredService<MediaCache>(),
			services.GetRequiredService<ILogger<MediaProxyEndpoint>>(),
			"/api/image"));

		builder.Services.AddSingleton<IApiEndpoint>(services => new MediaProxyEndpoint(
			services.GetRequiredService<InstagramHttp>(),
			services.GetRequiredService<MediaCache>(),
			services.GetRequiredService<ILogger<MediaProxyEndpoint>>(),
			"/api/download-photo",
			asAttachment: true));

		builder.Services.AddSingleton<IApiEndpoint, DownloadSelectedEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, OpenDownloadFolderEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ExitAppEndpoint>();

		// The resolve routes are what turn an imported URL into a cover image, an owner,
		// a caption and a carousel. SaveFreeClient is the fallback for posts Instagram
		// refuses to serve logged out (ported from fetch_via_savefree in server.py). Without them every card keeps an empty coverUrl and
		// the grid's "server unreachable" note comes on — see InstagramResolver.
		builder.Services.AddSingleton<IApiEndpoint, BrowseFolderEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ResolvePostEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ResolveBatchEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ResolveOwnersEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, CachedPostsEndpoint>();

		// Routes that are not registered yet answer 501, which the web layer handles
		// gracefully. Remaining phases: save-imported-list / browser-*.

		builder.Services.AddSingleton<LoopbackHost>();

		// ── MVVM ────────────────────────────────────────────────────────────────
		//   App ← MainPage ← MainPageViewModel ← LoopbackHost
		//
		// AppShell.xaml (the native side-panel shell built earlier) is intentionally not
		// registered: the web layer draws its own shell. See the note in AppShell.xaml.
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MainPage>();

		return builder.Build();
	}
}
