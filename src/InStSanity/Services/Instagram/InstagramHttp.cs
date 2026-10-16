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
		Client.DefaultRequestHeaders.Accept.ParseAdd(
			"image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
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

		request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "image");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

		return request;
	}
}
