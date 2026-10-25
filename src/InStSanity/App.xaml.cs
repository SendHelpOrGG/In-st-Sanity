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
		// the remaining native Windows chrome (scrollbars, dialogs) dark so it does not
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
		// Say which cut of the web layer this run actually serves. "legacy" here means the
		// executable is sitting next to an index.html from before the boot-screen rewrite,
		// which is the one thing that makes the app show the old loading screen.
		var index = File.Exists(indexPath) ? new FileInfo(indexPath) : null;
		var cut = "MISSING";
		if (index is not null)
		{
			cut = "legacy";
			try
			{
				using var reader = new StreamReader(indexPath);
				if (reader.ReadToEnd().Contains("data-ls-cut=\"v5-remastered-2\"", StringComparison.Ordinal))
				{
					cut = "v5-remastered-2";
				}
			}
			catch (IOException)
			{
				// leave it as "legacy" — the file exists but could not be read
			}
		}

		System.Diagnostics.Debug.WriteLine(
			$"[In-st-Sanity] host: {webHost.Url}  web root: {webHost.WebRoot}  index.html: " +
			(index is null
				? "MISSING"
				: $"{index.Length:N0} B, written {index.LastWriteTimeUtc:u}, boot cut {cut}"));
		System.Diagnostics.Debug.WriteLine(
			"[In-st-Sanity] api: " + string.Join(", ", webHost.Routes.OrderBy(route => route, StringComparer.OrdinalIgnoreCase)));
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
			Title = headless ? "In-st-Sanity (headless)" : string.Empty,
			Width = 1440,
			Height = 900,
			MinimumWidth = 1024,
			MinimumHeight = 680,
		};
	}
}
