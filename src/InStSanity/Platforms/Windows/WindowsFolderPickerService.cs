using InStSanity.Services.Api;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace InStSanity.Platforms.Windows;

/// <summary>Windows implementation of the download-directory picker used by the web layer.</summary>
public sealed class WindowsFolderPickerService : IFolderPickerService
{
	public Task<string?> PickFolderAsync(CancellationToken cancellationToken) =>
		MainThread.InvokeOnMainThreadAsync(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not
				Microsoft.UI.Xaml.Window nativeWindow)
			{
				throw new InvalidOperationException("The desktop window is not ready for folder selection.");
			}

			var picker = new global::Windows.Storage.Pickers.FolderPicker
			{
				SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
			};
			picker.FileTypeFilter.Add("*");

			// WinRT pickers need the owning WinUI window so the dialog is modal to this app.
			var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
			WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

			var folder = await picker.PickSingleFolderAsync();
			cancellationToken.ThrowIfCancellationRequested();
			return folder?.Path;
		});
}
