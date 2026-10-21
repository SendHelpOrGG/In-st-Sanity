namespace InStSanity.Services.Api;

/// <summary>Opens the native folder chooser and returns the selected path, or null if cancelled.</summary>
public interface IFolderPickerService
{
	Task<string?> PickFolderAsync(CancellationToken cancellationToken);
}
