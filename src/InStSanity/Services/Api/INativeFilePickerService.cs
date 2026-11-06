namespace InStSanity.Services.Api;

/// <summary>Opens the Windows-native picker for an app-owned photo or video selection.</summary>
public interface INativeFilePickerService
{
	Task<IReadOnlyList<NativeFileSelection>> PickFilesAsync(string kind, CancellationToken cancellationToken);
}

/// <summary>A file the user explicitly selected through the native picker.</summary>
public sealed record NativeFileSelection(string Path, string Name, long Size, string ContentType);
