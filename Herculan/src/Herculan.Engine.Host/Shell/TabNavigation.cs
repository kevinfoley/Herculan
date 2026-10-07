using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>Which tab is up, and how one comes up: a tab click, a handler putting the frame or the mission tab up, and the palette each is drawn through.</summary>
sealed class TabNavigation {
	private readonly ShellScreen _screen;
	private readonly ShellCanvas _canvas;
	private readonly GameInProgress _game;
	private readonly ShellSaveScreen _saveScreen;
	private readonly HangarTabs _hangar;
	private readonly MissionTabScreens _mission;
	private readonly ShellAudio _audio;
	private readonly Action _repaint;

	public TabNavigation(ShellScreen screen, ShellCanvas canvas, GameInProgress game, ShellSaveScreen saveScreen, HangarTabs hangar,
			MissionTabScreens mission, ShellAudio audio, WidgetEvents widgets, Action repaint) {
		_screen = screen;
		_canvas = canvas;
		_game = game;
		_saveScreen = saveScreen;
		_hangar = hangar;
		_mission = mission;
		_audio = audio;
		_repaint = repaint;

		widgets.Handle(ShellWidgetKind.StripButton, widget => Activate(widget.Index));
	}

	/// <summary>
	/// What a tab's entry does beyond showing it. The mission tab's map view sets the campaign map's
	/// first-show flag (Mission_Show, 004441e3), so the next visit opens the briefing.
	/// </summary>
	public void EnterTab(int id) {
		if (_hangar.Enter(id)) {
			return;
		}

		if (id == ShellScreen.MissionTab) {
			_mission.Enter();
		} else if (id == ShellScreen.SaveTab) {
			_saveScreen.Enter();
		}
	}

	/// <summary>
	/// Mission_ShowView(MissionScreenView, 1) (0043a857): the mission tab put up in its view, with no tab
	/// click, and then the left press it posts at MISSION, which lights the tab and makes the press sound —
	/// its handler finds tab 7 already current and does nothing more.
	/// </summary>
	public void ShowMissionView() {
		_screen.SelectTab(ShellScreen.MissionTab);
		SwitchPalette(ShellScreen.MissionTab);
		EnterTab(ShellScreen.MissionTab);
		_repaint();
		_audio.Sound?.PlayPress();
	}

	/// <summary>The frame back up, its strip regated for the campaign mode.</summary>
	public void ReturnToFrame() {
		_screen.ReturnToFrame(_game.Mode);
		_repaint();
	}

	/// <summary>The palette a tab is drawn through, from the mission tab's view and the campaign stage.</summary>
	public string PaletteFor(int tab) => _canvas.PaletteFor(tab, _mission.View(), _game.CampaignStage);

	// A strip button's handler.
	private void Activate(int id) {
		if (id == ShellScreen.MenuButtonId) {
			return;
		}

		// Clicking the tab you are already on is a no-op in the original: every handler returns
		// early when CurrentTabIndex (0047581c) already holds its own index, before the teardown, the palette and
		// the click sound.
		if (id == _screen.SelectedTab) {
			return;
		}

		// Tab 0's handler autosaves before it builds the menu; tab 1's writes where EXIT goes before it
		// enters the screen.
		if (id == ShellScreen.MainMenuTab) {
			_game.AutoSave();
		} else if (id == ShellScreen.SaveTab) {
			_saveScreen.ExitTarget = ShellSaveExitTarget.TabStrip;
		}

		_mission.TabClicked();
		_screen.SelectTab(id);

		SwitchPalette(id);
		EnterTab(id);
		_repaint();

		// The handler's last act, after the screen is up (ShellSound_PlayTabClick, 0042ee89).
		_audio.Sound?.PlayTabClick();
	}

	// The original writes an index into the palette widget and shows it; here the whole of the art
	// is decoded through one palette at load, so a change means loading it again and rebuilding the
	// renderer's textures. That is a few milliseconds on a click, and it happens only when the
	// palette actually changes.
	private void SwitchPalette(int tab) => _canvas.LoadPalette(PaletteFor(tab));
}
