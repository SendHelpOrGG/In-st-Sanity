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
	public double Grain { get; init; }
	public double GrainSize { get; init; } = 35;
	public bool MonochromeGrain { get; init; }
	public double Fade { get; init; }
	public double Vignette { get; init; }

	public bool HasEdits =>
		Exposure != 0 || Brightness != 0 || Contrast != 0 || Gamma != 0 || Highlights != 0 || Shadows != 0 ||
		Temperature != 0 || Tint != 0 || Saturation != 0 || Vibrance != 0 || Sharpness != 0 ||
		Clarity != 0 || Texture != 0 || Blur != 0 || Denoise != 0 || Sepia != 0 || Invert != 0 ||
		Grain != 0 || Fade != 0 || Vignette != 0;
}

/// <summary>CPU-only BGRA pixel effects used by the local Windows photo-resize pipeline.</summary>
internal static class PhotoImageAdjustmentProcessor
{
	// Keeps the worst-case in-memory footprint bounded when optional filter buffers are needed.
	public const long MaxAdjustedPixelCount = 20_000_000;

	public static void Apply(byte[] pixels, int width, int height, PhotoImageAdjustments settings, byte[] seedBytes)
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
		if (settings.Sepia > 0 || settings.Invert > 0)
		{
			ApplyCreativeEffects(pixels, settings.Sepia, settings.Invert);
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

		if (settings.Fade > 0 || settings.Vignette > 0 || settings.Grain > 0)
		{
			ApplyFinishingEffects(pixels, width, height, settings, seedBytes);
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

	static void ApplyCreativeEffects(byte[] pixels, double sepiaAmount, double invertAmount)
	{
		var sepia = Math.Clamp(sepiaAmount / 100.0, 0, 1);
		var invert = Math.Clamp(invertAmount / 100.0, 0, 1);
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

			pixels[index] = ToByte(blue);
			pixels[index + 1] = ToByte(green);
			pixels[index + 2] = ToByte(red);
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

	static void ApplyFinishingEffects(byte[] pixels, int width, int height, PhotoImageAdjustments settings, byte[] seedBytes)
	{
		var fade = settings.Fade / 100.0;
		var vignette = settings.Vignette / 100.0;
		var grainAmount = settings.Grain / 100.0 * 31;
		var hasGrain = settings.Grain > 0;
		var grainCell = 1 + (int)Math.Round(Math.Pow(settings.GrainSize / 100.0, 2) * 11);
		var seed = CreateSeed(seedBytes);
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

				pixels[index] = ToByte((pixels[index] * vignetteFactor * fadeFactor + fadeLift + noiseBlue * grainAmount) / 255.0);
				pixels[index + 1] = ToByte((pixels[index + 1] * vignetteFactor * fadeFactor + fadeLift + noiseGreen * grainAmount) / 255.0);
				pixels[index + 2] = ToByte((pixels[index + 2] * vignetteFactor * fadeFactor + fadeLift + noiseRed * grainAmount) / 255.0);
			}
		}
	}

	static double GrainNoise(int x, int y, int cell, uint seed, uint channel)
	{
		var cellX = x / cell;
		var cellY = y / cell;
		var fx = (x % cell) / (double)cell;
		var fy = (y % cell) / (double)cell;
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
