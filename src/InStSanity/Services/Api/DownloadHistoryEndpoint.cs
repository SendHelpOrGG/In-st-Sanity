using System.Text.Json;
using InStSanity.Services.WebHost;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/download-history</c> — reads timestamps for already saved media files.</summary>
public sealed class DownloadHistoryEndpoint : IApiEndpoint
{
	const int MaxRequestBytes = 4 * 1024 * 1024;
	const int MaxShortcodes = 50_000;

	static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".avif", ".heic", ".heif",
		".bmp", ".tif", ".tiff", ".jxl", ".ico",
	};

	static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".mp4", ".m4v", ".mov", ".webm", ".avi", ".mkv", ".3gp", ".mpeg", ".mpg",
	};

	public string Route => "/api/download-history";

	public Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		if (request.Body.Length == 0 || request.Body.Length > MaxRequestBytes)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		string? requestedFolder;
		List<string> requestedShortcodes;
		try
		{
			using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 8 });
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
			}

			var root = document.RootElement;
			requestedFolder = ReadOptionalString(root, "folder");
			if (!root.TryGetProperty("shortcodes", out var shortcodesValue) ||
				shortcodesValue.ValueKind != JsonValueKind.Array)
			{
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
			}

			requestedShortcodes = shortcodesValue
				.EnumerateArray()
				.Where(value => value.ValueKind == JsonValueKind.String)
				.Select(value => NormalizeShortcode(value.GetString()))
				.Where(value => value.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Take(MaxShortcodes + 1)
				.ToList();
			if (requestedShortcodes.Count > MaxShortcodes)
			{
				return Task.FromResult(HttpResponseInfo.Error("too_many_shortcodes", 400));
			}
		}
		catch (JsonException)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		cancellationToken.ThrowIfCancellationRequested();

		string folder;
		try
		{
			folder = DownloadSelectedEndpoint.ResolveFolder(requestedFolder);
		}
		catch (Exception exception) when (
			exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or
			InvalidOperationException or System.Security.SecurityException)
		{
			return Task.FromResult(HttpResponseInfo.Json(new { ok = false, error = "folder_unavailable", history = new { } }));
		}

		var requested = requestedShortcodes.ToDictionary(value => value, value => value, StringComparer.OrdinalIgnoreCase);
		var found = new Dictionary<string, MutablePostHistory>(StringComparer.OrdinalIgnoreCase);
		if (Directory.Exists(folder))
		{
			try
			{
				foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
				{
					cancellationToken.ThrowIfCancellationRequested();
					var extension = Path.GetExtension(path);
					var isVideo = VideoExtensions.Contains(extension);
					if (!isVideo && !ImageExtensions.Contains(extension))
					{
						continue;
					}

					if (!TryParseNumberedFile(
						Path.GetFileNameWithoutExtension(path),
						isVideo,
						requested,
						out var canonicalShortcode,
						out var mediaIndex))
					{
						continue;
					}

					DateTimeOffset downloadedAtUtc;
					try
					{
						downloadedAtUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
					}
					catch (IOException)
					{
						continue;
					}

					if (!found.TryGetValue(canonicalShortcode, out var postHistory))
					{
						postHistory = new MutablePostHistory(canonicalShortcode);
						found.Add(canonicalShortcode, postHistory);
					}

					var record = new HistoryMediaFile(mediaIndex, downloadedAtUtc, Path.GetFileName(path));
					var target = isVideo ? postHistory.Videos : postHistory.Photos;
					if (!target.TryGetValue(mediaIndex, out var current) || downloadedAtUtc >= current.DownloadedAtUtc)
					{
						target[mediaIndex] = record;
					}
				}
			}
			catch (Exception exception) when (
				exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
			{
				// A partially readable folder still yields the timestamps collected so far.
			}
		}

		var history = found.ToDictionary(
			pair => pair.Key,
			pair => new
			{
				shortcode = pair.Value.Shortcode,
				photos = pair.Value.Photos.Values.OrderBy(file => file.Index).Select(file => new
				{
					photoIndex = file.Index,
					downloadedAtUtc = file.DownloadedAtUtc,
					filename = file.Filename,
				}).ToArray(),
				videos = pair.Value.Videos.Values.OrderBy(file => file.Index).Select(file => new
				{
					videoIndex = file.Index,
					downloadedAtUtc = file.DownloadedAtUtc,
					filename = file.Filename,
				}).ToArray(),
			},
			StringComparer.OrdinalIgnoreCase);

		return Task.FromResult(HttpResponseInfo.Json(new { ok = true, history }));
	}

	static string? ReadOptionalString(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	}

	static string NormalizeShortcode(string? shortcode)
	{
		if (string.IsNullOrWhiteSpace(shortcode))
		{
			return string.Empty;
		}

		var source = shortcode.Trim();
		var safe = new string(source
			.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' ? character : '_')
			.ToArray());
		return safe.Length <= 128 ? safe : string.Empty;
	}

	static bool TryParseNumberedFile(
		string stem,
		bool isVideo,
		IReadOnlyDictionary<string, string> requestedShortcodes,
		out string canonicalShortcode,
		out int zeroBasedIndex)
	{
		canonicalShortcode = string.Empty;
		zeroBasedIndex = -1;
		if (string.IsNullOrWhiteSpace(stem))
		{
			return false;
		}

		// The no-duplicates-off mode appends " (2)", " (3)", and so on.
		var duplicateSuffixStart = stem.LastIndexOf(" (", StringComparison.Ordinal);
		if (duplicateSuffixStart > 0 && stem.EndsWith(')') &&
			int.TryParse(stem.AsSpan(duplicateSuffixStart + 2, stem.Length - duplicateSuffixStart - 3), out _))
		{
			stem = stem[..duplicateSuffixStart];
		}

		if (!isVideo)
		{
			if (stem.EndsWith("_resized", StringComparison.OrdinalIgnoreCase))
			{
				stem = stem[..^"_resized".Length];
			}
			else if (stem.EndsWith("_edited", StringComparison.OrdinalIgnoreCase))
			{
				stem = stem[..^"_edited".Length];
			}
		}

		if (isVideo)
		{
			if (TryReadNumberedMarker(stem, "_v", out var videoPrefix, out var videoNumber) &&
				TryLookupNumberedShortcode(videoPrefix, requestedShortcodes, out canonicalShortcode))
			{
				zeroBasedIndex = videoNumber - 1;
				return true;
			}
			return false;
		}

		// Current photos are account_shortcode_phN. Accept the legacy
		// shortcode_photo_N and shortcode_N spellings as well.
		if (TryReadNumberedMarker(stem, "_ph", out var photoPrefix, out var photoNumber) &&
			TryLookupNumberedShortcode(photoPrefix, requestedShortcodes, out canonicalShortcode))
		{
			zeroBasedIndex = photoNumber - 1;
			return true;
		}

		if (TryReadNumberedMarker(stem, "_photo_", out var legacyPhotoPrefix, out var legacyPhotoNumber) &&
			requestedShortcodes.TryGetValue(legacyPhotoPrefix, out var resolvedLegacyShortcode))
		{
			canonicalShortcode = resolvedLegacyShortcode;
			zeroBasedIndex = legacyPhotoNumber - 1;
			return true;
		}

		if (TryReadNumberedMarker(stem, "_", out var numberedPrefix, out var photoIndex) &&
			requestedShortcodes.TryGetValue(numberedPrefix, out var resolvedNumberedShortcode))
		{
			canonicalShortcode = resolvedNumberedShortcode;
			zeroBasedIndex = photoIndex - 1;
			return true;
		}

		return false;
	}

	static bool TryLookupNumberedShortcode(
		string prefix,
		IReadOnlyDictionary<string, string> requestedShortcodes,
		out string canonicalShortcode)
	{
		if (requestedShortcodes.TryGetValue(prefix, out canonicalShortcode!)) return true;

		// Account names and shortcodes may both contain underscores. Walk possible
		// account/shortcode boundaries and use the requested-shortcode lookup rather
		// than scanning every requested post for every file in the folder.
		for (var separator = prefix.IndexOf('_'); separator > 0; separator = prefix.IndexOf('_', separator + 1))
		{
			if (requestedShortcodes.TryGetValue(prefix[(separator + 1)..], out canonicalShortcode!)) return true;
		}

		canonicalShortcode = string.Empty;
		return false;
	}

	static bool TryReadNumberedMarker(string stem, string marker, out string prefix, out int oneBasedIndex)
	{
		prefix = string.Empty;
		oneBasedIndex = 0;
		var separator = stem.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (separator <= 0 ||
			!int.TryParse(stem.AsSpan(separator + marker.Length), out oneBasedIndex) ||
			oneBasedIndex <= 0)
		{
			return false;
		}

		prefix = stem[..separator];
		return prefix.Length > 0;
	}

	sealed class MutablePostHistory(string shortcode)
	{
		public string Shortcode { get; } = shortcode;
		public Dictionary<int, HistoryMediaFile> Photos { get; } = [];
		public Dictionary<int, HistoryMediaFile> Videos { get; } = [];
	}

	sealed record HistoryMediaFile(int Index, DateTimeOffset DownloadedAtUtc, string Filename);
}
