namespace InStSanity.Services.Browser;

/// <summary>
/// Turns whatever the user typed into the Browser page's address bar into a URL:
/// full URLs, <c>@username</c> handles, bare profile names, and search terms.
/// </summary>
public static class InstagramBrowserAddressParser
{
	const string InstagramHomeUrl = "https://www.instagram.com/";

	public static bool TryParse(string text, out Uri uri)
	{
		var value = text.Trim();
		if (value.StartsWith('@'))
		{
			var username = value.TrimStart('@').Trim('/');
			if (username.Length == 0)
			{
				uri = new Uri(InstagramHomeUrl);
				return true;
			}

			value = $"https://www.instagram.com/{Uri.EscapeDataString(username)}/";
		}
		else if (value.StartsWith('/'))
		{
			value = $"https://www.instagram.com{value}";
		}
		else if (value.Contains('/') && !value.Contains('.') && !value.Contains("://", StringComparison.Ordinal))
		{
			value = $"https://www.instagram.com/{value.TrimStart('/')}";
		}
		else if (value.Any(char.IsWhiteSpace) || (!value.Contains('.') && !value.Contains('/')))
		{
			value = $"https://www.instagram.com/explore/search/keyword/?q={Uri.EscapeDataString(value)}";
		}
		else if (!value.Contains("://", StringComparison.Ordinal))
		{
			value = $"https://{value}";
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
			(parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
		{
			uri = new Uri(InstagramHomeUrl);
			return false;
		}

		if (parsed.Scheme == Uri.UriSchemeHttp &&
			InstagramBrowserNavigationPolicy.IsAllowed(ToHttpsUri(parsed)))
		{
			parsed = ToHttpsUri(parsed);
		}

		uri = parsed;
		return true;
	}

	public static Uri ToHttpsUri(Uri uri)
	{
		var builder = new UriBuilder(uri)
		{
			Scheme = Uri.UriSchemeHttps,
			Port = -1,
		};
		return builder.Uri;
	}
}
