using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Disc;
using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Install;

/// <summary>
/// Finds an Earthsiege 2 installation to load data from. The engine never runs the original
/// executables (see docs/herculan/planning.md) — it only reads their data files — so "an install"
/// here means a directory containing a <c>VOL</c> folder with the game's archives in it.
/// </summary>
public static class GameInstall {
	/// <summary>Environment variable checked first, so a developer can point at any install.</summary>
	public const string PathVariable = "ES2_GAME_PATH";

	/// <summary>The archive subfolder inside an install root.</summary>
	public const string ArchiveFolderName = "VOL";

	/// <summary>
	/// Where the last install used is remembered: <c>%APPDATA%\Herculan\install-path.txt</c> on Windows,
	/// and the platform's equivalent user-config directory elsewhere. One line, the path.
	/// </summary>
	public static string RememberedPathFile => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
			Environment.SpecialFolderOption.DoNotVerify),
		"Herculan", "install-path.txt");

	/// <summary>
	/// Resolves an install root, in order: an explicit <paramref name="explicitPath"/> (a command
	/// line argument), the <c>ES2_GAME_PATH</c> environment variable, the install last used (see
	/// <see cref="Remember"/>), then an <c>ES2</c> folder beside the running binary or any folder above
	/// it, which covers a sibling install checked out beside this repo, reached from
	/// <c>src/Herculan.Engine.Host/bin/...</c>.
	/// Returns null when nothing matched, so the caller can ask the player or print something more
	/// useful than a stack trace. An explicit path that is not an install is null too, without trying
	/// the rest: a path the player typed is never silently swapped for another.
	/// </summary>
	public static string? Locate(string? explicitPath = null) {
		if (!string.IsNullOrWhiteSpace(explicitPath)) {
			return IsInstallRoot(explicitPath) ? Path.GetFullPath(explicitPath) : null;
		}

		string? fromEnvironment = Environment.GetEnvironmentVariable(PathVariable);
		if (!string.IsNullOrWhiteSpace(fromEnvironment) && IsInstallRoot(fromEnvironment)) {
			return Path.GetFullPath(fromEnvironment);
		}

		// A remembered install that has since moved or been deleted falls through to the search.
		if (LoadRemembered() is { } remembered && IsInstallRoot(remembered)) {
			return Path.GetFullPath(remembered);
		}

		// Walk up from the binary looking for a sibling ES2 folder — covers running straight out
		// of the repo without any configuration, which is the normal development case.
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null) {
			string candidate = Path.Combine(directory.FullName, "ES2");
			if (IsInstallRoot(candidate)) {
				return Path.GetFullPath(candidate);
			}
			directory = directory.Parent;
		}

		return null;
	}

	/// <summary>
	/// Records <paramref name="installRoot"/> as the install last used, which <see cref="Locate"/> tries
	/// before searching. A failed write is logged and otherwise ignored: the next launch searches again.
	/// </summary>
	public static void Remember(string installRoot) {
		string path = RememberedPathFile;
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, Path.GetFullPath(installRoot));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not write {path}: {ex.Message}");
		}
	}

	/// <summary>The <c>VOL</c> archive directory inside an install root.</summary>
	public static string ArchiveDirectory(string installRoot) =>
		Path.Combine(installRoot, ArchiveFolderName);

	/// <summary>Whether <paramref name="path"/> is an install root: a directory holding the archive directory.</summary>
	public static bool IsInstallRoot(string path) =>
		Directory.Exists(path) && Directory.Exists(ArchiveDirectory(path));

	/// <summary>
	/// The directory the first token of the install's <c>data\drive.cfg</c> names, which on a retail
	/// install is the disc (docs/retail/formats/vol-archive.md, "Which archives are mounted"), resolved against
	/// the install root; null when the file is missing, unreadable or holds no token. Retail cannot start
	/// without the file (<c>Sim_Run</c>, <c>0045f144</c>; <c>DriveCfg_Read</c>, VSHELL <c>0040d327</c>);
	/// here an install without one simply has no disc.
	/// </summary>
	public static string? DiscDirectory(string installRoot) {
		string path = DriveCfgPath(installRoot);
		try {
			if (!File.Exists(path) || new DriveTransformer().Parse(File.ReadAllBytes(path))?.Directory is not { } directory) {
				return null;
			}

			return Path.GetFullPath(Path.Combine(installRoot, directory));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
				or NotSupportedException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); the install has no disc.");
			return null;
		}
	}

	/// <summary>
	/// Why <paramref name="directory"/> cannot go into <c>drive.cfg</c>, or null when it can: it must exist,
	/// and survive the retail readers' <c>fscanf("%s")</c> and the file's single-byte text, so no whitespace
	/// and nothing outside Latin-1.
	/// </summary>
	public static DiscDirectoryProblem? CheckDiscDirectory(string directory) =>
		!Directory.Exists(directory) ? DiscDirectoryProblem.Missing
		: DriveTransformer.HasWhitespace(directory) ? DiscDirectoryProblem.Whitespace
		: directory.Any(c => c > '\xff') ? DiscDirectoryProblem.NotLatin1
		: null;

	/// <summary>
	/// Writes <paramref name="directory"/> into the install's <c>data\drive.cfg</c> as the disc, keeping the
	/// file's second line, the install's own directory, or writing <paramref name="installRoot"/> there when it
	/// has none, as the installer's <c>BATCH.EXE</c> does (docs/retail/retail-builds.md, "The installer"). Takes only a
	/// directory <see cref="CheckDiscDirectory"/> passes.
	/// </summary>
	public static void WriteDiscDirectory(string installRoot, string directory) {
		if (CheckDiscDirectory(directory) is { } problem) {
			throw new ArgumentException($"{directory} cannot go into drive.cfg: {problem}.", nameof(directory));
		}

		string path = DriveCfgPath(installRoot);
		var transformer = new DriveTransformer();
		var drive = File.Exists(path) ? transformer.Parse(File.ReadAllBytes(path)) ?? new Drive() : new Drive();
		drive.Directory = Path.GetFullPath(directory);
		drive.InstallDirectory ??= Path.GetFullPath(installRoot);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, transformer.Write(drive)!);
	}

	/// <summary>
	/// The disc image the install's <c>data\drive.cfg</c> names on HERCULAN's own line
	/// (<see cref="Drive.DiscImage"/>), resolved against the install root; null when it names none or the file
	/// cannot be read.
	/// </summary>
	public static string? DiscImagePath(string installRoot) {
		string path = DriveCfgPath(installRoot);
		try {
			if (!File.Exists(path) || new DriveTransformer().Parse(File.ReadAllBytes(path))?.DiscImage is not { } image) {
				return null;
			}

			return Path.GetFullPath(Path.Combine(installRoot, image));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
				or NotSupportedException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); the install has no disc image.");
			return null;
		}
	}

	/// <summary>
	/// Opens the install's disc: the image <see cref="DiscImagePath"/> names when there is one and it opens, and
	/// otherwise the directory <see cref="DiscDirectory"/> names when it exists; null when neither does. An image
	/// that fails to open is reported and passed over for the directory. Reading an image is this engine's own;
	/// retail reads only the directory (docs/retail/formats/vol-archive.md, "Which archives are mounted").
	/// </summary>
	public static GameDisc? OpenDisc(string installRoot) {
		if (DiscImagePath(installRoot) is { } imagePath) {
			try {
				return GameDisc.OpenImage(imagePath);
			} catch (Exception ex) when (ex is DiscFormatException or IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"The disc image {imagePath} cannot be read ({ex.Message}); using the disc folder instead.");
			}
		}

		return DiscDirectory(installRoot) is { } directory && Directory.Exists(directory)
			? GameDisc.OpenFolder(directory)
			: null;
	}

	/// <summary>
	/// Why <paramref name="path"/> cannot be the install's disc image, or null when it can: it must open as a CD
	/// image with an ISO 9660 file system holding archives (<c>VOL\*.vol</c>) and the movie the shell's startup
	/// looks for, <paramref name="discCheckFile"/>. <paramref name="detail"/> carries the reader's own account of
	/// a failure to open.
	/// </summary>
	public static DiscImageProblem? CheckDiscImage(string path, string discCheckFile, out string? detail) {
		detail = null;
		if (!File.Exists(path)) {
			return DiscImageProblem.Missing;
		}

		try {
			using var disc = GameDisc.OpenImage(path);
			return disc.ArchiveNames().Count > 0 && disc.FileExists(discCheckFile) ? null : DiscImageProblem.NotEarthsiege2;
		} catch (Exception ex) when (ex is DiscFormatException or IOException or UnauthorizedAccessException) {
			detail = ex.Message;
			return DiscImageProblem.Unreadable;
		}
	}

	/// <summary>
	/// Writes <paramref name="imagePath"/> into the install's <c>data\drive.cfg</c> as its disc image, or takes the
	/// image out with null, keeping the file's two retail lines. A file without them gets <c>.</c>, the install
	/// itself, as its disc, and <paramref name="installRoot"/> as its install, so that retail's two reads find two
	/// tokens before the image's line.
	/// </summary>
	public static void WriteDiscImage(string installRoot, string? imagePath) {
		string path = DriveCfgPath(installRoot);
		var transformer = new DriveTransformer();
		var drive = File.Exists(path) ? transformer.Parse(File.ReadAllBytes(path)) ?? new Drive() : new Drive();
		drive.DiscImage = imagePath == null ? null : Path.GetFullPath(imagePath);
		if (drive.DiscImage != null) {
			drive.Directory ??= ".";
			drive.InstallDirectory ??= Path.GetFullPath(installRoot);
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, transformer.Write(drive)!);
	}

	/// <summary>
	/// Opens a file both programs read from the disc — a movie, the on-line manual, a training instructor
	/// clip: <paramref name="relativePath"/> on <paramref name="disc"/> when it is there, and otherwise under the
	/// install root; null when neither has it.
	///
	/// <para>Retail looks on the disc only (<c>DriveCfg_PrefixPath</c>, <c>0045ee44</c>;
	/// <c>Path_UnderDriveCfg</c>, VSHELL <c>0040d429</c>). Falling back to the install is this engine's,
	/// so an install copied whole, whose <c>drive.cfg</c> still names a CD drive that is gone, keeps its
	/// movies, manual and instructor; see KNOWN_ISSUES.md.</para>
	/// </summary>
	public static Stream? OpenDiscFile(string installRoot, GameDisc? disc, string relativePath) {
		if (disc?.OpenRead(relativePath) is { } onDisc) {
			return onDisc;
		}

		string inInstall = Path.Combine(installRoot, relativePath);
		return File.Exists(inInstall) ? File.OpenRead(inInstall) : null;
	}

	/// <summary>Whether <see cref="OpenDiscFile"/> would find <paramref name="relativePath"/>.</summary>
	public static bool DiscFileExists(string installRoot, GameDisc? disc, string relativePath) =>
		disc?.FileExists(relativePath) == true || File.Exists(Path.Combine(installRoot, relativePath));

	/// <summary>Whether <paramref name="relativePath"/> names a folder on <paramref name="disc"/> or under the install root.</summary>
	public static bool DiscFolderExists(string installRoot, GameDisc? disc, string relativePath) =>
		disc?.DirectoryExists(relativePath) == true || Directory.Exists(Path.Combine(installRoot, relativePath));

	/// <summary>
	/// <see cref="OpenDiscFile"/>'s file read whole, or null when neither place has it or it is longer than
	/// <paramref name="maxBytes"/>, which is checked before anything is allocated.
	/// </summary>
	public static byte[]? ReadDiscFile(string installRoot, GameDisc? disc, string relativePath, long maxBytes) {
		using var stream = OpenDiscFile(installRoot, disc, relativePath);
		if (stream == null || stream.Length > maxBytes) {
			return null;
		}

		var bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		return bytes;
	}

	/// <summary>The install's <c>data\drive.cfg</c>.</summary>
	public static string DriveCfgPath(string installRoot) =>
		Path.Combine(installRoot, MissionLoader.DataFolderName, Drive.FileName);

	/// <summary>The install's <c>data\sound.cfg</c>, which both executables read (<see cref="SoundCfg"/>).</summary>
	public static string SoundCfgPath(string installRoot) =>
		Path.Combine(installRoot, MissionLoader.DataFolderName, SoundCfg.FileName);

	/// <summary><c>language.cfg</c>'s name (<see cref="HercWorks.Core.Data.File.Cfg.Language"/>).</summary>
	public const string LanguageCfgName = "LANGUAGE.CFG";

	/// <summary>The install's <c>data\language.cfg</c>.</summary>
	public static string LanguageCfgPath(string installRoot) =>
		Path.Combine(installRoot, MissionLoader.DataFolderName, LanguageCfgName);

	/// <summary>
	/// The first byte of the install's <c>data\language.cfg</c>, the one byte every retail reader takes; null when the
	/// file is missing, empty or unreadable.
	/// </summary>
	public static byte? ReadLanguageLetter(string installRoot) {
		try {
			using var file = File.OpenRead(LanguageCfgPath(installRoot));
			int letter = file.ReadByte();
			return letter < 0 ? null : (byte)letter;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary>
	/// Writes <paramref name="language"/>'s letter as the install's whole <c>data\language.cfg</c>, as the installer's
	/// <c>BATCH.EXE</c> does (<see cref="RetailInstaller.Install"/>).
	/// </summary>
	public static void WriteLanguage(string installRoot, RetailInstaller.Language language) {
		string path = LanguageCfgPath(installRoot);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, [(byte)language]);
	}

	/// <summary>Why a file cannot be the install's disc image (<see cref="CheckDiscImage"/>).</summary>
	public enum DiscImageProblem {
		Missing,
		Unreadable,
		NotEarthsiege2,
	}

	/// <summary>Why a directory cannot go into <c>drive.cfg</c> (<see cref="CheckDiscDirectory"/>).</summary>
	public enum DiscDirectoryProblem {
		Missing,
		Whitespace,
		NotLatin1,
	}

	private static string? LoadRemembered() {
		string path = RememberedPathFile;
		try {
			return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); searching for the install instead.");
			return null;
		}
	}
}
