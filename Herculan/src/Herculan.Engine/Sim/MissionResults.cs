using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// What the simulator leaves the shell as a mission ends: <c>data\results.dat</c> and the mission counters
/// written back over <c>data\mission.var</c>, both by <c>Mission_WriteResults</c> (<c>0042412c</c>), and the
/// exit code <c>Sim_Shutdown</c> (<c>00461eec</c>) returns. See
/// docs/retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c for the file and
/// docs/retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7 for its reader.
/// </summary>
public static class MissionResults {
	/// <summary>The results file's name, written beside <c>mission.var</c>.</summary>
	public const string FileName = "results.dat";

	/// <summary><c>Sim_Shutdown</c>'s exit codes (docs/retail/command-line.md#exit-codes).</summary>
	public const int QuitExitCode = 0;
	public const int DebriefExitCode = 3;
	public const int DemoExitCode = 6;

	/// <summary>
	/// <c>Mission_WriteResults</c> (<c>0042412c</c>): the <c>results.dat</c> bytes, and the 2000 bytes of
	/// counters for <c>mission.var</c>, for the mission as it stands. It asks the objectives once more, which
	/// applies the counters of any record newly met before they are written.
	/// </summary>
	public static (byte[] Results, byte[] Counters) Write(SimWorld world, MechObject player) {
		var results = new MemoryStream();
		var writer = new BinaryWriter(results);

		bool won = world.Objectives.EvaluateObjectives(world, player) == MissionStatus.Complete;
		writer.Write((short)(won ? 1 : 0));

		int award = SimMath.Q10Multiply(SalvageScale, TotalSalvage(world, player))
			+ world.MissionCounters[World.MissionLoader.SalvageBonusCounter] * SalvageBonusKilograms;
		writer.Write(award);

		writer.Write((short)world.Salvage.Count);
		foreach (var (weaponId, condition) in world.Salvage) {
			writer.Write(weaponId);
			writer.Write(condition);
		}

		if (player.Group is { } group) {
			WriteStatusBlocks(writer, group);
		}

		writer.Flush();

		var counters = new byte[world.MissionCounters.Count * 2];
		for (int i = 0; i < world.MissionCounters.Count; i++) {
			counters[i * 2] = (byte)world.MissionCounters[i];
			counters[i * 2 + 1] = (byte)(world.MissionCounters[i] >> 8);
		}

		return (results.ToArray(), counters);
	}

	/// <summary>
	/// <c>Sim_Shutdown</c>'s exit code, <c>004d283c</c>: 6 after a demo, 0 when the game is being quit, and
	/// otherwise 3, the shell's debrief. The original answers 4 instead of 3 for a destroyed player while
	/// <c>MissionModeFlag</c> (<c>004a9ed6</c>) is set, and the load zeroes that header field
	/// (<see cref="World.ScriptDatHeader"/>), so 4 is never returned.
	/// </summary>
	public static int ExitCode(bool demo, bool quitting) => demo ? DemoExitCode : quitting ? QuitExitCode : DebriefExitCode;

	/// <summary>
	/// <c>Mission_TotalSalvage</c> (<c>00423e88</c>) — <see cref="MechObject.SalvageValue"/> summed over every
	/// machine not on the player's side that is destroyed or immobilised, walked from the end of the machine
	/// list as <c>Pool_Prev</c> walks it; the order is the order their mounts join the salvage list.
	/// </summary>
	private static int TotalSalvage(SimWorld world, MechObject player) {
		int total = 0;
		for (int i = world.Objects.Count - 1; i >= 0; i--) {
			if (world.Objects[i] is MechObject mech && mech.Side != player.Side && (mech.Immobilised || mech.Destroyed)) {
				total += mech.SalvageValue(world);
			}
		}

		return total;
	}

	/// <summary>
	/// <c>Group_WriteStatusBlocks</c> (<c>00423d68</c>) — one 72-byte block per member of the player's group, in
	/// group order: 33 conditions from the damage readouts (<see cref="ComponentDamage.ReadDamageReadouts"/>
	/// entries 1-13, 20-29 and 32-41, each as <see cref="MechObject.SalvageCondition"/>), which the shell reads
	/// over the machine's 66-byte status block, then the member's Herc, Base and Flyer kills
	/// (<see cref="MechObject.KillsOf"/>).
	/// </summary>
	private static void WriteStatusBlocks(BinaryWriter writer, MissionGroup group) {
		foreach (var member in group.Members) {
			var mech = member as MechObject;
			short[] readouts = mech?.Damage?.ReadDamageReadouts() ?? new short[ComponentDamage.ReadoutCount];
			foreach (var (first, count) in StatusBlockRuns) {
				for (int i = first; i < first + count; i++) {
					writer.Write(MechObject.SalvageCondition(readouts[i]));
				}
			}

			writer.Write(mech?.KillsOf(TargetClass.Herc) ?? (short)0);
			writer.Write(mech?.KillsOf(TargetClass.Structure) ?? (short)0);
			writer.Write(mech?.KillsOf(TargetClass.Flyer) ?? (short)0);
		}
	}

	/// <summary>The three runs of readouts a status block carries: 13 components, 10 dependents, 10 mounts.</summary>
	private static readonly (int First, int Count)[] StatusBlockRuns = {
		(ComponentDamage.FirstArmorReadout, 13), (ComponentDamage.FirstDependentReadout, 10),
		(ComponentDamage.FirstCombinedReadout, ComponentDamage.CombinedReadoutCount),
	};

	/// <summary>The Q10 scale on the wrecks' total, the literal <c>0x9c4</c> (2500): about 2.44 kg a point.</summary>
	private const int SalvageScale = 0x9c4;

	/// <summary>What one unit of mission counter 20 adds to the award, in kilograms.</summary>
	private const int SalvageBonusKilograms = 25000;
}
