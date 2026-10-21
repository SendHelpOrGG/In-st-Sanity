using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/browse-folder</c> — asks the desktop OS to choose a download folder.</summary>
public sealed class BrowseFolderEndpoint : IApiEndpoint
{
	readonly IFolderPickerService _folderPicker;
	readonly ILogger<BrowseFolderEndpoint> _logger;

	public BrowseFolderEndpoint(
		IFolderPickerService folderPicker,
		ILogger<BrowseFolderEndpoint> logger)
	{
		_folderPicker = folderPicker;
		_logger = logger;
	}

	public string Route => "/api/browse-folder";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		try
		{
			var path = await _folderPicker.PickFolderAsync(cancellationToken).ConfigureAwait(false);
			return path is null
				? HttpResponseInfo.Json(new { ok = true, cancelled = true })
				: HttpResponseInfo.Json(new { ok = true, path });
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Could not open the native download-folder picker");
			return HttpResponseInfo.Json(new { ok = false, error = "folder_picker_failed" });
		}
	}
}
