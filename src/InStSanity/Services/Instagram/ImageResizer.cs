using InStSanity.Services.Images;
using Microsoft.Maui.Storage;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace InStSanity.Services.Instagram;

/// <summary>
/// Scales a fetched image payload down for grid thumbnails, using the Windows imaging
/// stack (no extra packages). Returns null when no resize is needed or possible — the
/// caller then serves the original payload unchanged.
/// </summary>
internal static class ImageResizer
{
	const string OldPhotoTextureAssetPath = "wwwroot/old-photo-damage-texture.png";
	static readonly object OldPhotoTextureGate = new();
	static Task<PhotoImageDamageTexture?>? _oldPhotoTextureTask;

	public static async Task<byte[]?> TryResizeAsync(byte[] payload, int targetWidth)
	{
		if (payload.Length < 32 || targetWidth < 16)
		{
			return null;
		}

		try
		{
			using var inStream = new InMemoryRandomAccessStream();
			using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
			{
				writer.WriteBytes(payload);
				await writer.StoreAsync();
			}

			inStream.Seek(0);
			var decoder = await BitmapDecoder.CreateAsync(inStream);
			if (decoder.PixelWidth <= targetWidth)
			{
				return null; // already small enough
			}

			var scale = (double)targetWidth / decoder.PixelWidth;
			var targetHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
			var transform = new BitmapTransform
			{
				ScaledWidth = (uint)targetWidth,
				ScaledHeight = targetHeight,
				InterpolationMode = BitmapInterpolationMode.Fant,
			};

			using var software = await decoder.GetSoftwareBitmapAsync(
				BitmapPixelFormat.Bgra8,
				BitmapAlphaMode.Premultiplied,
				transform,
				ExifOrientationMode.RespectExifOrientation,
				ColorManagementMode.DoNotColorManage);

			// Keep PNG when the source is PNG (alpha); everything else becomes JPEG.
			var isPng = payload[1] == (byte)'P' && payload[2] == (byte)'N' && payload[3] == (byte)'G';
			using var outStream = new InMemoryRandomAccessStream();
			var encoder = await BitmapEncoder.CreateAsync(
				isPng ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId,
				outStream);
			encoder.SetSoftwareBitmap(software);
			await encoder.FlushAsync();

			outStream.Seek(0);
			using var reader = new DataReader(outStream.GetInputStreamAt(0));
			await reader.LoadAsync((uint)outStream.Size);
			var bytes = new byte[reader.UnconsumedBufferLength];
			reader.ReadBytes(bytes);
			return bytes.Length > 0 ? bytes : null;
		}
		catch
		{
			// Undecodable payload (or codec missing): the original bytes are still fine.
			return null;
		}
	}

	public static async Task<ImageResizeResult?> TryResizeToMaxSideAsync(
		byte[] payload,
		int maxSide,
		string originalExtension,
		bool upscale,
		PhotoImageAdjustments? requestedAdjustments = null)
	{
		if (payload.Length < 32 || maxSide < 16)
		{
			return null;
		}

		var adjustments = requestedAdjustments ?? new PhotoImageAdjustments();
		try
		{
			using var inStream = new InMemoryRandomAccessStream();
			using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
			{
				writer.WriteBytes(payload);
				await writer.StoreAsync();
			}

			inStream.Seek(0);
			var decoder = await BitmapDecoder.CreateAsync(inStream);
			var largestSide = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
			var targetSide = (uint)maxSide;
			var wasResized = largestSide > targetSide || (largestSide < targetSide && upscale);
			if (!wasResized && !adjustments.HasEdits)
			{
				return new ImageResizeResult(payload, originalExtension, WasResized: false, WasEdited: false);
			}

			var scale = wasResized ? (double)maxSide / largestSide : 1.0;
			var targetWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
			var targetHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
			var transform = new BitmapTransform
			{
				ScaledWidth = targetWidth,
				ScaledHeight = targetHeight,
				InterpolationMode = BitmapInterpolationMode.Fant,
			};

			var isPng = payload.Length >= 8 && payload[0] == 0x89 &&
				payload[1] == (byte)'P' && payload[2] == (byte)'N' && payload[3] == (byte)'G';
			using var outStream = new InMemoryRandomAccessStream();
			var encoder = await BitmapEncoder.CreateAsync(
				isPng ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId,
				outStream);

			if (adjustments.HasEdits)
			{
				var pixelCount = (long)targetWidth * targetHeight;
				if (pixelCount > PhotoImageAdjustmentProcessor.MaxAdjustedPixelCount)
				{
					throw new PhotoImageTooLargeException();
				}

				var pixelData = await decoder.GetPixelDataAsync(
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Straight,
					transform,
					ExifOrientationMode.RespectExifOrientation,
					ColorManagementMode.DoNotColorManage);
				var pixels = pixelData.DetachPixelData();
				var oldPhotoTexture = adjustments.OldPhoto > 0
					? await GetOldPhotoTextureAsync().ConfigureAwait(false)
					: null;
				PhotoImageAdjustmentProcessor.Apply(
					pixels,
					checked((int)targetWidth),
					checked((int)targetHeight),
					adjustments,
					payload,
					oldPhotoTexture);

				encoder.SetPixelData(
					BitmapPixelFormat.Bgra8,
					isPng ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
					targetWidth,
					targetHeight,
					Math.Max(1, decoder.DpiX),
					Math.Max(1, decoder.DpiY),
					pixels);
			}
			else
			{
				using var software = await decoder.GetSoftwareBitmapAsync(
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Premultiplied,
					transform,
					ExifOrientationMode.RespectExifOrientation,
					ColorManagementMode.DoNotColorManage);
				encoder.SetSoftwareBitmap(software);
			}

			await encoder.FlushAsync();
			outStream.Seek(0);
			using var reader = new DataReader(outStream.GetInputStreamAt(0));
			await reader.LoadAsync((uint)outStream.Size);
			var bytes = new byte[reader.UnconsumedBufferLength];
			reader.ReadBytes(bytes);
			return bytes.Length > 0
				? new ImageResizeResult(bytes, isPng ? ".png" : ".jpg", WasResized: wasResized, WasEdited: adjustments.HasEdits)
				: null;
		}
		catch (PhotoImageTooLargeException)
		{
			throw;
		}
		catch
		{
			// Conversion failures are reported by the batch service; never write a damaged image.
			return null;
		}
	}

	static Task<PhotoImageDamageTexture?> GetOldPhotoTextureAsync()
	{
		lock (OldPhotoTextureGate)
		{
			return _oldPhotoTextureTask ??= LoadOldPhotoTextureAsync();
		}
	}

	static async Task<PhotoImageDamageTexture?> LoadOldPhotoTextureAsync()
	{
		try
		{
			using var assetStream = await FileSystem.OpenAppPackageFileAsync(OldPhotoTextureAssetPath).ConfigureAwait(false);
			using var buffer = new MemoryStream();
			await assetStream.CopyToAsync(buffer).ConfigureAwait(false);
			var encoded = buffer.ToArray();
			using var textureStream = new InMemoryRandomAccessStream();
			using (var writer = new DataWriter(textureStream.GetOutputStreamAt(0)))
			{
				writer.WriteBytes(encoded);
				await writer.StoreAsync();
			}

			textureStream.Seek(0);
			var decoder = await BitmapDecoder.CreateAsync(textureStream);
			if (decoder.PixelWidth is 0 or > 4096 || decoder.PixelHeight is 0 or > 4096)
			{
				return null;
			}

			var pixelData = await decoder.GetPixelDataAsync(
				BitmapPixelFormat.Bgra8,
				BitmapAlphaMode.Straight,
				new BitmapTransform(),
				ExifOrientationMode.IgnoreExifOrientation,
				ColorManagementMode.DoNotColorManage);
			var bgra = pixelData.DetachPixelData();
			var width = checked((int)decoder.PixelWidth);
			var height = checked((int)decoder.PixelHeight);
			if (bgra.Length != checked(width * height * 4))
			{
				return null;
			}

			var luminance = new byte[checked(width * height)];
			for (var pixel = 0; pixel < luminance.Length; pixel++)
			{
				var index = pixel * 4;
				var blue = bgra[index];
				var green = bgra[index + 1];
				var red = bgra[index + 2];
				luminance[pixel] = (byte)Math.Clamp(
					Math.Round(red * 0.2126 + green * 0.7152 + blue * 0.0722),
					0,
					255);
			}

			return new PhotoImageDamageTexture(width, height, luminance);
		}
		catch
		{
			// The editor remains usable if an optional texture asset cannot be decoded.
			return null;
		}
	}
}

internal readonly record struct ImageResizeResult(byte[] Bytes, string Extension, bool WasResized, bool WasEdited);
