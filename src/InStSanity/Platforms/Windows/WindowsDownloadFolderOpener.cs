using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using InStSanity.Services.Api;
using Microsoft.CSharp.RuntimeBinder;

namespace InStSanity.Platforms.Windows;

/// <summary>Activates an existing Explorer window for the requested directory, or opens one.</summary>
public sealed class WindowsDownloadFolderOpener : IDownloadFolderOpener
{
	const int SwRestore = 9;

	public void OpenFolder(string folderPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		var normalizedFolder = NormalizeFolderPath(folderPath);
		if (TryActivateExistingExplorerWindow(normalizedFolder))
		{
			return;
		}

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
		startInfo.ArgumentList.Add(normalizedFolder);

		using var process = Process.Start(startInfo);
		if (process is null)
		{
			throw new InvalidOperationException("File Explorer could not be started.");
		}
	}

	static bool TryActivateExistingExplorerWindow(string folderPath)
	{
		object? shell = null;
		object? windows = null;
		try
		{
			var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
			if (shellType is null)
			{
				return false;
			}

			shell = Activator.CreateInstance(shellType);
			if (shell is null)
			{
				return false;
			}

			dynamic shellApplication = shell;
			windows = shellApplication.Windows();
			if (windows is null)
			{
				return false;
			}

			dynamic explorerWindows = windows;
			var windowCount = Convert.ToInt32(explorerWindows.Count, CultureInfo.InvariantCulture);
			for (var index = 0; index < windowCount; index++)
			{
				object? window = null;
				try
				{
					window = explorerWindows.Item(index);
					if (window is null)
					{
						continue;
					}

					dynamic explorerWindow = window;
					var executablePath = Convert.ToString(explorerWindow.FullName, CultureInfo.InvariantCulture);
					if (!string.Equals(
						Path.GetFileName(executablePath),
						"explorer.exe",
						StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					var openFolder = Convert.ToString(
						explorerWindow.Document.Folder.Self.Path,
						CultureInfo.InvariantCulture);
					if (!AreSameFolder(folderPath, openFolder))
					{
						continue;
					}

					var handleValue = Convert.ToInt64(explorerWindow.HWND, CultureInfo.InvariantCulture);
					var windowHandle = new IntPtr(handleValue);
					explorerWindow.Visible = true;
					if (windowHandle != IntPtr.Zero)
					{
						_ = ShowWindowAsync(windowHandle, SwRestore);
						_ = SetForegroundWindow(windowHandle);
					}

					return true;
				}
				catch (Exception exception) when (IsExplorerAutomationFailure(exception))
				{
					// Explorer may be navigating or may not expose a filesystem folder for this window.
				}
				finally
				{
					ReleaseComObject(window);
				}
			}
		}
		catch (Exception exception) when (IsExplorerAutomationFailure(exception))
		{
			// Shell automation is only an optimization; launch Explorer normally if it is unavailable.
		}
		finally
		{
			ReleaseComObject(windows);
			ReleaseComObject(shell);
		}

		return false;
	}

	static bool AreSameFolder(string expectedFolder, string? candidateFolder)
	{
		if (string.IsNullOrWhiteSpace(candidateFolder))
		{
			return false;
		}

		try
		{
			return string.Equals(
				NormalizeFolderPath(expectedFolder),
				NormalizeFolderPath(candidateFolder),
				StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (
			exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
		{
			return false;
		}
	}

	static string NormalizeFolderPath(string folderPath) =>
		Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));

	static bool IsExplorerAutomationFailure(Exception exception) =>
		exception is COMException or InvalidComObjectException or InvalidCastException or InvalidOperationException or
			ArgumentException or NotSupportedException or FormatException or OverflowException or MissingMethodException or
			System.Reflection.TargetInvocationException or System.Security.SecurityException or RuntimeBinderException;

	static void ReleaseComObject(object? value)
	{
		if (value is not null && Marshal.IsComObject(value))
		{
			try
			{
				_ = Marshal.ReleaseComObject(value);
			}
			catch (Exception exception) when (exception is COMException or InvalidComObjectException)
			{
			}
		}
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool SetForegroundWindow(IntPtr hWnd);
}
