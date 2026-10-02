namespace Herculan.Engine.Input;

/// <summary>
/// One tick of a joystick after its bindings have been applied — what
/// <see cref="JoystickBindings.Resolve"/> hands back.
/// </summary>
/// <param name="Axes">
/// The stick's contribution to the four game axes, zero on an axis it does not reach. It is not the
/// final answer: <see cref="JoystickBindings.Combine"/> puts the keyboard behind it.
/// </param>
/// <param name="Fire">
/// Whether the button bound to <see cref="JoystickAction.Fire"/> is held. A held state, not an
/// edge — the whole trigger path re-runs every frame, so holding it fires again as soon as the
/// refire delay expires.
/// </param>
/// <param name="Pressed">
/// The actions whose buttons went down this tick. Empty on most ticks, and never more than one
/// entry — see <see cref="JoystickBindings"/> for why. <see cref="JoystickAction.Fire"/> and
/// <see cref="JoystickAction.Off"/> never appear.
/// </param>
/// <param name="Views">
/// The hat, when it is assigned to VIEWS: passed through to the view path rather than to an axis.
/// <see cref="JoystickHat.None"/> under either other setting, because the original zeroes the hat
/// bytes before anything can read them.
/// </param>
/// <param name="KeyboardAimsTurret">
/// Whether the stick has taken the movement pair, in which case the keyboard's steering pair is
/// <b>moved</b> onto the turret pair rather than fighting the stick for the same axes. Applied by
/// <see cref="JoystickBindings.Combine"/>.
/// </param>
/// <param name="SuppressKeyboardPitch">
/// Whether an assigned throttle axis has taken the keyboard's turret-pitch contribution out of play,
/// which the original does whenever a lever exists and is bound to anything at all.
/// </param>
/// <param name="ClaimedButton">
/// The button, 0-based, whose press took this tick's one action slot — whatever it is bound to, OFF
/// and FIRE included, except the button the trigger scan withholds, which never takes it. -1 when none
/// did. An input tape records it.
/// </param>
public readonly record struct JoystickPilotInput(
		PilotAxes Axes,
		bool Fire,
		IReadOnlyList<JoystickAction> Pressed,
		JoystickHat Views = JoystickHat.None,
		bool KeyboardAimsTurret = false,
		bool SuppressKeyboardPitch = false,
		int ClaimedButton = -1) {

	/// <summary>No stick, or one the input layer could not enumerate. Every field inert.</summary>
	public static readonly JoystickPilotInput None =
		new(PilotAxes.Centred, false, Array.Empty<JoystickAction>());
}
