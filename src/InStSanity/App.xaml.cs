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
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_services.GetRequiredService<MainPage>())
		{
			Title = "In-st-Sanity",
			Width = 1440,
			Height = 900,
			MinimumWidth = 1024,
			MinimumHeight = 680,
		};
	}
}
