using HercWorks.Disc;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;

namespace Herculan.Es1.Install;

/// <summary>
/// Metaltech: Earthsiege's CD as the Settings window names it: a folder, such as the CD drive or a copy of the disc, or a
/// disc image (<c>.iso</c>, <c>.bin</c> or <c>.cue</c>) read in place. When the host's environment sets no
/// <c>ALT_DRIVE</c>, it stands in for the CD drive a full install's <c>ES.BAT</c> names in <c>ALT_DRIVE</c>, with
/// <c>PREFER_ALT</c> cleared (docs/es1/shell/launcher.md#the-installers-esbat), so the launcher finds the flic player and
/// the flics on it.
///
/// <para>This engine's own, kept in <see cref="PathFile"/> rather than in the install: <c>ES.BAT</c> holds a DOS drive
/// letter, and a host path written there would break the install for DOS, which cannot see it, and overrun the 25-byte
/// buffers <c>ESCD.EXE</c> copies <c>ALT_DRIVE</c> into (docs/es1/shell/launcher.md#the-flics).</para>
/// </summary>
public static class Es1Disc {
	/// <summary>The flic player <c>ESCD.EXE</c> looks for on the CD, which a disc must hold.</summary>
	public const string FlicPlayer = "playflic.exe";

	/// <summary>The intro flic, which a disc must hold too.</summary>
	public const string IntroFlic = @"flic\intro3.dfl";

	/// <summary>Why a path cannot be the disc.</summary>
	public enum Problem {
		/// <summary>No folder or file is there.</summary>
		Missing,

		/// <summary>A file that does not open as a disc image.</summary>
		Unreadable,

		/// <summary>A folder or image without <see cref="FlicPlayer"/> or <see cref="IntroFlic"/>.</summary>
		NotEs1,
	}

	/// <summary>Where the disc is kept: <c>es1-disc-path.txt</c> in <see cref="UserConfig.Folder"/>, one line, the path.</summary>
	public static string PathFile => UserConfig.PathOf("es1-disc-path.txt");

	/// <summary>The disc's folder or image, whether or not it is still there; null for none.</summary>
	public static string? Remembered() {
		string file = PathFile;
		try {
			return File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } path ? path : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {file} ({ex.Message}); playing without the disc.");
			return null;
		}
	}

	/// <summary>Keeps <paramref name="path"/> as the disc. False when the write failed, which has been logged.</summary>
	public static bool Remember(string path) => UserConfig.Write(PathFile, Path.GetFullPath(path));

	/// <summary>Forgets the disc. False when the file could not be deleted, which has been logged.</summary>
	public static bool Forget() {
		try {
			File.Delete(PathFile);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not delete {PathFile}: {ex.Message}");
			return false;
		}
	}

	/// <summary>Whether <paramref name="path"/> names a disc image rather than a folder.</summary>
	public static bool IsImage(string path) => !Directory.Exists(path) && File.Exists(path);

	/// <summary>
	/// Why <paramref name="path"/> cannot be the disc, or null when it can; <paramref name="detail"/> is the reader's
	/// message for <see cref="Problem.Unreadable"/>.
	/// </summary>
	public static Problem? Check(string path, out string? detail) {
		detail = null;
		if (!Directory.Exists(path) && !File.Exists(path)) {
			return Problem.Missing;
		}

		try {
			using var disc = Open(path);
			return disc.FileExists(FlicPlayer) && disc.FileExists(IntroFlic) ? null : Problem.NotEs1;
		} catch (Exception ex) when (ex is DiscFormatException or IOException or UnauthorizedAccessException) {
			detail = ex.Message;
			return Problem.Unreadable;
		}
	}

	/// <summary>
	/// The remembered disc opened, or null when none is kept or it no longer passes <see cref="Check"/>, which is
	/// logged.
	/// </summary>
	public static GameDisc? OpenRemembered() {
		if (Remembered() is not { } path) {
			return null;
		}

		if (Check(path, out string? detail) is { } problem) {
			Console.Error.WriteLine($"The Metaltech: Earthsiege disc {path} is not used: {problem}{(detail != null ? $" ({detail})" : "")}.");
			return null;
		}

		return Open(path);
	}

	private static GameDisc Open(string path) => IsImage(path) ? GameDisc.OpenImage(path) : GameDisc.OpenFolder(path);
}
