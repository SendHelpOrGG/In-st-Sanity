using InStSanity.Services.WebHost;
using Microsoft.Maui.ApplicationModel;
using WinUIApplication = Microsoft.UI.Xaml.Application;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/exit-app</c> — exits the desktop application after acknowledging the request.</summary>
public sealed class ExitAppEndpoint : IApiEndpoint
{
	int _exitScheduled;

	public string Route => "/api/exit-app";

	public Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (Interlocked.Exchange(ref _exitScheduled, 1) == 0)
		{
			_ = ExitAfterResponseAsync();
		}

		return Task.FromResult(HttpResponseInfo.Json(new { ok = true }));
	}

	static async Task ExitAfterResponseAsync()
	{
		// Give LoopbackHost time to send the acknowledgement to the WebView before the
		// WinUI process shuts down.
		await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
		MainThread.BeginInvokeOnMainThread(() => WinUIApplication.Current?.Exit());
	}
}
