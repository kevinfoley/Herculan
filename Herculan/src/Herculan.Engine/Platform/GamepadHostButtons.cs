using Herculan.Engine.Input;
using Silk.NET.Input;

namespace Herculan.Engine.Platform;

/// <summary>
/// A gamepad's Back and Start, which the host keeps for itself (<see cref="ControllerDevice.Reserved"/>): Back raises
/// the menu bar as [Shift+Esc] does (<see cref="FrontEndWindow.BeginFrame"/> over a front end), and Start is the game's
/// host's to answer, which over an Earthsiege 2 mission raises its pause menu (docs/herculan/herculan-key-bindings.md).
/// This engine's own; retail has no gamepad. Either button on any connected gamepad counts.
/// </summary>
public sealed class GamepadHostButtons {
	private bool _backDown;
	private bool _startDown;

	/// <summary>Whether Back went down this frame.</summary>
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
