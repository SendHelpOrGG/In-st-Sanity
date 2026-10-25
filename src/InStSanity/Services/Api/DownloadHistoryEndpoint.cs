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

					if (!TryParseNumberedFile(Path.GetFileNameWithoutExtension(path), out var shortcode, out var mediaIndex) ||
						!requested.TryGetValue(shortcode, out var canonicalShortcode))
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
					contentIndex = file.Index,
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

	static bool TryParseNumberedFile(string stem, out string shortcode, out int zeroBasedIndex)
	{
		shortcode = string.Empty;
		zeroBasedIndex = -1;
		if (string.IsNullOrWhiteSpace(stem))
		{
			return false;
		}

		// The previous unique-name mode appends " (2)", " (3)", and so on.
		var duplicateSuffixStart = stem.LastIndexOf(" (", StringComparison.Ordinal);
		if (duplicateSuffixStart > 0 && stem.EndsWith(')') &&
			int.TryParse(stem.AsSpan(duplicateSuffixStart + 2, stem.Length - duplicateSuffixStart - 3), out _))
		{
			stem = stem[..duplicateSuffixStart];
		}

		var numberSeparator = stem.LastIndexOf('_');
		if (numberSeparator <= 0 ||
			!int.TryParse(stem.AsSpan(numberSeparator + 1), out var oneBasedIndex) ||
			oneBasedIndex <= 0)
		{
			return false;
		}

		var parsedShortcode = stem[..numberSeparator];
		if (parsedShortcode.EndsWith("_photo", StringComparison.OrdinalIgnoreCase))
		{
			parsedShortcode = parsedShortcode[..^6];
		}

		if (parsedShortcode.Length == 0)
		{
			return false;
		}

		shortcode = parsedShortcode;
		zeroBasedIndex = oneBasedIndex - 1;
		return true;
	}

	sealed class MutablePostHistory(string shortcode)
	{
		public string Shortcode { get; } = shortcode;
		public Dictionary<int, HistoryMediaFile> Photos { get; } = [];
		public Dictionary<int, HistoryMediaFile> Videos { get; } = [];
	}

	sealed record HistoryMediaFile(int Index, DateTimeOffset DownloadedAtUtc, string Filename);
}
