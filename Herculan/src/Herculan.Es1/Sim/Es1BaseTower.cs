using Herculan.Engine.Numerics;
using Herculan.Es1.Ai;
using Herculan.Es1.Projectiles;

namespace Herculan.Es1.Sim;

/// <summary>
/// The gun tower, the armed table <c>StructureArmedVtable</c> (<c>ES1DBSIM 0009061a</c>)'s tick and its turret
/// (docs/es1/simulation/structures.md#the-gun-tower--base_armedthinktick-00003364): it picks a target every few seconds,
/// turns its turret toward it and fires both barrels inside its range while its firing window is open.
/// </summary>
public static class Es1BaseTower {
	/// <summary>What the retarget countdown <c>+0x1b6</c> reloads with, about 3.9 s.</summary>
	public const short RetargetInterval = 10000;

	/// <summary>The mask the retarget hands <see cref="Es1Targets.SelectTarget"/>, <c>0x30</c>: reject the tower's own class, ignore the bearing.</summary>
	public const short TargetMask = Es1Targets.OtherClassOnly | Es1Targets.IgnoreBearing;

	/// <summary>The range inside which the aim point takes the lead and the tower may fire, 40000 (240 m).</summary>
	public const int FireRange = 40000;

	/// <summary>The <c>PROJ.DAT</c> record whose speed <c>+0xa</c> the lead takes, and the gun round subtype the tower fires.</summary>
	public const short GunRecord = 2, GunSubtype = 2;

	/// <summary><c>BaseFiringWindow</c> (<c>0009048c</c>), what the window's countdown reloads with by the flag it has just taken: shut, open.</summary>
	public static readonly short[] FiringWindow = [10000, 10000];

	/// <summary>The aim error, each of its X and Z in [−this, this), within which the tower fires.</summary>
	public const int AimWindow = 1000;

	/// <summary>The right barrel's muzzle in the turret's frame; the left's is its mirror in X.</summary>
	public static readonly (short X, short Y, short Z) Muzzle = (300, 400, 0);

	/// <summary>What the refire countdown <c>+0x1aa</c> reloads with after a pair, about 0.59 s.</summary>
	public const short RefireInterval = 1500;

	/// <summary>The part whose posed node is the turret's frame.</summary>
	public const int TurretPart = 2;

	/// <summary><c>BaseTurret_Gain</c> (<c>00090490</c>), Q8, by axis: elevation, traverse.</summary>
	public static readonly short[] Gain = [2000, 2500];

	/// <summary><c>BaseTurret_RateLimit</c> (<c>00090494</c>), the most a rate moves in a tick, by axis.</summary>
	public static readonly short[] RateLimit = [200, 800];

	/// <summary><c>BaseTurret_MinAngle</c> (<c>00090498</c>) and <c>BaseTurret_MaxAngle</c> (<c>0009049c</c>), the stops, by axis.</summary>
	public static readonly short[] MinAngle = [-4000, -32768], MaxAngle = [4000, 32767];

	/// <summary>The shift the aim error takes before it is held to ±<see cref="Es1Turret.AxisFull"/>.</summary>
	public const int ErrorShift = 3;

	/// <summary>
	/// <c>Base_ArmedThinkTick</c> (<c>00003364</c>)(<paramref name="tower"/>), slot <c>+0x1c</c> of the armed table, for a
	/// standing tower (<see cref="Es1Base.Tick"/> hands a destroyed one to <c>Base_ThinkTick</c>):
	/// <list type="number">
	/// <item>on each expiry of the retarget countdown, <see cref="Es1Base.TowerTarget"/> =
	/// <see cref="Es1Targets.SelectTarget"/>(tower, <see cref="TargetMask"/>) and the countdown reloaded with
	/// <see cref="RetargetInterval"/>;</item>
	/// <item>the range <i>r</i>, <c>Math_DistanceBetweenPoints</c> from the target to the tower, 0 with none;</item>
	/// <item>the muzzle flash: while the cell countdown is not 0, on each expiry the cell of sequence <c>+0x2a</c> steps to
	/// (cell + 1) modulo its frame count, and the countdown reloads from <c>+0x2c</c> only while the cell is not 0, so the
	/// sequence plays once through;</item>
	/// <item>with a target, the aim point, the turret, the firing window and the shot (<see cref="Engage"/>).</item>
	/// </list>
	/// A sequence the shape gives no frames, which retail divides by, or that lies past the cell array, leaves the cell and
	/// stops the flash, an engine guard.
	/// </summary>
	public static void ArmedThinkTick(Es1Base tower, Es1SimGlobals globals) {
		var clock = globals.Clock;
		if (clock.CountdownTimerTick(ref tower.RetargetCountdown) == 0) {
			tower.TowerTarget = Es1Targets.SelectTarget(tower, TargetMask, globals);
			tower.RetargetCountdown = RetargetInterval;
		}

		var target = tower.TowerTarget;
		int range = target?.Position.ApproxDistanceTo(tower.Position) ?? 0;
		if (tower.CellCountdown != 0 && clock.CountdownTimerTick(ref tower.CellCountdown) == 0) {
			short sequence = tower.Type.CellSequence;
			int frames = tower.StandingShape.FrameCount(sequence);
			if (sequence >= 0 && sequence < tower.CellFrames.Length && frames != 0) {
				short cell = (short)(((ushort)tower.CellFrames[sequence] + 1) % frames);
				tower.CellFrames[sequence] = cell;
				if (cell != 0) {
					tower.CellCountdown = tower.Type.CellInterval;
				}
			}
		}

		if (target != null) {
			Engage(tower, target, range, globals);
		}
	}

	/// <summary>
	/// <see cref="ArmedThinkTick"/> with a target <paramref name="target"/> at range <paramref name="range"/>:
	/// <list type="number">
	/// <item>the aim point, the target's position plus its slot <c>+0x34</c>'s offset (<see cref="IEs1SimObject.AimOffset"/>);
	/// inside <see cref="FireRange"/>, with record <see cref="GunRecord"/>'s speed <i>v</i> positive and the target's speed
	/// <i>s</i> (slot <c>+0x3c</c>) not 0, moved along the target's heading by <c>Math_OffsetPointByBearing</c> the lead
	/// (<c>int16</c>)((<c>int16</c>)(<i>r</i> ≫ 3) × 8<i>s</i> / <i>v</i>);</item>
	/// <item>the error, <see cref="AimTurret"/>(tower, the point);</item>
	/// <item>on each expiry of the firing window's countdown the flag flips and the countdown reloads with
	/// <see cref="FiringWindow"/>[the new flag];</item>
	/// <item>the refire countdown stepped; at 0, with the window open, the error's X and Z each in
	/// [−<see cref="AimWindow"/>, <see cref="AimWindow"/>) and <i>r</i> below <see cref="FireRange"/>, the pair
	/// (<see cref="FirePair"/>).</item>
	/// </list>
	/// Both countdowns run only on ticks with a target. Retail reads record 2 unchecked; an install short of it takes no
	/// lead here, an engine guard.
	/// </summary>
	private static void Engage(Es1Base tower, IEs1SimObject target, int range, Es1SimGlobals globals) {
		var point = target.Position + target.AimOffset;
		if (range < FireRange) {
			var records = globals.Projectiles.Records;
			short shotSpeed = records.Count > GunRecord ? records[GunRecord].Speed : (short)0;
			short targetSpeed = target.GetSpeed();
			if (shotSpeed > 0 && targetSpeed != 0) {
				short lead = (short)((short)(range >> 3) * (targetSpeed * 8) / shotSpeed);
				point = Es1CombatStates.OffsetPointByBearing(point, target.Heading, lead);
			}
		}

		var error = AimTurret(tower, point);
		var clock = globals.Clock;
		if (clock.CountdownTimerTick(ref tower.FiringWindowCountdown) == 0) {
			tower.FiringWindowOpen = !tower.FiringWindowOpen;
			tower.FiringWindowCountdown = FiringWindow[tower.FiringWindowOpen ? 1 : 0];
		}

		if (clock.CountdownTimerTick(ref tower.RefireCountdown) == 0 && tower.FiringWindowOpen
				&& (uint)(error.X + AimWindow) < 2 * AimWindow && (uint)(error.Z + AimWindow) < 2 * AimWindow
				&& range < FireRange) {
			FirePair(tower, globals);
		}
	}

	/// <summary>
	/// The pair: the turret's frame, part <see cref="TurretPart"/>'s posed node concatenated with the tower's frame
	/// (<c>Transform_Concat</c>); <see cref="Es1Bullet.Fire"/>(tower, that frame's angles, <see cref="Muzzle"/> through it,
	/// <see cref="GunSubtype"/>) and again from the muzzle's mirror in X; then the refire countdown reloaded with
	/// <see cref="RefireInterval"/> and the cell countdown set to 1, which starts the muzzle flash. Retail takes a missing
	/// part's node through a null pointer, from address 0; both retail gun tower shapes, <c>dts\bases_an</c> and
	/// <c>dts\bases_ae</c> root 2, have part 2, and the tower's frame alone is taken here, an engine guard.
	/// </summary>
	private static void FirePair(Es1Base tower, Es1SimGlobals globals) {
		var frame = Transform3.Concat(tower.PartNodeTransform(TurretPart) ?? Transform3.Identity, tower.Frame);
		var angles = frame.ToEuler();
		Es1Bullet.Fire(globals, tower, angles, frame.TransformPoint(Muzzle.X, Muzzle.Y, Muzzle.Z), GunSubtype);
		Es1Bullet.Fire(globals, tower, angles, frame.TransformPoint(-Muzzle.X, Muzzle.Y, Muzzle.Z), GunSubtype);
		tower.RefireCountdown = RefireInterval;
		tower.CellCountdown = 1;
	}

	/// <summary>
	/// <c>Base_AimTurret</c> (<c>00003058</c>)(<paramref name="tower"/>, <paramref name="point"/>)
	/// (docs/es1/simulation/structures.md#the-turret--base_aimturret-00003058): the point taken into the tower's frame and
	/// then into part <see cref="TurretPart"/>'s posed node, each inverted; the error, <c>Math_EulerToward</c> from the
	/// node's origin to it. Axis 0 takes the error's X, axis 1 its Z negated, each shifted right <see cref="ErrorShift"/>
	/// and held to ±<see cref="Es1Turret.AxisFull"/>; for each, the demand Q8(<see cref="Gain"/>, Q8(<i>a</i>,
	/// |<i>a</i>|)), the rate <c>Math_RateLimitedMoveToward</c> it by <see cref="RateLimit"/>, the angle plus the rate held
	/// to <see cref="MinAngle"/>–<see cref="MaxAngle"/>, the upper stop tested first, and the thread of the axis seeked in
	/// sequence <i>axis</i> to the angle. Then <see cref="Es1Base.ApplyRootMotionIfEnabled"/> with
	/// <see cref="Es1Base.RootMotionTime"/>, which re-poses the nodes. Answers the error, <c>BaseTurret_AimError</c>
	/// (<c>000aee00</c>). The rates and their limit are raw per-tick steps, as retail takes them. Retail inverts a missing
	/// part's node read from address 0; the tower's frame alone is taken here, as in <see cref="FirePair"/>, an engine
	/// guard. The first-use byte <c>000aedfc</c> it sets, and does not read, is not kept.
	/// </summary>
	public static (short X, short Y, short Z) AimTurret(Es1Base tower, Vec3i point) {
		var inTower = tower.Frame.Inverted().TransformPoint(point.X, point.Y, point.Z);
		var node = tower.PartNodeTransform(TurretPart) ?? Transform3.Identity;
		var local = node.Inverted().TransformPoint(inTower.X, inTower.Y, inTower.Z);
		var error = SimTrig.EulerToward(local, Vec3i.Zero);

		Span<short> axes = [Held(error.X), (short)-Held(error.Z)];
		for (int axis = 0; axis < axes.Length; axis++) {
			short input = axes[axis];
			short magnitude = input == short.MinValue ? short.MaxValue : Math.Abs(input);
			short step = (short)SimMath.Q8MultiplyRounded(input, magnitude);
			short demand = (short)SimMath.Q8MultiplyRounded(Gain[axis], step);
			SimMath.RateLimitedMoveToward(ref tower.TurretRate[axis], demand, RateLimit[axis]);
			short next = (short)(tower.TurretAngle[axis] + tower.TurretRate[axis]);
			short angle = next >= MaxAngle[axis] ? MaxAngle[axis] : next <= MinAngle[axis] ? MinAngle[axis] : next;
			tower.TurretAngle[axis] = angle;
			Es1Turret.Seek(tower.Threads[axis], (short)axis, angle);
		}

		tower.ApplyRootMotionIfEnabled(Es1Base.RootMotionTime);
		return error;

		// The error shifted right and held to the axis' full deflection either way.
		static short Held(short value) {
			int shifted = value >> ErrorShift;
			return (short)(shifted >= Es1Turret.AxisFull ? Es1Turret.AxisFull : shifted <= -Es1Turret.AxisFull ? -Es1Turret.AxisFull : shifted);
		}
	}
}
