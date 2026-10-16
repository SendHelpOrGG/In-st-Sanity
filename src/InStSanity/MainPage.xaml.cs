using InStSanity.ViewModels;

namespace InStSanity;

public partial class MainPage : ContentPage
{
	public MainPage(MainPageViewModel viewModel)
	{
		InitializeComponent();

		BindingContext = viewModel;
	}
}
