using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// A gamepad on retail's own panels, the pause and mission status alerts, the objectives, the preferences strip and the
/// CONTROLS panel (docs/herculan/herculan-key-bindings.md, "Retail's panels on a gamepad"). The d-pad or the left stick
/// steps the focus by where the buttons sit (<see cref="PanelPadFocus"/>), repeating at the keyboard's own rate while
/// held; A presses the focused button and B cancels. A and B go in as [Return] and [Esc], held in the live keyboard for
/// the frame they go down, so the panel answers them through its own keys and a recording takes the keys themselves.
/// While the pad has a panel the mission's stick reads A and B as up (<see cref="JoystickSource.PanelButtonsTaken"/>).
/// This engine's own; retail has no gamepad.
/// </summary>
sealed class PanelGamepad {
	// The keys standing for the four steps, which only pace the repeat: none of them reaches the panel.
	private static readonly Key[] StepKeys = { Key.Up, Key.Down, Key.Left, Key.Right };
	private static readonly PadDirection[] Directions = { PadDirection.Up, PadDirection.Down, PadDirection.Left, PadDirection.Right };

	// The d-pad's buttons in the same order.
	private static readonly int[] DPad = {
		ControllerDevice.GamepadDPadUp, ControllerDevice.GamepadDPadDown, ControllerDevice.GamepadDPadLeft,
		ControllerDevice.GamepadDPadRight,
	};

	private readonly KeyRepeat _repeat = new(KeyRepeat.SystemTypematic());

	private bool _active;
	private bool _aDown;
	private bool _bDown;

	// The step held now (0-3, in Directions' order) or -1, and the one the stick alone holds, which keeps its own
	// hysteresis (HddGamepad.ArrowOf). Whether the steps wait for the d-pad and stick to be let go, as they do from the
	// moment the pad takes a panel.
	private int _step = -1;
	private int _stickStep = -1;
	private bool _stepsWaitForRelease;

	/// <summary>Whether the pad has the panel on top this frame.</summary>
	public bool Active => _active;

	/// <summary>
	/// Reads the pad, once an update after the host's keys are cleared and before the frame reads them.
	/// <paramref name="allowed"/> is whether the pad may have the panels: one is up, with nothing of the host's over it.
	/// </summary>
	public void Update(IInputContext input, LiveKeys? keys, ModalPanels panels, KeyMap keyMap, bool allowed,
			double deltaSeconds) {
		var pad = allowed && keys != null
			? ControllerDevice.Connected(input).FirstOrDefault(device => device.IsGamepad)
			: null;
		if (pad == null) {
			_active = false;
			_step = -1;
			_stickStep = -1;
			return;
		}

		bool a = pad.Button(ControllerDevice.GamepadA);
		bool b = pad.Button(ControllerDevice.GamepadB);
		if (!_active) {
			// A button already held as the pad takes the panel, such as the A that chose it from the pause menu, does
			// nothing here until it is let go.
			_aDown = a;
			_bDown = b;
			_stepsWaitForRelease = true;
		}

		_active = true;

		if (a && !_aDown) {
			keys!.HeldByHost.Add(keyMap.KeyOf(KeyAction.PanelAccept));
		} else if (b && !_bDown) {
			keys!.HeldByHost.Add(keyMap.KeyOf(KeyAction.PanelCancel));
		}

		_aDown = a;
		_bDown = b;

		Steer(pad, panels, deltaSeconds);
	}

	// The d-pad, else the left stick, steps the focus as it goes down and on each repeat while held.
	private void Steer(ControllerDevice pad, ModalPanels panels, double deltaSeconds) {
		var stick = new Vector2(pad.Axis(0), pad.Axis(1));
		_stickStep = HddGamepad.ArrowOf(stick, _stickStep);

		int dpad = Array.FindIndex(DPad, pad.Button);
		int step = dpad >= 0 ? dpad : _stickStep;
		if (_stepsWaitForRelease) {
			_stepsWaitForRelease = step >= 0;
			step = -1;
		}

		Key? held = step >= 0 ? StepKeys[step] : null;
		_repeat.Advance(key => key == held, deltaSeconds);
		bool fresh = step >= 0 && step != _step;
		_step = step;
		if (fresh || held is { } key && _repeat.Repeated == key) {
			panels.PadFocusToward(Directions[step]);
		}
	}
}
