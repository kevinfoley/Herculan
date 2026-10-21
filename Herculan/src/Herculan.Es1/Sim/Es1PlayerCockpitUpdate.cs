using Herculan.Es1.View;

namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Player_PerFrameCockpitUpdate</c> (<c>ES1DBSIM 000155a8</c>), (machine), from <c>Sim_RenderFrame</c> after the 3D
/// pass and from <c>Sim_StartMission</c>'s step 13 (docs/es1/simulation/mech.md#the-cockpit): the instruments the machine
/// feeds, then the gunsight's state block, which makes the gunsight's selection the machine's target.
/// </summary>
public static class Es1PlayerCockpitUpdate {
	/// <summary>The pool that fills the energy meter, the divisor of its Q10 fraction.</summary>
	public const int FullEnergy = 10000;

	/// <summary>
	/// The update on <paramref name="machine"/> through <paramref name="cockpit"/>, <c>CockpitViewInstance</c>: step 1's
	/// energy meter value; then a copy of the gunsight's block takes the pitch, and unless the machine is immobilized
	/// (<c>+0x9e</c>) the heading and the twist; when its selection at offset 6 is not the machine's target, the lamp is
	/// cleared, the holders' counts moved, the selection made the target with <see cref="Es1Mech.TargetChanged"/> set, and
	/// with no target and TRACK's latch set <c>g_CenterTurretMode</c> set; then the lamp at offset <c>0x25</c> and the copy
	/// to <c>Gunsight_SetValues</c>.
	/// </summary>
	public static void Run(Es1Mech machine, Es1Cockpit cockpit) {
		var globals = cockpit.Globals;

		// 1. The energy meter's value, which 00039b78 hands the meter at the cockpit's +0x1e1. PLACEHOLDER: 00039b78 and the
		// meter, plan step 6c's instruments. PLACEHOLDER: Shield_BalanceInputRead (0000fbb4)(+0x1b8), the shields of plan
		// step 7. PLACEHOLDER: WeaponMounts_PerFrameUpdate (0000cf78)(+0x198, the distance to the target or 0), which reads
		// the console panel and pushes the weapon gauges: plan steps 6c and 7.
		globals.EnergyMeterValue = (machine.Energy << 10) / FullEnergy;

		// 2. PLACEHOLDER: the throttle exchange with the gauge at +0x1ed through 0003b250 and 0003b200, the gauge's value to
		// the throttle +0x226 or with +0x8d set the throttle to it, and the speed's fraction to it: plan step 6b2.

		// 3.
		var gunsight = cockpit.Gunsight;
		var block = gunsight.GetStateBlock().Copy();
		block.Pitch = machine.Pitch;
		if (!machine.Immobilized) {
			block.Heading = machine.Heading;
			block.Twist = machine.Twist;
		}

		if (!ReferenceEquals(machine.Target, block.Selection)) {
			machine.LockLamp = false;
			if (machine.Target != null) {
				machine.Target.TargetHolders--;
			}

			machine.Target = block.Selection;
			if (machine.Target != null) {
				machine.Target.TargetHolders++;
			}

			machine.TargetChanged = true;
			// Retail's machine always holds a manager; this one has none until Mech_ConfigureLoadout has run.
			if (block.Selection == null && machine.Mounts?.TrackLatch == true) {
				globals.CenterTurretMode = true;
			}
		}

		// 4.
		block.LockLamp = machine.LockLamp;
		gunsight.SetValues(block);
	}
}
