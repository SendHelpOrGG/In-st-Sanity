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
		Platforms.Windows.SingleInstance.Acquire();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		if (!Platforms.Windows.SingleInstance.IsPrimaryInstance)
		{
			// Another instance owns the loopback port and the WebView2 profile.
			// (Focusing the existing window is planned for a later phase.)
			Exit();
			return;
		}

		base.OnLaunched(args);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
