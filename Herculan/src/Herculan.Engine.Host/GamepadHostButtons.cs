using Silk.NET.Input;

namespace Herculan.Engine.Host;

/// <summary>
/// A gamepad's Back and Start, which the host keeps for itself (<see cref="ControllerDevice.Reserved"/>): Back raises
/// the menu bar as [Shift+Esc] does, and Start pauses a mission as [P] does (docs/herculan/herculan-key-bindings.md).
/// This engine's own; retail has no gamepad. Either button on any connected gamepad counts.
/// </summary>
sealed class GamepadHostButtons {
	private bool _backDown;
	private bool _startDown;

	/// <summary>Whether Back went down this frame.</summary>
	public bool BackPressed { get; private set; }

	/// <summary>Whether Start went down this frame.</summary>
	public bool StartPressed { get; private set; }

	/// <summary>Whether Start is held.</summary>
	public bool StartDown => _startDown;

	/// <summary>Reads both buttons, once a frame before anything asks.</summary>
	public void Update(IInputContext input) {
		bool back = false;
		bool start = false;
		foreach (var device in ControllerDevice.Connected(input)) {
			if (device.IsGamepad) {
				back |= device.Button(ControllerDevice.GamepadBack);
				start |= device.Button(ControllerDevice.GamepadStart);
			}
		}

		BackPressed = back && !_backDown;
		StartPressed = start && !_startDown;
		_backDown = back;
		_startDown = start;
	}
}
