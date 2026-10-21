namespace InStSanity.Services.Api;

/// <summary>Opens the user's configured download folder in the native file manager.</summary>
public interface IDownloadFolderOpener
{
	void OpenFolder(string folderPath);
}
