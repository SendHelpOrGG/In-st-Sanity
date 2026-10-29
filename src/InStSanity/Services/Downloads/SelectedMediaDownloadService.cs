using System.Net;
using System.Text;
using InStSanity.Services.Instagram;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Downloads;

public sealed record SelectedMediaDownloadItem(
    string? Filename,
    string? Shortcode,
    int? PhotoIndex,
    int? VideoIndex,
    int? ContentIndex,
	bool IsVideo,
	string? RawUrl,
	string? DataUri,
	string? SvgMarkup,
	bool IsBrowserQueue = false);

public sealed record SelectedMediaDownloadFileResult(
    string Filename,
    bool Ok,
    string? Error,
    bool Skipped = false,
    DateTimeOffset? DownloadedAtUtc = null);

public sealed record SelectedMediaDownloadBatchResult(
    bool Ok,
    string? Error,
    string? Folder,
    int RequestedCount,
    int SavedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<SelectedMediaDownloadFileResult> Files);

/// <summary>
/// Shared safe media downloader for Library batches and the sequential Browser queue.
/// </summary>
public sealed class SelectedMediaDownloadService : IDisposable
{
	public const int MaxItemsPerRequest = 50;
	const int MaxMediaBytes = 50 * 1024 * 1024;
	const long MaxVideoBytes = 1024L * 1024 * 1024;
	const int Parallelism = 4;

	static readonly HashSet<string> SafePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".avif", ".heic", ".heif",
		".bmp", ".tif", ".tiff", ".jxl", ".ico",
	};

	static readonly HashSet<string> SafeVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".mp4", ".m4v", ".mov", ".webm", ".avi", ".mkv", ".3gp", ".mpeg", ".mpg",
	};

	static readonly HashSet<string> SafeExtensions = new(SafePhotoExtensions, StringComparer.OrdinalIgnoreCase);
	static SelectedMediaDownloadService()
	{
		SafeExtensions.UnionWith(SafeVideoExtensions);
	}

	static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
	};

	readonly InstagramHttp _http;
	readonly MediaCache _cache;
	readonly ILogger<SelectedMediaDownloadService> _logger;
	readonly HttpClient _browserMediaClient;

	public SelectedMediaDownloadService(
		InstagramHttp http,
		MediaCache cache,
		ILogger<SelectedMediaDownloadService> logger)
	{
		_http = http;
		_cache = cache;
		_logger = logger;
		var browserHandler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			UseCookies = true,
			CookieContainer = http.Cookies,
			AllowAutoRedirect = false,
		};
		_browserMediaClient = new HttpClient(browserHandler) { Timeout = http.Client.Timeout };
		_browserMediaClient.DefaultRequestHeaders.UserAgent.ParseAdd(InstagramHttp.UserAgent);
		_browserMediaClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
	}

	public void Dispose() => _browserMediaClient.Dispose();


    public string PrepareFolder(string? requestedFolder)
    {
        var folder = ResolveFolder(requestedFolder);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static bool IsAllowedBrowserMediaUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Length > 4096 ||
            !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0 && IsInstagramMediaUri(uri);
    }

    public async Task<SelectedMediaDownloadBatchResult> DownloadBatchAsync(
        string? requestedFolder,
        IReadOnlyList<SelectedMediaDownloadItem> items,
        bool noDuplicates,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new(false, "no_items", requestedFolder, 0, 0, 0, 0, Array.Empty<SelectedMediaDownloadFileResult>());
        }

        if (items.Count > MaxItemsPerRequest)
        {
            return new(false, "too_many_items", requestedFolder, items.Count, 0, 0, items.Count, Array.Empty<SelectedMediaDownloadFileResult>());
        }

        string folder;
        try
        {
            folder = PrepareFolder(requestedFolder);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _logger.LogWarning(exception, "Could not prepare the selected download folder");
            var failedFiles = items
                .Select((item, index) => new SelectedMediaDownloadFileResult(
                    SafeFileName(item.Filename, FallbackFileName(item, index), item.IsVideo ? ".mp4" : ".jpg", index),
                    Ok: false,
                    Error: "folder_unavailable"))
                .ToArray();

            return new(false, "folder_unavailable", requestedFolder, items.Count, 0, 0, items.Count, failedFiles);
        }

        var files = new SelectedMediaDownloadFileResult[items.Count];
        var writeTurns = items
            .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        writeTurns[0].TrySetResult(true);
        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Parallelism,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                files[index] = await DownloadOneCoreAsync(
                    items[index],
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
					noDuplicates,
					false,
					token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var savedCount = files.Count(file => file.Ok);
        var skippedCount = files.Count(file => file.Skipped);
        var failedCount = files.Count(file => !file.Ok && !file.Skipped);
        return new(failedCount == 0, null, folder, files.Length, savedCount, skippedCount, failedCount, files);
    }

    /// <summary>Downloads one Browser-queue item; callers invoke this serially to preserve queue order.</summary>
    public Task<SelectedMediaDownloadFileResult> DownloadOneAsync(
        SelectedMediaDownloadItem item,
        string resolvedFolder,
        int index,
        bool noDuplicates,
        CancellationToken cancellationToken)
    {
		if (!IsAllowedBrowserMediaUrl(item.RawUrl))
		{
			var filename = SafeFileName(item.Filename, FallbackFileName(item, index), item.IsVideo ? ".mp4" : ".jpg", index);
			return Task.FromResult(new SelectedMediaDownloadFileResult(filename, Ok: false, Error: "invalid_media_url"));
		}
		return DownloadOneCoreAsync(item, resolvedFolder, index, Task.CompletedTask, static () => { }, noDuplicates, true, cancellationToken);
    }

	async Task<SelectedMediaDownloadFileResult> DownloadOneCoreAsync(
		SelectedMediaDownloadItem item,
		string folder,
		int index,
		Task writeTurn,
		Action advanceWriteTurn,
		bool noDuplicates,
		bool requireHttps,
		CancellationToken cancellationToken)
	{
		var fallbackName = FallbackFileName(item, index);
		var fallbackExtension = item.IsVideo ? ".mp4" : ".jpg";
		var initialName = SafeFileName(item.Filename, fallbackName, fallbackExtension, index);
		var writeTurnAcquired = false;
		string? stagedVideoPath = null;

		try
		{
			var existingPath = noDuplicates ? FindExistingMediaPath(folder, item, initialName, index) : null;
			if (existingPath is not null)
			{
				await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
				writeTurnAcquired = true;
				return DuplicateSkipped(existingPath);
			}

			if (item.IsVideo)
			{
				var stagedVideo = await DownloadRemoteVideoAsync(item.RawUrl, folder, requireHttps, cancellationToken).ConfigureAwait(false);
				stagedVideoPath = stagedVideo.Path;
				await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
				writeTurnAcquired = true;
				existingPath = noDuplicates ? FindExistingMediaPath(folder, item, initialName, index) : null;
				if (existingPath is not null)
				{
					return DuplicateSkipped(existingPath);
				}

				var videoExtension = VideoExtensionFor(stagedVideo, initialName);
				var videoFilename = SafeFileName(item.Filename, fallbackName, videoExtension, index);
				var moveResult = MoveUniqueVideoFile(folder, videoFilename, stagedVideoPath, noDuplicates);
				if (!moveResult.Skipped)
				{
					stagedVideoPath = null;
				}

				return moveResult.Skipped
					? DuplicateSkipped(Path.Combine(folder, moveResult.Filename))
					: new SelectedMediaDownloadFileResult(
						moveResult.Filename,
						Ok: true,
						Error: null,
						DownloadedAtUtc: moveResult.DownloadedAtUtc);
			}

			// Fetch concurrently, but serialize disk writes in request order so media
			// from each post gets consecutive creation/modification timestamps.
			var payload = await LoadPayloadAsync(item, requireHttps, cancellationToken).ConfigureAwait(false);
			await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
			writeTurnAcquired = true;
			existingPath = noDuplicates ? FindExistingMediaPath(folder, item, initialName, index) : null;
			if (existingPath is not null)
			{
				return DuplicateSkipped(existingPath);
			}

			var extension = ExtensionFor(payload, initialName);
			var filename = SafeFileName(item.Filename, fallbackName, extension, index);
			var writeResult = await WriteUniqueFileAsync(folder, filename, payload.Bytes, noDuplicates, cancellationToken)
				.ConfigureAwait(false);

			return writeResult.Skipped
				? DuplicateSkipped(Path.Combine(folder, writeResult.Filename))
				: new SelectedMediaDownloadFileResult(
					writeResult.Filename,
					Ok: true,
					Error: null,
					DownloadedAtUtc: writeResult.DownloadedAtUtc);
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
				"Selected {MediaType} {Index} was not saved ({Reason})",
				item.IsVideo ? "video" : "photo",
				index + 1,
				exception.Code);
			return new SelectedMediaDownloadFileResult(initialName, Ok: false, Error: exception.Code);
		}
		catch (Exception exception)
		{
			if (!writeTurnAcquired)
			{
				await writeTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
				writeTurnAcquired = true;
			}

			_logger.LogWarning(exception, "Selected {MediaType} {Index} could not be saved", item.IsVideo ? "video" : "photo", index + 1);
			return new SelectedMediaDownloadFileResult(initialName, Ok: false, Error: "download_failed");
		}
		finally
		{
			if (stagedVideoPath is not null)
			{
				TryDelete(stagedVideoPath);
			}
			if (writeTurnAcquired)
			{
				advanceWriteTurn();
			}
		}
	}

	async Task<MediaPayload> LoadPayloadAsync(SelectedMediaDownloadItem item, bool requireHttps, CancellationToken cancellationToken)
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

		return await DownloadRemoteImageAsync(item.RawUrl, requireHttps, cancellationToken).ConfigureAwait(false);
	}

	async Task<HttpResponseMessage> SendMediaGetAsync(
		Uri initialUri,
		bool isVideo,
		bool requireHttps,
		CancellationToken cancellationToken)
	{
		if (!requireHttps)
		{
			using var request = _http.CreateMediaGet(initialUri, isVideo: isVideo);
			return await _http.Client
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
		}

		var currentUri = initialUri;
		for (var redirects = 0; ; redirects++)
		{
			using var request = _http.CreateMediaGet(currentUri, isVideo: isVideo);
			var response = await _browserMediaClient
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
			if (!IsRedirectStatus(response.StatusCode)) return response;

			using (response)
			{
				if (redirects >= 8 || response.Headers.Location is not { } location)
				{
					throw new DownloadFailureException("redirected_to_untrusted_host");
				}

				Uri redirectUri;
				try
				{
					redirectUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
				}
				catch (UriFormatException)
				{
					throw new DownloadFailureException("redirected_to_untrusted_host");
				}

				if (!IsAllowedBrowserMediaUrl(redirectUri.AbsoluteUri) ||
					Path.GetExtension(redirectUri.AbsolutePath).Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
				{
					throw new DownloadFailureException("redirected_to_untrusted_host");
				}
				currentUri = redirectUri;
			}
		}
	}

	static bool IsRedirectStatus(HttpStatusCode statusCode) =>
		statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
			HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

	async Task<MediaPayload> DownloadRemoteImageAsync(string rawUrl, bool requireHttps, CancellationToken cancellationToken)
	{
		var candidate = rawUrl.Trim();
		if (candidate.StartsWith("//", StringComparison.Ordinal))
		{
			candidate = "https:" + candidate;
		}

		if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsInstagramMediaUri(uri) ||
			(requireHttps && !IsAllowedBrowserMediaUrl(uri.AbsoluteUri)))
		{
			throw new DownloadFailureException("invalid_media_url");
		}

		var cached = await _cache.TryReadAsync(uri, cancellationToken).ConfigureAwait(false);
		if (cached is { Length: > 0 } && cached.Length <= MaxMediaBytes &&
			DetectImageContentType(cached) is { } cachedType)
		{
			return new MediaPayload(cached, cachedType);
		}

		using var response = await SendMediaGetAsync(uri, isVideo: false, requireHttps, cancellationToken)
			.ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new DownloadFailureException($"upstream_status_{(int)response.StatusCode}");
		}

		var finalUri = response.RequestMessage?.RequestUri ?? uri;
		if (!IsInstagramMediaUri(finalUri) ||
			(requireHttps && !IsAllowedBrowserMediaUrl(finalUri.AbsoluteUri)))
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

	async Task<StagedVideo> DownloadRemoteVideoAsync(string? rawUrl, string folder, bool requireHttps, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(rawUrl))
		{
			throw new DownloadFailureException("no_media_source");
		}

		var candidate = rawUrl.Trim();
		if (candidate.StartsWith("//", StringComparison.Ordinal))
		{
			candidate = "https:" + candidate;
		}

		if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsInstagramMediaUri(uri) ||
			(requireHttps && !IsAllowedBrowserMediaUrl(uri.AbsoluteUri)))
		{
			throw new DownloadFailureException("invalid_media_url");
		}

		using var response = await SendMediaGetAsync(uri, isVideo: true, requireHttps, cancellationToken)
			.ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new DownloadFailureException($"upstream_status_{(int)response.StatusCode}");
		}

		var finalUri = response.RequestMessage?.RequestUri ?? uri;
		if (!IsInstagramMediaUri(finalUri) ||
			(requireHttps && !IsAllowedBrowserMediaUrl(finalUri.AbsoluteUri)))
		{
			throw new DownloadFailureException("redirected_to_untrusted_host");
		}

		if (response.Content.Headers.ContentLength is > MaxVideoBytes)
		{
			throw new DownloadFailureException("video_too_large");
		}

		var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
		var temporaryPath = Path.Combine(folder, $".inst-sanity-{Guid.NewGuid():N}.download");
		try
		{
			await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			await using (var output = new FileStream(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 128 * 1024,
				options: FileOptions.Asynchronous | FileOptions.SequentialScan))
			{
				var chunk = new byte[128 * 1024];
				long totalBytes = 0;
				while (true)
				{
					var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
					if (read == 0) break;
					totalBytes += read;
					if (totalBytes > MaxVideoBytes)
					{
						throw new DownloadFailureException("video_too_large");
					}
					await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
				}
				if (totalBytes == 0)
				{
					throw new DownloadFailureException("empty_response");
				}
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}

			var prefix = await ReadFilePrefixAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
			if (!IsVideoPayload(contentType, prefix))
			{
				throw new DownloadFailureException("response_was_not_a_video");
			}

			return new StagedVideo(temporaryPath, contentType, prefix);
		}
		catch
		{
			TryDelete(temporaryPath);
			throw;
		}
	}

	static async Task<byte[]> ReadFilePrefixAsync(string path, CancellationToken cancellationToken)
	{
		await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var prefix = new byte[32];
		var read = await input.ReadAsync(prefix.AsMemory(), cancellationToken).ConfigureAwait(false);
		return prefix.AsSpan(0, read).ToArray();
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

	static bool IsVideoPayload(string contentType, byte[] payload) =>
		contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || DetectVideoContentType(payload) is not null;

	static string? DetectVideoContentType(byte[] payload)
	{
		if (payload.Length >= 12 &&
			payload[4] == 0x66 && payload[5] == 0x74 && payload[6] == 0x79 && payload[7] == 0x70)
		{
			return "video/mp4";
		}

		if (payload.Length >= 4 && payload[0] == 0x1A && payload[1] == 0x45 && payload[2] == 0xDF && payload[3] == 0xA3)
		{
			return "video/webm";
		}

		if (payload.Length >= 12 &&
			payload[0] == 0x52 && payload[1] == 0x49 && payload[2] == 0x46 && payload[3] == 0x46 &&
			payload[8] == 0x41 && payload[9] == 0x56 && payload[10] == 0x49 && payload[11] == 0x20)
		{
			return "video/x-msvideo";
		}

		return null;
	}

	static string VideoExtensionFor(StagedVideo video, string fallbackName)
	{
		var contentType = video.ContentType.Split(';')[0].Trim().ToLowerInvariant();
		var extension = contentType switch
		{
			"video/mp4" or "video/m4v" or "application/mp4" => ".mp4",
			"video/quicktime" => ".mov",
			"video/webm" => ".webm",
			"video/x-msvideo" => ".avi",
			"video/x-matroska" => ".mkv",
			"video/3gpp" => ".3gp",
			"video/mpeg" => ".mpeg",
			_ => string.Empty,
		};
		if (extension.Length == 0)
		{
			var detectedType = DetectVideoContentType(video.Prefix);
			extension = detectedType switch
			{
				"video/webm" => ".webm",
				"video/x-msvideo" => ".avi",
				"video/mp4" => ".mp4",
				_ => string.Empty,
			};
		}
		if (extension.Length > 0) return extension;
		var requestedExtension = Path.GetExtension(fallbackName);
		return SafeVideoExtensions.Contains(requestedExtension) ? requestedExtension : ".mp4";
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
		return SafePhotoExtensions.Contains(requestedExtension) ? requestedExtension : ".jpg";
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
			safeStem = $"instagram_{(SafeVideoExtensions.Contains(extension) ? "video" : "photo")}_{index + 1}";
		}

		if (ReservedNames.Contains(safeStem))
		{
			safeStem = "_" + safeStem;
		}

		var safeExtension = SafeExtensions.Contains(extension) ? extension : ".jpg";
		return safeStem + safeExtension;
	}

	static string FallbackFileName(SelectedMediaDownloadItem item, int index)
	{
		if (item.IsBrowserQueue)
		{
			var slideNumber = Math.Max(0, item.ContentIndex ?? index) + 1;
			return $"{SafeShortcode(item.Shortcode)}_slide{slideNumber}{(item.IsVideo ? ".mp4" : ".jpg")}";
		}

		if (item.IsVideo)
		{
			var videoNumber = Math.Max(0, item.VideoIndex ?? index) + 1;
			return $"{SafeShortcode(item.Shortcode)}_v{videoNumber}.mp4";
		}

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

	static string? FindExistingMediaPath(string folder, SelectedMediaDownloadItem item, string initialName, int index)
	{
		var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			Path.GetFileNameWithoutExtension(initialName),
		};
		var extensions = item.IsVideo ? SafeVideoExtensions : SafePhotoExtensions;
		var shortcode = SafeShortcode(item.Shortcode);
		if (!item.IsBrowserQueue && item.IsVideo)
		{
			var videoNumber = Math.Max(0, item.VideoIndex ?? index) + 1;
			var videoName = SafeFileName($"{shortcode}_v{videoNumber}.mp4", $"{shortcode}_v{videoNumber}.mp4", ".mp4", index);
			stems.Add(Path.GetFileNameWithoutExtension(videoName));
		}
		else if (!item.IsBrowserQueue)
		{
			var photoNumber = Math.Max(0, item.PhotoIndex ?? index) + 1;
			var currentName = SafeFileName($"{shortcode}_{photoNumber}.jpg", $"{shortcode}_{photoNumber}.jpg", ".jpg", index);
			var legacyName = SafeFileName($"{shortcode}_photo_{photoNumber}.jpg", $"{shortcode}_photo_{photoNumber}.jpg", ".jpg", index);
			stems.Add(Path.GetFileNameWithoutExtension(currentName));
			stems.Add(Path.GetFileNameWithoutExtension(legacyName));
		}

		string? latestPath = null;
		var latestTimestamp = DateTimeOffset.MinValue;
		foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
		{
			if (!extensions.Contains(Path.GetExtension(path))) continue;
			var stem = RemoveDuplicateSuffix(Path.GetFileNameWithoutExtension(path));
			if (!stems.Contains(stem)) continue;
			var timestamp = TryGetDownloadedAtUtc(path) ?? DateTimeOffset.MinValue;
			if (latestPath is null || timestamp >= latestTimestamp)
			{
				latestPath = path;
				latestTimestamp = timestamp;
			}
		}
		return latestPath;
	}

	static string RemoveDuplicateSuffix(string stem)
	{
		var suffixStart = stem.LastIndexOf(" (", StringComparison.Ordinal);
		return suffixStart > 0 && stem.EndsWith(')') &&
			int.TryParse(stem.AsSpan(suffixStart + 2, stem.Length - suffixStart - 3), out _)
			? stem[..suffixStart]
			: stem;
	}

	static SelectedMediaDownloadFileResult DuplicateSkipped(string existingPath) =>
		new(
			Path.GetFileName(existingPath),
			Ok: false,
			Error: null,
			Skipped: true,
			DownloadedAtUtc: TryGetDownloadedAtUtc(existingPath));

	static DateTimeOffset? TryGetDownloadedAtUtc(string path)
	{
		try
		{
			return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			return null;
		}
	}

	static async Task<(string Filename, bool Skipped, DateTimeOffset? DownloadedAtUtc)> WriteUniqueFileAsync(
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
					return (candidate, true, TryGetDownloadedAtUtc(path));
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

				return (candidate, false, TryGetDownloadedAtUtc(path));
			}
			catch
			{
				TryDelete(path);
				throw;
			}
		}

		throw new DownloadFailureException("filename_conflict");
	}

	static (string Filename, bool Skipped, DateTimeOffset? DownloadedAtUtc) MoveUniqueVideoFile(
		string folder,
		string filename,
		string stagedPath,
		bool noDuplicates)
	{
		var extension = Path.GetExtension(filename);
		var stem = Path.GetFileNameWithoutExtension(filename);
		for (var suffix = 1; suffix <= 1000; suffix++)
		{
			var candidate = suffix == 1 ? filename : $"{stem} ({suffix}){extension}";
			var path = Path.Combine(folder, candidate);
			try
			{
				File.Move(stagedPath, path);
				try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (IOException) { }
				return (candidate, false, TryGetDownloadedAtUtc(path));
			}
			catch (IOException) when (File.Exists(path))
			{
				if (noDuplicates) return (candidate, true, TryGetDownloadedAtUtc(path));
			}
		}
		throw new DownloadFailureException("filename_conflict");
	}

	public static string ResolveFolder(string? requestedFolder)
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

	sealed record MediaPayload(byte[] Bytes, string ContentType);
	sealed record StagedVideo(string Path, string ContentType, byte[] Prefix);

	sealed class DownloadFailureException(string code) : Exception(code)
	{
		public string Code { get; } = code;
	}
}
