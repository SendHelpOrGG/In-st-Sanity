using InStSanity.Services.WebHost;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/collapse-window</c> — minimizes the desktop window.</summary>
public sealed class CollapseWindowEndpoint : IApiEndpoint
{
	public string Route => "/api/collapse-window";

	public Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		cancellationToken.ThrowIfCancellationRequested();

#if WINDOWS
		try
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				try
				{
					var window = Microsoft.Maui.ApplicationModel.Platform.Application.Current?.Windows.FirstOrDefault();
					if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
					{
						var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
						var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
						var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
						appWindow?.Minimize();
					}
				}
				catch
				{
					// best-effort
				}
			});
		}
		catch
		{
			// best-effort
		}
#endif

		return Task.FromResult(HttpResponseInfo.Json(new { ok = true }));
	}
}
