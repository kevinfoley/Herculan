using Herculan.Engine.Install;
using Herculan.Engine.Localization;
using HercWorks.Core.Io.Transform;
using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// The menu bar's "Game" menu over a game's shell (<see cref="HostMenuBar"/>): "Switch to" each other game
/// (<see cref="RetailInstalls.Games"/>), which <c>switchTo</c> answers by ending the shell's turn for it
/// (<see cref="GameSwitch"/>), and "Ask at start-up", ticked while no game is saved to start without asking
/// (<see cref="StartupGame"/>). Clearing the tick saves the game running now; ticking it deletes the saved game. A switch
/// leaves the saved game as it is. This engine's own; retail has no counterpart.
/// </summary>
/// <param name="current">The game whose shell the bar is over.</param>
/// <param name="switchTo">Ends the shell's turn for the game chosen, from inside the bar's ImGui frame.</param>
public sealed class GameMenu(LocalizationTable localization, RetailGame current, Action<RetailGame> switchTo) {
	// Read once for the turn; this menu is what changes it meanwhile.
	private bool _askAtStartup = StartupGame.Load() == null;

	/// <summary>The menu's items, drawn inside its open <c>BeginMenu</c>.</summary>
	public void DrawItems() {
		foreach (var game in RetailInstalls.Games) {
			if (game == current) {
				continue;
			}

			string label = string.Format(localization.GetStringOrKey("menu.switch_game"),
				localization.GetStringOrKey(GameChooser.NameKey(game)));
			if (ImGui.MenuItem(label)) {
				switchTo(game);
			}
		}

		ImGui.Separator();
		if (ImGui.MenuItem(localization.GetStringOrKey("menu.ask_at_startup"), null, _askAtStartup)) {
			_askAtStartup = !_askAtStartup;
			StartupGame.Save(_askAtStartup ? null : current);
		}
	}
}
