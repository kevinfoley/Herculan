using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The keys that act on the window rather than the game: [/] for the on-line manual, and the full-screen toggle
/// and its ways out. Each acts once per press, ignoring auto-repeats: a held [Alt+Enter] toggles once per repeat in
/// the original, which this does not reproduce (KNOWN_ISSUES.md).
/// </summary>
sealed class WindowKeys : ISystemButtonActions {
	private readonly EngineWindow _window;
	private readonly SimulatorInput _input;
	private readonly TapePlayback _tape;
	private readonly string _manualRoot;
	private readonly GameDisc? _disc;

	private bool _manualKeyDown;
	private bool _fullScreenKeyDown;
	private bool _fullScreenLiveKeyDown;
	private bool _leaveFullScreenKeyDown;

	public WindowKeys(EngineWindow window, SimulatorInput input, TapePlayback tape, string manualRoot, GameDisc? disc) {
		_window = window;
		_input = input;
		_tape = tape;
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
	/// (docs/retail/simulation/cockpit-input.md, "The two system buttons"). Here it is the same move the front end
	/// makes for VSHELL's toggle (FrontEndWindow.ToggleFullScreen): the window covers its monitor at the monitor's own mode, the
	/// cockpit scaled into it as it is in a window, which is the divergence the user chose there so that
	/// no display mode changes.
	/// </summary>
	public void ToggleFullScreen() => _window.ToggleFullScreen(_input.Mouse);

	/// <summary>
	/// [/] is Sim_DispatchCommand's 0x35, the on-line manual; [?] is the same key, since SimCommandMask strips
	/// the Shift bit (docs/retail/simulation/cockpit-input.md#keyboard-commands-are-scancodes). The dispatcher never sees
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
}
