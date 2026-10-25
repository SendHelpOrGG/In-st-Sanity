using InStSanity.Services.WebHost;

namespace InStSanity.Services.Browser;

/// <summary>
/// <c>POST /api/instagram-logout</c> — clears the Instagram session kept in the app's
/// persistent WebView2 profile on this PC before the request completes.
/// </summary>
public sealed class InstagramLogoutEndpoint(InstagramBrowserWindowService browserWindow) : IApiEndpoint
{
	public string Route => "/api/instagram-logout";

	public async Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		cancellationToken.ThrowIfCancellationRequested();
		var loggedOut = await browserWindow.LogoutAsync().ConfigureAwait(false);
		return loggedOut
			? HttpResponseInfo.Json(new { ok = true })
			: HttpResponseInfo.Error("The Instagram browser is not ready.", 503);
	}
}
