using InStSanity.Services.WebHost;
using Microsoft.Extensions.DependencyInjection;

namespace InStSanity;

public partial class App : Application
{
	readonly IServiceProvider _services;

	public App(IServiceProvider services, LoopbackHost webHost)
	{
		InitializeComponent();

		_services = services;

		// In-st-Sanity is a single-theme (dark) desktop app. Forcing the theme here keeps
		// the native Windows chrome (title bar, scrollbars, dialogs) dark so it does not
		// flash light while the web layer loads.
		UserAppTheme = AppTheme.Dark;

		// Start serving before the window exists: the WebView navigates to this host as
		// soon as MainPage is created.
		webHost.Start();

#if DEBUG
		// First-run diagnostics, visible in the Visual Studio Output window. If the UI does
		// not appear, these two lines say whether the host came up and whether the web layer
		// was found next to the executable.
		var indexPath = Path.Combine(webHost.WebRoot, "index.html");
		System.Diagnostics.Debug.WriteLine(
			$"[In-st-Sanity] host: {webHost.Url}  web root: {webHost.WebRoot}  index.html: {(File.Exists(indexPath) ? "found" : "MISSING")}");
#endif
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// INSTSANITY_HEADLESS=1 boots the backend without a WebView, so the HTTP surface can
		// be verified where the WebView2 runtime or a desktop session is unavailable.
		var headless = string.Equals(
			Environment.GetEnvironmentVariable("INSTSANITY_HEADLESS"),
			"1",
			StringComparison.Ordinal);

		Page root = headless
			? new HeadlessPage()
			: _services.GetRequiredService<MainPage>();

		return new Window(root)
		{
			Title = headless ? "In-st-Sanity (headless)" : "In-st-Sanity",
			Width = 1440,
			Height = 900,
			MinimumWidth = 1024,
			MinimumHeight = 680,
		};
	}
}
