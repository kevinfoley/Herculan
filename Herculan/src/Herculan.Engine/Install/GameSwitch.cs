using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Install;

/// <summary>
/// The menu bar's "Switch to" (<see cref="Menus.GameMenu"/>): the game asked for, which the shell's turn ends for. Each
/// game's launcher checks it after a shell turn as it checks <see cref="Platform.TerminationSignal"/>, and ends its loop
/// while it is set; the host then takes it and runs that game. The saved start-up game (<see cref="StartupGame"/>) is
/// not touched. A game's Settings window asks the same way for its own game on another install, or on the same one to
/// start it again (<see cref="Install"/>). This engine's own; retail has no counterpart.
/// </summary>
public static class GameSwitch {
	/// <summary>The game asked for, or null when none is.</summary>
	public static RetailGame? Requested { get; private set; }

	/// <summary>The install the game is asked for on, or null to find its install as at start-up.</summary>
	public static string? Install { get; private set; }

	/// <summary>Asks for <paramref name="game"/>, on <paramref name="install"/> when one is given.</summary>
	public static void Request(RetailGame game, string? install = null) {
		Requested = game;
		Install = install;
	}

	/// <summary>The game asked for and its install, which are then no longer asked for, or null when none was.</summary>
	public static (RetailGame Game, string? Install)? Take() {
		if (Requested is not { } game) {
			return null;
		}

		string? install = Install;
		Requested = null;
		Install = null;
		return (game, install);
	}
}
