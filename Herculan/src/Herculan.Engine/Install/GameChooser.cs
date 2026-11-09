using System.Numerics;
using Herculan.Engine.Localization;
using HercWorks.Core.Io.Transform;
using ImGuiNET;

namespace Herculan.Engine.Install;

/// <summary>
/// The window a launch with no install named opens first, unless a game was saved to start without asking
/// (<see cref="StartupGame"/>): a button for each game (<see cref="RetailInstalls.Games"/>), with the folder its install
/// was found in or a line saying none was, "Don't ask again", and Quit at the foot. Choosing a game whose install was not
/// found is the host's to answer, with that game's install prompt. This engine's own; retail has no counterpart.
/// </summary>
public sealed class GameChooser {
	public const int WindowWidth = 640;
	public const int WindowHeight = 340;

	/// <summary>What one frame of the chooser left it at.</summary>
	public enum Outcome {
		Open,
		Chosen,
		Quit,
	}

	private readonly LocalizationTable _localization;
	private readonly IReadOnlyDictionary<RetailGame, string?> _found;
	private bool _dontAskAgain;

	/// <param name="found">Each game's install root as found at start-up, or null where none was.</param>
	/// <param name="dontAskAgain">Whether "Don't ask again" starts ticked, as it is when the player comes back to the chooser.</param>
	public GameChooser(LocalizationTable localization, IReadOnlyDictionary<RetailGame, string?> found, bool dontAskAgain = false) {
		_localization = localization;
		_found = found;
		_dontAskAgain = dontAskAgain;
	}

	/// <summary>The game chosen; set once <see cref="Draw"/> has returned <see cref="Outcome.Chosen"/>.</summary>
	public RetailGame? Chosen { get; private set; }

	/// <summary>Whether "Don't ask again" is ticked.</summary>
	public bool DontAskAgain => _dontAskAgain;

	/// <summary>The string of <paramref name="game"/>'s name.</summary>
	public static string NameKey(RetailGame game) => game == RetailGame.Earthsiege1 ? "game.earthsiege1" : "game.earthsiege2";

	/// <summary>Draws the chooser into the current window, filling it to its foot.</summary>
	public Outcome Draw() {
		var outcome = Outcome.Open;
		ImGui.TextWrapped(_localization.GetStringOrKey("chooser.message"));
		ImGui.Spacing();

		var style = ImGui.GetStyle();
		var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight() + style.FramePadding.Y * 2f);
		foreach (var game in RetailInstalls.Games) {
			ImGui.PushID((int)game);
			if (ImGui.Button(_localization.GetStringOrKey(NameKey(game)), buttonSize)) {
				Chosen = game;
				outcome = Outcome.Chosen;
			}

			ImGui.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
			ImGui.TextWrapped(_found.GetValueOrDefault(game) ?? _localization.GetStringOrKey("chooser.not_found"));
			ImGui.PopStyleColor();
			ImGui.PopID();
			ImGui.Spacing();
		}

		ImGui.Checkbox(_localization.GetStringOrKey("chooser.dont_ask"), ref _dontAskAgain);

		// Quit sits at the foot of the window, or under the content when that runs past the foot.
		string quitLabel = _localization.GetStringOrKey("install_prompt.quit");
		var row = new ButtonRow([ButtonRow.LabelWidth(quitLabel)]);
		float foot = ImGui.GetWindowHeight() - row.Height - style.WindowPadding.Y;
		if (ImGui.GetCursorPosY() < foot) {
			ImGui.SetCursorPosY(foot);
		}
		if (row.Button(quitLabel)) {
			outcome = Outcome.Quit;
		}

		return outcome;
	}

	/// <summary>
	/// Shows the chooser in a window of its own (<see cref="StartupWindow"/>) until the player chooses a game or quits.
	/// Returns the game, or null when the player chose Quit or closed the window, and whether "Don't ask again" was ticked.
	/// </summary>
	public static (RetailGame? Game, bool DontAskAgain) Run(LocalizationTable localization, string fontPath,
			IReadOnlyDictionary<RetailGame, string?> found, bool dontAskAgain = false) {
		var chooser = new GameChooser(localization, found, dontAskAgain);
		var outcome = Outcome.Open;
		StartupWindow.Run(localization, "chooser.window_title", WindowWidth, WindowHeight, fontPath,
			_ => (outcome = chooser.Draw()) != Outcome.Open);
		return (outcome == Outcome.Chosen ? chooser.Chosen : null, chooser.DontAskAgain);
	}
}
