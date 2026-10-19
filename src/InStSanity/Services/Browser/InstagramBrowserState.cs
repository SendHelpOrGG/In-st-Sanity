namespace InStSanity.Services.Browser;

/// <summary>Snapshot of the native Instagram browser and the account signed in behind it.</summary>
public sealed record InstagramBrowserState(
	bool Open,
	string Url,
	bool CanGoBack,
	bool CanGoForward,
	bool LoggedIn,
	string Username,
	string FullName,
	string ProfilePicUrl);

/// <summary>Window-relative dock rectangle (device-independent pixels) for the themed Browser page viewport.</summary>
public sealed record InstagramBrowserRegion(double Left, double Top, double Width, double Height);

/// <summary>Account facts read from the persistent WebView2 profile after an Instagram navigation.</summary>
public sealed record InstagramAccountSnapshot(bool LoggedIn, string Username, string FullName, string ProfilePicUrl);
