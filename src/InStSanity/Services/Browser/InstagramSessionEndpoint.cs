using InStSanity.Services.WebHost;

namespace InStSanity.Services.Browser;

/// <summary>
/// <c>GET /api/instagram-session</c> — the signed-in account and the navigation state of
/// the native Instagram browser. The web layer polls this to draw the account-aware
/// top-bar button, the My Account settings card, and the Browser page address bar.
/// </summary>
public sealed class InstagramSessionEndpoint(InstagramBrowserWindowService browserWindow) : IApiEndpoint
{
	public string Route => "/api/instagram-session";

	public Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method != "GET" && request.Method != "HEAD")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		cancellationToken.ThrowIfCancellationRequested();
		var state = browserWindow.GetState();
		return Task.FromResult(HttpResponseInfo.Json(new
		{
			loggedIn = state.LoggedIn,
			username = state.Username,
			fullName = state.FullName,
			profilePicUrl = state.ProfilePicUrl,
			url = state.Url,
			canGoBack = state.CanGoBack,
			canGoForward = state.CanGoForward,
			browserOpen = state.Open,
		}));
	}
}
