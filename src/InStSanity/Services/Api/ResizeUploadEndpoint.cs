using InStSanity.Services.Images;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary>POSTs one selected image at a time into an isolated local resize-upload session.</summary>
public sealed class ResizeUploadEndpoint : IApiEndpoint
{
	readonly PhotoResizeService _photoResizeService;
	readonly ILogger<ResizeUploadEndpoint> _logger;

	public ResizeUploadEndpoint(
		PhotoResizeService photoResizeService,
		ILogger<ResizeUploadEndpoint> logger)
	{
		_photoResizeService = photoResizeService;
		_logger = logger;
	}

	public string Route => "/api/resize-upload";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.QueryValue("sessionId"), out var sessionId) || sessionId == Guid.Empty)
		{
			return HttpResponseInfo.Json(new { ok = false, error = "invalid_session" }, 400);
		}

		if (request.Method == "DELETE")
		{
			await _photoResizeService.CancelSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
			return HttpResponseInfo.Json(new { ok = true });
		}

		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		if (!string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/octet-stream", StringComparison.OrdinalIgnoreCase))
		{
			return HttpResponseInfo.Json(new { ok = false, error = "invalid_content_type" }, 415);
		}

		if (!Guid.TryParse(request.QueryValue("fileId"), out var fileId) || fileId == Guid.Empty ||
			!int.TryParse(request.QueryValue("order"), out var order))
		{
			return HttpResponseInfo.Json(new { ok = false, error = "invalid_upload" }, 400);
		}

		try
		{
			var result = await _photoResizeService.UploadFileAsync(
				sessionId,
				fileId,
				order,
				request.QueryValue("fileName"),
				request.Body,
				cancellationToken).ConfigureAwait(false);
			return HttpResponseInfo.Json(result);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Photo upload request failed");
			return HttpResponseInfo.Json(new { ok = false, error = "upload_failed" }, 500);
		}
	}
}
