using System.Text;

namespace Herculan.Engine.Platform;

/// <summary>
/// The folder the player's settings are kept in, <c>%APPDATA%\Herculan</c> on Windows and the platform's user-config
/// directory elsewhere, and a save into it that a crash cannot leave half-written. Each setting has a file of its own,
/// read and written by the one class it belongs to, so the game and the editor never write the same file.
/// </summary>
public static class UserConfig {
	/// <summary>The folder. It may not exist yet: <see cref="Write"/> creates it.</summary>
	public static string Folder => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
		"Herculan");

	/// <summary>The path of <paramref name="fileName"/> in <see cref="Folder"/>.</summary>
	public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

	/// <summary>
	/// Replaces <paramref name="path"/> with <paramref name="contents"/> in UTF-8 without a byte-order mark, as
	/// <see cref="File.WriteAllText(string, string)"/> writes, creating its folder on the first save. The text goes to a
	/// temporary file beside it, flushed to the disk, which then replaces it, so a crash, a power cut or a full disk
	/// mid-save leaves the old file whole rather than empty or cut short. A failed write is logged, and returns false.
	/// </summary>
	public static bool Write(string path, string contents) {
		string temporary = path + ".tmp";
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
				stream.Write(Encoding.UTF8.GetBytes(contents));
				stream.Flush(flushToDisk: true);
			}

			File.Move(temporary, path, overwrite: true);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not write {path}: {ex.Message}");
			try {
				File.Delete(temporary);
			} catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) {
				// Left for the next save, which overwrites it.
			}

			return false;
		}
	}

	/// <summary><see cref="Write"/> with each line ended by the platform's newline, as <see cref="File.WriteAllLines(string, IEnumerable{string})"/> writes.</summary>
	public static bool WriteLines(string path, IEnumerable<string> lines)
		=> Write(path, string.Concat(lines.Select(line => line + Environment.NewLine)));
}
