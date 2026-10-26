using System.Text.Json;
using InStSanity.Services.Images;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary>Converts one staged resize session after the user confirms the chosen maximum side.</summary>
public sealed class ResizeConvertEndpoint : IApiEndpoint
{
	readonly PhotoResizeService _photoResizeService;
	readonly ILogger<ResizeConvertEndpoint> _logger;

	public ResizeConvertEndpoint(
		PhotoResizeService photoResizeService,
		ILogger<ResizeConvertEndpoint> logger)
	{
		_photoResizeService = photoResizeService;
		_logger = logger;
	}

	public string Route => "/api/resize-convert";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		ResizeRequest? resizeRequest;
		try
		{
			resizeRequest = ParseRequest(request.Body);
		}
		catch (JsonException)
		{
			return HttpResponseInfo.Json(PhotoResizeConversionResult.Failure("invalid_request"), 400);
		}

		if (resizeRequest is null)
		{
			return HttpResponseInfo.Json(PhotoResizeConversionResult.Failure("invalid_request"), 400);
		}

		var validatedRequest = resizeRequest.Value;
		try
		{
			var result = await _photoResizeService.ConvertAsync(
				validatedRequest.SessionId,
				validatedRequest.MaxSide,
				validatedRequest.Upscale,
				validatedRequest.OutputFolder,
				cancellationToken).ConfigureAwait(false);
			return HttpResponseInfo.Json(result);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Photo resize conversion failed");
			return HttpResponseInfo.Json(PhotoResizeConversionResult.Failure("conversion_failed"), 500);
		}
	}

	static ResizeRequest? ParseRequest(byte[] body)
	{
		using var document = JsonDocument.Parse(body);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object ||
			!TryGetProperty(root, "sessionId", out var sessionValue) ||
			sessionValue.ValueKind != JsonValueKind.String ||
			!Guid.TryParse(sessionValue.GetString(), out var sessionId) || sessionId == Guid.Empty ||
			!TryGetProperty(root, "maxSide", out var maxSideValue) || !maxSideValue.TryGetInt32(out var maxSide))
		{
			return null;
		}

		var upscale = true;
		if (TryGetProperty(root, "upscale", out var upscaleValue))
		{
			if (upscaleValue.ValueKind == JsonValueKind.True)
			{
				upscale = true;
			}
			else if (upscaleValue.ValueKind == JsonValueKind.False)
			{
				upscale = false;
			}
			else
			{
				return null;
			}
		}

		var outputFolder = TryGetProperty(root, "outputFolder", out var folderValue) && folderValue.ValueKind == JsonValueKind.String
			? folderValue.GetString()
			: null;
		return new ResizeRequest(sessionId, maxSide, upscale, outputFolder);
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

	readonly record struct ResizeRequest(Guid SessionId, int MaxSide, bool Upscale, string? OutputFolder);
}
