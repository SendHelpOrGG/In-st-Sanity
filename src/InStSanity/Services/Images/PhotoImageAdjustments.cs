namespace InStSanity.Services.Images;

/// <summary>Non-destructive adjustments applied while producing resized image copies.</summary>
public sealed record PhotoImageAdjustments
{
	public double Exposure { get; init; }
	public double Brightness { get; init; }
	public double Contrast { get; init; }
	public double Gamma { get; init; }
	public double Highlights { get; init; }
	public double Shadows { get; init; }
	public double Temperature { get; init; }
	public double Tint { get; init; }
	public double Saturation { get; init; }
	public double Vibrance { get; init; }
	public double Sharpness { get; init; }
	public double Clarity { get; init; }
	public double Texture { get; init; }
	public double Blur { get; init; }
	public double Denoise { get; init; }
	public double Sepia { get; init; }
	public double Invert { get; init; }
	public double Posterize { get; init; }
	public double Pixelate { get; init; }
	public double Duotone { get; init; }
	public string DuotoneShadowColor { get; init; } = "#1B1B3A";
	public string DuotoneHighlightColor { get; init; } = "#FFD166";
	public double Halftone { get; init; }
	public double HalftoneSize { get; init; } = 12;
	public double Colorize { get; init; }
	public string ColorizeColor { get; init; } = "#D94F70";
	public double Grain { get; init; }
	public double GrainSize { get; init; } = 35;
	public bool MonochromeGrain { get; init; }
	public double Fade { get; init; }
	public double Vignette { get; init; }
	public double OldPhoto { get; init; }
	public int OldPhotoSeed { get; init; } = 1;

	public bool HasEdits =>
		Exposure != 0 || Brightness != 0 || Contrast != 0 || Gamma != 0 || Highlights != 0 || Shadows != 0 ||
		Temperature != 0 || Tint != 0 || Saturation != 0 || Vibrance != 0 || Sharpness != 0 ||
		Clarity != 0 || Texture != 0 || Blur != 0 || Denoise != 0 || Sepia != 0 || Invert != 0 ||
		Posterize != 0 || Pixelate != 0 || Duotone != 0 || Halftone != 0 || Colorize != 0 ||
		Grain != 0 || Fade != 0 || Vignette != 0 || OldPhoto != 0;
}

internal sealed record PhotoImageDamageTexture(int Width, int Height, byte[] Luminance);

/// <summary>CPU-only BGRA pixel effects used by the local Windows photo-resize pipeline.</summary>
internal static class PhotoImageAdjustmentProcessor
{
	// Keeps the worst-case in-memory footprint bounded when optional filter buffers are needed.
	public const long MaxAdjustedPixelCount = 20_000_000;

	public static void Apply(byte[] pixels, int width, int height, PhotoImageAdjustments settings, byte[] seedBytes, PhotoImageDamageTexture? oldPhotoTexture = null)
	{
		if (!settings.HasEdits)
		{
			return;
		}

		if (width <= 0 || height <= 0 || (long)width * height > MaxAdjustedPixelCount || pixels.Length != checked(width * height * 4))
		{
			throw new PhotoImageTooLargeException();
		}

		if (settings.Denoise > 0)
		{
			ApplyDenoise(pixels, width, height, settings.Denoise);
		}

		ApplyToneAndColor(pixels, width, height, settings);
		if (settings.Sepia > 0 || settings.Invert > 0 || settings.Colorize > 0 || settings.Duotone > 0)
		{
			ApplyCreativeEffects(pixels, settings);
		}

		if (settings.Sharpness > 0)
		{
			ApplyUnsharp(pixels, width, height, 1, settings.Sharpness / 100.0 * 1.8, false);
		}

		if (settings.Clarity != 0)
		{
			var radius = Math.Clamp(Math.Min(width, height) / 100, 2, 10);
			ApplyUnsharp(pixels, width, height, radius, settings.Clarity / 100.0 * 1.15, midtoneMask: true);
		}

		if (settings.Texture != 0)
		{
			ApplyUnsharp(pixels, width, height, 2, settings.Texture / 100.0 * 1.35, false);
		}

		if (settings.Blur > 0)
		{
			var radius = 1 + (int)Math.Round(settings.Blur / 100.0 * 11);
			ApplyBlur(pixels, width, height, radius, settings.Blur / 100.0);
		}

		if (settings.Fade > 0 || settings.Vignette > 0 || settings.Grain > 0 || settings.OldPhoto > 0)
		{
			ApplyFinishingEffects(pixels, width, height, settings, seedBytes, oldPhotoTexture);
		}

		if (settings.Posterize > 0)
		{
			ApplyPosterize(pixels, settings.Posterize);
		}

		if (settings.Halftone > 0)
		{
			ApplyHalftone(pixels, width, height, settings.Halftone, settings.HalftoneSize);
		}

		if (settings.Pixelate > 0)
		{
			ApplyPixelate(pixels, width, height, settings.Pixelate);
		}
	}

	static void ApplyToneAndColor(byte[] pixels, int width, int height, PhotoImageAdjustments settings)
	{
		if (settings.Exposure == 0 && settings.Brightness == 0 && settings.Contrast == 0 && settings.Gamma == 0 &&
			settings.Highlights == 0 && settings.Shadows == 0 && settings.Temperature == 0 &&
			settings.Tint == 0 && settings.Saturation == 0 && settings.Vibrance == 0)
		{
			return;
		}

		var exposure = Math.Pow(2, settings.Exposure / 50.0);
		var brightness = settings.Brightness / 100.0 * 0.35;
		var contrast = 1 + settings.Contrast / 100.0;
		var gammaExponent = Math.Pow(2, -settings.Gamma / 100.0 * 1.2);
		var shadows = settings.Shadows / 100.0 * 0.34;
		var highlights = settings.Highlights / 100.0 * 0.34;
		var temperature = settings.Temperature / 100.0 * 0.18;
		var tint = settings.Tint / 100.0 * 0.12;
		var saturation = 1 + settings.Saturation / 100.0;
		var vibrance = settings.Vibrance / 100.0;

		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var index = (y * width + x) * 4;
				if (pixels[index + 3] == 0)
				{
					continue;
				}

				var blue = pixels[index] / 255.0;
				var green = pixels[index + 1] / 255.0;
				var red = pixels[index + 2] / 255.0;

				red = (red * exposure + brightness - 0.5) * contrast + 0.5;
				green = (green * exposure + brightness - 0.5) * contrast + 0.5;
				blue = (blue * exposure + brightness - 0.5) * contrast + 0.5;

				var luminance = Luminance(red, green, blue);
				var shadowMask = (1 - luminance) * (1 - luminance);
				var highlightMask = luminance * luminance;
				var tonalShift = shadows * shadowMask + highlights * highlightMask;
				red += tonalShift;
				green += tonalShift;
				blue += tonalShift;

				if (settings.Gamma != 0)
				{
					red = Math.Pow(Math.Clamp(red, 0, 1), gammaExponent);
					green = Math.Pow(Math.Clamp(green, 0, 1), gammaExponent);
					blue = Math.Pow(Math.Clamp(blue, 0, 1), gammaExponent);
				}

				red += temperature + tint;
				green -= tint * 1.45;
				blue -= temperature - tint;

				luminance = Luminance(red, green, blue);
				var maximum = Math.Max(red, Math.Max(green, blue));
				var minimum = Math.Min(red, Math.Min(green, blue));
				var vibranceFactor = 1 + vibrance * (1 - Math.Clamp(maximum - minimum, 0, 1));
				var saturationFactor = saturation * vibranceFactor;
				red = luminance + (red - luminance) * saturationFactor;
				green = luminance + (green - luminance) * saturationFactor;
				blue = luminance + (blue - luminance) * saturationFactor;

				pixels[index] = ToByte(blue);
				pixels[index + 1] = ToByte(green);
				pixels[index + 2] = ToByte(red);
			}
		}
	}

	static void ApplyCreativeEffects(byte[] pixels, PhotoImageAdjustments settings)
	{
		var sepia = Math.Clamp(settings.Sepia / 100.0, 0, 1);
		var invert = Math.Clamp(settings.Invert / 100.0, 0, 1);
		var colorize = Math.Clamp(settings.Colorize / 100.0, 0, 1);
		var duotone = Math.Clamp(settings.Duotone / 100.0, 0, 1);
		var tintColor = ParseHexColor(settings.ColorizeColor, "#D94F70");
		var shadowColor = ParseHexColor(settings.DuotoneShadowColor, "#1B1B3A");
		var highlightColor = ParseHexColor(settings.DuotoneHighlightColor, "#FFD166");

		for (var index = 0; index < pixels.Length; index += 4)
		{
			if (pixels[index + 3] == 0)
			{
				continue;
			}

			var blue = pixels[index] / 255.0;
			var green = pixels[index + 1] / 255.0;
			var red = pixels[index + 2] / 255.0;
			if (sepia > 0)
			{
				var sepiaRed = Math.Clamp(red * 0.393 + green * 0.769 + blue * 0.189, 0, 1);
				var sepiaGreen = Math.Clamp(red * 0.349 + green * 0.686 + blue * 0.168, 0, 1);
				var sepiaBlue = Math.Clamp(red * 0.272 + green * 0.534 + blue * 0.131, 0, 1);
				red = red * (1 - sepia) + sepiaRed * sepia;
				green = green * (1 - sepia) + sepiaGreen * sepia;
				blue = blue * (1 - sepia) + sepiaBlue * sepia;
			}

			if (invert > 0)
			{
				red = red * (1 - invert) + (1 - red) * invert;
				green = green * (1 - invert) + (1 - green) * invert;
				blue = blue * (1 - invert) + (1 - blue) * invert;
			}

			if (colorize > 0)
			{
				var luminance = Luminance(red, green, blue);
				red = red * (1 - colorize) + luminance * tintColor.Red * colorize;
				green = green * (1 - colorize) + luminance * tintColor.Green * colorize;
				blue = blue * (1 - colorize) + luminance * tintColor.Blue * colorize;
			}

			if (duotone > 0)
			{
				var luminance = Math.Clamp(Luminance(red, green, blue), 0, 1);
				var toneRed = shadowColor.Red + (highlightColor.Red - shadowColor.Red) * luminance;
				var toneGreen = shadowColor.Green + (highlightColor.Green - shadowColor.Green) * luminance;
				var toneBlue = shadowColor.Blue + (highlightColor.Blue - shadowColor.Blue) * luminance;
				red = red * (1 - duotone) + toneRed * duotone;
				green = green * (1 - duotone) + toneGreen * duotone;
				blue = blue * (1 - duotone) + toneBlue * duotone;
			}

			pixels[index] = ToByte(blue);
			pixels[index + 1] = ToByte(green);
			pixels[index + 2] = ToByte(red);
		}
	}

	static (double Red, double Green, double Blue) ParseHexColor(string? value, string fallback)
	{
		var color = IsHexColor(value) ? value! : fallback;
		return (
			Convert.ToInt32(color.Substring(1, 2), 16) / 255.0,
			Convert.ToInt32(color.Substring(3, 2), 16) / 255.0,
			Convert.ToInt32(color.Substring(5, 2), 16) / 255.0);
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

	static void ApplyPosterize(byte[] pixels, double amount)
	{
		var strength = Math.Clamp(amount / 100.0, 0, 1);
		if (strength <= 0)
		{
			return;
		}

		var levels = Math.Max(2, 32 - (int)Math.Round(strength * 30, MidpointRounding.AwayFromZero));
		var step = 255.0 / (levels - 1);
		for (var index = 0; index < pixels.Length; index += 4)
		{
			if (pixels[index + 3] == 0)
			{
				continue;
			}

			for (var channel = 0; channel < 3; channel++)
			{
				pixels[index + channel] = (byte)Math.Clamp(
					Math.Round(Math.Round(pixels[index + channel] / step, MidpointRounding.AwayFromZero) * step, MidpointRounding.AwayFromZero),
					0,
					255);
			}
		}
	}

	static void ApplyHalftone(byte[] pixels, int width, int height, double amount, double requestedCellSize)
	{
		var strength = Math.Clamp(amount / 100.0, 0, 1);
		var cellSize = Math.Clamp((int)Math.Round(requestedCellSize, MidpointRounding.AwayFromZero), 4, 40);
		if (strength <= 0)
		{
			return;
		}

		for (var top = 0; top < height; top += cellSize)
		{
			var bottom = Math.Min(height, top + cellSize);
			for (var left = 0; left < width; left += cellSize)
			{
				var right = Math.Min(width, left + cellSize);
				var blueTotal = 0.0;
				var greenTotal = 0.0;
				var redTotal = 0.0;
				var luminanceTotal = 0.0;
				var count = 0;
				for (var y = top; y < bottom; y++)
				{
					for (var x = left; x < right; x++)
					{
						var index = (y * width + x) * 4;
						if (pixels[index + 3] == 0)
						{
							continue;
						}

						var blue = pixels[index];
						var green = pixels[index + 1];
						var red = pixels[index + 2];
						blueTotal += blue;
						greenTotal += green;
						redTotal += red;
						luminanceTotal += Luminance(red / 255.0, green / 255.0, blue / 255.0);
						count++;
					}
				}

				if (count == 0)
				{
					continue;
				}

				var averageBlue = blueTotal / count;
				var averageGreen = greenTotal / count;
				var averageRed = redTotal / count;
				var radius = cellSize * 0.7071067811865476 * Math.Sqrt(1 - luminanceTotal / count);
				var centerX = (left + right - 1) / 2.0;
				var centerY = (top + bottom - 1) / 2.0;
				var radiusSquared = radius * radius;
				for (var y = top; y < bottom; y++)
				{
					for (var x = left; x < right; x++)
					{
						var index = (y * width + x) * 4;
						if (pixels[index + 3] == 0)
						{
							continue;
						}

						var offsetX = x - centerX;
						var offsetY = y - centerY;
						var insideDot = offsetX * offsetX + offsetY * offsetY <= radiusSquared;
						var targetBlue = insideDot ? averageBlue : 255;
						var targetGreen = insideDot ? averageGreen : 255;
						var targetRed = insideDot ? averageRed : 255;
						pixels[index] = (byte)Math.Clamp(Math.Round(pixels[index] * (1 - strength) + targetBlue * strength, MidpointRounding.AwayFromZero), 0, 255);
						pixels[index + 1] = (byte)Math.Clamp(Math.Round(pixels[index + 1] * (1 - strength) + targetGreen * strength, MidpointRounding.AwayFromZero), 0, 255);
						pixels[index + 2] = (byte)Math.Clamp(Math.Round(pixels[index + 2] * (1 - strength) + targetRed * strength, MidpointRounding.AwayFromZero), 0, 255);
					}
				}
			}
		}
	}

	static void ApplyPixelate(byte[] pixels, int width, int height, double amount)
	{
		var strength = Math.Clamp(amount / 100.0, 0, 1);
		if (strength <= 0)
		{
			return;
		}

		var maximumCellSize = Math.Clamp((int)Math.Round(Math.Min(width, height) * 0.12, MidpointRounding.AwayFromZero), 2, 96);
		var cellSize = 2 + (int)Math.Round((maximumCellSize - 2) * strength);
		for (var top = 0; top < height; top += cellSize)
		{
			var bottom = Math.Min(height, top + cellSize);
			for (var left = 0; left < width; left += cellSize)
			{
				var right = Math.Min(width, left + cellSize);
				var blueTotal = 0;
				var greenTotal = 0;
				var redTotal = 0;
				var count = 0;
				for (var y = top; y < bottom; y++)
				{
					for (var x = left; x < right; x++)
					{
						var index = (y * width + x) * 4;
						if (pixels[index + 3] == 0)
						{
							continue;
						}

						blueTotal += pixels[index];
						greenTotal += pixels[index + 1];
						redTotal += pixels[index + 2];
						count++;
					}
				}

				if (count == 0)
				{
					continue;
				}

				var averageBlue = blueTotal / (double)count;
				var averageGreen = greenTotal / (double)count;
				var averageRed = redTotal / (double)count;
				for (var y = top; y < bottom; y++)
				{
					for (var x = left; x < right; x++)
					{
						var index = (y * width + x) * 4;
						if (pixels[index + 3] == 0)
						{
							continue;
						}

						pixels[index] = (byte)Math.Clamp(Math.Round(averageBlue, MidpointRounding.AwayFromZero), 0, 255);
						pixels[index + 1] = (byte)Math.Clamp(Math.Round(averageGreen, MidpointRounding.AwayFromZero), 0, 255);
						pixels[index + 2] = (byte)Math.Clamp(Math.Round(averageRed, MidpointRounding.AwayFromZero), 0, 255);
					}
				}
			}
		}
	}

	static void ApplyDenoise(byte[] pixels, int width, int height, double amount)
	{
		var original = (byte[])pixels.Clone();
		var blurred = BoxBlur(original, width, height, 1);
		var strength = amount / 100.0 * 0.78;
		for (var i = 0; i < pixels.Length; i += 4)
		{
			if (original[i + 3] == 0)
			{
				continue;
			}

			var originalLuma = Luminance(original[i + 2] / 255.0, original[i + 1] / 255.0, original[i] / 255.0);
			var blurLuma = Luminance(blurred[i + 2] / 255.0, blurred[i + 1] / 255.0, blurred[i] / 255.0);
			var edgeProtection = 1 / (1 + Math.Abs(originalLuma - blurLuma) * 10);
			var blend = strength * edgeProtection;
			for (var channel = 0; channel < 3; channel++)
			{
				pixels[i + channel] = (byte)Math.Clamp(Math.Round(original[i + channel] * (1 - blend) + blurred[i + channel] * blend), 0, 255);
			}
		}
	}

	static void ApplyUnsharp(byte[] pixels, int width, int height, int radius, double amount, bool midtoneMask)
	{
		if (amount == 0)
		{
			return;
		}

		var original = (byte[])pixels.Clone();
		var blurred = BoxBlur(original, width, height, radius);
		for (var i = 0; i < pixels.Length; i += 4)
		{
			if (original[i + 3] == 0)
			{
				continue;
			}

			var mask = 1.0;
			if (midtoneMask)
			{
				var luma = Luminance(original[i + 2] / 255.0, original[i + 1] / 255.0, original[i] / 255.0);
				mask = 4 * luma * (1 - luma);
			}

			for (var channel = 0; channel < 3; channel++)
			{
				var detail = original[i + channel] - blurred[i + channel];
				pixels[i + channel] = (byte)Math.Clamp(Math.Round(original[i + channel] + detail * amount * mask), 0, 255);
			}
		}
	}

	static void ApplyBlur(byte[] pixels, int width, int height, int radius, double amount)
	{
		var original = (byte[])pixels.Clone();
		var blurred = BoxBlur(original, width, height, radius);
		for (var i = 0; i < pixels.Length; i += 4)
		{
			for (var channel = 0; channel < 3; channel++)
			{
				pixels[i + channel] = (byte)Math.Clamp(Math.Round(original[i + channel] * (1 - amount) + blurred[i + channel] * amount), 0, 255);
			}
		}
	}

	static byte[] BoxBlur(byte[] source, int width, int height, int radius)
	{
		radius = Math.Clamp(radius, 1, 12);
		var horizontal = new byte[source.Length];
		var output = new byte[source.Length];
		var window = radius * 2 + 1;

		for (var y = 0; y < height; y++)
		{
			var row = y * width;
			var blue = 0;
			var green = 0;
			var red = 0;
			for (var offset = -radius; offset <= radius; offset++)
			{
				var pixel = (row + Math.Clamp(offset, 0, width - 1)) * 4;
				blue += source[pixel];
				green += source[pixel + 1];
				red += source[pixel + 2];
			}

			for (var x = 0; x < width; x++)
			{
				var pixel = (row + x) * 4;
				horizontal[pixel] = (byte)(blue / window);
				horizontal[pixel + 1] = (byte)(green / window);
				horizontal[pixel + 2] = (byte)(red / window);
				horizontal[pixel + 3] = source[pixel + 3];
				if (x + 1 < width)
				{
					var remove = (row + Math.Clamp(x - radius, 0, width - 1)) * 4;
					var add = (row + Math.Clamp(x + radius + 1, 0, width - 1)) * 4;
					blue += source[add] - source[remove];
					green += source[add + 1] - source[remove + 1];
					red += source[add + 2] - source[remove + 2];
				}
			}
		}

		for (var x = 0; x < width; x++)
		{
			var blue = 0;
			var green = 0;
			var red = 0;
			for (var offset = -radius; offset <= radius; offset++)
			{
				var pixel = (Math.Clamp(offset, 0, height - 1) * width + x) * 4;
				blue += horizontal[pixel];
				green += horizontal[pixel + 1];
				red += horizontal[pixel + 2];
			}

			for (var y = 0; y < height; y++)
			{
				var pixel = (y * width + x) * 4;
				output[pixel] = (byte)(blue / window);
				output[pixel + 1] = (byte)(green / window);
				output[pixel + 2] = (byte)(red / window);
				output[pixel + 3] = source[pixel + 3];
				if (y + 1 < height)
				{
					var remove = (Math.Clamp(y - radius, 0, height - 1) * width + x) * 4;
					var add = (Math.Clamp(y + radius + 1, 0, height - 1) * width + x) * 4;
					blue += horizontal[add] - horizontal[remove];
					green += horizontal[add + 1] - horizontal[remove + 1];
					red += horizontal[add + 2] - horizontal[remove + 2];
				}
			}
		}

		return output;
	}

	static void ApplyFinishingEffects(
		byte[] pixels,
		int width,
		int height,
		PhotoImageAdjustments settings,
		byte[] seedBytes,
		PhotoImageDamageTexture? oldPhotoTexture)
	{
		var fade = settings.Fade / 100.0;
		var vignette = settings.Vignette / 100.0;
		var oldPhoto = settings.OldPhoto / 100.0;
		var grainAmount = settings.Grain / 100.0 * 31;
		var hasGrain = settings.Grain > 0;
		var grainCell = 0.25 + Math.Pow(settings.GrainSize / 100.0, 2) * 11.75;
		var grainScale = Math.Min(1, grainCell);
		var seed = CreateSeed(seedBytes);
		var textureMapping = oldPhotoTexture is { Width: > 0, Height: > 0 }
			? CreateTextureMapping(oldPhotoTexture, width, height, settings.OldPhotoSeed)
			: default;
		var halfWidth = Math.Max(1, width / 2.0);
		var halfHeight = Math.Max(1, height / 2.0);

		for (var y = 0; y < height; y++)
		{
			var normalizedY = (y + 0.5 - halfHeight) / halfHeight;
			for (var x = 0; x < width; x++)
			{
				var index = (y * width + x) * 4;
				if (pixels[index + 3] == 0)
				{
					continue;
				}

				var normalizedX = (x + 0.5 - halfWidth) / halfWidth;
				var radius = Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
				var vignetteMask = SmoothStep(0.42, 1.12, radius);
				var vignetteFactor = 1 - vignette * vignetteMask * 0.72;
				var fadeFactor = 1 - fade * 0.18;
				var fadeLift = fade * 0.18 * 255;
				var noiseBlue = hasGrain ? GrainNoise(x, y, grainCell, seed, 0) : 0;
				var noiseGreen = !hasGrain || settings.MonochromeGrain ? noiseBlue : GrainNoise(x, y, grainCell, seed, 1);
				var noiseRed = !hasGrain || settings.MonochromeGrain ? noiseBlue : GrainNoise(x, y, grainCell, seed, 2);
				var wearDelta = oldPhoto > 0 && oldPhotoTexture is { Width: > 0, Height: > 0 }
					? (SampleTextureLuminance(oldPhotoTexture, textureMapping, x, y, width, height) - 128) * oldPhoto * 0.62
					: 0;

				pixels[index] = ToByte((pixels[index] * vignetteFactor * fadeFactor + fadeLift + noiseBlue * grainAmount * grainScale + wearDelta) / 255.0);
				pixels[index + 1] = ToByte((pixels[index + 1] * vignetteFactor * fadeFactor + fadeLift + noiseGreen * grainAmount * grainScale + wearDelta) / 255.0);
				pixels[index + 2] = ToByte((pixels[index + 2] * vignetteFactor * fadeFactor + fadeLift + noiseRed * grainAmount * grainScale + wearDelta) / 255.0);
			}
		}
	}

	static (double UStart, double USpan, double VStart, double VSpan, bool FlipX, bool FlipY) CreateTextureMapping(
		PhotoImageDamageTexture texture,
		int width,
		int height,
		int seedValue)
	{
		var imageAspect = width / (double)height;
		var textureAspect = texture.Width / (double)texture.Height;
		var uSpan = 1.0;
		var vSpan = 1.0;
		if (imageAspect < textureAspect)
		{
			uSpan = imageAspect / textureAspect;
		}
		else if (imageAspect > textureAspect)
		{
			vSpan = textureAspect / imageAspect;
		}

		var randomState = (uint)seedValue;
		var zoom = 0.82 + NextTextureSeedRandom(ref randomState) * 0.18;
		uSpan *= zoom;
		vSpan *= zoom;
		var offsetU = NextTextureSeedRandom(ref randomState);
		var offsetV = NextTextureSeedRandom(ref randomState);
		var flipX = NextTextureSeedRandom(ref randomState) < 0.5;
		var flipY = NextTextureSeedRandom(ref randomState) < 0.5;
		return ((1 - uSpan) * offsetU, uSpan, (1 - vSpan) * offsetV, vSpan, flipX, flipY);
	}

	static double NextTextureSeedRandom(ref uint state)
	{
		state = unchecked(state * 1_664_525u + 1_013_904_223u);
		return state / 4_294_967_296.0;
	}

	static double SampleTextureLuminance(
		PhotoImageDamageTexture texture,
		(double UStart, double USpan, double VStart, double VSpan, bool FlipX, bool FlipY) mapping,
		int x,
		int y,
		int width,
		int height)
	{
		var normalizedU = (x + 0.5) / width;
		var normalizedV = (y + 0.5) / height;
		if (mapping.FlipX)
		{
			normalizedU = 1 - normalizedU;
		}

		if (mapping.FlipY)
		{
			normalizedV = 1 - normalizedV;
		}
		var u = mapping.UStart + normalizedU * mapping.USpan;
		var v = mapping.VStart + normalizedV * mapping.VSpan;
		var sampleX = Math.Clamp(u * (texture.Width - 1), 0, texture.Width - 1.0);
		var sampleY = Math.Clamp(v * (texture.Height - 1), 0, texture.Height - 1.0);
		var x0 = (int)Math.Floor(sampleX);
		var y0 = (int)Math.Floor(sampleY);
		var x1 = Math.Min(texture.Width - 1, x0 + 1);
		var y1 = Math.Min(texture.Height - 1, y0 + 1);
		var fx = sampleX - x0;
		var fy = sampleY - y0;
		var topLeft = texture.Luminance[y0 * texture.Width + x0];
		var topRight = texture.Luminance[y0 * texture.Width + x1];
		var bottomLeft = texture.Luminance[y1 * texture.Width + x0];
		var bottomRight = texture.Luminance[y1 * texture.Width + x1];
		var top = topLeft + (topRight - topLeft) * fx;
		var bottom = bottomLeft + (bottomRight - bottomLeft) * fx;
		return top + (bottom - top) * fy;
	}

	static double GrainNoise(int x, int y, double cell, uint seed, uint channel)
	{
		var scaledX = x / cell;
		var scaledY = y / cell;
		var cellX = (int)Math.Floor(scaledX);
		var cellY = (int)Math.Floor(scaledY);
		var fx = scaledX - cellX;
		var fy = scaledY - cellY;
		var topLeft = HashNoise(cellX, cellY, seed, channel);
		var topRight = HashNoise(cellX + 1, cellY, seed, channel);
		var bottomLeft = HashNoise(cellX, cellY + 1, seed, channel);
		var bottomRight = HashNoise(cellX + 1, cellY + 1, seed, channel);
		var top = topLeft + (topRight - topLeft) * fx;
		var bottom = bottomLeft + (bottomRight - bottomLeft) * fx;
		return top + (bottom - top) * fy;
	}

	static double HashNoise(int x, int y, uint seed, uint channel)
	{
		var value = unchecked(seed ^ ((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu) ^ (channel * 0xC2B2AE35u));
		value ^= value >> 16;
		value = unchecked(value * 0x7FEB352Du);
		value ^= value >> 15;
		value = unchecked(value * 0x846CA68Bu);
		value ^= value >> 16;
		return value / (double)uint.MaxValue * 2 - 1;
	}

	static uint CreateSeed(byte[] bytes)
	{
		var seed = 2166136261u;
		var step = Math.Max(1, bytes.Length / 2048);
		for (var index = 0; index < bytes.Length; index += step)
		{
			seed = unchecked((seed ^ bytes[index]) * 16777619u);
		}

		return seed == 0 ? 1u : seed;
	}

	static double Luminance(double red, double green, double blue) => 0.2126 * red + 0.7152 * green + 0.0722 * blue;
	static double SmoothStep(double edge0, double edge1, double value)
	{
		var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
		return t * t * (3 - 2 * t);
	}
	static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}

internal sealed class PhotoImageTooLargeException : Exception
{
	public PhotoImageTooLargeException() : base("The selected longest-side size is too large to apply adjustments safely.") { }
}
