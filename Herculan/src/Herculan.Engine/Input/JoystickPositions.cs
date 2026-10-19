namespace Herculan.Engine.Input;

/// <summary>
/// One frame of a joystick by position, as <see cref="JoystickDeviceMap.ReadPositions"/> reads it: the four role axes
/// where the device is holding them, before any response curve, and the hat and buttons as <see cref="JoystickReading"/>
/// carries them. It is for a game that puts the stick through a response curve of its own; ES2's is
/// <see cref="JoystickReading.ApplyResponse"/>, which <see cref="JoystickDeviceMap.Read"/> lays on top of this.
///
/// <para>Each axis is in <see cref="JoystickReading.RawFull"/> units: the device's fraction of its travel scaled to
/// <c>-RawFull..+RawFull</c>, rounded to the nearest count, with the map's invert for that role applied, and 0 for a role
/// the map leaves <see cref="JoystickDeviceMap.Unmapped"/>. No deadzone is taken off.</para>
/// </summary>
/// <param name="StickX">The axis in the stick's left/right role (<see cref="JoystickDeviceMap.StickXAxis"/>).</param>
/// <param name="StickY">The axis in the stick's fore/aft role (<see cref="JoystickDeviceMap.StickYAxis"/>).</param>
/// <param name="Throttle">The axis in the throttle role (<see cref="JoystickDeviceMap.ThrottleAxis"/>).</param>
/// <param name="Rudder">The axis in the rudder role (<see cref="JoystickDeviceMap.RudderAxis"/>).</param>
/// <param name="Hat"><inheritdoc cref="JoystickReading" path="/param[@name='Hat']"/></param>
/// <param name="Buttons"><inheritdoc cref="JoystickReading" path="/param[@name='Buttons']"/></param>
public readonly record struct JoystickPositions(
		int StickX = 0, int StickY = 0, int Throttle = 0, int Rudder = 0,
		JoystickHat Hat = JoystickHat.None, byte Buttons = 0);
