using Herculan.Engine.Localization;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Games;

/// <summary>
/// What the host hands a game's run (<see cref="IGameModule.Run"/>): the install found, the command line's common flags,
/// the application's strings and fonts, the one window every game's turns take, and the games the application runs, which
/// the "Game" menu offers a switch to.
/// </summary>
public sealed class GameRunContext {
	/// <summary>The install the run starts on, which holds the game.</summary>
	public required string InstallRoot { get; init; }

	/// <summary>
	/// Whether the run was switched to from another game's "Game" menu or Settings window rather than started by the
	/// command line, in which case the command line's staging asked the first game for nothing of this one: the game runs
	/// its front end.
	/// </summary>
	public required bool Switched { get; init; }

	public required CommonOptions Options { get; init; }

	public required LocalizationTable Localization { get; init; }

	/// <summary>The font every ImGui window is drawn in.</summary>
	public required string FontPath { get; init; }

	/// <summary>The italic face of <see cref="FontPath"/>'s font.</summary>
	public required string ItalicFontPath { get; init; }

	/// <summary>The window every turn of every game takes in turn, opened by the first call.</summary>
	public required Func<HostWindow> Window { get; init; }

	/// <summary>Every game the application runs, in the order the start-up chooser and the "Game" menu list them.</summary>
	public required IReadOnlyList<GameIdentity> Games { get; init; }
}
