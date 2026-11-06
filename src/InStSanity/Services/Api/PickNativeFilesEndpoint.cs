using InStSanity.Platforms.Windows;
using InStSanity.Services.WebHost;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.Api;

/// <summary><c>POST /api/pick-native-files</c> — opens the Windows photo/video picker.</summary>
public sealed class PickNativeFilesEndpoint : IApiEndpoint
{
	readonly INativeFilePickerService _filePicker;
	readonly NativePickedFileStore _pickedFiles;
	readonly ILogger<PickNativeFilesEndpoint> _logger;

	public PickNativeFilesEndpoint(
		INativeFilePickerService filePicker,
		NativePickedFileStore pickedFiles,
		ILogger<PickNativeFilesEndpoint> logger)
	{
		_filePicker = filePicker;
		_pickedFiles = pickedFiles;
		_logger = logger;
	}

	public string Route => "/api/pick-native-files";

	public async Task<HttpResponseInfo> HandleAsync(
		HttpRequestInfo request,
		CancellationToken cancellationToken)
	{
		if (request.Method != "POST")
		{
			return HttpResponseInfo.Error("method_not_allowed", 405);
		}

		var kind = request.QueryValue("kind");
		if (kind is not ("images" or "video" or "background" or "gif"))
		{
			return HttpResponseInfo.Error("invalid_picker_kind", 400);
		}

		AppDiagnostics.Write("picker.endpoint.request", $"kind={kind}");
		try
		{
			var selectedFiles = await _filePicker.PickFilesAsync(kind, cancellationToken).ConfigureAwait(false);
			if (selectedFiles.Count == 0)
			{
				AppDiagnostics.Write("picker.endpoint.cancelled", $"kind={kind}");
				return HttpResponseInfo.Json(new { ok = true, cancelled = true, files = Array.Empty<NativePickedFileDescriptor>() });
			}

			var files = selectedFiles.Select(_pickedFiles.Register).ToArray();
			AppDiagnostics.Write("picker.endpoint.complete", $"kind={kind}; selectedCount={files.Length}");
			return HttpResponseInfo.Json(new { ok = true, cancelled = false, files });
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			AppDiagnostics.Write("picker.endpoint.cancelled-by-host", $"kind={kind}");
			throw;
		}
		catch (Exception exception)
		{
			AppDiagnostics.WriteException("picker.endpoint.failed", exception, $"kind={kind}");
			_logger.LogError(exception, "Could not open the native {Kind} file picker", kind);
			return HttpResponseInfo.Json(new { ok = false, error = "file_picker_failed" });
		}
	}
}
