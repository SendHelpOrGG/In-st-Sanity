using System.Text;
using System.Text.Json;
using InStSanity.Services.Instagram;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/download-selected</c> — downloads selected images into a user-chosen folder.</summary>
public sealed class DownloadSelectedEndpoint : IApiEndpoint
{
	const int MaxItemsPerRequest = 50;
	const int MaxMediaBytes = 50 * 1024 * 1024;
	const int Parallelism = 4;

	static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".avif", ".heic", ".heif",
		".bmp", ".tif", ".tiff", ".jxl", ".ico",
	};

	static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
	};

	readonly InstagramHttp _http;
	readonly MediaCache _cache;
	readonly ILogger<DownloadSelectedEndpoint> _logger;

	public DownloadSelectedEndpoint(
		InstagramHttp http,
		MediaCache cache,
		ILogger<DownloadSelectedEndpoint> logger)
	{
		_http = http;
		_cache = cache;
		_logger = logger;
	}

	public string Route => "/api/download-selected";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		BatchRequest? parsedBatch;
		try
		{
			parsedBatch = ParseRequest(request.Body);
		}
		catch (JsonException)
		{
			return Failure("invalid_request");
		}

		if (!parsedBatch.HasValue)
		{
			return Failure("invalid_request");
		}

		var batch = parsedBatch.Value;
		if (batch.Items.Count == 0)
		{
			return Failure("no_items");
		}

		if (batch.Items.Count > MaxItemsPerRequest)
		{
			return Failure("too_many_items", failedCount: batch.Items.Count);
		}

		string folder;
		try
		{
			folder = ResolveFolder(batch.Folder);
			Directory.CreateDirectory(folder);
		}
		catch (Exception exception) when (
			exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or
			InvalidOperationException or System.Security.SecurityException)
		{
			_logger.LogWarning(exception, "Could not prepare the selected download folder");
			var failedFiles = batch.Items
				.Select((item, index) => new DownloadFileResult(
					SafeFileName(item.Filename, FallbackFileName(item, index), ".jpg", index),
					Ok: false,
					Error: "folder_unavailable"))
				.ToArray();

			return HttpResponseInfo.Json(new
			{
				ok = false,
				error = "folder_unavailable",
				folder = batch.Folder,
				savedCount = 0,
				skippedCount = 0,
				failedCount = batch.Items.Count,
				files = failedFiles,
			});
		}

		var files = new DownloadFileResult[batch.Items.Count];
		var writeTurns = batch.Items
			.Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
			.ToArray();
		writeTurns[0].TrySetResult(true);
		await Parallel.ForEachAsync(
			Enumerable.Range(0, batch.Items.Count),
			new ParallelOptions
			{
				MaxDegreeOfParallelism = Parallelism,
				CancellationToken = cancellationToken,
			},
			async (index, token) =>
			{
				files[index] = await DownloadOneAsync(
					batch.Items[index],
					folder,
					index,
					writeTurns[index].Task,
					() =>
					{
						if (index + 1 < writeTurns.Length)
						{
							writeTurns[index + 1].TrySetResult(true);
						}
					},
					batch.NoDuplicates,
					token).ConfigureAwait(false);
			}).ConfigureAwait(false);

		var savedCount = files.Count(file => file.Ok);
		var skippedCount = files.Count(file => file.Skipped);
		var failedCount = files.Count(file => !file.Ok && !file.Skipped);

		return HttpResponseInfo.Json(new
		{
			ok = failedCount == 0,
			requestedCount = files.Length,
			savedCount,
			skippedCount,
			failedCount,
			folder,
			files,
		});
	}

	async Task<DownloadFileResult> DownloadOneAsync(
		DownloadItem item,
		string folder,
		int index,
		Task writeTurn,
		Action advanceWriteTurn,
		bool noDuplicates,
		CancellationToken cancellationToken)
	{
		var fallbackName = FallbackFileName(item, index);
		var initialName = SafeFileName(item.Filename, fallbackName, ".jpg", index);
		var writeTurnAcquired = false;

		try
		{
			if (noDuplicates && ExistingPhotoExists(folder, item, initialName, index))
			{
				await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
				writeTurnAcquired = true;
				return DuplicateSkipped(initialName);
			}

			// Fetch concurrently, but serialize disk writes in request order so media
			// from each post gets consecutive creation/modification timestamps.
			var payload = await LoadPayloadAsync(item, cancellationToken).ConfigureAwait(false);
			await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
			writeTurnAcquired = true;
			if (noDuplicates && ExistingPhotoExists(folder, item, initialName, index))
			{
				return DuplicateSkipped(initialName);
			}

			var extension = ExtensionFor(payload, initialName);
			var filename = SafeFileName(item.Filename, fallbackName, extension, index);
			var writeResult = await WriteUniqueFileAsync(folder, filename, payload.Bytes, noDuplicates, cancellationToken)
				.ConfigureAwait(false);

			return writeResult.Skipped
				? DuplicateSkipped(writeResult.Filename)
				: new DownloadFileResult(writeResult.Filename, Ok: true, Error: null);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (DownloadFailureException exception)
		{
			if (!writeTurnAcquired)
			{
				await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
				writeTurnAcquired = true;
			}

			_logger.LogInformation(
				"Selected photo {Index} was not saved ({Reason})",
				index + 1,
				exception.Code);
			return new DownloadFileResult(initialName, Ok: false, Error: exception.Code);
		}
		catch (Exception exception)
		{
			if (!writeTurnAcquired)
			{
				await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
				writeTurnAcquired = true;
			}

			_logger.LogWarning(exception, "Selected photo {Index} could not be saved", index + 1);
			return new DownloadFileResult(initialName, Ok: false, Error: "download_failed");
		}
		finally
		{
			if (writeTurnAcquired)
			{
				advanceWriteTurn();
			}
		}
	}

	async Task<MediaPayload> LoadPayloadAsync(DownloadItem item, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(item.DataUri))
		{
			return DecodeDataUri(item.DataUri);
		}

		if (!string.IsNullOrWhiteSpace(item.SvgMarkup))
		{
			var markup = item.SvgMarkup.Trim();
			if (!markup.Contains("<svg", StringComparison.OrdinalIgnoreCase))
			{
				throw new DownloadFailureException("invalid_image_data");
			}

			var bytes = Encoding.UTF8.GetBytes(markup);
			EnsureSize(bytes.Length);
			return new MediaPayload(bytes, "image/svg+xml");
		}

		if (string.IsNullOrWhiteSpace(item.RawUrl))
		{
			throw new DownloadFailureException("no_media_source");
		}

		return await DownloadRemoteImageAsync(item.RawUrl, cancellationToken).ConfigureAwait(false);
	}

	async Task<MediaPayload> DownloadRemoteImageAsync(string rawUrl, CancellationToken cancellationToken)
	{
		var candidate = rawUrl.Trim();
		if (candidate.StartsWith("//", StringComparison.Ordinal))
		{
			candidate = "https:" + candidate;
		}

		if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsInstagramMediaUri(uri))
		{
			throw new DownloadFailureException("invalid_media_url");
		}

		var cached = await _cache.TryReadAsync(uri, cancellationToken).ConfigureAwait(false);
		if (cached is { Length: > 0 } && cached.Length <= MaxMediaBytes &&
			DetectImageContentType(cached) is { } cachedType)
		{
			return new MediaPayload(cached, cachedType);
		}

		using var mediaRequest = _http.CreateMediaGet(uri);
		using var response = await _http.Client
			.SendAsync(mediaRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
			.ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new DownloadFailureException($"upstream_status_{(int)response.StatusCode}");
		}

		var finalUri = response.RequestMessage?.RequestUri ?? uri;
		if (!IsInstagramMediaUri(finalUri))
		{
			throw new DownloadFailureException("redirected_to_untrusted_host");
		}

		if (response.Content.Headers.ContentLength is > MaxMediaBytes)
		{
			throw new DownloadFailureException("image_too_large");
		}

		var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
		var payload = await ReadLimitedAsync(response.Content, cancellationToken).ConfigureAwait(false);
		if (payload.Length == 0)
		{
			throw new DownloadFailureException("empty_response");
		}

		if (!IsImagePayload(contentType, payload))
		{
			throw new DownloadFailureException("response_was_not_an_image");
		}

		await _cache.WriteAsync(uri, payload, cancellationToken).ConfigureAwait(false);
		return new MediaPayload(payload, contentType);
	}

	static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
	{
		await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using var buffer = new MemoryStream();
		var chunk = new byte[64 * 1024];

		while (true)
		{
			var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			if (buffer.Length + read > MaxMediaBytes)
			{
				throw new DownloadFailureException("image_too_large");
			}

			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}

	static MediaPayload DecodeDataUri(string dataUri)
	{
		var comma = dataUri.IndexOf(',');
		if (!dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 5)
		{
			throw new DownloadFailureException("invalid_image_data");
		}

		var metadata = dataUri[5..comma];
		var segments = metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var contentType = segments.FirstOrDefault(segment => segment.Contains('/')) ?? string.Empty;
		if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
		{
			throw new DownloadFailureException("unsupported_image_type");
		}

		var encoded = dataUri[(comma + 1)..];
		byte[] payload;
		try
		{
			payload = segments.Any(segment => segment.Equals("base64", StringComparison.OrdinalIgnoreCase))
				? Convert.FromBase64String(encoded)
				: Encoding.UTF8.GetBytes(Uri.UnescapeDataString(encoded));
		}
		catch (FormatException)
		{
			throw new DownloadFailureException("invalid_image_data");
		}

		EnsureSize(payload.Length);
		if (payload.Length == 0)
		{
			throw new DownloadFailureException("empty_image_data");
		}

		return new MediaPayload(payload, contentType);
	}

	static bool IsImagePayload(string contentType, byte[] payload)
	{
		if (DetectImageContentType(payload) is not null)
		{
			return true;
		}

		// Do not save a login page or a video under an image filename just because the
		// CDN returned HTTP 200. Trust the declared MIME only when it is an image type.
		if (MediaTypeDetector.ExtensionFor(payload) is ".mp4" or ".webm")
		{
			return false;
		}

		return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
	}

	static string? DetectImageContentType(byte[] payload)
	{
		if (payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF)
		{
			return "image/jpeg";
		}

		if (payload.Length >= 8 &&
			payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4E && payload[3] == 0x47)
		{
			return "image/png";
		}

		if (payload.Length >= 6 && payload[0] == 0x47 && payload[1] == 0x49 && payload[2] == 0x46)
		{
			return "image/gif";
		}

		if (payload.Length >= 12 &&
			payload[0] == 0x52 && payload[1] == 0x49 && payload[2] == 0x46 && payload[3] == 0x46 &&
			payload[8] == 0x57 && payload[9] == 0x45 && payload[10] == 0x42 && payload[11] == 0x50)
		{
			return "image/webp";
		}

		if (payload.Length >= 12 &&
			payload[4] == 0x66 && payload[5] == 0x74 && payload[6] == 0x79 && payload[7] == 0x70)
		{
			var brand = Encoding.ASCII.GetString(payload, 8, 4);
			if (brand is "avif" or "avis")
			{
				return "image/avif";
			}
		}

		if (payload.Length >= 2 && payload[0] == 0x42 && payload[1] == 0x4D)
		{
			return "image/bmp";
		}

		if (payload.Length >= 4 &&
			((payload[0] == 0x49 && payload[1] == 0x49 && payload[2] == 0x2A && payload[3] == 0x00) ||
			 (payload[0] == 0x4D && payload[1] == 0x4D && payload[2] == 0x00 && payload[3] == 0x2A)))
		{
			return "image/tiff";
		}

		if (payload.Length > 0)
		{
			var prefix = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 1024))
				.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
			if (prefix.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
				(prefix.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
				 prefix.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
			{
				return "image/svg+xml";
			}
		}

		return null;
	}

	static bool IsInstagramMediaUri(Uri uri)
	{
		if (uri.Scheme is not ("https" or "http"))
		{
			return false;
		}

		var host = uri.Host.TrimEnd('.');
		return host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase) ||
			host.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase) ||
			host.Equals("fbcdn.net", StringComparison.OrdinalIgnoreCase) ||
			host.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase) ||
			host.Equals("cdninstagram.com", StringComparison.OrdinalIgnoreCase) ||
			host.EndsWith(".cdninstagram.com", StringComparison.OrdinalIgnoreCase);
	}

	static string ExtensionFor(MediaPayload payload, string fallbackName)
	{
		var contentType = DetectImageContentType(payload.Bytes) ?? payload.ContentType;
		var normalisedType = contentType.Split(';')[0].Trim().ToLowerInvariant();
		var extension = normalisedType switch
		{
			"image/jpeg" or "image/jpg" => ".jpg",
			"image/png" => ".png",
			"image/webp" => ".webp",
			"image/gif" => ".gif",
			"image/svg+xml" => ".svg",
			"image/avif" => ".avif",
			"image/heic" => ".heic",
			"image/heif" => ".heif",
			"image/bmp" => ".bmp",
			"image/tiff" => ".tif",
			"image/jxl" => ".jxl",
			"image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
			_ => string.Empty,
		};

		if (extension.Length > 0)
		{
			return extension;
		}

		var requestedExtension = Path.GetExtension(fallbackName);
		return SafeExtensions.Contains(requestedExtension) ? requestedExtension : ".jpg";
	}

	static string SafeFileName(string? requestedName, string fallbackName, string extension, int index)
	{
		var source = string.IsNullOrWhiteSpace(requestedName) ? fallbackName : requestedName.Trim();
		var leaf = source.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
		var oldExtension = Path.GetExtension(leaf);
		var stem = oldExtension.Length > 0 ? leaf[..^oldExtension.Length] : leaf;
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
			safeStem = $"instagram_photo_{index + 1}";
		}

		if (ReservedNames.Contains(safeStem))
		{
			safeStem = "_" + safeStem;
		}

		var safeExtension = SafeExtensions.Contains(extension) ? extension : ".jpg";
		return safeStem + safeExtension;
	}

	static string FallbackFileName(DownloadItem item, int index)
	{
		var photoNumber = Math.Max(0, item.PhotoIndex ?? index) + 1;
		return $"{SafeShortcode(item.Shortcode)}_{photoNumber}.jpg";
	}

	static string SafeShortcode(string? shortcode)
	{
		var source = string.IsNullOrWhiteSpace(shortcode) ? "instagram" : shortcode.Trim();
		var safe = new string(source
			.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' ? character : '_')
			.ToArray());
		return safe.Length == 0 ? "instagram" : safe;
	}

	static bool ExistingPhotoExists(string folder, DownloadItem item, string initialName, int index)
	{
		var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			Path.GetFileNameWithoutExtension(initialName),
		};

		var photoNumber = Math.Max(0, item.PhotoIndex ?? index) + 1;
		var shortcode = SafeShortcode(item.Shortcode);
		var currentName = SafeFileName(
			$"{shortcode}_{photoNumber}.jpg",
			$"{shortcode}_{photoNumber}.jpg",
			".jpg",
			index);
		var legacyName = SafeFileName(
			$"{shortcode}_photo_{photoNumber}.jpg",
			$"{shortcode}_photo_{photoNumber}.jpg",
			".jpg",
			index);
		stems.Add(Path.GetFileNameWithoutExtension(currentName));
		stems.Add(Path.GetFileNameWithoutExtension(legacyName));

		return stems.Any(stem => SafeExtensions.Any(extension => File.Exists(Path.Combine(folder, stem + extension))));
	}

	static DownloadFileResult DuplicateSkipped(string filename) =>
		new(filename, Ok: false, Error: null, Skipped: true);

	static async Task<(string Filename, bool Skipped)> WriteUniqueFileAsync(
		string folder,
		string filename,
		byte[] payload,
		bool noDuplicates,
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
				if (noDuplicates)
				{
					return (candidate, true);
				}

				continue;
			}

			try
			{
				await using (output)
				{
					await output.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
				}

				return (candidate, false);
			}
			catch
			{
				TryDelete(path);
				throw;
			}
		}

		throw new DownloadFailureException("filename_conflict");
	}

	internal static string ResolveFolder(string? requestedFolder)
	{
		var path = string.IsNullOrWhiteSpace(requestedFolder)
			? "~/Pictures/Instagram"
			: Environment.ExpandEnvironmentVariables(requestedFolder.Trim());
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (path == "~")
		{
			path = home;
		}
		else if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
		{
			if (home.Length == 0)
			{
				throw new InvalidOperationException("The user profile folder is unavailable.");
			}

			var relative = path[2..].TrimStart('/', '\\')
				.Replace('/', Path.DirectorySeparatorChar)
				.Replace('\\', Path.DirectorySeparatorChar);
			path = Path.Combine(home, relative);
		}
		else if (!Path.IsPathRooted(path))
		{
			if (home.Length == 0)
			{
				throw new InvalidOperationException("The user profile folder is unavailable.");
			}

			path = Path.Combine(home, path);
		}

		return Path.GetFullPath(path);
	}

	static void EnsureSize(int byteCount)
	{
		if (byteCount > MaxMediaBytes)
		{
			throw new DownloadFailureException("image_too_large");
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
		catch (IOException)
		{
			// A partial download is best-effort cleanup only.
		}
		catch (UnauthorizedAccessException)
		{
			// A partial download is best-effort cleanup only.
		}
	}

	static HttpResponseInfo Failure(string error, int failedCount = 0) =>
		HttpResponseInfo.Json(new
		{
			ok = false,
			error,
			savedCount = 0,
			failedCount,
			skippedCount = 0,
			files = Array.Empty<DownloadFileResult>(),
		});

	static BatchRequest? ParseRequest(byte[] body)
	{
		using var document = JsonDocument.Parse(body);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object || !TryGetProperty(root, "items", out var itemsElement) ||
			itemsElement.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		var folder = ReadString(root, "folder");
		var noDuplicates = ReadBoolean(root, "noDuplicates");
		var items = new List<DownloadItem>();
		foreach (var itemElement in itemsElement.EnumerateArray())
		{
			if (items.Count >= MaxItemsPerRequest)
			{
				items.Add(new DownloadItem(null, null, null, null, null, null));
				break;
			}

			items.Add(new DownloadItem(
				ReadString(itemElement, "filename"),
				ReadString(itemElement, "shortcode"),
				ReadInt32(itemElement, "photoIndex"),
				ReadString(itemElement, "rawUrl"),
				ReadString(itemElement, "dataUri"),
				ReadString(itemElement, "svgMarkup")));
		}

		return new BatchRequest(folder, items, noDuplicates);
	}

	static string? ReadString(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	static int? ReadInt32(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.TryGetInt32(out var number)
			? number
			: null;

	static bool ReadBoolean(JsonElement element, string name) =>
		TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.True;

	static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
				{
					value = property.Value;
					return true;
				}
			}
		}

		value = default;
		return false;
	}

	readonly record struct BatchRequest(string? Folder, List<DownloadItem> Items, bool NoDuplicates);
	readonly record struct DownloadItem(
		string? Filename,
		string? Shortcode,
		int? PhotoIndex,
		string? RawUrl,
		string? DataUri,
		string? SvgMarkup);
	sealed record DownloadFileResult(string Filename, bool Ok, string? Error, bool Skipped = false);
	sealed record MediaPayload(byte[] Bytes, string ContentType);

	sealed class DownloadFailureException(string code) : Exception(code)
	{
		public string Code { get; } = code;
	}
}
