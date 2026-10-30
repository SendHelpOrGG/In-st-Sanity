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
				validatedRequest.NoDuplicates,
				validatedRequest.ApplyAdjustmentsToBatch,
				validatedRequest.OutputFolder,
				validatedRequest.Adjustments,
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

		var noDuplicates = true;
		if (TryGetProperty(root, "noDuplicates", out var noDuplicatesValue))
		{
			if (noDuplicatesValue.ValueKind == JsonValueKind.True)
			{
				noDuplicates = true;
			}
			else if (noDuplicatesValue.ValueKind == JsonValueKind.False)
			{
				noDuplicates = false;
			}
			else
			{
				return null;
			}
		}

		var applyAdjustmentsToBatch = true;
		if (TryGetProperty(root, "applyAdjustmentsToBatch", out var applyToBatchValue))
		{
			if (applyToBatchValue.ValueKind == JsonValueKind.True)
			{
				applyAdjustmentsToBatch = true;
			}
			else if (applyToBatchValue.ValueKind == JsonValueKind.False)
			{
				applyAdjustmentsToBatch = false;
			}
			else
			{
				return null;
			}
		}

		var outputFolder = TryGetProperty(root, "outputFolder", out var folderValue) && folderValue.ValueKind == JsonValueKind.String
			? folderValue.GetString()
			: null;
		var adjustments = new PhotoImageAdjustments();
		if (TryGetProperty(root, "adjustments", out var adjustmentsValue) &&
			(adjustmentsValue.ValueKind != JsonValueKind.Object || !TryParseAdjustments(adjustmentsValue, out adjustments)))
		{
			return null;
		}

		return new ResizeRequest(sessionId, maxSide, upscale, noDuplicates, applyAdjustmentsToBatch, outputFolder, adjustments);
	}

	static bool TryParseAdjustments(JsonElement element, out PhotoImageAdjustments adjustments)
	{
		adjustments = new PhotoImageAdjustments();
		if (!TryReadNumber(element, "exposure", -100, 100, 0, out var exposure) ||
			!TryReadNumber(element, "brightness", -100, 100, 0, out var brightness) ||
			!TryReadNumber(element, "contrast", -100, 100, 0, out var contrast) ||
			!TryReadNumber(element, "gamma", -100, 100, 0, out var gamma) ||
			!TryReadNumber(element, "highlights", -100, 100, 0, out var highlights) ||
			!TryReadNumber(element, "shadows", -100, 100, 0, out var shadows) ||
			!TryReadNumber(element, "temperature", -100, 100, 0, out var temperature) ||
			!TryReadNumber(element, "tint", -100, 100, 0, out var tint) ||
			!TryReadNumber(element, "saturation", -100, 100, 0, out var saturation) ||
			!TryReadNumber(element, "vibrance", -100, 100, 0, out var vibrance) ||
			!TryReadNumber(element, "sharpness", 0, 100, 0, out var sharpness) ||
			!TryReadNumber(element, "clarity", -100, 100, 0, out var clarity) ||
			!TryReadNumber(element, "texture", -100, 100, 0, out var texture) ||
			!TryReadNumber(element, "blur", 0, 100, 0, out var blur) ||
			!TryReadNumber(element, "denoise", 0, 100, 0, out var denoise) ||
			!TryReadNumber(element, "sepia", 0, 100, 0, out var sepia) ||
			!TryReadNumber(element, "invert", 0, 100, 0, out var invert) ||
			!TryReadNumber(element, "posterize", 0, 100, 0, out var posterize) ||
			!TryReadNumber(element, "pixelate", 0, 100, 0, out var pixelate) ||
			!TryReadNumber(element, "duotone", 0, 100, 0, out var duotone) ||
			!TryReadNumber(element, "halftone", 0, 100, 0, out var halftone) ||
			!TryReadNumber(element, "halftoneSize", 4, 40, 12, out var halftoneSize) ||
			!TryReadNumber(element, "colorize", 0, 100, 0, out var colorize) ||
			!TryReadNumber(element, "grain", 0, 100, 0, out var grain) ||
			!TryReadNumber(element, "grainSize", 0, 100, 35, out var grainSize) ||
			!TryReadNumber(element, "fade", 0, 100, 0, out var fade) ||
			!TryReadNumber(element, "vignette", 0, 100, 0, out var vignette) ||
			!TryReadNumber(element, "oldPhoto", 0, 100, 0, out var oldPhoto) ||
			!TryReadNumber(element, "oldPhotoSeed", 0, int.MaxValue, 1, out var oldPhotoSeed) ||
			oldPhotoSeed != Math.Truncate(oldPhotoSeed))
		{
			return false;
		}

		if (!TryReadColor(element, "duotoneShadowColor", "#1B1B3A", out var duotoneShadowColor) ||
			!TryReadColor(element, "duotoneHighlightColor", "#FFD166", out var duotoneHighlightColor) ||
			!TryReadColor(element, "colorizeColor", "#D94F70", out var colorizeColor))
		{
			return false;
		}

		var monochromeGrain = false;
		if (TryGetProperty(element, "monochromeGrain", out var monochromeValue))
		{
			if (monochromeValue.ValueKind == JsonValueKind.True)
			{
				monochromeGrain = true;
			}
			else if (monochromeValue.ValueKind != JsonValueKind.False)
			{
				return false;
			}
		}

		adjustments = new PhotoImageAdjustments
		{
			Exposure = exposure,
			Brightness = brightness,
			Contrast = contrast,
			Gamma = gamma,
			Highlights = highlights,
			Shadows = shadows,
			Temperature = temperature,
			Tint = tint,
			Saturation = saturation,
			Vibrance = vibrance,
			Sharpness = sharpness,
			Clarity = clarity,
			Texture = texture,
			Blur = blur,
			Denoise = denoise,
			Sepia = sepia,
			Invert = invert,
			Posterize = posterize,
			Pixelate = pixelate,
			Duotone = duotone,
			DuotoneShadowColor = duotoneShadowColor,
			DuotoneHighlightColor = duotoneHighlightColor,
			Halftone = halftone,
			HalftoneSize = halftoneSize,
			Colorize = colorize,
			ColorizeColor = colorizeColor,
			Grain = grain,
			GrainSize = grainSize,
			MonochromeGrain = monochromeGrain,
			Fade = fade,
			Vignette = vignette,
			OldPhoto = oldPhoto,
			OldPhotoSeed = (int)oldPhotoSeed,
		};
		return true;
	}

	static bool TryReadNumber(JsonElement element, string name, double minimum, double maximum, double defaultValue, out double result)
	{
		result = defaultValue;
		if (!TryGetProperty(element, name, out var value))
		{
			return true;
		}

		if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var parsed) ||
			!double.IsFinite(parsed) || parsed < minimum || parsed > maximum)
		{
			return false;
		}

		result = parsed;
		return true;
	}

	static bool TryReadColor(JsonElement element, string name, string defaultValue, out string result)
	{
		result = defaultValue;
		if (!TryGetProperty(element, name, out var value))
		{
			return true;
		}

		if (value.ValueKind != JsonValueKind.String || !IsHexColor(value.GetString()))
		{
			return false;
		}

		result = value.GetString()!.ToUpperInvariant();
		return true;
	}

	static bool IsHexColor(string? value)
	{
		if (value is null || value.Length != 7 || value[0] != '#')
		{
			return false;
		}

		for (var index = 1; index < value.Length; index++)
		{
			var character = value[index];
			if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
			{
				return false;
			}
		}

		return true;
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

	readonly record struct ResizeRequest(
		Guid SessionId,
		int MaxSide,
		bool Upscale,
		bool NoDuplicates,
		bool ApplyAdjustmentsToBatch,
		string? OutputFolder,
		PhotoImageAdjustments Adjustments);
}
