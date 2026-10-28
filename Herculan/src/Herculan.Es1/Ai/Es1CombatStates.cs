using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// The geometry block a combat state builds once a tick, amends and hands the move step
/// (docs/es1/simulation/mech-ai.md#the-combat-states, docs/retail/simulation/ai-combat-states.md#the-geometry-block--ai_buildcombatgeometry-0041e758).
/// </summary>
public sealed class Es1CombatGeometry {
	/// <summary><c>+0x00</c>, the bearing to the target.</summary>
	public short Bearing;

	/// <summary><c>+0x02</c>, the bearing error, the bearing less the heading: the field a state rewrites to steer.</summary>
	public short BearingError;

	/// <summary><c>+0x04</c>, the bearing error's magnitude, which the move step's range tests read before it recomputes it.</summary>
	public short ErrorMagnitude;

	/// <summary><c>+0x06</c>, the target's aspect: the bearing back to the machine in the target's turret frame.</summary>
	public short Aspect;

	/// <summary><c>+0x08</c>, the aspect's magnitude.</summary>
	public short AspectMagnitude;

	/// <summary><c>+0x0a</c>, the 3D range.</summary>
	public int Range;

	/// <summary><c>+0x0e</c>, the approach flag: 1 forward, −1 back, 0 stand.</summary>
	public short Approach;

	/// <summary><c>+0x10</c> and <c>+0x14</c>, the near and far standoffs.</summary>
	public int NearStandoff, FarStandoff;
}

/// <summary>
/// The combat states' thinks and the steps they share (docs/es1/simulation/mech-ai.md#the-combat-states, which links ES2's
/// derivation): the geometry, the move step and the circling step, and the thinks of <c>attacking</c>, <c>flanking</c>,
/// <c>facing off</c>, <c>attacking base</c>, <c>attacking flyer</c>, <c>fleeing</c> and <c>driving off en</c>. Every steer
/// is the bearing error shifted right by <see cref="Es1Navigation.SteerShift"/>.
///
/// <para>Each think but <c>fleeing</c>'s reads the machine's target with no null test; a combat state that holds no
/// target, which the reassess never leaves, does nothing and answers 1 here, so the reassess runs on the next tick: an
/// engine guard.</para>
/// </summary>
public static class Es1CombatStates {
	/// <summary>The standoffs <see cref="BuildGeometry"/> starts with.</summary>
	public const int NearStandoff = 15000, FarStandoff = 30000;

	/// <summary>The machine offset the move step writes a non-zero approach flag to, the first word of the behaviour block's scratch.</summary>
	public const int ApproachOffset = 0x54;

	/// <summary><c>attacking</c>: the aspect under which the target counts as facing the machine, and how far off its beam the point lies.</summary>
	public const short AttackFacingAspect = 8000;

	/// <inheritdoc cref="AttackFacingAspect"/>
	public const int AttackBeamOffset = 18000;

	/// <summary>
	/// <c>Math_OffsetPointByBearing</c> (<c>00063d8c</c>)(<paramref name="distance"/>, <paramref name="bearing"/>,
	/// <paramref name="point"/>): <c>Math_Q14Multiply</c>(distance, <c>Math_CosBam</c>(bearing + <c>0x4000</c>)) added to X
	/// and <c>Math_Q14Multiply</c>(distance, <c>Math_CosBam</c>(bearing)) to Y, each the full 32-bit product of ES1's
	/// rounding multiply; Z kept (docs/es1/simulation/mech-ai.md#functions). ES2's cuts the distance and the products to 16
	/// bits.
	/// </summary>
	public static Vec3i OffsetPointByBearing(Vec3i point, short bearing, int distance) => new(
		point.X + SimMath.Q14MultiplyRounded(distance, SimTrig.Cos(unchecked((short)(bearing + 0x4000)))),
		point.Y + SimMath.Q14MultiplyRounded(distance, SimTrig.Cos(bearing)),
		point.Z);

	/// <summary>An angle's magnitude, −<c>0x8000</c> taken as <c>0x7fff</c>.</summary>
	public static short Magnitude(short angle) => angle == short.MinValue ? short.MaxValue : Math.Abs(angle);

	/// <summary>
	/// <c>Ai_BuildCombatGeometry</c> (<c>0001851c</c>)(<paramref name="mech"/>, <paramref name="target"/>): the bearing to
	/// the target (<c>Math_HeadingToward</c>), the error from the heading and its magnitude, the aspect (the target's
	/// twist, slot <c>+0x40</c>, plus the bearing less <c>0x8000</c> less the target's heading) and its magnitude, the 3D
	/// range (<c>Math_DistanceBetweenPoints</c>), approach 0 and the standoffs <see cref="NearStandoff"/> and
	/// <see cref="FarStandoff"/>.
	/// </summary>
	public static Es1CombatGeometry BuildGeometry(Es1Mech mech, IEs1SimObject target) {
		short bearing = Es1MissionGroup.HeadingToward(mech.Position, target.Position);
		short error = (short)(bearing - mech.Heading);
		short aspect = (short)(target.TorsoTwistAngle + (short)(bearing - 0x8000 - target.Heading));
		return new Es1CombatGeometry {
			Bearing = bearing,
			BearingError = error,
			ErrorMagnitude = Magnitude(error),
			Aspect = aspect,
			AspectMagnitude = Magnitude(aspect),
			Range = target.Position.ApproxDistanceTo(mech.Position),
			NearStandoff = NearStandoff,
			FarStandoff = FarStandoff,
		};
	}

	/// <summary>
	/// <c>Ai_CombatMoveStep</c> (<c>00018618</c>)(<paramref name="mech"/>, <paramref name="geometry"/>): inside the near
	/// standoff the approach is 1 when the error's magnitude is <c>0x4000</c> or more and −1 otherwise; past the far
	/// standoff 1 at <c>0x4000</c> or less and −1 otherwise; between, the state's own. The magnitude is then recomputed
	/// from the error; a non-zero approach is written to <see cref="ApproachOffset"/> and asks ±<see cref="Es1Navigation.Full"/>;
	/// the control law takes it with the error's steer.
	/// </summary>
	public static void MoveStep(Es1Mech mech, Es1CombatGeometry geometry, Es1SimGlobals globals) {
		if (geometry.Range < geometry.NearStandoff) {
			geometry.Approach = geometry.ErrorMagnitude < 0x4000 ? (short)-1 : (short)1;
		} else if (geometry.FarStandoff < geometry.Range) {
			geometry.Approach = geometry.ErrorMagnitude < 0x4001 ? (short)1 : (short)-1;
		}

		geometry.ErrorMagnitude = Magnitude(geometry.BearingError);
		short speed = 0;
		if (geometry.Approach != 0) {
			// The word lies over the first bytes of a state's scratch: fleeing's countdown at +0x55 has its low byte
			// rewritten each tick by the flag's high byte.
			mech.Behaviour.SetScratchInt16(ApproachOffset, geometry.Approach);
			speed = geometry.Approach < 0 ? (short)-Es1Navigation.Full : Es1Navigation.Full;
		}

		mech.Locomotion.LocomotionTick(globals, 0, speed, (short)(geometry.BearingError >> Es1Navigation.SteerShift));
	}

	/// <summary>The circling step's countdowns: the break-off, <c>+0x5a</c> (handle <c>+0x59</c>), and the interval between break-offs, <c>+0x5d</c> (handle <c>+0x5c</c>).</summary>
	public const int BreakOffOffset = 0x5a, IntervalOffset = 0x5d;

	/// <summary>The circling step's hysteresis byte: set once the machine has squared up.</summary>
	public const int SquaredUpOffset = 0x60;

	/// <summary>The word the square-up arm zeroes when the turret is near centre, ES2's <c>+0x5d</c>.</summary>
	public const int SquareUpWordOffset = 0x57;

	/// <summary>The break-off's length, the interval's reload, and the damage in the window past which a break-off is armed.</summary>
	public const short BreakOffLength = 1000, BreakOffInterval = 4000;

	/// <inheritdoc cref="BreakOffLength"/>
	public const int BreakOffDamage = 100;

	/// <summary>The aspect thresholds under which the machine circles: while circling, and once squared up.</summary>
	public const short CircleAspect = 0x6000, SquaredUpAspect = 0x4000;

	/// <summary>The point the circle steers at: this far from the target, this far either side of the line back to the machine.</summary>
	public const int CircleRadius = 15000;

	/// <inheritdoc cref="CircleRadius"/>
	public const short CircleSide = 6000;

	/// <summary>The turret twist within ± which a squared-up machine stands.</summary>
	public const int SquareUpTwist = 2000;

	/// <summary>
	/// <c>Ai_CircleStep</c> (<c>00016574</c>)(<paramref name="mech"/>, <paramref name="geometry"/>). While the break-off
	/// countdown runs: near standoff 0, approach −1, bearing error 0, the hysteresis cleared and the interval
	/// <see cref="BreakOffInterval"/>. Otherwise the break-off is armed at <see cref="BreakOffLength"/> when the interval has
	/// run out and the damage taken (<c>+0x21e</c>) is over <see cref="BreakOffDamage"/>; then, with the aspect's magnitude
	/// under <see cref="SquaredUpAspect"/> once squared up or <see cref="CircleAspect"/> otherwise, it steers at the point
	/// <see cref="CircleRadius"/> from the target at the bearing back to the machine ±<see cref="CircleSide"/> (+ for a bearing
	/// error not negative), near standoff 0, approach 1, the hysteresis cleared; else the hysteresis set and approach 0 with
	/// the machine's own twist (slot <c>+0x40</c>) within ±<see cref="SquareUpTwist"/>, <see cref="SquareUpWordOffset"/>
	/// zeroed, or −1. It ends with <see cref="MoveStep"/>.
	/// </summary>
	public static void CircleStep(Es1Mech mech, IEs1SimObject target, Es1CombatGeometry geometry, Es1SimGlobals globals) {
		var block = mech.Behaviour;
		var clock = globals.Clock;
		short breakOff = block.ScratchInt16(BreakOffOffset);
		breakOff = clock.CountdownTimerTick(ref breakOff);
		block.SetScratchInt16(BreakOffOffset, breakOff);
		if (breakOff == 0) {
			short interval = block.ScratchInt16(IntervalOffset);
			clock.CountdownTimerTick(ref interval);
			block.SetScratchInt16(IntervalOffset, interval);
			if (interval == 0 && mech.DamageTaken > BreakOffDamage) {
				block.SetScratchInt16(BreakOffOffset, BreakOffLength);
			}

			short threshold = block.Scratch[SquaredUpOffset - Es1BehaviourBlock.ScratchOffset] == 0 ? CircleAspect : SquaredUpAspect;
			if (geometry.AspectMagnitude < threshold) {
				geometry.NearStandoff = 0;
				geometry.Approach = 1;
				short side = geometry.BearingError < 0 ? (short)-CircleSide : CircleSide;
				var point = OffsetPointByBearing(target.Position, (short)(geometry.Bearing - 0x8000 + side), CircleRadius);
				geometry.BearingError = (short)(Es1MissionGroup.HeadingToward(mech.Position, point) - mech.Heading);
				block.Scratch[SquaredUpOffset - Es1BehaviourBlock.ScratchOffset] = 0;
			} else {
				block.Scratch[SquaredUpOffset - Es1BehaviourBlock.ScratchOffset] = 1;
				if ((uint)(mech.TorsoTwistAngle + SquareUpTwist) < SquareUpTwist * 2) {
					geometry.Approach = 0;
					block.SetScratchInt16(SquareUpWordOffset, 0);
				} else {
					geometry.Approach = -1;
				}
			}
		} else {
			geometry.NearStandoff = 0;
			geometry.Approach = -1;
			geometry.BearingError = 0;
			block.Scratch[SquaredUpOffset - Es1BehaviourBlock.ScratchOffset] = 0;
			block.SetScratchInt16(IntervalOffset, BreakOffInterval);
		}

		MoveStep(mech, geometry, globals);
	}

	/// <summary>
	/// <c>Mech_BehaviourAttackThink</c> (<c>000163a0</c>), <c>attacking</c>: the skirt gate, the geometry; with the aspect's
	/// magnitude under <see cref="AttackFacingAspect"/>, the approach −1 for a target within 4000 of dead astern and 1
	/// otherwise, and the steer at the point <see cref="AttackBeamOffset"/> off the target's beam, to its right
	/// (<c>0x4000</c>) when the bearing error's sign matches whether its magnitude is past <c>0x4000</c> and to its left
	/// (<c>0xc000</c>) otherwise, the point reflected through the machine for approach −1; otherwise approach 0 within
	/// <c>0x2000</c> of the nose and −1 beyond. Then the move step, the fire and the tail.
	/// </summary>
	public static short AttackThink(Es1Mech mech, Es1SimGlobals globals) {
		if (Es1AiStandIns.BeginSkirtIfBlocked(mech, globals)) {
			return 0;
		}

		if (mech.Target is not { } target) {
			return 1;
		}

		var geometry = BuildGeometry(mech, target);
		if (geometry.AspectMagnitude < AttackFacingAspect) {
			short approach = 0x8000 - geometry.ErrorMagnitude < 4000 ? (short)-1 : (short)1;
			short side = geometry.BearingError < 0 == geometry.ErrorMagnitude > 0x4000 ? (short)0x4000 : unchecked((short)0xc000);
			var point = OffsetPointByBearing(target.Position, (short)(target.Heading + side), AttackBeamOffset);
			if (approach == -1) {
				var at = mech.Position;
				point = new Vec3i(at.X - (point.X - at.X), at.Y - (point.Y - at.Y), at.Z - (point.Z - at.Z));
			}

			geometry.Approach = approach;
			geometry.BearingError = (short)(Es1MissionGroup.HeadingToward(mech.Position, point) - mech.Heading);
		} else {
			geometry.Approach = geometry.ErrorMagnitude < 0x2000 ? (short)0 : (short)-1;
		}

		MoveStep(mech, geometry, globals);
		return Tail(mech, geometry, globals);
	}

	/// <summary>
	/// <c>Mech_BehaviourFlankThink</c> (<c>000173c8</c>), <c>flanking</c>: the skirt gate, the geometry,
	/// <see cref="CircleStep"/>, which makes the move step, then the fire and the tail.
	/// </summary>
	public static short FlankThink(Es1Mech mech, Es1SimGlobals globals) {
		if (Es1AiStandIns.BeginSkirtIfBlocked(mech, globals)) {
			return 0;
		}

		if (mech.Target is not { } target) {
			return 1;
		}

		var geometry = BuildGeometry(mech, target);
		CircleStep(mech, target, geometry, globals);
		return Tail(mech, geometry, globals);
	}

	/// <summary>The standoffs of <c>facing off</c>.</summary>
	public const int FaceOffNear = 16000, FaceOffFar = 35000;

	/// <summary>
	/// <c>Mech_BehaviourFaceOffThink</c> (<c>000172f0</c>), <c>facing off</c>: the skirt gate, the geometry, approach 1
	/// within <c>0x2000</c> of the nose and −1 beyond, the standoffs <see cref="FaceOffNear"/> and <see cref="FaceOffFar"/>,
	/// the move step, the fire and the tail.
	/// </summary>
	public static short FaceOffThink(Es1Mech mech, Es1SimGlobals globals) {
		if (Es1AiStandIns.BeginSkirtIfBlocked(mech, globals)) {
			return 0;
		}

		if (mech.Target is not { } target) {
			return 1;
		}

		var geometry = BuildGeometry(mech, target);
		geometry.Approach = geometry.ErrorMagnitude < 0x2000 ? (short)1 : (short)-1;
		geometry.NearStandoff = FaceOffNear;
		geometry.FarStandoff = FaceOffFar;
		MoveStep(mech, geometry, globals);
		return Tail(mech, geometry, globals);
	}

	/// <summary>The aspect and its magnitude <c>attacking base</c> forces for a harmless structure, and its standoffs after the circling step.</summary>
	public const short HarmlessAspect = 32000;

	/// <inheritdoc cref="HarmlessAspect"/>
	public const int BaseNear = 25000, BaseFar = -20536;

	/// <summary>
	/// <c>Mech_BehaviourAttackBaseThink</c> (<c>00016718</c>), <c>attacking base</c>: the skirt gate, the geometry, the aspect
	/// and its magnitude <see cref="HarmlessAspect"/> when the target's type record <c>+0x34</c> is 0,
	/// <see cref="CircleStep"/> with its move step, then the standoffs <see cref="BaseNear"/> and <see cref="BaseFar"/> and a
	/// second <see cref="MoveStep"/>, so the control law runs twice; the fire; done when the target or the machine is
	/// disarmed, immobilized or destroyed. Reached only with a structure target, which the slice does not spawn.
	/// </summary>
	public static short AttackBaseThink(Es1Mech mech, Es1SimGlobals globals) {
		if (Es1AiStandIns.BeginSkirtIfBlocked(mech, globals)) {
			return 0;
		}

		if (mech.Target is not { } target) {
			return 1;
		}

		var geometry = BuildGeometry(mech, target);
		if (Es1CombatReassess.StructureTypeWord34IsZero(target)) {
			geometry.Aspect = HarmlessAspect;
			geometry.AspectMagnitude = HarmlessAspect;
		}

		CircleStep(mech, target, geometry, globals);
		geometry.NearStandoff = BaseNear;
		geometry.FarStandoff = BaseFar;
		MoveStep(mech, geometry, globals);
		return TargetOrMachineDone(mech, target, geometry, globals);
	}

	/// <summary><c>attacking flyer</c>'s gates on the bearing error and the aspect, its range, and its standoffs.</summary>
	public const short FlyerGate = 2000;

	/// <inheritdoc cref="FlyerGate"/>
	public const int FlyerRange = 35000, FlyerFar = 1000000;

	/// <summary>
	/// <c>Mech_BehaviourAttackFlyerThink</c> (<c>0001684c</c>), <c>attacking flyer</c>, with no skirt gate: the geometry,
	/// approach −1, or 0 when the error's magnitude is under <see cref="FlyerGate"/> and the aspect's is over it or the range
	/// over <see cref="FlyerRange"/>; the standoffs 0 and <see cref="FlyerFar"/>; the move step, the fire; done when the
	/// target or the machine is disarmed, immobilized or destroyed. Reached only with an aircraft target, which the slice
	/// does not spawn.
	/// </summary>
	public static short AttackFlyerThink(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.Target is not { } target) {
			return 1;
		}

		var geometry = BuildGeometry(mech, target);
		geometry.Approach = -1;
		if (geometry.ErrorMagnitude < FlyerGate && (geometry.AspectMagnitude > FlyerGate || geometry.Range > FlyerRange)) {
			geometry.Approach = 0;
		}

		geometry.NearStandoff = 0;
		geometry.FarStandoff = FlyerFar;
		MoveStep(mech, geometry, globals);
		return TargetOrMachineDone(mech, target, geometry, globals);
	}

	/// <summary><c>fleeing</c>'s side countdown, an <c>int32</c> at <c>+0x55</c> (handle <c>+0x54</c>), its reload, and its side word at <c>+0x59</c>.</summary>
	public const int FleeSideCountdownOffset = 0x55, FleeSideOffset = 0x59;

	/// <inheritdoc cref="FleeSideCountdownOffset"/>
	public const int FleeSideInterval = 4000;

	/// <summary><c>fleeing</c>'s far standoff, past any range.</summary>
	public const int FleeFar = 10000000;

	/// <summary>
	/// <c>Mech_BehaviourFleeThink</c> (<c>0001713c</c>), <c>fleeing</c>: a target held is stashed
	/// (<see cref="Es1BehaviourBlock.FleeingFrom"/>, <c>+0x5b</c>) and released, and the squad verb cleared; the geometry
	/// against the stash. Within <c>0x3000</c> of the nose approach −1 and the error turned a half turn; otherwise, when the
	/// 32-bit side countdown (<c>Timer_CountDown</c>) has run out, it is reloaded with <see cref="FleeSideInterval"/> and the
	/// side is <c>Math_RandomNext</c> &amp; 1, then approach 1 and the error turned <c>0x6000</c> away, + for side 0. The
	/// standoffs 0 and <see cref="FleeFar"/>, the move step, <c>Ai_AimAndFire</c> at the stash; it answers 1 when the
	/// machine or the stash is disarmed, immobilized or destroyed.
	/// </summary>
	public static short FleeThink(Es1Mech mech, Es1SimGlobals globals) {
		var block = mech.Behaviour;
		if (mech.Target is { } target) {
			block.FleeingFrom = target;
			mech.ReleaseTarget();
			mech.SquadVerb = 0;
		}

		// Retail reads the stash unchecked; with none, which no path that installs fleeing leaves, the state is done here.
		if (block.FleeingFrom is not { } from) {
			return 1;
		}

		var geometry = BuildGeometry(mech, from);
		if (geometry.ErrorMagnitude < 0x3000) {
			geometry.Approach = -1;
			geometry.BearingError = unchecked((short)(geometry.BearingError - 0x8000));
		} else {
			int countdown = block.ScratchInt32(FleeSideCountdownOffset);
			globals.Clock.TimerCountDown(ref countdown);
			block.SetScratchInt32(FleeSideCountdownOffset, countdown);
			if (countdown == 0) {
				block.SetScratchInt32(FleeSideCountdownOffset, FleeSideInterval);
				block.SetScratchInt16(FleeSideOffset, (short)(globals.Random.Next() & 1));
			}

			geometry.Approach = 1;
			geometry.BearingError = unchecked((short)(geometry.BearingError
				+ (block.ScratchInt16(FleeSideOffset) == 0 ? 0x6000 : -0x6000)));
		}

		geometry.NearStandoff = 0;
		geometry.FarStandoff = FleeFar;
		MoveStep(mech, geometry, globals);
		Es1AiWeapons.AimAndFire(mech, from, geometry.Aspect, globals);
		return mech.OutOfAction || from.Disarmed || from.Immobilized || from.Destroyed ? (short)1 : (short)0;
	}

	/// <summary><c>driving off en</c>: the most the point it holds lies from the post, how near it counts as there, and the fear it sets.</summary>
	public const int DriveOffReach = 50000, DriveOffArrival = 5000;

	/// <inheritdoc cref="DriveOffReach"/>
	public const short DriveOffFear = 750;

	/// <summary>
	/// <c>Mech_BehaviourDriveOffThink</c> (<c>00017c84</c>), <c>driving off en</c>. With <i>g</i> the post
	/// (<see cref="Es1Mech.AiGoalPosition"/>) and <i>t</i> the target, the ground ranges |<i>g t</i>| and |<i>g</i>
	/// machine| are taken before the skirt gate. The point <i>p</i> lies min(|<i>g t</i>| ≫ 1, <see cref="DriveOffReach"/>)
	/// from (<i>g</i>.X, <i>g</i>.Y, 0) toward <i>t</i>. While |<i>g t</i>| is at most that, and the bearing errors to
	/// <i>p</i> and to <i>t</i> differ by <c>0x1000</c> or more or |<i>g t</i>| is at most |<i>g</i> machine|, it answers
	/// <see cref="FaceOffThink"/>. Otherwise within <see cref="DriveOffArrival"/> of <i>p</i> it stands and steers at
	/// <i>t</i>; else it walks at <i>p</i> at full speed while <i>p</i> is within <c>0x2000</c> of the nose and backs steering
	/// at <i>p</i> otherwise. <see cref="Es1Mech.Fear"/> = <see cref="DriveOffFear"/>; <c>Ai_AimAndFire</c> with the aspect
	/// built by hand; when <see cref="Es1Targets.ShouldAbandonTarget"/> answers or the machine is out of action, the squad
	/// order's target <c>+0x1dc</c> cleared and 1.
	/// </summary>
	public static short DriveOffThink(Es1Mech mech, Es1SimGlobals globals) {
		var post = mech.AiGoalPosition();
		if (mech.Target is not { } target) {
			return 1;
		}

		int toTarget = post is { } at ? Es1PlayerThink.DistanceBetweenPoints2D(at, target.Position) : 0;
		int toMachine = post is { } from ? Es1PlayerThink.DistanceBetweenPoints2D(from, mech.Position) : 0;
		if (Es1AiStandIns.BeginSkirtIfBlocked(mech, globals)) {
			return 0;
		}

		if (post is not { } goal) {
			// PLACEHOLDER: retail reads the post through the group's target with no test, as guarding does; with none (a
			// structure or flyer target, which the slice does not build) the machine stands here, asking nothing.
			mech.Locomotion.LocomotionTick(globals, 0, 0, 0);
			return 0;
		}

		int reach = Math.Min(toTarget >> 1, DriveOffReach);
		var point = OffsetPointByBearing(new Vec3i(goal.X, goal.Y, 0), Es1MissionGroup.HeadingToward(goal, target.Position), reach);
		short errorToPoint = (short)(Es1MissionGroup.HeadingToward(mech.Position, point) - mech.Heading);
		short errorToTarget = (short)(Es1MissionGroup.HeadingToward(mech.Position, target.Position) - mech.Heading);
		short difference = (short)(errorToPoint - errorToTarget);
		if (toTarget <= reach && ((uint)(difference + 0x1000) > 0x1fff || toTarget <= toMachine)) {
			return FaceOffThink(mech, globals);
		}

		if (Es1PlayerThink.DistanceBetweenPoints2D(point, mech.Position) < DriveOffArrival) {
			mech.Locomotion.LocomotionTick(globals, 0, 0, (short)(errorToTarget >> Es1Navigation.SteerShift));
		} else {
			short speed = (uint)(errorToPoint + 0x2000) < 0x4000 ? Es1Navigation.Full : (short)-Es1Navigation.Full;
			mech.Locomotion.LocomotionTick(globals, 0, speed, (short)(errorToPoint >> Es1Navigation.SteerShift));
		}

		mech.Fear = DriveOffFear;
		short aspect = (short)(target.TorsoTwistAngle
			+ (short)(Es1MissionGroup.HeadingToward(target.Position, mech.Position) - target.Heading));
		Es1AiWeapons.AimAndFire(mech, null, aspect, globals);
		if (Es1Targets.ShouldAbandonTarget(mech) || mech.OutOfAction) {
			mech.SquadTarget = null;
			return 1;
		}

		return 0;
	}

	/// <summary>
	/// The tail of <c>attacking</c>, <c>flanking</c> and <c>facing off</c>: <c>Ai_AimAndFire</c>(machine, 0, the aspect);
	/// when <see cref="Es1Targets.ShouldAbandonTarget"/> answers or the machine is disarmed, immobilized or destroyed,
	/// <see cref="Es1Targets.ClearSquadEngageOrder"/> and 1, otherwise 0.
	/// </summary>
	private static short Tail(Es1Mech mech, Es1CombatGeometry geometry, Es1SimGlobals globals) {
		Es1AiWeapons.AimAndFire(mech, null, geometry.Aspect, globals);
		if (Es1Targets.ShouldAbandonTarget(mech) || mech.OutOfAction) {
			Es1Targets.ClearSquadEngageOrder(mech);
			return 1;
		}

		return 0;
	}

	/// <summary>
	/// The tail of <c>attacking base</c> and <c>attacking flyer</c>: <c>Ai_AimAndFire</c>(machine, 0, the aspect); when the
	/// target or the machine is disarmed, immobilized or destroyed, <see cref="Es1Targets.ClearSquadEngageOrder"/> and 1,
	/// otherwise 0.
	/// </summary>
	private static short TargetOrMachineDone(Es1Mech mech, IEs1SimObject target, Es1CombatGeometry geometry,
			Es1SimGlobals globals) {
		Es1AiWeapons.AimAndFire(mech, null, geometry.Aspect, globals);
		if (target.Disarmed || target.Immobilized || target.Destroyed || mech.OutOfAction) {
			Es1Targets.ClearSquadEngageOrder(mech);
			return 1;
		}

		return 0;
	}
}
