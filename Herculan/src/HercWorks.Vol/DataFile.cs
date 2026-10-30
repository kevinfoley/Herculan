namespace HercWorks.Vol;

/// <summary>
/// General abstract base for all VOL-related data files.
/// Ported from org.hercworks.voln.DataFile.
/// </summary>
public abstract class DataFile {
	/// <summary>The file's first four bytes, fewer for a shorter file; empty for a type that has no header.</summary>
	public byte[]? Header { get; set; }

	public string? FileName { get; set; }
	public string? GameDirPath { get; set; }
	public string? FilePath { get; set; }

	public FileType? Ext { get; set; }
	public FileType? Dir { get; set; }

	public byte[]? RawBytes { get; set; }

	/// <summary>Raw little-endian size bytes as read from the VOL file's per-entry prefix.</summary>
	public byte[]? FileSize { get; set; }

	protected DataFile() { }

	protected DataFile(string fileName, string dirPath) {
		FileName = fileName;
		GameDirPath = dirPath;
	}

	public static string MakeFileName(string pathName) {
		int idx = pathName.LastIndexOf('\\') + 1;
		if (idx == 0) {
			idx = pathName.LastIndexOf('/') + 1;
		}
		return pathName.Substring(idx, pathName.Length - idx);
	}

	public void AssignDir(string path) {
		FilePath = path.Substring(0, path.LastIndexOf('\\') + 1);
	}

	public string OriginNameNoExt() {
		if (FileName != null && FileName.LastIndexOf('.') != -1) {
			return FileName.Substring(0, FileName.LastIndexOf('.'));
		}
		return FileName ?? string.Empty;
	}
}
