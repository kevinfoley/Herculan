using ImGuiNET;
using Silk.NET.Input;

namespace Herculan.Engine.Host;

/// <summary>
/// The first connected gamepad, given to ImGui as its gamepad keys (<see cref="Platform.ScaledImGui.GamepadFeed"/>) so the
/// menu bar and its windows can be worked without a mouse, through ImGui's own gamepad navigation
/// (docs/herculan/herculan-key-bindings.md, "Gamepad in the menus"). This engine's own. Back and Start are the host's
/// (<see cref="GamepadHostButtons"/>) and the left stick's click the squad orders' (<see cref="Simulator.SquadOrderRadial"/>),
/// so ImGui is not given them.
/// </summary>
sealed class ImGuiGamepad(IInputContext input) {
	// Where a stick starts and finishes counting toward a direction, and a trigger, which rests at -1, toward its pull,
	// as fractions of the axis: the bands ImGui's own GLFW backend uses.
	private const float StickStart = 0.25f;
	private const float StickFull = 1f;
	private const float TriggerStart = -0.75f;
	private const float TriggerFull = 1f;

	// How far past its start an analog key has to be to count as down: ImGui's GLFW backend's threshold.
	private const float AnalogDown = 0.1f;

	private static readonly (int Button, ImGuiKey Key)[] Buttons = [
		(ControllerDevice.GamepadA, ImGuiKey.GamepadFaceDown),
		(ControllerDevice.GamepadB, ImGuiKey.GamepadFaceRight),
		(ControllerDevice.GamepadX, ImGuiKey.GamepadFaceLeft),
		(ControllerDevice.GamepadY, ImGuiKey.GamepadFaceUp),
		(ControllerDevice.GamepadLeftBumper, ImGuiKey.GamepadL1),
		(ControllerDevice.GamepadRightBumper, ImGuiKey.GamepadR1),
		(ControllerDevice.GamepadRightStick, ImGuiKey.GamepadR3),
		(ControllerDevice.GamepadDPadUp, ImGuiKey.GamepadDpadUp),
		(ControllerDevice.GamepadDPadRight, ImGuiKey.GamepadDpadRight),
		(ControllerDevice.GamepadDPadDown, ImGuiKey.GamepadDpadDown),
		(ControllerDevice.GamepadDPadLeft, ImGuiKey.GamepadDpadLeft),
	];

	/// <summary>
	/// Whether ImGui is given the pad as released and centred, which it is while the Controller window's Detect is
	/// listening, so that moving a control to bind it does not also move between the window's items.
	/// </summary>
	public bool Suspended { get; set; }

	/// <summary>Queues the pad's state. Called once a frame, before ImGui's frame starts.</summary>
	public void Feed(ImGuiIOPtr io) {
		var pad = Suspended ? null : ControllerDevice.Connected(input).FirstOrDefault(device => device.IsGamepad);

		foreach (var (button, key) in Buttons) {
			io.AddKeyEvent(key, pad?.Button(button) == true);
		}

		// GLFW reads a stick pushed up or left as negative.
		float Axis(int index) => pad?.Axis(index) ?? 0f;
		Analog(io, ImGuiKey.GamepadLStickLeft, -Axis(0), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadLStickRight, Axis(0), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadLStickUp, -Axis(1), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadLStickDown, Axis(1), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadRStickLeft, -Axis(2), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadRStickRight, Axis(2), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadRStickUp, -Axis(3), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadRStickDown, Axis(3), StickStart, StickFull);
		Analog(io, ImGuiKey.GamepadL2, pad == null ? -1f : Axis(4), TriggerStart, TriggerFull);
		Analog(io, ImGuiKey.GamepadR2, pad == null ? -1f : Axis(5), TriggerStart, TriggerFull);
	}

	// ImGui drops an event that repeats the key's last value, so every key is sent every frame.
	private static void Analog(ImGuiIOPtr io, ImGuiKey key, float value, float start, float full) {
		float amount = Math.Clamp((value - start) / (full - start), 0f, 1f);
		io.AddKeyAnalogEvent(key, amount > AnalogDown, amount);
	}
}
