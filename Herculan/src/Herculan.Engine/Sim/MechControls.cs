namespace Herculan.Engine.Sim;

/// <summary>
/// One tick's worth of pilot input for a HERC, in the axis convention DBSIM's own device layer
/// hands to <c>Mech_ApplyThrottleInput</c> (<c>004160dc</c>): two signed stick axes, full deflection
/// at ±0x100, and the throttle-lever mode the original keeps as a global because only the player
/// has one.
///
/// <para><b>A flyer reads the same four axes differently.</b> The device layer makes no distinction;
/// <c>Sim_PollPlayerInput</c> hands them to a different control law, which takes
/// <see cref="Turn"/> as the aileron, <see cref="Throttle"/> as the elevator,
/// <see cref="TorsoTwist"/> as the rudder and <see cref="TorsoPitch"/> as the throttle. The field
/// names here are the walker's roles, which are also the device axes' own names. See
/// docs/retail/simulation/razor-flight.md.</para>
/// </summary>
/// <param name="Turn">
/// Stick X. Negative is left, positive right — a joystick's own sign convention. The control law
/// inverts it internally depending on the direction of travel, which is what makes reverse steer
/// the way reversing a vehicle does.
/// </param>
/// <param name="Throttle">
/// Stick Y, applied as a rate: each tick moves the throttle setting by
/// <c>Q8(0x91, -Throttle)</c>, so holding full deflection covers the whole range in about seven
/// ticks. Negative (stick forward) opens the throttle.
///
/// <para>Held against the stop it runs the setting all the way from full forward to full reverse,
/// pausing exactly one tick at zero on the way through — the sign-crossing guard snaps to zero
/// rather than stepping past it, and the following tick starts again from there. That one-tick
/// detent is what the manual means by "Centered is stopped".</para>
///
/// <para>When <see cref="ThrottleLever"/> is set this axis is read as an absolute lever position
/// instead, and the rate path is skipped.</para>
/// </param>
/// <param name="ThrottleLever">
/// Whether a physical throttle lever is driving the throttle, in what mode, and which way round.
/// Magnitude picks the mode and sign picks the sense: 0 for no lever — keyboard and plain stick —
/// <see cref="ThrottleLeverUnipolar"/> for retail's lever, and
/// <see cref="ThrottleLeverBipolar"/> for this engine's centre-zero one. A negative value is
/// either mode read upside down.
///
/// <para>This is the original's <c>ThrottleLeverMode</c> (<c>0049a06e</c>), which is <b>not</b> a forward/reverse gear
/// selector despite the name in the symbol table — docs/retail/simulation/mech-locomotion.md carries the
/// argument. What matters here is that it gates the throttle clamp: at 0 the setting is free to
/// take either sign, and with a unipolar lever present the clamp closes to one side of zero.</para>
///
/// <para><b><see cref="ThrottleLeverBipolar"/> is this engine's invention, not retail behaviour.</b>
/// DBSIM has only the unipolar mode: its lever spends its whole travel on one direction and
/// <c>CHANGE DIRECTION</c> flips which. See docs/retail/formats/joystick-input.md.</para>
/// </param>
/// <param name="TorsoTwist">
/// The turret axis, left/right. Full deflection at ±0x100, as the two above. It is a
/// <i>rate</i> demand, not a position: <see cref="MechObject.TorsoTwistTick"/> builds the torso's
/// turn rate toward what this asks for and integrates that into the angle.
/// </param>
/// <param name="TorsoPitch">The turret axis, up/down. Positive looks up.</param>
/// <param name="CenterTorso">
/// The manual's [Backspace] "Center Turret" command — a mode, not a keypress: the original latches
/// it (<c>g_CenterTurretMode</c> (<c>004d2588</c>)) and runs the centring tick every tick until the pilot moves either
/// turret axis, which clears it. The host holds it the same way.
/// </param>
/// <param name="CenterBody">
/// The manual's [\] "Center Body" command — the other half of the pair, and a locomotion assist
/// rather than a turret one: it steers the <i>legs</i> round to line up under the turret, rather
/// than bringing the turret back to the legs.
///
/// <para>Read on its rising edge, not held: latching it (<c>g_CenterBodyMode</c> (<c>004d2af4</c>)) captures the world
/// direction the turret is pointing in (<c>g_CenterBodyTargetHeading</c> (<c>004d2af8</c>)), and everything after that is measured
/// against that one number, so it has to be taken once. It clears <see cref="CenterTorso"/> and is
/// cleared by it — the original's dispatch sets one of the two globals and zeroes the other
/// wherever it touches either.</para>
/// </param>
/// <param name="Fire">
/// The manual's [Space] "Fire Active Weapon" — the device struct's own byte at <c>+0x0d</c>, which
/// is the joystick trigger and whatever key is bound alongside it.
///
/// <para><b>Held, not pressed.</b> It reaches the weapon manager as a mount vtable call
/// (<c>+0x30</c>, <c>WeaponMount_TriggerHeld</c> (<c>0040f8ad</c>)) that does nothing but return that byte, and the whole trigger
/// path is re-run every frame — so a held trigger fires again as soon as the refire delay expires
/// and the capacitor is back over its threshold. Nothing along the path looks at edges, which is why
/// there is no scancode case for [Space] anywhere in the command dispatcher.</para>
/// </param>
public readonly record struct MechControls(short Turn, short Throttle, int ThrottleLever = 0,
		short TorsoTwist = 0, short TorsoPitch = 0, bool CenterTorso = false,
		bool CenterBody = false, bool Fire = false) {
	/// <summary>Full stick deflection, in either direction.</summary>
	public const short AxisFull = 0x100;

	/// <summary>
	/// <see cref="ThrottleLever"/>'s magnitude for retail's lever: the travel runs from idle at one
	/// stop to full at the other, all of it in the direction the sign names.
	/// </summary>
	public const int ThrottleLeverUnipolar = 1;

	/// <summary>
	/// <see cref="ThrottleLever"/>'s magnitude for the centre-zero lever — <b>this engine's
	/// invention</b>. Idle sits at the middle of the travel, forward of it is forward and aft of it
	/// is reverse, so the lever reaches both directions without <c>CHANGE DIRECTION</c>.
	/// </summary>
	public const int ThrottleLeverBipolar = 2;

	/// <summary>
	/// What a held direction key is worth — <b>half</b> a stick's full deflection, not all of it.
	///
	/// <para>DBSIM's own constant. <c>Input_BuildKeyboardAxes</c> (<c>0045a4b0</c>) builds the keyboard's two axis pairs by
	/// accumulating <c>direction * 0x80</c> per held key, where the direction pair is the ±1
	/// components the key binding carries, so a cardinal key reaches <c>0x80</c> on its axis and
	/// nothing reaches <c>0x100</c>. The joystick hat is a third value again (<c>0xc0</c>); only an
	/// analogue stick spans the full range. See docs/retail/formats/joystick-input.md#the-keyboard.</para>
	///
	/// <para>It is load-bearing for steering, because the turn rate is
	/// <c>Q8(tentRate, axis)</c> — <b>linear</b> in the axis. At <see cref="AxisFull"/> a keyboard
	/// pilot turns exactly twice as fast as retail. It also halves how quickly the throttle ramps
	/// (<c>Q8(0x91, -axis)</c> per tick, so about 14 ticks to the stop rather than 7) and how fast
	/// the turret sweeps, without changing top speed, which the throttle reaches either way.</para>
	/// </summary>
	public const short KeyboardAxis = 0x80;

	/// <summary>Hands off the controls.</summary>
	public static MechControls Neutral => new(0, 0);
}
