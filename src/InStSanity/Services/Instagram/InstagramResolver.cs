using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Instagram;

/// <summary>One slide of a post, as the web layer's <c>pictures[]</c> expects it.</summary>
public sealed class ResolvedMedia
{
	/// <summary>Best still image for the slide (a video's poster frame when it is a video).</summary>
	public string ImgUrl { get; init; } = string.Empty;

	/// <summary>Smaller image candidate for grid covers and video posters, when Instagram provides one.</summary>
	public string ThumbnailUrl { get; init; } = string.Empty;

	/// <summary>Same URL under the name the web layer reads first.</summary>
	public string RawUrl { get; init; } = string.Empty;

	public bool IsVideo { get; init; }

	/// <summary>Playable MP4 when the slide is a video; empty otherwise.</summary>
	public string VideoUrl { get; init; } = string.Empty;

	/// <summary>Where this slide was obtained: "direct" (Instagram) or "api" (save-free/Open Graph).</summary>
	public string Origin { get; init; } = string.Empty;

	/// <summary>True only when the resolver found a non-thumbnail photo candidate suitable for original-size saving.</summary>
	internal bool HasFullSizeImage { get; init; }
}

/// <summary>
/// A post resolved from Instagram, shaped exactly like the JSON the ported web layer
/// feeds into <c>applyPostSummaryData</c> / <c>applyResolvedPostData</c>.
/// </summary>
public sealed class ResolvedPost
{
	public bool Ok { get; init; }

	public string Shortcode { get; init; } = string.Empty;

	/// <summary>The post a slide belongs to (carousel children resolve to the parent code).</summary>
	public string ParentCode { get; init; } = string.Empty;

	public string Title { get; init; } = string.Empty;

	public string Caption { get; init; } = string.Empty;

	public string Owner { get; init; } = string.Empty;

	public string OwnerFull { get; init; } = string.Empty;

	/// <summary>Already formatted the way the UI prints it (<c>12.3k</c>); empty when unknown.</summary>
	public string Likes { get; init; } = string.Empty;

	/// <summary>Unix seconds; the web layer formats the relative age itself.</summary>
	/// <remarks>
	/// Settable rather than <c>init</c> for one reason: when the public page carries no
	/// date, the shortcode itself still encodes it, and filling it in after parsing is
	/// cheaper than rebuilding the object field by field.
	/// </remarks>
	public double? Timestamp { get; set; }

	public int PhotoCount { get; init; }

	public int VideoCount { get; init; }

	public string CoverUrl { get; init; } = string.Empty;

	/// <summary>Smaller cover candidate used only in the library grid.</summary>
	public string CoverThumbnailUrl { get; init; } = string.Empty;

	public bool CoverIsVideo { get; init; }

	public bool SummaryResolved { get; init; }

	public bool FullResolved { get; init; }

	/// <summary><c>direct</c> when the media came out of the page's own JSON, <c>api</c> for og-only.</summary>
	public string SourceKind { get; init; } = string.Empty;

	public IReadOnlyList<ResolvedMedia> Pictures { get; init; } = Array.Empty<ResolvedMedia>();
}

/// <summary>
/// Resolves an Instagram post URL into the metadata and media URLs the web layer needs.
/// </summary>
/// <remarks>
/// <para>
/// This is the missing half of the port: the imported <c>saved_posts.json</c> only contains
/// post URLs, so without a resolve every card has an empty <c>coverUrl</c> and the media
/// proxy is never asked for anything. The browser build scraped through third-party
/// proxies; here the request comes from the desktop process, so Instagram is fetched
/// directly (see <c>docs/hybrid-port-plan.md</c> §5).
/// </para>
/// <para>
/// One fetch fills both the summary (cover + counts) and the full carousel, so a post is
/// never downloaded twice. Results are cached in memory — the web layer also persists
/// everything it applied, so a restart normally only re-resolves what is still missing.
/// </para>
/// </remarks>
public sealed class InstagramResolver
{
	/// <summary>How long a good resolution stays fresh.</summary>
	static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

	/// <summary>How long a failed one is remembered, so a dead post is not retried in a loop.</summary>
	static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(10);

	/// <summary>
	/// One page fetch. Kept short on purpose: the grid gives a batch 16 s and an opened
	/// post 12 s, so a fetch that is going to fail has to fail inside that window.
	/// </summary>
	static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(7);

	static readonly Regex ShortcodeRegex = new(
		@"instagram\.com/(?:[A-Za-z0-9_.]+/)?(?:p|reel|reels|tv)/([A-Za-z0-9_-]+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex ContextJsonRegex = new(
		"\"contextJSON\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex JsonScriptRegex = new(
		"<script[^>]*type=[\"']application/json[\"'][^>]*>([\\s\\S]*?)</script>",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex MetaRegex = new(
		"<meta[^>]+(?:property|name)=[\"'](?<key>[^\"']+)[\"'][^>]+content=[\"'](?<value>[^\"']*)[\"']",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex EmbeddedImageRegex = new(
		"<img[^>]+class=[\"'][^\"']*EmbeddedMediaImage[^\"']*[\"'][^>]+src=[\"'](?<src>[^\"']+)[\"']",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex UsernameTextRegex = new(
		"class=[\"']UsernameText[\"'][^>]*>\\s*@?(?<name>[^<]+)<",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	static readonly Regex SeenAtRegex = new(
		"\"taken_at(?:_timestamp)?\"\\s*:\\s*(?<ts>\\d{9,12})",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);


	readonly InstagramHttp _http;
	readonly SaveFreeClient? _saveFree;
	readonly ILogger<InstagramResolver> _logger;
	readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
	readonly object _cacheLock = new();
	int _cacheGeneration;
	long _nextRateLimitLogUtcTicks;

	int _attempts;
	int _resolved;
	int _failed;
	string _lastFailure = string.Empty;

	/// <summary>Why the last resolve of a shortcode failed, for the UI's restricted cards.</summary>
	readonly ConcurrentDictionary<string, string> _failureReasons = new(StringComparer.OrdinalIgnoreCase);

	sealed class CacheEntry
	{
		public CacheEntry(ResolvedPost? post, DateTimeOffset expires)
		{
			Post = post;
			Expires = expires;
		}

		public ResolvedPost? Post { get; }

		public DateTimeOffset Expires { get; }
	}

	public InstagramResolver(
		InstagramHttp http,
		ILogger<InstagramResolver> logger,
		SaveFreeClient? saveFree = null)
	{
		_http = http;
		_logger = logger;
		_saveFree = saveFree;
	}

	/// <summary>Pulls the shortcode out of any Instagram post URL (p / reel / reels / tv).</summary>
	public static string? TryGetShortcode(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}

		var match = ShortcodeRegex.Match(url);
		return match.Success ? match.Groups[1].Value : null;
	}

	/// <summary>Everything resolved so far, for <c>/api/cached-posts</c>.</summary>
	public IReadOnlyDictionary<string, ResolvedPost> CachedPosts()
	{
		var now = DateTimeOffset.UtcNow;
		var result = new Dictionary<string, ResolvedPost>(StringComparer.OrdinalIgnoreCase);

		foreach (var pair in _cache)
		{
			// Summary entries are cached under "sum:" + shortcode; the web layer wants the
			// full posts only, keyed by the bare shortcode.
			if (pair.Key.StartsWith("sum:", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (pair.Value.Post is { Ok: true } post && pair.Value.Expires > now)
			{
				result[pair.Key] = post;
			}
		}

		return result;
	}

	/// <summary>
	/// Discards all session-dependent resolutions so the next request uses the current
	/// Instagram cookies. The generation also prevents a request started with old
	/// cookies from repopulating the cache after a sign-in transition.
	/// </summary>
	public void ClearCache()
	{
		lock (_cacheLock)
		{
			_cacheGeneration++;
			_cache.Clear();
			_failureReasons.Clear();
			Volatile.Write(ref _lastFailure, string.Empty);
		}
	}

	/// <summary>Fetch attempts since launch, including cache hits.</summary>
	public int Attempts => Volatile.Read(ref _attempts);

	/// <summary>Attempts that produced a post.</summary>
	public int Resolved => Volatile.Read(ref _resolved);

	/// <summary>Attempts that produced nothing (see <see cref="LastFailure"/> for why).</summary>
	public int Failed => Volatile.Read(ref _failed);

	/// <summary>
	/// Why the last attempt failed: <c>timeout</c>, <c>unreachable</c>, <c>http_403</c>,
	/// <c>login_wall</c>, <c>no_media</c>, <c>bad_url</c>. Empty when nothing has failed.
	/// Surfaced on <c>/__health</c> — it is the difference between "the app never asked"
	/// and "Instagram refused the app".
	/// </summary>
	public string LastFailure => Volatile.Read(ref _lastFailure);

	/// <summary>
	/// Resolves one post, or returns <c>null</c> when Instagram could not be read.
	/// The result always contains every slide; callers that only need the summary can
	/// project it.
	/// </summary>
	public Task<ResolvedPost?> ResolveAsync(string? url, CancellationToken cancellationToken) =>
		ResolveAsync(url, summaryOnly: false, cancellationToken);

	/// <summary>
	/// Card-sized resolve: the cover, the counts and the owner, without the carousel.
	/// This is what a grid of 1139 posts asks for, so it is the path that has to be cheap.
	/// </summary>
	public Task<ResolvedPost?> ResolveSummaryAsync(string? url, CancellationToken cancellationToken) =>
		ResolveAsync(url, summaryOnly: true, cancellationToken);

	/// <summary>Shortcode carried by a post URL, or "" (the API layer keys failure entries with it).</summary>
	public string ShortcodeFromUrl(string? url) => TryGetShortcode(url) ?? string.Empty;

	/// <summary>Why the last resolve of this URL failed (login_wall, http_404, …), or null.</summary>
	public string? FailureReasonFor(string? url)
	{
		var shortcode = TryGetShortcode(url);
		return shortcode is not null && _failureReasons.TryGetValue(shortcode, out var reason)
			? reason
			: null;
	}

	async Task<ResolvedPost?> ResolveAsync(
		string? url,
		bool summaryOnly,
		CancellationToken cancellationToken)
	{
		var shortcode = TryGetShortcode(url);
		if (shortcode is null)
		{
			return null;
		}

		var cacheKey = summaryOnly ? "sum:" + shortcode : shortcode;
		int cacheGeneration;
		lock (_cacheLock)
		{
			cacheGeneration = _cacheGeneration;
			if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
			{
				return cached.Post;
			}
		}

		Interlocked.Increment(ref _attempts);

		ResolvedPost? post = null;
		var reason = "exception";
		try
		{
			(post, reason) = await ResolveCoreAsync(shortcode, summaryOnly, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			reason = $"exception:{ex.GetType().Name}";
			_logger.LogWarning(ex, "Resolve failed for {Shortcode}", shortcode);
		}

		if (post is { Ok: true })
		{
			Interlocked.Increment(ref _resolved);
		}
		else
		{
			Interlocked.Increment(ref _failed);
			if (reason is not ("http_404" or "http_429"))
			{
				_logger.LogDebug("Resolve failed for {Shortcode}: {Reason}", shortcode, reason);
			}
		}

		var written = DateTimeOffset.UtcNow;
		lock (_cacheLock)
		{
			if (_cacheGeneration == cacheGeneration)
			{
				if (post is not { Ok: true })
				{
					Volatile.Write(ref _lastFailure, reason);
					_failureReasons[shortcode] = reason;
				}

				_cache[cacheKey] = new CacheEntry(
					post,
					written + (post is { Ok: true } ? CacheLifetime : FailureLifetime));
			}
		}

		return post;
	}

	/// <summary>
	/// The alphabet Instagram writes media ids in.
	/// </summary>
	const string ShortcodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

	/// <summary>
	/// Turns a shortcode into the numeric media id Instagram's JSON API expects.
	/// The shortcode is the id written in base 64 with Instagram's own alphabet.
	/// </summary>
	static string? ShortcodeToMediaId(string shortcode)
	{
		if (shortcode.Length is 0 or > 20)
		{
			return null;
		}

		return ParseShortcode(shortcode)?.ToString(CultureInfo.InvariantCulture);
	}

	static BigInteger? ParseShortcode(string shortcode)
	{
		var id = BigInteger.Zero;
		foreach (var character in shortcode)
		{
			var index = ShortcodeAlphabet.IndexOf(character, StringComparison.Ordinal);
			if (index < 0)
			{
				return null;
			}

			id = (id * 64) + index;
		}

		return id;
	}

	/// <summary>
	/// When the post was created, recovered from the shortcode itself. Instagram puts
	/// the creation time in the media id's high bits, and the shortcode is that id in
	/// base 64 — so the date needs no request at all.
	/// </summary>
	/// <remarks>
	/// This is the same arithmetic as <c>shortcode_to_timestamp</c> in the original
	/// <c>server.py</c>. It is what keeps a card from claiming "8 days ago" for a post
	/// whose page no longer carries <c>taken_at</c>.
	/// </remarks>
	static double? ShortcodeTimestamp(string shortcode)
	{
		if (shortcode.Length < 10)
		{
			return null;
		}

		var id = ParseShortcode(shortcode.Length > 11 ? shortcode[..11] : shortcode);
		if (id is null)
		{
			return null;
		}

		var seconds = (double)((id.Value >> 23) + 1314220021721) / 1000.0;
		return seconds is > 1286323200 and < 2200000000 ? seconds : null;
	}

	/// <summary>Fills the date in when the page did not carry one.</summary>
	static ResolvedPost WithShortcodeTimestamp(ResolvedPost post, string shortcode)
	{
		if (post.Timestamp is null && ShortcodeTimestamp(shortcode) is { } seconds)
		{
			post.Timestamp = seconds;
		}

		return post;
	}

	void LogRateLimitOnce(HttpResponseMessage response, string source)
	{
		var nowTicks = DateTime.UtcNow.Ticks;
		var nextAllowedTicks = Interlocked.Read(ref _nextRateLimitLogUtcTicks);
		if (nowTicks < nextAllowedTicks)
		{
			return;
		}

		var nextTicks = nowTicks + TimeSpan.FromMinutes(1).Ticks;
		if (Interlocked.CompareExchange(ref _nextRateLimitLogUtcTicks, nextTicks, nextAllowedTicks) != nextAllowedTicks)
		{
			return;
		}

		var retryAfter = response.Headers.TryGetValues("Retry-After", out var values)
			? string.Join(", ", values).Trim()
			: "not provided";
		if (retryAfter.Length == 0)
		{
			retryAfter = "not provided";
		}
		else if (retryAfter.Length > 160)
		{
			retryAfter = retryAfter[..160];
		}

		_logger.LogWarning(
			"Instagram rate-limited post summary request at {Source} (HTTP 429); Retry-After: {RetryAfter}; repeat notices are suppressed for 1 minute.",
			source,
			retryAfter);
	}

	/// <summary>
	/// Instagram's own media-info endpoint — pure JSON, no HTML to parse, and the lightest
	/// request available. It answers for public posts without a session; when it refuses
	/// (401/404), the embed page is tried instead.
	/// </summary>
	async Task<(ResolvedPost? Post, string Reason)> FetchMediaInfoAsync(
		string shortcode,
		CancellationToken cancellationToken)
	{
		var mediaId = ShortcodeToMediaId(shortcode);
		if (mediaId is null)
		{
			return (null, "bad_url");
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(FetchTimeout);

		try
		{
			using var request = new HttpRequestMessage(
				HttpMethod.Get,
				$"https://i.instagram.com/api/v1/media/{mediaId}/info/");

			// The public web app id; sending it is what makes the endpoint answer JSON
			// instead of redirecting to the login page.
			request.Headers.TryAddWithoutValidation("X-IG-App-ID", "936619743392459");
			request.Headers.TryAddWithoutValidation("Accept", "*/*");
			request.Headers.Referrer = new Uri(InstagramHttp.InstagramOrigin);

			using var response = await _http.Client
				.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				if (response.StatusCode == HttpStatusCode.TooManyRequests)
				{
					LogRateLimitOnce(response, "media-info");
				}
				else if (response.StatusCode != HttpStatusCode.NotFound)
				{
					_logger.LogDebug(
						"Media info: upstream {Status} for {Shortcode}",
						(int)response.StatusCode,
						shortcode);
				}

				return (null, $"http_{(int)response.StatusCode}");
			}

			var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
			if (!InstagramHttp.HasJsonValuePrefix(body))
			{
				var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
				_logger.LogDebug(
					"Media info returned non-JSON content ({ContentType}) for {Shortcode}",
					contentType,
					shortcode);
				return (null, "no_media");
			}

			var root = JsonNode.Parse(body);
			if (root is JsonObject payload &&
				payload["items"] is JsonArray items && items.Count > 0 && items[0] is JsonNode item)
			{
				var built = BuildFromMedia(item, shortcode);
				if (built is not null)
				{
					return (built, string.Empty);
				}
			}

			return (null, "no_media");
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return (null, "timeout");
		}
		catch (HttpRequestException ex)
		{
			_logger.LogDebug(ex, "Media info failed for {Shortcode}", shortcode);
			return (null, "unreachable");
		}
		catch (JsonException)
		{
			return (null, "no_media");
		}
	}

	/// <summary>
	/// One resolve: fetch, parse, fall back. Summary mode avoids returning the carousel,
	/// but still uses the media-info endpoint when anonymous page parsing cannot identify the post.
	/// </summary>
	async Task<(ResolvedPost? Post, string Reason)> ResolveCoreAsync(
		string shortcode,
		bool summaryOnly,
		CancellationToken cancellationToken)
	{
		// Signed in (the Browser tab's session is synced into InstagramHttp): ask
		// Instagram's own media-info JSON first — with a session it is the only layer that
		// sees follower-only and age-restricted posts, and it is the lightest request here.
		ResolvedPost? mediaInfoPost = null;
		var mediaInfoFailureReason = string.Empty;
		var authed = _http.HasInstagramSession;
		if (authed)
		{
			var (info, infoReason) = await FetchMediaInfoAsync(shortcode, cancellationToken).ConfigureAwait(false);
			mediaInfoPost = info;
			mediaInfoFailureReason = infoReason;
			if (info is not null && !NeedsSaveFree(info, summaryOnly))
			{
				return (WithShortcodeTimestamp(info, shortcode), string.Empty);
			}
			if (infoReason == "http_429")
			{
				return (null, infoReason);
			}
		}

		// Anonymous reads use the crawler identity (Instagram serves those the real page);
		// signed-in reads use the browser identity so the logged-in HTML comes back.
		var pageUserAgent = authed ? null : InstagramHttp.BotUserAgent;

		// Both pages at once. They answer different things — the embed carries the
		// carousel JSON, the post page carries the Open Graph tags — and asking for them
		// in sequence doubles the time a failing post costs the batch.
		var embedTask = FetchHtmlAsync(
			$"https://www.instagram.com/p/{shortcode}/embed/captioned/",
			pageUserAgent,
			cancellationToken);

		var pageTask = FetchHtmlAsync(
			$"https://www.instagram.com/p/{shortcode}/",
			pageUserAgent,
			cancellationToken);

		var embed = await embedTask.ConfigureAwait(false);
		var page = await pageTask.ConfigureAwait(false);

		var reason = page.Reason.Length > 0 ? page.Reason : embed.Reason;

		// 1. The captioned embed's own JSON: every slide, exact counts, the caption.
		ResolvedPost? direct = ParseFromPageJson(embed.Html ?? string.Empty, shortcode);

		// 2. The post page's JSON — the newer Polaris shape, and the shape Instagram
		//    serves to a crawler when the embed is throttled.
		direct ??= ParseFromPageJson(page.Html ?? string.Empty, shortcode);

		// 3. Open Graph: one cover, the owner, the likes, the caption. Enough for a card,
		//    and often the only thing a throttled request returns.
		direct ??= ParseFromOpenGraph(page.Html ?? string.Empty, shortcode)
			?? ParseFromOpenGraph(embed.Html ?? string.Empty, shortcode);

		if (direct is not null)
		{
			direct = WithShortcodeTimestamp(direct, shortcode);
		}

		// 4. save-free.net, for what Instagram will not serve logged out. Asked only when
		//    the direct result cannot stand on its own — see NeedsSaveFree — because it is
		//    a third party and the grid resolves twelve posts per request.
		if (NeedsSaveFree(direct, summaryOnly) && _saveFree is not null)
		{
			var fallback = await _saveFree
				.FetchAsync(shortcode, cancellationToken)
				.ConfigureAwait(false);

			if (fallback.Count > 0)
			{
				// Open Graph supplies only a preview image, not a trustworthy full-size
				// slide. Merge Save-Free with Instagram media only when the latter is real
				// Direct media; otherwise its cropped API preview would win the merge.
				var directPictures = direct is not null &&
					string.Equals(direct.SourceKind, "direct", StringComparison.Ordinal)
					? direct.Pictures
					: null;
				var pictures = MergeSlides(directPictures, fallback);
				var hasDirect = pictures.Any(picture => string.Equals(picture.Origin, "direct", StringComparison.Ordinal));
				var hasApi = pictures.Any(picture => string.Equals(picture.Origin, "api", StringComparison.Ordinal));
				// Keep the established authenticated verdict; anonymous posts use the
				// provenance of the slides actually returned, not the discarded OG preview.
				var sourceKind = authed
					? (direct is null ? "api" : "mixed")
					: (hasDirect && hasApi ? "mixed" : hasDirect ? "direct" : "api");

				return (WithPictures(direct, shortcode, pictures, sourceKind), string.Empty);
			}
		}

		// If the page only supplied a preview, prefer media-info when available; if that
		// also fails, let the preview through as a temporary last-resort media result.
		if (direct is not null && !string.Equals(direct.SourceKind, "direct", StringComparison.Ordinal))
		{
			if (mediaInfoPost is null && !authed)
			{
				var (info, infoReason) = await FetchMediaInfoAsync(shortcode, cancellationToken).ConfigureAwait(false);
				mediaInfoPost = info;
				mediaInfoFailureReason = infoReason;
			}

			if (mediaInfoPost is not null)
			{
				return (WithShortcodeTimestamp(mediaInfoPost, shortcode), string.Empty);
			}
		}

		if (direct is not null)
		{
			return (direct, string.Empty);
		}

		// 5. Last resort: the JSON media endpoint, which sometimes answers when both HTML
		//    paths were refused. Summary mode uses this too, so anonymous cards can receive
		//    a source verdict without requiring the user to open each post first.
		if (mediaInfoPost is null && !authed)
		{
			var (info, infoReason) = await FetchMediaInfoAsync(shortcode, cancellationToken).ConfigureAwait(false);
			mediaInfoPost = info;
			mediaInfoFailureReason = infoReason;
		}

		if (mediaInfoPost is not null)
		{
			return (WithShortcodeTimestamp(mediaInfoPost, shortcode), string.Empty);
		}

		return (null, reason.Length > 0 ? reason : mediaInfoFailureReason.Length > 0 ? mediaInfoFailureReason : "no_media");
	}

	/// <summary>
	/// Whether the direct result is too thin to return. A card only needs a cover; an
	/// opened post also needs every slide, and a playable URL for each video — this is the
	/// same test <c>server.py</c> made before reaching for a third party
	/// (<c>not full_resolved or any(is_v and not vid_u)</c>).
	/// </summary>
	static bool NeedsSaveFree(ResolvedPost? post, bool summaryOnly)
	{
		if (post is null)
		{
			return true;
		}

		if (summaryOnly)
		{
			return post.CoverUrl.Length == 0 || !string.Equals(post.SourceKind, "direct", StringComparison.Ordinal);
		}

		// Open Graph only carries the first slide, so a carousel resolved that way is
		// never the whole post; the page's own JSON is marked "direct".
		if (post.Pictures.Count == 0 ||
			!string.Equals(post.SourceKind, "direct", StringComparison.Ordinal))
		{
			return true;
		}

		return post.Pictures.Any(picture => picture.IsVideo && picture.VideoUrl.Length == 0);
	}

	/// <summary>
	/// Combines the page's own slides with the third party's. More slides wins (that is
	/// the carousel the page did not show), equal counts are merged slide by slide so a
	/// missing poster or a missing MP4 is filled in without losing what was already good.
	/// </summary>
	static IReadOnlyList<ResolvedMedia> MergeSlides(
		IReadOnlyList<ResolvedMedia>? direct,
		IReadOnlyList<ResolvedMedia> fallback)
	{
		if (direct is null || direct.Count == 0 || fallback.Count > direct.Count)
		{
			return fallback;
		}

		if (fallback.Count < direct.Count)
		{
			return direct;
		}

		var merged = new List<ResolvedMedia>(direct.Count);
		for (var index = 0; index < direct.Count; index++)
		{
			var page = direct[index];
			var other = fallback[index];
			var usePageImage = page.ImgUrl.Length > 0 &&
				(page.HasFullSizeImage || !other.HasFullSizeImage || other.ImgUrl.Length == 0);
			var image = usePageImage ? page.ImgUrl : other.ImgUrl;
			var hasFullSizeImage = usePageImage ? page.HasFullSizeImage : other.HasFullSizeImage;
			var video = page.VideoUrl.Length > 0 ? page.VideoUrl : other.VideoUrl;
			var thumbnail = page.ThumbnailUrl.Length > 0
				? page.ThumbnailUrl
				: (other.ThumbnailUrl.Length > 0 ? other.ThumbnailUrl : image);
			var origin = usePageImage ? page.Origin : other.Origin;
			// if image came from direct but video from fallback, the slide used both -> keep direct origin (mixed will be at post level)
			if (other.VideoUrl.Length > 0 && page.VideoUrl.Length == 0 && other.Origin == "api" && page.Origin == "direct" && other.IsVideo)
				origin = other.Origin; // video supplemented

			merged.Add(new ResolvedMedia
			{
				ImgUrl = image,
				ThumbnailUrl = thumbnail,
				RawUrl = image,
				IsVideo = page.IsVideo || other.IsVideo,
				VideoUrl = video,
				Origin = origin,
				HasFullSizeImage = hasFullSizeImage,
			});
		}

		return merged;
	}

	/// <summary>
	/// Rebuilds a post around a new slide list, keeping every piece of metadata the page
	/// did provide. With no <paramref name="post"/> the result is cover-and-counts only,
	/// which is still enough for a card.
	/// </summary>
	static ResolvedPost WithPictures(
		ResolvedPost? post,
		string shortcode,
		IReadOnlyList<ResolvedMedia> pictures,
		string sourceKind)
	{
		var cover = pictures.Count > 0 ? pictures[0] : null;
		var photoCount = pictures.Count(picture => !picture.IsVideo);
		var videoCount = pictures.Count(picture => picture.IsVideo);

		return new ResolvedPost
		{
			Ok = true,
			Shortcode = post?.Shortcode is { Length: > 0 } code ? code : shortcode,
			ParentCode = post?.ParentCode is { Length: > 0 } parent ? parent : shortcode,
			Title = post?.Title ?? string.Empty,
			Caption = post?.Caption ?? string.Empty,
			Owner = post?.Owner ?? string.Empty,
			OwnerFull = post?.OwnerFull ?? string.Empty,
			Likes = post?.Likes ?? string.Empty,
			Timestamp = post?.Timestamp ?? ShortcodeTimestamp(shortcode),
			PhotoCount = photoCount,
			VideoCount = videoCount,
			CoverUrl = cover?.ImgUrl ?? post?.CoverUrl ?? string.Empty,
			CoverThumbnailUrl = cover?.ThumbnailUrl is { Length: > 0 } thumbnail
				? thumbnail
				: (post?.CoverThumbnailUrl ?? cover?.ImgUrl ?? post?.CoverUrl ?? string.Empty),
			CoverIsVideo = cover?.IsVideo ?? post?.CoverIsVideo ?? false,
			SummaryResolved = true,
			FullResolved = pictures.Count > 0 &&
				pictures.All(picture => !picture.IsVideo || picture.VideoUrl.Length > 0),
			SourceKind = sourceKind,
			Pictures = pictures,
		};
	}

	async Task<(string? Html, string Reason)> FetchHtmlAsync(
		string url,
		string? userAgent,
		CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(FetchTimeout);

		Uri uri;
		try
		{
			uri = new Uri(url);
		}
		catch (UriFormatException)
		{
			return (null, "bad_url");
		}

		try
		{
			using var request = _http.CreatePageGet(uri, userAgent);

			using var response = await _http.Client
				.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
				.ConfigureAwait(false);

			// Instagram answers blocked / logged-out scrapes with a redirect to the login
			// page; that is still a 200, so it has to be checked explicitly.
			var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
			if (finalUrl.Contains("/accounts/login", StringComparison.OrdinalIgnoreCase) ||
				finalUrl.Contains("/challenge/", StringComparison.OrdinalIgnoreCase))
			{
				_logger.LogDebug("Resolve fetch: login wall for {Url}", url);
				return (null, "login_wall");
			}

			if (!response.IsSuccessStatusCode)
			{
				if (response.StatusCode == HttpStatusCode.TooManyRequests)
				{
					LogRateLimitOnce(response, "page-fetch");
				}
				else if (response.StatusCode != HttpStatusCode.NotFound)
				{
					_logger.LogDebug(
						"Resolve fetch: upstream {Status} for {Url}",
						(int)response.StatusCode,
						url);
				}

				return (null, $"http_{(int)response.StatusCode}");
			}

			return (await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false), string.Empty);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogDebug("Resolve fetch timed out for {Url}", url);
			return (null, "timeout");
		}
		catch (HttpRequestException ex)
		{
			_logger.LogDebug(ex, "Resolve fetch failed for {Url}", url);
			return (null, "unreachable");
		}
	}

	/// <summary>
	/// Finds the requested post's identity anchor, then inspects only known media-holder
	/// fields below that anchor. Instagram can put the shortcode on a wrapper and the
	/// preview/carousel payload in a nested object; unrelated page JSON is never a source
	/// of slides.
	/// </summary>
	static ResolvedPost? ParseFromPageJson(string html, string shortcode)
	{
		var candidates = new List<string>();

		foreach (Match contextMatch in ContextJsonRegex.Matches(html))
		{
			var unescaped = UnescapeJsonString(contextMatch.Groups[1].Value);
			if (!string.IsNullOrEmpty(unescaped))
			{
				candidates.Add(unescaped);
			}
		}

		foreach (Match script in JsonScriptRegex.Matches(html))
		{
			var body = script.Groups[1].Value;
			if (body.Contains("shortcode_media", StringComparison.Ordinal) ||
				body.Contains("image_versions2", StringComparison.Ordinal) ||
				body.Contains("carousel_media", StringComparison.Ordinal) ||
				body.Contains("edge_sidecar_to_children", StringComparison.Ordinal) ||
				body.Contains("display_resources", StringComparison.Ordinal) ||
				body.Contains("display_url", StringComparison.Ordinal) ||
				body.Contains("display_uri", StringComparison.Ordinal) ||
				body.Contains("video_versions", StringComparison.Ordinal) ||
				body.Contains("thumbnail_src", StringComparison.Ordinal) ||
				body.Contains("__isXIGPolarisMedia", StringComparison.Ordinal))
			{
				candidates.Add(body);
			}
		}

		var roots = new List<JsonNode>();
		foreach (var candidate in candidates)
		{
			if (!InstagramHttp.HasJsonValuePrefix(candidate))
			{
				continue;
			}

			try
			{
				var root = JsonNode.Parse(candidate);
				if (root is not null)
				{
					roots.Add(root);
				}
			}
			catch (JsonException)
			{
				continue;
			}
		}

		var expectedMediaId = ShortcodeToMediaId(shortcode);
		// Search every JSON block for a target carousel before accepting any single
		// preview image; the first block may be only a lightweight preview.
		foreach (var root in roots)
		{
			var media = FindPostMediaNode(root, 0, shortcode, expectedMediaId, true, out var identityAnchor);
			if (media is null)
			{
				continue;
			}

			var built = BuildFromMedia(media, shortcode, identityAnchor);
			if (built is not null)
			{
				return built;
			}
		}

		foreach (var root in roots)
		{
			var media = FindPostMediaNode(root, 0, shortcode, expectedMediaId, false, out var identityAnchor);
			if (media is null)
			{
				continue;
			}

			var built = BuildFromMedia(media, shortcode, identityAnchor);
			if (built is not null)
			{
				return built;
			}
		}

		return null;
	}

	/// <summary>Searches page JSON for the post identity before considering any image fields.</summary>
	static JsonNode? FindPostMediaNode(
		JsonNode? node,
		int depth,
		string shortcode,
		string? expectedMediaId,
		bool preferCarousel,
		out JsonObject? identityAnchor)
	{
		identityAnchor = null;
		if (node is null || depth > 18)
		{
			return null;
		}

		if (node is JsonObject obj)
		{
			if (MatchesPostIdentity(obj, shortcode, expectedMediaId))
			{
				var scopedMedia = FindMediaPayloadWithinPost(obj, 0, preferCarousel, shortcode);
				if (scopedMedia is not null)
				{
					identityAnchor = obj;
					return scopedMedia;
				}
			}

			// This outer walk locates identity anchors only. Media extraction happens
			// separately and is restricted to recognized post/media-holder keys.
			foreach (var pair in obj)
			{
				var found = FindPostMediaNode(pair.Value, depth + 1, shortcode, expectedMediaId, preferCarousel, out var nestedIdentity);
				if (found is not null)
				{
					identityAnchor = nestedIdentity;
					return found;
				}
			}

			return null;
		}

		if (node is JsonArray array)
		{
			foreach (var item in array)
			{
				var found = FindPostMediaNode(item, depth + 1, shortcode, expectedMediaId, preferCarousel, out var nestedIdentity);
				if (found is not null)
				{
					identityAnchor = nestedIdentity;
					return found;
				}
			}
		}

		return null;
	}

	static bool MatchesPostIdentity(JsonObject obj, string shortcode, string? expectedMediaId)
	{
		var code = Text(obj["code"]);
		var embeddedShortcode = Text(obj["shortcode"]);
		var hasCode = !string.IsNullOrWhiteSpace(code);
		var hasEmbeddedShortcode = !string.IsNullOrWhiteSpace(embeddedShortcode);
		if (hasCode || hasEmbeddedShortcode)
		{
			// When both identity fields are present, both must agree. A conflicting
			// shortcode must not be rescued by another matching field or numeric id.
			return (!hasCode || string.Equals(code, shortcode, StringComparison.Ordinal)) &&
				(!hasEmbeddedShortcode || string.Equals(embeddedShortcode, shortcode, StringComparison.Ordinal));
		}

		if (string.IsNullOrEmpty(expectedMediaId))
		{
			return false;
		}

		var embeddedMediaId = ScalarText(obj["pk"]) ?? ScalarText(obj["id"]);
		return string.Equals(embeddedMediaId, expectedMediaId, StringComparison.Ordinal) ||
			embeddedMediaId?.StartsWith(expectedMediaId + "_", StringComparison.Ordinal) == true;
	}

	static bool HasConflictingShortcode(JsonObject obj, string shortcode)
	{
		var code = Text(obj["code"]);
		var embeddedShortcode = Text(obj["shortcode"]);
		return (!string.IsNullOrWhiteSpace(code) && !string.Equals(code, shortcode, StringComparison.Ordinal)) ||
			(!string.IsNullOrWhiteSpace(embeddedShortcode) && !string.Equals(embeddedShortcode, shortcode, StringComparison.Ordinal));
	}

	/// <summary>
	/// Chooses the media holder only within a post whose identity has already matched.
	/// It does not descend through owner, comment, or recommendation branches.
	/// </summary>
	static JsonNode? FindMediaPayloadWithinPost(JsonNode? node, int depth, bool preferCarousel, string shortcode)
	{
		if (node is null || depth > 12)
		{
			return null;
		}

		if (node is JsonObject obj)
		{
			if (depth > 0 && HasConflictingShortcode(obj, shortcode))
			{
				return null;
			}

			if (HasCarouselPayloadFields(obj))
			{
				return obj;
			}

			// Traverse only recognized post/media-holder keys; never follow owner,
			// comment, recommendation, or arbitrary page branches. Prefer a nested
			// holder over a wrapper's lone preview URL.
			foreach (var pair in obj)
			{
				if (!IsPostMediaContainerKey(pair.Key))
				{
					continue;
				}

				var found = FindMediaPayloadWithinPost(pair.Value, depth + 1, preferCarousel, shortcode);
				if (found is not null)
				{
					return found;
				}
			}

			return !preferCarousel && HasMediaPayloadFields(obj) ? obj : null;
		}

		if (node is JsonArray array)
		{
			foreach (var item in array)
			{
				var found = FindMediaPayloadWithinPost(item, depth + 1, preferCarousel, shortcode);
				if (found is not null)
				{
					return found;
				}
			}
		}

		return null;
	}

	static bool IsPostMediaContainerKey(string key) => key is
		"shortcode_media" or
		"xdt_shortcode_media" or
		"media" or
		"preview" or
		"preview_media" or
		"xdt_api__v1__media__shortcode__web_info" or
		"graphql" or
		"gql_data" or
		"data" or
		"node";

	static bool HasCarouselPayloadFields(JsonObject obj) =>
		obj["carousel_media"] is JsonArray carousel && carousel.Count > 0 ||
		obj["edge_sidecar_to_children"]?["edges"] is JsonArray edges && edges.Count > 0;

	static bool HasSingleMediaPayloadFields(JsonObject obj) =>
		obj.ContainsKey("image_versions2") ||
		obj.ContainsKey("display_url") ||
		obj.ContainsKey("display_uri") ||
		obj.ContainsKey("display_resources") ||
		obj.ContainsKey("video_url") ||
		obj.ContainsKey("video_versions") ||
		obj.ContainsKey("thumbnail_src");

	static bool HasMediaPayloadFields(JsonObject obj) =>
		HasCarouselPayloadFields(obj) || HasSingleMediaPayloadFields(obj);

	static JsonNode? FirstArrayItem(JsonNode? node) =>
		node is JsonArray array && array.Count > 0 ? array[0] : null;

	static ResolvedPost? BuildFromMedia(JsonNode media, string shortcode, JsonNode? identityAnchor = null)
	{
		var ownerNode = media["owner"] ?? media["user"] ?? identityAnchor?["owner"] ?? identityAnchor?["user"];
		var owner = Text(ownerNode?["username"]) ?? string.Empty;
		var ownerFull = Text(ownerNode?["full_name"]) ?? string.Empty;

		var caption = Text(media["caption"]?["text"]);
		if (string.IsNullOrEmpty(caption))
		{
			caption = Text(identityAnchor?["caption"]?["text"]);
		}

		if (string.IsNullOrEmpty(caption))
		{
			caption = Text(FirstArrayItem(media["edge_media_to_caption"]?["edges"])?["node"]?["text"])
				?? Text(FirstArrayItem(identityAnchor?["edge_media_to_caption"]?["edges"])?["node"]?["text"]);
		}

		caption ??= string.Empty;

		var likes = Integer(media["like_count"])
			?? Integer(media["edge_liked_by"]?["count"])
			?? Integer(media["edge_media_preview_like"]?["count"])
			?? Integer(identityAnchor?["like_count"])
			?? Integer(identityAnchor?["edge_liked_by"]?["count"])
			?? Integer(identityAnchor?["edge_media_preview_like"]?["count"]);

		var timestamp = Number(media["taken_at"]) ?? Number(media["taken_at_timestamp"])
			?? Number(identityAnchor?["taken_at"]) ?? Number(identityAnchor?["taken_at_timestamp"]);

		var slides = new List<JsonNode>();
		if (media["carousel_media"] is JsonArray carousel)
		{
			foreach (var slide in carousel)
			{
				if (slide is not null)
				{
					slides.Add(slide);
				}
			}
		}
		else if (media["edge_sidecar_to_children"]?["edges"] is JsonArray edges)
		{
			foreach (var edge in edges)
			{
				if (edge?["node"] is JsonNode slide)
				{
					slides.Add(slide);
				}
			}
		}

		if (slides.Count == 0)
		{
			slides.Add(media);
		}

		var pictures = new List<ResolvedMedia>();
		foreach (var slide in slides)
		{
			var image = PickImage(slide);
			var thumbnail = PickThumbnailImage(slide, image);
			var videoUrl = PickVideoUrl(slide);
			var isVideo = IsVideoSlide(slide) || videoUrl.Length > 0;

			if (image.Length == 0 && videoUrl.Length == 0)
			{
				continue;
			}

			pictures.Add(new ResolvedMedia
			{
				ImgUrl = image,
				ThumbnailUrl = thumbnail,
				RawUrl = image,
				IsVideo = isVideo,
				VideoUrl = videoUrl,
				Origin = "direct",
				HasFullSizeImage = HasFullSizeImageCandidate(slide),
			});
		}

		if (pictures.Count == 0)
		{
			return null;
		}

		var photoCount = pictures.Count(picture => !picture.IsVideo);
		var videoCount = pictures.Count(picture => picture.IsVideo);
		var cover = pictures[0];
		var allVideosPlayable = pictures.All(picture => !picture.IsVideo || picture.VideoUrl.Length > 0);

		return new ResolvedPost
		{
			Ok = true,
			Shortcode = shortcode,
			ParentCode = Text(media["code"]) ?? Text(media["shortcode"]) ??
				Text(identityAnchor?["code"]) ?? Text(identityAnchor?["shortcode"]) ?? shortcode,
			Title = FirstLine(caption),
			Caption = caption,
			Owner = owner,
			OwnerFull = ownerFull,
			Likes = FormatLikes(likes),
			Timestamp = timestamp,
			PhotoCount = photoCount,
			VideoCount = videoCount,
			CoverUrl = cover.ImgUrl,
			CoverThumbnailUrl = cover.ThumbnailUrl.Length > 0 ? cover.ThumbnailUrl : cover.ImgUrl,
			CoverIsVideo = cover.IsVideo,
			SummaryResolved = true,
			FullResolved = allVideosPlayable && pictures.Count > 0,
			SourceKind = "direct",
			Pictures = pictures,
		};
	}

	/// <summary>Last resort when the page carries no media JSON: the og: tags of the preview.</summary>
	static ResolvedPost? ParseFromOpenGraph(string html, string shortcode)
	{
		var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match match in MetaRegex.Matches(html))
		{
			var key = match.Groups["key"].Value.Trim().ToLowerInvariant();
			var value = Decode(match.Groups["value"].Value);
			if (key.Length > 0 && value.Length > 0)
			{
				meta.TryAdd(key, value);
			}
		}

		var cover = meta.TryGetValue("og:image", out var ogImage) ? ogImage : string.Empty;
		if (cover.Length == 0)
		{
			var embedded = EmbeddedImageRegex.Match(html);
			if (embedded.Success)
			{
				cover = Decode(embedded.Groups["src"].Value);
			}
		}

		if (cover.Length == 0)
		{
			return null;
		}

		var video = meta.TryGetValue("og:video", out var ogVideo) ? ogVideo : string.Empty;
		if (video.Length == 0 && meta.TryGetValue("og:video:secure_url", out var secureVideo))
		{
			video = secureVideo;
		}

		var owner = string.Empty;
		var username = UsernameTextRegex.Match(html);
		if (username.Success)
		{
			owner = Decode(username.Groups["name"].Value).Trim();
		}

		var title = meta.TryGetValue("og:title", out var ogTitle) ? ogTitle : string.Empty;
		var description = meta.TryGetValue("og:description", out var ogDescription) ? ogDescription : string.Empty;

		if (owner.Length == 0)
		{
			var fromDescription = Regex.Match(description, @"-\s*([A-Za-z0-9._]+)\s+on\s+");
			if (fromDescription.Success)
			{
				owner = fromDescription.Groups[1].Value;
			}
		}

		var likes = string.Empty;
		var likeMatch = Regex.Match(description, @"([\d,.]+[kKmM]?)\s+likes?");
		if (likeMatch.Success)
		{
			likes = likeMatch.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
		}

		// "Owner on Instagram: “caption”"
		var cleanTitle = title;
		var quoteStart = title.IndexOf('“');
		if (quoteStart < 0)
		{
			quoteStart = title.IndexOf('"');
		}

		if (quoteStart >= 0)
		{
			cleanTitle = title[(quoteStart + 1)..].TrimEnd('”', '"', ' ');
		}

		var isVideo = video.Length > 0 || meta.TryGetValue("og:type", out var ogType) &&
			ogType.Contains("video", StringComparison.OrdinalIgnoreCase);

		var seenAt = SeenAtRegex.Match(html);
		double? timestamp = null;
		if (seenAt.Success &&
			double.TryParse(seenAt.Groups["ts"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
		{
			timestamp = seconds;
		}

		var pictures = new List<ResolvedMedia>
		{
			new()
			{
				ImgUrl = cover,
				ThumbnailUrl = cover,
				RawUrl = cover,
				IsVideo = isVideo,
				VideoUrl = isVideo ? video : string.Empty,
				Origin = "api",
				HasFullSizeImage = false,
			},
		};

		return new ResolvedPost
		{
			Ok = true,
			Shortcode = shortcode,
			ParentCode = shortcode,
			Title = cleanTitle,
			Caption = cleanTitle,
			Owner = owner,
			OwnerFull = string.Empty,
			Likes = likes,
			Timestamp = timestamp,
			PhotoCount = isVideo ? 0 : 1,
			VideoCount = isVideo ? 1 : 0,
			CoverUrl = cover,
			CoverThumbnailUrl = cover,
			CoverIsVideo = isVideo,
			SummaryResolved = true,
			FullResolved = false,
			SourceKind = "api",
			Pictures = pictures,
		};
	}

	static string PickThumbnailImage(JsonNode slide, string fullImage)
	{
		var thumbnail = ThumbnailCandidate(slide["display_resources"], 480);
		if (thumbnail.Length > 0)
		{
			return thumbnail;
		}

		thumbnail = ThumbnailCandidate(slide["image_versions2"]?["candidates"], 480);
		if (thumbnail.Length > 0)
		{
			return thumbnail;
		}

		var fallbackThumbnail = Text(slide["thumbnail_src"]);
		if (string.IsNullOrWhiteSpace(fallbackThumbnail))
		{
			fallbackThumbnail = Text(slide["display_uri"]);
		}

		return string.IsNullOrWhiteSpace(fallbackThumbnail) ? fullImage : fallbackThumbnail;
	}

	static string ThumbnailCandidate(JsonNode? candidates, double maximumWidth)
	{
		if (candidates is not JsonArray array || array.Count == 0)
		{
			return string.Empty;
		}

		string bestUrl = string.Empty;
		double bestWidth = -1;
		string smallestUrl = string.Empty;
		double smallestWidth = double.MaxValue;
		foreach (var candidate in array)
		{
			var url = Text(candidate?["src"]);
			if (string.IsNullOrWhiteSpace(url))
			{
				url = Text(candidate?["url"]);
			}

			if (string.IsNullOrWhiteSpace(url))
			{
				continue;
			}

			var width = Number(candidate?["config_width"]) ?? Number(candidate?["width"]) ?? 0;
			if (width > 0 && width < smallestWidth)
			{
				smallestWidth = width;
				smallestUrl = url;
			}

			if (width > 0 && width <= maximumWidth && width > bestWidth)
			{
				bestWidth = width;
				bestUrl = url;
			}
		}

		return bestUrl.Length > 0 ? bestUrl : smallestUrl;
	}

	static bool HasFullSizeImageCandidate(JsonNode slide) =>
		!string.IsNullOrWhiteSpace(Text(slide["display_url"])) ||
		BestCandidate(slide["image_versions2"]?["candidates"]).Length > 0 ||
		BestCandidate(slide["display_resources"]).Length > 0 ||
		!string.IsNullOrWhiteSpace(Text(slide["display_uri"]));

	static string PickImage(JsonNode slide)
	{
		var direct = Text(slide["display_url"]);
		if (!string.IsNullOrEmpty(direct))
		{
			return direct;
		}

		var best = BestCandidate(slide["image_versions2"]?["candidates"]);
		if (best.Length > 0)
		{
			return best;
		}

		best = BestCandidate(slide["display_resources"]);
		if (best.Length > 0)
		{
			return best;
		}

		direct = Text(slide["display_uri"]);
		if (!string.IsNullOrEmpty(direct))
		{
			return direct;
		}

		return Text(slide["thumbnail_src"]) ?? string.Empty;
	}

	/// <summary>Picks the widest candidate — Instagram ships several sizes per slide.</summary>
	static string BestCandidate(JsonNode? candidates)
	{
		if (candidates is not JsonArray array || array.Count == 0)
		{
			return string.Empty;
		}

		var bestUrl = string.Empty;
		var bestWidth = -1.0;

		foreach (var candidate in array)
		{
			var url = Text(candidate?["url"]) ?? Text(candidate?["src"]);
			if (string.IsNullOrEmpty(url))
			{
				continue;
			}

			var width = Number(candidate?["width"]) ?? Number(candidate?["config_width"]) ?? 0;
			if (width > bestWidth)
			{
				bestWidth = width;
				bestUrl = url;
			}
		}

		return bestUrl;
	}

	static string PickVideoUrl(JsonNode slide)
	{
		var direct = Text(slide["video_url"]);
		if (!string.IsNullOrEmpty(direct))
		{
			return direct;
		}

		if (slide["video_versions"] is JsonArray versions && versions.Count > 0)
		{
			return Text(versions[0]?["url"]) ?? string.Empty;
		}

		return string.Empty;
	}

	static bool IsVideoSlide(JsonNode slide)
	{
		if (slide["is_video"] is JsonValue isVideo && isVideo.TryGetValue<bool>(out var flagged) && flagged)
		{
			return true;
		}

		var mediaType = Integer(slide["media_type"]);
		if (mediaType == 2)
		{
			return true;
		}

		return slide["video_versions"] is JsonArray versions && versions.Count > 0;
	}

	static string? ScalarText(JsonNode? node)
	{
		var text = Text(node);
		if (text is not null)
		{
			return text;
		}

		if (node is JsonValue value && value.TryGetValue<long>(out var number))
		{
			return number.ToString(CultureInfo.InvariantCulture);
		}

		return null;
	}

	static string? Text(JsonNode? node)
	{
		if (node is not JsonValue value)
		{
			return null;
		}

		if (value.TryGetValue<string>(out var text))
		{
			return Decode(text);
		}

		return null;
	}

	static int? Integer(JsonNode? node)
	{
		if (node is not JsonValue value)
		{
			return null;
		}

		if (value.TryGetValue<int>(out var number))
		{
			return number;
		}

		if (value.TryGetValue<long>(out var wide))
		{
			return wide is > int.MaxValue or < int.MinValue ? null : (int)wide;
		}

		return null;
	}

	static double? Number(JsonNode? node)
	{
		if (node is not JsonValue value)
		{
			return null;
		}

		if (value.TryGetValue<double>(out var number))
		{
			return number;
		}

		if (value.TryGetValue<long>(out var wide))
		{
			return wide;
		}

		return null;
	}

	/// <summary>Instagram counts read as <c>1.2k</c> in the UI; keep that formatting server-side.</summary>
	static string FormatLikes(int? likes)
	{
		if (likes is not int count)
		{
			return string.Empty;
		}

		if (count >= 1000000)
		{
			return (count / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture)
				.Replace(".0", string.Empty, StringComparison.Ordinal) + "m";
		}

		if (count >= 1000)
		{
			return (count / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)
				.Replace(".0", string.Empty, StringComparison.Ordinal) + "k";
		}

		return count.ToString(CultureInfo.InvariantCulture);
	}

	static string FirstLine(string text)
	{
		foreach (var line in text.Split('\n'))
		{
			var trimmed = line.Trim();
			if (trimmed.Length > 0)
			{
				return trimmed;
			}
		}

		return string.Empty;
	}

	static string UnescapeJsonString(string raw)
	{
		try
		{
			return JsonSerializer.Deserialize<string>("\"" + raw + "\"") ?? string.Empty;
		}
		catch (JsonException)
		{
			return string.Empty;
		}
	}

	/// <summary>HTML entities appear in every og: tag; media URLs break if they are kept.</summary>
	static string Decode(string value)
	{
		if (value.IndexOf('&') < 0)
		{
			return value;
		}

		return value
			.Replace("&quot;", "\"", StringComparison.Ordinal)
			.Replace("&#x27;", "'", StringComparison.Ordinal)
			.Replace("&#39;", "'", StringComparison.Ordinal)
			.Replace("&lt;", "<", StringComparison.Ordinal)
			.Replace("&gt;", ">", StringComparison.Ordinal)
			.Replace("&amp;", "&", StringComparison.Ordinal);
	}
}
