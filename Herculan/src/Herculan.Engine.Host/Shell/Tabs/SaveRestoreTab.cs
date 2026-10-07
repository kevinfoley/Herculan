using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell.Tabs;

/// <summary>The SAVE/RESTORE tab's handlers: a row's selection, the rename SAVE starts, RESTORE and EXIT.</summary>
sealed class SaveRestoreTab {
	private readonly ShellSaveScreen _saveScreen;
	private readonly GameInProgress _game;
	private readonly CampaignScreens _loop;
	private readonly MissionTabScreens _mission;
	private readonly TabNavigation _navigation;
	private readonly ShellScreen _screen;
	private readonly WidgetEvents _widgets;
	private readonly Action _repaint;

	public SaveRestoreTab(ShellSaveScreen saveScreen, GameInProgress game, CampaignScreens loop, MissionTabScreens mission,
			TabNavigation navigation, ShellScreen screen, WidgetEvents widgets, Action repaint) {
		_saveScreen = saveScreen;
		_game = game;
		_loop = loop;
		_mission = mission;
		_navigation = navigation;
		_screen = screen;
		_widgets = widgets;
		_repaint = repaint;

		widgets.Handle(ShellWidgetKind.SaveRow, widget => SelectSaveSlot(widget.Index));
		widgets.Handle(ShellWidgetKind.SaveButton, widget => ClickSaveButton((ShellSaveButton)widget.Index));
	}

	public ShellHit? HitAt(float canvasX, float canvasY) => _saveScreen.HitAt(canvasX, canvasY);

	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, int? focusedRow) =>
		_saveScreen.Paint(surface, text, sprites, focusedRow);

	// A save row's handler, SaveScreen_SelectSlot (0043795f). Clicking the row already selected is a
	// no-op, the same early return the original's selection move opens with, and so is any row while
	// a rename is live.
	private void SelectSaveSlot(int slot) {
		if (!_saveScreen.SelectSlot(slot)) {
			return;
		}

		_repaint();
	}

	private void ClickSaveButton(ShellSaveButton button) {
		switch (button) {
			case ShellSaveButton.Save:
				BeginRename();
				break;
			case ShellSaveButton.Accept:
				AcceptRename();
				break;
			case ShellSaveButton.Cancel:
				_saveScreen.CancelRename();
				_repaint();
				break;
			case ShellSaveButton.Restore:
				RestoreSelectedSlot();
				break;
			case ShellSaveButton.Exit:
				LeaveSaveScreen();
				break;
		}
	}

	// SAVE, SaveScreen_OnSave (00437bd3): the rename starts, and the pointer is taken onto the row so
	// that keystrokes reach it.
	private void BeginRename() {
		int slot = _saveScreen.SelectedSlot;
		_saveScreen.BeginRename();
		_widgets.Grab(new ShellHit(new ShellWidget(ShellWidgetKind.SaveRow, slot), ShellHandler.EditField));
		_repaint();
	}

	// ACCEPT, SaveScreen_OnAccept (00437ffa): Game_SaveSlot under the row's string, then
	// Stats_StageCurrentGame for the slot's summary.
	private void AcceptRename() {
		int slot = _saveScreen.SelectedSlot;
		string label = _saveScreen.RowText(slot);
		if (_game.Save(slot, label) is { } entry && _game.LoadedGame != null) {
			_saveScreen.SetSlot(slot, entry with { Summary = ShellSaveSummary.From(_game.LoadedGame) });
		}

		_saveScreen.EndRename();
		_repaint();
	}

	// RESTORE, SaveScreen_OnRestore (00437d03): load the selected slot and write it straight back out as
	// the slot-10 autosave, then leave exactly as EXIT does on the tab-strip path, whichever way the
	// screen was entered. It clears the campaign map's first-show flag (DAT_004778aa).
	private void RestoreSelectedSlot() {
		int slot = _saveScreen.SelectedSlot;
		if (!_loop.LoadSlot(slot)) {
			Console.WriteLine($"Slot {slot + 1} could not be read — nothing restored.");
			return;
		}

		_mission.ClearMapShown();
		_game.AutoSave();
		_saveScreen.Leave();
		_navigation.ReturnToFrame();
	}

	// EXIT, SaveScreen_OnExit (00437d94): the teardown, then wherever the handler that entered the
	// screen said to go.
	private void LeaveSaveScreen() {
		_saveScreen.Leave();
		if (_saveScreen.ExitTarget == ShellSaveExitTarget.MainMenu) {
			_screen.SelectTab(ShellScreen.MainMenuTab);
			_repaint();
			return;
		}

		_navigation.ReturnToFrame();
	}
}
