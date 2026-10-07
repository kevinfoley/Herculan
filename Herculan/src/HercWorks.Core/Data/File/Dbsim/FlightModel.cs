namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - fm\RAZOR.FM and fm\SKIMMER.FM — the flight physics parameters for a chassis whose type
/// record sets the flyer flag. <c>MechType_InitOne</c> (<c>004201a8</c>) reads the file's 54 bytes
/// straight into the type record at <c>+0x1dc</c>, and the flight model
/// (<c>FlightModel_Step</c>, <c>00466a54</c>) is the only thing that reads them.
///
/// <para>The names here are the roles that function gives each field. The layout, the roles and the
/// readings that mislead are in docs/retail/formats/flight-model-fm.md.</para>
///
/// <para>Bytes 14-17 are a slot the loader fills in with the ceiling slope. It is a derived figure
/// rather than file content, so it is not a field of this type — see the engine's
/// <c>FlightModelRecord</c>.</para>
/// </summary>
public class FlightModel {
	/// <summary>Offset 0 — pitch rate cap, and the gain from full elevator deflection (Q8).</summary>
	public short MaxPitchRate { get; set; }

	/// <summary>Offset 2 — roll rate cap, and the gain from full aileron deflection (Q8).</summary>
	public short MaxRollRate { get; set; }

	/// <summary>Offset 4 — yaw rate cap, and the gain from full rudder deflection (Q8).</summary>
	public short MaxYawRate { get; set; }

	/// <summary>Offset 6 — how much pitch rate may be commanded in one tick.</summary>
	public short MaxPitchAccel { get; set; }

	/// <summary>
	/// Offset 8 — the same cap for roll <i>and</i> for yaw; the flight model clamps both commands
	/// against this one field.
	/// </summary>
	public short MaxRollAccel { get; set; }

	/// <summary>Offset 10 — how fast airspeed closes on the speed the throttle asks for.</summary>
	public short ThrustResponse { get; set; }

	/// <summary>
	/// Offset 18 — the Q10 fraction of each axis' angular rate bled off per tick when that axis is
	/// not being commanded, or is being commanded against its own motion. 500 on the RAZOR.
	/// </summary>
	public short AngularDamping { get; set; }

	/// <summary>
	/// Offset 22 — right shift turning the pitch attitude into a self-levelling pitch command. 16 in
	/// both retail files, which on a 16-bit angle is the designers' way of switching pitch
	/// self-levelling off: an aircraft holds the attitude it is trimmed to.
	/// </summary>
	public short PitchLevelShift { get; set; } = 16;

	/// <summary>
	/// Offset 26 — the roll counterpart, and a real one: 5 on the RAZOR, 6 on the SKIMMER, so a
	/// released stick rolls the wings level.
	/// </summary>
	public short RollLevelShift { get; set; }

	/// <summary>
	/// Offset 30 — right shift turning the bank angle into a heading rate. This is the whole of the
	/// aircraft's turning: there is no rudder-driven flat turn, the nose follows the bank. 4 in both
	/// retail files.
	/// </summary>
	public short BankTurnShift { get; set; }

	/// <summary>Offset 34 — the flight ceiling at <see cref="AirSpeedMax"/>. 60000 on the RAZOR.</summary>
	public int CeilingAtMaxSpeed { get; set; }

	/// <summary>
	/// Offset 38 — the flight ceiling at <see cref="AirSpeedMin"/>, above ground level. 6000 in both
	/// retail files, which is 36 metres.
	/// </summary>
	public int CeilingAtMinSpeed { get; set; } = 6000;

	/// <summary>Offset 42 — airspeed at full throttle.</summary>
	public int AirSpeedMax { get; set; }

	/// <summary>
	/// Offset 46 — airspeed at idle. It is a floor, not a stall speed: the throttle spans
	/// <see cref="AirSpeedMin"/> to <see cref="AirSpeedMax"/> and the aircraft cannot be slowed
	/// below it.
	/// </summary>
	public int AirSpeedMin { get; set; }

	/// <summary>
	/// Offset 50 — the Q10 fraction of the sideways and vertical components of velocity shed each
	/// tick, which is what keeps the aircraft flying where it is pointing rather than drifting.
	/// </summary>
	public int LateralDrag { get; set; }
}
