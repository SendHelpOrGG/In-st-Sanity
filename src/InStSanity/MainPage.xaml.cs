using InStSanity.Platforms.Windows;
using InStSanity.Services.Browser;
using InStSanity.Services.Instagram;
using InStSanity.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace InStSanity;

public partial class MainPage : ContentPage
{
	const string InstagramHomeUrl = "https://www.instagram.com/";

	readonly InstagramBrowserWindowService _browserWindow;
	readonly InstagramHttp _instagramHttp;
	WebView? _instagramBrowserView;
	string? _currentInstagramUrl;

	public MainPage(MainPageViewModel viewModel, InstagramBrowserWindowService browserWindow, InstagramHttp instagramHttp)
	{
		InitializeComponent();

		BindingContext = viewModel;
		_browserWindow = browserWindow;
		_instagramHttp = instagramHttp;
		browserWindow.OpenRequested += OnInstagramBrowserOpenRequested;
		browserWindow.NavigateRequested += OnInstagramBrowserNavigateRequested;
		browserWindow.CommandRequested += OnInstagramBrowserCommandRequested;
		browserWindow.RegionRequested += OnInstagramBrowserRegionRequested;
		browserWindow.CookieSyncHandler = () => MainThread.InvokeOnMainThreadAsync(SyncInstagramCookiesAsync);

		// Links opened by the app UI belong in the system browser (Windows only; the
		// guard keeps the platform-specific intent explicit).
#if WINDOWS
		WebViewBootstrapper.Attach(WebAppView);
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

	void OnInstagramBrowserOpenRequested(string requestedUrl)
	{
		var isFirstOpen = _instagramBrowserView is null;
		var targetUrl = requestedUrl.Trim();
		if (targetUrl.Length == 0 && isFirstOpen)
		{
			targetUrl = InstagramHomeUrl;
		}

		Uri? targetUri = null;
		if (targetUrl.Length > 0 &&
			(!Uri.TryCreate(targetUrl, UriKind.Absolute, out targetUri) ||
			 !InstagramBrowserNavigationPolicy.IsAllowed(targetUri)))
		{
			// Nothing valid to show: keep the docked page hidden rather than paging away.
			return;
		}

		EnsureInstagramBrowserView();
		InstagramBrowserOverlay.IsVisible = true;

		if (targetUri is not null &&
			!string.Equals(_currentInstagramUrl, targetUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
		{
			NavigateInstagramBrowser(targetUri);
		}

		UpdateBrowserState();
	}

	void OnInstagramBrowserNavigateRequested(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
			!InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			return;
		}

		EnsureInstagramBrowserView();
		InstagramBrowserOverlay.IsVisible = true;
		NavigateInstagramBrowser(uri);
		UpdateBrowserState();
	}

	void OnInstagramBrowserCommandRequested(string command)
	{
		switch (command)
		{
			case "back":
				if (_instagramBrowserView?.CanGoBack == true)
				{
					_instagramBrowserView.GoBack();
				}

				UpdateBrowserState();
				break;

			case "forward":
				if (_instagramBrowserView?.CanGoForward == true)
				{
					_instagramBrowserView.GoForward();
				}

				UpdateBrowserState();
				break;

			case "reload":
				_instagramBrowserView?.Reload();
				break;

			case "show":
				EnsureInstagramBrowserView();
				InstagramBrowserOverlay.IsVisible = true;
				UpdateBrowserState();
				break;

			case "hide":
				CloseInstagramBrowser();
				break;

			case "logout":
				_ = LogoutInstagramAsync();
				break;
		}
	}

	void OnInstagramBrowserRegionRequested(InstagramBrowserRegion region)
	{
		// Dock the native page over the themed viewport of the Browser tab. The region
		// arrives in window-relative device-independent pixels, matching MAUI layout.
		InstagramBrowserOverlay.Margin = new Thickness(region.Left, region.Top, 0, 0);
		InstagramBrowserOverlay.WidthRequest = Math.Max(1, region.Width);
		InstagramBrowserOverlay.HeightRequest = Math.Max(1, region.Height);
	}

	void EnsureInstagramBrowserView()
	{
		if (_instagramBrowserView is not null)
		{
			return;
		}

		var browserView = new WebView
		{
			HorizontalOptions = LayoutOptions.Fill,
			VerticalOptions = LayoutOptions.Fill,
		};
		browserView.Navigating += OnInstagramBrowserNavigating;
		browserView.Navigated += OnInstagramBrowserNavigated;
		InstagramBrowserHost.Content = browserView;
		_instagramBrowserView = browserView;

#if WINDOWS
		WebViewBootstrapper.AttachInstagramBrowser(browserView);
#endif
	}

	void NavigateInstagramBrowser(Uri uri)
	{
		if (_instagramBrowserView is null || !InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			return;
		}

		_currentInstagramUrl = uri.AbsoluteUri;
		_instagramBrowserView.Source = new UrlWebViewSource { Url = uri.AbsoluteUri };
	}

	void OnInstagramBrowserNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (Uri.TryCreate(e.Url, UriKind.Absolute, out var httpUri) &&
			httpUri.Scheme == Uri.UriSchemeHttp &&
			InstagramBrowserNavigationPolicy.IsAllowed(InstagramBrowserAddressParser.ToHttpsUri(httpUri)))
		{
			e.Cancel = true;
			NavigateInstagramBrowser(InstagramBrowserAddressParser.ToHttpsUri(httpUri));
			return;
		}

		if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) ||
			!InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			// External sites open in the system browser; Instagram and Meta pages stay here.
			e.Cancel = true;
			if (Uri.TryCreate(e.Url, UriKind.Absolute, out var externalUri) &&
				(externalUri.Scheme == Uri.UriSchemeHttp || externalUri.Scheme == Uri.UriSchemeHttps))
			{
				_ = OpenExternalUriAsync(externalUri);
			}

			return;
		}

		_currentInstagramUrl = uri.AbsoluteUri;
	}

	void OnInstagramBrowserNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) &&
			InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			_currentInstagramUrl = uri.AbsoluteUri;
		}

		UpdateBrowserState();
		_ = RefreshInstagramAccountAsync();
	}

	void UpdateBrowserState()
	{
		_browserWindow.SetOpenState(
			InstagramBrowserOverlay.IsVisible,
			_currentInstagramUrl ?? InstagramHomeUrl,
			_instagramBrowserView?.CanGoBack == true,
			_instagramBrowserView?.CanGoForward == true);
	}

	async Task RefreshInstagramAccountAsync()
	{
#if WINDOWS
		if (_instagramBrowserView is null)
		{
			return;
		}

		var snapshot = await WebViewBootstrapper.ProbeAccountAsync(_instagramBrowserView);
		if (snapshot is null)
		{
			return;
		}

		_browserWindow.SetAccount(snapshot.LoggedIn, snapshot.Username, snapshot.FullName, snapshot.ProfilePicUrl);
		if (snapshot.LoggedIn)
		{
			await SyncInstagramCookiesAsync();
		}
		else
		{
			_instagramHttp.ClearInstagramCookies();
		}
#endif
	}

	async Task LogoutInstagramAsync()
	{
#if WINDOWS
		// The WebView2 profile persists across runs, so an old Instagram session may exist
		// before any browser page was opened in this one. Create the view if needed, then
		// clear the cookies.
		EnsureInstagramBrowserView();
		if (_instagramBrowserView is null)
		{
			return;
		}

		await WebViewBootstrapper.ClearInstagramSessionAsync(_instagramBrowserView);
		_instagramHttp.ClearInstagramCookies();
		_browserWindow.SetAccount(false, string.Empty, string.Empty, string.Empty);
		UpdateBrowserState();
#endif
	}

	async Task<bool> SyncInstagramCookiesAsync()
	{
#if WINDOWS
		// The WebView2 profile is the login surface; the resolver reads the same session
		// through InstagramHttp so restricted posts resolve as the signed-in account.
		EnsureInstagramBrowserView();
		if (_instagramBrowserView is null)
		{
			return false;
		}

		var cookies = await WebViewBootstrapper.CopyInstagramCookiesAsync(_instagramBrowserView);
		if (cookies is null)
		{
			return false;
		}

		_instagramHttp.ApplySessionCookies(cookies);
		return true;
#else
		await Task.CompletedTask;
		return false;
#endif
	}

	static async Task OpenExternalUriAsync(Uri uri)
	{
		try
		{
			await Launcher.Default.OpenAsync(uri);
		}
		catch
		{
			// Do not surface shell-launch errors over the Instagram page.
		}
	}

	void CloseInstagramBrowser()
	{
		InstagramBrowserOverlay.IsVisible = false;
		UpdateBrowserState();
		WebAppView.Focus();
	}

	protected override bool OnBackButtonPressed()
	{
		if (InstagramBrowserOverlay.IsVisible)
		{
			CloseInstagramBrowser();
			return true;
		}

		return base.OnBackButtonPressed();
	}
}
