using InStSanity.Services.Instagram;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary>
/// Serves remote media through the app, so the web layer never talks to a third-party
/// image proxy.
/// </summary>
/// <remarks>
/// <para>
/// Two routes share this implementation, matching the contract the web layer calls:
/// <list type="bullet">
///   <item><description><c>/api/image?url=…</c> — inline cover/slide imagery.</description></item>
///   <item><description><c>/api/download-photo?url=…</c> — same payload as an attachment.</description></item>
/// </list>
/// </para>
/// <para>
/// Failures answer <c>502</c> with <c>{"ok":false,…}</c>; the web layer already has an
/// error path for exactly this (<c>handlePostImageError</c> → fallback artwork), so a
/// blocked or expired URL degrades the same way it did in the browser.
/// </para>
/// </remarks>
public sealed class MediaProxyEndpoint : IApiEndpoint
{
	readonly InstagramHttp _http;
	readonly MediaCache _cache;
	readonly ILogger _logger;
	readonly bool _asAttachment;

	public MediaProxyEndpoint(
		InstagramHttp http,
		MediaCache cache,
		ILogger<MediaProxyEndpoint> logger,
		string route,
		bool asAttachment = false)
	{
		_http = http;
		_cache = cache;
		_logger = logger;
		_asAttachment = asAttachment;
		Route = route;
	}

	public string Route { get; }

	public async Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method is not ("GET" or "HEAD"))
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		var rawUrl = request.QueryValue("url");
		if (string.IsNullOrWhiteSpace(rawUrl))
		{
			return HttpResponseInfo.Error("missing 'url' query parameter", 400);
		}

		if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			return HttpResponseInfo.Error("'url' must be an absolute http(s) URL", 400);
		}

		var payload = await _cache.TryReadAsync(uri, cancellationToken).ConfigureAwait(false);

		if (payload is null)
		{
			try
			{
				using var response = await _http.Client
					.SendAsync(_http.CreateMediaGet(uri), HttpCompletionOption.ResponseContentRead, cancellationToken)
					.ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
				{
					_logger.LogInformation(
						"Media proxy: upstream {Status} for {Url}",
						(int)response.StatusCode,
						uri);

					return HttpResponseInfo.Error($"upstream_status_{(int)response.StatusCode}", 502);
				}

				payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

				if (payload.Length == 0)
				{
					return HttpResponseInfo.Error("upstream_empty_response", 502);
				}

				await _cache.WriteAsync(uri, payload, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
			{
				_logger.LogWarning(ex, "Media proxy failed for {Url}", uri);
				return HttpResponseInfo.Error("upstream_unreachable", 502);
			}
		}

		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Cache-Control"] = "public, max-age=3600",
		};

		if (_asAttachment)
		{
			headers["Content-Disposition"] = $"attachment; filename=\"{SuggestFileName(uri, payload)}\"";
		}

		return HttpResponseInfo.Bytes(payload, MediaTypeDetector.Detect(payload, uri), 200, headers);
	}

	/// <summary>Derives a sensible download name from the URL, falling back to the payload type.</summary>
	internal static string SuggestFileName(Uri uri, byte[] payload)
	{
		var name = Path.GetFileName(uri.AbsolutePath);
		var extension = Path.GetExtension(name);

		if (string.IsNullOrWhiteSpace(name) || extension.Length is < 2 or > 6)
		{
			return "instagram_media" + MediaTypeDetector.ExtensionFor(payload);
		}

		return name;
	}
}
