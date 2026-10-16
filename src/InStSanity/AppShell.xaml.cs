namespace InStSanity;

/// <summary>
/// The native side-panel shell. Not part of the boot path while the web layer is hosted —
/// see the header comment in AppShell.xaml and docs/hybrid-port-plan.md §2.
/// </summary>
public partial class AppShell : Shell
{
	public AppShell(MainPage mainPage)
	{
		InitializeComponent();

		// The side-panel item in AppShell.xaml is just chrome; the page it shows
		// comes from the DI container, so MainPage can take a view model in its
		// constructor.
		HomeShellContent.Content = mainPage;
	}
}
