using HercWorks.Vol.Util;

namespace HercWorks.Vol.Io;

/// <summary>
/// Ported from org.hercworks.voln.io.VolFileWriter.
///
/// NOTE ON A FIXED BUG: the original Java used File.pathSeparator (";" on Windows) to join
/// directory and file names, which produced invalid paths. This port uses Path.Combine instead.
/// </summary>
public static class VolFileWriter {
	/// <summary>
	/// 'Strict' here means DO NOT calculate new dynamic sizes — instead write the VOL directly
	/// to a file with all data already assembled and counted. Best case is modifying an existing
	/// VOL and doing simple byte-edit tasks.
	/// </summary>
	public static void PackVolToFileStrict(Voln vol, string destPath) {
		if (!Directory.Exists(destPath)) {
			Directory.CreateDirectory(destPath);
		}

		string volOutputPath = Path.Combine(destPath, vol.FileName!);

		using var fout = new FileStream(volOutputPath, FileMode.Create, FileAccess.Write);

		Console.WriteLine($"-Dir List={vol.DirCount}");
		Console.WriteLine($"-Dir Byte Size={vol.DirSize}");
		Console.WriteLine($"-File List={vol.ListCount}");
		Console.WriteLine($"-File Byte Size={vol.ListSize}");

		// Header magic + unknown
		fout.Write(Voln.ByteHeader.Voln, 0, Voln.ByteHeader.Voln.Length);

		// Engine use flags
		fout.WriteByte((byte)(vol.DbsimFlag ? 0x01 : 0x00));
		fout.WriteByte((byte)(vol.VshellFlag ? 0x01 : 0x00));

		// Unknown flags
		fout.WriteByte(0x00);
		fout.WriteByte(0x00);

		// Search precedence, see Voln.VolOrderNum.
		fout.WriteByte(vol.VolOrderNum);

		// Directory count
		fout.WriteByte(vol.DirCount);

		// Directory list byte size (little-endian)
		var dirSizeBytes = ByteOps.GetUInt16LEBytes(vol.DirSize);
		fout.Write(dirSizeBytes, 0, dirSizeBytes.Length);

		// Write folder list
		WriteVolDirList(vol.DbsimFlag, vol.DirCount, vol.Folders, fout);

		// Header file list total
		var listCountBytes = ByteOps.GetUInt16LEBytes(vol.ListCount);
		fout.Write(listCountBytes, 0, listCountBytes.Length);

		// Header file list size in bytes
		var listSizeBytes = ByteOps.GetInt32LEBytes(vol.ListSize);
		fout.Write(listSizeBytes, 0, listSizeBytes.Length);
		fout.Flush();

		// Write files list
		WriteKnownVolFileList(vol, fout);

		// Write files
		PackKnownVolFiles(vol, fout);
	}

	private static void WriteVolDirList(bool isSim, int totalDirs, Dictionary<byte, VolDir> directory, FileStream fout) {
		for (int i = 0; i < totalDirs; i++) {
			if (!directory.TryGetValue((byte)i, out var dir)) {
				continue;
			}

			string label = isSim ? dir.Label.ToLowerInvariant() : dir.Label.ToUpperInvariant();
			byte[] labelBytes = System.Text.Encoding.ASCII.GetBytes(label);

			fout.Write(labelBytes, 0, labelBytes.Length);
			fout.WriteByte((byte)'\\');
			fout.WriteByte(0x00);
			fout.Flush();
		}
	}

	/// <summary>
	/// "Known" here means we've loaded a compiled/existing VOL into memory and want to write it
	/// back out EXACTLY as it was loaded. Best used for debugging unmodified original ES2 VOL files.
	/// </summary>
	private static void WriteKnownVolFileList(Voln vol, FileStream fout) {
		foreach (var entry in vol.FilesSet) {
			fout.Write(entry.VolListBytes!, 0, entry.VolListBytes!.Length);
			fout.WriteByte(entry.DirIdx);
			fout.Write(entry.VolOffset!, 0, entry.VolOffset!.Length);
			fout.Flush();
		}
	}

	/// <summary>
	/// "Known" here means we've loaded a compiled/existing VOL into memory and want to write it
	/// back out EXACTLY as it was loaded. Best used for debugging unmodified original ES2 VOL files.
	/// </summary>
	private static void PackKnownVolFiles(Voln vol, FileStream fout) {
		foreach (var entry in vol.FilesSet) {
			fout.WriteByte(entry.FileCompressionType);
			fout.Write(entry.FileSize!, 0, entry.FileSize!.Length);
			fout.Write(entry.MagicPrefix!, 0, entry.MagicPrefix!.Length);
			fout.Write(entry.RawBytes!, 0, entry.RawBytes!.Length);

			if (entry.Trailer is { Length: > 0 }) {
				fout.Write(entry.Trailer, 0, entry.Trailer.Length);
			}
		}
	}

	public static void UnpackVol(Voln vol, string destPath) {
		if (!Directory.Exists(destPath)) {
			Directory.CreateDirectory(destPath);
		}

		string volDirRoot = Path.Combine(destPath, vol.FileName!.Substring(0, vol.FileName.LastIndexOf('.')));

		if (!Directory.Exists(volDirRoot)) {
			Directory.CreateDirectory(volDirRoot);
		}

		foreach (var idx in vol.Folders.Keys) {
			var folder = vol.Folders[idx];
			string dirPath = Path.Combine(volDirRoot, folder.Label);

			if (!Directory.Exists(dirPath)) {
				Directory.CreateDirectory(dirPath);
			}

			foreach (var entry in folder.Files) {
				if (entry.RawBytes == null || entry.RawBytes.Length == 0) {
					continue;
				}
				WriteVolAssetFile(dirPath, entry);
			}
		}
	}

	/// <summary>
	/// Writes the file in retail-compatible form: the same 9-byte per-entry prefix (compression
	/// type + file size + magic) and trailing marker byte a packed .VOL carries for this entry,
	/// not just the bare content. Real loose copies of these files (e.g. as shipped alongside a
	/// retail install's own .VOL for the external-override mechanism) carry this same prefix, so
	/// an unpacked file needs it too to be a byte-faithful, drop-in-compatible copy.
	/// </summary>
	private static void WriteVolAssetFile(string dirPath, VolEntry entry) {
		string filePath = Path.Combine(dirPath, entry.FileName!.Trim());

		try {
			using var fout = new FileStream(filePath, FileMode.Create, FileAccess.Write);

			fout.WriteByte(entry.FileCompressionType);
			fout.Write(entry.FileSize!, 0, entry.FileSize!.Length);
			fout.Write(entry.MagicPrefix!, 0, entry.MagicPrefix!.Length);
			fout.Write(entry.RawBytes!, 0, entry.RawBytes!.Length);

			if (entry.Trailer is { Length: > 0 }) {
				fout.Write(entry.Trailer, 0, entry.Trailer.Length);
			}
		} catch (IOException e) {
			Console.WriteLine($"{e.Message}\n file={entry.FilePath}\\{entry.FileName}");
		}
	}
}
