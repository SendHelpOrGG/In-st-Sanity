using InStSanity.Services.Instagram;
using InStSanity.Services.WebHost;

namespace InStSanity.Services.Api;

/// <summary>
/// Projects a resolved post into the summary shape the web layer's
/// <c>applyPostSummaryData</c> reads (no slide list — that is what a card needs).
/// </summary>
static class ResolveJson
{
	public static object Summarize(ResolvedPost post) => new
	{
		ok = true,
		shortcode = post.Shortcode,
		parentCode = post.ParentCode,
		title = post.Title,
		caption = post.Caption,
		owner = post.Owner,
		ownerFull = post.OwnerFull,
		likes = post.Likes,
		timestamp = post.Timestamp,
		photoCount = post.PhotoCount,
		videoCount = post.VideoCount,
		coverUrl = post.CoverUrl,
		coverThumbnailUrl = post.CoverThumbnailUrl,
		coverIsVideo = post.CoverIsVideo,
		summaryOnly = true,
		summaryResolved = post.SummaryResolved,
		fullResolved = post.FullResolved,
		sourceKind = post.SourceKind,
	};
}

/// <summary>
/// <c>GET /api/resolve-post?url=…</c> — one post, summary or full carousel.
/// </summary>
/// <remarks>
/// Answers <c>200 {"ok":false}</c> when Instagram cannot be read, rather than a 5xx: the
/// web layer treats a non-2xx as "backend down" and backs off for minutes, but a single
/// unreachable post is a normal outcome, not an outage.
/// </remarks>
public sealed class ResolvePostEndpoint : IApiEndpoint
{
	readonly InstagramResolver _resolver;

	public ResolvePostEndpoint(InstagramResolver resolver) => _resolver = resolver;

	public string Route => "/api/resolve-post";

	public async Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method is not ("GET" or "HEAD"))
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		var url = request.QueryValue("url");
		if (string.IsNullOrWhiteSpace(url))
		{
			return HttpResponseInfo.Error("missing 'url' query parameter", 400);
		}

		var summary = string.Equals(request.QueryValue("summary"), "1", StringComparison.Ordinal);
		var post = summary
			? await _resolver.ResolveSummaryAsync(url, cancellationToken).ConfigureAwait(false)
			: await _resolver.ResolveAsync(url, cancellationToken).ConfigureAwait(false);

		if (post is null)
		{
			return HttpResponseInfo.Json(new { ok = false, error = "unresolved", url });
		}

		return HttpResponseInfo.Json(summary ? ResolveJson.Summarize(post) : post);
	}
}

/// <summary>
/// <c>GET /api/resolve-batch?urls=a,b,c</c> — the call the card grid makes for a page of
/// posts, and the one whose failure used to print "server unreachable".
/// </summary>
/// <remarks>
/// The response is bounded by <see cref="Deadline"/>, because the grid abandons the
/// request after 16 s: whatever has not resolved by then is answered as missing and the
/// next pass picks it up from the cache. A batch that outlives its caller looks exactly
/// like a dead backend to the UI, which is the one thing this must never do.
/// </remarks>
public sealed class ResolveBatchEndpoint : IApiEndpoint
{
	/// <summary>The grid sends at most 10; a little headroom keeps one slow post from stalling a page.</summary>
	const int MaxUrls = 12;

	/// <summary>Mirrors the web layer's own cap (2) with room for a full page to land in time.</summary>
	const int Parallelism = 6;

	static readonly TimeSpan Deadline = TimeSpan.FromSeconds(9);

	readonly InstagramResolver _resolver;

	public ResolveBatchEndpoint(InstagramResolver resolver) => _resolver = resolver;

	public string Route => "/api/resolve-batch";

	public async Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method is not ("GET" or "HEAD"))
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		var raw = request.QueryValue("urls");
		if (string.IsNullOrWhiteSpace(raw))
		{
			return HttpResponseInfo.Error("missing 'urls' query parameter", 400);
		}

		var urls = raw
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(url => url.Contains("instagram.", StringComparison.OrdinalIgnoreCase))
			.Take(MaxUrls)
			.ToArray();

		// An empty list is a normal answer, not a client error: the grid retries forever
		// on a 4xx/5xx and prints "server unreachable" while it does.
		if (urls.Length == 0)
		{
			return EmptyBatch();
		}

		using var gate = new SemaphoreSlim(Parallelism);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Deadline);

		async Task<ResolvedPost?> ResolveOneAsync(string url)
		{
			try
			{
				await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return null;
			}

			try
			{
				// The grid only draws cards: cover, counts, owner. Asking for the whole
				// carousel here is what made a page of twelve posts outlive its caller.
				return await _resolver.ResolveSummaryAsync(url, deadline.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return null;
			}
			finally
			{
				gate.Release();
			}
		}

		var results = await Task.WhenAll(urls.Select(ResolveOneAsync)).ConfigureAwait(false);
		var resolved = results.Where(post => post is { Ok: true }).Select(post => post!).ToArray();

		var byShortcode = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		foreach (var post in resolved)
		{
			byShortcode[post.Shortcode] = post;
		}

		return HttpResponseInfo.Json(new { ok = true, byShortcode, posts = resolved });
	}

	static HttpResponseInfo EmptyBatch() => HttpResponseInfo.Json(new
	{
		ok = true,
		byShortcode = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
		posts = Array.Empty<object>(),
	});
}

/// <summary>
/// <c>GET /api/resolve-owners?urls=a,b,c</c> — the account name only, used by the
/// whole-library search index.
/// </summary>
public sealed class ResolveOwnersEndpoint : IApiEndpoint
{
	const int MaxUrls = 24;
	const int Parallelism = 6;

	static readonly TimeSpan Deadline = TimeSpan.FromSeconds(9);

	readonly InstagramResolver _resolver;

	public ResolveOwnersEndpoint(InstagramResolver resolver) => _resolver = resolver;

	public string Route => "/api/resolve-owners";

	public async Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method is not ("GET" or "HEAD"))
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		var raw = request.QueryValue("urls");
		if (string.IsNullOrWhiteSpace(raw))
		{
			return HttpResponseInfo.Error("missing 'urls' query parameter", 400);
		}

		var urls = raw
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(url => url.Contains("instagram.", StringComparison.OrdinalIgnoreCase))
			.Take(MaxUrls)
			.ToArray();

		if (urls.Length == 0)
		{
			return HttpResponseInfo.Json(new
			{
				ok = true,
				byShortcode = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
			});
		}

		using var gate = new SemaphoreSlim(Parallelism);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Deadline);

		async Task<ResolvedPost?> ResolveOneAsync(string url)
		{
			try
			{
				await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return null;
			}

			try
			{
				// The grid only draws cards: cover, counts, owner. Asking for the whole
				// carousel here is what made a page of twelve posts outlive its caller.
				return await _resolver.ResolveSummaryAsync(url, deadline.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return null;
			}
			finally
			{
				gate.Release();
			}
		}

		var results = await Task.WhenAll(urls.Select(ResolveOneAsync)).ConfigureAwait(false);

		var byShortcode = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		foreach (var post in results)
		{
			if (post is not { Ok: true })
			{
				continue;
			}

			byShortcode[post.Shortcode] = new
			{
				ok = true,
				shortcode = post.Shortcode,
				owner = post.Owner,
				ownerFull = post.OwnerFull,
				sourceKind = post.SourceKind,
			};
		}

		return HttpResponseInfo.Json(new { ok = true, byShortcode });
	}
}

/// <summary>
/// <c>GET /api/cached-posts</c> — everything resolved in this run, so a fresh page does
/// not re-download what the app already knows.
/// </summary>
public sealed class CachedPostsEndpoint : IApiEndpoint
{
	readonly InstagramResolver _resolver;

	public CachedPostsEndpoint(InstagramResolver resolver) => _resolver = resolver;

	public string Route => "/api/cached-posts";

	public Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		var byShortcode = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in _resolver.CachedPosts())
		{
			byShortcode[pair.Key] = ResolveJson.Summarize(pair.Value);
		}

		var response = HttpResponseInfo.Json(new
		{
			ok = true,
			fileName = string.Empty,
			entries = Array.Empty<object>(),
			resolvedByShortcode = byShortcode,
		});

		return Task.FromResult(response);
	}
}
