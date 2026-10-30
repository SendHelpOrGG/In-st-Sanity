using System.Text.Json;
using InStSanity.Platforms.Windows;
using InStSanity.Services.Browser;
using InStSanity.Services.Downloads;
using InStSanity.Services.Instagram;
using InStSanity.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace InStSanity;

public partial class MainPage : ContentPage
{
	const string InstagramHomeUrl = "https://www.instagram.com/";
	static readonly JsonSerializerOptions QueueJsonOptions = new(JsonSerializerDefaults.Web);

	readonly InstagramBrowserWindowService _browserWindow;
	readonly BrowserMediaQueueService _browserMediaQueue;
	readonly InstagramHttp _instagramHttp;
	readonly InstagramResolver _instagramResolver;
	WebView? _instagramBrowserView;
	string? _currentInstagramUrl;
	InstagramBrowserRegion? _instagramBrowserRegion;
	long _lastStickerCursorForwardedAtMs;
	string _cachedBrowserOverlayThemeJson = "null";
	DateTime _browserOverlayThemeCachedAtUtc = DateTime.MinValue;

	// Last confirmed identity: the page probe can miss it on pages without the profile
	// markup and the account API can be unavailable, so keep the name instead of flapping.
	const string AccountUsernameKey = "ig_account_username";
	const string AccountFullNameKey = "ig_account_full_name";
	const string AccountPicKey = "ig_account_pic";
	string _knownAccountUsername = string.Empty;
	string _knownAccountFullName = string.Empty;
	string _knownAccountPic = string.Empty;

	public MainPage(
		MainPageViewModel viewModel,
		InstagramBrowserWindowService browserWindow,
		BrowserMediaQueueService browserMediaQueue,
		InstagramHttp instagramHttp,
		InstagramResolver instagramResolver)
	{
		InitializeComponent();

		BindingContext = viewModel;
		_browserWindow = browserWindow;
		_browserMediaQueue = browserMediaQueue;
		_instagramHttp = instagramHttp;
		_instagramResolver = instagramResolver;
		browserWindow.OpenRequested += OnInstagramBrowserOpenRequested;
		browserWindow.NavigateRequested += OnInstagramBrowserNavigateRequested;
		browserWindow.CommandRequested += OnInstagramBrowserCommandRequested;
		browserWindow.RegionRequested += OnInstagramBrowserRegionRequested;
		browserWindow.CookieSyncHandler = () => MainThread.InvokeOnMainThreadAsync<bool>(SyncInstagramCookiesAsync);
		browserWindow.SessionRefreshHandler = () => MainThread.InvokeOnMainThreadAsync<bool>(RefreshInstagramAccountAsync);
		browserWindow.LogoutHandler = () => MainThread.InvokeOnMainThreadAsync<bool>(LogoutInstagramAsync);
		browserMediaQueue.StateChanged += OnBrowserMediaQueueStateChanged;
		browserMediaQueue.ItemCompleted += OnBrowserQueueItemCompleted;

		// Links opened by the app UI belong in the system browser (Windows only; the
		// guard keeps the platform-specific intent explicit).
#if WINDOWS
		WebViewBootstrapper.Attach(WebAppView);
#endif

		_ = InitializeInstagramSessionAsync();
	}

	/// <summary>
	/// The WebView2 profile keeps the Instagram sign-in between runs, so restore the
	/// account surfaces at startup and warm the Instagram page: the browser tab opens
	/// straight onto the main page and the top bar and Settings know the signed-in user
	/// without waiting for a navigation to trigger a probe.
	/// </summary>
	async Task InitializeInstagramSessionAsync()
	{
#if WINDOWS
		try
		{
			RestoreKnownAccount();

			// Give the window a beat to lay out before the WebView2 control initialises.
			await Task.Delay(250);
			EnsureInstagramBrowserView();
			if (_instagramBrowserView is null)
			{
				return;
			}

			if (_currentInstagramUrl is null)
			{
				NavigateInstagramBrowser(new Uri(InstagramHomeUrl));
			}

			await RefreshInstagramAccountAsync();
		}
		catch
		{
			// Best-effort: the URL watcher re-probes the account periodically.
		}
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

	void OnInstagramBrowserOpenRequested(object? sender, string requestedUrl)
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
		_ = SyncBrowserQueueToPageAsync(refreshTheme: true);
	}

	void OnInstagramBrowserNavigateRequested(object? sender, string url)
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
		_ = SyncBrowserQueueToPageAsync(refreshTheme: true);
	}

	void OnInstagramBrowserCommandRequested(object? sender, string command)
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
				_ = SyncBrowserQueueToPageAsync(refreshTheme: true);
				break;

			case "hide":
				CloseInstagramBrowser();
				break;

			case "logout":
				_ = LogoutInstagramAsync();
				break;
		}
	}

	void OnInstagramBrowserRegionRequested(object? sender, InstagramBrowserRegion region)
	{
		// Dock the native page over the themed viewport of the Browser tab. The region
		// arrives in window-relative device-independent pixels, matching MAUI layout.
		_instagramBrowserRegion = region;
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
		WebViewBootstrapper.AttachInstagramBrowser(browserView, OnBrowserOverlayMessage);
		// Instagram navigates with history.pushState, which never raises Navigated;
		// without this the address bar would freeze on the URL the browser opened with.
		_ = WebViewBootstrapper.TrackSourceChangesAsync(browserView, url =>
		{
			if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && InstagramBrowserNavigationPolicy.IsAllowed(uri))
			{
				_currentInstagramUrl = uri.AbsoluteUri;
				UpdateBrowserState();
			}
		});

		_ = WatchInstagramBrowserUrlAsync(browserView);
#endif
	}

	/// <summary>
	/// Instagram moves between pages without real navigations, so event-driven URL
	/// tracking can miss hops (profile clicks especially). Reading the live location
	/// once a second keeps the address bar truthful no matter how the page moved.
	/// </summary>
	async Task WatchInstagramBrowserUrlAsync(WebView view)
	{
		var ticks = 0;
		while (_instagramBrowserView == view)
		{
			await Task.Delay(1000);
			if (_instagramBrowserView != view)
			{
				return;
			}

			string json;
			try
			{
				var core = await WebViewBootstrapper.WaitForCoreWebView2Async(view, TimeSpan.FromMilliseconds(400));
				if (core is null)
				{
					continue;
				}

				json = await WebViewBootstrapper.ExecOnUiAsync(core, "location.href");
			}
			catch
			{
				continue;
			}

			if (Uri.TryCreate(WebViewBootstrapper.TryUnwrapScriptJson(json), UriKind.Absolute, out var uri) &&
				InstagramBrowserNavigationPolicy.IsAllowed(uri) &&
				!string.Equals(_currentInstagramUrl, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
			{
				_currentInstagramUrl = uri.AbsoluteUri;
				UpdateBrowserState();

				// Instagram navigates like a single-page app, so opening your profile
				// never raises Navigated; re-probe right away to pick the name up.
				_ = RefreshInstagramAccountAsync();
			}

			ticks++;
			if (ticks % 20 == 0)
			{
				// Keep the identity fresh without needing a navigation to trigger it.
				_ = RefreshInstagramAccountAsync();
			}
		}
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
		_ = SyncBrowserQueueToPageAsync(refreshTheme: true);
	}

	void UpdateBrowserState()
	{
		_browserWindow.SetOpenState(
			InstagramBrowserOverlay.IsVisible,
			_currentInstagramUrl ?? InstagramHomeUrl,
			_instagramBrowserView?.CanGoBack == true,
			_instagramBrowserView?.CanGoForward == true);
	}

	void OnBrowserMediaQueueStateChanged(BrowserMediaQueueSnapshot snapshot)
	{
		MainThread.BeginInvokeOnMainThread(() => _ = SyncBrowserQueueToPageAsync());
	}

	void OnBrowserQueueItemCompleted(BrowserQueueItemCompleted completed)
	{
		MainThread.BeginInvokeOnMainThread(() => _ = RecordBrowserQueueDownloadHistoryAsync(completed));
	}

	void OnBrowserOverlayMessage(string source, string message)
	{
		if (GetOverlayMessageAction(message) == "cursor-position")
		{
			if (IsTrustedInstagramBrowserSource(source) && TryReadBrowserCursorPosition(message, out var x, out var y))
			{
				MainThread.BeginInvokeOnMainThread(() => ForwardBrowserCursorToSticker(x, y));
			}
			return;
		}

		MainThread.BeginInvokeOnMainThread(() => ProcessBrowserOverlayMessage(source, message));
	}

	static string GetOverlayMessageAction(string message)
	{
		try
		{
			using var document = JsonDocument.Parse(message);
			return document.RootElement.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String
				? action.GetString() ?? string.Empty
				: string.Empty;
		}
		catch (JsonException)
		{
			return string.Empty;
		}
	}

	static bool IsTrustedInstagramBrowserSource(string? source)
	{
		if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Port != 443 || uri.UserInfo.Length != 0)
		{
			return false;
		}

		var host = uri.IdnHost;
		return host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase) ||
			host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase);
	}

	static bool TryReadBrowserCursorPosition(string message, out double x, out double y)
	{
		x = 0;
		y = 0;
		try
		{
			using var document = JsonDocument.Parse(message);
			var root = document.RootElement;
			return root.ValueKind == JsonValueKind.Object &&
				root.TryGetProperty("v", out var version) && version.ValueKind == JsonValueKind.Number &&
				version.TryGetInt32(out var versionNumber) && versionNumber == 1 &&
				root.TryGetProperty("x", out var xValue) && xValue.ValueKind == JsonValueKind.Number && xValue.TryGetDouble(out x) && double.IsFinite(x) &&
				root.TryGetProperty("y", out var yValue) && yValue.ValueKind == JsonValueKind.Number && yValue.TryGetDouble(out y) && double.IsFinite(y);
		}
		catch (JsonException)
		{
			return false;
		}
	}

	void ForwardBrowserCursorToSticker(double cursorX, double cursorY)
	{
		var region = _instagramBrowserRegion;
		if (!InstagramBrowserOverlay.IsVisible || region is null ||
			!double.IsFinite(region.Left) || !double.IsFinite(region.Top) ||
			!double.IsFinite(region.Width) || !double.IsFinite(region.Height) || region.Width <= 0 || region.Height <= 0 ||
			cursorX < -2 || cursorY < -2 || cursorX > region.Width + 2 || cursorY > region.Height + 2)
		{
			return;
		}

		var now = Environment.TickCount64;
		if (now - _lastStickerCursorForwardedAtMs < 30) return;
		_lastStickerCursorForwardedAtMs = now;
		var appX = region.Left + Math.Clamp(cursorX, 0, region.Width);
		var appY = region.Top + Math.Clamp(cursorY, 0, region.Height);
		_ = UpdateStickerCursorInWebAppAsync(appX, appY);
	}

	async Task UpdateStickerCursorInWebAppAsync(double x, double y)
	{
		try
		{
			var coordinates = JsonSerializer.Serialize(new[] { x, y });
			var script = $"(()=>{{const p={coordinates};window.updateStickerEyesFromExternalCursor?.(p[0],p[1]);return true}})()";
			await WebAppView.EvaluateJavaScriptAsync(script);
		}
		catch
		{
			// The main app WebView may be navigating or closing; the next pointer move retries.
		}
	}

	void ProcessBrowserOverlayMessage(string source, string message)
	{
		if (GetOverlayMessageAction(message) == "open-download-folder")
		{
			if (IsTrustedInstagramBrowserSource(source)) _ = OpenDownloadFolderFromBrowserAsync();
			return;
		}

		var command = _browserMediaQueue.HandleOverlayMessage(source, message);
		switch (command.Action)
		{
			case BrowserMediaOverlayAction.Ready:
				_ = SyncBrowserQueueToPageAsync();
				break;
			case BrowserMediaOverlayAction.StartDownload:
				_ = StartBrowserQueueDownloadAsync();
				break;
			case BrowserMediaOverlayAction.Rejected:
				_ = SyncBrowserQueueToPageAsync(command.Error);
				break;
		}
	}

	async Task OpenDownloadFolderFromBrowserAsync()
	{
		try
		{
			await WebAppView.EvaluateJavaScriptAsync("window.openDownloadFolder?.()");
		}
		catch
		{
			// The main app WebView can be navigating while the native Browser is open.
		}
	}

	async Task StartBrowserQueueDownloadAsync()
	{
#if WINDOWS
		try
		{
			var core = await WebViewBootstrapper.WaitForCoreWebView2Async(WebAppView, TimeSpan.FromSeconds(5));
			if (core is null)
			{
				await SyncBrowserQueueToPageAsync("bridge_unavailable");
				return;
			}
			var raw = await WebViewBootstrapper.ExecOnUiAsync(core,
				"(()=>{try{return JSON.stringify({photoFolder:String(window.getConfiguredDownloadFolder?.()||'~/Pictures/Instagram'),videoFolder:String(window.getConfiguredVideoDownloadFolder?.()||window.getConfiguredDownloadFolder?.()||'~/Pictures/Instagram'),noDuplicates:window.isNoDuplicatesEnabled?.()!==false})}catch(e){return '{}'}})()");
			var json = WebViewBootstrapper.TryUnwrapScriptJson(raw);
			using var preferences = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
			var root = preferences.RootElement;
			var photoFolder = TryGetPreferenceString(root, "photoFolder") ?? "~/Pictures/Instagram";
			var videoFolder = TryGetPreferenceString(root, "videoFolder") ?? photoFolder;
			var noDuplicates = !root.TryGetProperty("noDuplicates", out var duplicateValue) || duplicateValue.ValueKind != JsonValueKind.False;
			await Task.Run(() => _browserMediaQueue.DownloadAsync(photoFolder, videoFolder, noDuplicates));
		}
		catch
		{
			// Keep the selected media queued so a later retry can recover from a bridge error.
			await SyncBrowserQueueToPageAsync("bridge_unavailable");
		}
#else
		await Task.CompletedTask;
#endif
	}

	async Task SyncBrowserQueueToPageAsync(string? error = null, bool refreshTheme = false)
	{
#if WINDOWS
		var view = _instagramBrowserView;
		if (view is null) return;
		try
		{
			var core = await WebViewBootstrapper.WaitForCoreWebView2Async(view, TimeSpan.FromSeconds(2));
			if (core is null) return;
			var snapshot = _browserMediaQueue.GetSnapshot();
			var pageSnapshot = new
			{
				items = snapshot.Items.Select(item => new
				{
					item.Key,
					item.Shortcode,
					item.SlideIndex,
					item.ContentCount,
					item.IsVideo,
					item.ThumbnailUrl,
					item.Filename,
					item.Error,
				}),
				isDownloading = snapshot.IsDownloading,
				currentIndex = snapshot.CurrentIndex,
				total = snapshot.Total,
				status = snapshot.Status,
			};
			var snapshotJson = JsonSerializer.Serialize(pageSnapshot, QueueJsonOptions);
			var errorJson = JsonSerializer.Serialize(error, QueueJsonOptions);
			var themeJson = await GetBrowserOverlayThemeJsonAsync(refreshTheme);
			var script = $"window.__instSanityReceiveQueueState?.({snapshotJson},{errorJson},{themeJson})";
			await WebViewBootstrapper.ExecOnUiAsync(core, script);
		}
		catch
		{
			// The WebView can navigate or close while a queue update is being delivered.
		}
#endif
	}

	async Task<string> GetBrowserOverlayThemeJsonAsync(bool forceRefresh = false)
	{
#if WINDOWS
		if (!forceRefresh && DateTime.UtcNow - _browserOverlayThemeCachedAtUtc < TimeSpan.FromSeconds(10))
		{
			return _cachedBrowserOverlayThemeJson;
		}
		try
		{
			var core = await WebViewBootstrapper.WaitForCoreWebView2Async(WebAppView, TimeSpan.FromSeconds(2));
			if (core is null)
			{
				_cachedBrowserOverlayThemeJson = "null";
				_browserOverlayThemeCachedAtUtc = DateTime.UtcNow;
				return _cachedBrowserOverlayThemeJson;
			}
			var raw = await WebViewBootstrapper.ExecOnUiAsync(core,
				"(()=>{const s=getComputedStyle(document.body),v=(n,f)=>s.getPropertyValue(n).trim()||f;return JSON.stringify({panel:v('--t-sidebar-bg','rgba(17,24,32,.97)'),border:v('--t-line','#ffffff30'),ink:v('--t-ink','#f8fafc'),muted:v('--t-muted','#9fb1c1'),accent:v('--t-accent','#99f6e4'),accent2:v('--t-accent2','#2dd4bf'),buttonBg:v('--t-btn-bg','linear-gradient(135deg,#99f6e4,#2dd4bf)'),buttonInk:v('--t-btn-ink','#091318')})})()");
			var json = WebViewBootstrapper.TryUnwrapScriptJson(raw);
			if (string.IsNullOrWhiteSpace(json))
			{
				_cachedBrowserOverlayThemeJson = "null";
				_browserOverlayThemeCachedAtUtc = DateTime.UtcNow;
				return _cachedBrowserOverlayThemeJson;
			}
			using var document = JsonDocument.Parse(json);
			_cachedBrowserOverlayThemeJson = document.RootElement.GetRawText();
			_browserOverlayThemeCachedAtUtc = DateTime.UtcNow;
			return _cachedBrowserOverlayThemeJson;
		}
		catch
		{
			_cachedBrowserOverlayThemeJson = "null";
			_browserOverlayThemeCachedAtUtc = DateTime.UtcNow;
			return _cachedBrowserOverlayThemeJson;
		}
#else
		return "null";
#endif
	}

	async Task RecordBrowserQueueDownloadHistoryAsync(BrowserQueueItemCompleted completed)
	{
#if WINDOWS
		try
		{
			var core = await WebViewBootstrapper.WaitForCoreWebView2Async(WebAppView, TimeSpan.FromSeconds(5));
			if (core is null) return;
			var item = completed.Item;
			var result = completed.Result;
			var historyItem = new
			{
				shortcode = item.Shortcode,
				contentIndex = item.SlideIndex,
				photoIndex = (int?)null,
				videoIndex = (int?)null,
				contentCount = item.ContentCount,
				filename = result.Filename,
				browserQueue = true,
			};
			var file = new
			{
				ok = result.Ok,
				skipped = result.Skipped,
				downloadedAtUtc = result.DownloadedAtUtc,
				filename = result.Filename,
			};
			string script;
			if (item.IsVideo)
			{
				var videoRecord = new
				{
					shortcode = item.Shortcode,
					contentIndex = item.SlideIndex,
					videoIndex = (int?)null,
					contentCount = item.ContentCount,
					downloadedAtUtc = result.DownloadedAtUtc,
					filename = result.Filename,
					browserQueue = true,
				};
				script = $"window.__recordVideoDownloadHistory?.({JsonSerializer.Serialize(videoRecord, QueueJsonOptions)})";
			}
			else
			{
				script = $"window.__recordPhotoDownloadHistory?.({JsonSerializer.Serialize(historyItem, QueueJsonOptions)},{JsonSerializer.Serialize(file, QueueJsonOptions)})";
			}
			await WebViewBootstrapper.ExecOnUiAsync(core, script);
		}
		catch
		{
			// Saving a history mark is best-effort and never changes the download result.
		}
#endif
	}

	static string? TryGetPreferenceString(JsonElement root, string name) =>
		root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	async Task<bool> RefreshInstagramAccountAsync()
	{
#if WINDOWS
		if (_instagramBrowserView is null)
		{
			EnsureInstagramBrowserView();
			if (_currentInstagramUrl is null)
			{
				NavigateInstagramBrowser(new Uri(InstagramHomeUrl));
			}
		}

		if (_instagramBrowserView is null)
		{
			return false;
		}

		var snapshot = await WebViewBootstrapper.ProbeAccountAsync(_instagramBrowserView);
		if (snapshot is null)
		{
			return false;
		}

		var loggedIn = snapshot.LoggedIn;
		var username = snapshot.Username;
		var fullName = snapshot.FullName;
		var profilePicUrl = snapshot.ProfilePicUrl;
		var cookiesSynced = true;
		if (loggedIn)
		{
			cookiesSynced = await SyncInstagramCookiesAsync();
			if (!cookiesSynced)
			{
				// Never let the resolver keep using a previous account's cookies after a
				// transition if the current WebView profile could not be copied.
				_instagramHttp.ClearInstagramCookies();
			}

			if (cookiesSynced && (username.Length == 0 || profilePicUrl.Length == 0))
			{
				// The page probe can miss the identity as Instagram's markup and its
				// script guards move; the account API over the synced session is reliable.
				var identity = await _instagramHttp.FetchCurrentAccountAsync();
				if (identity is not null)
				{
					username = username.Length == 0 ? identity.Username : username;
					fullName = fullName.Length == 0 ? identity.FullName : fullName;
					profilePicUrl = profilePicUrl.Length == 0 ? identity.ProfilePicUrl : profilePicUrl;
				}
			}

			if (fullName.Length == 0)
			{
				fullName = username;
			}

			if (username.Length == 0)
			{
				username = _knownAccountUsername;
			}

			if (fullName.Length == 0)
			{
				fullName = _knownAccountFullName;
			}

			if (profilePicUrl.Length == 0)
			{
				profilePicUrl = _knownAccountPic;
			}

			if (username.Length > 0)
			{
				_knownAccountUsername = username;
				_knownAccountFullName = fullName;
				_knownAccountPic = profilePicUrl;
			}
		}
		else
		{
			_instagramHttp.ClearInstagramCookies();
			_knownAccountUsername = string.Empty;
			_knownAccountFullName = string.Empty;
			_knownAccountPic = string.Empty;
		}

		if (loggedIn && username.Length > 0)
		{
			SaveKnownAccount(username, fullName, profilePicUrl);
		}
		else if (!loggedIn)
		{
			ClearKnownAccount();
		}

		var previousAccount = _browserWindow.GetState();
		var identityChanged = previousAccount.LoggedIn != loggedIn ||
			(loggedIn && !string.Equals(previousAccount.Username, username, StringComparison.OrdinalIgnoreCase));
		if (previousAccount.LoggedIn &&
			(!loggedIn || !string.Equals(previousAccount.Username, username, StringComparison.OrdinalIgnoreCase)))
		{
			_browserMediaQueue.ResetForLogout();
		}
		if (identityChanged)
		{
			_instagramResolver.ClearCache();
		}

		_browserWindow.SetAccount(loggedIn, username, fullName, profilePicUrl);
		return !loggedIn || cookiesSynced;
#else
		await Task.CompletedTask;
		return false;
#endif
	}

	void RestoreKnownAccount()
	{
		try
		{
			var username = Preferences.Default.Get(AccountUsernameKey, string.Empty);
			if (username.Length == 0)
			{
				return;
			}

			_knownAccountUsername = username;
			_knownAccountFullName = Preferences.Default.Get(AccountFullNameKey, string.Empty);
			_knownAccountPic = Preferences.Default.Get(AccountPicKey, string.Empty);
			_browserWindow.SetAccount(
				true,
				username,
				_knownAccountFullName.Length > 0 ? _knownAccountFullName : username,
				_knownAccountPic);
		}
		catch
		{
			// best-effort cache of the last confirmed identity
		}
	}

	void SaveKnownAccount(string username, string fullName, string profilePicUrl)
	{
		try
		{
			Preferences.Default.Set(AccountUsernameKey, username);
			Preferences.Default.Set(AccountFullNameKey, fullName);
			Preferences.Default.Set(AccountPicKey, profilePicUrl);
		}
		catch
		{
			// best-effort cache of the last confirmed identity
		}
	}

	void ClearKnownAccount()
	{
		try
		{
			Preferences.Default.Remove(AccountUsernameKey);
			Preferences.Default.Remove(AccountFullNameKey);
			Preferences.Default.Remove(AccountPicKey);
		}
		catch
		{
			// best-effort cache of the last confirmed identity
		}
	}

	async Task<bool> LogoutInstagramAsync()
	{
#if WINDOWS
		// The WebView2 profile persists across runs, so an old Instagram session may exist
		// before any browser page was opened in this one. Create the view if needed, then
		// clear the cookies before acknowledging logout to the web layer.
		EnsureInstagramBrowserView();
		if (_instagramBrowserView is null)
		{
			return false;
		}

		_browserMediaQueue.ResetForLogout();
		await WebViewBootstrapper.ClearInstagramSessionAsync(_instagramBrowserView);
		_knownAccountUsername = string.Empty;
		_knownAccountFullName = string.Empty;
		_knownAccountPic = string.Empty;
		ClearKnownAccount();
		_instagramHttp.ClearInstagramCookies();
		_instagramResolver.ClearCache();
		_browserWindow.SetAccount(false, string.Empty, string.Empty, string.Empty);
		UpdateBrowserState();
		return true;
#else
		await Task.CompletedTask;
		return false;
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
