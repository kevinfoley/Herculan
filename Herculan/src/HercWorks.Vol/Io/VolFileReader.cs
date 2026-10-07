using HercWorks.Vol.Util;
using System.Diagnostics;

namespace HercWorks.Vol.Io;

/// <summary>
/// Ported from org.hercworks.voln.io.VolFileReader.
/// </summary>
public static class VolFileReader {
	private static readonly byte[] Magic = "VOLN"u8.ToArray();

	private const int OffsetProgramMask = 4;

	// The search precedence: SHELL1.vol and SIMPATCH.vol carry 0x0A, every other archive 0x05, and
	// retail searches 0x0A archives first. See docs/retail/formats/vol-archive.md, "Which archives are mounted".
	private const int OffsetVolOrderNum = 8;

	private const int OffsetDirCount = 9;
	private const int OffsetDirSize = 10;
	private const int OffsetDirListStart = 12;

	// File list follows directory data; file bytes follow the file list.

	/// <summary>
	/// The program mask from a <c>.VOL</c>'s header, reading only its first eight bytes, or null when the
	/// file is too short or does not start with <c>VOLN</c>. Lets a caller skip an archive meant for the
	/// other program without reading it whole, as <c>Volume_LoadVolume</c> (<c>0047c9a4</c>) does.
	/// </summary>
	public static uint? ReadProgramMask(string volPath) {
		using FileStream stream = File.OpenRead(volPath);
		return ReadProgramMask(stream);
	}

	/// <summary>
	/// <see cref="ReadProgramMask(string)"/> for an archive that is not a file of its own, such as one inside a
	/// disc image, reading the first eight bytes from <paramref name="stream"/>'s current position.
	/// </summary>
	public static uint? ReadProgramMask(Stream stream) {
		Span<byte> header = stackalloc byte[OffsetProgramMask + 4];
		if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length) {
			return null;
		}

		return header[..Magic.Length].SequenceEqual(Magic)
			? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetProgramMask..])
			: null;
	}

	public static Voln ParseVolFile(string volPath) =>
		ParseVolBytes(Path.GetFileName(volPath), File.ReadAllBytes(volPath), volPath);

	/// <summary>
	/// Parses an archive already read into memory, for one that is not a file of its own, such as one inside a
	/// disc image. <paramref name="fileName"/> is its name, <paramref name="filePath"/> where it came from, for
	/// <see cref="Voln.FilePath"/>.
	/// </summary>
	public static Voln ParseVolBytes(string fileName, byte[] data, string? filePath = null) {
		var volFile = new Voln.VolnBuilder()
			.SetFileName(DataFile.MakeFileName(fileName))
			.SetRawBytes(data)
			.Build();

		volFile.FilePath = filePath;

		volFile.ProgramMask = (uint)ByteOps.ReadInt32LE(data, OffsetProgramMask);
		volFile.DbsimFlag = (volFile.ProgramMask & Voln.DbsimProgram) != 0;
		volFile.VshellFlag = (volFile.ProgramMask & Voln.VshellProgram) != 0;

		volFile.VolOrderNum = data[OffsetVolOrderNum];

		volFile.DirCount = data[OffsetDirCount];

		volFile.DirSize = (ushort)ByteOps.ReadUInt16LE(data, OffsetDirSize);

		// Build folder list
		int cursor = OffsetDirListStart;
		cursor = GenerateFolderList(volFile, data, cursor);

		// NOTE: does sorting directory intake really matter if writing a new VOL won't conform
		// to the read-in VOL's format at all? (carried over from the original Java comment)
		volFile.ListCount = (ushort)ByteOps.ReadUInt16LE(data, cursor);
		cursor += 2;

		volFile.ListSize = ByteOps.ReadInt32LE(data, cursor);
		cursor += 4;

		byte[] fileListBytes = ByteOps.Slice(volFile.RawBytes!, cursor, volFile.ListSize);

		if (fileListBytes.Length == 0) {
			Console.WriteLine("WARN: no files! ending process");
			return volFile;
		}

		GenerateFileList(volFile, fileListBytes);
		SortHeaderFileListDirs(volFile);

		cursor += volFile.ListSize;

		return volFile;
	}

	private static int GenerateFolderList(Voln vol, byte[] volData, int cursor) {
		byte[] dirList = ByteOps.Slice(volData, cursor, vol.DirSize);
		var dirNameBytes = new List<byte>();
		byte dirCount = 0;

		foreach (byte b in dirList) {
			if (b >= 0x30 && b <= 0x7A) {
				if (b == 0x5C) // '\' separator
				{
					string label = BytesToAsciiString(dirNameBytes.ToArray());
					//Debug.WriteLine(label);

					var dir = new VolDir(label, dirCount);
					vol.Folders[dir.DirIdx] = dir;

					cursor += dirNameBytes.Count + 2;

					dirCount = (byte)(dirCount + 1);
					dirNameBytes.Clear();
				} else {
					dirNameBytes.Add(b);
				}
			}
		}
		return cursor;
	}

	private static void GenerateFileList(Voln vol, byte[] fileListBytes) {
		vol.FilesSet = new VolEntry[vol.ListCount];

		int fileCount = 0;

		for (int i = 0; i < vol.ListCount; i++) {
			var entry = new VolEntry();
			int cursor = i * 18;

			byte[] listing = ByteOps.Slice(fileListBytes, cursor, 18);

			byte[] listName = ByteOps.Slice(listing, 0, 13);
			entry.VolListBytes = listName;

			entry.DirIdx = listing[13];

			entry.VolOffset = ByteOps.Slice(listing, 14, 4);

			entry.FileName = VolEntry.NameFromListBytes(listName);

			entry.Ext = FileTypeExtensions.FromExtension(GetExtension(entry.FileName));

			entry.Dir = FileTypeExtensions.FromExtension(vol.Folders[entry.DirIdx].Label.ToLowerInvariant());

			int startOfs = entry.VolOffsetValue;

			entry.FileCompressionType = vol.RawBytes![startOfs];
			startOfs += 1;

			entry.FileSize = ByteOps.Slice(vol.RawBytes, startOfs, 4);
			int fileSizeVal = ByteOps.ReadInt32LE(entry.FileSize, 0);
			startOfs += 4;

			entry.MagicPrefix = ByteOps.Slice(vol.RawBytes, startOfs, 4);
			startOfs += 4;

			int endOfs = startOfs + fileSizeVal;

			try {
				entry.RawBytes = FetchFileBytes(vol.RawBytes, startOfs, endOfs);
			} catch (Exception e) {
				Console.WriteLine($"ERROR[{entry.FileName}](setRawBytes): {e.Message}");
			}

			bool hasNoHeader = entry.Ext.HasValue && Voln.FilesNoHeader().Contains(entry.Ext.Value.Val());
			if (hasNoHeader) {
				entry.Header = Array.Empty<byte>();
			} else {
				try {
					entry.Header = ByteOps.Slice(entry.RawBytes!, 0, 4);
				} catch (Exception dbgE) {
					Console.WriteLine($"ERROR[{entry.FileName}](setHeader):{dbgE.Message}");
				}
			}

			entry.Trailer = endOfs < vol.RawBytes.Length
				? ByteOps.Slice(vol.RawBytes, endOfs, 1)
				: null;

			vol.FilesSet[fileCount] = entry;
			fileCount += 1;
		}
	}

	private static void SortHeaderFileListDirs(Voln vol) {
		foreach (var file in vol.FilesSet) {
			vol.Folders[file.DirIdx].Files.Add(file);
		}
	}

	public static byte[]? FetchFileBytes(byte[] volData, int fileMemOfs, int nextFileMemOfs) {
		try {
			return ByteOps.Slice(volData, fileMemOfs, nextFileMemOfs - fileMemOfs);
		} catch (Exception e) {
			Console.WriteLine(e.Message);
			return null;
		}
	}

	private static string GetExtension(string fileName) {
		int idx = fileName.LastIndexOf('.');
		return idx >= 0 ? fileName.Substring(idx + 1) : fileName;
	}

	private static string BytesToAsciiString(byte[] bytes) {
		var chars = new char[bytes.Length];
		for (int i = 0; i < bytes.Length; i++) {
			chars[i] = (char)bytes[i];
		}
		return new string(chars);
	}
}
