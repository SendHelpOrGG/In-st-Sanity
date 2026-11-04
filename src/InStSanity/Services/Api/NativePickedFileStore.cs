using System.Collections.Concurrent;

namespace InStSanity.Services.Api;

/// <summary>Short-lived handles for files explicitly selected by the user in the native picker.</summary>
public sealed class NativePickedFileStore
{
	static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
	readonly ConcurrentDictionary<Guid, NativePickedFileRecord> _files = new();

	public NativePickedFileDescriptor Register(NativeFileSelection selection)
	{
		ArgumentNullException.ThrowIfNull(selection);
		if (string.IsNullOrWhiteSpace(selection.Path) || !Path.IsPathFullyQualified(selection.Path))
		{
			throw new ArgumentException("The selected file path is invalid.", nameof(selection));
		}

		var id = Guid.NewGuid();
		var record = new NativePickedFileRecord(
			id,
			Path.GetFullPath(selection.Path),
			selection.Name,
			Math.Max(0, selection.Size),
			selection.ContentType,
			DateTimeOffset.UtcNow.Add(Lifetime));
		_files[id] = record;
		RemoveExpiredEntries();

		return new NativePickedFileDescriptor(
			id.ToString("N"),
			record.Name,
			record.Size,
			record.ContentType);
	}

	public bool TryGet(Guid id, out NativePickedFileRecord record)
	{
		if (id != Guid.Empty && _files.TryGetValue(id, out record!) && record.ExpiresAtUtc > DateTimeOffset.UtcNow)
		{
			return true;
		}

		if (id != Guid.Empty)
		{
			_files.TryRemove(id, out _);
		}

		record = null!;
		return false;
	}

	public async Task<byte[]?> ReadAllBytesAsync(Guid id, CancellationToken cancellationToken)
	{
		if (!TryGet(id, out var file))
		{
			return null;
		}

		return await File.ReadAllBytesAsync(file.Path, cancellationToken).ConfigureAwait(false);
	}

	void RemoveExpiredEntries()
	{
		var now = DateTimeOffset.UtcNow;
		foreach (var (id, file) in _files)
		{
			if (file.ExpiresAtUtc <= now)
			{
				_files.TryRemove(id, out _);
			}
		}
	}
}

public sealed record NativePickedFileDescriptor(string Id, string Name, long Size, string ContentType);

public sealed record NativePickedFileRecord(
	Guid Id,
	string Path,
	string Name,
	long Size,
	string ContentType,
	DateTimeOffset ExpiresAtUtc);
