using System.Text.Json;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/open-download-folder</c> — opens the configured downloads folder in Explorer.</summary>
public sealed class OpenDownloadFolderEndpoint : IApiEndpoint
{
	readonly IDownloadFolderOpener _folderOpener;
	readonly ILogger<OpenDownloadFolderEndpoint> _logger;

	public OpenDownloadFolderEndpoint(
		IDownloadFolderOpener folderOpener,
		ILogger<OpenDownloadFolderEndpoint> logger)
	{
		_folderOpener = folderOpener;
		_logger = logger;
	}

	public string Route => "/api/open-download-folder";

	public Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return Task.FromResult(HttpResponseInfo.Error("method_not_allowed", 405));
		}

		if (request.Body.Length == 0 || request.Body.Length > 16 * 1024)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		string? requestedFolder;
		try
		{
			using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 8 });
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
			}

			if (!document.RootElement.TryGetProperty("folder", out var folderValue) ||
				folderValue.ValueKind == JsonValueKind.Null)
			{
				requestedFolder = null;
			}
			else if (folderValue.ValueKind == JsonValueKind.String)
			{
				requestedFolder = folderValue.GetString();
			}
			else
			{
				return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
			}
		}
		catch (JsonException)
		{
			return Task.FromResult(HttpResponseInfo.Error("invalid_request", 400));
		}

		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			var folder = DownloadSelectedEndpoint.ResolveFolder(requestedFolder);
			Directory.CreateDirectory(folder);
			_folderOpener.OpenFolder(folder);
			return Task.FromResult(HttpResponseInfo.Json(new { ok = true, folder }));
		}
		catch (Exception exception) when (
			exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or
			InvalidOperationException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
		{
			_logger.LogWarning(exception, "Could not open the configured download folder in File Explorer");
			return Task.FromResult(HttpResponseInfo.Json(new { ok = false, error = "open_folder_failed" }));
		}
	}
}
