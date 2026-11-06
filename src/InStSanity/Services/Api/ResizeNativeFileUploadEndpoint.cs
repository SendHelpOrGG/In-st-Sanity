using InStSanity.Services.Images;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary>Stages a native-selected image without copying it through the WebView's file input.</summary>
public sealed class ResizeNativeFileUploadEndpoint : IApiEndpoint
{
	const long MaxFileBytes = 50L * 1024 * 1024;

	readonly PhotoResizeService _photoResizeService;
	readonly NativePickedFileStore _pickedFiles;
	readonly ILogger<ResizeNativeFileUploadEndpoint> _logger;

	public ResizeNativeFileUploadEndpoint(
		PhotoResizeService photoResizeService,
		NativePickedFileStore pickedFiles,
		ILogger<ResizeNativeFileUploadEndpoint> logger)
	{
		_photoResizeService = photoResizeService;
		_pickedFiles = pickedFiles;
		_logger = logger;
	}

	public string Route => "/api/resize-native-upload";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		if (!Guid.TryParse(request.QueryValue("sessionId"), out var sessionId) || sessionId == Guid.Empty ||
			!Guid.TryParse(request.QueryValue("fileId"), out var fileId) || fileId == Guid.Empty ||
			!int.TryParse(request.QueryValue("order"), out var order) ||
			!Guid.TryParse(request.QueryValue("nativeFileId"), out var nativeFileId) || nativeFileId == Guid.Empty)
		{
			return HttpResponseInfo.Json(new { ok = false, error = "invalid_upload" }, 400);
		}

		if (!_pickedFiles.TryGet(nativeFileId, out var selectedFile))
		{
			return HttpResponseInfo.Json(new { ok = false, error = "file_unavailable" }, 404);
		}

		if (selectedFile.Size <= 0)
		{
			return HttpResponseInfo.Json(PhotoResizeUploadResult.Failure("empty_file"));
		}

		if (selectedFile.Size > MaxFileBytes)
		{
			return HttpResponseInfo.Json(PhotoResizeUploadResult.Failure("file_too_large"));
		}

		try
		{
			var payload = await _pickedFiles.ReadAllBytesAsync(nativeFileId, cancellationToken).ConfigureAwait(false);
			if (payload is null)
			{
				return HttpResponseInfo.Json(new { ok = false, error = "file_unavailable" }, 404);
			}

			var result = await _photoResizeService.UploadFileAsync(
				sessionId,
				fileId,
				order,
				request.QueryValue("fileName") ?? selectedFile.Name,
				payload,
				cancellationToken).ConfigureAwait(false);
			return HttpResponseInfo.Json(result);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Native-selected photo upload request failed");
			return HttpResponseInfo.Json(new { ok = false, error = "upload_failed" }, 500);
		}
	}
}
