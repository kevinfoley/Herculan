namespace Herculan.Engine.Input;

/// <summary>
/// The four game axes the simulator's control laws read — the device struct's <c>+0x0e</c>,
/// <c>+0x10</c>, <c>+0x12</c> and <c>+0x14</c> (<c>DAT_004d2358</c> onwards), which
/// <c>Sim_PollPlayerInput</c> hands to <c>Mech_ApplyThrottleInput</c> and the two torso ticks.
///
/// <para>They are the <b>destinations</b> of the binding, not sources: the joystick, the hat and the
/// keyboard all write here, and which of them reaches a given axis is what a controls block
/// decides. Full deflection is 256 on every one; a stick reaches
/// it through <see cref="JoystickReading.ApplyResponse"/>, a held key is worth half
/// and a hat three quarters.</para>
///
/// <para>A RAZOR reads the same four differently: <see cref="Steer"/> is the aileron,
/// <see cref="Throttle"/> the elevator, <see cref="TorsoTwist"/> the rudder and
/// <see cref="TorsoPitch"/> the throttle. The device layer makes no distinction.</para>
/// </summary>
public readonly record struct PilotAxes(short Steer = 0, short Throttle = 0,
		short TorsoTwist = 0, short TorsoPitch = 0) {

	/// <summary>Hands off every axis.</summary>
	public static readonly PilotAxes Centred = new();

	/// <summary>Whether any axis is off centre.</summary>
	public bool Any => Steer != 0 || Throttle != 0 || TorsoTwist != 0 || TorsoPitch != 0;

	/// <summary>
	/// This set where it is off centre, <paramref name="fallback"/> where it is not — the arbitration
	/// <c>Input_ReadPlayerInput</c> runs per axis when it combines its sources, testing each for
	/// non-zero in turn and taking the first that has moved.
	/// </summary>
	public PilotAxes Or(PilotAxes fallback) => new(
		Steer != 0 ? Steer : fallback.Steer,
		Throttle != 0 ? Throttle : fallback.Throttle,
		TorsoTwist != 0 ? TorsoTwist : fallback.TorsoTwist,
		TorsoPitch != 0 ? TorsoPitch : fallback.TorsoPitch);
}
