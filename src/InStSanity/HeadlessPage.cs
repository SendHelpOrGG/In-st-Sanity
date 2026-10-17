namespace InStSanity;

/// <summary>
/// Window content used when <c>INSTSANITY_HEADLESS=1</c>: the app boots and serves its
/// backend, but creates no WebView.
/// </summary>
/// <remarks>
/// This exists so the native side can be verified deterministically — on a CI runner, or on
/// any machine where the WebView2 runtime may be missing. Rendering the web layer needs an
/// interactive desktop session and the Evergreen runtime; the HTTP surface needs neither, and
/// that is exactly what <c>tools/smoke-test.ps1</c> asserts.
/// </remarks>
internal sealed class HeadlessPage : ContentPage
{
	public HeadlessPage()
	{
		BackgroundColor = Color.FromArgb("#111318");

		Content = new VerticalStackLayout
		{
			Spacing = 6,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			Children =
			{
				new Label
				{
					Text = "In-st-Sanity",
					FontSize = 20,
					FontAttributes = FontAttributes.Bold,
					TextColor = Color.FromArgb("#E9ECF1"),
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "headless mode — backend only, no WebView",
					FontSize = 12,
					TextColor = Color.FromArgb("#6E7683"),
					HorizontalTextAlignment = TextAlignment.Center,
				},
			},
		};
	}
}
