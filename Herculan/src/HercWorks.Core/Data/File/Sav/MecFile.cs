namespace HercWorks.Core.Data.File.Sav;

/// <summary>
/// <c>ES2\DATA\player.mec</c> — the player's own squad, written by VSHELL alongside
/// <c>data\script.dat</c> and read by DBSIM at world init. Where <c>script.dat</c>'s block 7 is the
/// mission's roster of AI mechs, this is the roster of the ones the player brought: the machine the
/// player pilots plus any wingmen, each with the loadout configured in the shell's HERC bay.
///
/// <para>Decoded from <c>DBSim_LoadScriptDat</c> (<c>00424308</c>), which opens this file
/// immediately before <c>script.dat</c>, and from <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>), DBSIM's world-spawn pass,
/// which appends these entries to the end of the mission's mech list and hands each one's two
/// <see cref="MecEntry.WeaponRefs"/>/<see cref="MecEntry.WeaponAmmoTypes"/> arrays to the same
/// <c>Mech_ConfigureLoadout</c> that <c>script.dat</c>'s own records feed. The squad spawns at the
/// position carried by <c>script.dat</c> block 11's <b>record 0</b>, which exists purely to place
/// it — DBSIM overwrites that record's member list with these entries.</para>
///
/// <para>VSHELL writes it in <c>Game_ExportMissionHandoff</c> (<c>0040f0d4</c>) and closes it with
/// <c>PlayerMec_WriteUnlockTable</c> (<c>00412253</c>): <c>int16 33</c> and then 33 bytes, one per
/// weapon catalog id, the <see cref="WeaponFlags"/>. See
/// <c>docs/retail/shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4</c>.</para>
/// </summary>
public class MecFile {
	/// <summary>
	/// Which <see cref="Entries"/> slot the player themself pilots; the rest are wingmen. DBSIM
	/// compares this against the entry index as it spawns them and flags the match as the camera's
	/// and the input's owner.
	/// </summary>
	public short PlayerEntryIndex { get; set; }

	/// <summary>The squad, in the order DBSIM appends them to the mission's mech list.</summary>
	public MecEntry[] Entries { get; set; } = [];

	/// <summary>
	/// The weapon-unlock table VSHELL writes after the last entry — one byte per weapon catalog id,
	/// 33 of them in every retail file, each that weapon's <c>weapons.dat</c> <c>+0x16</c> field:
	/// whether the campaign has unlocked that weapon for purchase.
	///
	/// <para>Duplicated state, and nothing traced reads it back: VSHELL reopens this file only in
	/// its map screen and takes just the two leading shorts, and DBSIM's reader stops at the last
	/// entry. The save slot is where these flags are authoritative — this file is regenerated from
	/// it at every mission launch — so the table is preserved for byte-fidelity, not because the
	/// game depends on it.</para>
	///
	/// <para>Empty when the source file carried no table; retail accepts those. An empty table is
	/// written back as no table at all rather than as zeroes, because the flags cannot be
	/// reconstructed from anything else in this file and inventing them would state something false
	/// about the player's armory.</para>
	/// </summary>
	public byte[] WeaponFlags { get; set; } = [];
}

/// <summary>
/// One machine in the player's squad: two fields from its pilot, then the HERC — type, loadout and
/// the 66-byte status block, which DBSIM copies wholesale into the mech's in-memory record as the
/// three condition spans below.
/// </summary>
public class MecEntry {
	/// <summary>
	/// The pilot's name index — pilot record <c>+0x02</c> on the shell side, into
	/// <c>esnames.bin</c>. VSHELL's per-entry writer is <c>PlayerMec_WriteEntry</c> (<c>004106b7</c>).
	///
	/// <para>DBSIM reads it too: <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) stamps it onto the
	/// spawned machine at <c>mech+0x29c</c>, and <c>Cockpit_LoadSquadmatePilots</c> (<c>00431530</c>) hands it to
	/// <c>HddGauge_LoadPilotFrames</c> (<c>0044a7c0</c>), which walks <c>str\PILOTS.STR</c> to that
	/// index for the comm box's name and divides it by three for the portrait bank that talks in it.
	/// So the simulator's own 36-name table is indexed by the same number.</para>
	/// </summary>
	public short PilotNameIndex { get; set; }

	/// <summary>The pilot's skill tier, 0-3 — pilot record <c>+0x25</c> on the shell side.</summary>
	public short Skill { get; set; }

	/// <summary>The mech type, an index into <c>nam\MECHS.NAM</c>'s name list — the same numbering
	/// <see cref="Msn.Script.ScriptMechRecord.TypeIndex"/> uses.</summary>
	public short MechType { get; set; }

	/// <summary>
	/// How many weapon slots this entry declares. Both arrays below are this long on disk, which is
	/// what makes the record variable-length.
	/// </summary>
	public short SlotCount { get; set; }

	/// <summary>Per-slot weapon ids; <c>0</c> for an empty slot.</summary>
	public short[] WeaponRefs { get; set; } = [];

	/// <summary>
	/// Per-slot second value, paired with <see cref="WeaponRefs"/> by DBSIM's loadout call — the
	/// ammunition type each missile launcher is loaded with.
	///
	/// <para>Resolved from <c>MechLoadout_ConstructWeaponMounts</c> (<c>0040fff8</c>),
	/// which takes this array's entry for a hardpoint through
	/// <c>Proj_LookupRecord(Rocket, key)</c> whenever the weapon's template carries the launcher
	/// sentinel, and from <c>WeaponMount_GetDisplayName</c> (<c>0040e18c</c>), which then prints that record's own subtype as the
	/// mount's name. That is why the retail player's <c>MSL10</c> hardpoint reads <c>ARH</c> in the
	/// cockpit rather than <c>MSL10</c>. Non-launcher slots carry a filler 5, which the factory
	/// rewrites to 0 before looking it up.</para>
	/// </summary>
	public short[] WeaponAmmoTypes { get; set; } = [];

	/// <summary>Meaning unknown. VSHELL writes a literal 0.</summary>
	public short Unk3A { get; set; }

	/// <summary>
	/// 26 bytes copied to the mech record at <c>+0x3c</c> — thirteen <c>int16</c> external facet
	/// conditions, 0-100, by <see cref="Struct.Herc.HercExternals"/> index. See
	/// <c>docs/retail/formats/save-games.md#the-66-byte-status-block</c>.
	/// </summary>
	public byte[] ExternalConditions { get; set; } = new byte[26];

	/// <summary>
	/// 20 bytes copied to the mech record at <c>+0x56</c> — ten <c>int16</c> conditions, 0-100.
	/// Indices 0-8 are the nine internal components named by <see cref="Struct.Herc.HercInternals"/>;
	/// index 9 is the machine's overall condition, which the debrief reads to set its pilot's.
	/// </summary>
	public byte[] InternalConditions { get; set; } = new byte[20];

	/// <summary>
	/// 20 bytes copied to the mech record at <c>+0x6a</c> — ten <c>int16</c> per-hardpoint
	/// conditions, one per weapon slot, in the same slot order as <see cref="WeaponRefs"/>. The shell
	/// destroys a mount whose value reaches 0.
	/// </summary>
	public byte[] HardpointConditions { get; set; } = new byte[20];
}
