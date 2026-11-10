using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Herculan.Es1.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// What Metaltech: Earthsiege's <see cref="ControllerWindow"/> shows: what <see cref="Es1ControllerPort"/> reads, the four
/// axis slots and BUTTON 1–4, by position on ±<see cref="JoystickReading.RawFull"/>. The rows are named for the slots, a
/// gamepad's too, since what each port axis does is the joystick model's that the CONTROLS panel chooses
/// (docs/es1/simulation/joystick.md#what-each-configuration-reads). There is no hat row while the port puts the hat on
/// no line, and no deadzone, which the port does not take off. The map's other settings stay in the file as they are,
/// for Earthsiege 2. See docs/herculan/joystick-config.md, "Setting up a stick".
/// </summary>
static class Es1ControllerRows {
	/// <summary>The port's buttons, the map's logical buttons 1–4.</summary>
	private const int PortButtons = 4;

	public static ControllerRows Create() => new() {
		AxisLabelKeys = ["controller.stick_x", "controller.stick_y", "controller.throttle", "controller.rudder"],
		ButtonRows = PortButtons,
		Reads = (device, map) => {
			var positions = Es1ControllerPort.Read(map, device.Axis, device.BindableButton);
			return new ControllerReads(positions.StickX, positions.StickY, positions.Throttle, positions.Rudder,
				positions.Hat, positions.Buttons);
		},
		ReadsFull = JoystickReading.RawFull,
		NoteKey = "controller.es1_actions_note",
	};
}
