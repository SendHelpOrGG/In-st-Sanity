using InStSanity.Platforms.Windows;
using InStSanity.Services.Api;
using InStSanity.Services.Browser;
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
				if (!AppWindowTitleBar.IsCustomizationSupported())
					return;

				var handle = WindowNative.GetWindowHandle(window);
				var id = Win32Interop.GetWindowIdFromWindow(handle);
				var appWindow = AppWindow.GetFromWindowId(id);

				// The web UI already carries the app branding, so avoid repeating the name
				// in the native caption while keeping the window icon and caption controls.
				appWindow.Title = string.Empty;

				// The taskbar and caption must show the imp app icon. ApplicationIcon
				// covers the exe; this pins the window itself to the same .ico.
				try
				{
					var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
					if (File.Exists(iconPath))
						appWindow.SetIcon(iconPath);
				}
				catch
				{
					// The icon is cosmetic; never block window creation over it.
				}

				var titleBar = appWindow.TitleBar;
				titleBar.ExtendsContentIntoTitleBar = true;
				titleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

				// Always place a new app window in the center of the primary display's
				// usable area, rather than relying on Windows' remembered placement.
				var primaryDisplay = DisplayArea.GetFromPoint(
					new Windows.Graphics.PointInt32(0, 0),
					DisplayAreaFallback.Primary);
				var workArea = primaryDisplay.WorkArea;
				var windowSize = appWindow.Size;
				var x = workArea.X + (workArea.Width - windowSize.Width) / 2;
				var y = workArea.Y + (workArea.Height - windowSize.Height) / 2;
				appWindow.Move(new Windows.Graphics.PointInt32(x, y));
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
		builder.Services.AddSingleton<InstagramBrowserWindowService>();
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
		builder.Services.AddSingleton<IApiEndpoint, CollapseWindowEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, OpenInstagramBrowserEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, InstagramBrowserViewEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, InstagramSessionEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, InstagramLogoutEndpoint>();

		// The resolve routes are what turn an imported URL into a cover image, an owner,
		// a caption and a carousel. SaveFreeClient is the fallback for posts Instagram
		// refuses to serve logged out (ported from fetch_via_savefree in server.py). Without them every card keeps an empty coverUrl and
		// the grid's "server unreachable" note comes on — see InstagramResolver.
		builder.Services.AddSingleton<IApiEndpoint, BrowseFolderEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ResolvePostEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ResolveBatchEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, ResolveOwnersEndpoint>();
		builder.Services.AddSingleton<IApiEndpoint, CachedPostsEndpoint>();

		// The visible Instagram browser is hosted natively by MainPage and opened via
		// OpenInstagramBrowserEndpoint. The Browser page drives it through
		// InstagramBrowserViewEndpoint and reads account state from InstagramSessionEndpoint;
		// InstagramLogoutEndpoint clears the persistent profile session. Instagram
		// credentials are entered only on Instagram; the session routes merely reflect
		// what the user did there. Other unregistered API routes continue to return 501
		// from LoopbackHost.

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
