using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// <c>ramming</c>, the state a group's order 3 installs: the think that chooses a target and alternates approaching with
/// charging, and the move that detonates the machine when anything blocks it (docs/es1/simulation/mech-ai.md#ramming,
/// which links ES2's).
/// </summary>
public static class Es1Ramming {
	/// <summary>The scratch's retarget countdown (handle <c>+0x54</c>), phase word, and phase countdown (handle <c>+0x5b</c>).</summary>
	public const int RetargetOffset = 0x55, ChargingOffset = 0x59, PhaseOffset = 0x5c;

	/// <summary>The retarget countdown's reload.</summary>
	public const int RetargetInterval = 10000;

	/// <summary>The target choice's mask: no shooting weight and no crowding divisor.</summary>
	public const short TargetMask = Es1Targets.NoShootingWeight | Es1Targets.NoCrowding;

	/// <summary>A charge's length, <see cref="ChargeBase"/> + a draw below <see cref="ChargeSpread"/>, and an approach's.</summary>
	public const short ChargeBase = 3000, ChargeSpread = 1500, ApproachBase = 8000, ApproachSpread = 4000;

	/// <summary>The avoidance argument every steer of the state passes <c>Mech_LocomotionTick</c>.</summary>
	public const short AvoidanceArgument = 1;

	/// <summary>The blast's damage and radius, and the damage written to each of the rammer's components.</summary>
	public const short BlastDamage = 2000, BlastRadius = 3000, ComponentDamage = 32000;

	/// <summary>
	/// <c>Mech_BehaviourRamThink</c> (<c>00018320</c>), <c>ramming</c>. The retarget countdown is stepped
	/// (<c>Timer_CountDown</c>); when it runs out, <see cref="Es1Targets.SelectTarget"/> with <see cref="TargetMask"/> is
	/// installed (<see cref="Es1Mech.InstallTarget"/>) and the countdown reloaded with <see cref="RetargetInterval"/>. With
	/// no target, <c>Mech_LocomotionTick</c>(machine, <see cref="AvoidanceArgument"/>, 0, 0). With one, the steer at it; then
	/// approaching (the phase word clear), the phase countdown stepped (<c>Math_CountdownTimerTick</c>), and at 0 the phase
	/// word set and the countdown loaded with <see cref="ChargeBase"/> + <c>Math_RandomBelow</c>(<see cref="ChargeSpread"/>);
	/// charging, <see cref="RamTick"/>, the steer, <see cref="RamTick"/>, the steer, then the phase countdown stepped, and at
	/// 0 the phase word cleared and the countdown loaded with <see cref="ApproachBase"/> +
	/// <c>Math_RandomBelow</c>(<see cref="ApproachSpread"/>). It answers 0.
	/// </summary>
	public static short RamThink(Es1Mech mech, Es1SimGlobals globals) {
		var block = mech.Behaviour;
		var clock = globals.Clock;
		int retarget = block.ScratchInt32(RetargetOffset);
		clock.TimerCountDown(ref retarget);
		block.SetScratchInt32(RetargetOffset, retarget);
		if (retarget == 0) {
			mech.InstallTarget(Es1Targets.SelectTarget(mech, TargetMask, globals));
			block.SetScratchInt32(RetargetOffset, RetargetInterval);
		}

		if (mech.Target is not { } target) {
			mech.Locomotion.LocomotionTick(globals, AvoidanceArgument, 0, 0);
			return 0;
		}

		// Retail reads the target again before each steer; nothing between them writes +0x15e, so it is read once here.
		Steer(mech, target, globals);
		if (block.ScratchInt16(ChargingOffset) == 0) {
			short phase = block.ScratchInt16(PhaseOffset);
			clock.CountdownTimerTick(ref phase);
			block.SetScratchInt16(PhaseOffset, phase);
			if (phase == 0) {
				short draw = (short)globals.Random.NextBelow(ChargeSpread);
				block.SetScratchInt16(ChargingOffset, 1);
				block.SetScratchInt16(PhaseOffset, (short)(draw + ChargeBase));
			}
		} else {
			// A detonation here leaves the think running on: the second step can detonate again, and the writes below land in
			// the scratch of the state the damage write installed.
			RamTick(mech, globals);
			Steer(mech, target, globals);
			RamTick(mech, globals);
			Steer(mech, target, globals);
			short phase = block.ScratchInt16(PhaseOffset);
			clock.CountdownTimerTick(ref phase);
			block.SetScratchInt16(PhaseOffset, phase);
			if (phase == 0) {
				short draw = (short)globals.Random.NextBelow(ApproachSpread);
				block.SetScratchInt16(ChargingOffset, 0);
				block.SetScratchInt16(PhaseOffset, (short)(draw + ApproachBase));
			}
		}

		return 0;
	}

	/// <summary>
	/// <c>Mech_BehaviourRamTick</c> (<c>00018278</c>), <c>ramming</c>'s move: <c>Mech_IntegrateMotion</c>,
	/// <see cref="Es1Footfalls.PlaceLegsOnGround"/>, and when <see cref="Es1MechMovement.CollisionTest"/>'s low 16 bits
	/// are not 0 or <see cref="Es1Mech.RanInto"/> is set, <see cref="Es1MechDamage.ExplosiveBlastSweep"/>(<see cref="BlastDamage"/>,
	/// <see cref="BlastRadius"/>, its position), which spares no machine in range, this one included, then
	/// <see cref="Es1MechDamage.ComponentDamageWrite"/>(machine, machine, <see cref="ComponentDamage"/>, <i>i</i>) for each
	/// component <i>i</i> whose occupancy flag is set as the loop reaches it. Unlike the walking move it keeps a refused
	/// step. It answers 1.
	/// </summary>
	public static short RamTick(Es1Mech mech, Es1SimGlobals globals) {
		mech.Locomotion.IntegrateMotion(globals.Clock);
		Es1Footfalls.PlaceLegsOnGround(mech);
		if ((short)Es1MechMovement.CollisionTest(mech, globals) != 0 || mech.RanInto) {
			Es1MechDamage.ExplosiveBlastSweep(BlastDamage, BlastRadius, mech.Position, globals);
			// Every HERC is built with its damage header; one without is taken as having no component, an engine guard.
			if (mech.Damage is { } header) {
				for (short component = 0; component < Es1DamageHeader.ComponentCount; component++) {
					if (header.Occupied[component]) {
						Es1MechDamage.ComponentDamageWrite(mech, mech, ComponentDamage, component, globals);
					}
				}
			}
		}

		return 1;
	}

	/// <summary>
	/// The steer at <paramref name="target"/>: <c>Mech_LocomotionTick</c>(machine, <see cref="AvoidanceArgument"/>,
	/// <see cref="Es1Navigation.Full"/>, the bearing error shifted right by <see cref="Es1Navigation.SteerShift"/>).
	/// </summary>
	private static void Steer(Es1Mech mech, IEs1SimObject target, Es1SimGlobals globals) {
		short error = (short)(Es1MissionGroup.HeadingToward(mech.Position, target.Position) - mech.Heading);
		mech.Locomotion.LocomotionTick(globals, AvoidanceArgument, Es1Navigation.Full, (short)(error >> Es1Navigation.SteerShift));
	}
}
