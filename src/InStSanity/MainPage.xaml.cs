using InStSanity.Platforms.Windows;
using InStSanity.Services.Browser;
using InStSanity.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace InStSanity;

public partial class MainPage : ContentPage
{
	const string InstagramHomeUrl = "https://www.instagram.com/";

	WebView? _instagramBrowserView;
	string? _currentInstagramUrl;

	public MainPage(MainPageViewModel viewModel, InstagramBrowserWindowService browserWindow)
	{
		InitializeComponent();

		BindingContext = viewModel;
		browserWindow.OpenRequested += OnInstagramBrowserOpenRequested;

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
			InstagramBrowserStatusLabel.Text = "Only secure Instagram and Meta pages can open in this in-app browser.";
			InstagramBrowserOverlay.IsVisible = true;
			return;
		}

		EnsureInstagramBrowserView();
		InstagramBrowserOverlay.IsVisible = true;

		if (targetUri is not null &&
			!string.Equals(_currentInstagramUrl, targetUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
		{
			NavigateInstagramBrowser(targetUri);
		}
		else if (targetUri is null)
		{
			InstagramBrowserStatusLabel.Text = "Reopened your Instagram browser session.";
		}

		UpdateInstagramBrowserNavigationButtons();
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
		InstagramBrowserAddressEntry.Text = uri.AbsoluteUri;
		InstagramBrowserStatusLabel.Text = $"Opening {uri.Host}…";
		_instagramBrowserView.Source = new UrlWebViewSource { Url = uri.AbsoluteUri };
	}

	void OnInstagramBrowserNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (Uri.TryCreate(e.Url, UriKind.Absolute, out var httpUri) &&
			httpUri.Scheme == Uri.UriSchemeHttp &&
			InstagramBrowserNavigationPolicy.IsAllowed(ToHttpsUri(httpUri)))
		{
			e.Cancel = true;
			NavigateInstagramBrowser(ToHttpsUri(httpUri));
			return;
		}

		if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) ||
			!InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			e.Cancel = true;
			InstagramBrowserStatusLabel.Text = "External sites open in your default browser; Instagram and Meta pages stay here.";
			if (Uri.TryCreate(e.Url, UriKind.Absolute, out var externalUri) &&
				(externalUri.Scheme == Uri.UriSchemeHttp || externalUri.Scheme == Uri.UriSchemeHttps))
			{
				_ = OpenExternalUriAsync(externalUri);
			}

			return;
		}

		_currentInstagramUrl = uri.AbsoluteUri;
		InstagramBrowserAddressEntry.Text = uri.AbsoluteUri;
		InstagramBrowserStatusLabel.Text = $"Loading {uri.Host}…";
	}

	void OnInstagramBrowserNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) &&
			InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			_currentInstagramUrl = uri.AbsoluteUri;
			InstagramBrowserAddressEntry.Text = uri.AbsoluteUri;
		}

		InstagramBrowserStatusLabel.Text = e.Result == WebNavigationResult.Success
			? "Instagram page loaded. Sign-in is handled directly by Instagram."
			: "Instagram could not load this page. Check your connection and try again.";
		UpdateInstagramBrowserNavigationButtons();
	}

	void OnInstagramBrowserBackClicked(object? sender, EventArgs e)
	{
		if (_instagramBrowserView?.CanGoBack == true)
		{
			_instagramBrowserView.GoBack();
		}
	}

	void OnInstagramBrowserForwardClicked(object? sender, EventArgs e)
	{
		if (_instagramBrowserView?.CanGoForward == true)
		{
			_instagramBrowserView.GoForward();
		}
	}

	void OnInstagramBrowserReloadClicked(object? sender, EventArgs e) => _instagramBrowserView?.Reload();

	void OnInstagramBrowserGoClicked(object? sender, EventArgs e) => NavigateFromAddressBar();

	void OnInstagramBrowserAddressCompleted(object? sender, EventArgs e) => NavigateFromAddressBar();

	void NavigateFromAddressBar()
	{
		var text = InstagramBrowserAddressEntry.Text?.Trim() ?? string.Empty;
		if (text.Length == 0)
		{
			return;
		}

		if (!TryParseAddress(text, out var uri))
		{
			InstagramBrowserStatusLabel.Text = "Enter a valid Instagram URL, profile name, or search term.";
			return;
		}

		if (InstagramBrowserNavigationPolicy.IsAllowed(uri))
		{
			NavigateInstagramBrowser(uri);
			return;
		}

		InstagramBrowserStatusLabel.Text = "Opening this external address in your default browser…";
		_ = OpenExternalUriAsync(uri);
	}

	static bool TryParseAddress(string text, out Uri uri)
	{
		var value = text.Trim();
		if (value.StartsWith('@'))
		{
			var username = value.TrimStart('@').Trim('/');
			if (username.Length == 0)
			{
				uri = new Uri(InstagramHomeUrl);
				return true;
			}

			value = $"https://www.instagram.com/{Uri.EscapeDataString(username)}/";
		}
		else if (value.StartsWith('/'))
		{
			value = $"https://www.instagram.com{value}";
		}
		else if (value.Contains('/') && !value.Contains('.') && !value.Contains("://", StringComparison.Ordinal))
		{
			value = $"https://www.instagram.com/{value.TrimStart('/')}";
		}
		else if (value.Any(char.IsWhiteSpace) || (!value.Contains('.') && !value.Contains('/')))
		{
			value = $"https://www.instagram.com/explore/search/keyword/?q={Uri.EscapeDataString(value)}";
		}
		else if (!value.Contains("://", StringComparison.Ordinal))
		{
			value = $"https://{value}";
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
			(parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
		{
			uri = new Uri(InstagramHomeUrl);
			return false;
		}

		if (parsed.Scheme == Uri.UriSchemeHttp &&
			InstagramBrowserNavigationPolicy.IsAllowed(ToHttpsUri(parsed)))
		{
			parsed = ToHttpsUri(parsed);
		}

		uri = parsed;
		return true;
	}

	static Uri ToHttpsUri(Uri uri)
	{
		var builder = new UriBuilder(uri)
		{
			Scheme = Uri.UriSchemeHttps,
			Port = -1,
		};
		return builder.Uri;
	}

	static async Task OpenExternalUriAsync(Uri uri)
	{
		try
		{
			await Launcher.Default.OpenAsync(uri);
		}
		catch
		{
			// Do not surface shell-launch errors over the Instagram page; the status label
			// already tells the user that the link was external.
		}
	}

	void UpdateInstagramBrowserNavigationButtons()
	{
		InstagramBrowserBackButton.IsEnabled = _instagramBrowserView?.CanGoBack == true;
		InstagramBrowserForwardButton.IsEnabled = _instagramBrowserView?.CanGoForward == true;
	}

	void OnInstagramBrowserCloseClicked(object? sender, EventArgs e) => CloseInstagramBrowser();

	void CloseInstagramBrowser()
	{
		InstagramBrowserOverlay.IsVisible = false;
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
