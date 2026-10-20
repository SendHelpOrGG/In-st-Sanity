using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using InStSanity.Services.Browser;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Web.WebView2.Core;

namespace InStSanity.Platforms.Windows;

/// <summary>Configures the MAUI WebViews after their WebView2 controls initialize.</summary>
internal static class WebViewBootstrapper
{
	static readonly ConditionalWeakTable<object, object> Hooked = new();

	// Same-origin call Instagram's own web app makes; the x-ig-app-id header is the
	// public one every browser session uses. Page scripts cannot see HttpOnly cookies,
	// so the login check runs through CoreWebView2.CookieManager instead.
	const string AccountProbeScript =
		"(async()=>{try{const h={'x-ig-app-id':'936619743392459','x-requested-with':'XMLHttpRequest'};" +
		"let u=null;try{const r=await fetch('/api/v1/accounts/current_user/?edit=true',{credentials:'include',headers:h});" +
		"if(r.ok){const j=await r.json();if(j&&j.user)u=j.user}}catch(e){}" +
		"if(!u){const m=(document.title||'').match(/\\(@([^)]+)\\)/);if(m)u={username:m[1],full_name:'',profile_pic_url:''}}" +
		"return u?JSON.stringify({u:u.username||'',n:u.full_name||u.username||'',p:u.profile_pic_url||''}):''}catch(e){return ''}})()";

	static readonly string[] InstagramCookieUris =
	{
		"https://www.instagram.com/",
		"https://i.instagram.com/",
	};

	/// <summary>Routes links opened by the app UI to the user's system browser.</summary>
	public static void Attach(WebView webView) => Attach(webView, isInstagramBrowser: false);

	/// <summary>Keeps Instagram/Meta popups inside the native Instagram browser.</summary>
	public static void AttachInstagramBrowser(WebView webView) => Attach(webView, isInstagramBrowser: true);

	static void Attach(WebView webView, bool isInstagramBrowser)
	{
		// The handler (and with it CoreWebView2) appears after the page is first laid out.
		webView.HandlerChanged += (_, _) => TryHook(webView, isInstagramBrowser);
		TryHook(webView, isInstagramBrowser);
	}

	static void TryHook(WebView webView, bool isInstagramBrowser)
	{
		if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 platformView)
		{
			return;
		}

		if (Hooked.TryGetValue(platformView, out _))
		{
			return;
		}

		Hooked.Add(platformView, platformView);

		if (platformView.CoreWebView2 is not null)
		{
			Configure(platformView.CoreWebView2, isInstagramBrowser);
		}
		else
		{
			platformView.CoreWebView2Initialized += (sender, _) =>
			{
				if (sender.CoreWebView2 is not null)
				{
					Configure(sender.CoreWebView2, isInstagramBrowser);
				}
			};
		}
	}

	static void Configure(CoreWebView2 core, bool isInstagramBrowser)
	{
		core.NewWindowRequested += (sender, args) =>
		{
			args.Handled = true;

			if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				return;
			}

			if (isInstagramBrowser && InstagramBrowserNavigationPolicy.IsAllowed(uri))
			{
				sender.Navigate(uri.AbsoluteUri);
				return;
			}

			_ = Launcher.Default.OpenAsync(uri);
		};

#if DEBUG
		// Right-click → Inspect is useful while developing the port.
		core.Settings.AreDevToolsEnabled = true;
		core.Settings.AreDefaultContextMenusEnabled = true;
#else
		core.Settings.AreDevToolsEnabled = false;
		core.Settings.AreDefaultContextMenusEnabled = isInstagramBrowser;
#endif
	}

	/// <summary>
	/// Waits briefly for the WebView2 runtime to finish initialising, then returns the
	/// underlying CoreWebView2 (or null when the control never came up).
	/// </summary>
	internal static async Task<CoreWebView2?> WaitForCoreWebView2Async(WebView webView, TimeSpan timeout)
	{
		if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 platformView)
		{
			return null;
		}

		var deadline = DateTime.UtcNow + timeout;
		while (platformView.CoreWebView2 is null && DateTime.UtcNow < deadline)
		{
			await Task.Delay(50);
		}

		return platformView.CoreWebView2;
	}

	/// <summary>
	/// Reads the signed-in Instagram account from the persistent browser profile:
	/// the HttpOnly session cookie proves the login, the page script resolves the
	/// username, display name, and avatar.
	/// </summary>
	internal static async Task<InstagramAccountSnapshot?> ProbeAccountAsync(WebView webView)
	{
		var core = await WaitForCoreWebView2Async(webView, TimeSpan.FromSeconds(5));
		if (core is null)
		{
			return null;
		}

		var loggedIn = false;
		try
		{
			var cookies = await core.CookieManager.GetCookiesAsync("https://www.instagram.com/");
			foreach (var cookie in cookies)
			{
				if (string.Equals(cookie.Name, "sessionid", StringComparison.Ordinal) &&
					!string.IsNullOrEmpty(cookie.Value))
				{
					loggedIn = true;
					break;
				}
			}
		}
		catch
		{
			// Cookie enumeration can race the profile lock; the next probe retries.
		}

		string username = string.Empty;
		string fullName = string.Empty;
		string profilePicUrl = string.Empty;
		if (loggedIn)
		{
			try
			{
				var result = await core.ExecuteScriptAsync(AccountProbeScript);
				if (!string.IsNullOrEmpty(result) && result != "null")
				{
					using var outer = JsonDocument.Parse(result);
					var inner = outer.RootElement.ValueKind == JsonValueKind.String
						? outer.RootElement.GetString()
						: null;
					if (!string.IsNullOrEmpty(inner))
					{
						using var payload = JsonDocument.Parse(inner);
						if (payload.RootElement.TryGetProperty("u", out var u)) username = u.GetString() ?? string.Empty;
						if (payload.RootElement.TryGetProperty("n", out var n)) fullName = n.GetString() ?? string.Empty;
						if (payload.RootElement.TryGetProperty("p", out var p)) profilePicUrl = p.GetString() ?? string.Empty;
					}
				}
			}
			catch
			{
				// An unreadable probe still leaves the cookie-based login flag intact.
			}
		}

		if (fullName.Length == 0)
		{
			fullName = username;
		}

		return new InstagramAccountSnapshot(loggedIn, username, fullName, profilePicUrl);
	}

	/// <summary>
	/// Clears the Instagram session (cookies plus site storage) kept in the app's
	/// persistent WebView2 profile on this PC.
	/// </summary>
	internal static async Task ClearInstagramSessionAsync(WebView webView)
	{
		var core = await WaitForCoreWebView2Async(webView, TimeSpan.FromSeconds(5));
		if (core is null)
		{
			return;
		}

		foreach (var uri in InstagramCookieUris)
		{
			try
			{
				var cookies = await core.CookieManager.GetCookiesAsync(uri);
				foreach (var cookie in cookies)
				{
					core.CookieManager.DeleteCookie(cookie);
				}
			}
			catch
			{
				// Keep clearing the remaining origins even when one enumeration fails.
			}
		}

		try
		{
			await core.ExecuteScriptAsync("try{localStorage.clear();sessionStorage.clear()}catch(e){}");
		}
		catch
		{
			// Storage cleanup is best-effort; the cookies are what hold the session.
		}
	}

	/// <summary>Reads the Instagram session cookies (HttpOnly included) out of the persistent browser profile.</summary>
	internal static async Task<List<Cookie>?> CopyInstagramCookiesAsync(WebView webView)
	{
		var core = await WaitForCoreWebView2Async(webView, TimeSpan.FromSeconds(5));
		if (core is null)
		{
			return null;
		}

		var list = new List<Cookie>();
		foreach (var uri in InstagramCookieUris)
		{
			try
			{
				var cookies = await core.CookieManager.GetCookiesAsync(uri);
				foreach (var cookie in cookies)
				{
					if (string.IsNullOrEmpty(cookie.Name))
					{
						continue;
					}

					var domain = string.IsNullOrEmpty(cookie.Domain) ? "instagram.com" : cookie.Domain;
					var copy = new Cookie(cookie.Name, cookie.Value ?? string.Empty, cookie.Path ?? "/", domain)
					{
						Secure = cookie.IsSecure,
						HttpOnly = cookie.IsHttpOnly,
					};
					if (cookie.Expires > 0)
					{
						// WebView2 hands the expiry over as a Unix-epoch double (seconds).
						var seconds = cookie.Expires > 100_000_000_000 ? cookie.Expires / 1000.0 : cookie.Expires;
						copy.Expires = DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
					}

					list.Add(copy);
				}
			}
			catch
			{
				// keep copying from the remaining origins
			}
		}

		return list;
	}
}
