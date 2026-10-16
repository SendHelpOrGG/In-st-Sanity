using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Web.WebView2.Core;

namespace InStSanity.Platforms.Windows;

/// <summary>
/// Platform glue for the WebView that hosts the web layer.
/// </summary>
/// <remarks>
/// The web layer is written for a browser: it calls <c>window.open(url, '_blank')</c> to open
/// Instagram in a new tab. In the desktop app those links belong in the user's browser rather
/// than a second WebView, so <c>NewWindowRequested</c> is intercepted and handed to the shell.
/// </remarks>
internal static class WebViewBootstrapper
{
	static readonly ConditionalWeakTable<object, object> Hooked = new();

	/// <summary>Wires up platform events on a MAUI <see cref="WebView"/>.</summary>
	public static void Attach(WebView webView)
	{
		// The handler (and with it CoreWebView2) appears after the page is first laid out.
		webView.HandlerChanged += (_, _) => TryHook(webView);
		TryHook(webView);
	}

	static void TryHook(WebView webView)
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
			Configure(platformView.CoreWebView2);
		}
		else
		{
			platformView.CoreWebView2Initialized += (sender, _) =>
			{
				if (sender.CoreWebView2 is not null)
				{
					Configure(sender.CoreWebView2);
				}
			};
		}
	}

	static void Configure(CoreWebView2 core)
	{
		core.NewWindowRequested += (_, args) =>
		{
			args.Handled = true;

			if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) &&
				(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
			{
				_ = Launcher.Default.OpenAsync(uri);
			}
		};

#if DEBUG
		// Right-click → Inspect opens DevTools while developing the port.
		core.Settings.AreDevToolsEnabled = true;
		core.Settings.AreDefaultContextMenusEnabled = true;
#else
		core.Settings.AreDevToolsEnabled = false;
		core.Settings.AreDefaultContextMenusEnabled = false;
#endif
	}
}
