using InStSanity.ViewModels;

namespace InStSanity;

public partial class MainPage : ContentPage
{
	public MainPage(MainPageViewModel viewModel)
	{
		InitializeComponent();

		BindingContext = viewModel;

		// Links the web layer opens in a new tab belong in the system browser (Windows only;
		// the project targets Windows, the guard keeps the intent explicit).
#if WINDOWS
		Platforms.Windows.WebViewBootstrapper.Attach(WebAppView);
#endif
	}

	void OnWebAppNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (e.Result == WebNavigationResult.Success)
		{
			StartupOverlay.IsVisible = false;
			return;
		}

		// The web host is not reachable: keep the overlay up and say so, instead of
		// leaving the user with a blank window.
		StartupSpinner.IsRunning = false;
		StartupSpinner.IsVisible = false;
		StartupMessage.Text =
			$"Could not load the app UI from {WebAppView.Source}.\n" +
			"Check the log for the host URL and try again.";
	}
}
