using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Es2.Host.Simulator;

/// <summary>
/// Sends the actions in a Custom profile that can't reach the game through retail's joystick input, which has room only
/// for the axes, the eight CONTROLS buttons and the hat (<see cref="Input.CustomControls"/>). Actions that the host
/// itself handles, such as the menu bar and the pause menu, are reported to the host rather than sent as keys. Every
/// other action with a key binding is sent by holding that binding's keys for the frame (<see cref="KeyMap.Chord"/>), so
/// the game responds as it would to the key, and a mission recording holds the key.
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
	/// Reads the menu bar and pause menu inputs off <paramref name="joystick"/>'s profile, once a frame. They are read even
	/// while the stick is <see cref="JoystickSource.Quiet"/>: the stick is quiet while those menus are up, and these inputs
	/// still have to close them.
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
	/// Returns a test for whether the profile's input for <paramref name="wheel"/> is held on a given device, which is
	/// what opens that radial. A radial is aimed by a gamepad's stick, so a radial bound on a joystick cannot be opened
	/// (docs/herculan/plan-controller-profiles.md, "Open").
	/// </summary>
	public static Func<ControllerDevice, bool> WheelClick(JoystickSource joystick, KeyAction wheel) =>
		device => joystick.Device is { } read && device.Slot == read.Slot && joystick.HostActionHeld(wheel);

	/// <summary>
	/// Sends, as keys, the actions that <paramref name="joystick"/>'s profile has held down this frame. Each one with a key
	/// binding holds its first binding's keys and modifiers (<see cref="KeyMap.Chord"/>) in <paramref name="keys"/>. The
	/// menu bar and pause menu are skipped, since the host handles them (<see cref="Update"/>), and so are the two radials,
	/// since a radial is aimed by a gamepad's stick rather than by keys (<see cref="WheelClick"/>).
	/// </summary>
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
