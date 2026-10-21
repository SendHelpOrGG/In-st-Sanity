using System.Security.Cryptography;
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
/// Optional <c>&amp;w=16…1024</c> asks for a downscaled copy (grid thumbnails), which is
/// resized once and cached under the width-tagged key. Responses carry an ETag and accept
/// byte ranges, so revisits answer <c>304</c>/<c>206</c> and video playback can seek; the
/// proxyed payload is content-addressed by URL, hence the long <c>immutable</c> caching.
/// </para>
/// <para>
/// Failures answer <c>502</c> with <c>{"ok":false,…}</c>; the web layer already has an
/// error path for exactly this (<c>handlePostImageError</c> → fallback artwork), so a
/// blocked or expired URL degrades the same way it did in the browser.
/// </para>
/// </remarks>
public sealed class MediaProxyEndpoint : IApiEndpoint
{
	const int MaxThumbnailWidth = 1024;

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

		var width = 0;
		var rawWidth = request.QueryValue("w");
		if (rawWidth is not null)
		{
			if (!int.TryParse(rawWidth, out width) || width < 16 || width > MaxThumbnailWidth)
			{
				return HttpResponseInfo.Error($"'w' must be an integer between 16 and {MaxThumbnailWidth}", 400);
			}
		}

		var payload = await _cache.TryReadAsync(uri, width, cancellationToken).ConfigureAwait(false);

		if (payload is null)
		{
			var original = await _cache.TryReadAsync(uri, 0, cancellationToken).ConfigureAwait(false);
			if (original is null)
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

					original = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

					if (original.Length == 0)
					{
						return HttpResponseInfo.Error("upstream_empty_response", 502);
					}

					await _cache.WriteAsync(uri, 0, original, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
				{
					_logger.LogWarning(ex, "Media proxy failed for {Url}", uri);
					return HttpResponseInfo.Error("upstream_unreachable", 502);
				}
			}

			payload = original;
			if (width > 0)
			{
				var resized = await ImageResizer.TryResizeAsync(original, width).ConfigureAwait(false);
				if (resized is not null)
				{
					payload = resized;
				}

				// Cache what we serve (resized when possible, original otherwise) so the
				// next grid visit never repeats the decode.
				await _cache.WriteAsync(uri, width, payload, cancellationToken).ConfigureAwait(false);
			}
		}

		var contentType = MediaTypeDetector.Detect(payload, uri);
		var etag = "\"" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()[..32] + "\"";

		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// The URL is content-addressed (signed CDN paths never get new bytes), so the
			// payload can be pinned; the ETag covers the rare forced revalidation.
			["Cache-Control"] = "public, max-age=31536000, immutable",
			["Accept-Ranges"] = "bytes",
			["ETag"] = etag,
		};

		if (_asAttachment)
		{
			headers["Content-Disposition"] = $"attachment; filename=\"{SuggestFileName(uri, payload)}\"";
		}

		if (string.Equals(request.Header("If-None-Match"), etag, StringComparison.Ordinal))
		{
			return HttpResponseInfo.Bytes(Array.Empty<byte>(), contentType, 304, headers);
		}

		if (LoopbackHost.TryParseRange(request.Header("Range"), payload.LongLength, out var start, out var end))
		{
			headers["Content-Range"] = $"bytes {start}-{end}/{payload.LongLength}";
			var slice = payload.AsSpan((int)start, (int)(end - start + 1)).ToArray();
			return HttpResponseInfo.Bytes(slice, contentType, 206, headers);
		}

		return HttpResponseInfo.Bytes(payload, contentType, 200, headers);
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
