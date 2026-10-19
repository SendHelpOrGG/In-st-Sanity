namespace InStSanity.Services.Browser;

/// <summary>Limits the in-app browser to Instagram and Meta-owned sign-in destinations.</summary>
public static class InstagramBrowserNavigationPolicy
{
	static readonly string[] AllowedDomains =
	[
		"instagram.com",
		"threads.net",
		"facebook.com",
		"meta.com",
	];

	public static bool IsAllowed(Uri? uri)
	{
		if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Port != 443 || uri.UserInfo.Length != 0)
		{
			return false;
		}

		var host = uri.IdnHost;
		return AllowedDomains.Any(domain =>
			host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
			host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
	}
}
