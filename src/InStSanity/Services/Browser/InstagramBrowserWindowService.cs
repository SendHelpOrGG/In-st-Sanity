using Microsoft.Maui.ApplicationModel;

namespace InStSanity.Services.Browser;

/// <summary>
/// Bridge between the themed Browser page in the web layer and the native Instagram
/// browser hosted by <see cref="MainPage"/>. Commands marshal to the UI thread; the state
/// snapshot is what the <c>/api/instagram-session</c> route reads.
/// </summary>
public sealed class InstagramBrowserWindowService
{
	/// <summary>Raised on the UI thread to open the browser, optionally at a URL (empty keeps the current page).</summary>
	public event EventHandler<string>? OpenRequested;

	/// <summary>Raised on the UI thread to navigate the browser to a policy-approved URL.</summary>
	public event EventHandler<string>? NavigateRequested;

	/// <summary>Raised on the UI thread with a command: "back", "forward", "reload", "show", "hide", or "logout".</summary>
	public event EventHandler<string>? CommandRequested;

	/// <summary>Raised on the UI thread to dock the browser over the themed viewport rectangle.</summary>
	public event EventHandler<InstagramBrowserRegion>? RegionRequested;

	readonly object _stateLock = new();
	InstagramBrowserState _state = new(false, string.Empty, false, false, false, string.Empty, string.Empty, string.Empty);

	/// <summary>Opens the browser (empty URL keeps the current page). False when no desktop page is attached (headless runs).</summary>
	public bool RequestOpen(string? url = null) => Raise(OpenRequested, url?.Trim() ?? string.Empty);

	public bool RequestNavigate(string url) => Raise(NavigateRequested, url);

	public bool RequestCommand(string command) => Raise(CommandRequested, command);

	public bool RequestRegion(InstagramBrowserRegion region) => Raise(RegionRequested, region);

	public InstagramBrowserState GetState()
	{
		lock (_stateLock)
		{
			return _state;
		}
	}

	public void SetOpenState(bool open, string url, bool canGoBack, bool canGoForward)
	{
		lock (_stateLock)
		{
			_state = _state with
			{
				Open = open,
				Url = url,
				CanGoBack = canGoBack,
				CanGoForward = canGoForward,
			};
		}
	}

	public void SetAccount(bool loggedIn, string username, string fullName, string profilePicUrl)
	{
		lock (_stateLock)
		{
			_state = _state with
			{
				LoggedIn = loggedIn,
				Username = username,
				FullName = fullName,
				ProfilePicUrl = profilePicUrl,
			};
		}
	}

	/// <summary>Set by MainPage: copies the WebView2 profile's Instagram cookies into InstagramHttp. False when no browser page is attached.</summary>
	public Func<Task<bool>>? CookieSyncHandler { get; set; }

	readonly object _syncLock = new();
	DateTimeOffset _lastCookieSync = DateTimeOffset.MinValue;

	/// <summary>
	/// Pulls the Browser tab's Instagram session into the shared HTTP cookie jar, at most
	/// once per throttle window unless forced. False when nothing could be synced (no
	/// desktop page attached) — callers proceed anonymously.
	/// </summary>
	public async Task<bool> EnsureSessionCookiesAsync(bool force = false)
	{
		Func<Task<bool>>? handler;
		lock (_syncLock)
		{
			if (!force && DateTimeOffset.UtcNow - _lastCookieSync < TimeSpan.FromSeconds(45))
			{
				return true;
			}

			handler = CookieSyncHandler;
			_lastCookieSync = DateTimeOffset.UtcNow;
		}

		if (handler is null)
		{
			return false;
		}

		try
		{
			return await handler();
		}
		catch
		{
			return false;
		}
	}

	static bool Raise<T>(EventHandler<T>? handler, T args)
	{
		if (handler is null)
		{
			// No desktop page attached (headless runs): the caller should answer 503.
			return false;
		}

		if (!MainThread.IsMainThread)
		{
			MainThread.BeginInvokeOnMainThread(() => handler(null, args));
			return true;
		}

		handler(null, args);
		return true;
	}
}
