using System.Net;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using InStSanity.Services.Api;
using InStSanity.Services.Browser;
using InStSanity.Services.Instagram;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Web.WebView2.Core;

namespace InStSanity.Platforms.Windows;

/// <summary>Configures the MAUI WebViews after their WebView2 controls initialize.</summary>
internal static class WebViewBootstrapper
{
	static readonly ConditionalWeakTable<object, object> Hooked = new();
	sealed record VideoDownloadHistoryContext(string Shortcode, int? ContentIndex, int? VideoIndex, int? ContentCount);

	// Same-origin call Instagram's own web app makes; the x-ig-app-id header is the
	// public one every browser session uses. Page scripts cannot see HttpOnly cookies,
	// so the login check runs through CoreWebView2.CookieManager instead.
		const string AccountProbeScript =
		"(async()=>{try{const cs=(document.cookie.split('; ').find(x=>x.startsWith('csrftoken='))||'').slice(10);const h={'x-ig-app-id':'936619743392459','x-requested-with':'XMLHttpRequest','x-csrftoken':cs};const R='p,reel,reels,tv,explore,stories,direct,accounts,about,developer,legal,settings,terms,privacy,help,directory,emails,sessions,graphql,api,clips,guides'.split(',');let u=null;try{const r=await fetch('/api/v1/accounts/current_user/?edit=true',{credentials:'include',headers:h});if(r.ok){const j=await r.json();if(j&&j.user)u=j.user}}catch(e){}if(!u){try{const av=document.querySelector('img[alt*=\"profile photo\" i],img[alt*=\"profile picture\" i],img[alt*=\"your profile\" i]');const a=av&&av.closest('a');const href=(a&&a.getAttribute('href'))||'';const m=href.match(/^\\/([A-Za-z0-9_.]{1,30})\\/$/);if(m&&!R.includes(m[1].toLowerCase()))u={username:m[1],full_name:'',profile_pic_url:(av.currentSrc||av.src||'')}}catch(e){}}" +
		"if(!u){try{const pm=location.pathname.match(/^\\/([A-Za-z0-9_.]{1,30})\\/$/);const own=!!document.querySelector('a[href=\"/accounts/edit/\"],a[href^=\"/accounts/edit\"]');if(pm&&own&&!R.includes(pm[1].toLowerCase()))u={username:pm[1],full_name:'',profile_pic_url:''}}catch(e){}}" +
		"if(!u){const t=document.title||'';const m=t.match(/\\(@([A-Za-z0-9_.]+)\\)/)||t.match(/^([A-Za-z0-9_.]+) \\(/);if(m)u={username:m[1],full_name:'',profile_pic_url:''}}" +
		"const out=u?JSON.stringify({u:u.username||'',n:u.full_name||u.username||'',p:u.profile_pic_url||''}):'';window.__igAccountProbeResult=out;return out}catch(e){window.__igAccountProbeResult='';return ''}})()";

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

	static bool IsVideoDownload(CoreWebView2DownloadStartingEventArgs args)
	{
		try
		{
			var operation = args.DownloadOperation;
			if (operation.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
			{
				return true;
			}

			var uri = operation.Uri;
			if (Uri.TryCreate(uri, UriKind.Absolute, out var absoluteUri) && IsVideoExtension(Path.GetExtension(absoluteUri.AbsolutePath)))
			{
				return true;
			}

			return IsVideoExtension(Path.GetExtension(operation.ResultFilePath));
		}
		catch
		{
			return false;
		}
	}

	static bool IsVideoExtension(string? extension) => extension?.ToLowerInvariant() is ".mp4" or ".m4v" or ".mov" or ".webm" or ".avi" or ".mkv" or ".3gp" or ".mpeg" or ".mpg";

	static async Task ConfigureVideoDownloadAsync(CoreWebView2 core, CoreWebView2DownloadStartingEventArgs args)
	{
		var deferral = args.GetDeferral();
		try
		{
			// Capture which carousel video started the native menu download before
			// applying the user's folder and shortcode_vN filename preferences.
			var historyContext = await TryGetVideoDownloadHistoryContextAsync(core, args.DownloadOperation.Uri).ConfigureAwait(false);

			try { args.Handled = true; } catch { }
			try
			{
				var result = await core.ExecuteScriptAsync("(()=>{try{return JSON.stringify({folder:String(window.getConfiguredVideoDownloadFolder?.()||window.getConfiguredDownloadFolder?.()||''),noDuplicates:window.isNoDuplicatesEnabled?.()===true})}catch(e){return '{}'}})()");
				var preferencesJson = JsonSerializer.Deserialize<string>(result);
				using var preferences = JsonDocument.Parse(string.IsNullOrWhiteSpace(preferencesJson) ? "{}" : preferencesJson);
				var root = preferences.RootElement;
				var requestedFolder = root.TryGetProperty("folder", out var folderValue) && folderValue.ValueKind == JsonValueKind.String
					? folderValue.GetString()
					: null;
				var noDuplicates = root.TryGetProperty("noDuplicates", out var noDuplicatesValue) && noDuplicatesValue.ValueKind == JsonValueKind.True;
				if (string.IsNullOrWhiteSpace(requestedFolder))
				{
					throw new InvalidOperationException("The Settings download folder is unavailable.");
				}

				var folder = DownloadSelectedEndpoint.ResolveFolder(requestedFolder);
				Directory.CreateDirectory(folder);
				var fileName = BuildVideoDownloadFileName(historyContext, args.DownloadOperation);
				if (noDuplicates && historyContext is not null && FindExistingVideoPath(folder, fileName) is { } existingPath)
				{
					try { args.Cancel = true; } catch { }
					if (historyContext is not null)
					{
						var downloadedAtUtc = DateTimeOffset.UtcNow;
						try { downloadedAtUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(existingPath), TimeSpan.Zero); } catch { }
						var historyRecord = new
						{
							shortcode = historyContext.Shortcode,
							contentIndex = historyContext.ContentIndex,
							videoIndex = historyContext.VideoIndex,
							contentCount = historyContext.ContentCount,
							downloadedAtUtc,
							filename = Path.GetFileName(existingPath),
						};
						var recordJson = JsonSerializer.Serialize(historyRecord);
						try { await core.ExecuteScriptAsync($"window.__recordVideoDownloadHistory?.({recordJson})"); } catch { }
					}
					var notice = JsonSerializer.Serialize($"Video already exists: {Path.GetFileName(existingPath)} (duplicate skipped)");
					try { await core.ExecuteScriptAsync($"window.toast?.({notice})"); } catch { }
					return;
				}
				args.ResultFilePath = GetUniqueDownloadPath(folder, fileName);
			}
			catch
			{
				// Never let WebView2 silently fall back to its own Downloads folder.
				try { args.Cancel = true; } catch { }
				try { await core.ExecuteScriptAsync("window.toast?.('Video download canceled: could not use the folder selected in Settings')"); } catch { }
				return;
			}

			if (historyContext is not null)
			{
				var operation = args.DownloadOperation;
				operation.StateChanged += (_, _) =>
				{
					if (operation.State != CoreWebView2DownloadState.Completed)
					{
						return;
					}

					try
					{
						var resultPath = operation.ResultFilePath;
						var downloadedAtUtc = DateTimeOffset.UtcNow;
						try
						{
							if (File.Exists(resultPath))
							{
								downloadedAtUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(resultPath), TimeSpan.Zero);
							}
						}
						catch
						{
							// Keep the completion time if the filesystem timestamp is unavailable.
						}
						var historyRecord = new
						{
							shortcode = historyContext.Shortcode,
							contentIndex = historyContext.ContentIndex,
							videoIndex = historyContext.VideoIndex,
							contentCount = historyContext.ContentCount,
							downloadedAtUtc,
							filename = Path.GetFileName(resultPath),
						};
						var recordJson = JsonSerializer.Serialize(historyRecord);
						_ = core.ExecuteScriptAsync($"window.__recordVideoDownloadHistory?.({recordJson})");
					}
					catch
					{
						// History is best effort and must not interfere with the native download.
					}
				};
			}

			try { await core.ExecuteScriptAsync("window.showVideoDownloadNotice?.()"); } catch { }
		}
		finally
		{
			deferral.Complete();
		}
	}

	static string BuildVideoDownloadFileName(VideoDownloadHistoryContext? historyContext, CoreWebView2DownloadOperation operation)
	{
		var extension = GetVideoDownloadExtension(operation);
		if (historyContext is not null)
		{
			var videoNumber = Math.Max(0, historyContext.VideoIndex ?? historyContext.ContentIndex ?? 0) + 1;
			return $"{SafeVideoShortcode(historyContext.Shortcode)}_v{videoNumber}{extension}";
		}

		var requestedName = Path.GetFileName(operation.ResultFilePath);
		var requestedStem = Path.GetFileNameWithoutExtension(requestedName);
		if (string.IsNullOrWhiteSpace(requestedStem) || requestedStem.Equals("image", StringComparison.OrdinalIgnoreCase))
		{
			return "video" + extension;
		}

		return Path.GetFileNameWithoutExtension(requestedName) + extension;
	}

	static string GetVideoDownloadExtension(CoreWebView2DownloadOperation operation)
	{
		var mimeType = operation.MimeType?.Split(';', 2)[0].Trim().ToLowerInvariant();
		var mimeExtension = mimeType switch
		{
			"video/mp4" or "video/m4v" => ".mp4",
			"video/quicktime" => ".mov",
			"video/webm" => ".webm",
			"video/x-msvideo" => ".avi",
			"video/x-matroska" => ".mkv",
			"video/3gpp" => ".3gp",
			"video/mpeg" => ".mpeg",
			_ => string.Empty,
		};
		if (mimeExtension.Length > 0) return mimeExtension;

		var resultExtension = Path.GetExtension(operation.ResultFilePath);
		if (IsVideoExtension(resultExtension)) return resultExtension.ToLowerInvariant();
		if (Uri.TryCreate(operation.Uri, UriKind.Absolute, out var uriExtension) && IsVideoExtension(Path.GetExtension(uriExtension.AbsolutePath)))
		{
			return Path.GetExtension(uriExtension.AbsolutePath).ToLowerInvariant();
		}
		return ".mp4";
	}

	static string SafeVideoShortcode(string? shortcode)
	{
		var source = string.IsNullOrWhiteSpace(shortcode) ? "instagram" : shortcode.Trim();
		var safe = new string(source.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' ? character : '_').ToArray());
		return string.IsNullOrWhiteSpace(safe) ? "instagram" : safe;
	}

	static string? FindExistingVideoPath(string folder, string requestedName)
	{
		var expectedStem = Path.GetFileNameWithoutExtension(requestedName);
		if (string.IsNullOrWhiteSpace(expectedStem)) return null;
		foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
		{
			if (!IsVideoExtension(Path.GetExtension(path))) continue;
			var stem = Path.GetFileNameWithoutExtension(path);
			var suffixStart = stem.LastIndexOf(" (", StringComparison.Ordinal);
			if (suffixStart > 0 && stem.EndsWith(')') &&
				int.TryParse(stem.AsSpan(suffixStart + 2, stem.Length - suffixStart - 3), out _))
			{
				stem = stem[..suffixStart];
			}
			if (stem.Equals(expectedStem, StringComparison.OrdinalIgnoreCase)) return path;
		}
		return null;
	}

	static async Task<VideoDownloadHistoryContext?> TryGetVideoDownloadHistoryContextAsync(CoreWebView2 core, string sourceUrl)
	{
		try
		{
			var sourceJson = JsonSerializer.Serialize(sourceUrl);
			var result = await core.ExecuteScriptAsync($"window.__getVideoDownloadHistoryContext?.({sourceJson})||null");
			using var document = JsonDocument.Parse(result);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("shortcode", out var shortcodeValue) || shortcodeValue.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			int? contentIndex = root.TryGetProperty("contentIndex", out var contentIndexValue) &&
				contentIndexValue.ValueKind == JsonValueKind.Number && contentIndexValue.TryGetInt32(out var parsedContentIndex) && parsedContentIndex >= 0
				? parsedContentIndex
				: null;
			int? videoIndex = root.TryGetProperty("videoIndex", out var videoIndexValue) &&
				videoIndexValue.ValueKind == JsonValueKind.Number && videoIndexValue.TryGetInt32(out var parsedVideoIndex) && parsedVideoIndex >= 0
				? parsedVideoIndex
				: null;
			if (contentIndex is null && videoIndex is null)
			{
				return null;
			}

			int? contentCount = root.TryGetProperty("contentCount", out var countValue) &&
				countValue.ValueKind == JsonValueKind.Number && countValue.TryGetInt32(out var parsedCount) && parsedCount > 0
				? parsedCount
				: null;
			return new VideoDownloadHistoryContext(shortcodeValue.GetString()!, contentIndex, videoIndex, contentCount);
		}
		catch
		{
			return null;
		}
	}

	static string GetUniqueDownloadPath(string folder, string requestedName)
	{
		var fileName = Path.GetFileName(requestedName);
		if (string.IsNullOrWhiteSpace(fileName)) fileName = "video.mp4";
		var extension = Path.GetExtension(fileName);
		var stem = Path.GetFileNameWithoutExtension(fileName);
		if (string.IsNullOrWhiteSpace(stem)) stem = "video";
		var candidate = Path.Combine(folder, fileName);
		for (var suffix = 2; File.Exists(candidate); suffix++)
		{
			candidate = Path.Combine(folder, $"{stem} ({suffix}){extension}");
		}
		return candidate;
	}

	static void Configure(CoreWebView2 core, bool isInstagramBrowser)
	{
		core.DownloadStarting += (_sender, args) =>
		{
			if (!isInstagramBrowser && IsVideoDownload(args))
			{
				_ = ConfigureVideoDownloadAsync(core, args);
				return;
			}

			// Other WebView downloads remain blocked; photos use /api/download-selected.
			try { args.Cancel = true; } catch { }
		};

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
		// Keep DevTools available to the debugger; the page menu is filtered below.
		core.Settings.AreDevToolsEnabled = true;
#else
		core.Settings.AreDevToolsEnabled = false;
#endif
		// Preserve the intentional native HTML-video menu, but suppress all other
		// browser menus such as Back, Refresh, Save as, Print, and Inspect.
		core.Settings.AreDefaultContextMenusEnabled = true;
		core.ContextMenuRequested += (_, args) =>
		{
			if (args.ContextMenuTarget.Kind != CoreWebView2ContextMenuTargetKind.Video)
			{
				args.Handled = true;
			}
		};
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
				var inner = TryUnwrapScriptJson(await ExecOnUiAsync(core, AccountProbeScript));
				if (string.IsNullOrEmpty(inner))
				{
					// The probe keeps its payload on the page as well: read it back once it
					// settles, which works no matter how ExecuteScriptAsync treats promises.
					await Task.Delay(900);
					inner = TryUnwrapScriptJson(await ExecOnUiAsync(core, "window.__igAccountProbeResult||''"));
				}

				if (inner is not null && InstagramHttp.HasJsonValuePrefix(inner))
				{
					using var payload = JsonDocument.Parse(inner);
					if (payload.RootElement.TryGetProperty("u", out var u)) username = u.GetString() ?? string.Empty;
					if (payload.RootElement.TryGetProperty("n", out var n)) fullName = n.GetString() ?? string.Empty;
					if (payload.RootElement.TryGetProperty("p", out var p)) profilePicUrl = p.GetString() ?? string.Empty;
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

	/// <summary>Reports URL changes MAUI's Navigated event never sees (Instagram moves between pages with history.pushState).</summary>
	internal static async Task TrackSourceChangesAsync(WebView webView, Action<string> onSourceChanged)
	{
		var core = await WaitForCoreWebView2Async(webView, TimeSpan.FromSeconds(15));
		if (core is null)
		{
			return;
		}

		core.SourceChanged += (_, _) =>
		{
			try
			{
				onSourceChanged(core.Source ?? string.Empty);
			}
			catch
			{
				// state reporting must never throw into WebView2
			}
		};

		// history.pushState raises HistoryChanged; mirror the callback so SPA hops
		// update the address bar through either event.
		core.HistoryChanged += (_, _) =>
		{
			try
			{
				onSourceChanged(core.Source ?? string.Empty);
			}
			catch
			{
				// state reporting must never throw into WebView2
			}
		};
	}

	/// <summary>Runs a script in the WebView on the UI thread, returning its JSON-encoded result.</summary>
	internal static Task<string> ExecOnUiAsync(CoreWebView2 core, string script) =>
		MainThread.InvokeOnMainThreadAsync<string>(async () =>
		{
			try
			{
				return await core.ExecuteScriptAsync(script) ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		});

	/// <summary>Unwraps the JSON string ExecuteScriptAsync returns for script results that are strings.</summary>
	internal static string? TryUnwrapScriptJson(string? result)
	{
		if (string.IsNullOrEmpty(result) || result == "null" || !InstagramHttp.HasJsonValuePrefix(result))
		{
			return null;
		}

		try
		{
			using var outer = JsonDocument.Parse(result);
			return outer.RootElement.ValueKind == JsonValueKind.String
				? outer.RootElement.GetString()
				: null;
		}
		catch
		{
			return null;
		}
	}
}
