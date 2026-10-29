using System.Text.Json;
using System.Text.RegularExpressions;
using InStSanity.Services.Downloads;

namespace InStSanity.Services.Browser;

public enum BrowserMediaOverlayAction
{
	Ignored,
	Ready,
	QueueChanged,
	StartDownload,
	CancelDownload,
	Rejected,
}

public sealed record BrowserMediaOverlayCommand(BrowserMediaOverlayAction Action, string? Error = null);

public sealed record BrowserQueuedMedia(
	string Key,
	string Shortcode,
	int SlideIndex,
	int? ContentCount,
	bool IsVideo,
	string RawUrl,
	string? ThumbnailUrl,
	string Filename,
	string? Error = null);

public sealed record BrowserMediaQueueSnapshot(
	IReadOnlyList<BrowserQueuedMedia> Items,
	bool IsDownloading,
	int CurrentIndex,
	int Total,
	string Status);

public sealed record BrowserQueueItemCompleted(
	BrowserQueuedMedia Item,
	SelectedMediaDownloadFileResult Result);

/// <summary>
/// In-memory session queue for images/videos selected in Instagram's native WebView.
/// Page messages are treated as untrusted and never carry a download folder or credentials.
/// </summary>
public sealed class BrowserMediaQueueService(SelectedMediaDownloadService downloads)
{
	public const int MaxItems = 50;
	const int MaxMessageBytes = 8 * 1024;
	static readonly Regex PostPathRegex = new(
		"^/(?:p|reel|reels|tv)/(?<shortcode>[A-Za-z0-9_-]{1,64})/?$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	readonly object _gate = new();
	readonly List<BrowserQueuedMedia> _items = [];
	CancellationTokenSource? _downloadCancellation;
	bool _isDownloading;
	int _currentIndex;
	int _total;
	string _status = string.Empty;

	public event Action<BrowserMediaQueueSnapshot>? StateChanged;
	public event Action<BrowserQueueItemCompleted>? ItemCompleted;

	public BrowserMediaQueueSnapshot GetSnapshot()
	{
		lock (_gate)
		{
			return SnapshotUnsafe();
		}
	}

	public BrowserMediaOverlayCommand HandleOverlayMessage(string? source, string? json)
	{
		if (!TryValidateInstagramSource(source, out _))
		{
			return new(BrowserMediaOverlayAction.Rejected, "untrusted_source");
		}

		if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaxMessageBytes)
		{
			return new(BrowserMediaOverlayAction.Rejected, "invalid_message");
		}

		try
		{
			using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 6 });
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!TryGetProperty(root, "v", out var versionValue) || versionValue.ValueKind != JsonValueKind.Number ||
				!versionValue.TryGetInt32(out var version) || version != 1 ||
				!TryGetString(root, "action", out var action))
			{
				return new(BrowserMediaOverlayAction.Rejected, "invalid_message");
			}

			switch (action)
			{
				case "overlay-ready":
					return new(BrowserMediaOverlayAction.Ready);

				case "toggle-media":
					if (!TryBuildQueueItem(root, out var item, out var error))
					{
						return new(BrowserMediaOverlayAction.Rejected, error);
					}

					return TryToggle(item!, out error)
						? new(BrowserMediaOverlayAction.QueueChanged)
						: new(BrowserMediaOverlayAction.Rejected, error);

				case "clear-queue":
					return Clear() ? new(BrowserMediaOverlayAction.QueueChanged) : new(BrowserMediaOverlayAction.Rejected, "queue_busy");

				case "start-download":
					lock (_gate)
					{
						if (_isDownloading) return new(BrowserMediaOverlayAction.Rejected, "queue_busy");
						if (_items.Count == 0) return new(BrowserMediaOverlayAction.Rejected, "queue_empty");
					}
					return new(BrowserMediaOverlayAction.StartDownload);

				case "cancel-download":
					CancelDownload();
					return new(BrowserMediaOverlayAction.CancelDownload);

				default:
					return new(BrowserMediaOverlayAction.Rejected, "unknown_action");
			}
		}
		catch (JsonException)
		{
			return new(BrowserMediaOverlayAction.Rejected, "invalid_message");
		}
	}

	public bool Clear()
	{
		lock (_gate)
		{
			if (_isDownloading) return false;
			if (_items.Count == 0 && _status.Length == 0) return true;
			_items.Clear();
			_status = string.Empty;
			_currentIndex = 0;
			_total = 0;
		}

		Publish();
		return true;
	}

	/// <summary>Logout is a queue boundary: cancel any in-flight request and discard this session's selection.</summary>
	public void ResetForLogout()
	{
		CancellationTokenSource? cancellation;
		lock (_gate)
		{
			cancellation = _downloadCancellation;
			_items.Clear();
			_status = string.Empty;
			_currentIndex = 0;
			_total = 0;
		}
		try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
		Publish();
	}

	public void CancelDownload()
	{
		CancellationTokenSource? cancellation;
		lock (_gate)
		{
			if (!_isDownloading) return;
			_status = "Cancelling…";
			cancellation = _downloadCancellation;
		}
		try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
		Publish();
	}

	public async Task DownloadAsync(
		string? photoFolder,
		string? videoFolder,
		bool noDuplicates,
		CancellationToken cancellationToken = default)
	{
		BrowserQueuedMedia[] batch;
		CancellationTokenSource cancellation;
		lock (_gate)
		{
			if (_isDownloading || _items.Count == 0) return;
			_isDownloading = true;
			_currentIndex = 0;
			_total = _items.Count;
			_status = "Preparing downloads…";
			batch = _items.ToArray();
			cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_downloadCancellation = cancellation;
		}
		Publish();

		var completed = 0;
		var skipped = 0;
		var failed = 0;
		var wasCancelled = false;
		var resolvedFolders = new Dictionary<bool, string?>();
		var folderErrors = new HashSet<bool>();
		foreach (var isVideo in batch.Select(item => item.IsVideo).Distinct())
		{
			try
			{
				resolvedFolders[isVideo] = downloads.PrepareFolder(isVideo ? videoFolder : photoFolder);
			}
			catch (Exception exception) when (IsFolderFailure(exception))
			{
				folderErrors.Add(isVideo);
			}
		}

		for (var index = 0; index < batch.Length; index++)
		{
			var item = batch[index];
			lock (_gate)
			{
				if (cancellation.IsCancellationRequested) break;
				_currentIndex = index + 1;
				_status = $"Downloading {index + 1} of {batch.Length} · {(item.IsVideo ? "video" : "photo")}";
			}
			Publish();

			SelectedMediaDownloadFileResult result;
			if (folderErrors.Contains(item.IsVideo))
			{
				result = new SelectedMediaDownloadFileResult(
					item.Filename,
					Ok: false,
					Error: "folder_unavailable");
			}
			else
			{
				var downloadItem = new SelectedMediaDownloadItem(
					Filename: item.Filename,
					Shortcode: item.Shortcode,
					PhotoIndex: null,
					VideoIndex: null,
					ContentIndex: item.SlideIndex,
					IsVideo: item.IsVideo,
					RawUrl: item.RawUrl,
					DataUri: null,
					SvgMarkup: null,
					IsBrowserQueue: true);
				try
				{
					result = await downloads.DownloadOneAsync(
						downloadItem,
						resolvedFolders[item.IsVideo]!,
						index,
						noDuplicates,
						cancellation.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
				{
					wasCancelled = true;
					break;
				}
			}

			lock (_gate)
			{
				var queueIndex = _items.FindIndex(candidate => candidate.Key == item.Key);
				if (result.Ok || result.Skipped)
				{
					completed++;
					if (result.Skipped) skipped++;
					if (queueIndex >= 0) _items.RemoveAt(queueIndex);
				}
				else
				{
					failed++;
					if (queueIndex >= 0) _items[queueIndex] = item with { Error = FriendlyError(result.Error) };
				}
				_status = result.Ok || result.Skipped
					? $"Saved {completed} of {batch.Length} · {failed} failed"
					: $"{completed} saved · {failed} failed; failures stay queued";
			}

			if (result.Ok || result.Skipped)
			{
				try { ItemCompleted?.Invoke(new BrowserQueueItemCompleted(item, result)); } catch { }
			}
			Publish();
		}

		if (cancellation.IsCancellationRequested && completed + failed < batch.Length) wasCancelled = true;
		lock (_gate)
		{
			_isDownloading = false;
			_currentIndex = 0;
			_total = 0;
			if (wasCancelled)
			{
				_status = _items.Count == 0 ? "Download cancelled." : "Cancelled · unfinished and failed items remain queued";
			}
			else if (_items.Count == 0)
			{
				_status = completed == 0 ? string.Empty : skipped > 0 && completed == skipped
					? $"{skipped} already saved · queue complete"
					: $"Saved {completed} media item{(completed == 1 ? "" : "s")}";
			}
			else
			{
				_status = $"{completed} saved · {failed} failed; failed items remain queued";
			}
			if (ReferenceEquals(_downloadCancellation, cancellation)) _downloadCancellation = null;
		}
		cancellation.Dispose();
		Publish();
	}

	bool TryToggle(BrowserQueuedMedia item, out string error)
	{
		var changed = false;
		lock (_gate)
		{
			error = string.Empty;
			if (_isDownloading)
			{
				error = "queue_busy";
				return false;
			}

			var existing = _items.FindIndex(candidate => candidate.Key == item.Key);
			if (existing >= 0)
			{
				_items.RemoveAt(existing);
				_status = _items.Count == 0 ? string.Empty : "Selection removed";
				changed = true;
			}
			else if (_items.Count >= MaxItems)
			{
				error = "queue_full";
				_status = $"Queue limit reached ({MaxItems})";
				changed = true;
			}
			else
			{
				_items.Add(item);
				_status = string.Empty;
				changed = true;
			}
		}

		if (changed) Publish();
		return error.Length == 0;
	}

	static bool TryBuildQueueItem(
		JsonElement root,
		out BrowserQueuedMedia? item,
		out string error)
	{
		item = null;
		error = "invalid_media";
		if (!TryReadString(root, "shortcode", 64, out var shortcode) ||
			!TryReadString(root, "postUrl", 2048, out var postUrl) ||
			!TryReadString(root, "mediaType", 8, out var mediaType) ||
			!TryReadString(root, "rawUrl", 4096, out var rawUrl) ||
			!TryReadInt(root, "slideIndex", 0, 100, out var slideIndex) ||
			!TryValidateInstagramPostUrl(postUrl, shortcode))
		{
			return false;
		}

		var isVideo = mediaType.Equals("video", StringComparison.OrdinalIgnoreCase);
		if (!isVideo && !mediaType.Equals("photo", StringComparison.OrdinalIgnoreCase)) return false;
		if (!SelectedMediaDownloadService.IsAllowedBrowserMediaUrl(rawUrl) ||
			(Uri.TryCreate(rawUrl, UriKind.Absolute, out var mediaUri) &&
				Path.GetExtension(mediaUri.AbsolutePath).Equals(".m3u8", StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		string? thumbnailUrl = null;
		if (TryReadString(root, "thumbnailUrl", 4096, out var candidateThumbnail) &&
			SelectedMediaDownloadService.IsAllowedBrowserMediaUrl(candidateThumbnail))
		{
			thumbnailUrl = candidateThumbnail;
		}

		int? contentCount = TryReadInt(root, "contentCount", 1, 100, out var count) ? count : null;
		var type = isVideo ? "video" : "photo";
		var key = $"{shortcode}|{type}|{slideIndex}";
		var extension = isVideo ? ".mp4" : ".jpg";
		var filename = $"{shortcode}_slide{slideIndex + 1}{extension}";
		item = new BrowserQueuedMedia(key, shortcode, slideIndex, contentCount, isVideo, rawUrl, thumbnailUrl, filename, null);
		error = string.Empty;
		return true;
	}

	static bool TryValidateInstagramSource(string? source, out Uri sourceUri)
	{
		sourceUri = null!;
		if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Port != 443 || uri.UserInfo.Length != 0)
		{
			return false;
		}

		var host = uri.IdnHost;
		if (!host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase) &&
			!host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		sourceUri = uri;
		return true;
	}

	static bool TryValidateInstagramPostUrl(string postUrl, string shortcode)
	{
		if (!Uri.TryCreate(postUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Port != 443 || uri.UserInfo.Length != 0)
		{
			return false;
		}

		var host = uri.IdnHost;
		if (!host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase) &&
			!host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var routeMatch = PostPathRegex.Match(uri.AbsolutePath);
		return routeMatch.Success && string.Equals(routeMatch.Groups["shortcode"].Value, shortcode, StringComparison.Ordinal);
	}

	static bool TryReadString(JsonElement element, string name, int maximumLength, out string value)
	{
		value = string.Empty;
		if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.String) return false;
		var text = property.GetString()?.Trim() ?? string.Empty;
		if (text.Length == 0 || text.Length > maximumLength) return false;
		value = text;
		return true;
	}

	static bool TryReadInt(JsonElement element, string name, int minimum, int maximum, out int value)
	{
		value = 0;
		return TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Number &&
			property.TryGetInt32(out value) && value >= minimum && value <= maximum;
	}

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

	BrowserMediaQueueSnapshot SnapshotUnsafe() =>
		new(_items.Select(item => item with { }).ToArray(), _isDownloading, _currentIndex, _total, _status);

	void Publish()
	{
		try { StateChanged?.Invoke(GetSnapshot()); } catch { }
	}

	static bool IsFolderFailure(Exception exception) =>
		exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or
			InvalidOperationException or System.Security.SecurityException;

	static string FriendlyError(string? error) => error switch
	{
		"folder_unavailable" => "Folder unavailable",
		"invalid_media_url" or "redirected_to_untrusted_host" => "Untrusted media URL",
		"video_too_large" => "Video exceeds the 1 GB limit",
		"image_too_large" => "Photo exceeds the 50 MB limit",
		_ => "Download failed · retry available",
	};
}
