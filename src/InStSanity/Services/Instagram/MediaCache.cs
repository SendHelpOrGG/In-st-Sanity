using System.Security.Cryptography;
using System.Text;

namespace InStSanity.Services.Instagram;

/// <summary>
/// On-disk cache for media pulled from Instagram's CDN.
/// </summary>
/// <remarks>
/// Covers are requested repeatedly (grid, detail view, full-size view), and CDN URLs are
/// signed and expire, so caching by URL keeps navigation instant and survives expired links
/// for anything already downloaded. Resized thumbnails are stored under the same URL plus
/// a width tag, so a repeat grid visit never re-decodes the original.
/// </remarks>
public sealed class MediaCache
{
	public MediaCache()
	{
		Directory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"InStSanity",
			"MediaCache");
	}

	/// <summary>Folder the cached payloads live in (created on first write).</summary>
	public string Directory { get; }

	public Task<byte[]?> TryReadAsync(Uri uri, CancellationToken cancellationToken)
		=> TryReadAsync(uri, 0, cancellationToken);

	public async Task<byte[]?> TryReadAsync(Uri uri, int width, CancellationToken cancellationToken)
	{
		var path = PathFor(uri, width);

		try
		{
			return File.Exists(path)
				? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
				: null;
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	public Task WriteAsync(Uri uri, byte[] payload, CancellationToken cancellationToken)
		=> WriteAsync(uri, 0, payload, cancellationToken);

	public async Task WriteAsync(Uri uri, int width, byte[] payload, CancellationToken cancellationToken)
	{
		if (payload.Length == 0)
		{
			return;
		}

		var path = PathFor(uri, width);
		var temporary = path + ".tmp";

		try
		{
			System.IO.Directory.CreateDirectory(Directory);
			await File.WriteAllBytesAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, path, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The cache is best-effort: a failure here must never break a request.
			TryDelete(temporary);
		}
	}

	public string PathFor(Uri uri) => PathFor(uri, 0);

	public string PathFor(Uri uri, int width)
	{
		var key = width > 0 ? $"{uri.AbsoluteUri}#w={width}" : uri.AbsoluteUri;
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
			.ToLowerInvariant();

		return Path.Combine(Directory, hash);
	}

	static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (IOException)
		{
			// ignored
		}
		catch (UnauthorizedAccessException)
		{
			// ignored
		}
	}
}
