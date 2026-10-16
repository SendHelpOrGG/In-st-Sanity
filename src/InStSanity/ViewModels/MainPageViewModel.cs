namespace InStSanity.ViewModels;

/// <summary>
/// View model for <see cref="MainPage"/>.
/// </summary>
public sealed class MainPageViewModel : BaseViewModel
{
	public MainPageViewModel()
	{
		Title = "Home";
	}

	/// <summary>Placeholder text — remove together with the placeholder in MainPage.xaml.</summary>
	public string EmptyStateTitle { get; } = "Nothing here yet";

	/// <summary>Placeholder text — remove together with the placeholder in MainPage.xaml.</summary>
	public string EmptyStateMessage { get; } = "The shell is ready. Build your content here.";
}
