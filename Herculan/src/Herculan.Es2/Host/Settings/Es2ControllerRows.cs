using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Herculan.Es2.Sim;

namespace Herculan.Es2.Host.Settings;

/// <summary>
/// What Earthsiege 2's <see cref="ControllerWindow"/> shows: the four axis slots, the hat and the eight BUTTON rows of
/// retail's twelve CONTROLS bytes, read through the response curve and deadzone (<see cref="ControllerDevice.Read"/>) on
/// ±<see cref="MechControls.AxisFull"/>; and this engine's own radar toggle (<see cref="JoystickDeviceMap.RadarToggle"/>),
/// centre-idle throttle (<see cref="JoystickDeviceMap.BipolarThrottle"/>) and RAZOR pitch invert
/// (<see cref="JoystickDeviceMap.InvertRazorPitch"/>). See docs/herculan/joystick-config.md, "Setting up a stick".
/// </summary>
static class Es2ControllerRows {
	public static ControllerRows Create() => new() {
		AxisLabelKeys = ["controller.stick_x", "controller.stick_y", "controller.throttle", "controller.rudder"],
		ThrottleInvertTipKey = "controller.invert_throttle_tip",
		HatLabelKey = "controller.hat_row",
		ButtonRows = JoystickCapabilities.MaxButtons,
		ReadsDeadzone = true,
		Reads = (device, map) => {
			var reading = device.Read(map);
			return new ControllerReads(reading.StickX, reading.StickY, reading.Throttle, reading.Rudder, reading.Hat, reading.Buttons);
		},
		ReadsFull = MechControls.AxisFull,
		NoteKey = "controller.actions_note",
		Actions = ControllerRows.OfferedExcept(ControllerActions.Es1Only),
		HasRazor = true,
		ExtraButtonRows = [
			new ControllerButtonRow("controller.radar_toggle", "controller.radar_toggle_tip",
				map => map.RadarToggle, (map, source) => map with { RadarToggle = source }),
		],
		Options = [
			new ControllerOption("controller.centre_idle", "controller.centre_idle_tip",
				map => map.BipolarThrottle, (map, value) => map with { BipolarThrottle = value }),
			new ControllerOption("controller.invert_razor_pitch", "controller.invert_razor_pitch_tip",
				map => map.InvertRazorPitch, (map, value) => map with { InvertRazorPitch = value }),
		],
	};
}
