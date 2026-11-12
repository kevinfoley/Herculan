using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es2.Host.Simulator;

/// <summary>
/// Sends the actions in a Custom profile that can't reach the game through retail's joystick input, which has room only
/// for the axes, the eight CONTROLS buttons and the hat (<see cref="Input.CustomControls"/>). The host's own actions
/// are detected for the host to act on. Every other action with a key binding is sent by holding that binding's keys for
/// the frame (<see cref="KeyMap.Chord"/>), so the cockpit does what the key does and a recording captures the key.
/// <b>This engine's own.</b>
/// </summary>
sealed class CustomHostActions(KeyMap keyMap) {
	private bool _menuBarHeld;
	private bool _pauseMenuHeld;

	/// <summary>Whether the profile's menu bar input went down this frame.</summary>
	public bool MenuBarPressed { get; private set; }

	/// <summary>Whether the profile's pause menu input went down this frame.</summary>
	public bool PauseMenuPressed { get; private set; }

	/// <summary>
	/// Reads the host's two buttons off <paramref name="joystick"/>'s profile, once a frame, whether or not the stick is
	/// quiet, since they work the menus that quiet it.
	/// </summary>
	public void Update(JoystickSource? joystick) {
		bool menuBar = joystick?.HostActionHeld(KeyAction.MenuBar) == true;
		bool pauseMenu = joystick?.HostActionHeld(KeyAction.PauseMenu) == true;
		MenuBarPressed = menuBar && !_menuBarHeld;
		PauseMenuPressed = pauseMenu && !_pauseMenuHeld;
		_menuBarHeld = menuBar;
		_pauseMenuHeld = pauseMenu;
	}

	/// <summary>
	/// What opens a radial, given the profile's input for <paramref name="wheel"/>: that input held on the device being
	/// read. A radial is aimed by a gamepad's stick, so one bound on a joystick opens nothing (the plan's Open).
	/// </summary>
	public static Func<ControllerDevice, bool> WheelClick(JoystickSource joystick, KeyAction wheel) =>
		device => joystick.Device is { } read && device.Slot == read.Slot && joystick.HostActionHeld(wheel);

	/// <summary>Holds the primary chord of each action <paramref name="joystick"/>'s profile holds that has keys and is not the host's.</summary>
	public void HoldKeys(LiveKeys keys, JoystickSource joystick) {
		foreach (var action in joystick.HeldActions()) {
			if (action is KeyAction.MenuBar or KeyAction.PauseMenu or KeyAction.SquadOrdersWheel or KeyAction.ScreensWheel
					|| !keyMap.HasKeys(action)) {
				continue;
			}

			var chord = keyMap.Chord(action);
			if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) {
				keys.HeldByHost.Add(Key.AltLeft);
			}

			if (chord.Modifiers.HasFlag(KeyModifiers.Ctrl)) {
				keys.HeldByHost.Add(Key.ControlLeft);
			}

			if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) {
				keys.HeldByHost.Add(Key.ShiftLeft);
			}

			keys.HeldByHost.Add(chord.Key);
		}
	}
}
