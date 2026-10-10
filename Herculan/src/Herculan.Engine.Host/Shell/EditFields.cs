using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Shell;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The shell's edit fields — the save screen's rows and the names of the registration screen and the DEBUG dialog — and
/// the keys and caret they take.
/// </summary>
sealed class EditFields {
	private readonly FrontEndWindow _window;
	private readonly ShellAudio _audio;
	private readonly ShellMovies _movies;
	private readonly ShellPointer _pointer;
	private readonly ShellScreen _screen;
	private readonly ShellSaveScreen _saveScreen;
	private readonly MainMenuPanels _menu;
	private readonly ShellMissionNameDialog _missionName;
	private readonly ShellCanvas _canvas;
	private readonly WidgetEvents _widgets;
	private readonly Action _repaint;

	// The edit field that had the focus last update, and its blink alarm, installed as a field takes the
	// focus and removed as it loses it.
	private ShellWidget? _caretField;
	private readonly ShellAlarm _caretAlarm = new(Environment.TickCount64);

	public EditFields(FrontEndWindow window, ShellAudio audio, ShellMovies movies, ShellPointer pointer, ShellScreen screen,
			ShellSaveScreen saveScreen, MainMenuPanels menu, ShellMissionNameDialog missionName, ShellCanvas canvas,
			WidgetEvents widgets, Action repaint) {
		_window = window;
		_audio = audio;
		_movies = movies;
		_pointer = pointer;
		_screen = screen;
		_saveScreen = saveScreen;
		_menu = menu;
		_missionName = missionName;
		_canvas = canvas;
		_widgets = widgets;
		_repaint = repaint;
	}

	/// <summary>
	/// A keystroke, delivered as VSHELL's queue delivers one: to the pointer's target, which on the save
	/// screen may be one of its rows (ESDialog_HandleEvent, 0040beaf). The row takes a character or a
	/// command, Enter releases the pointer, and whatever the key, the row's handler then runs, which
	/// selects it. The registration screen's name field takes keys the same way, its handler regating
	/// ACCEPT, and so does the DEBUG dialog's, which has none. Nothing else ported here takes a key. A fade or the movie queue drops keys as it drops
	/// clicks, and a field of the menu bar's windows being typed into takes the key instead.
	/// </summary>
	public void DeliverKey(Key key, bool released) {
		var keyboard = _window.Keyboard;
		if (keyboard == null || _audio.Fading || _movies.Active
				|| _window.ImGuiWantsKeyboard
				|| _pointer.Target?.Widget is not { } row
				|| !(row.Kind == ShellWidgetKind.SaveRow && _screen.SelectedTab == ShellScreen.SaveTab
					|| row.Kind == ShellWidgetKind.RegistrationField && _menu.RegistrationUp
					|| row.Kind == ShellWidgetKind.MissionNameField && _missionName.IsOpen)
				|| ShellKeyboard.Index(key) is not { } index) {
			return;
		}

		bool shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
		bool ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
		bool alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
		if (ShellKeyboard.Event(index, released, shift, ctrl, alt) is not { } shellKey) {
			return;
		}

		bool focused = _pointer.Focused == row;
		bool nameField = row.Kind != ShellWidgetKind.SaveRow;
		var font = _canvas.Art.Sprites?.Font(ShellArt.ScreenFont);
		bool changed = row.Kind switch {
			ShellWidgetKind.RegistrationField => _menu.Registration.Key(shellKey, focused, font),
			ShellWidgetKind.MissionNameField => _missionName.Key(shellKey, focused, font),
			_ => _saveScreen.Key(row.Index, shellKey, focused, font),
		};
		if (shellKey.Command == ShellKey.Enter && focused && (nameField || _saveScreen.CaretEnabled(row.Index))) {
			_pointer.ReleaseFocus();
			changed = true;
		}

		if (changed) {
			_repaint();
		}

		_widgets.Fire(row);
	}

	/// <summary>
	/// The focused edit field's blink alarm (ESDialog_HandleEvent, 0040beaf: WinTimer_InstallAlarm, 500 and
	/// 500), installed as the field takes the focus; one tick per update at most, a late one dropping the
	/// time it overshot (<see cref="ShellAlarm"/>). A change of focus repaints too, as the field's paint on
	/// the press does.
	/// </summary>
	public void BlinkCaret() {
		var focused = _pointer.Focused;
		bool tick = _caretAlarm.Tick(Environment.TickCount64);
		if (focused != _caretField) {
			_caretField = focused;
			if (focused is { Kind: ShellWidgetKind.SaveRow or ShellWidgetKind.RegistrationField or ShellWidgetKind.MissionNameField }) {
				_caretAlarm.Install(ShellSaveScreen.CaretBlinkMilliseconds, ShellSaveScreen.CaretBlinkMilliseconds);
			} else {
				_caretAlarm.Remove();
			}

			if (_screen.SelectedTab == ShellScreen.SaveTab || _menu.RegistrationUp || _missionName.IsOpen) {
				_repaint();
			}

			return;
		}

		if (!tick) {
			return;
		}

		if (focused is { Kind: ShellWidgetKind.SaveRow } row && _screen.SelectedTab == ShellScreen.SaveTab) {
			_saveScreen.CaretTick(row.Index);
		} else if (focused is { Kind: ShellWidgetKind.RegistrationField } && _menu.RegistrationUp) {
			_menu.Registration.CaretTick();
		} else if (focused is { Kind: ShellWidgetKind.MissionNameField } && _missionName.IsOpen) {
			_missionName.CaretTick();
		} else {
			return;
		}

		_repaint();
	}
}
