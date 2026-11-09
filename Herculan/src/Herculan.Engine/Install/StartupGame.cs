using Herculan.Engine.Platform;
using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Install;

/// <summary>
/// The game a launch with no install named starts without asking, which the start-up chooser's "Don't ask again" saves
/// (<see cref="GameChooser"/>): <c>startup-game.txt</c> in <see cref="UserConfig.Folder"/>, one line, the
/// <see cref="RetailGame"/> member's name. No file, or one that names no game, means the chooser asks. This engine's
/// own; retail has no counterpart.
/// </summary>
public static class StartupGame {
	/// <summary>The file.</summary>
	public static string FilePath => UserConfig.PathOf("startup-game.txt");

	/// <summary>The saved game, or null when the chooser asks.</summary>
	public static RetailGame? Load() {
		string path = FilePath;
		try {
			return File.Exists(path) && Enum.TryParse(File.ReadAllText(path).Trim(), out RetailGame game) && Enum.IsDefined(game)
				? game
				: null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); asking which game to start.");
			return null;
		}
	}

	/// <summary>
	/// Saves <paramref name="game"/> as the game to start without asking, or with null has the chooser ask again. A failed
	/// write or delete is logged and otherwise ignored.
	/// </summary>
	public static void Save(RetailGame? game) {
		if (game is { } saved) {
			UserConfig.Write(FilePath, saved.ToString());
			return;
		}

		try {
			File.Delete(FilePath);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not delete {FilePath}: {ex.Message}");
		}
	}
}
