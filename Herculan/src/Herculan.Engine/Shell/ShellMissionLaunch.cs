using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>
/// Why <c>Rock &amp; Roll &gt;</c> refused to launch — the code <c>Mission_OnRockAndRoll</c> (<c>00445509</c>)
/// hands the refusal dialog, <c>LaunchRefusal_Show</c> (<c>0044d27c</c>), which prints the two <c>estext.bin</c> lines from
/// <c>0x13c + 2 * code</c>.
/// </summary>
public enum ShellLaunchRefusal {
	/// <summary><c>Your herc is not functional.</c> — the player's machine is not built or not flightworthy.</summary>
	NotFunctional = 0,

	/// <summary><c>Your herc is unarmed.</c></summary>
	Unarmed = 1,

	/// <summary><c>You have no herc assignment.</c> — the player has no bay.</summary>
	NoAssignment = 2,

	/// <summary><c>One or more hercs of your squad is unarmed.</c></summary>
	SquadUnarmed = 3,
}

/// <summary>
/// <c>Rock &amp; Roll &gt;</c>, <c>Mission_OnRockAndRoll</c> (<c>00445509</c>): four tests in order, the
/// first to fail refusing the launch, and otherwise <c>Game_ExportMissionHandoff</c> (<c>0040f0d4</c>)
/// and the exit code that tells the launcher to run the simulator. See
/// docs/retail/shell/mission-screen.md#the-mission-screen and docs/retail/shell/campaign-loop.md for the handoff.
/// </summary>
public static class ShellMissionLaunch {
	/// <summary>
	/// The first weapon id that does not arm a machine: <c>Herc_HasWeapon</c> (<c>004116ec</c>) counts a mount only while its
	/// id is below <c>0x1d</c>, so the four pods, <c>TARG</c> to <c>ENRG</c>, leave a machine unarmed.
	/// </summary>
	public const int FirstUnarmingWeapon = 0x1d;

	/// <summary>How many catalog ids the export's closing table carries, <c>PlayerMec_WriteUnlockTable</c>'s literal <c>0x21</c>.</summary>
	public const int WeaponCatalogCount = 0x21;

	/// <summary>The mount slots <c>Herc_HasWeapon</c> (<c>004116ec</c>) walks — all ten a record has, whatever its capacity.</summary>
	private const int MountSlots = 10;

	/// <summary>
	/// The four tests, in <c>Mission_OnRockAndRoll</c>'s order: the player has a bay (<c>00482a9e != -1</c>);
	/// its machine is built and flightworthy (<c>Herc_IsDeployable</c> (<c>00410a9d</c>)); it is armed (<c>Herc_IsArmed</c> (<c>00410b11</c>)); and
	/// every squad member on strength in a position in play has an armed machine (<c>Squad_AllArmed</c> (<c>0040f6c6</c>)).
	/// Null when the launch goes ahead.
	/// </summary>
	public static ShellLaunchRefusal? Check(ShellHangar hangar) {
		if (hangar.Player is not { Bay: not -1 } player) {
			return ShellLaunchRefusal.NoAssignment;
		}

		if (hangar.Bay(player.Bay) is not { IsBuilt: true, IsFlightworthy: true } machine) {
			return ShellLaunchRefusal.NotFunctional;
		}

		if (!IsArmed(machine)) {
			return ShellLaunchRefusal.Unarmed;
		}

		for (int position = 1; position < hangar.SquadPositions; position++) {
			if (hangar.SquadMemberAt(position) is { OnStrength: true } member && !IsArmed(hangar.Bay(member.Bay))) {
				return ShellLaunchRefusal.SquadUnarmed;
			}
		}

		return null;
	}

	/// <summary><c>Herc_IsArmed</c> (<c>00410b11</c>) through <c>Herc_HasWeapon</c> (<c>004116ec</c>): a machine is armed when any of its ten mounts holds a weapon below <see cref="FirstUnarmingWeapon"/>.</summary>
	public static bool IsArmed(ShellBayMachine? machine) {
		if (machine == null) {
			return false;
		}

		for (int slot = 0; slot < MountSlots; slot++) {
			if (machine.Mount(slot) is { WeaponId: < FirstUnarmingWeapon }) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>data\player.mec</c> as <c>Game_ExportMissionHandoff</c> writes it: the player's entry index, always
	/// 0; the count at <c>00482a7a</c>; the player's machine; then the machine of each squad member on
	/// strength, walking the positions in play from 1; and the weapon unlock table. Each entry is the
	/// pilot's name index and skill, then <c>PlayerMec_WriteEntry</c> (<c>004106b7</c>)'s record of the pilot's bay. The count is
	/// written as the player structure holds it, whatever the entries come to, and a bay with no machine
	/// writes its two leading fields and nothing after them, as the original's does.
	/// </summary>
	public static byte[] ExportPlayerMec(ShellHangar hangar) {
		var entries = new List<MecEntry>();
		var hasMachine = new List<bool>();

		void Add(ShellBayPilot pilot) {
			var entry = new MecEntry { PilotNameIndex = (short)pilot.NameIndex, Skill = (short)pilot.Skill };
			if (hangar.Bay(pilot.Bay) is { } machine) {
				int capacity = Math.Max(machine.MountCapacity, 0);
				entry.MechType = (short)machine.ChassisType;
				entry.SlotCount = (short)capacity;
				entry.WeaponRefs = Enumerable.Range(0, capacity).Select(slot => (short)machine.WeaponAt(slot)).ToArray();
				entry.WeaponAmmoTypes = Enumerable.Range(0, capacity)
					.Select(slot => (short)(machine.Mount(slot)?.Guidance ?? ShellWeaponUnit.NoGuidance)).ToArray();
				(entry.ExternalConditions, entry.InternalConditions, entry.HardpointConditions) = machine.StatusBlock();
				hasMachine.Add(true);
			} else {
				hasMachine.Add(false);
			}

			entries.Add(entry);
		}

		if (hangar.Player is { } player) {
			Add(player);
		}

		for (int position = 1; position < hangar.SquadPositions; position++) {
			if (hangar.SquadMemberAt(position) is { OnStrength: true } member) {
				Add(member);
			}
		}

		var flags = Enumerable.Range(0, WeaponCatalogCount).Select(id => (byte)(hangar.IsWeaponUnlocked(id) ? 1 : 0)).ToArray();

		// MecFileTransformer writes whole entries and takes the count from the array, so the header and
		// any bay-less entry are laid down here and only full entries go through it.
		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		writer.Write((short)0);
		writer.Write((short)hangar.MachinesOnStrength);
		var transformer = new MecFileTransformer();
		for (int i = 0; i < entries.Count; i++) {
			if (hasMachine[i]) {
				byte[] single = transformer.Write(new MecFile { Entries = new[] { entries[i] } })!;
				writer.Write(single, 4, single.Length - 4);
			} else {
				writer.Write(entries[i].PilotNameIndex);
				writer.Write(entries[i].Skill);
			}
		}

		writer.Write((short)flags.Length);
		writer.Write(flags);
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>
	/// Writes the handoff the simulator loads into <paramref name="directory"/>: the working
	/// <c>script.dat</c> and <c>mission.str</c> (<see cref="ShellWorkingFiles"/>), which a slot's load or the
	/// career's mission load put in <c>data\</c>; <c>mission.var</c>, the 2000-byte campaign flag array
	/// <c>MissionVar_Write</c> (<c>0040e9cb</c>) writes; and <c>player.mec</c> from the hangar as the
	/// screens have left it. Returns the path of the <c>script.dat</c> written, or null when there is no
	/// mission file to copy.
	/// </summary>
	public static string? WriteHandoff(string directory, ShellWorkingFiles working, PlayerSave save, ShellHangar hangar) {
		if (working.Script is not { } script || !File.Exists(script)) {
			return null;
		}

		Directory.CreateDirectory(directory);
		string scriptPath = Path.Combine(directory, MissionLoader.ScriptFileName);
		CopyUnlessSame(script, scriptPath);

		string? text = working.Text;
		string textPath = Path.Combine(directory, MissionLoader.TextFileName);
		if (File.Exists(text)) {
			CopyUnlessSame(text, textPath);
		} else if (File.Exists(textPath)) {
			File.Delete(textPath);
		}

		if (save.HasCampaignState) {
			var flags = new byte[PlayerSave.CampaignFlagCount * 2];
			for (int i = 0; i < PlayerSave.CampaignFlagCount; i++) {
				BitConverter.GetBytes(save.GetCampaignFlag(i)).CopyTo(flags, i * 2);
			}

			File.WriteAllBytes(Path.Combine(directory, MissionVarFileName), flags);
		}

		File.WriteAllBytes(Path.Combine(directory, MissionLoader.PlayerFileName), ExportPlayerMec(hangar));
		return scriptPath;
	}

	private static void CopyUnlessSame(string from, string to) {
		if (!string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) {
			File.Copy(from, to, overwrite: true);
		}
	}

	/// <summary>The flag array's file, <c>data\mission.var</c>.</summary>
	public const string MissionVarFileName = MissionLoader.CountersFileName;
}
