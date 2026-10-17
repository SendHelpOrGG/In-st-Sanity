using InStSanity.Platforms.Windows;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace InStSanity.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object. This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// Claim the single-instance mutex before any window, WebView or socket is created.
		SingleInstance.Acquire();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		if (!SingleInstance.IsPrimaryInstance)
		{
			// Another instance owns the loopback port and the WebView2 profile. Exit
			// deterministically: Application.Exit() before a window exists can leave the
			// process alive, and a second copy would only fail to bind the port.
			System.Diagnostics.Debug.WriteLine(
				"[In-st-Sanity] another instance is already running - exiting this one.");
			Environment.Exit(0);
			return;
		}

		base.OnLaunched(args);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
