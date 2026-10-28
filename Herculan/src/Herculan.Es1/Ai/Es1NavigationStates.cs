using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// The thinks of the states a group's order installs (docs/es1/simulation/mech-ai.md#navigation, which links ES2's
/// derivation): <c>patrolling</c>, <c>search destroy</c>, <c>travelling</c> and <c>bulldog travel</c>, <c>sleeping</c> and
/// <c>guarding</c>. Each answers 0, so none ends its state. The countdowns are in the behaviour block's scratch
/// (<see cref="Es1BehaviourBlock.Scratch"/>), the counter at <c>+0x55</c> of the handle at <c>+0x54</c>, which
/// <c>Behaviour_SetState</c> zeroes, so a state's first tick acquires at once.
/// </summary>
public static class Es1NavigationStates {
	/// <summary>The counter of the states' countdown, at machine offset <c>+0x55</c>.</summary>
	public const int CountdownOffset = 0x55;

	/// <summary>The countdown's reload, about 3.9 seconds.</summary>
	public const short LookInterval = 10000;

	/// <summary>The group order on which <c>patrolling</c> acquires with no squad verb.</summary>
	public const short PatrolAcquireOrder = 8;

	/// <summary>The squad verb on which <c>patrolling</c> acquires.</summary>
	public const short PatrolSquadVerb = 2;

	/// <summary>The squad verb whose target <c>guarding</c> takes, when it has one.</summary>
	public const short EngageSquadVerb = 4;

	/// <summary>The ground ranges of <c>guarding</c>'s post: beyond the outer it walks to the post; within the inner it walks away.</summary>
	public const int GuardOuterRange = 20000, GuardInnerRange = 10000;

	/// <summary>
	/// <c>Mech_BehaviourPatrolThink</c> (<c>ES1DBSIM 000176bc</c>), <c>patrolling</c>: the squad verb read first,
	/// <see cref="Es1Navigation.NavigationStep"/>, the target released; a machine that is not its group's first member and
	/// had no squad verb is done. Otherwise, once the 16-bit countdown (<c>Math_CountdownTimerTick</c>) has run out, it is
	/// reloaded with <see cref="LookInterval"/> and <see cref="Acquire"/> runs, acquiring for the order
	/// <see cref="PatrolAcquireOrder"/> with no squad verb or for the squad verb <see cref="PatrolSquadVerb"/>, and the
	/// target installed whatever it finds.
	/// </summary>
	public static short PatrolThink(Es1Mech mech, Es1SimGlobals globals) {
		short verb = mech.SquadVerb;
		Es1Navigation.NavigationStep(mech, globals);
		mech.ReleaseTarget();
		if (!ReferenceEquals(Es1Navigation.Leader(mech), mech) && verb == 0) {
			return 0;
		}

		if (!LookAgain(mech, globals)) {
			return 0;
		}

		// Retail reads the group's order through +0x3f unchecked; with no group, none here.
		bool acquires = (verb == 0 && mech.Group?.Order == PatrolAcquireOrder) || verb == PatrolSquadVerb;
		Acquire(mech, acquires, installAny: true, globals);
		return 0;
	}

	/// <summary>
	/// <c>Mech_BehaviourSearchDestroyThink</c> (<c>000174ec</c>), <c>search destroy</c>: <see cref="Es1Navigation.NavigationStep"/>,
	/// the target released; a machine that is not its group's first member is done. Otherwise, once the countdown has run
	/// out, <see cref="Acquire"/>, installing only a target <c>Group_IsOrderTarget</c> passes.
	/// </summary>
	public static short SearchDestroyThink(Es1Mech mech, Es1SimGlobals globals) {
		Es1Navigation.NavigationStep(mech, globals);
		mech.ReleaseTarget();
		if (!ReferenceEquals(Es1Navigation.Leader(mech), mech)) {
			return 0;
		}

		if (LookAgain(mech, globals)) {
			Acquire(mech, acquires: true, installAny: false, globals);
		}

		return 0;
	}

	/// <summary>
	/// <c>Mech_BehaviourTravelThink</c> (<c>000178a4</c>), <c>travelling</c> and <c>bulldog travel</c>:
	/// <see cref="Es1Navigation.FollowRoute"/> for every member, the target released; once the 32-bit countdown
	/// (<c>Timer_CountDown</c>) has run out, <c>Ai_SelectTarget</c>(machine, 0) into the look-at
	/// (<see cref="Es1BehaviourBlock.LookAt"/>, <c>+0x59</c>), the countdown <see cref="LookInterval"/>, and the radar
	/// <c>+0x90</c> on with a look-at or the radar setting <c>+0x91</c>, off otherwise. With a look-at,
	/// <c>Ai_AimAndFire</c>(machine, look-at, its twist (slot <c>+0x40</c>) + the bearing from it to the machine less its
	/// heading).
	/// </summary>
	public static short TravelThink(Es1Mech mech, Es1SimGlobals globals) {
		Es1Navigation.FollowRoute(mech, globals);
		mech.ReleaseTarget();
		var block = mech.Behaviour;
		int countdown = block.ScratchInt32(CountdownOffset);
		globals.Clock.TimerCountDown(ref countdown);
		block.SetScratchInt32(CountdownOffset, countdown);
		if (countdown == 0) {
			block.LookAt = Es1Targets.SelectTarget(mech, 0, globals);
			block.SetScratchInt32(CountdownOffset, LookInterval);
			mech.RadarOn = block.LookAt != null || mech.RadarSetting;
		}

		if (block.LookAt is { } watched) {
			short aspect = (short)(watched.TorsoTwistAngle
				+ (short)(Es1MissionGroup.HeadingToward(watched.Position, mech.Position) - watched.Heading));
			Es1AiStandIns.AimAndFire(mech, watched, aspect, globals);
		}

		return 0;
	}

	/// <summary>
	/// <c>Mech_BehaviourSleepThink</c> (<c>00016248</c>), <c>sleeping</c>: the target released and the control law with
	/// nothing asked, so the machine stands.
	/// </summary>
	public static short SleepThink(Es1Mech mech, Es1SimGlobals globals) {
		mech.ReleaseTarget();
		mech.Locomotion.LocomotionTick(globals, 0, 0, 0);
		return 0;
	}

	/// <summary>
	/// <c>Mech_BehaviourGuardThink</c> (<c>00017ed4</c>), <c>guarding</c>: the post is <see cref="Es1Mech.AiGoalPosition"/>,
	/// read first; the target released. While the 16-bit countdown runs, or for a machine with no squad verb whose group's
	/// first member is <c>PlayerMech</c>, <see cref="Es1Navigation.NavigationStep"/> and done; the countdown is stepped
	/// either way. Otherwise the squad order's target for the verb <see cref="EngageSquadVerb"/> with one, or, with the
	/// countdown reloaded with <see cref="LookInterval"/>, <see cref="SelectGuardTarget"/>; with a target,
	/// <see cref="EngageGuardTarget"/>. With nothing found:
	/// the turret centred, <see cref="Es1Navigation.UpdateWeaponsFree"/>, and beyond <see cref="GuardOuterRange"/> of the
	/// post <see cref="Es1Navigation.DriveToPoint"/> to it; nearer, the steer of the bearing error less a half turn, so the
	/// machine faces away from the post, walking at full speed within <see cref="GuardInnerRange"/> and standing beyond.
	/// </summary>
	public static short GuardThink(Es1Mech mech, Es1SimGlobals globals) {
		var post = mech.AiGoalPosition();
		mech.ReleaseTarget();
		var block = mech.Behaviour;
		short countdown = block.ScratchInt16(CountdownOffset);
		globals.Clock.CountdownTimerTick(ref countdown);
		block.SetScratchInt16(CountdownOffset, countdown);
		if (countdown != 0 || (Es1Navigation.Leader(mech) is { } leader && ReferenceEquals(leader, globals.PlayerMech)
				&& mech.SquadVerb == 0)) {
			Es1Navigation.NavigationStep(mech, globals);
			return 0;
		}

		IEs1SimObject? found;
		if (mech.SquadVerb == EngageSquadVerb && mech.SquadTarget != null) {
			found = mech.SquadTarget;
		} else {
			block.SetScratchInt16(CountdownOffset, LookInterval);
			found = SelectGuardTarget(mech, post, globals);
		}

		if (found != null) {
			EngageGuardTarget(mech, found, globals);
			return 0;
		}

		mech.Turret.CenterTorsoTick(globals.Clock, globals.TurretServos, 0);
		Es1Navigation.UpdateWeaponsFree(mech);
		if (post is not { } point) {
			// PLACEHOLDER: retail reads the post through the group's target with no test. Here there is none where that
			// target is a structure or flyer, which the slice does not build until the plan's flyers-and-bases step, or a
			// kind with no route; the machine stands, asking nothing of the control law.
			mech.Locomotion.LocomotionTick(globals, 0, 0, 0);
			return 0;
		}

		int distance = Es1PlayerThink.DistanceBetweenPoints2D(point, mech.Position);
		if (distance > GuardOuterRange) {
			Es1Navigation.DriveToPoint(mech, point, globals);
			return 0;
		}

		short bearing = Es1MissionGroup.HeadingToward(mech.Position, point);
		short speed = distance < GuardInnerRange ? Es1Navigation.Full : (short)0;
		short steer = (short)((short)(bearing - mech.Heading - 0x8000) >> Es1Navigation.SteerShift);
		mech.Locomotion.LocomotionTick(globals, 0, speed, steer);
		return 0;
	}

	/// <summary><c>guarding</c>'s scoring limits: the ground range from the post a machine scores from, and the one when the group has a target (<c>+0x1c</c>).</summary>
	public const int GuardScoreLimit = 980000, GuardScoreLimitWithTarget = 945000;

	/// <summary>
	/// <c>guarding</c>'s look: over <c>g_MechPool</c>, oldest first as <c>Pool_Prev</c> walks it, each machine
	/// <see cref="Es1Targets.IsTargetable"/>(machine, 0) passes that is not immobilized scores the limit
	/// (<see cref="GuardScoreLimit"/>, or <see cref="GuardScoreLimitWithTarget"/> when the group has a target) less its
	/// ground range to the post (<c>Math_DistanceBetweenPoints2D</c>), divided by one more than its holders when it has any,
	/// and only a positive score counts. A machine neither disarmed nor destroyed displaces one that is, whatever the scores,
	/// and one that is is passed over once one that is not is held; otherwise the higher score wins.
	/// </summary>
	private static IEs1SimObject? SelectGuardTarget(Es1Mech mech, Vec3i? post, Es1SimGlobals globals) {
		// Retail reads the post and the group unchecked; with either missing nothing is found here, an engine guard.
		if (post is not { } point || mech.Group is not { } group) {
			return null;
		}

		int limit = group.Target == null ? GuardScoreLimit : GuardScoreLimitWithTarget;
		Es1Mech? best = null;
		int bestScore = 0;
		bool bestLive = false;
		foreach (var candidate in globals.MechPool) {
			if (!Es1Targets.IsTargetable(mech, 0, candidate, globals) || candidate.Immobilized) {
				continue;
			}

			bool live = !(candidate.Disarmed || candidate.Immobilized || candidate.Destroyed);
			if (bestLive && !live) {
				continue;
			}

			int score = limit - Es1PlayerThink.DistanceBetweenPoints2D(point, candidate.Position);
			if (score <= 0) {
				continue;
			}

			if (candidate.TargetHolders != 0) {
				score /= candidate.TargetHolders + 1;
			}

			if (bestScore < score || (!bestLive && live)) {
				best = candidate;
				bestScore = score;
				bestLive = live;
			}
		}

		return best;
	}

	/// <summary>
	/// <c>guarding</c>'s arm for a target found: the target installed; a machine disarmed, immobilized or destroyed takes
	/// <c>fleeing</c> with the aim slot −1; any other <c>driving off en</c>, <see cref="Es1CombatReassess.SelectAimComponent"/>
	/// and the radar on unless it is in a group the player's machine leads without <see cref="Es1Mech.RadarKeptOn"/>.
	/// </summary>
	private static void EngageGuardTarget(Es1Mech mech, IEs1SimObject target, Es1SimGlobals globals) {
		mech.InstallTarget(target);
		if (mech.OutOfAction) {
			mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Fleeing], globals.DwellJitter);
			mech.AimSlot = -1;
			return;
		}

		mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.DrivingOff], globals.DwellJitter);
		Es1CombatReassess.SelectAimComponent(mech, globals);
		if (Es1Navigation.Leader(mech) is not { IsPlayer: true } || mech.RadarKeptOn) {
			mech.RadarOn = true;
		}
	}

	/// <summary>
	/// The 16-bit countdown stepped by <c>Math_CountdownTimerTick</c>; once it has run out, reloaded with
	/// <see cref="LookInterval"/>. Answers whether it ran out.
	/// </summary>
	private static bool LookAgain(Es1Mech mech, Es1SimGlobals globals) {
		var block = mech.Behaviour;
		short countdown = block.ScratchInt16(CountdownOffset);
		globals.Clock.CountdownTimerTick(ref countdown);
		block.SetScratchInt16(CountdownOffset, countdown);
		if (countdown != 0) {
			return false;
		}

		block.SetScratchInt16(CountdownOffset, LookInterval);
		return true;
	}

	/// <summary>
	/// The acquisition <c>patrolling</c> and <c>search destroy</c> share. A machine out of action takes
	/// <c>Ai_SelectTarget</c>(machine, 0), installed whatever it finds, and with a target the aim slot −1 and
	/// <c>fleeing</c>. Any other, when <paramref name="acquires"/>, takes <c>Ai_SelectTarget</c>(machine, 1); with
	/// <paramref name="installAny"/> it installs what it finds and enters combat with a target, otherwise it installs and
	/// enters combat only with a target <c>Group_IsOrderTarget</c> passes. Entering combat sets <c>+0xa6</c> and runs
	/// <c>Mech_AiEnterCombat</c>.
	/// </summary>
	private static void Acquire(Es1Mech mech, bool acquires, bool installAny, Es1SimGlobals globals) {
		if (mech.OutOfAction) {
			mech.InstallTarget(Es1Targets.SelectTarget(mech, 0, globals));
			if (mech.Target != null) {
				mech.AimSlot = -1;
				mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Fleeing], globals.DwellJitter);
			}

			return;
		}

		if (!acquires) {
			return;
		}

		var found = Es1Targets.SelectTarget(mech, 1, globals);
		// Retail reads the group through +0x3f unchecked; a machine with no group has no order target here.
		if (installAny) {
			mech.InstallTarget(found);
		} else if (found == null || mech.Group?.IsOrderTarget(found) != true) {
			return;
		} else {
			mech.InstallTarget(found);
		}

		if (mech.Target != null) {
			mech.TargetHandedOver = true;
			Es1CombatReassess.EnterCombat(mech, globals);
		}
	}
}
