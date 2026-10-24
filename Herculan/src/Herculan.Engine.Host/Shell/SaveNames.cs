using Herculan.Engine.Content;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Settings;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The save names this engine writes for the player, none of them retail's, each the pilot's name, the sector and the
/// mission number of the game being saved (<see cref="ShellSaveScreen.AutoName"/>) in the interface's language: the name
/// SAVE fills its row with when clicked with a gamepad or under <see cref="TweakSettingDefinitions.AutoFillSaveName"/>,
/// and the auto-save a debrief writes under <see cref="TweakSettingDefinitions.AutoSaveSlot10"/>.
/// </summary>
sealed class SaveNames {
	/// <summary>
	/// The row the debrief's auto-save writes: the tenth, which reads <c>10.</c>. Retail's own slot 10 is the hidden
	/// current-game autosave, a different slot.
	/// </summary>
	public const int AutoSaveRow = ShellSaveScreen.RowCount - 1;

	private readonly GameInProgress _game;
	private readonly ShellSaveScreen _saveScreen;
	private readonly ShellCanvas _canvas;
	private readonly LocalizationTable _localization;
	private readonly Func<bool> _padClicked;

	/// <param name="padClicked">Whether the click being handled was made with a gamepad.</param>
	public SaveNames(GameInProgress game, ShellSaveScreen saveScreen, ShellCanvas canvas, LocalizationTable localization,
			Func<bool> padClicked) {
		_game = game;
		_saveScreen = saveScreen;
		_canvas = canvas;
		_localization = localization;
		_padClicked = padClicked;
	}

	/// <summary>The screen font the rows measure their text with.</summary>
	public HudFont? Font => _canvas.Art.Sprites?.Font(ShellArt.ScreenFont);

	/// <summary>
	/// The name SAVE starts its row with: the game's own when SAVE was clicked with a gamepad or the tweak is on, else
	/// null, retail's empty row.
	/// </summary>
	public string? RenameFill() =>
		_padClicked() || TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.AutoFillSaveName) ? GameName() : null;

	/// <summary>
	/// The debrief's auto-save under the tweak: the game in progress written to <see cref="AutoSaveRow"/> as ACCEPT
	/// writes a row, named <c>AUTO SAVE</c> and the game's own name.
	/// </summary>
	public void AutoSaveAfterDebrief() {
		if (!TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.AutoSaveSlot10) || GameName() is not { } name) {
			return;
		}

		string label = _saveScreen.RowLabel(AutoSaveRow,
			string.Format(_localization.GetStringOrKey("save.auto_save_name"), name), Font);
		_game.SaveNamed(AutoSaveRow, label);
	}

	private string? GameName() =>
		ShellSaveSummary.From(_game.HeldGame()) is { } summary
			? ShellSaveScreen.AutoName(summary, _canvas.Art.Text, _localization.GetStringOrKey("save.auto_name"))
			: null;
}
