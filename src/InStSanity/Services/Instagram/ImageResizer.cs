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
		string originalExtension)
	{
		if (payload.Length < 32 || maxSide < 16)
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
			var largestSide = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
			if (largestSide <= (uint)maxSide)
			{
				return new ImageResizeResult(payload, originalExtension, WasResized: false);
			}

			var scale = (double)maxSide / largestSide;
			var targetWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
			var targetHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
			var transform = new BitmapTransform
			{
				ScaledWidth = targetWidth,
				ScaledHeight = targetHeight,
				InterpolationMode = BitmapInterpolationMode.Fant,
			};

			using var software = await decoder.GetSoftwareBitmapAsync(
				BitmapPixelFormat.Bgra8,
				BitmapAlphaMode.Premultiplied,
				transform,
				ExifOrientationMode.RespectExifOrientation,
				ColorManagementMode.DoNotColorManage);

			var isPng = payload.Length >= 8 && payload[0] == 0x89 &&
				payload[1] == (byte)'P' && payload[2] == (byte)'N' && payload[3] == (byte)'G';
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
			return bytes.Length > 0
				? new ImageResizeResult(bytes, isPng ? ".png" : ".jpg", WasResized: true)
				: null;
		}
		catch
		{
			// Conversion failures are reported by the batch service; never write a damaged image.
			return null;
		}
	}
}

internal readonly record struct ImageResizeResult(byte[] Bytes, string Extension, bool WasResized);
