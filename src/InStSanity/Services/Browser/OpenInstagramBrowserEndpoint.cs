using System.Text.Json;
using InStSanity.Services.WebHost;

namespace InStSanity.Services.Browser;

/// <summary>Opens Instagram's real website in the native, persistent WebView2 browser overlay.</summary>
public sealed class OpenInstagramBrowserEndpoint(InstagramBrowserWindowService browserWindow) : IApiEndpoint
{
	private const int MaxRequestBytes = 8 * 1024;
	private const int MaxUrlLength = 2048;

	public string Route => "/api/open-instagram-browser";

	public Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		if (request.Body.Length > MaxRequestBytes)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		var url = string.Empty;
		if (request.Body.Length > 0)
		{
			try
			{
				using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 4 });
				if (document.RootElement.ValueKind != JsonValueKind.Object)
				{
					return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
				}

				if (document.RootElement.TryGetProperty("url", out var urlElement))
				{
					if (urlElement.ValueKind != JsonValueKind.String)
					{
						return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
					}

					var requestedUrl = urlElement.GetString()?.Trim();
					if (!string.IsNullOrWhiteSpace(requestedUrl))
					{
						url = requestedUrl;
					}
				}
			}
			catch (JsonException)
			{
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
			}
		}

		if (url.Length > MaxUrlLength)
		{
			return Task.FromResult(HttpResponseInfo.Error("Use a secure Instagram or Meta URL in this browser.", 400));
		}

		Uri? uri = null;
		if (!string.IsNullOrWhiteSpace(url) &&
			(!Uri.TryCreate(url, UriKind.Absolute, out uri) || !InstagramBrowserNavigationPolicy.IsAllowed(uri)))
		{
			return Task.FromResult(HttpResponseInfo.Error("Use a secure Instagram or Meta URL in this browser.", 400));
		}

		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(browserWindow.RequestOpen(uri?.AbsoluteUri ?? string.Empty)
			? HttpResponseInfo.Json(new { ok = true })
			: HttpResponseInfo.Error("The Instagram browser is not ready.", 503));
	}
}
