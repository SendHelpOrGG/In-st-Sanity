using System.Collections.Concurrent;
using System.Globalization;
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

	/// <summary>Same URL under the name the web layer reads first.</summary>
	public string RawUrl { get; init; } = string.Empty;

	public bool IsVideo { get; init; }

	/// <summary>Playable MP4 when the slide is a video; empty otherwise.</summary>
	public string VideoUrl { get; init; } = string.Empty;
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
	public double? Timestamp { get; init; }

	public int PhotoCount { get; init; }

	public int VideoCount { get; init; }

	public string CoverUrl { get; init; } = string.Empty;

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
	readonly ILogger<InstagramResolver> _logger;
	readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

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

	public InstagramResolver(InstagramHttp http, ILogger<InstagramResolver> logger)
	{
		_http = http;
		_logger = logger;
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
			if (pair.Value.Post is { Ok: true } post && pair.Value.Expires > now)
			{
				result[pair.Key] = post;
			}
		}

		return result;
	}

	/// <summary>
	/// Resolves one post, or returns <c>null</c> when Instagram could not be read.
	/// The result always contains every slide; callers that only need the summary can
	/// project it.
	/// </summary>
	public async Task<ResolvedPost?> ResolveAsync(string? url, CancellationToken cancellationToken)
	{
		var shortcode = TryGetShortcode(url);
		if (shortcode is null)
		{
			return null;
		}

		if (_cache.TryGetValue(shortcode, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
		{
			return cached.Post;
		}

		ResolvedPost? post = null;
		try
		{
			post = await ResolveCoreAsync(shortcode, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Resolve failed for {Shortcode}", shortcode);
		}

		var written = DateTimeOffset.UtcNow;
		_cache[shortcode] = new CacheEntry(
			post,
			written + (post is { Ok: true } ? CacheLifetime : FailureLifetime));

		return post;
	}

	async Task<ResolvedPost?> ResolveCoreAsync(string shortcode, CancellationToken cancellationToken)
	{
		// The captioned embed is the one public page that carries the media, the owner and
		// the caption without a session. The normal post page is the second try.
		var html = await GetHtmlAsync(
			$"https://www.instagram.com/p/{shortcode}/embed/captioned/?cr=1&v=14",
			cancellationToken).ConfigureAwait(false);

		if (string.IsNullOrEmpty(html) || html.Contains("Page Not Found", StringComparison.OrdinalIgnoreCase))
		{
			html = await GetHtmlAsync(
				$"https://www.instagram.com/p/{shortcode}/",
				cancellationToken).ConfigureAwait(false);
		}

		if (string.IsNullOrEmpty(html))
		{
			return null;
		}

		var fromJson = ParseFromPageJson(html, shortcode);
		if (fromJson is not null)
		{
			return fromJson;
		}

		return ParseFromOpenGraph(html, shortcode);
	}

	async Task<string?> GetHtmlAsync(string url, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(12));

		try
		{
			using var request = _http.CreatePageGet(new Uri(url));

			using var response = await _http.Client
				.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				_logger.LogInformation(
					"Resolve fetch: upstream {Status} for {Url}",
					(int)response.StatusCode,
					url);

				return null;
			}

			return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogInformation("Resolve fetch timed out for {Url}", url);
			return null;
		}
		catch (HttpRequestException ex)
		{
			_logger.LogInformation(ex, "Resolve fetch failed for {Url}", url);
			return null;
		}
	}

	/// <summary>
	/// Walks the JSON blobs Instagram embeds in the page (the captioned embed's
	/// <c>contextJSON</c>, the <c>xdt_shortcode_media</c> blob, the older Polaris
	/// <c>image_versions2</c> payload) and builds a post out of the first media object.
	/// </summary>
	static ResolvedPost? ParseFromPageJson(string html, string shortcode)
	{
		var candidates = new List<string>();

		var contextMatch = ContextJsonRegex.Match(html);
		if (contextMatch.Success)
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
				body.Contains("image_versions2", StringComparison.Ordinal))
			{
				candidates.Add(body);
			}
		}

		foreach (var candidate in candidates)
		{
			JsonNode? root;
			try
			{
				root = JsonNode.Parse(candidate);
			}
			catch (JsonException)
			{
				continue;
			}

			var media = FindMediaNode(root, 0);
			if (media is null)
			{
				continue;
			}

			var built = BuildFromMedia(media, shortcode);
			if (built is not null)
			{
				return built;
			}
		}

		return null;
	}

	/// <summary>Depth-first search for the object that actually describes a post.</summary>
	static JsonNode? FindMediaNode(JsonNode? node, int depth)
	{
		if (node is null || depth > 14)
		{
			return null;
		}

		if (node is JsonObject obj)
		{
			var hasIdentity =
				obj.ContainsKey("code") ||
				obj.ContainsKey("shortcode") ||
				obj.ContainsKey("pk");

			// A carousel's parent object often carries no display_url of its own — the
			// slides live under edge_sidecar_to_children / carousel_media — so those
			// keys have to count as media too, or the parent is skipped and the walk
			// ends up returning one slide as if it were the whole post.
			var hasMedia =
				obj.ContainsKey("image_versions2") ||
				obj.ContainsKey("display_url") ||
				obj.ContainsKey("display_resources") ||
				obj.ContainsKey("edge_sidecar_to_children") ||
				obj.ContainsKey("carousel_media") ||
				obj.ContainsKey("video_url");

			if (hasIdentity && hasMedia)
			{
				return obj;
			}

			foreach (var pair in obj)
			{
				var found = FindMediaNode(pair.Value, depth + 1);
				if (found is not null)
				{
					return found;
				}
			}

			return null;
		}

		if (node is JsonArray array)
		{
			foreach (var item in array)
			{
				var found = FindMediaNode(item, depth + 1);
				if (found is not null)
				{
					return found;
				}
			}
		}

		return null;
	}

	static ResolvedPost? BuildFromMedia(JsonNode media, string shortcode)
	{
		var ownerNode = media["owner"] ?? media["user"];
		var owner = Text(ownerNode?["username"]) ?? string.Empty;
		var ownerFull = Text(ownerNode?["full_name"]) ?? string.Empty;

		var caption = Text(media["caption"]?["text"]);
		if (string.IsNullOrEmpty(caption))
		{
			caption = Text(media["edge_media_to_caption"]?["edges"]?[0]?["node"]?["text"]);
		}

		caption ??= string.Empty;

		var likes = Integer(media["like_count"])
			?? Integer(media["edge_liked_by"]?["count"])
			?? Integer(media["edge_media_preview_like"]?["count"]);

		var timestamp = Number(media["taken_at"]) ?? Number(media["taken_at_timestamp"]);

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
			var videoUrl = PickVideoUrl(slide);
			var isVideo = IsVideoSlide(slide) || videoUrl.Length > 0;

			if (image.Length == 0 && videoUrl.Length == 0)
			{
				continue;
			}

			pictures.Add(new ResolvedMedia
			{
				ImgUrl = image,
				RawUrl = image,
				IsVideo = isVideo,
				VideoUrl = videoUrl,
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
			ParentCode = Text(media["code"]) ?? Text(media["shortcode"]) ?? shortcode,
			Title = FirstLine(caption),
			Caption = caption,
			Owner = owner,
			OwnerFull = ownerFull,
			Likes = FormatLikes(likes),
			Timestamp = timestamp,
			PhotoCount = photoCount,
			VideoCount = videoCount,
			CoverUrl = cover.ImgUrl,
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
				RawUrl = cover,
				IsVideo = isVideo,
				VideoUrl = isVideo ? video : string.Empty,
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
			CoverIsVideo = isVideo,
			SummaryResolved = true,
			FullResolved = !isVideo || video.Length > 0,
			SourceKind = "api",
			Pictures = pictures,
		};
	}

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
			var url = Text(candidate?["url"]);
			if (string.IsNullOrEmpty(url))
			{
				continue;
			}

			var width = Number(candidate?["width"]) ?? 0;
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
