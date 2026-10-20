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
			using var decoder = await BitmapDecoder.CreateAsync(inStream);
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
}
