using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;

namespace Herculan.Engine.Sim;

/// <summary>
/// The <c>ramming</c> behaviour state (17) — the one state whose move slot is not
/// <c>Mech_MovementTick</c>, and the only deliberate suicide in the simulation. The derivation is
/// docs/retail/simulation/ai-combat-states.md, "`ramming` (17)".
/// </summary>
public sealed partial class MechObject {
	/// <summary>How long a target selection holds before the think looks again — the original's own literal.</summary>
	private const int RamRetargetInterval = 10000;

	/// <summary>
	/// The charge lasts this plus <c>rand(<see cref="RamChargeJitter"/>)</c>, in
	/// <see cref="SimMath.CountdownTimerTick"/>'s unit rather than milliseconds: about 1.5 seconds
	/// before the jitter.
	/// </summary>
	private const int RamChargeTime = 3000;

	/// <inheritdoc cref="RamChargeTime"/>
	private const short RamChargeJitter = 1500;

	/// <summary>And the approach between two charges lasts this plus <c>rand(<see cref="RamApproachJitter"/>)</c>.</summary>
	private const int RamApproachTime = 8000;

	/// <inheritdoc cref="RamApproachTime"/>
	private const short RamApproachJitter = 4000;

	/// <summary>The blast a ramming machine sets off on contact, and its damage.</summary>
	private const int RamBlastRadius = 3000;

	/// <inheritdoc cref="RamBlastRadius"/>
	private const short RamBlastDamage = 2000;

	/// <summary>
	/// What the machine then puts on each of its own components: past any armour there is, so the
	/// self-destruction is certain rather than rolled.
	/// </summary>
	private const short RamSelfDestructDamage = 32000;

	/// <summary>
	/// <c>Mech_BehaviourRamThink</c> (<c>0041e570</c>) — charge the selected target at full throttle
	/// and, in bursts, batter it.
	///
	/// <para>Two phases on one timer. Approaching, the think only steers; charging, it runs
	/// <see cref="RamTick"/> twice more on top of the once the move slot already ran, so the machine
	/// covers three ticks of ground in one and arrives at three times its walking speed. Each extra
	/// pair is guarded on the target still being held, because a <see cref="RamTick"/> that made
	/// contact has just killed this machine and killing it drops what it was holding.</para>
	///
	/// <para>Both timers start at zero out of <see cref="SetBehaviourState"/>, so entering the state
	/// acquires a target on the first think and starts charging on the same one.</para>
	/// </summary>
	private bool RamThink(SimWorld world) {
		if (SimMath.TimerCountDown(ref _ramRetargetTimer) == 0) {
			Target = AiTargeting.SelectTarget(world, this,
				TargetFilter.IgnoreIncoming | TargetFilter.IgnoreCrowding);
			_ramRetargetTimer = RamRetargetInterval;
		}

		if (Target is not { } target) {
			// Nothing to charge: stand still. The retarget timer is the only way out.
			LocomotionTick(world, 0, 0, ignoreObstacles: true);
			return false;
		}

		RamSteer(world, target);

		if (!_ramCharging) {
			if (SimMath.CountdownTimerTick(ref _ramPhaseTimer) == 0) {
				_ramCharging = true;
				_ramPhaseTimer = (short)(RamChargeTime + world.Random.NextBelow(RamChargeJitter));
			}

			return false;
		}

		RamTick(world);

		if (Target is not { } second) {
			return false;
		}

		RamSteer(world, second);
		RamTick(world);

		if (Target is not { } third) {
			return false;
		}

		RamSteer(world, third);

		if (SimMath.CountdownTimerTick(ref _ramPhaseTimer) == 0) {
			_ramCharging = false;
			_ramPhaseTimer = (short)(RamApproachTime + world.Random.NextBelow(RamApproachJitter));
		}

		return false;
	}

	/// <summary>
	/// The think's steering, which is its whole control law: full throttle, and a turn rate that is
	/// the bearing error's top byte. The gain is <c>&gt;&gt; 8</c> where every other state steers at
	/// <c>&gt;&gt; 6</c>, so a rammer turns a quarter as hard — it commits to a line rather than
	/// tracking a dodging target.
	///
	/// <para>It also switches obstacle avoidance down to the player's line of fire alone — the
	/// original's every ram steer passes 1 as <c>Mech_LocomotionTick</c>'s fourth argument, and no
	/// other caller does. Without that the target itself counts as an obstruction, and the
	/// avoidance's steer, many times this one's, holds the rammer in an orbit around it.</para>
	/// </summary>
	private void RamSteer(SimWorld world, SimObject target) =>
		LocomotionTick(world,
			(short)((short)(Detection.HeadingToward(target.Position, Position) - (short)Heading) >> 8),
			MechControls.AxisFull, ignoreObstacles: true);

	/// <summary>
	/// <c>Mech_BehaviourRamTick</c> (<c>0041e488</c>) — the <c>ramming</c> state's move slot, run once
	/// a tick from <see cref="Tick"/> like any other move and twice more from
	/// <see cref="RamThink"/> while the machine is charging.
	///
	/// <para>It is <see cref="MovementTick"/> with the undo taken out. Integrate, drop onto the
	/// terrain, place the legs, test the collision — and where the walking move would restore the
	/// step and back away, this detonates: a blast at the machine's own feet that it is itself
	/// excluded from, and then a flat <see cref="RamSelfDestructDamage"/> onto every component it
	/// still has. There is no roll, no falloff and no survival.</para>
	///
	/// <para><b>The contact does not have to be the target</b>, or even be this tick's. Any block at
	/// all sets it off, a rock as readily as a HERC, and so does <see cref="SimObject.RunInto"/> —
	/// which is latched by anything that ever walked into this machine and never lowered again, so a
	/// machine that was bumped earlier in the mission detonates on its first tick in the state.</para>
	/// </summary>
	private void RamTick(SimWorld world) {
		IntegrateMotion();

		var moved = Position;
		Position = new Vec3i(moved.X, moved.Y,
			world.Terrain.HeightAtWorld(moved.X, moved.Y) + Type.RideHeight);

		PlaceLegsOnGround(world);

		if (!CollisionTest(world) && !RunInto) {
			return;
		}

		HitTests.ExplosiveBlastSweep(world, Position, RamBlastRadius, RamBlastDamage, null, this);

		// Every component the machine still has, in index order. ComponentDamageWrite makes the
		// active-flag test the original's loop makes for itself before each call.
		for (short component = 0; component < ComponentDamage.MechComponentCount; component++) {
			ComponentDamageWrite(world, component, RamSelfDestructDamage, this);
		}
	}

	// The ramming state's share of the behaviour block's scratch: the retarget countdown at
	// block+0x0d, the phase flag at block+0x12 and the phase countdown at block+0x15.
	private int _ramRetargetTimer;
	private bool _ramCharging;
	private short _ramPhaseTimer;
}
