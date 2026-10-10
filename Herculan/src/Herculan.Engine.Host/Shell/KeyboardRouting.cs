using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Shell;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// Where a key goes: [Esc] to HERCULAN's own menu bar wherever retail has no use for the key, the display keys
/// MainWndProc handles itself, and the rest to the edit fields.
/// </summary>
sealed class KeyboardRouting {
	private readonly FrontEndWindow _window;
	private readonly ShellMovies _movies;
	private readonly MissionTabScreens _mission;
	private readonly ShellPointer _pointer;
	private readonly StartupScreen _startup;
	private readonly GameInProgress _game;
	private readonly MainMenuPanels _menu;
	private readonly EditFields _fields;
	private readonly Action _repaint;

	public KeyboardRouting(FrontEndWindow window, ShellMovies movies, MissionTabScreens mission, ShellPointer pointer,
			StartupScreen startup, GameInProgress game, MainMenuPanels menu, EditFields fields, Action repaint) {
		_window = window;
		_movies = movies;
		_mission = mission;
		_pointer = pointer;
		_startup = startup;
		_game = game;
		_menu = menu;
		_fields = fields;
		_repaint = repaint;
	}

	public void KeyDown(Key key) {
		// HERCULAN's own menu bar, which [Esc] raises wherever retail has no use for the key (EscapeIsRetails).
		if (key == Key.Escape && !EscapeIsRetails()) {
			_window.MenuBarEscape();
			return;
		}

		DisplayHotkey(key, released: false);
		_fields.DeliverKey(key, released: false);
	}

	public void KeyUp(Key key) {
		DisplayHotkey(key, released: true);
		if (key != Key.Escape || EscapeIsRetails()) {
			_fields.DeliverKey(key, released: true);
		}
	}

	// Whether [Esc] is retail's (docs/retail/shell/main-menu.md#typing-into-a-row): a movie and the briefing map's
	// intro skip on it, a field being typed into takes it, and with Alt or Ctrl it leaves full screen.
	// Retail also hands it to an edit field that is merely under the pointer, which runs the field's
	// handler and so selects a save row; here the menu bar takes it instead (KNOWN_ISSUES.md).
	private bool EscapeIsRetails() {
		var keyboard = _window.Keyboard;
		return _movies.Active || _mission.IntroUp() != null || _pointer.Focused != null
			|| keyboard != null && (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight)
				|| keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
	}

	// MainWndProc (00404a2c)'s display keys (docs/retail/shell/main-menu.md#full-screen-asks-first), each gated
	// on no movie playing and the startup sequence being over. Alt+Enter toggles full screen on the Enter key's release;
	// Alt+Tab, Alt+Esc and Ctrl+Esc leave it on either edge (Display_LeaveFullScreen, 0040722e), Alt+Tab left out
	// here at the user's request so switching away keeps full screen (EngineWindow.ToggleFullScreen). Each then writes option 6
	// from the window and, with the preferences screen up, relights its display group; otherwise it
	// commits the options without their handlers and writes all 54. Alt+Enter skips the gate here at the user's request,
	// so it works during the intro movies and the startup sequence too (KNOWN_ISSUES.md).
	private void DisplayHotkey(Key key, bool released) {
		var keyboard = _window.Keyboard;
		if (keyboard == null) {
			return;
		}

		bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
		bool alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
		bool ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
		bool altOnly = alt && !shift && !ctrl;
		bool altEnter = altOnly && key is Key.Enter or Key.KeypadEnter;
		if (!altEnter && (_startup.Running || _movies.Playing)) {
			return;
		}

		if (altEnter) {
			if (!released) {
				return;
			}

			_window.ToggleFullScreen();
		} else if ((altOnly && key == Key.Escape) || (ctrl && !alt && !shift && key == Key.Escape)) {
			if (_window.FullScreen) {
				_window.ToggleFullScreen();
			}
		} else {
			return;
		}

		var options = _game.Options;
		options.Set(Prefs.DisplayModeOption, (byte)(_window.FullScreen ? 1 : 0));
		if (_menu.PreferencesUp) {
			_repaint();
		} else {
			options.Commit(apply: false);
			options.Save(Enumerable.Range(0, Prefs.Length).ToArray());
		}
	}
}
