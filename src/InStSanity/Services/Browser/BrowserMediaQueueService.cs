using System.Text.Json;
using System.Text.RegularExpressions;
using InStSanity.Services.Downloads;
using InStSanity.Services.Instagram;

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
	string Account,
	int SlideIndex,
	int? ContentCount,
	bool IsVideo,
	string RawUrl,
	string? ThumbnailUrl,
	string Filename,
	string? Error = null,
	bool IsDownloaded = false,
	bool WasSkipped = false);

public sealed record BrowserMediaQueueSnapshot(
	IReadOnlyList<BrowserQueuedMedia> Items,
	bool IsDownloading,
	int CurrentIndex,
	int Total,
	string Status,
	bool HasBeenOpened);

public sealed record BrowserQueueItemCompleted(
	BrowserQueuedMedia Item,
	SelectedMediaDownloadFileResult Result);

/// <summary>
/// In-memory session queue for images/videos selected in Instagram's native WebView.
/// Page messages are treated as untrusted and never carry a download folder or credentials.
/// </summary>
public sealed class BrowserMediaQueueService(SelectedMediaDownloadService downloads, InstagramResolver resolver)
{
	public const int MaxItems = 50;
	const int MaxMessageBytes = 8 * 1024;
	static readonly Regex PostPathRegex = new(
		"^/(?:(?<account>[A-Za-z0-9._]{1,30})/)?(?:p|reel|reels|tv)/(?<shortcode>[A-Za-z0-9_-]{1,64})/?$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	readonly object _gate = new();
	readonly List<BrowserQueuedMedia> _items = [];
	CancellationTokenSource? _downloadCancellation;
	bool _isDownloading;
	int _currentIndex;
	int _total;
	string _status = string.Empty;
	bool _hasBeenOpened;

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
				!TryReadString(root, "action", 32, out var action))
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

				case "remove-media":
					if (!TryReadString(root, "key", 4200, out var key))
					{
						return new(BrowserMediaOverlayAction.Rejected, "invalid_media_key");
					}
					if (Remove(key)) return new(BrowserMediaOverlayAction.QueueChanged);
					lock (_gate)
					{
						return new(BrowserMediaOverlayAction.Rejected, _isDownloading ? "queue_busy" : "queue_item_unavailable");
					}

				case "clear-queue":
					return Clear() ? new(BrowserMediaOverlayAction.QueueChanged) : new(BrowserMediaOverlayAction.Rejected, "queue_busy");

				case "start-download":
					lock (_gate)
					{
						if (_isDownloading) return new(BrowserMediaOverlayAction.Rejected, "queue_busy");
						if (_items.Count == 0) return new(BrowserMediaOverlayAction.Rejected, "queue_empty");
						if (_items.All(item => item.IsDownloaded)) return new(BrowserMediaOverlayAction.Rejected, "queue_complete");
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

	public bool Remove(string key)
	{
		if (string.IsNullOrWhiteSpace(key) || key.Length > 4200) return false;
		lock (_gate)
		{
			if (_isDownloading) return false;
			var index = _items.FindIndex(item => string.Equals(item.Key, key, StringComparison.Ordinal));
			if (index < 0) return false;
			_items.RemoveAt(index);
			_status = _items.Count == 0 ? string.Empty : "Selection removed";
		}
		Publish();
		return true;
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
			_hasBeenOpened = false;
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
			if (_isDownloading || _items.Count == 0 || _items.All(item => item.IsDownloaded)) return;
			for (var index = 0; index < _items.Count; index++)
			{
				if (!_items[index].IsDownloaded && _items[index].Error is not null)
				{
					_items[index] = _items[index] with { Error = null };
				}
			}
			batch = _items.Where(item => !item.IsDownloaded).ToArray();
			if (batch.Length == 0) return;
			_isDownloading = true;
			_currentIndex = 0;
			_total = batch.Length;
			_status = "Preparing downloads…";
			cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_downloadCancellation = cancellation;
		}
		Publish();

		var completed = 0;
		var skipped = 0;
		var failed = 0;
		var wasCancelled = false;
		var batchFailure = false;
		try
		{
			var resolvedFolders = new Dictionary<bool, string?>();
			var folderErrors = new HashSet<bool>();
			foreach (var isVideo in batch.Select(item => item.IsVideo).Distinct())
			{
				if (isVideo && string.IsNullOrWhiteSpace(videoFolder))
				{
					// Never let a missing dedicated video setting silently resolve to the
					// downloader's default folder.
					folderErrors.Add(true);
					continue;
				}

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
					_status = item.IsVideo
						? $"Resolving video source {index + 1} of {batch.Length}…"
						: $"Resolving full-size photo {index + 1} of {batch.Length}…";
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
					try
					{
						var rawUrl = item.RawUrl;
						if (item.IsVideo)
						{
							// Match the Saved Posts path: resolve the selected post slide at download
							// time instead of trusting the WebView's possibly ephemeral video source.
							try
							{
								var resolvedVideoUrl = await ResolveQueuedVideoUrlAsync(item, cancellation.Token).ConfigureAwait(false);
							if (!string.IsNullOrWhiteSpace(resolvedVideoUrl)) rawUrl = resolvedVideoUrl;
							}
							catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
							{
								throw;
							}
							catch (Exception)
							{
								// Fall back to a validated WebView source below if resolution is unavailable.
							}

							if (!IsDownloadableVideoUrl(rawUrl)) rawUrl = string.Empty;
						}
						else
						{
							// Instagram's visible <img> URL is often a responsive/preview variant.
							// Always resolve the post's selected slide and download its best image URL.
							try
							{
								// The WebView carousel index can lag; the resolved media URL identifies the actual post slide.
								var resolvedPhoto = await ResolveQueuedPhotoUrlAsync(item, cancellation.Token).ConfigureAwait(false);
								if (resolvedPhoto.HasValue)
								{
									rawUrl = resolvedPhoto.Value.Url;
									var resolvedSlideIndex = resolvedPhoto.Value.SlideIndex;
									var resolvedFilename = BuildQueueFilename(item.Account, item.Shortcode, item.IsVideo, resolvedSlideIndex);
									if (item.SlideIndex != resolvedSlideIndex || !string.Equals(item.Filename, resolvedFilename, StringComparison.Ordinal))
									{
										item = item with { SlideIndex = resolvedSlideIndex, Filename = resolvedFilename };
										UpdateResolvedQueueItem(item);
									}
								}
								else
								{
									rawUrl = string.Empty;
								}
							}
							catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
							{
								throw;
							}
							catch (Exception)
							{
								rawUrl = string.Empty;
							}
						}

						if (string.IsNullOrWhiteSpace(rawUrl))
						{
							result = new SelectedMediaDownloadFileResult(
								item.Filename,
								Ok: false,
								Error: item.IsVideo ? "video_source_unavailable" : "photo_full_size_unavailable");
						}
						else
						{
							lock (_gate)
							{
								_status = $"Downloading {index + 1} of {batch.Length} · {(item.IsVideo ? "video" : "full-size photo")}";
							}
							Publish();

							var downloadItem = new SelectedMediaDownloadItem(
								Filename: item.Filename,
								Shortcode: item.Shortcode,
								PhotoIndex: null,
								VideoIndex: null,
								ContentIndex: item.SlideIndex,
								IsVideo: item.IsVideo,
								RawUrl: rawUrl,
								DataUri: null,
								SvgMarkup: null,
								IsBrowserQueue: true,
								Account: item.Account);
							result = await downloads.DownloadOneAsync(
								downloadItem,
								resolvedFolders[item.IsVideo]!,
								index,
								noDuplicates,
								cancellation.Token).ConfigureAwait(false);

							if (item.IsVideo && !result.Ok && !result.Skipped &&
								IsDownloadableVideoUrl(item.RawUrl) &&
								!string.Equals(item.RawUrl, rawUrl, StringComparison.Ordinal))
							{
								// A cached or newly resolved URL can still be stale; retry once with the
								// current, allow-listed WebView source before keeping the item failed.
								result = await downloads.DownloadOneAsync(
									downloadItem with { RawUrl = item.RawUrl },
									resolvedFolders[item.IsVideo]!,
									index,
									noDuplicates,
									cancellation.Token).ConfigureAwait(false);
							}
						}
					}
					catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
					{
						wasCancelled = true;
						break;
					}
					catch (Exception exception)
					{
						System.Diagnostics.Debug.WriteLine(
							$"[In-st-Sanity] Browser queue item {index + 1} failed unexpectedly ({exception.GetType().Name}).");
						result = new SelectedMediaDownloadFileResult(
							item.Filename,
							Ok: false,
							Error: "download_failed");
					}
				}

				lock (_gate)
				{
					var queueIndex = _items.FindIndex(candidate => candidate.Key == item.Key);
					if (result.Ok || result.Skipped)
					{
						completed++;
						if (result.Skipped) skipped++;
						if (queueIndex >= 0)
						{
							_items[queueIndex] = item with
							{
								Error = null,
								IsDownloaded = true,
								WasSkipped = result.Skipped,
							};
						}
					}
					else
					{
						failed++;
						if (queueIndex >= 0)
						{
							_items[queueIndex] = item with
							{
								Error = FriendlyError(result.Error),
								IsDownloaded = false,
								WasSkipped = false,
							};
						}
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
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			wasCancelled = true;
		}
		catch (Exception exception)
		{
			batchFailure = true;
			System.Diagnostics.Debug.WriteLine(
				$"[In-st-Sanity] Browser queue batch stopped unexpectedly ({exception.GetType().Name}).");
		}
		finally
		{
			if (cancellation.IsCancellationRequested && completed + failed < batch.Length) wasCancelled = true;
			lock (_gate)
			{
				_isDownloading = false;
				_currentIndex = 0;
				_total = 0;
				var pendingCount = _items.Count(item => !item.IsDownloaded);
				if (wasCancelled)
				{
					_status = _items.Count == 0
						? "Download cancelled."
						: pendingCount == 0
							? "All queued media are saved · queue retained"
							: "Download cancelled · completed items are kept; remaining items stay queued";
				}
				else if (batchFailure)
				{
					_status = pendingCount == 0
						? "Download stopped · queue retained"
						: $"{completed} completed · download stopped; remaining items stay queued";
				}
				else if (pendingCount == 0)
				{
					_status = skipped > 0
						? completed == skipped
							? $"{skipped} already saved · queue retained"
							: $"Saved {completed - skipped} · {skipped} already saved · queue retained"
						: $"Saved {completed} media item{(completed == 1 ? "" : "s")} · queue retained";
				}
				else if (failed > 0)
				{
					_status = $"{completed} completed · {failed} failed; failed items stay queued";
				}
				else
				{
					_status = $"{completed} completed · remaining items stay queued";
				}
				if (ReferenceEquals(_downloadCancellation, cancellation)) _downloadCancellation = null;
			}
			cancellation.Dispose();
			Publish();
		}
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
				_hasBeenOpened = true;
				changed = true;
			}
		}

		if (changed) Publish();
		return error.Length == 0;
	}

	async Task<(string Url, int SlideIndex)?> ResolveQueuedPhotoUrlAsync(BrowserQueuedMedia item, CancellationToken cancellationToken)
	{
		var postUrl = $"https://www.instagram.com/p/{Uri.EscapeDataString(item.Shortcode)}/";
		var post = await resolver.ResolveAsync(postUrl, cancellationToken).ConfigureAwait(false);
		if (post is null || !post.Ok || post.Pictures.Count == 0) return null;

		var slideIndex = FindQueuedPhotoSlide(item, post.Pictures);
		if (slideIndex < 0) return null;

		var media = post.Pictures[slideIndex];
		if (media.IsVideo || !IsFullSizeResolvedPhoto(media)) return null;

		var imageUrl = media.ImgUrl;
		if (!SelectedMediaDownloadService.IsAllowedBrowserMediaUrl(imageUrl)) return null;
		return (Url: imageUrl, SlideIndex: slideIndex);
	}

	static bool IsFullSizeResolvedPhoto(ResolvedMedia media) => media.HasFullSizeImage;

	static int FindQueuedPhotoSlide(BrowserQueuedMedia item, IReadOnlyList<ResolvedMedia> pictures)
	{
		var matchingIndices = new List<int>();
		for (var index = 0; index < pictures.Count; index++)
		{
			if (!pictures[index].IsVideo && HasSameMediaPath(item.RawUrl, pictures[index].ImgUrl))
			{
				matchingIndices.Add(index);
			}
		}

		if (matchingIndices.Count == 1) return matchingIndices[0];
		if (matchingIndices.Contains(item.SlideIndex)) return item.SlideIndex;
		if (item.SlideIndex >= 0 && item.SlideIndex < pictures.Count && !pictures[item.SlideIndex].IsVideo)
		{
			return item.SlideIndex;
		}

		var photoIndices = Enumerable.Range(0, pictures.Count).Where(index => !pictures[index].IsVideo).ToArray();
		return photoIndices.Length == 1 ? photoIndices[0] : -1;
	}

	static int FindQueuedVideoSlide(BrowserQueuedMedia item, IReadOnlyList<ResolvedMedia> pictures)
	{
		var videoIndices = Enumerable.Range(0, pictures.Count).Where(index => pictures[index].IsVideo).ToArray();
		var sourceMatches = videoIndices
			.Where(index => HasSameMediaPath(item.RawUrl, pictures[index].VideoUrl))
			.ToArray();
		if (sourceMatches.Length == 1) return sourceMatches[0];
		if (sourceMatches.Contains(item.SlideIndex)) return item.SlideIndex;

		var posterMatches = videoIndices
			.Where(index => HasSameMediaPath(item.ThumbnailUrl, pictures[index].ThumbnailUrl) ||
				HasSameMediaPath(item.ThumbnailUrl, pictures[index].ImgUrl))
			.ToArray();
		if (posterMatches.Length == 1) return posterMatches[0];
		if (posterMatches.Contains(item.SlideIndex)) return item.SlideIndex;
		if (item.SlideIndex >= 0 && item.SlideIndex < pictures.Count && pictures[item.SlideIndex].IsVideo)
		{
			return item.SlideIndex;
		}

		return videoIndices.Length == 1 ? videoIndices[0] : -1;
	}

	static bool HasSameMediaPath(string? firstUrl, string? secondUrl)
	{
		if (!Uri.TryCreate(firstUrl, UriKind.Absolute, out var first) ||
			!Uri.TryCreate(secondUrl, UriKind.Absolute, out var second))
		{
			return false;
		}

		var firstPath = Uri.UnescapeDataString(first.AbsolutePath);
		var secondPath = Uri.UnescapeDataString(second.AbsolutePath);
		if (firstPath.Equals(secondPath, StringComparison.OrdinalIgnoreCase)) return true;

		var firstFileName = firstPath[(firstPath.LastIndexOf('/') + 1)..];
		var secondFileName = secondPath[(secondPath.LastIndexOf('/') + 1)..];
		return firstFileName.Length > 0 &&
			firstFileName.Equals(secondFileName, StringComparison.OrdinalIgnoreCase);
	}

	async Task<string?> ResolveQueuedVideoUrlAsync(BrowserQueuedMedia item, CancellationToken cancellationToken)
	{
		var postUrl = $"https://www.instagram.com/p/{Uri.EscapeDataString(item.Shortcode)}/";
		var post = await resolver.ResolveAsync(postUrl, cancellationToken).ConfigureAwait(false);
		if (post is null || !post.Ok || post.Pictures.Count == 0) return null;

		var slideIndex = FindQueuedVideoSlide(item, post.Pictures);
		if (slideIndex < 0) return null;

		var videoUrl = post.Pictures[slideIndex].VideoUrl;
		return IsDownloadableVideoUrl(videoUrl) ? videoUrl : null;
	}

	static bool IsDownloadableVideoUrl(string? rawUrl) =>
		SelectedMediaDownloadService.IsAllowedBrowserMediaUrl(rawUrl) && !IsUnsupportedVideoStream(rawUrl);

	static bool IsUnsupportedVideoStream(string? rawUrl)
	{
		if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) return false;
		var extension = Path.GetExtension(uri.AbsolutePath);
		return extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".mpd", StringComparison.OrdinalIgnoreCase);
	}

	static string BuildQueueFilename(string account, string shortcode, bool isVideo, int slideIndex)
	{
		var extension = isVideo ? ".mp4" : ".jpg";
		return isVideo
			? $"{account}_{shortcode}_v{slideIndex + 1}{extension}"
			: $"{account}_{shortcode}_ph{slideIndex + 1}{extension}";
	}

	static bool TryBuildQueueItem(
		JsonElement root,
		out BrowserQueuedMedia? item,
		out string error)
	{
		item = null;
		if (!TryReadString(root, "shortcode", 64, out var shortcode))
		{
			error = "invalid_media_shortcode";
			return false;
		}
		if (!TryReadString(root, "postUrl", 2048, out var postUrl) || !TryValidateInstagramPostUrl(postUrl, shortcode))
		{
			error = "invalid_media_post_url";
			return false;
		}
		var account = TryReadString(root, "account", 80, out var suppliedAccount)
			? SafeAccountName(suppliedAccount)
			: AccountFromPostUrl(postUrl);
		if (!TryReadString(root, "mediaType", 8, out var mediaType) ||
			(!mediaType.Equals("video", StringComparison.OrdinalIgnoreCase) &&
			 !mediaType.Equals("photo", StringComparison.OrdinalIgnoreCase)))
		{
			error = "invalid_media_type";
			return false;
		}
		var isVideo = mediaType.Equals("video", StringComparison.OrdinalIgnoreCase);
		var hasRawUrl = TryReadString(root, "rawUrl", 4096, out var rawUrl);
		if (!hasRawUrl)
		{
			if (!isVideo)
			{
				error = "invalid_media_raw_url";
				return false;
			}
			rawUrl = string.Empty;
		}
		else if (!SelectedMediaDownloadService.IsAllowedBrowserMediaUrl(rawUrl))
		{
			if (!isVideo)
			{
				error = "invalid_media_raw_url";
				return false;
			}
			rawUrl = string.Empty;
		}
		if (IsUnsupportedVideoStream(rawUrl))
		{
			if (!isVideo)
			{
				error = "invalid_media_stream_url";
				return false;
			}
			rawUrl = string.Empty;
		}
		if (!TryReadInt(root, "slideIndex", 0, 100, out var slideIndex))
		{
			error = "invalid_media_slide_index";
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
		var mediaIdentity = Uri.TryCreate(rawUrl, UriKind.Absolute, out var identityUri)
			? identityUri.AbsolutePath
			: $"slide:{slideIndex}";
		var key = $"{shortcode}|{type}|{mediaIdentity}";
		// Keep queue identity URL-based even though saved filenames use concise phN/vN ordinals.
		var filename = BuildQueueFilename(account, shortcode, isVideo, slideIndex);
		item = new BrowserQueuedMedia(key, shortcode, account, slideIndex, contentCount, isVideo, rawUrl, thumbnailUrl, filename, null);
		error = string.Empty;
		return true;
	}

	void UpdateResolvedQueueItem(BrowserQueuedMedia item)
	{
		var changed = false;
		lock (_gate)
		{
			var queueIndex = _items.FindIndex(candidate => candidate.Key == item.Key);
			if (queueIndex < 0) return;
			var queuedItem = _items[queueIndex];
			if (queuedItem.SlideIndex == item.SlideIndex &&
				string.Equals(queuedItem.Filename, item.Filename, StringComparison.Ordinal)) return;
			_items[queueIndex] = queuedItem with { SlideIndex = item.SlideIndex, Filename = item.Filename };
			changed = true;
		}
		if (changed) Publish();
	}

	static string AccountFromPostUrl(string postUrl)
	{
		if (!Uri.TryCreate(postUrl, UriKind.Absolute, out var uri)) return "instagram";
		var match = PostPathRegex.Match(Uri.UnescapeDataString(uri.AbsolutePath));
		return match.Success ? SafeAccountName(match.Groups["account"].Value) : "instagram";
	}

	static string SafeAccountName(string? value)
	{
		var source = string.IsNullOrWhiteSpace(value) ? "instagram" : value.Trim().TrimStart('@');
		var safe = new string(source
			.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' ? character : '_')
			.ToArray())
			.Trim(' ', '.');
		if (safe.Length > 80) safe = safe[..80].TrimEnd(' ', '.');
		return safe.Length == 0 ? "instagram" : safe;
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
		new(_items.Select(item => item with { }).ToArray(), _isDownloading, _currentIndex, _total, _status, _hasBeenOpened);

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
		"video_source_unavailable" => "Video source unavailable",
		"photo_full_size_unavailable" => "Full-size photo unavailable · check Instagram sign-in and retry",
		"invalid_media_url" or "redirected_to_untrusted_host" => "Untrusted media URL",
		"video_too_large" => "Video exceeds the 1 GB limit",
		"image_too_large" => "Photo exceeds the 50 MB limit",
		_ => "Download failed · retry available",
	};
}
