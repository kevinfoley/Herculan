using Herculan.Engine.Numerics;
using Herculan.Es1.Projectiles;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// A flyer's attack (docs/es1/simulation/flyers.md#the-attack-run--flyer_attackrun-0001bba4): <c>attacking</c>'s think,
/// and the run the leader flies, away from the target, round and back in, firing a missile first and then pairs of gun
/// rounds; the others keep station on it (<see cref="Es1FlyerAi.FormationStep"/>).
/// </summary>
public static class Es1FlyerAttack {
	/// <summary>The ground range past which the run's figures are capped for the lead, and the cap.</summary>
	public const int RangeCap = 200000;

	/// <summary>The ground range past which an extending flyer turns back in, phase 0 to 1.</summary>
	public const int TurnInRange = 80000;

	/// <summary>The ground range inside which a flyer runs in: it leads with the gun's speed, pitches at the target and may fire.</summary>
	public const int RunInRange = 45000;

	/// <summary>The ground range inside which the run's first shot is the missile.</summary>
	public const int MissileRange = 30000;

	/// <summary>The bearing error, either way, outside which a running-in flyer breaks off.</summary>
	public const int BreakOffArc = 0x2000;

	/// <summary>The height below which a running-in flyer breaks off, 5500 (33 m).</summary>
	public const int BreakOffAltitude = 0x157c;

	/// <summary>The bearing and pitch errors, each in [−1000, 999], within which the flyer fires.</summary>
	public const int FireWindow = 1000;

	/// <summary>The pitch rate, in [−10, 9], within which the flyer fires.</summary>
	public const int PitchRateWindow = 10;

	/// <summary>The refire countdown's reload, about 0.6 seconds.</summary>
	public const short RefireInterval = 1500;

	/// <summary>The muzzle in the flyer's own frame; the second gun's is its mirror in X.</summary>
	public static readonly (short X, short Y, short Z) Muzzle = (500, 200, -100);

	/// <summary>The missile subtype and the gun round subtype the run fires.</summary>
	public const short MissileSubtype = 0, GunSubtype = 2;

	/// <summary>The <c>PROJ.DAT</c> record whose speed <c>+0xa</c> the lead takes inside <see cref="RunInRange"/>.</summary>
	public const int GunRecord = 2;

	/// <summary>
	/// The missile flag, the word at <c>+0x54</c> of the behaviour block's scratch. It lies under the target sweep's
	/// countdown record <c>+0x54</c>, whose counter <c>+0x55</c> (<see cref="Es1FlyerAi.SweepCountdownOffset"/>) takes its
	/// high byte; no sweep runs in <c>attacking</c>.
	/// </summary>
	public const int MissileFlagOffset = 0x54;

	/// <summary>The run's phases: extending and running in. Phases 2 and 3 are not reachable (<see cref="AttackRun"/>).</summary>
	public const short Extending = 0, RunningIn = 1;

	/// <summary>
	/// <c>Flyer_BehaviourAttackThink</c> (<c>ES1DBSIM 0001c3f8</c>), <c>attacking</c>: 1, finished, when the target is
	/// immobilized (<c>+0x9e</c>) or destroyed (<c>+0x93</c>); otherwise <see cref="AttackRun"/> for the group's first member
	/// and <see cref="Es1FlyerAi.FormationStep"/> for the others, and 0.
	/// </summary>
	public static short AttackThink(Es1Flyer flyer, Es1SimGlobals globals) {
		// Retail reads the target unchecked. Every path into attacking installs one and nothing releases it there; with
		// none the state is done here, an engine guard, as the HERC's combat states do.
		if (flyer.Target is not { } target || target.Immobilized || target.Destroyed) {
			return 1;
		}

		if (Es1FlyerAi.IsLeader(flyer)) {
			AttackRun(flyer, target, globals);
		} else {
			Es1FlyerAi.FormationStep(flyer, globals);
		}

		return 0;
	}

	/// <summary>
	/// <c>Flyer_AttackRun</c> (<c>0001bba4</c>)(<paramref name="flyer"/>), the target <paramref name="target"/>
	/// (docs/es1/simulation/flyers.md#the-attack-run--flyer_attackrun-0001bba4). The point is the target's position; the
	/// bearing error to it (<c>Math_HeadingToward</c> less the heading); the ground range <i>r</i>
	/// (<c>Math_DistanceBetweenPoints2D</c>), taken once; the flyer's and the target's speeds through slot <c>+0x3c</c>.
	/// <list type="number">
	/// <item>The lead: in <see cref="RunningIn"/> inside <see cref="RunInRange"/> the shot speed is <c>PROJ.DAT</c> record
	/// <see cref="GunRecord"/>'s <c>+0xa</c>, otherwise the flyer's own speed; with it positive and the target's speed not 0,
	/// the point moves along the target's heading by (<c>int16</c>)((<c>int16</c>)(min(<i>r</i>, <see cref="RangeCap"/>)
	/// ≫ 3) × target speed × 8 / shot speed) and the bearing error is taken again.</item>
	/// <item><see cref="Extending"/>: the error turned half round, the missile flag (<see cref="MissileFlagOffset"/>)
	/// cleared, <see cref="RunningIn"/> beyond <see cref="TurnInRange"/>, and <see cref="Es1FlyerFlight.PitchToAltitude"/>
	/// at <see cref="Es1FlyerAi.CruiseAltitude"/>.</item>
	/// <item><see cref="RunningIn"/>, inside <see cref="RunInRange"/> only: with the ground warning, pitch to altitude 0,
	/// otherwise <see cref="Es1FlyerFlight.PitchCommand"/> at the elevation to the point; on the first such tick the run-in
	/// latch set and the third phase's test drawn; back to <see cref="Extending"/>, the latch cleared, when the error leaves
	/// ±<see cref="BreakOffArc"/> or the flyer is below <see cref="BreakOffAltitude"/>.</item>
	/// <item>The shot, in any phase but <see cref="Extending"/> inside <see cref="RunInRange"/>: the error and the pitch
	/// error to the point (elevation less the pitch) in [−<see cref="FireWindow"/>, <see cref="FireWindow"/>), the pitch rate
	/// in [−<see cref="PitchRateWindow"/>, <see cref="PitchRateWindow"/>), and then the refire countdown
	/// (<c>Math_CountdownTimerTick</c>) at 0, each test reached only when the one before passes. The muzzle
	/// (<see cref="Muzzle"/>) through the flyer's frame; with the missile flag clear inside <see cref="MissileRange"/>,
	/// <see cref="Es1Rocket.Fire"/>(the forward speed, flyer, its angles, muzzle, <see cref="MissileSubtype"/>) and the flag
	/// set; otherwise <see cref="Es1Bullet.Fire"/>(flyer, its angles, muzzle, <see cref="GunSubtype"/>) from the muzzle and
	/// from its mirror in X. The countdown reloaded with <see cref="RefireInterval"/>.</item>
	/// <item>Below phase 2, the formation throttle <see cref="Es1FlyerAi.LeaderThrottle"/> and
	/// <see cref="Es1FlyerFlight.SteerAndFly"/>(flyer, none, the error).</item>
	/// </list>
	/// The rounds and the missile leave along the flyer's own attitude, not aimed at the target. The pitch rate
	/// <c>+0x18a</c> is read as it stands; it has no writer found (docs/es1/simulation/flyers.md#open), and nothing ported
	/// writes it, so it stays 0 here and the gate's test on it passes.
	/// </summary>
	public static void AttackRun(Es1Flyer flyer, IEs1SimObject target, Es1SimGlobals globals) {
		var point = target.Position;
		short error = (short)(Es1MissionGroup.HeadingToward(flyer.Position, point) - flyer.Heading);
		int range = Es1PlayerThink.DistanceBetweenPoints2D(flyer.Position, point);
		short shotSpeed = flyer.GetSpeed();
		short targetSpeed = target.GetSpeed();
		int capped = Math.Min(range, RangeCap);
		if (flyer.AttackPhase == RunningIn && range < RunInRange) {
			// Retail reads the record unchecked; with none (an install short of PROJ.DAT's third record) no lead here.
			var records = globals.Projectiles.Records;
			shotSpeed = records.Count > GunRecord ? records[GunRecord].Speed : (short)0;
		}

		if (shotSpeed > 0 && targetSpeed != 0) {
			short lead = (short)((short)(capped >> 3) * (targetSpeed * 8) / shotSpeed);
			point = Es1CombatStates.OffsetPointByBearing(point, target.Heading, lead);
			error = (short)(Es1MissionGroup.HeadingToward(flyer.Position, point) - flyer.Heading);
		}

		var block = flyer.Behaviour;
		if (flyer.AttackPhase == Extending) {
			error = unchecked((short)(error - 0x8000));
			block.SetScratchInt16(MissileFlagOffset, 0);
			if (range > TurnInRange) {
				flyer.AttackPhase = RunningIn;
			}

			Es1FlyerFlight.PitchToAltitude(flyer, Es1FlyerAi.CruiseAltitude);
		} else if (flyer.AttackPhase == RunningIn) {
			if (range < RunInRange) {
				RunInStep(flyer, point, error, globals);
			}
		}

		// Phases 2 and 3, the slow strafe and the stop (docs/es1/simulation/flyers.md#the-third-phase), are not ported:
		// only RunInStep's entry test sets phase 2, and that test is constant 0, so no flyer reaches them.
		if (flyer.AttackPhase != Extending && range < RunInRange && (uint)(error + FireWindow) < 2 * FireWindow) {
			TryFire(flyer, point, range, globals);
		}

		if (flyer.AttackPhase < 2) {
			flyer.FormationThrottle = Es1FlyerAi.LeaderThrottle;
			Es1FlyerFlight.SteerAndFly(flyer, null, error);
		}
	}

	/// <summary>
	/// <see cref="AttackRun"/>'s <see cref="RunningIn"/> step inside <see cref="RunInRange"/>: the pitch, the latch and the
	/// third phase's draw on its first tick, and the break-off.
	/// </summary>
	private static void RunInStep(Es1Flyer flyer, Vec3i point, short error, Es1SimGlobals globals) {
		if (flyer.GroundWarning) {
			Es1FlyerFlight.PitchToAltitude(flyer, 0);
		} else {
			Es1FlyerFlight.PitchCommand(flyer, Es1ShotSweep.ElevationAngle(flyer.Position, point));
		}

		if (!flyer.RunInLatch) {
			flyer.RunInLatch = true;
			// The third phase's test, (Math_RandomNext & 0xfff) >> 31 at 0001bd9a: CWDE, AND EAX,0xfff, SHR EAX,0x1f. The AND
			// clears bit 31, so the test is 0 for every draw; the draw still advances the generator.
			int draw = globals.Random.Next();
			if ((uint)(draw & 0xfff) >> 31 != 0) {
				// Never taken, so not ported: the timer +0x1cd loaded with 30000 + Math_RandomBelow(20000) and phase 2 set.
			}
		}

		if ((uint)(error + BreakOffArc) >= 2 * BreakOffArc || flyer.Position.Z < BreakOffAltitude) {
			flyer.RunInLatch = false;
			flyer.AttackPhase = Extending;
		}
	}

	/// <summary>
	/// <see cref="AttackRun"/>'s shot, past its bearing test: the pitch error and pitch rate gates, the refire countdown,
	/// then the missile or the gun pair.
	/// </summary>
	private static void TryFire(Es1Flyer flyer, Vec3i point, int range, Es1SimGlobals globals) {
		short pitchError = (short)(Es1ShotSweep.ElevationAngle(flyer.Position, point) - flyer.Rotation.X);
		if ((uint)(pitchError + FireWindow) >= 2 * FireWindow || (uint)(flyer.Rates.Pitch + PitchRateWindow) >= 2 * PitchRateWindow) {
			return;
		}

		short countdown = flyer.RefireCountdown;
		globals.Clock.CountdownTimerTick(ref countdown);
		flyer.RefireCountdown = countdown;
		if (countdown != 0) {
			return;
		}

		var block = flyer.Behaviour;
		var angles = flyer.Rotation;
		var muzzle = flyer.Frame.TransformPoint(Muzzle.X, Muzzle.Y, Muzzle.Z);
		if (block.ScratchInt16(MissileFlagOffset) == 0 && range < MissileRange) {
			Es1Rocket.Fire(globals, flyer.Velocity.Y, flyer, angles, muzzle, MissileSubtype);
			block.SetScratchInt16(MissileFlagOffset, 1);
		} else {
			Es1Bullet.Fire(globals, flyer, angles, muzzle, GunSubtype);
			var mirror = flyer.Frame.TransformPoint(-Muzzle.X, Muzzle.Y, Muzzle.Z);
			Es1Bullet.Fire(globals, flyer, angles, mirror, GunSubtype);
		}

		flyer.RefireCountdown = RefireInterval;
	}
}
