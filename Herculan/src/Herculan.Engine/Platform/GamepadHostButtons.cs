using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Engine.Platform;

/// <summary>
/// HERCULAN's solution for reading the status of the Select/Back and Start buttons on a gamepad.
/// </summary>
public sealed class GamepadHostButtons {
	private bool _backDown;
	private bool _startDown;

	/// <summary>Whether Select/Back went down this frame.</summary>
	public bool BackPressed { get; private set; }

	/// <summary>Whether Start went down this frame.</summary>
	public bool StartPressed { get; private set; }

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
