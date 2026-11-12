using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es2.Host.Simulator;

/// <summary>
/// A Custom profile's actions that the stick's reading does not carry (<see cref="Input.CustomControls"/>), as the host
/// sends them. <b>This engine's own</b> (docs/herculan/plan-controller-profiles.md, step 2.4). An action with a key goes in
/// as its primary chord, held in the live keyboard for the frame as a radial's choice is, so the cockpit's own key
/// decides what it does and a recording takes the key.
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
