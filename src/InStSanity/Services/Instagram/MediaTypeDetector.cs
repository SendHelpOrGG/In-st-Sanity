using InStSanity.Services.WebHost;

namespace InStSanity.Services.Instagram;

/// <summary>
/// Sniffs the content type of fetched media.
/// </summary>
/// <remarks>
/// Instagram's CDN serves the same URL as JPEG, WebP or MP4 depending on the request, and
/// cached payloads are stored without an extension, so the magic bytes are the reliable
/// source of truth. The URL extension is only a fallback.
/// </remarks>
internal static class MediaTypeDetector
{
	const string OctetStream = "application/octet-stream";

	public static string Detect(byte[] payload, Uri uri)
	{
		if (TryDetectFromSignature(payload, out var contentType, out _))
		{
			return contentType;
		}

		var fromExtension = MimeTypes.ForExtension(Path.GetExtension(uri.AbsolutePath));
		return fromExtension == OctetStream ? OctetStream : fromExtension;
	}

	/// <summary>File extension suitable for a download, including the leading dot.</summary>
	public static string ExtensionFor(byte[] payload)
		=> TryDetectFromSignature(payload, out _, out var extension) ? extension : ".bin";

	static bool TryDetectFromSignature(byte[] payload, out string contentType, out string extension)
	{
		contentType = OctetStream;
		extension = ".bin";

		if (payload.Length < 12)
		{
			return false;
		}

		// JPEG
		if (payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF)
		{
			return Describe("image/jpeg", ".jpg", out contentType, out extension);
		}

		// PNG
		if (payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4E && payload[3] == 0x47)
		{
			return Describe("image/png", ".png", out contentType, out extension);
		}

		// GIF
		if (payload[0] == 0x47 && payload[1] == 0x49 && payload[2] == 0x46)
		{
			return Describe("image/gif", ".gif", out contentType, out extension);
		}

		// RIFF....WEBP
		if (payload[0] == 0x52 && payload[1] == 0x49 && payload[2] == 0x46 && payload[3] == 0x46 &&
			payload[8] == 0x57 && payload[9] == 0x45 && payload[10] == 0x42 && payload[11] == 0x50)
		{
			return Describe("image/webp", ".webp", out contentType, out extension);
		}

		// ....ftyp => ISO base media (MP4 / M4V)
		if (payload[4] == 0x66 && payload[5] == 0x74 && payload[6] == 0x79 && payload[7] == 0x70)
		{
			return Describe("video/mp4", ".mp4", out contentType, out extension);
		}

		// EBML => WebM / Matroska
		if (payload[0] == 0x1A && payload[1] == 0x45 && payload[2] == 0xDF && payload[3] == 0xA3)
		{
			return Describe("video/webm", ".webm", out contentType, out extension);
		}

		return false;
	}

	static bool Describe(string type, string ext, out string contentType, out string extension)
	{
		contentType = type;
		extension = ext;
		return true;
	}
}
