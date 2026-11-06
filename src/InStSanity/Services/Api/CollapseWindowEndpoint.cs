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
					var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
					if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow)
					{
						var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
						var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
						var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
						if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
						{
							presenter.Minimize();
						}
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
