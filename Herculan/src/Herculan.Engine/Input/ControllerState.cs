using Herculan.Engine.Input;

namespace Herculan.Es2.Input;

/// <summary>
/// A device's axes, buttons and hats as a Custom profile reads them (<see cref="CustomControls"/>): the device's own, or
/// the host's view of it with some inputs held at rest, as <see cref="Host.JoystickSource"/> gives while a gamepad works
/// the Heads-Down Display or one of retail's panels. <b>This engine's own.</b>
/// </summary>
public readonly record struct ControllerState(Func<int, float> Axis, Func<int, bool> Button, Func<int, JoystickHat> Hat) {
	/// <summary>Everything <paramref name="device"/> reports, unfiltered.</summary>
	public static ControllerState Of(ControllerDevice device) => new(device.Axis, device.Button, device.Hat);
}
