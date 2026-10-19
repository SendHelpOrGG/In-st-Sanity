using System.Runtime.CompilerServices;
using InStSanity.Services.Browser;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Web.WebView2.Core;

namespace InStSanity.Platforms.Windows;

/// <summary>Configures the MAUI WebViews after their WebView2 controls initialize.</summary>
internal static class WebViewBootstrapper
{
	static readonly ConditionalWeakTable<object, object> Hooked = new();

	/// <summary>Routes links opened by the app UI to the user's system browser.</summary>
	public static void Attach(WebView webView) => Attach(webView, isInstagramBrowser: false);

	/// <summary>Keeps Instagram/Meta popups inside the native Instagram browser.</summary>
	public static void AttachInstagramBrowser(WebView webView) => Attach(webView, isInstagramBrowser: true);

	static void Attach(WebView webView, bool isInstagramBrowser)
	{
		// The handler (and with it CoreWebView2) appears after the page is first laid out.
		webView.HandlerChanged += (_, _) => TryHook(webView, isInstagramBrowser);
		TryHook(webView, isInstagramBrowser);
	}

	static void TryHook(WebView webView, bool isInstagramBrowser)
	{
		if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 platformView)
		{
			return;
		}

		if (Hooked.TryGetValue(platformView, out _))
		{
			return;
		}

		Hooked.Add(platformView, platformView);

		if (platformView.CoreWebView2 is not null)
		{
			Configure(platformView.CoreWebView2, isInstagramBrowser);
		}
		else
		{
			platformView.CoreWebView2Initialized += (sender, _) =>
			{
				if (sender.CoreWebView2 is not null)
				{
					Configure(sender.CoreWebView2, isInstagramBrowser);
				}
			};
		}
	}

	static void Configure(CoreWebView2 core, bool isInstagramBrowser)
	{
		core.NewWindowRequested += (sender, args) =>
		{
			args.Handled = true;

			if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				return;
			}

			if (isInstagramBrowser && InstagramBrowserNavigationPolicy.IsAllowed(uri))
			{
				sender.Navigate(uri.AbsoluteUri);
				return;
			}

			_ = Launcher.Default.OpenAsync(uri);
		};

#if DEBUG
		// Right-click → Inspect is useful while developing the port.
		core.Settings.AreDevToolsEnabled = true;
		core.Settings.AreDefaultContextMenusEnabled = true;
#else
		core.Settings.AreDevToolsEnabled = false;
		core.Settings.AreDefaultContextMenusEnabled = isInstagramBrowser;
#endif
	}
}
