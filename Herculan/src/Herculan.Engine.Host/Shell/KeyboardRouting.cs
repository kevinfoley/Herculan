using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Input;
using Herculan.Engine.Shell;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// Where a key goes: [Shift+Esc] to HERCULAN's own menu bar, as in a mission, and plain [Esc] too while the bar is
/// up, to back out of it; the display keys MainWndProc handles itself; and the rest to the edit fields.
/// </summary>
sealed class KeyboardRouting {
	private readonly FrontEndWindow _window;
	private readonly ShellMovies _movies;
	private readonly StartupScreen _startup;
	private readonly GameInProgress _game;
	private readonly MainMenuPanels _menu;
	private readonly EditFields _fields;
	private readonly Action _repaint;
	private readonly KeyMap _keyMap;

	// Whether the [Esc] held now went to the menu bar, which its release follows, and which key that was.
	private bool _escapeIsMenuBars;
	private Key _menuBarKey;

	public KeyboardRouting(FrontEndWindow window, ShellMovies movies, StartupScreen startup, GameInProgress game,
			MainMenuPanels menu, EditFields fields, Action repaint, KeyMap keyMap) {
		_window = window;
		_movies = movies;
		_startup = startup;
		_game = game;
		_menu = menu;
		_fields = fields;
		_repaint = repaint;
		_keyMap = keyMap;
	}

	public void KeyDown(Key key) {
		bool menuBarKey = key == _keyMap.KeyOf(KeyAction.MenuBar);
		bool menuBackKey = key == _keyMap.KeyOf(KeyAction.MenuBack);
		if (menuBarKey || menuBackKey) {
			_escapeIsMenuBars = MenuBarTakesEscape(menuBarKey, menuBackKey);
			_menuBarKey = key;
			if (_escapeIsMenuBars) {
				_window.MenuBarEscape();
				return;
			}
		}

		DisplayHotkey(key, released: false);
		_fields.DeliverKey(key, released: false);
	}

	/// <summary>
	/// An auto-repeat of the key held last, which retail takes as a fresh key-down: the display keys and the edit fields
	/// see it again. A key the menu bar took stays the bar's while it is held, so its repeats reach nothing.
	/// </summary>
	public void KeyRepeat(Key key) {
		if (key == _menuBarKey && _escapeIsMenuBars) {
			return;
		}

		DisplayHotkey(key, released: false);
		_fields.DeliverKey(key, released: false);
	}

	public void KeyUp(Key key) {
		DisplayHotkey(key, released: true);
		if (key != _menuBarKey || !_escapeIsMenuBars) {
			_fields.DeliverKey(key, released: true);
		}
	}

	/// <summary>
	/// Whether the [Esc] held now is the menu bar's, so that nothing of retail's sees it — read by the movie and
	/// briefing-intro skip, which poll the key rather than taking its events (<see cref="PolledButtons"/>).
	/// </summary>
	public bool EscapeIsMenuBars => _escapeIsMenuBars && _window.Keyboard?.IsKeyPressed(_menuBarKey) == true;

	// [Shift+Esc] alone, or any unmodified [Esc] while the bar is up, is the menu bar's, wherever the shell is; every
	// other [Esc] is retail's (docs/retail/shell/main-menu.md#typing-into-a-row): a movie and the briefing map's intro
	// skip on it, an edit field under the pointer takes it, and with Alt or Ctrl it leaves full screen. A press the
	// bar takes is the bar's for as long as it is held, its release included, so it never does both.
	private bool MenuBarTakesEscape(bool menuBarKey, bool menuBackKey) {
		var keyboard = _window.Keyboard;
		bool shift = keyboard != null && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
		bool alt = keyboard != null && (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight));
		bool ctrl = keyboard != null && (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
		return !alt && !ctrl && ((menuBarKey && shift) || (menuBackKey && _window.MenuBarUp));
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
		var leaveKeys = _keyMap.Chords(KeyAction.LeaveFullScreen);
		bool altEnter = altOnly && _keyMap.Chords(KeyAction.FullScreen).Any(chord => chord.Key == key);
		if (!altEnter && (_startup.Running || _movies.Playing)) {
			return;
		}

		if (altEnter) {
			if (!released) {
				return;
			}

			_window.ToggleFullScreen();
		} else if ((altOnly && key == leaveKeys[0].Key) || (ctrl && !alt && !shift && key == leaveKeys[1].Key)) {
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
