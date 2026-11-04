using InStSanity.Services.Api;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace InStSanity.Platforms.Windows;

/// <summary>Windows implementation of the image/video picker, owned by the MAUI window.</summary>
public sealed class WindowsNativeFilePickerService : INativeFilePickerService
{
	static readonly string[] ImageExtensions =
	{
		".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".jxl",
	};

	static readonly string[] VideoExtensions =
	{
		".mp4", ".m4v", ".mov", ".webm", ".avi", ".mkv", ".3gp", ".mpeg", ".mpg",
	};

	static readonly string[] BackgroundExtensions =
	{
		".jpg", ".jpeg", ".png", ".webp", ".bmp",
	};

	static readonly string[] GifExtensions = { ".gif" };

	public async Task<IReadOnlyList<NativeFileSelection>> PickFilesAsync(
		string kind,
		CancellationToken cancellationToken)
	{
		AppDiagnostics.Write("picker.request", $"kind={kind}; mainThread={MainThread.IsMainThread}");
		var selectedFiles = await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not
				Microsoft.UI.Xaml.Window nativeWindow)
			{
				throw new InvalidOperationException("The desktop window is not ready for file selection.");
			}

			var spec = GetPickerSpec(kind);
			AppDiagnostics.Write("picker.dialog.prepare",
				$"kind={kind}; allowMultiple={spec.AllowMultiple}; extensions={spec.Extensions.Count}; startLocation={spec.StartLocation}");
			var picker = new global::Windows.Storage.Pickers.FileOpenPicker
			{
				SuggestedStartLocation = spec.StartLocation,
			};
			foreach (var extension in spec.Extensions)
			{
				picker.FileTypeFilter.Add(extension);
			}

			// WinRT pickers need the owning WinUI window so the dialog is modal to this app.
			var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
			WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
			AppDiagnostics.Write("picker.dialog.opening",
				$"kind={kind}; hwnd=0x{windowHandle.ToInt64():X}; pickerInitialized=true");

			var pickedFiles = new List<global::Windows.Storage.StorageFile>();
			if (spec.AllowMultiple)
			{
				var files = await picker.PickMultipleFilesAsync();
				if (files is not null)
				{
					pickedFiles.AddRange(files);
				}
			}
			else
			{
				var file = await picker.PickSingleFileAsync();
				if (file is not null)
				{
					pickedFiles.Add(file);
				}
			}

			AppDiagnostics.Write("picker.dialog.returned",
				$"kind={kind}; selectedCount={pickedFiles.Count}; cancelled={pickedFiles.Count == 0}");
			return (IReadOnlyList<global::Windows.Storage.StorageFile>)pickedFiles;
		}).ConfigureAwait(false);

		cancellationToken.ThrowIfCancellationRequested();
		AppDiagnostics.Write("picker.metadata.start", $"kind={kind}; selectedCount={selectedFiles.Count}");
		var selections = new List<NativeFileSelection>(selectedFiles.Count);
		foreach (var file in selectedFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var properties = await file.GetBasicPropertiesAsync();
			cancellationToken.ThrowIfCancellationRequested();
			var size = properties.Size > (ulong)long.MaxValue ? long.MaxValue : (long)properties.Size;
			selections.Add(new NativeFileSelection(file.Path, file.Name, size, file.ContentType));
			if (selections.Count <= 10 || selections.Count % 50 == 0)
			{
				AppDiagnostics.Write("picker.metadata.file",
					$"kind={kind}; index={selections.Count}; extension={Path.GetExtension(file.Name).ToLowerInvariant()}; size={size}; contentType={file.ContentType}");
			}
		}
		AppDiagnostics.Write("picker.metadata.complete", $"kind={kind}; count={selections.Count}");

		return selections;
	}

	static PickerSpec GetPickerSpec(string kind) => kind.Trim().ToLowerInvariant() switch
	{
		"images" => new(
			global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
			ImageExtensions,
			AllowMultiple: true),
		"video" => new(
			global::Windows.Storage.Pickers.PickerLocationId.VideosLibrary,
			VideoExtensions,
			AllowMultiple: false),
		"background" => new(
			global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
			BackgroundExtensions,
			AllowMultiple: false),
		"gif" => new(
			global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
			GifExtensions,
			AllowMultiple: false),
		_ => throw new ArgumentException("The requested file-picker kind is not supported.", nameof(kind)),
	};

	sealed record PickerSpec(
		global::Windows.Storage.Pickers.PickerLocationId StartLocation,
		IReadOnlyList<string> Extensions,
		bool AllowMultiple);
}
