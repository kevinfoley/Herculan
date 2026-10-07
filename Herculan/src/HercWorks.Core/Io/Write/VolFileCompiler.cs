using HercWorks.Vol;
using HercWorks.Vol.Util;
using System.Diagnostics;
using System.Text;

namespace HercWorks.Core.Io.Write;

/// <summary>
/// Attempts to write non-strict Voln objects out to a proper ThreeSpace 2.0 .VOL file — i.e.
/// compiles a brand-new VOL from scratch (computing fresh offsets/sizes), unlike the "strict"
/// writer which just round-trips an already-loaded VOL byte-for-byte.
/// Ported from org.hercworks.core.io.write.VolFileCompiler.
/// </summary>
public static class VolFileCompiler {
	/// <summary>Compiles a brand-new VOL file, calculating offsets and sizes, and writes it to <paramref name="outputPath"/>.</summary>
	public static void Compile(Voln vol, string outputPath) {
		CalculateDirHeader(vol);
		GenerateFileSet(vol);
		CompileFileList(vol);

		byte[]? rawBytes = null;
		try {
			rawBytes = PackCompiledVol(vol);
		} catch (IOException e) {
			Console.WriteLine(e.Message);
		}

		vol.RawBytes = rawBytes;

		try {
			VolFileWriter.PackVolToFileStrict(vol, outputPath);
		} catch (Exception e) {
			Console.WriteLine(e.Message);
		}
	}

	private static void CalculateDirHeader(Voln vol) {
		vol.DirCount = (byte)vol.Folders.Count;

		short dirLen = 0;

		for (int i = 0; i < vol.Folders.Count; i++) {
			if (!vol.Folders.TryGetValue((byte)i, out var dir)) {
				continue;
			}
			int size = dir.Label.Length + 2; // '\' + spacer byte
			dirLen += (short)size;
		}

		vol.DirSize = (ushort)dirLen;
	}

	private static void GenerateFileSet(Voln vol) {
		int size = 0;

		for (int i = 0; i < vol.Folders.Count; i++) {
			if (!vol.Folders.TryGetValue((byte)i, out var dir)) {
				continue;
			}
			size += dir.Files.Count;
		}

		vol.FilesSet = new VolEntry[size];

		int idx = 0;
		for (int i = 0; i < vol.Folders.Count; i++) {
			if (!vol.Folders.TryGetValue((byte)i, out var dir)) {
				continue;
			}
			foreach (var entry in dir.Files) {
				vol.FilesSet[idx] = entry;
				idx++;
			}
		}
		vol.ListCount = (ushort)idx;
		vol.ListSize = idx * 18;
	}

	private static void CompileFileList(Voln vol) {
		// 1) start at the correct offset - header len + dir list len
		// 2) calculate file list size: 18 * file total, then store offset: startOfs + fileList size
		int offset = Voln.ByteHeader.VolnHeaderLen.Length + 1 + 2 + vol.DirSize;

		int fileListSize = vol.FilesSet.Length * 18;
		int fileOfs = offset + fileListSize + 6; // 2 for [# of files], 4 for [file list size]

		for (int i = 0; i < vol.FilesSet.Length; i++) {
			var entry = vol.FilesSet[i];
			int entrySize = CalcFileChunkSize(entry) - i;

			Debug.WriteLine($"{entry.FileName}: entrySize[{entrySize}]");

			byte[] nameBytes = Encoding.ASCII.GetBytes(entry.FileName ?? string.Empty);
			var listBytes = new byte[13];
			Array.Copy(nameBytes, listBytes, Math.Min(nameBytes.Length, 13));
			entry.VolListBytes = listBytes;

			entry.VolOffset = ByteOps.GetInt32LEBytes(fileOfs);

			fileOfs += entrySize;
		}
	}

	private static byte[] PackCompiledVol(Voln vol) {
		using var volStream = new MemoryStream();

		Console.WriteLine($"-Dir List={vol.DirCount}");
		Console.WriteLine($"-Dir Byte Size={vol.DirSize}");
		Console.WriteLine($"-File List={vol.ListCount}");
		Console.WriteLine($"-File Byte Size={vol.ListSize}");

		// Header with unknown
		volStream.Write(Voln.ByteHeader.Voln, 0, Voln.ByteHeader.Voln.Length);

		// Engine use flags
		volStream.WriteByte((byte)(vol.DbsimFlag ? 0x01 : 0x00));
		volStream.WriteByte((byte)(vol.VshellFlag ? 0x01 : 0x00));

		// Unknown flags
		volStream.WriteByte(0x00);
		volStream.WriteByte(0x00);

		// Search precedence, see Voln.VolOrderNum.
		volStream.WriteByte(0x0A); // TODO (carried over from Java)

		// Directory count
		volStream.WriteByte(vol.DirCount);

		// Directory list byte size
		var dirSizeBytes = ByteOps.GetUInt16LEBytes(vol.DirSize);
		volStream.Write(dirSizeBytes, 0, dirSizeBytes.Length);

		// Write folder list
		CompileVolDirList(vol.DbsimFlag, vol.DirCount, vol.Folders, volStream);

		// Header file list total
		var listCountBytes = ByteOps.GetUInt16LEBytes(vol.ListCount);
		volStream.Write(listCountBytes, 0, listCountBytes.Length);

		// Header file list size in bytes
		var listSizeBytes = ByteOps.GetInt32LEBytes(vol.ListSize);
		volStream.Write(listSizeBytes, 0, listSizeBytes.Length);
		volStream.Flush();

		// Write files list
		CompileVolFileList(vol, volStream);

		// Write files
		PackVolFiles(vol, volStream);

		return volStream.ToArray();
	}

	private static void CompileVolDirList(bool isSim, int totalDirs, Dictionary<byte, VolDir> directory, MemoryStream bass) {
		for (int i = 0; i < totalDirs; i++) {
			if (!directory.TryGetValue((byte)i, out var dir)) {
				continue;
			}

			string dirName = (isSim ? dir.Label.ToLowerInvariant() : dir.Label.ToUpperInvariant()) + "\\";

			var nameBytes = Encoding.ASCII.GetBytes(dirName);
			bass.Write(nameBytes, 0, nameBytes.Length);
			bass.WriteByte(0x00);
			bass.Flush();
		}
	}

	private static void CompileVolFileList(Voln vol, MemoryStream bass) {
		foreach (var entry in vol.FilesSet) {
			bass.Write(entry.VolListBytes!, 0, entry.VolListBytes!.Length);
			bass.WriteByte(entry.DirIdx);
			bass.Write(entry.VolOffset!, 0, entry.VolOffset!.Length);
			bass.Flush();
		}
	}

	private static void PackVolFiles(Voln vol, MemoryStream bass) {
		Console.WriteLine("COMPILE FILE LIST=====================================");

		foreach (var entry in vol.FilesSet) {
			string tailByte = entry.Trailer != null ? ByteOps.ToHex(entry.Trailer) : "";
			int tailByteVal = entry.Trailer != null ? entry.Trailer[0] & 0xFF : 0;

			Console.WriteLine($"{entry.FileName}\t| ofs[{entry.VolOffsetValue}]\t| magic[{entry.PrintMagicPrefix()}]\t| rawByteSize[{entry.RawBytes?.Length}]\t| tail byte[{tailByte}]({tailByteVal})");

			bass.WriteByte(entry.FileCompressionType);
			bass.Write(entry.FileSize!, 0, entry.FileSize!.Length);
			bass.Write(entry.MagicPrefix!, 0, entry.MagicPrefix!.Length);
			bass.Write(entry.RawBytes!, 0, entry.RawBytes!.Length);

			if (tailByte.Length > 0) {
				bass.Write(entry.Trailer!, 0, entry.Trailer!.Length);
			}
		}
	}

	private static int CalcFileChunkSize(VolEntry entry) {
		int seg = 1; // compression type byte
		seg += 4; // file size UINT32
		seg += 4; // magic UINT32
		seg += entry.RawBytes!.Length;

		// Original computed this via a hex-string-length/2 round trip (Bytes.from(hexString) —
		// treating the hex text as literal ASCII bytes, then halving the count). That's
		// mathematically equivalent to just the tail byte count for any length, since a hex
		// string is always 2 characters per byte — simplified to the direct form here.
		seg += entry.Trailer?.Length ?? 0;

		return seg;
	}
}
