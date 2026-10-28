using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// <c>skirting</c>, the excursion a machine makes round whatever stopped its shot short of its target
/// (docs/es1/simulation/mech-ai.md#skirting, which links ES2's): the latch the shot sweep sets, the gate at the head of the
/// combat thinks that turns it into the state, the state's think and its line-of-sight test.
/// </summary>
public static class Es1Skirting {
	/// <summary>The scratch's first-tick word, side word, re-test countdown (handle <c>+0x5c</c>) and range to keep.</summary>
	public const int StartedOffset = 0x58, SideOffset = 0x5a, RetestOffset = 0x5d, RangeOffset = 0x61;

	/// <summary>The re-test countdown's reload: the line of sight is tried again each time it runs out.</summary>
	public const int RetestInterval = 5000;

	/// <summary>
	/// <c>Mech_AiOnLineOfFireBlocked</c> (<c>00017a80</c>), the HERC's slot <c>+0x68</c>: <paramref name="blocker"/> stored
	/// in <see cref="Es1Mech.LineOfFireBlocker"/> (<c>+0x246</c>) and <see cref="Es1Mech.LineOfFireBlocked"/>
	/// (<c>+0xa7</c>) set.
	/// </summary>
	public static void OnLineOfFireBlocked(Es1Mech mech, IEs1Placed blocker) {
		mech.LineOfFireBlocker = blocker;
		mech.LineOfFireBlocked = true;
	}

	/// <summary>
	/// <c>Ai_BeginSkirtIfBlocked</c> (<c>00017bf8</c>)(machine): with <see cref="Es1Mech.LineOfFireBlocked"/> set,
	/// <c>skirting</c> installed through <c>Behaviour_SetState</c>, the descriptor it replaced stashed
	/// (<see cref="Es1BehaviourBlock.Stashed"/>, <c>+0x54</c>) and the 3D range from the target to the machine
	/// (<c>Math_DistanceBetweenPoints</c>) written to <see cref="RangeOffset"/>; it answers true. Otherwise false.
	/// </summary>
	public static bool BeginSkirtIfBlocked(Es1Mech mech, Es1SimGlobals globals) {
		if (!mech.LineOfFireBlocked) {
			return false;
		}

		var block = mech.Behaviour;
		var stashed = block.Descriptor;
		block.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Skirting], globals.DwellJitter);
		block.Stashed = stashed;
		// Retail reads the target unchecked, and the combat thinks run this gate before their own test for one; with none
		// the range is 0 here, an engine guard.
		block.SetScratchInt32(RangeOffset, mech.Target is { } target ? target.Position.ApproxDistanceTo(mech.Position) : 0);
		return true;
	}

	/// <summary>
	/// <c>Mech_BehaviourSkirtThink</c> (<c>00017aa4</c>), <c>skirting</c>, with <i>b</i> the blocker's position
	/// (<see cref="Es1Mech.LineOfFireBlocker"/>) read each tick. On the first tick (<see cref="StartedOffset"/> clear) the
	/// side word is set to whether the bearing to <i>b</i> less the heading is negative, the first-tick word set and the
	/// re-test countdown loaded with <see cref="RetestInterval"/>. The countdown is then stepped (<c>Timer_CountDown</c>);
	/// when it runs out it is reloaded, and when <see cref="LineOfSightBlocked"/> answers false the stashed descriptor is
	/// installed and <see cref="Es1Mech.LineOfFireBlocked"/> cleared. Every tick, the new state's zeroed scratch included,
	/// it walks at <see cref="Es1Navigation.Full"/> toward the point the range word (<see cref="RangeOffset"/>) from
	/// <i>b</i> at the bearing from <i>b</i> to the machine + <c>0x4000</c>, or − for a set side word, steering by the
	/// bearing error, and runs <c>Mech_CenterTorsoTick</c>(machine, 0). It answers 0.
	/// </summary>
	public static short SkirtThink(Es1Mech mech, Es1SimGlobals globals) {
		// +0xa7 is set only with +0x246, so a machine in skirting holds a blocker; with none, nothing here, an engine guard.
		if (mech.LineOfFireBlocker is not { } blocker) {
			return 0;
		}

		var block = mech.Behaviour;
		var at = blocker.Position;
		short fromBlocker = Es1MissionGroup.HeadingToward(at, mech.Position);
		if (block.ScratchInt16(StartedOffset) == 0) {
			short toBlocker = (short)(Es1MissionGroup.HeadingToward(mech.Position, at) - mech.Heading);
			block.SetScratchInt16(SideOffset, toBlocker < 0 ? (short)1 : (short)0);
			block.SetScratchInt16(StartedOffset, 1);
			block.SetScratchInt32(RetestOffset, RetestInterval);
		}

		int retest = block.ScratchInt32(RetestOffset);
		globals.Clock.TimerCountDown(ref retest);
		block.SetScratchInt32(RetestOffset, retest);
		if (retest == 0) {
			block.SetScratchInt32(RetestOffset, RetestInterval);
			// The stash is set whenever skirting is installed, the gate being its only installer; with none the state
			// is kept here, an engine guard.
			if (!LineOfSightBlocked(mech, globals) && block.Stashed is { } stashed) {
				block.SetState(stashed, globals.DwellJitter);
				mech.LineOfFireBlocked = false;
			}
		}

		short side = block.ScratchInt16(SideOffset) == 0 ? (short)0x4000 : unchecked((short)0xc000);
		var point = Es1CombatStates.OffsetPointByBearing(at, (short)(fromBlocker + side), block.ScratchInt32(RangeOffset));
		short error = (short)(Es1MissionGroup.HeadingToward(mech.Position, point) - mech.Heading);
		mech.Locomotion.LocomotionTick(globals, 0, Es1Navigation.Full, (short)(error >> Es1Navigation.SteerShift));
		mech.Turret.CenterTorsoTick(globals.Clock, globals.TurretServos, 0);
		return 0;
	}

	/// <summary>
	/// <c>Ai_LineOfSightBlocked</c> (<c>00017a00</c>)(machine): true when
	/// <see cref="Es1ObstacleAvoidance.StructureRaycastShapes"/>(the machine's group's side byte, sign-extended, the target's
	/// position, the machine's) or <c>Sim_RaycastShapes</c> (<c>00020b70</c>)(0, the target's position, the machine's)
	/// against the zone's shapes answers non-zero; otherwise false.
	/// </summary>
	public static bool LineOfSightBlocked(Es1Mech mech, Es1SimGlobals globals) {
		// Retail reads the target and the group unchecked; with no target the line is taken as clear, and with no group
		// the side as 0, engine guards.
		if (mech.Target is not { } target) {
			return false;
		}

		short side = (sbyte)(mech.Group?.Side ?? 0);
		return Es1ObstacleAvoidance.StructureRaycastShapes(side, target.Position, mech.Position, globals, out _)
			|| globals.ZoneShapes.RaycastShapes(0, target.Position, mech.Position, out _);
	}
}
