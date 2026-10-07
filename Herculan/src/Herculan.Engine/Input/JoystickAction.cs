using Herculan.Engine.Cockpit;

namespace Herculan.Engine.Input;

/// <summary>
/// What a joystick button can be bound to — the action codes stored in the eight button bytes of a
/// <c>prefs.cfg</c> controls block, and the index into <c>CTL_ALRT.STR</c> group 2's twenty-one
/// names.
///
/// <para>The codes are dispatched by <c>Sim_PollPlayerInput</c> (<c>00460764</c>), whose switch runs
/// over <c>SimOptions[ControlsOptionBase + 4 + button]</c>. <see cref="Fire"/> never reaches that
/// switch: the button bound to it is pulled out one step earlier, in
/// <c>Input_BuildPlayerDevice</c>, and copied into the device struct's held trigger byte at
/// <c>+0x0d</c> — its own byte is zeroed on the way, so
/// the dispatch loop never sees it.</para>
///
/// <para><see cref="Off"/> is the name a row displays when its stored byte is 0. It is never an
/// offered choice: a button row's option list is zero-terminated, so code 0 ends the list rather than
/// appearing in it — see <see cref="ControlsPanel"/>.</para>
/// </summary>
public enum JoystickAction : byte {
	/// <summary>OFF — what a row reads when nothing is bound. Not an offered choice.</summary>
	Off = 0x00,

	/// <summary>FIRE. Held, not pressed, and taken out of the dispatch path — see the type summary.</summary>
	Fire = 0x01,

	/// <summary>TARGET — <c>TargetSelect_Cycle</c>, what [Enter] does.</summary>
	Target = 0x02,

	/// <summary>CENTER LEGS — the manual's Center Body, latching the turret's heading to steer to.</summary>
	CenterLegs = 0x03,

	/// <summary>CENTER TURRET — the manual's [Backspace], a latched mode rather than a keypress.</summary>
	CenterTurret = 0x04,

	/// <summary>
	/// CHANGE DIRECTION — flips <c>ThrottleLeverMode</c> between +1 and -1, inverting a physical
	/// throttle lever's sense. Gated on there being a lever at all and on it being assigned to
	/// THROTTLE rather than TURRET, so it does nothing on a stick without one.
	/// </summary>
	ChangeDirection = 0x05,

	/// <summary>ATT TOGGLE — dispatches command <c>0x14</c>, the auto-turret-tracking toggle.</summary>
	AttitudeToggle = 0x06,

	/// <summary>ALL STOP — sets the throttle to zero and marks the input-moved-it dirty flag.</summary>
	AllStop = 0x07,

	/// <summary>TARGET NEAREST — <c>TargetSelect_Nearest</c>, what ['] does.</summary>
	TargetNearest = 0x08,

	/// <summary>SHIELDS FRONT — mech command <c>0x1a</c>, the same step the <c>]</c> key takes.</summary>
	ShieldsFront = 0x09,

	/// <summary>SHIELDS REAR — mech command <c>0x1b</c>, the <c>[</c> key's step.</summary>
	ShieldsRear = 0x0a,

	/// <summary>HDD VIEW — pans to the Heads-Down Display, or back up if it is already there.</summary>
	HddView = 0x0b,

	/// <summary>OUTSIDE VIEW — steps the external view chain.</summary>
	OutsideView = 0x0c,

	/// <summary>LINK WEAPON — presses the console LINK button, scancode <c>0x26</c>.</summary>
	LinkWeapon = 0x0d,

	/// <summary>MFD DISPLAYS — steps the MFD's own mode, through <c>MfdDisplay_CycleMode</c> (<c>00446e14</c>).</summary>
	MfdDisplays = 0x0e,

	/// <summary>CHASE VIEW — the chain of views again, behind a frame-count gate at <c>Sim_FrameCount</c> (<c>004d25ff</c>); docs/retail/simulation/external-views.md.</summary>
	ChaseView = 0x0f,

	/// <summary>WEAPON TOGGLE — weapon-manager command <c>0x202</c>, the [Alt]+[2] group step.</summary>
	WeaponToggle = 0x10,

	/// <summary>COCKPIT VIEW — view command 1, back up into the cockpit.</summary>
	CockpitView = 0x11,

	/// <summary>NEXT CHAIN — presses the console chain button, scancode <c>0x29</c>.</summary>
	NextChain = 0x12,

	/// <summary>NEXT WEAPON — weapon-manager command <c>0x11</c>, what [W] does.</summary>
	NextWeapon = 0x13,

	/// <summary>PREV WEAPON — weapon-manager command <c>0x211</c>, [Alt]+[W].</summary>
	PreviousWeapon = 0x14,
}
