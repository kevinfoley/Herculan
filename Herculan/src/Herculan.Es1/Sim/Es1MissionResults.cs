using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// What the simulator leaves the shell as the mission ends: <c>data\results.dat</c> and the flags written back over
/// <c>data\mission.var</c>, both by <c>Mission_WriteResults</c> (<c>ES1DBSIM 0001d698</c>), with the two functions of
/// the groups' module it calls, <c>Mission_TotalSalvage</c> and <c>Group_WriteStatusBlocks</c>
/// (docs/es1/simulation/world.md#writing-the-results--mission_writeresults-0001d698,
/// docs/es1/simulation/mission-groups.md#what-the-results-read). <c>HercWorks.Core</c> has no model of
/// <c>results.dat</c>, so the bytes are built here, each from the doc.
/// </summary>
public static class Es1MissionResults {
	/// <summary>The results file, entry 7 of the shell's file-name table (docs/es1/formats/eng-mission-text.md#the-debriefing).</summary>
	public const string ResultsName = "results.dat";

	/// <summary>The Q10 scale <c>Mission_WriteResults</c> puts on the wrecks' total, the literal <c>0x9c4</c>.</summary>
	public const short SalvageScale = 0x9c4;

	/// <summary>What each unit of flag 7 adds to the award.</summary>
	public const int SalvageBonusPerFlag = 25000;

	/// <summary>The flag whose units add <see cref="SalvageBonusPerFlag"/> each, <c>000bc4c6</c>.</summary>
	public const int SalvageBonusFlag = 7;

	/// <summary>A status block's three runs of readings: entries 1 to 13, 20 to 29 and 32 to 41.</summary>
	private static readonly (int First, int Count)[] StatusBlockRuns = [(1, 13), (20, 10), (32, 10)];

	/// <summary>The kill tallies a status block carries, the 6 bytes at <c>+0x23a</c>.</summary>
	private const int StatusBlockTallies = 3;

	/// <summary>
	/// <c>Mission_WriteResults</c>(<paramref name="player"/>): with <i>g</i> the player's group and <i>w</i> header word
	/// 3, <c>data\results.dat</c> in the install opened and written with the outcome, <c>Group_StatusLineIndex</c>(<i>g</i>,
	/// <i>w</i>), an <c>int16</c>; the award, <c>Math_Q10Multiply</c>(<see cref="TotalSalvage"/>(<i>g</i>),
	/// <see cref="SalvageScale"/>) + flag 7 × <see cref="SalvageBonusPerFlag"/>, an <c>int32</c>; the count of
	/// <see cref="Es1SimGlobals.SalvagePairs"/>, an <c>int16</c>, and each pair as two <c>int16</c>, those the total has just
	/// queued among them; and <see cref="WriteStatusBlocks"/>. Then <c>data\mission.var</c> opened and the 100 flags,
	/// 200 bytes, written over it. Returns false with the assert named in <paramref name="error"/> when a file does not
	/// open, where retail asserts at severity 5 (<c>world.cpp</c> line <c>0x58</c> or <c>0x6d</c>), so the second file is
	/// not written after the first fails.
	/// </summary>
	/// <remarks>
	/// Retail reads the player's group unchecked; with none nothing is written and the answer is true, an engine guard.
	/// </remarks>
	public static bool Write(Es1Mech player, Es1SimGlobals globals, string installRoot, out string? error) {
		error = null;
		if (player.Group is not { } group) {
			return true;
		}

		var results = new MemoryStream();
		var writer = new BinaryWriter(results);
		writer.Write(group.StatusLineIndex(globals.HeaderWord3, globals));
		int total = TotalSalvage(group, globals);
		short flag = SalvageBonusFlag < globals.MissionVars.Length ? globals.MissionVars[SalvageBonusFlag] : (short)0;
		writer.Write(SimMath.Q10MultiplyRounded(total, SalvageScale) + flag * SalvageBonusPerFlag);
		writer.Write((short)globals.SalvagePairs.Count);
		foreach (var (weapon, condition) in globals.SalvagePairs) {
			writer.Write(weapon);
			writer.Write(condition);
		}

		WriteStatusBlocks(group, writer);
		writer.Flush();
		if (!TryWrite(installRoot, ResultsName, results.ToArray())) {
			error = $"data\\{ResultsName} does not open for writing in {installRoot} (world.cpp line 0x58 asserts here).";
			return false;
		}

		var flags = new byte[Es1MissionFiles.MissionVarLength];
		Buffer.BlockCopy(globals.MissionVars, 0, flags, 0, Math.Min(globals.MissionVars.Length * 2, flags.Length));
		if (!TryWrite(installRoot, Es1MissionFiles.MissionVarName, flags)) {
			error = $"data\\{Es1MissionFiles.MissionVarName} does not open for writing in {installRoot} (world.cpp line 0x6d asserts here).";
			return false;
		}

		return true;
	}

	/// <summary>
	/// <paramref name="bytes"/> written over the install's <c>data\</c><paramref name="name"/> as <c>FileWStream_Ctor</c>
	/// opens it and the stream writes it; false when it does not open.
	/// </summary>
	internal static bool TryWrite(string installRoot, string name, byte[] bytes) {
		try {
			File.WriteAllBytes(Es1MissionFiles.DataPath(installRoot, name), bytes);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return false;
		}
	}

	/// <summary>
	/// <c>Mission_TotalSalvage</c> (<c>0001d434</c>)(<paramref name="group"/>, <i>w</i>), which ignores <i>w</i>:
	/// <see cref="SalvageValue"/> summed over every machine of <c>g_MechPool</c>, oldest first, whose group's side byte
	/// differs from <paramref name="group"/>'s and that is immobilized or destroyed. Retail reads each machine's group
	/// unchecked; a machine with none is passed over here, an engine guard.
	/// </summary>
	public static int TotalSalvage(Es1MissionGroup group, Es1SimGlobals globals) {
		int total = 0;
		foreach (var machine in globals.MechPool) {
			if (machine.Group is { } own && own.Side != group.Side && (machine.Immobilized || machine.Destroyed)) {
				total += SalvageValue(machine, globals);
			}
		}

		return total;
	}

	/// <summary>
	/// <c>Mech_SalvageValue</c> (<c>00014034</c>)(<paramref name="machine"/>): 0 when it is worth no salvage
	/// (<see cref="Es1Mech.WorthNoSalvage"/>); otherwise each mount of its manager whose component, 19 + its hardpoint's
	/// slot byte (<c>+0x17</c>, signed), reads below <c>0x80</c> (<see cref="Es1DamageHeader.ReadDamagePercent"/>) queued
	/// for salvage (<see cref="Es1SimGlobals.QueueSalvage"/>) with its weapon id and ((<c>0x100</c> − reading) × 100)
	/// &gt;&gt; 8; then <c>Math_Q10Multiply</c>(<see cref="Es1DamageHeader.WeightedArmorRemaining"/>, the chassis' salvage
	/// scale, the type record's <c>+0x54</c>), the scale halved when component 0 reads <c>0x100</c>. Retail reads the
	/// manager and the damage header unchecked; a machine with neither answers 0 and queues nothing here, an engine guard.
	/// </summary>
	public static int SalvageValue(Es1Mech machine, Es1SimGlobals globals) {
		if (machine.WorthNoSalvage || machine.Damage is not { } header) {
			return 0;
		}

		if (machine.Mounts is { } manager) {
			foreach (var mount in manager.Mounts) {
				if (mount == null) {
					continue;
				}

				short reading = header.ReadDamagePercent(FirstMountComponent + (sbyte)mount.Hardpoint.LoadoutSlot);
				if (reading < 0x80) {
					globals.QueueSalvage(mount.WeaponId, Condition(reading));
				}
			}
		}

		short scale = machine.Type.Chassis.SalvageScale;
		if (header.ReadDamagePercent(0) == Es1DamageHeader.Full) {
			scale >>= 1;
		}

		return SimMath.Q10MultiplyRounded(header.WeightedArmorRemaining(), scale);
	}

	/// <summary>The component of hardpoint slot 0, 19.</summary>
	private const int FirstMountComponent = 0x13;

	/// <summary>
	/// <c>Group_WriteStatusBlocks</c> (<c>0001d2c8</c>)(<paramref name="group"/>, stream): for each member in order,
	/// <see cref="Es1Mech.ReadDamageReadouts"/>' entries 1 to 13, 20 to 29 and 32 to 41 each written as an <c>int16</c>
	/// ((<c>0x100</c> − <i>r</i>) × 100) &gt;&gt; 8, then the first three of its kill tallies
	/// (<see cref="Es1Mech.KillTallies"/>, the 6 bytes at <c>+0x23a</c>): 72 bytes a member. Retail reads every member as
	/// a HERC; a member that is none, which no player's group holds, writes the block of a machine whose readings are all
	/// 0 and whose tallies are 0 here, an engine guard.
	/// </summary>
	public static void WriteStatusBlocks(Es1MissionGroup group, BinaryWriter writer) {
		foreach (var member in group.Members) {
			var machine = member as Es1Mech;
			short[] readings = machine?.ReadDamageReadouts() ?? new short[Es1Mech.DamageReadingCount];
			foreach (var (first, count) in StatusBlockRuns) {
				for (int i = first; i < first + count; i++) {
					writer.Write(Condition(readings[i]));
				}
			}

			for (int i = 0; i < StatusBlockTallies; i++) {
				writer.Write(machine != null ? machine.KillTallies[i] : (short)0);
			}
		}
	}

	/// <summary>A reading <paramref name="reading"/> as the condition the results carry, ((<c>0x100</c> − reading) × 100) &gt;&gt; 8.</summary>
	private static short Condition(short reading) => (short)((uint)((0x100 - reading) * 100) >> 8);
}
