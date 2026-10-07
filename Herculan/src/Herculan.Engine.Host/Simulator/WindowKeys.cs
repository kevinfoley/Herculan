using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Host.Settings;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The keys that act on the window rather than the game: [Esc] backing out to the menu bar, [/] for the
/// on-line manual, and the full-screen toggle and its ways out.
/// </summary>
sealed class WindowKeys : ISystemButtonActions {
	private readonly EngineWindow _window;
	private readonly SimulatorInput _input;
	private readonly TapePlayback _tape;
	private readonly HostMenuBar _menuBar;
	private readonly string _manualRoot;
	private readonly GameDisc? _disc;

	private bool _manualKeyDown;
	private bool _fullScreenKeyDown;
	private bool _fullScreenLiveKeyDown;
	private bool _leaveFullScreenKeyDown;
	private bool _menuBarEscapeDown;
	private bool _tapeEscapeDown;

	public WindowKeys(EngineWindow window, SimulatorInput input, TapePlayback tape, HostMenuBar menuBar,
			string manualRoot, GameDisc? disc) {
		_window = window;
		_input = input;
		_tape = tape;
		_menuBar = menuBar;
		_manualRoot = manualRoot;
		_disc = disc;
	}

	/// <summary>
	/// OnlineManual_Raise (0045f054) then Help_Show (004668c0), which leaves full screen before its WinHelpA
	/// call — here before OnlineManual hands the page to the browser. The [/] key and the right-hand system
	/// button both come here.
	/// </summary>
	public void OpenManual() {
		if (_window.FullScreen) {
			ToggleFullScreen();
		}

		OnlineManual.Open(_manualRoot, _disc);
	}

	/// <summary>
	/// Video_ToggleFullscreen (004666c4). Retail takes DirectDraw exclusive at the 3D view's size, an 8-bit
	/// display mode, with the window topmost over it, confines the pointer to the screen and centres it;
	/// going back releases DirectDraw and restores the window rect it saved on the way in
	/// (docs/retail/formats/cockpit-input.md, "The two system buttons"). Here it is the same move the front end
	/// makes for VSHELL's toggle (FrontEndWindow.ToggleFullScreen): the window covers its monitor at the monitor's own mode, the
	/// cockpit scaled into it as it is in a window, which is the divergence the user chose there so that
	/// no display mode changes.
	/// </summary>
	public void ToggleFullScreen() => _window.ToggleFullScreen(_input.Mouse);

	/// <summary>
	/// [/] is Sim_DispatchCommand's 0x35, the on-line manual; [?] is the same key, since SimCommandMask strips
	/// the Shift bit (docs/retail/formats/cockpit-input.md#keyboard-commands-are-scancodes). The dispatcher never sees
	/// a key while a modal panel's own loop holds the input, so neither does this.
	/// </summary>
	public void ReadManualKey(bool flashCommHasKeyboard, bool modalPanelOpen) {
		var keyboard = _input.Keyboard;
		if (keyboard == null || _input.KeyboardCapturedByImGui || flashCommHasKeyboard) {
			_manualKeyDown = false;
			return;
		}

		bool down = keyboard.IsKeyPressed(Key.Slash);
		bool edge = down && !_manualKeyDown;
		_manualKeyDown = down;
		if (edge && !modalPanelOpen) {
			OpenManual();
		}
	}

	/// <summary>
	/// The simulator's two full-screen key paths. [Alt+Enter] is command 0x21c, which Sim_DispatchCommand
	/// offers Sim_HandleWindowKey (0045fd60) after the widget tree, so it comes through the dispatcher's
	/// own input — a replaying tape's keys during a replay. During a replay Input_BuildPlayerDevice also
	/// offers Sim_HandleWindowKey the live keyboard's command, so the player's own [Alt+Enter] works too.
	/// Retail's -B stops a replay toggling; this host has no -B. [Alt+Enter] also toggles here while a modal
	/// panel (the pause panel among them) holds the input, which is this engine's choice, made without
	/// settling whether retail's panel loops reach the dispatcher for it; the panels ignore an [Enter] with
	/// [Alt] held (ModalPanels.PanelKey), so the toggle never also presses a panel button. The other path
	/// is Key_WndProcHook (00477ae0), which hands 0x20f [Alt+Tab], 0x201 [Alt+Esc] and 0x401 [Ctrl+Esc] to
	/// Video_LeaveFullscreen (004668b0) before anything else sees a key, so those read the live keyboard
	/// always. [Alt+Tab] is left out here at the user's request, so switching away keeps the window full
	/// screen behind the one switched to, as a modern game's does (EngineWindow.ToggleFullScreen). The hook
	/// matches them before SimCommandMask strips [Shift], so with [Shift] held they are other codes;
	/// [Alt+Enter] goes through the mask, so [Shift] does not matter to it. Both act on the key going down,
	/// as the hook passes a key-down message on and marks a key-up one.
	/// </summary>
	public void ReadFullScreenKeys() {
		bool replaying = _tape.Playing;
		var liveKeys = _input.LiveKeys;
		bool toggle = AltEnterPressed(_input.Keyboard, !_input.KeyboardCapturedByImGui, ref _fullScreenKeyDown)
			| AltEnterPressed(replaying ? liveKeys : null, !_input.ImGuiHasKeyboard, ref _fullScreenLiveKeyDown);

		bool leave = false;
		if (liveKeys != null && !_input.ImGuiHasKeyboard) {
			bool shift = liveKeys.IsKeyPressed(Key.ShiftLeft) || liveKeys.IsKeyPressed(Key.ShiftRight);
			bool alt = AltHeld(liveKeys);
			bool ctrl = CtrlHeld(liveKeys);
			bool down = !shift && ((alt && !ctrl && liveKeys.IsKeyPressed(Key.Escape))
				|| (ctrl && !alt && liveKeys.IsKeyPressed(Key.Escape)));
			leave = down && !_leaveFullScreenKeyDown;
			_leaveFullScreenKeyDown = down;
		} else {
			_leaveFullScreenKeyDown = false;
		}

		if (leave && _window.FullScreen) {
			ToggleFullScreen();
		} else if (toggle) {
			ToggleFullScreen();
		}

		// [Alt+Enter] going down on one key source.
		bool AltEnterPressed(IKeyState? keys, bool open, ref bool wasDown) {
			if (keys == null || !open) {
				wasDown = false;
				return false;
			}

			bool down = AltHeld(keys) && !CtrlHeld(keys)
				&& (keys.IsKeyPressed(Key.Enter) || keys.IsKeyPressed(Key.KeypadEnter));
			bool pressed = down && !wasDown;
			wasDown = down;
			return pressed;
		}
	}

	/// <summary>
	/// [Esc] backs out one layer at a time: closes whichever of the menu bar's panels is open, else hides an
	/// empty menu bar, else returns to the cockpit from the external view, a side window or the Heads-Down
	/// Display, else raises the menu bar. The menu bar is the only way to reach either panel, since every key
	/// from F1 to F12 is already taken by the game.
	/// </summary>
	public void ReadMenuBarEscapeKey(bool consumedByOtherPanel, CockpitView view, bool hasCockpit) {
		// During a replay the two halves of this key come apart: the tape's [Esc] is the game's and only
		// ever backs out of a view, and the live one keeps the menu bar, which is this engine's.
		bool tapePlaying = _tape.Playing;
		var keyboard = _input.Keyboard;
		if (tapePlaying && keyboard != null) {
			bool tapeDown = keyboard.IsKeyPressed(Key.Escape);
			if (tapeDown && !_tapeEscapeDown && !consumedByOtherPanel && view.ExternalViewActive) {
				view.Chain?.Escape();
			} else if (tapeDown && !_tapeEscapeDown && !consumedByOtherPanel && hasCockpit
					&& !view.ExternalViewActive && view.AwayFromForward) {
				view.ReturnToForward();
			}

			_tapeEscapeDown = tapeDown;
			consumedByOtherPanel = false;
		}

		var liveKeys = _input.LiveKeys;
		if (liveKeys == null) {
			return;
		}

		// Tracked every frame independent of consumedByOtherPanel, exactly like the modal panels track their
		// own Escape edge regardless of who else claims it — otherwise a press that is still held on the frame
		// a retail panel lets go of Escape reads as a second, fresh press here.
		bool down = liveKeys.IsKeyPressed(Key.Escape);
		bool pressed = down && !_menuBarEscapeDown;
		_menuBarEscapeDown = down;

		if (pressed && !consumedByOtherPanel) {
			if (_menuBar.BackOut()) {
				// A panel or the bar itself came down.
			} else if (!tapePlaying && view.ExternalViewActive) {
				// With the cockpit's widgets off, scancode 1 falls through them to the dispatcher's own
				// case, which is the way back from the external view — see ExternalViewChain.Escape.
				view.Chain?.Escape();
			} else if (!tapePlaying && hasCockpit && !view.ExternalViewActive && view.AwayFromForward) {
				// The manual's [Esc] is "the way back" from the side windows and the Heads-Down Display
				// alike — view command 6 from a glance, 1 from heads-down. Only once that is done does
				// [Esc] fall through to raising the menu bar.
				view.ReturnToForward();
			} else {
				_menuBar.Show();
			}
		}
	}
}
