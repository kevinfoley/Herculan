using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Herculan.Es1.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// What Metaltech: Earthsiege's <see cref="ControllerWindow"/> shows: what <see cref="Es1ControllerPort"/> reads, the four
/// axis slots, the hat and BUTTON 1–4, by position on ±<see cref="JoystickReading.RawFull"/>. The rows are named for the
/// slots, a gamepad's too, since what each port axis does is the joystick model's that the CONTROLS panel chooses
/// (docs/es1/simulation/joystick.md#what-each-configuration-reads). The hat row shows the direction the port reads,
/// before the stick model puts it on a line, and has no diagonals option, since the port reads a diagonal as centred;
/// nor is there a deadzone, which the port does not take off. The map's other settings stay in the file as they are,
/// for Earthsiege 2. See docs/herculan/joystick-config.md, "Setting up a stick".
/// </summary>
static class Es1ControllerRows {
	/// <summary>The port's buttons, the map's logical buttons 1–4.</summary>
	private const int PortButtons = 4;

	public static ControllerRows Create() => new() {
		AxisLabelKeys = ["controller.stick_x", "controller.stick_y", "controller.throttle", "controller.rudder"],
		HatLabelKey = "controller.hat_row",
		HatDiagonalsOption = false,
		ButtonRows = PortButtons,
		Reads = (device, map) => {
			var positions = Es1ControllerPort.Read(map, device.Axis, device.BindableButton, device.Hat);
			return new ControllerReads(positions.StickX, positions.StickY, positions.Throttle, positions.Rudder,
				positions.Hat, positions.Buttons);
		},
		ReadsFull = JoystickReading.RawFull,
		NoteKey = "controller.es1_actions_note",
		Actions = ControllerRows.OfferedExcept(ControllerActions.Es2Only),
	};
}
