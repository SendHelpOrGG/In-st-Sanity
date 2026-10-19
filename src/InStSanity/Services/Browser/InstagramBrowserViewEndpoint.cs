using System.Text.Json;
using InStSanity.Services.WebHost;

namespace InStSanity.Services.Browser;

/// <summary>
/// <c>POST /api/browser-view</c> — drives the native Instagram browser docked under the
/// themed Browser page: viewport region updates, address-bar navigation, history,
/// reload, show, and hide.
/// </summary>
public sealed class InstagramBrowserViewEndpoint(InstagramBrowserWindowService browserWindow) : IApiEndpoint
{
	const int MaxRequestBytes = 8 * 1024;

	public string Route => "/api/browser-view";

	public Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		if (request.Body.Length == 0 || request.Body.Length > MaxRequestBytes)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		string action;
		JsonElement body;
		try
		{
			using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 4 });
			if (document.RootElement.ValueKind != JsonValueKind.Object ||
				!document.RootElement.TryGetProperty("action", out var actionElement) ||
				actionElement.ValueKind != JsonValueKind.String)
			{
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
			}

			action = actionElement.GetString()?.Trim() ?? string.Empty;
			body = document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		cancellationToken.ThrowIfCancellationRequested();

		bool delivered;
		switch (action)
		{
			case "region":
			{
				if (!TryReadRegion(body, out var region))
				{
					return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
				}

				delivered = browserWindow.RequestRegion(region);
				break;
			}

			case "navigate":
			{
				if (!body.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
				{
					return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
				}

				var text = urlElement.GetString()?.Trim() ?? string.Empty;
				if (text.Length == 0 ||
					!InstagramBrowserAddressParser.TryParse(text, out var uri) ||
					!InstagramBrowserNavigationPolicy.IsAllowed(uri))
				{
					return Task.FromResult(HttpResponseInfo.Error("Use a secure Instagram or Meta URL in this browser.", 400));
				}

				delivered = browserWindow.RequestNavigate(uri.AbsoluteUri);
				break;
			}

			case "back":
			case "forward":
			case "reload":
			case "show":
			case "hide":
			{
				delivered = browserWindow.RequestCommand(action);
				break;
			}

			default:
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		return Task.FromResult(delivered
			? HttpResponseInfo.Json(new { ok = true })
			: HttpResponseInfo.Error("The Instagram browser is not ready.", 503));
	}

	static bool TryReadRegion(JsonElement body, out InstagramBrowserRegion region)
	{
		region = new InstagramBrowserRegion(0, 0, 0, 0);
		if (!TryReadNumber(body, "left", out var left) ||
			!TryReadNumber(body, "top", out var top) ||
			!TryReadNumber(body, "width", out var width) ||
			!TryReadNumber(body, "height", out var height))
		{
			return false;
		}

		if (width < 1 || height < 1 || left < -4096 || top < -4096 || width > 30000 || height > 30000)
		{
			return false;
		}

		region = new InstagramBrowserRegion(left, top, width, height);
		return true;
	}

	static bool TryReadNumber(JsonElement body, string name, out double value)
	{
		value = 0;
		return body.TryGetProperty(name, out var element) &&
			element.ValueKind == JsonValueKind.Number &&
			element.TryGetDouble(out value);
	}
}
