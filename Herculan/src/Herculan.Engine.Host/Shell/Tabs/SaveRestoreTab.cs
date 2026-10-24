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
	private readonly SaveNames _names;
	private readonly Action _repaint;

	public SaveRestoreTab(ShellSaveScreen saveScreen, GameInProgress game, CampaignScreens loop, MissionTabScreens mission,
			TabNavigation navigation, ShellScreen screen, WidgetEvents widgets, SaveNames names, Action repaint) {
		_saveScreen = saveScreen;
		_game = game;
		_loop = loop;
		_mission = mission;
		_navigation = navigation;
		_screen = screen;
		_widgets = widgets;
		_names = names;
		_repaint = repaint;

		widgets.Handle(ShellWidgetKind.SaveRow, widget => SelectSaveSlot(widget.Index));
		widgets.Handle(ShellWidgetKind.SaveButton, widget => ClickSaveButton((ShellSaveButton)widget.Index));
	}

	public ShellHit? HitAt(float canvasX, float canvasY) => _saveScreen.HitAt(canvasX, canvasY);

	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, int? focusedRow, ShellWidget? lit) =>
		_saveScreen.Paint(surface, text, sprites, focusedRow, lit);

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

	/// <summary>
	/// A gamepad's A: a click on ACCEPT while a rename is live on this tab, wherever the pointer is. This engine's own, so a
	/// name SAVE filled in for a gamepad click is accepted without steering to the button.
	/// </summary>
	public void PadAccept() => PadClick(ShellSaveButton.Accept);

	/// <summary>A gamepad's B: a click on CANCEL while a rename is live on this tab, wherever the pointer is, as <see cref="PadAccept"/>.</summary>
	public void PadCancel() => PadClick(ShellSaveButton.Cancel);

	private void PadClick(ShellSaveButton button) {
		if (_screen.SelectedTab != ShellScreen.SaveTab || !_saveScreen.IsEnabled(button)) {
			return;
		}

		var rect = _saveScreen.ButtonRect(button);
		_widgets.Click(ShellHit.Button(new ShellWidget(ShellWidgetKind.SaveButton, (int)button), rect,
			(rect.X0 + rect.X1) / 2f, (rect.Y0 + rect.Y1) / 2f));
	}

	// SAVE, SaveScreen_OnSave (00437bd3): the rename starts, and the pointer is taken onto the row so
	// that keystrokes reach it. The row starts with the game's name where SaveNames gives one.
	private void BeginRename() {
		int slot = _saveScreen.SelectedSlot;
		_saveScreen.BeginRename(_names.RenameFill(), _names.Font);
		_widgets.Grab(new ShellHit(new ShellWidget(ShellWidgetKind.SaveRow, slot), ShellHandler.EditField));
		_repaint();
	}

	// ACCEPT, SaveScreen_OnAccept (00437ffa): the row's string saved and the slot's summary staged.
	private void AcceptRename() {
		int slot = _saveScreen.SelectedSlot;
		_game.SaveNamed(slot, _saveScreen.RowText(slot));
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
