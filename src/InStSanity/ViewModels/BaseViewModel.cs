using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InStSanity.ViewModels;

/// <summary>
/// Minimal MVVM base class: change notification plus the two properties every
/// page in the app is expected to expose.
/// </summary>
/// <remarks>
/// Hand-rolled on purpose so the project has no extra dependencies. If you prefer
/// source-generated properties, add the CommunityToolkit.Mvvm package and swap
/// this class for <c>ObservableObject</c> plus <c>[ObservableProperty]</c>.
/// </remarks>
public abstract class BaseViewModel : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;

	string _title = string.Empty;

	/// <summary>Title shown in the shell's navigation bar.</summary>
	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value);
	}

	bool _isBusy;

	/// <summary>True while the view model is running a long operation.</summary>
	public bool IsBusy
	{
		get => _isBusy;
		set => SetProperty(ref _isBusy, value);
	}

	/// <summary>Assigns <paramref name="value"/> and raises change notification when it differs.</summary>
	protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(storage, value))
		{
			return false;
		}

		storage = value;
		OnPropertyChanged(propertyName);

		return true;
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
