using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Install;

/// <summary>
/// Finds each game's installation to load data from, and remembers the one last used for each game. The engine never
/// runs the original executables (see docs/herculan/planning.md); it only reads their data files. So "an install" here
/// means a directory containing a <c>VOL</c> folder with the game's archives in it, whose game
/// <see cref="RetailGameDetector"/> tells apart. Retail has no counterpart: each game's installer wrote its own paths.
/// </summary>
public static class RetailInstalls {
	/// <summary>Earthsiege 2's environment variable, which <see cref="Locate"/> checks first.</summary>
	public const string Es2PathVariable = "ES2_GAME_PATH";

	/// <summary>Metaltech: Earthsiege's environment variable, which <see cref="Locate"/> checks first.</summary>
	public const string Es1PathVariable = "ES1_GAME_PATH";

	/// <summary>The games, in the order the start-up chooser lists them.</summary>
	public static IReadOnlyList<RetailGame> Games { get; } = [RetailGame.Earthsiege2, RetailGame.Earthsiege1];

	/// <summary>The environment variable that names <paramref name="game"/>'s install.</summary>
	public static string PathVariable(RetailGame game) => game == RetailGame.Earthsiege1 ? Es1PathVariable : Es2PathVariable;

	/// <summary>
	/// The folder name <see cref="Locate"/> looks for beside the running binary and in each folder above it: <c>ES2</c>
	/// or <c>ES1</c>.
	/// </summary>
	public static string FolderName(RetailGame game) => game == RetailGame.Earthsiege1 ? "ES1" : "ES2";

	/// <summary>
	/// Where <paramref name="game"/>'s last install is remembered, in <see cref="UserConfig.Folder"/>: <c>install-path.txt</c>
	/// for Earthsiege 2 and <c>es1-install-path.txt</c> for Metaltech: Earthsiege. Each holds one line, the path.
	/// </summary>
	public static string RememberedPathFile(RetailGame game) =>
		UserConfig.PathOf(game == RetailGame.Earthsiege1 ? "es1-install-path.txt" : "install-path.txt");

	/// <summary>Whether <paramref name="path"/> is an install root of either game: a directory holding the archive directory.</summary>
	public static bool IsInstallRoot(string path) =>
		Directory.Exists(path) && Directory.Exists(GameContent.ArchiveDirectory(path));

	/// <summary>Whether <paramref name="path"/> is an install root (<see cref="IsInstallRoot"/>) that holds <paramref name="game"/>.</summary>
	public static bool IsInstallOf(string path, RetailGame game) =>
		IsInstallRoot(path) && RetailGameDetector.Detect(path) == game;

	/// <summary>
	/// Resolves <paramref name="game"/>'s install root, in order: an explicit <paramref name="explicitPath"/> (a command
	/// line argument), the game's environment variable (<see cref="PathVariable"/>), the install last used
	/// (<see cref="Remember"/>), then a <see cref="FolderName"/> folder beside the running binary or any folder above it,
	/// which covers a sibling install checked out beside this repo, reached from <c>src/Herculan.Engine.Host/bin/...</c>.
	/// Each must hold that game (<see cref="IsInstallOf"/>). Returns null when nothing matched, so the caller can ask the
	/// player or print something more useful than a stack trace. An explicit path that is not an install is null too,
	/// without trying the rest: a path the player typed is never silently swapped for another.
	/// </summary>
	public static string? Locate(RetailGame game, string? explicitPath = null) {
		if (!string.IsNullOrWhiteSpace(explicitPath)) {
			return IsInstallOf(explicitPath, game) ? Path.GetFullPath(explicitPath) : null;
		}

		string? fromEnvironment = Environment.GetEnvironmentVariable(PathVariable(game));
		if (!string.IsNullOrWhiteSpace(fromEnvironment) && IsInstallOf(fromEnvironment, game)) {
			return Path.GetFullPath(fromEnvironment);
		}

		// A remembered install that has since moved or been deleted falls through to the search.
		if (Remembered(game) is { } remembered && IsInstallOf(remembered, game)) {
			return Path.GetFullPath(remembered);
		}

		// Walk up from the binary looking for a sibling folder — covers running straight out of the repo without any
		// configuration, which is the normal development case.
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
			string candidate = CaseInsensitivePath.Combine(directory.FullName, FolderName(game));
			if (IsInstallOf(candidate, game)) {
				return Path.GetFullPath(candidate);
			}
		}

		return null;
	}

	/// <summary>
	/// Records <paramref name="installRoot"/> as the last install used of the game it holds (<see cref="RetailGameDetector"/>),
	/// which <see cref="Locate"/> tries before searching. A failed write is logged and otherwise ignored: the next launch
	/// searches again.
	/// </summary>
	public static void Remember(string installRoot) =>
		UserConfig.Write(RememberedPathFile(RetailGameDetector.Detect(installRoot)), Path.GetFullPath(installRoot));

	/// <summary>The install last remembered for <paramref name="game"/>, whether or not it is still there; null for none.</summary>
	public static string? Remembered(RetailGame game) {
		string path = RememberedPathFile(game);
		try {
			return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } remembered ? remembered : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); searching for the install instead.");
			return null;
		}
	}

	/// <summary>
	/// Whether <paramref name="path"/> is on a CD or DVD, or a disc image mounted as one: the drive holding it, the
	/// one with the longest root that contains it, reports <see cref="DriveType.CDRom"/>. A disc holds an install's
	/// folders but takes no writes, and both games write their saves into the install, so a disc is never the install.
	/// False when the drives cannot be read.
	/// </summary>
	public static bool IsOnDisc(string path) {
		try {
			string full = Path.GetFullPath(path);
			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			bool Holds(DriveInfo drive) {
				string root = drive.RootDirectory.FullName;
				return full.Equals(root, comparison) || full.StartsWith(Path.EndsInDirectorySeparator(root)
					? root : root + Path.DirectorySeparatorChar, comparison);
			}

			return DriveInfo.GetDrives().Where(Holds).MaxBy(drive => drive.RootDirectory.FullName.Length)?.DriveType == DriveType.CDRom;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
			return false;
		}
	}
}
