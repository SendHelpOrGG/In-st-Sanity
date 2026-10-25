using InStSanity.Services.WebHost;

namespace InStSanity.ViewModels;

/// <summary>
/// View model for <see cref="MainPage"/>, the native frame around the web layer.
/// </summary>
public sealed class MainPageViewModel : BaseViewModel
{
	public MainPageViewModel(LoopbackHost webHost)
	{
		Title = string.Empty;

		// The host is started before this view model is resolved, so the port is final.
		AppUrl = webHost.Url;
	}

	/// <summary>Loopback URL of the web layer hosted in the WebView.</summary>
	public string AppUrl { get; }
}
