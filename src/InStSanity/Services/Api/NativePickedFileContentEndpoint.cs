using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary>Serves a short-lived, user-selected file handle to the WebView for preview only.</summary>
public sealed class NativePickedFileContentEndpoint : IApiEndpoint
{
	const int MaxRangeBytes = 8 * 1024 * 1024;
	const long MaxUnrangedResponseBytes = 128L * 1024 * 1024;

	static readonly Dictionary<string, string> RangeHeaders = new(StringComparer.OrdinalIgnoreCase)
	{
		["Accept-Ranges"] = "bytes",
		["Cache-Control"] = "no-store",
	};

	readonly NativePickedFileStore _pickedFiles;
	readonly ILogger<NativePickedFileContentEndpoint> _logger;

	public NativePickedFileContentEndpoint(
		NativePickedFileStore pickedFiles,
		ILogger<NativePickedFileContentEndpoint> logger)
	{
		_pickedFiles = pickedFiles;
		_logger = logger;
	}

	public string Route => "/api/picked-file";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "GET")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		if (!Guid.TryParse(request.QueryValue("id"), out var id) || id == Guid.Empty || !_pickedFiles.TryGet(id, out var file))
		{
			return HttpResponseInfo.Error("picked_file_unavailable", 404);
		}

		try
		{
			var length = new FileInfo(file.Path).Length;
			if (length <= 0)
			{
				return HttpResponseInfo.Error("picked_file_unavailable", 404);
			}

			var contentType = MimeTypes.ForFile(file.Path);
			var rangeHeader = request.Header("Range");
			long start;
			long end;
			var partial = !string.IsNullOrWhiteSpace(rangeHeader);
			if (partial)
			{
				if (!LoopbackHost.TryParseRange(rangeHeader, length, out start, out end))
				{
					return RangeNotSatisfiable(length, contentType);
				}

				end = Math.Min(end, start + MaxRangeBytes - 1L);
			}
			else
			{
				if (length > MaxUnrangedResponseBytes)
				{
					return RangeNotSatisfiable(length, contentType);
				}

				start = 0;
				end = length - 1;
			}

			var body = await ReadSegmentAsync(file.Path, start, checked((int)(end - start + 1)), cancellationToken)
				.ConfigureAwait(false);
			if (body.Length != end - start + 1)
			{
				return HttpResponseInfo.Error("picked_file_unavailable", 404);
			}

			var headers = new Dictionary<string, string>(RangeHeaders, StringComparer.OrdinalIgnoreCase);
			if (partial)
			{
				headers["Content-Range"] = $"bytes {start}-{end}/{length}";
			}

			return HttpResponseInfo.Bytes(body, contentType, partial ? 206 : 200, headers);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
			System.Security.SecurityException)
		{
			_logger.LogWarning(exception, "Could not read a user-selected file for WebView preview");
			return HttpResponseInfo.Error("picked_file_unavailable", 404);
		}
	}

	static HttpResponseInfo RangeNotSatisfiable(long length, string contentType)
	{
		var headers = new Dictionary<string, string>(RangeHeaders, StringComparer.OrdinalIgnoreCase)
		{
			["Content-Range"] = $"bytes */{length}",
		};
		return HttpResponseInfo.Bytes(Array.Empty<byte>(), contentType, 416, headers);
	}

	static async Task<byte[]> ReadSegmentAsync(
		string path,
		long start,
		int count,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[count];
		await using var input = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 64 * 1024,
			options: FileOptions.Asynchronous);
		input.Seek(start, SeekOrigin.Begin);

		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await input.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			offset += read;
		}

		return offset == buffer.Length ? buffer : buffer[..offset];
	}
}
