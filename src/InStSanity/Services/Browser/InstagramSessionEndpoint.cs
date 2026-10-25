using InStSanity.Services.Instagram;
using InStSanity.Services.WebHost;

namespace InStSanity.Services.Browser;

/// <summary>
/// <c>GET /api/instagram-session</c> — returns the signed-in account and the navigation
/// state of the native Instagram browser. The web layer polls this to draw the
/// account-aware controls. Add <c>?refresh=1</c> to force a fresh browser probe and cookie
/// synchronization before returning the snapshot.
/// </summary>
public sealed class InstagramSessionEndpoint(
	InstagramBrowserWindowService browserWindow,
	InstagramResolver resolver) : IApiEndpoint
{
	public string Route => "/api/instagram-session";

	public async Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method != "GET" && request.Method != "HEAD")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (string.Equals(request.QueryValue("refresh"), "1", StringComparison.Ordinal))
		{
			if (!await browserWindow.RefreshSessionAsync().ConfigureAwait(false))
			{
				return HttpResponseInfo.Error("The Instagram session could not be refreshed.", 503);
			}

			// A forced refresh is also the explicit freshness boundary used by Settings.
			resolver.ClearCache();
		}

		var state = browserWindow.GetState();
		return HttpResponseInfo.Json(new
		{
			loggedIn = state.LoggedIn,
			username = state.Username,
			fullName = state.FullName,
			profilePicUrl = state.ProfilePicUrl,
			url = state.Url,
			canGoBack = state.CanGoBack,
			canGoForward = state.CanGoForward,
			browserOpen = state.Open,
		});
	}
}
