using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Instagram;

/// <summary>
/// Last-resort media extraction through save-free.net's public search endpoint.
/// </summary>
/// <remarks>
/// <para>
/// This is a direct port of <c>fetch_via_savefree</c>, <c>decode_hunter_js</c> and
/// <c>is_video_cdn_url</c> from the original <c>server.py</c>. It exists for the posts
/// Instagram will not serve to anyone without a session: the third party has already done
/// the logged-in work, and hands back plain CDN URLs.
/// </para>
/// <para>
/// The response is not JSON-shaped HTML as it looks. The endpoint answers
/// <c>{"status":"ok","data":"…"}</c> where <c>data</c> is a Hunter-encoded JS string
/// (<c>eval(function(p,a,c,k,e,d){…}("…",62,63,"charset",offset,radix,14))</c>); decoding it
/// yields <c>innerHTML = "…"</c> whose value is the real result markup. Both steps are
/// reproduced below, including the quirk that decoding is done by replacing charset
/// characters with their decimal indices before reading the chunk as a number.
/// </para>
/// <para>
/// Deliberately a fallback, and deliberately single-attempt: the Python original retried
/// once after 200 ms, but the grid gives a batch 9 s and this call already gets 8 s, so a
/// second attempt could only ever fire after the caller has stopped listening.
/// </para>
/// </remarks>
public sealed class SaveFreeClient
{
	public const string Endpoint = "https://save-free.net/api/ajaxSearch";

	const string BrowserUserAgent =
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

	/// <summary>The alphabet hunter-encoded payloads are decoded with.</summary>
	const string HunterAlphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+/";

	/// <summary>
	/// Capped below the original 8 s because the resolver asks this only after the two
	/// page fetches, and an opened post has 12 s in total.
	/// </summary>
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

	/// <summary><c>}\("data",62,"charset",offset,radix,14\)\)</c></summary>
	static readonly Regex HunterCall = new(
		@"\}\(""([^""]+)"",(\d+),""([^""]+)"",(\d+),(\d+),(\d+)\)\)",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex InnerHtmlAssign = new(
		"innerHTML\\s*=\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex ListItem = new(
		"<li>([\\s\\S]*?)</li>",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

	static readonly Regex OptionValue = new(
		"<option value=\"([^\"]+)\">([^<]+)</option>",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

	static readonly Regex ThumbnailSrc = new(
		"<img[^>]+src=\"([^\"]+)\"",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

	static readonly Regex DownloadHref = new(
		"<a[^>]+href=\"([^\"]+)\"[^>]*class=\"[^\"]*abutton",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>A signed download link: <c>…token=header.payload.…</c>.</summary>
	static readonly Regex TokenParam = new(
		"token=([^.&]+)\\.([^.&]+)\\.",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex EfgParam = new(
		@"[?&]efg=([^&""'\s]+)",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	/// <summary>
	/// Base64 fragments that appear inside the <c>efg</c> parameter of a video CDN URL.
	/// <c>dmlkZW8</c> is "video", <c>LnZpZGVv</c> is ".Video", <c>X3ZpZGVv</c> is "_video",
	/// <c>Q0xJUFM</c> is "CLIPS".
	/// </summary>
	static readonly string[] VideoEfgMarkers =
	{
		"dmlkZW8", "LnZpZGVv", "X3ZpZGVv", "Q0xJUFM", "IkNMSVBT",
	};

	readonly InstagramHttp _http;
	readonly ILogger<SaveFreeClient> _logger;

	public SaveFreeClient(InstagramHttp http, ILogger<SaveFreeClient> logger)
	{
		_http = http;
		_logger = logger;
	}

	/// <summary>
	/// Asks save-free for the slides of one post. Returns an empty list on any failure —
	/// a resolver that throws here would turn a missing cover into a failed batch.
	/// </summary>
	public async Task<IReadOnlyList<ResolvedMedia>> FetchAsync(
		string shortcode,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(shortcode))
		{
			return Array.Empty<ResolvedMedia>();
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(Timeout);

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["q"] = $"https://www.instagram.com/p/{shortcode}/",
					["t"] = "media",
					["lang"] = "en",
					["v"] = "v2",
				}),
			};

			request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
			request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
			request.Headers.TryAddWithoutValidation("Origin", "https://save-free.net");
			request.Headers.TryAddWithoutValidation("Referer", "https://save-free.net/en");
			request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

			using var response = await _http.Client
				.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				_logger.LogInformation(
					"save-free: upstream {Status} for {Shortcode}",
					(int)response.StatusCode,
					shortcode);

				return Array.Empty<ResolvedMedia>();
			}

			var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
			var slides = Parse(body);

			if (slides.Count > 0)
			{
				_logger.LogInformation(
					"save-free: {Count} slide(s) for {Shortcode}",
					slides.Count,
					shortcode);
			}

			return slides;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogInformation("save-free: timed out for {Shortcode}", shortcode);
			return Array.Empty<ResolvedMedia>();
		}
		catch (HttpRequestException ex)
		{
			_logger.LogInformation(ex, "save-free: unreachable for {Shortcode}", shortcode);
			return Array.Empty<ResolvedMedia>();
		}
	}

	// ── response parsing ───────────────────────────────────────────────────────

	IReadOnlyList<ResolvedMedia> Parse(string body)
	{
		JsonNode? root;
		try
		{
			root = JsonNode.Parse(body);
		}
		catch (JsonException)
		{
			return Array.Empty<ResolvedMedia>();
		}

		// Indexing a JsonNode that turned out to be an array or a scalar throws, so the
		// shape is checked before anything is read from it.
		if (root is not JsonObject payload ||
			payload["status"] is not JsonValue status ||
			Text(status) != "ok")
		{
			return Array.Empty<ResolvedMedia>();
		}

		var raw = Text(payload["data"]);
		if (raw.Length == 0)
		{
			return Array.Empty<ResolvedMedia>();
		}

		// The payload is JS that assigns the result markup to innerHTML. If the decode
		// fails the body is parsed as-is, which is what the original did: some answers
		// come back unencoded.
		var decoded = DecodeHunterJs(raw);
		string innerHtml;
		if (decoded.Length > 0)
		{
			var assignment = InnerHtmlAssign.Match(decoded);
			innerHtml = assignment.Success
				? Unquote(JsonUnescape(assignment.Groups[1].Value))
				: Unquote(decoded);
		}
		else
		{
			innerHtml = raw;
		}

		var slides = new List<ResolvedMedia>();
		foreach (Match item in ListItem.Matches(innerHtml))
		{
			var fragment = item.Groups[1].Value;

			var thumbnail = ThumbnailSrc.Match(fragment);
			var download = DownloadHref.Match(fragment);
			var options = OptionValue.Matches(fragment);

			// The first <option> is the highest quality download; the anchor and the
			// thumbnail are only there when the option list is missing.
			var bestToken =
				options.Count > 0 ? options[0].Groups[1].Value :
				download.Success ? download.Groups[1].Value :
				thumbnail.Success ? thumbnail.Groups[1].Value :
				string.Empty;

			var thumbnailUrl = thumbnail.Success ? ExtractJwtUrl(thumbnail.Groups[1].Value) : string.Empty;
			var bestUrl = ExtractJwtUrl(bestToken);

			var isVideo =
				fragment.Contains("icon-dlvideo", StringComparison.Ordinal) ||
				fragment.Contains("Download Video", StringComparison.Ordinal) ||
				IsVideoCdnUrl(bestUrl) ||
				IsVideoCdnUrl(thumbnailUrl);

			// A video keeps the download URL for playback and the thumbnail as its poster;
			// without a thumbnail the download URL doubles as the poster.
			var imageUrl = isVideo && thumbnailUrl.Length > 0
				? thumbnailUrl
				: bestUrl.Length > 0 ? bestUrl : thumbnailUrl;

			if (imageUrl.Length == 0)
			{
				continue;
			}

			slides.Add(new ResolvedMedia
			{
				ImgUrl = imageUrl,
				ThumbnailUrl = thumbnailUrl.Length > 0 ? thumbnailUrl : imageUrl,
				RawUrl = imageUrl,
				IsVideo = isVideo,
				VideoUrl = isVideo ? bestUrl : string.Empty,
			});
		}

		return slides;
	}

	/// <summary>
	/// Decodes a Hunter-encoded string (the <c>eval(function(p,a,c,k,e,d){…})</c> trick).
	/// </summary>
	/// <remarks>
	/// Ported name-for-name from <c>decode_hunter_js</c>, including the sequential
	/// character replacement, because the encoding is ambiguous otherwise: the payload is
	/// a charset-index alphabet where every character is a digits string in base
	/// <c>radix</c>, and the chunks are separated by <c>charset[radix]</c>.
	/// </remarks>
	static string DecodeHunterJs(string encoded)
	{
		var match = HunterCall.Match(encoded);
		if (!match.Success)
		{
			return string.Empty;
		}

		var payload = match.Groups[1].Value;
		var charset = match.Groups[3].Value;

		if (!int.TryParse(match.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) ||
			!int.TryParse(match.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var radix) ||
			radix <= 1 ||
			radix >= charset.Length)
		{
			return string.Empty;
		}

		var delimiter = charset[radix];
		var decoded = new StringBuilder(payload.Length);

		var index = 0;
		while (index < payload.Length)
		{
			var next = payload.IndexOf(delimiter, index);
			if (next < 0)
			{
				break;
			}

			var chunk = DecodeChunk(payload[index..next], charset, radix, offset);
			if (chunk.Length > 0)
			{
				decoded.Append(chunk);
			}

			index = next + 1;
		}

		return decoded.ToString();
	}

	static string DecodeChunk(string chunk, string charset, int radix, int offset)
	{
		// Characters of the chunk are replaced by their index in the charset, in order,
		// exactly like the original loop; the result is then a number in base [radix].
		var digits = new StringBuilder(chunk);
		for (var position = 0; position < charset.Length; position++)
		{
			digits = digits.Replace(
				charset[position].ToString(),
				position.ToString(CultureInfo.InvariantCulture));
		}

		var basis = HunterAlphabet[..radix];
		var value = BigInteger.Zero;
		var place = BigInteger.One;

		for (var i = digits.Length - 1; i >= 0; i--)
		{
			var digit = basis.IndexOf(digits[i]);
			if (digit >= 0)
			{
				value += digit * place;
			}

			place *= radix;
		}

		// Python's chr() takes the whole Unicode range, so the port does too: the payload
		// is markup, but truncating at the BMP would silently drop any character above it.
		var character = value - offset;
		if (character < 0 || character > 0x10FFFF)
		{
			return string.Empty;
		}

		var code = (int)character;
		return code <= char.MaxValue
			? ((char)code).ToString()
			: char.ConvertFromUtf32(code);
	}

	/// <summary>
	/// Unwraps the URL out of a save-free download token: the token's middle segment is a
	/// base64url JSON payload carrying the real <c>url</c>.
	/// </summary>
	static string ExtractJwtUrl(string token)
	{
		if (string.IsNullOrEmpty(token))
		{
			return string.Empty;
		}

		var match = TokenParam.Match(token);
		if (!match.Success)
		{
			return token;
		}

		try
		{
			var decoded = Base64UrlDecode(match.Groups[2].Value);
			if (decoded.Length == 0)
			{
				return token;
			}

			if (JsonNode.Parse(decoded) is not JsonObject claims)
			{
				return token;
			}

			var target = Text(claims["url"]);
			return target.Length > 0 ? target : string.Empty;
		}
		catch (JsonException)
		{
			return token;
		}
	}

	/// <summary>
	/// Whether a CDN URL points at a video. Instagram does not always say so in markup,
	/// but the CDN URL carries the evidence: an <c>/o1/v/t</c> path, an <c>.mp4</c>
	/// extension, or an <c>efg</c> parameter whose base64 fragment names the media type.
	/// </summary>
	static bool IsVideoCdnUrl(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return false;
		}

		if (url.Contains(".mp4", StringComparison.Ordinal) ||
			url.Contains("/o1/v/t", StringComparison.Ordinal))
		{
			return true;
		}

		var match = EfgParam.Match(url);
		if (!match.Success)
		{
			return false;
		}

		var raw = match.Groups[1].Value;
		try
		{
			raw = Uri.UnescapeDataString(raw);
		}
		catch (UriFormatException)
		{
			// Leave it escaped and let the markers below have their try.
		}

		foreach (var marker in VideoEfgMarkers)
		{
			if (raw.Contains(marker, StringComparison.Ordinal))
			{
				return true;
			}
		}

		var decoded = Base64UrlDecode(raw).ToLowerInvariant();
		return decoded.Contains("video", StringComparison.Ordinal) ||
			decoded.Contains("clips.", StringComparison.Ordinal);
	}

	// ── small helpers ──────────────────────────────────────────────────────────

	/// <summary>Base64url → text; empty when the fragment is not decodable.</summary>
	static string Base64UrlDecode(string value)
	{
		try
		{
			var normalised = value.Replace('-', '+').Replace('_', '/');
			var padding = (4 - normalised.Length % 4) % 4;
			return Encoding.UTF8.GetString(Convert.FromBase64String(normalised + new string('=', padding)));
		}
		catch (FormatException)
		{
			return string.Empty;
		}
	}

	/// <summary>Percent-decoding that never throws on malformed input.</summary>
	static string Unquote(string value)
	{
		try
		{
			return Uri.UnescapeDataString(value);
		}
		catch (UriFormatException)
		{
			return value;
		}
	}

	/// <summary>Undoes the JS string escaping around the captured <c>innerHTML</c>.</summary>
	static string JsonUnescape(string raw)
	{
		try
		{
			return JsonSerializer.Deserialize<string>("\"" + raw + "\"") ?? raw;
		}
		catch (JsonException)
		{
			return raw;
		}
	}

	static string Text(JsonNode? node) => node switch
	{
		null => string.Empty,
		JsonValue value when value.TryGetValue<string>(out var text) => text,
		JsonValue value => value.ToJsonString(),
		_ => node.ToJsonString(),
	};
}
