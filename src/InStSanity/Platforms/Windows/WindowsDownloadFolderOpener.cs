using System.Diagnostics;
using InStSanity.Services.Api;

namespace InStSanity.Platforms.Windows;

/// <summary>Launches File Explorer at the requested downloads directory.</summary>
public sealed class WindowsDownloadFolderOpener : IDownloadFolderOpener
{
	public void OpenFolder(string folderPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		if (string.IsNullOrWhiteSpace(windowsDirectory))
		{
			throw new InvalidOperationException("The Windows system directory is unavailable.");
		}

		var explorerPath = Path.Combine(windowsDirectory, "explorer.exe");
		var startInfo = new ProcessStartInfo
		{
			FileName = explorerPath,
			UseShellExecute = false,
		};
		startInfo.ArgumentList.Add(folderPath);

		using var process = Process.Start(startInfo);
		if (process is null)
		{
			throw new InvalidOperationException("File Explorer could not be started.");
		}
	}
}
