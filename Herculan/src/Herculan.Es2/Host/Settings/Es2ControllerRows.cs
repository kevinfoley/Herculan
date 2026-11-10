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
	/// <summary>
	/// A gamepad's axis and hat rows are named for what they do, which GamepadControls' fixed CONTROLS rows decide whatever
	/// fills them: the slots' own names would have the throttle slot aiming the turret. The throttle slot's invert has a
	/// tooltip of its own on a gamepad, where it is the right stick's up and down, which no lever reading applies to.
	/// </summary>
	public static ControllerRows Create() => new() {
		AxisLabelKeys = ["controller.stick_x", "controller.stick_y", "controller.throttle", "controller.rudder"],
		GamepadAxisLabelKeys =
			["controller.gamepad_stick_x", "controller.gamepad_stick_y", "controller.gamepad_throttle", "controller.gamepad_rudder"],
		ThrottleInvertTipKey = "controller.invert_throttle_tip",
		GamepadThrottleInvertTipKey = "controller.invert_throttle_gamepad_tip",
		HatLabelKey = "controller.hat_row",
		GamepadHatLabelKey = "controller.gamepad_hat_row",
		ButtonRows = JoystickCapabilities.MaxButtons,
		ReadsDeadzone = true,
		Reads = (device, map) => {
			var reading = device.Read(map);
			return new ControllerReads(reading.StickX, reading.StickY, reading.Throttle, reading.Rudder, reading.Hat, reading.Buttons);
		},
		ReadsFull = MechControls.AxisFull,
		NoteKey = "controller.actions_note",
		ExtraButtonRows = [
			new ControllerButtonRow("controller.radar_toggle", "controller.radar_toggle_tip",
				map => map.RadarToggle, (map, source) => map with { RadarToggle = source }),
		],
		// Centre-idle reads a throttle lever, and a gamepad's controls read none (GamepadControls), so it is greyed for one.
		Options = [
			new ControllerOption("controller.centre_idle", "controller.centre_idle_tip", "controller.centre_idle_gamepad_tip",
				map => map.BipolarThrottle, (map, value) => map with { BipolarThrottle = value }),
			new ControllerOption("controller.invert_razor_pitch", "controller.invert_razor_pitch_tip", null,
				map => map.InvertRazorPitch, (map, value) => map with { InvertRazorPitch = value }),
		],
	};
}
