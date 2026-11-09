using HercWorks.Core.Io.Transform;

namespace Herculan.Engine.Install;

/// <summary>
/// The menu bar's "Switch to" (<see cref="Menus.GameMenu"/>): the game asked for, which the shell's turn ends for. Each
/// game's launcher checks it after a shell turn as it checks <see cref="Platform.TerminationSignal"/>, and ends its loop
/// while it is set; the host then takes it and runs that game. The saved start-up game (<see cref="StartupGame"/>) is
/// not touched. This engine's own; retail has no counterpart.
/// </summary>
public static class GameSwitch {
	/// <summary>The game asked for, or null when none is.</summary>
	public static RetailGame? Requested { get; private set; }

	/// <summary>Asks for <paramref name="game"/>.</summary>
	public static void Request(RetailGame game) => Requested = game;

	/// <summary>The game asked for, which is then no longer asked for, or null when none was.</summary>
	public static RetailGame? Take() {
		var game = Requested;
		Requested = null;
		return game;
	}
}
