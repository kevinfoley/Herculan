using Herculan.Engine.Input;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The state a mission powers up in, for a <c>--screenshot</c> run, which never sees a keystroke — this
/// engine's own staging flags, nothing of the original's. Each property is the command-line flag of the same
/// purpose, whose comment in the host's parser says why it exists (docs/herculan/herculan-command-line.md).
/// What the capture waits for is <see cref="StagedScreenshot"/>.
/// </summary>
public sealed class StagedStart {
	public MfdMode? Mfd { get; set; }
	public bool HeadsDown { get; set; }
	public HddPage HddPage { get; set; } = CockpitHudState.Default.Hdd;
	public HddDamageView HddDamageView { get; set; } = CockpitHudState.Default.HddDamage;
	public short Throttle { get; set; }
	public int? Heading { get; set; }
	public bool External { get; set; }
	public short HeldTwist { get; set; }
	public short HeldPitch { get; set; }
	public int? WeaponRow { get; set; }
	public bool Link { get; set; }
	public bool HeldFire { get; set; }
	public bool HitShake { get; set; }
	public bool AcquireTarget { get; set; }
	public bool AutoTrack { get; set; }
	public int HddPilot { get; set; } = -1;
	public int HddSubject { get; set; } = HddDamageSubject.PlayerSlot;
	public HddOrder? HddOrder { get; set; }
	public bool HddTransmit { get; set; }
	public int FlashCommRow { get; set; } = -1;
	public bool FlashCommTransmit { get; set; }
	public bool Objectives { get; set; }
	public bool Preferences { get; set; }
	public bool Controls { get; set; }
	public JoystickCapabilities Joystick { get; set; } = JoystickCapabilities.None;
	public bool StatusAlert { get; set; }
	public int StatusAlertStatus { get; set; } = -1;
}
