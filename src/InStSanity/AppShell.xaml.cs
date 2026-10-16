namespace InStSanity;

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
