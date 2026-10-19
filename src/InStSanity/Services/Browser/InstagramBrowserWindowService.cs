using Microsoft.Maui.ApplicationModel;

namespace InStSanity.Services.Browser;

/// <summary>Bridges the local web UI to the native Instagram WebView overlay.</summary>
public sealed class InstagramBrowserWindowService
{
	public event Action<string>? OpenRequested;

	/// <summary>Requests that the visible app page open Instagram at the supplied URL.</summary>
	public bool RequestOpen(string url)
	{
		var handler = OpenRequested;
		if (handler is null)
		{
			return false;
		}

		MainThread.BeginInvokeOnMainThread(() => handler(url));
		return true;
	}
}
