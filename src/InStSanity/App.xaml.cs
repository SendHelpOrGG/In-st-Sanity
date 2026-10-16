using Microsoft.Extensions.DependencyInjection;

namespace InStSanity;

public partial class App : Application
{
	readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();

		_services = services;

		// In-st-Sanity is a single-theme (dark) desktop app. Forcing the theme here keeps
		// the native Windows chrome (title bar, scrollbars, fly-out pane, text boxes)
		// in sync with the colours in Resources/Styles/Colors.xaml.
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// The shell (side panel + main area) comes from the DI container,
		// so it can be given view models and services through its constructor.
		return new Window(_services.GetRequiredService<AppShell>())
		{
			Title = "In-st-Sanity",
			Width = 1280,
			Height = 820,
			MinimumWidth = 1000,
			MinimumHeight = 640,
		};
	}
}
