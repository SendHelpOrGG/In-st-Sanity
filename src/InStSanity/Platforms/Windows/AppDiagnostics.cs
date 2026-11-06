using System.Text;

namespace InStSanity.Platforms.Windows;

/// <summary>Small, persistent diagnostics log for failures that may close the app.</summary>
internal static class AppDiagnostics
{
	const long MaxLogBytes = 2 * 1024 * 1024;
	static readonly object WriteLock = new();
	static int _initialized;

	public static string LogDirectory
	{
		get
		{
			var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrWhiteSpace(localData)) localData = Path.GetTempPath();
			return Path.Combine(localData, "InStSanity", "Logs");
		}
	}

	public static string LogPath => Path.Combine(LogDirectory, "app-diagnostics.log");

	public static void Initialize()
	{
		if (Interlocked.Exchange(ref _initialized, 1) != 0) return;

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			var exception = args.ExceptionObject as Exception;
			WriteException("process.unhandled", exception,
				$"terminating={args.IsTerminating}; exceptionObject={args.ExceptionObject?.GetType().FullName ?? "null"}");
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
			WriteException("task.unobserved", args.Exception, $"observed={args.Observed}");
		AppDomain.CurrentDomain.ProcessExit += (_, _) => Write("process.exit", $"exitCode={Environment.ExitCode}");

		Write("app.start",
			$"pid={Environment.ProcessId}; os={Environment.OSVersion}; runtime={Environment.Version}; log={LogPath}");
	}

	public static void WriteException(string eventName, Exception? exception, string? context = null)
	{
		var exceptionText = exception?.ToString() ?? "(no managed exception details)";
		Write(eventName, string.IsNullOrWhiteSpace(context) ? exceptionText : $"{context}; {exceptionText}");
	}

	public static void Write(string eventName, string message)
	{
		try
		{
			var safeEvent = Sanitize(eventName, 100);
			var safeMessage = Sanitize(message, 16_000);
			var line = $"[{DateTimeOffset.Now:O}] [{safeEvent}] {safeMessage}{Environment.NewLine}";
			var bytes = Encoding.UTF8.GetBytes(line);

			lock (WriteLock)
			{
				var directory = LogDirectory;
				Directory.CreateDirectory(directory);
				var path = LogPath;
				if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaxLogBytes)
				{
					var previousPath = path + ".1";
					if (File.Exists(previousPath)) File.Delete(previousPath);
					File.Move(path, previousPath);
				}

				using var stream = new FileStream(
					path,
					FileMode.Append,
					FileAccess.Write,
					FileShare.ReadWrite,
					bufferSize: 4096,
					options: FileOptions.WriteThrough);
				stream.Write(bytes);
				stream.Flush(flushToDisk: true);
			}

			System.Diagnostics.Debug.WriteLine(line.TrimEnd());
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine(
				$"[In-st-Sanity] diagnostics write failed ({exception.GetType().Name}): {exception.Message}");
		}
	}

	static string Sanitize(string? value, int maxLength)
	{
		var safe = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
		return safe.Length <= maxLength ? safe : safe[..maxLength] + "…";
	}
}
