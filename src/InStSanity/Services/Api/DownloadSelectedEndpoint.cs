using System.Text.Json;
using InStSanity.Services.Downloads;
using InStSanity.Services.WebHost;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/download-selected</c> — downloads selected photos or videos into configured folders.</summary>
public sealed class DownloadSelectedEndpoint(SelectedMediaDownloadService downloads) : IApiEndpoint
{
	const int MaxItemsPerRequest = SelectedMediaDownloadService.MaxItemsPerRequest;

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

		var result = await downloads.DownloadBatchAsync(
			batch.Folder,
			batch.Items,
			batch.NoDuplicates,
			cancellationToken).ConfigureAwait(false);

		if (result.Error is not null)
		{
			return HttpResponseInfo.Json(new
			{
				ok = false,
				error = result.Error,
				folder = result.Folder,
				savedCount = result.SavedCount,
				skippedCount = result.SkippedCount,
				failedCount = result.FailedCount,
				files = result.Files,
			});
		}

		return HttpResponseInfo.Json(new
		{
			ok = result.Ok,
			requestedCount = result.RequestedCount,
			savedCount = result.SavedCount,
			skippedCount = result.SkippedCount,
			failedCount = result.FailedCount,
			folder = result.Folder,
			files = result.Files,
		});
	}

	internal static string ResolveFolder(string? requestedFolder) =>
		SelectedMediaDownloadService.ResolveFolder(requestedFolder);

	static HttpResponseInfo Failure(string error, int failedCount = 0) =>
		HttpResponseInfo.Json(new
		{
			ok = false,
			error,
			savedCount = 0,
			failedCount,
			skippedCount = 0,
			files = Array.Empty<SelectedMediaDownloadFileResult>(),
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
		var items = new List<SelectedMediaDownloadItem>();
		foreach (var itemElement in itemsElement.EnumerateArray())
		{
			if (items.Count >= MaxItemsPerRequest)
			{
				items.Add(new SelectedMediaDownloadItem(null, null, null, null, null, false, null, null, null));
				break;
			}

			items.Add(new SelectedMediaDownloadItem(
				ReadString(itemElement, "filename"),
				ReadString(itemElement, "shortcode"),
				ReadInt32(itemElement, "photoIndex"),
				ReadInt32(itemElement, "videoIndex"),
				ReadInt32(itemElement, "contentIndex"),
				ReadBoolean(itemElement, "isVideo"),
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

	readonly record struct BatchRequest(
		string? Folder,
		List<SelectedMediaDownloadItem> Items,
		bool NoDuplicates);
}
