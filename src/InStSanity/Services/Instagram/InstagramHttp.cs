using System.Net;

namespace InStSanity.Services.Instagram;

/// <summary>
/// The single HTTP client used to talk to Instagram from C#.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the browser-side scraping the original app had to do: because the requests
/// originate in the desktop process there is no CORS restriction, so no third-party proxy
/// (microlink.io, r.jina.ai, wsrv.nl) is involved — see the decision recorded in
/// <c>docs/hybrid-port-plan.md</c>.
/// </para>
/// <para>
/// The <see cref="CookieContainer"/> is shared by design: a later phase copies the session
/// cookies out of the embedded Instagram login and into this container, after which every
/// request sees the logged-in view without any of that leaking into the web layer.
/// </para>
/// </remarks>
public sealed class InstagramHttp
{
	public const string UserAgent =
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

	/// <summary>
	/// The user agent that talks to the public pages, and the reason covers work at all.
	/// </summary>
	/// <remarks>
	/// Instagram serves a login shell to a browser user agent and the real public page
	/// (with <c>og:</c> tags and the embedded JSON) to a link-preview crawler. The
	/// original Python backend relied on exactly this (<c>BOT_UAS</c> in <c>server.py</c>);
	/// with a Chrome UA every fetch lands on <c>/accounts/login</c> instead.
	/// </remarks>
	public const string BotUserAgent = "Twitterbot/1.0";

	/// <summary>Second crawler identity, for when the first one is throttled.</summary>
	public const string ShareBotUserAgent =
		"facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)";

	public const string InstagramOrigin = "https://www.instagram.com/";

	public InstagramHttp()
	{
		Cookies = new CookieContainer();

		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			UseCookies = true,
			CookieContainer = Cookies,
			AllowAutoRedirect = true,
			MaxAutomaticRedirections = 8,
		};

		Client = new HttpClient(handler)
		{
			Timeout = TimeSpan.FromSeconds(30),
		};

		Client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
	}

	public HttpClient Client { get; }

	public CookieContainer Cookies { get; }

	/// <summary>
	/// Builds a GET request carrying the headers Instagram's CDN expects for media.
	/// </summary>
	/// <param name="uri">Absolute media or page URL.</param>
	/// <param name="referer">Origin to report; Instagram's CDN rejects empty referers on some hosts.</param>
	public HttpRequestMessage CreateMediaGet(Uri uri, string? referer = InstagramOrigin)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, uri);

		if (referer is not null)
		{
			request.Headers.Referrer = new Uri(referer);
		}

		// Set per request rather than on the client: the resolver asks the same client
		// for HTML pages, and a default image Accept would follow those requests too.
		request.Headers.TryAddWithoutValidation(
			"Accept",
			"image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "image");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

		return request;
	}

	/// <summary>
	/// Builds a GET for an Instagram page (the captioned embed or the post page), asking
	/// for HTML the way a browser opened in an iframe would.
	/// </summary>
	public HttpRequestMessage CreatePageGet(Uri uri, string? userAgent = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, uri);

		// A request-level User-Agent replaces the client default for this call only;
		// media still goes out as Chrome, which is what the CDN expects.
		if (userAgent is not null)
		{
			request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
		}

		request.Headers.Referrer = new Uri(InstagramOrigin);
		request.Headers.TryAddWithoutValidation(
			"Accept",
			"text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", userAgent is null ? "same-origin" : "none");
		request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

		return request;
	}
}
