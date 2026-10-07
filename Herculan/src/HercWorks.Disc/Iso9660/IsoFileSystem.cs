using System.Buffers.Binary;
using System.Text;

namespace HercWorks.Disc.Iso9660;

/// <summary>
/// The ISO 9660 file system on a <see cref="DiscImage"/>'s data track, with Joliet names when the disc
/// carries them. The whole directory tree is read when it is opened, so a lookup costs no disc read, and
/// it is immutable afterwards, so one instance serves several threads (each <see cref="OpenRead(string)"/>
/// stream is for one).
///
/// <para>The tree is walked breadth-first under limits on depth, entry count and directory size, and a
/// directory whose extent was already visited is not entered again, so a crafted image cannot loop or
/// exhaust memory. A record that is malformed, names an extent outside the image, or carries a name that
/// is not a plain file name (a path separator, <c>..</c>, a control character) is left out and described
/// in <see cref="Problems"/>. Interleaved and multi-extent files, which CD-ROMs do not use, are left out
/// the same way, as are associated files; hidden ones are listed. Rock Ridge is not read.</para>
/// </summary>
public sealed class IsoFileSystem {
	/// <summary>The ISO 9660 logical block size this reader supports, which is every CD's.</summary>
	public const int LogicalBlockSize = DiscImage.UserDataSize;

	/// <summary>Deepest directory nesting entered. ISO 9660 allows 8 levels; this leaves room for discs that exceed it.</summary>
	public const int MaxDepth = 64;

	/// <summary>Most entries read over the whole tree.</summary>
	public const int MaxEntries = 1_000_000;

	/// <summary>Largest single directory extent read.</summary>
	public const int MaxDirectoryBytes = 16 << 20;

	/// <summary>Most directory bytes read over the whole tree.</summary>
	public const long MaxTotalDirectoryBytes = 256L << 20;

	/// <summary>Most volume descriptors read looking for the set terminator.</summary>
	private const int MaxVolumeDescriptors = 64;

	internal const int VolumeSpaceSizeOffset = 80;
	private const int VolumeIdentifierOffset = 40;
	private const int VolumeIdentifierLength = 32;
	private const int EscapeSequencesOffset = 88;
	private const int LogicalBlockSizeOffset = 128;
	private const int RootRecordOffset = 156;
	private const int MinRecordLength = 33;

	private const byte FlagDirectory = 0x02;
	private const byte FlagAssociated = 0x04;
	private const byte FlagMultiExtent = 0x80;

	private static readonly byte[] StandardIdentifier = "CD001"u8.ToArray();

	private readonly DiscImage _image;
	private readonly List<string> _problems = new();

	private IsoFileSystem(DiscImage image, string volumeIdentifier, bool isJoliet, IsoEntry root) {
		_image = image;
		VolumeIdentifier = volumeIdentifier;
		IsJoliet = isJoliet;
		Root = root;
	}

	/// <summary>The volume identifier: <c>EARTHSIEGE2</c> on the v1.10 disc.</summary>
	public string VolumeIdentifier { get; }

	/// <summary>Whether the names are the Joliet tree's rather than the primary tree's.</summary>
	public bool IsJoliet { get; }

	/// <summary>The root directory.</summary>
	public IsoEntry Root { get; }

	/// <summary>Records left out of the tree, and why. Empty for a well-formed disc.</summary>
	public IReadOnlyList<string> Problems => _problems;

	/// <summary>Whether <paramref name="sector"/> starts with a volume descriptor's <c>CD001</c> identifier.</summary>
	internal static bool IsVolumeDescriptor(ReadOnlySpan<byte> sector) =>
		sector.Length >= 6 && sector.Slice(1, 5).SequenceEqual(StandardIdentifier);

	internal static IsoFileSystem Read(DiscImage image, bool preferJoliet) {
		var dataTrack = image.DataTrack ?? throw new DiscFormatException("The image has no data track.");
		byte[] sector = new byte[LogicalBlockSize];
		byte[]? primary = null, joliet = null;

		for (int i = 0; i < MaxVolumeDescriptors; i++) {
			image.ReadSectors(dataTrack.StartLba + DiscImage.VolumeDescriptorStart + i, 1, sector);
			if (!IsVolumeDescriptor(sector)) {
				if (i == 0) {
					throw new DiscFormatException("The data track has no ISO 9660 volume descriptor.");
				}

				break;
			}

			if (sector[0] == 0xff) {
				break;
			}

			if (sector[0] == 1 && primary == null) {
				primary = (byte[])sector.Clone();
			} else if (sector[0] == 2 && joliet == null && IsJolietEscape(sector)) {
				joliet = (byte[])sector.Clone();
			}
		}

		bool useJoliet = joliet != null && (preferJoliet || primary == null);
		byte[] descriptor = (useJoliet ? joliet : primary)
			?? throw new DiscFormatException("The data track has no primary volume descriptor.");

		int blockSize = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(LogicalBlockSizeOffset));
		if (blockSize != LogicalBlockSize) {
			throw new DiscFormatException($"The volume's logical block size is {blockSize}; only {LogicalBlockSize} is supported.");
		}

		var identifierBytes = descriptor.AsSpan(VolumeIdentifierOffset, VolumeIdentifierLength);
		string identifier = (useJoliet ? Encoding.BigEndianUnicode.GetString(identifierBytes) : Encoding.Latin1.GetString(identifierBytes))
			.TrimEnd(' ', '\0');

		var rootRecord = descriptor.AsSpan(RootRecordOffset, 34);
		long rootLba = BinaryPrimitives.ReadUInt32LittleEndian(rootRecord[2..]) + (long)rootRecord[1];
		long rootLength = BinaryPrimitives.ReadUInt32LittleEndian(rootRecord[10..]);
		if (!ExtentFits(image, rootLba, rootLength) || rootLength == 0) {
			throw new DiscFormatException($"The root directory's extent (sector {rootLba}, {rootLength} bytes) is outside the image.");
		}

		var root = new IsoEntry(null, "", true, (int)rootLba, rootLength, ParseTime(rootRecord.Slice(18, 7)));
		var fileSystem = new IsoFileSystem(image, identifier, useJoliet, root);
		fileSystem.ReadTree();
		return fileSystem;
	}

	/// <summary>The entry at <paramref name="path"/>, '/' or '\' separated and matched ignoring case; null when absent.</summary>
	public IsoEntry? Find(string path) {
		var entry = Root;
		foreach (string part in path.Split('/', '\\')) {
			if (part.Length == 0 || part == ".") {
				continue;
			}

			if (entry.Child(part) is not { } child) {
				return null;
			}

			entry = child;
		}

		return entry;
	}

	/// <summary>Whether <paramref name="path"/> names a file.</summary>
	public bool FileExists(string path) => Find(path) is { IsDirectory: false };

	/// <summary>Whether <paramref name="path"/> names a directory.</summary>
	public bool DirectoryExists(string path) => Find(path) is { IsDirectory: true };

	/// <summary>Every file in the tree, depth-first in disc order.</summary>
	public IEnumerable<IsoEntry> EnumerateFiles() {
		var stack = new Stack<IsoEntry>();
		stack.Push(Root);
		while (stack.Count > 0) {
			var directory = stack.Pop();
			foreach (var child in directory.Children) {
				if (!child.IsDirectory) {
					yield return child;
				}
			}

			for (int i = directory.Children.Count - 1; i >= 0; i--) {
				if (directory.Children[i].IsDirectory) {
					stack.Push(directory.Children[i]);
				}
			}
		}
	}

	/// <summary>Opens the file at <paramref name="path"/> as a seekable, read-only stream.</summary>
	/// <exception cref="FileNotFoundException">No file has that path.</exception>
	public Stream OpenRead(string path) => OpenRead(FindFile(path));

	/// <summary>Opens <paramref name="file"/>, an entry of this file system, as a seekable, read-only stream.</summary>
	public Stream OpenRead(IsoEntry file) {
		if (file.IsDirectory) {
			throw new ArgumentException($"{file.FullPath} is a directory.", nameof(file));
		}

		return new SectorStream(_image, file.Lba, file.Length, audio: false);
	}

	/// <summary>Reads the whole file at <paramref name="path"/>.</summary>
	/// <exception cref="FileNotFoundException">No file has that path.</exception>
	public byte[] ReadAllBytes(string path) {
		var file = FindFile(path);
		if (file.Length > Array.MaxLength) {
			throw new DiscFormatException($"{file.FullPath} is {file.Length} bytes, too large to read into one array.");
		}

		byte[] bytes = new byte[file.Length];
		using var stream = OpenRead(file);
		stream.ReadExactly(bytes);
		return bytes;
	}

	private IsoEntry FindFile(string path) =>
		Find(path) is { IsDirectory: false } file ? file : throw new FileNotFoundException($"No file {path} on the disc.", path);

	private void ReadTree() {
		var queue = new Queue<(IsoEntry Directory, int Depth)>();
		var visited = new HashSet<int> { Root.Lba };
		queue.Enqueue((Root, 0));
		long totalBytes = 0;
		int totalEntries = 0;

		while (queue.Count > 0) {
			var (directory, depth) = queue.Dequeue();
			if (directory.Length > MaxDirectoryBytes) {
				_problems.Add($"{Describe(directory)}: {directory.Length}-byte directory not read; the largest read is {MaxDirectoryBytes}.");
				continue;
			}

			totalBytes += directory.Length;
			if (totalBytes > MaxTotalDirectoryBytes) {
				throw new DiscFormatException($"The directories hold more than {MaxTotalDirectoryBytes} bytes of records.");
			}

			int sectors = (int)((directory.Length + LogicalBlockSize - 1) / LogicalBlockSize);
			byte[] records = new byte[sectors * LogicalBlockSize];
			try {
				_image.ReadSectors(directory.Lba, sectors, records);
			} catch (DiscFormatException e) {
				_problems.Add($"{Describe(directory)}: not read ({e.Message})");
				continue;
			}

			int length = (int)directory.Length;
			int position = 0;
			while (position < length) {
				int recordLength = records[position];
				if (recordLength == 0) {
					// Records never straddle a sector; the rest of this one is padding.
					position = (position / LogicalBlockSize + 1) * LogicalBlockSize;
					continue;
				}

				if (recordLength < MinRecordLength || position % LogicalBlockSize + recordLength > LogicalBlockSize
						|| position + recordLength > length) {
					_problems.Add($"{Describe(directory)}: malformed record at byte {position}; the rest of the directory is not read.");
					break;
				}

				var record = records.AsSpan(position, recordLength);
				position += recordLength;

				if (ReadRecord(directory, record) is not { } child) {
					continue;
				}

				if (++totalEntries > MaxEntries) {
					throw new DiscFormatException($"The file system holds more than {MaxEntries} entries.");
				}

				if (!directory.TryAdd(child) || !child.IsDirectory) {
					continue;
				}

				if (depth + 1 > MaxDepth) {
					_problems.Add($"{child.FullPath}: deeper than {MaxDepth} levels; not entered.");
				} else if (!visited.Add(child.Lba)) {
					_problems.Add($"{child.FullPath}: its extent is a directory already read; not entered again.");
				} else {
					queue.Enqueue((child, depth + 1));
				}
			}
		}
	}

	/// <summary>The entry one directory record describes, or null when it is left out (and why, in <see cref="Problems"/>).</summary>
	private IsoEntry? ReadRecord(IsoEntry directory, ReadOnlySpan<byte> record) {
		int nameLength = record[32];
		if (MinRecordLength + nameLength > record.Length) {
			_problems.Add($"{Describe(directory)}: a record's name runs past the record.");
			return null;
		}

		var nameBytes = record.Slice(MinRecordLength, nameLength);
		if (nameLength == 1 && nameBytes[0] is 0 or 1) {
			return null; // "." and ".."
		}

		byte flags = record[25];
		if ((flags & FlagAssociated) != 0) {
			return null;
		}

		bool isDirectory = (flags & FlagDirectory) != 0;
		string rawName = IsJoliet
			? Encoding.BigEndianUnicode.GetString(nameBytes[..(nameLength & ~1)])
			: Encoding.Latin1.GetString(nameBytes);
		if (CleanName(rawName, isDirectory) is not { } name) {
			_problems.Add($"{Describe(directory)}: entry \"{Printable(rawName)}\" is not a plain file name; left out.");
			return null;
		}

		string path = directory.FullPath.Length == 0 ? name : directory.FullPath + "/" + name;
		if ((flags & FlagMultiExtent) != 0) {
			_problems.Add($"{path}: multi-extent files are not supported; left out.");
			return null;
		}

		if (record[26] != 0 || record[27] != 0) {
			_problems.Add($"{path}: interleaved files are not supported; left out.");
			return null;
		}

		long lba = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]) + (long)record[1];
		long length = BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);
		if (length == 0 && !isDirectory) {
			lba = 0;
		} else if (!ExtentFits(_image, lba, length) || (isDirectory && length == 0)) {
			_problems.Add($"{path}: extent (sector {lba}, {length} bytes) is outside the image; left out.");
			return null;
		}

		return new IsoEntry(directory, name, isDirectory, (int)lba, length, ParseTime(record.Slice(18, 7)));
	}

	private static bool ExtentFits(DiscImage image, long lba, long length) {
		long sectors = (length + LogicalBlockSize - 1) / LogicalBlockSize;
		return lba >= image.FirstLba && lba + sectors <= image.EndLba;
	}

	/// <summary>
	/// <paramref name="raw"/> without its <c>;1</c> version and, for a file, the trailing '.' of a name with
	/// no extension; null when what is left is not a plain file name.
	/// </summary>
	private static string? CleanName(string raw, bool isDirectory) {
		string name = raw;
		int version = name.LastIndexOf(';');
		if (version >= 0) {
			name = name[..version];
		}

		if (!isDirectory) {
			name = name.TrimEnd('.');
		}

		if (name.Length == 0 || name is "." or ".."
				|| name.Any(c => c < ' ' || c == '\x7f' || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')) {
			return null;
		}

		return name;
	}

	private static bool IsJolietEscape(ReadOnlySpan<byte> descriptor) =>
		descriptor[EscapeSequencesOffset] == '%' && descriptor[EscapeSequencesOffset + 1] == '/'
		&& descriptor[EscapeSequencesOffset + 2] is (byte)'@' or (byte)'C' or (byte)'E';

	/// <summary>A directory record's 7-byte recording time; null when unset or out of range.</summary>
	private static DateTimeOffset? ParseTime(ReadOnlySpan<byte> time) {
		int year = 1900 + time[0], month = time[1], day = time[2], hour = time[3], minute = time[4], second = time[5];
		int offsetQuarters = (sbyte)time[6];
		if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59
				|| second > 59 || offsetQuarters is < -48 or > 52) {
			return null;
		}

		return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(offsetQuarters * 15));
	}

	private static string Describe(IsoEntry directory) => directory.FullPath.Length == 0 ? "/" : directory.FullPath;

	private static string Printable(string name) =>
		new(name.Select(c => char.IsControl(c) ? '?' : c).ToArray());
}
