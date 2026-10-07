namespace HercWorks.Disc.Iso9660;

/// <summary>A file or directory in an <see cref="IsoFileSystem"/>.</summary>
public sealed class IsoEntry {
	private static readonly IReadOnlyList<IsoEntry> NoChildren = Array.Empty<IsoEntry>();

	private readonly List<IsoEntry>? _children;
	private readonly Dictionary<string, IsoEntry>? _byName;

	internal IsoEntry(IsoEntry? parent, string name, bool isDirectory, int lba, long length, DateTimeOffset? lastWriteTime) {
		Parent = parent;
		Name = name;
		IsDirectory = isDirectory;
		Lba = lba;
		Length = length;
		LastWriteTime = lastWriteTime;
		FullPath = parent == null ? "" : parent.FullPath.Length == 0 ? name : parent.FullPath + "/" + name;
		if (isDirectory) {
			_children = new List<IsoEntry>();
			_byName = new Dictionary<string, IsoEntry>(StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>The name, without the ISO 9660 <c>;1</c> version suffix; empty for the root.</summary>
	public string Name { get; }

	/// <summary>The path from the root, '/'-separated, without a leading separator; empty for the root.</summary>
	public string FullPath { get; }

	/// <summary>Whether this is a directory.</summary>
	public bool IsDirectory { get; }

	/// <summary>The disc address of the first sector of the entry's data.</summary>
	public int Lba { get; }

	/// <summary>The size in bytes: the file's length, or the directory's record extent.</summary>
	public long Length { get; }

	/// <summary>The recording time, or null when the record's is unset or malformed.</summary>
	public DateTimeOffset? LastWriteTime { get; }

	/// <summary>The directory holding this one; null for the root.</summary>
	public IsoEntry? Parent { get; }

	/// <summary>A directory's entries, in the order the disc records them (sorted by name); empty for a file.</summary>
	public IReadOnlyList<IsoEntry> Children => _children ?? NoChildren;

	/// <summary>A directory's entry of this name, ignoring case; null when there is none, or this is a file.</summary>
	public IsoEntry? Child(string name) => _byName != null && _byName.TryGetValue(name, out var child) ? child : null;

	/// <summary>Adds a child, unless one of that name is already present; returns whether it was added.</summary>
	internal bool TryAdd(IsoEntry child) {
		if (_byName == null || !_byName.TryAdd(child.Name, child)) {
			return false;
		}

		_children!.Add(child);
		return true;
	}

	/// <inheritdoc />
	public override string ToString() => IsDirectory ? FullPath + "/" : $"{FullPath} ({Length} bytes)";
}
