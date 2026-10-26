using System.Collections.Concurrent;
using System.Text.Json;
using InStSanity.Services.Instagram;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Images;

/// <summary>Stages user-selected photos and writes resized copies to the configured output folder.</summary>
public sealed class PhotoResizeService
{
	const int MaxFileBytes = 50 * 1024 * 1024;
	const int MaxFileCount = 500;
	const long MaxSessionBytes = 1024L * 1024 * 1024;
	const int MinMaxSide = 64;
	const int MaxMaxSide = 16_384;
	static readonly TimeSpan StagingLifetime = TimeSpan.FromHours(24);
	static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SessionLocks = new();
	static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".jxl",
	};
	static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
	};
	static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	readonly string _stagingRoot;
	readonly ILogger<PhotoResizeService> _logger;

	public PhotoResizeService(ILogger<PhotoResizeService> logger)
	{
		_logger = logger;
		var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		_stagingRoot = Path.Combine(
			string.IsNullOrWhiteSpace(localData) ? Path.GetTempPath() : localData,
			"InStSanity",
			"PhotoResizeUploads");
		CleanupExpiredSessions();
	}

	public async Task<PhotoResizeUploadResult> UploadFileAsync(
		Guid sessionId,
		Guid fileId,
		int order,
		string? requestedFileName,
		byte[] payload,
		CancellationToken cancellationToken)
	{
		if (sessionId == Guid.Empty || fileId == Guid.Empty || order < 0 || order >= MaxFileCount)
		{
			return PhotoResizeUploadResult.Failure("invalid_upload");
		}

		if (payload.Length == 0 || payload.Length > MaxFileBytes)
		{
			return PhotoResizeUploadResult.Failure(payload.Length == 0 ? "empty_file" : "file_too_large");
		}

		var fileName = SanitizeFileName(requestedFileName);
		if (fileName is null)
		{
			return PhotoResizeUploadResult.Failure("unsupported_image_type");
		}

		var gate = SessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var sessionDirectory = GetSessionDirectory(sessionId);
			Directory.CreateDirectory(sessionDirectory);
			var metadataPath = GetMetadataPath(sessionDirectory, fileId);
			var payloadPath = GetPayloadPath(sessionDirectory, fileId);
			if (File.Exists(metadataPath) && File.Exists(payloadPath))
			{
				var alreadyUploaded = await ReadStagedFileAsync(metadataPath, cancellationToken).ConfigureAwait(false);
				if (alreadyUploaded is not null)
				{
					var stagedFiles = await ReadStagedFilesAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
					return new PhotoResizeUploadResult(
						true,
						null,
						alreadyUploaded.FileName,
						stagedFiles.Count,
						stagedFiles.Sum(item => item.ByteCount));
				}

				TryDelete(payloadPath);
				TryDelete(metadataPath);
			}

			var existingFiles = await ReadStagedFilesAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
			if (existingFiles.Count >= MaxFileCount)
			{
				return PhotoResizeUploadResult.Failure("too_many_files", existingFiles.Count);
			}

			var existingBytes = existingFiles.Sum(item => item.ByteCount);
			if (existingBytes + payload.LongLength > MaxSessionBytes)
			{
				return PhotoResizeUploadResult.Failure("upload_limit_reached", existingFiles.Count, existingBytes);
			}

			var temporaryPath = Path.Combine(sessionDirectory, $"{fileId:N}.uploading");
			try
			{
				await using (var output = new FileStream(
					temporaryPath,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None,
					bufferSize: 64 * 1024,
					options: FileOptions.Asynchronous))
				{
					await output.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
				}

				File.Move(temporaryPath, payloadPath);
				var stagedFile = new StagedPhoto(fileId, fileName, order, payload.LongLength);
				await File.WriteAllBytesAsync(
					metadataPath,
					JsonSerializer.SerializeToUtf8Bytes(stagedFile, JsonOptions),
					cancellationToken).ConfigureAwait(false);

				return new PhotoResizeUploadResult(
					true,
					null,
					fileName,
					existingFiles.Count + 1,
					existingBytes + payload.LongLength);
			}
			catch
			{
				TryDelete(temporaryPath);
				TryDelete(payloadPath);
				TryDelete(metadataPath);
				throw;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
			System.Security.SecurityException)
		{
			_logger.LogWarning(exception, "Could not stage a photo for resizing");
			return PhotoResizeUploadResult.Failure("upload_failed");
		}
		finally
		{
			gate.Release();
		}
	}

	public async Task<PhotoResizeConversionResult> ConvertAsync(
		Guid sessionId,
		int maxSide,
		bool upscale,
		string? requestedOutputFolder,
		CancellationToken cancellationToken)
	{
		if (sessionId == Guid.Empty)
		{
			return PhotoResizeConversionResult.Failure("invalid_session");
		}

		if (maxSide is < MinMaxSide or > MaxMaxSide)
		{
			return PhotoResizeConversionResult.Failure("invalid_max_side");
		}

		var gate = SessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var sessionDirectory = GetSessionDirectory(sessionId);
			var stagedFiles = await ReadStagedFilesAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
			if (stagedFiles.Count == 0)
			{
				return PhotoResizeConversionResult.Failure("no_uploaded_files");
			}

			string outputFolder;
			try
			{
				outputFolder = ResolveFolder(requestedOutputFolder);
				Directory.CreateDirectory(outputFolder);
			}
			catch (Exception exception) when (
				exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
				InvalidOperationException or System.Security.SecurityException)
			{
				_logger.LogWarning(exception, "Could not prepare the resized-photo destination folder");
				return PhotoResizeConversionResult.Failure("output_folder_unavailable");
			}

			var results = new List<PhotoResizeFileResult>(stagedFiles.Count);
			foreach (var stagedFile in stagedFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var payloadPath = GetPayloadPath(sessionDirectory, stagedFile.FileId);
				try
				{
					var payload = await File.ReadAllBytesAsync(payloadPath, cancellationToken).ConfigureAwait(false);
					var resized = await ImageResizer.TryResizeToMaxSideAsync(
						payload,
						maxSide,
						Path.GetExtension(stagedFile.FileName),
						upscale).ConfigureAwait(false);
					if (resized is null)
					{
						results.Add(PhotoResizeFileResult.Failed(stagedFile.FileName, "image_decode_failed"));
						continue;
					}

					var outputName = BuildOutputFileName(stagedFile.FileName, resized.Value.Extension);
					var savedName = await WriteUniqueFileAsync(
						outputFolder,
						outputName,
						resized.Value.Bytes,
						cancellationToken).ConfigureAwait(false);
					results.Add(new PhotoResizeFileResult(
						stagedFile.FileName,
						savedName,
						resized.Value.WasResized ? "resized" : "unchanged",
						null));
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception exception) when (
					exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
					System.Security.SecurityException or InvalidOperationException)
				{
					_logger.LogWarning(exception, "Could not resize {FileName}", stagedFile.FileName);
					results.Add(PhotoResizeFileResult.Failed(stagedFile.FileName, "resize_failed"));
				}
			}

			var resizedCount = results.Count(item => item.Status == "resized");
			var unchangedCount = results.Count(item => item.Status == "unchanged");
			var failedCount = results.Count(item => item.Status == "failed");
			TryDeleteDirectory(sessionDirectory);
			return new PhotoResizeConversionResult(
				true,
				null,
				results.Count,
				resizedCount,
				unchangedCount,
				failedCount,
				results);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		finally
		{
			gate.Release();
		}
	}

	public async Task CancelSessionAsync(Guid sessionId, CancellationToken cancellationToken)
	{
		if (sessionId == Guid.Empty)
		{
			return;
		}

		var gate = SessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			TryDeleteDirectory(GetSessionDirectory(sessionId));
		}
		finally
		{
			gate.Release();
		}
	}

	static string? SanitizeFileName(string? requestedFileName)
	{
		if (string.IsNullOrWhiteSpace(requestedFileName))
		{
			return null;
		}

		var leaf = requestedFileName.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
		if (string.IsNullOrWhiteSpace(leaf))
		{
			return null;
		}

		var extension = Path.GetExtension(leaf).ToLowerInvariant();
		if (!SupportedExtensions.Contains(extension))
		{
			return null;
		}

		var stem = leaf[..^Path.GetExtension(leaf).Length];
		var invalidCharacters = Path.GetInvalidFileNameChars();
		var safeStem = new string(stem
			.Select(character => char.IsControl(character) || invalidCharacters.Contains(character) ? '_' : character)
			.ToArray())
			.Trim(' ', '.');
		if (safeStem.Length > 120)
		{
			safeStem = safeStem[..120].TrimEnd(' ', '.');
		}

		if (safeStem.Length == 0 || safeStem is "." or "..")
		{
			return null;
		}

		if (ReservedNames.Contains(safeStem))
		{
			safeStem = "_" + safeStem;
		}

		return safeStem + extension;
	}

	static string BuildOutputFileName(string originalFileName, string extension)
	{
		if (!SupportedExtensions.Contains(extension))
		{
			extension = ".jpg";
		}

		var originalStem = Path.GetFileNameWithoutExtension(originalFileName);
		var invalidCharacters = Path.GetInvalidFileNameChars();
		var safeStem = new string(originalStem
			.Select(character => char.IsControl(character) || invalidCharacters.Contains(character) ? '_' : character)
			.ToArray())
			.Trim(' ', '.');
		if (safeStem.Length > 120)
		{
			safeStem = safeStem[..120].TrimEnd(' ', '.');
		}

		if (safeStem.Length == 0 || safeStem is "." or "..")
		{
			safeStem = "resized_photo";
		}

		if (ReservedNames.Contains(safeStem))
		{
			safeStem = "_" + safeStem;
		}

		return safeStem + extension.ToLowerInvariant();
	}

	static async Task<string> WriteUniqueFileAsync(
		string folder,
		string filename,
		byte[] payload,
		CancellationToken cancellationToken)
	{
		var extension = Path.GetExtension(filename);
		var stem = Path.GetFileNameWithoutExtension(filename);
		for (var suffix = 1; suffix <= 1000; suffix++)
		{
			var candidate = suffix == 1 ? filename : $"{stem} ({suffix}){extension}";
			var path = Path.Combine(folder, candidate);
			FileStream output;
			try
			{
				output = new FileStream(
					path,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None,
					bufferSize: 64 * 1024,
					options: FileOptions.Asynchronous);
			}
			catch (IOException) when (File.Exists(path))
			{
				continue;
			}

			try
			{
				await using (output)
				{
					await output.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
				}
				return candidate;
			}
			catch
			{
				TryDelete(path);
				throw;
			}
		}

		throw new IOException("Too many resized-file name collisions.");
	}

	static async Task<List<StagedPhoto>> ReadStagedFilesAsync(
		string sessionDirectory,
		CancellationToken cancellationToken)
	{
		var result = new List<StagedPhoto>();
		if (!Directory.Exists(sessionDirectory))
		{
			return result;
		}

		foreach (var metadataPath in Directory.EnumerateFiles(sessionDirectory, "*.json"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var staged = await ReadStagedFileAsync(metadataPath, cancellationToken).ConfigureAwait(false);
			if (staged is not null && File.Exists(GetPayloadPath(sessionDirectory, staged.FileId)))
			{
				result.Add(staged);
			}
		}

		return result.OrderBy(item => item.Order).ThenBy(item => item.FileId).ToList();
	}

	static async Task<StagedPhoto?> ReadStagedFileAsync(string metadataPath, CancellationToken cancellationToken)
	{
		try
		{
			var bytes = await File.ReadAllBytesAsync(metadataPath, cancellationToken).ConfigureAwait(false);
			return JsonSerializer.Deserialize<StagedPhoto>(bytes, JsonOptions);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	string GetSessionDirectory(Guid sessionId) => Path.Combine(_stagingRoot, sessionId.ToString("N"));
	static string GetPayloadPath(string sessionDirectory, Guid fileId) => Path.Combine(sessionDirectory, $"{fileId:N}.payload");
	static string GetMetadataPath(string sessionDirectory, Guid fileId) => Path.Combine(sessionDirectory, $"{fileId:N}.json");

	static string ResolveFolder(string? requestedFolder)
	{
		var folder = string.IsNullOrWhiteSpace(requestedFolder)
			? "~/Pictures/Instagram/Resized"
			: Environment.ExpandEnvironmentVariables(requestedFolder.Trim());
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (folder == "~")
		{
			folder = home;
		}
		else if (folder.StartsWith("~/", StringComparison.Ordinal) || folder.StartsWith("~\\", StringComparison.Ordinal))
		{
			if (home.Length == 0)
			{
				throw new InvalidOperationException("The user profile folder is unavailable.");
			}

			var relative = folder[2..].TrimStart('/', '\\')
				.Replace('/', Path.DirectorySeparatorChar)
				.Replace('\\', Path.DirectorySeparatorChar);
			folder = Path.Combine(home, relative);
		}
		else if (!Path.IsPathRooted(folder))
		{
			if (home.Length == 0)
			{
				throw new InvalidOperationException("The user profile folder is unavailable.");
			}

			folder = Path.Combine(home, folder);
		}

		return Path.GetFullPath(folder);
	}

	void CleanupExpiredSessions()
	{
		try
		{
			if (!Directory.Exists(_stagingRoot))
			{
				return;
			}

			var cutoff = DateTime.UtcNow - StagingLifetime;
			foreach (var sessionDirectory in Directory.EnumerateDirectories(_stagingRoot))
			{
				if (Directory.GetLastWriteTimeUtc(sessionDirectory) < cutoff)
				{
					TryDeleteDirectory(sessionDirectory);
				}
			}
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			_logger.LogDebug(exception, "Could not clean old resize staging folders");
		}
	}

	static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			// Staging cleanup is best-effort; old sessions are pruned when the app starts.
		}
	}

	static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			// Partial upload cleanup is best-effort.
		}
	}

}

public sealed record StagedPhoto(Guid FileId, string FileName, int Order, long ByteCount);

public sealed record PhotoResizeUploadResult(
	bool Ok,
	string? Error,
	string? FileName,
	int UploadedCount,
	long UploadedBytes)
{
	public static PhotoResizeUploadResult Failure(string error, int uploadedCount = 0, long uploadedBytes = 0) =>
		new(false, error, null, uploadedCount, uploadedBytes);
}

public sealed record PhotoResizeFileResult(string FileName, string? OutputName, string Status, string? Error)
{
	public static PhotoResizeFileResult Failed(string fileName, string error) =>
		new(fileName, null, "failed", error);
}

public sealed record PhotoResizeConversionResult(
	bool Ok,
	string? Error,
	int RequestedCount,
	int ResizedCount,
	int UnchangedCount,
	int FailedCount,
	IReadOnlyList<PhotoResizeFileResult> Files)
{
	public static PhotoResizeConversionResult Failure(string error) =>
		new(false, error, 0, 0, 0, 0, Array.Empty<PhotoResizeFileResult>());
}
