namespace InStSanity.Platforms.Windows;

/// <summary>
/// WebView2 configuration that has to happen before the first WebView is created.
/// </summary>
/// <remarks>
/// Both settings are applied through the documented WebView2 environment variables so the
/// default MAUI handler pipeline stays untouched:
/// <list type="bullet">
///   <item><description>
///     <c>WEBVIEW2_USER_DATA_FOLDER</c> — pins the browser profile to the app's own folder.
///     Without it the web layer's <c>localStorage</c> settings, its IndexedDB background GIF,
///     and the in-app browser's Instagram sign-in cookies would be lost between runs.
///   </description></item>
///   <item><description>
///     <c>--autoplay-policy=no-user-gesture-required</c> — the UI sounds are synthesised with
///     WebAudio and Chromium would otherwise drop the first ones.
///   </description></item>
/// </list>
/// </remarks>
internal static class WindowsWebViewEnvironment
{
	const string UserDataFolderVariable = "WEBVIEW2_USER_DATA_FOLDER";
	const string BrowserArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
	const string AutoplayArgument = "--autoplay-policy=no-user-gesture-required";

	public static void Configure()
	{
		var dataFolder = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"InStSanity",
			"WebView2");

		Directory.CreateDirectory(dataFolder);
		Environment.SetEnvironmentVariable(UserDataFolderVariable, dataFolder);

		AppendBrowserArguments(AutoplayArgument);
	}

	static void AppendBrowserArguments(string argument)
	{
		var existing = Environment.GetEnvironmentVariable(BrowserArgumentsVariable);

		if (existing is not null && existing.Contains(argument, StringComparison.Ordinal))
		{
			return;
		}

		Environment.SetEnvironmentVariable(
			BrowserArgumentsVariable,
			string.IsNullOrWhiteSpace(existing) ? argument : $"{existing} {argument}");
	}
}
