namespace HercWorks.Core.Data.File.Msn.Script;

/// <summary>
/// <c>data\script.dat</c> — the mission as VSHELL hands it to DBSIM, and as VSHELL's briefing map
/// (<c>ShellMap</c>) reads it. Written by the mission load
/// (<see cref="Io.Transform.Common.MissionGenerator.WriteScriptDat"/>) after it applies the
/// <c>.MSN</c>'s conditions and variants: each block is one <see cref="MissionFile"/> row's surviving
/// records, refs renumbered to block indices, some fields dropped. See docs/retail/formats/script-dat.md.
///
/// <b>DBSIM reads this file twice.</b> <c>DBSim_LoadScriptDat</c> counts live objects and sizes its
/// pools, keeping little more than each roster record's type field; <c>DBSim_SpawnMissionObjects</c>
/// then re-opens the file and walks blocks 7-13 again to actually build the world, and that is the
/// pass that reads positions, headings and loadouts. Judging a block by what the first pass keeps
/// gives the wrong answer for most of them — see the format doc's "The two-pass read".
///
/// Bytes past block 13's end are a longer earlier mission's leftovers, since the writer does not
/// truncate (docs/retail/formats/script-dat.md#fixed-size-file-structure). This model round-trips through
/// block 13 only.
/// </summary>
public class ScriptDat {
	/// <summary>
	/// The 20-byte header, ten shorts, kept raw so the two fields nothing reads (offsets 4 and 16)
	/// round-trip as loaded; the named properties below are views over it. See
	/// docs/retail/formats/script-dat.md#header-format.
	/// </summary>
	public byte[] HeaderBytes { get; set; } = new byte[HeaderSize];

	/// <summary>Bytes the header occupies.</summary>
	public const int HeaderSize = 20;

	/// <summary>Header offset 0 — the theater, 0-4.</summary>
	public short TheaterIndex { get => ReadHeader(0); set => WriteHeader(0, value); }

	/// <summary>Header offset 2 — the zone, the <c>zoneNNNN</c> the mission plays in, passed to <c>Terrain_LoadZone</c>.</summary>
	public short ZoneIndex { get => ReadHeader(2); set => WriteHeader(2, value); }

	/// <summary>
	/// Header offset 6 — <c>DAT_004a9ed8</c>, the <b>mission objective type</b>. It selects which arm of
	/// the player's own think watches for progress: 0 the order target coming into range, 5 closing on
	/// the goal position, 3 or 7 the data-link sequence. Type 3 also takes the data-link subject out
	/// of the AI's candidate set, so the player's squad does not shoot the thing they came to read.
	///
	/// <para>Which missions patch it is in docs/retail/formats/script-dat.md#header-format.</para>
	/// </summary>
	public short ObjectiveType { get => ReadHeader(6); set => WriteHeader(6, value); }

	/// <summary>
	/// Header offset 8 — <c>ScriptDatTrainingMission</c> (<c>004a9eda</c>), the <b>training mission number</b>, 0 for anything that
	/// is not one. The <c>.MSN</c> header patch sets it: <c>TRAIN1</c>-<c>TRAIN4</c> carry 1-4, every
	/// other mission 0. It selects the cockpit's training message port and the instructor's
	/// <c>COMMAND&lt;n&gt;.STR</c> and <c>TM&lt;n&gt;_</c> clips, and silences the music. See
	/// docs/retail/formats/script-dat.md#the-training-mission-number.
	/// </summary>
	public short TrainingMissionNumber { get => ReadHeader(8); set => WriteHeader(8, value); }

	/// <summary>
	/// Header offset 10 — <c>UnlimitedAmmoFlag</c> (<c>004a9edc</c>), <b>unlimited ammunition and energy</b> when the file
	/// says exactly 1. The shell's practice missions screen sets it; a campaign forces it to 0. It acts
	/// on the player's machine alone, in two ways: a shot spends no ammunition, and the weapon mounts
	/// hand the Master Energy Pool back everything they drew this tick. See
	/// docs/retail/simulation/difficulty.md.
	/// </summary>
	public short UnlimitedAmmunition { get => ReadHeader(10); set => WriteHeader(10, value); }

	/// <summary>
	/// Header offset 12 — <c>PlayerInvulnerableFlag</c> (<c>004a9ede</c>), <b>player invulnerable</b> when the file says exactly
	/// 1. It gates the whole of the damage write for the locally piloted machine, so its components
	/// take nothing; its shields still absorb and still drain, because that happens before the write.
	/// </summary>
	public short PlayerInvulnerable { get => ReadHeader(12); set => WriteHeader(12, value); }

	/// <summary>
	/// Header offset 14 — <c>MissionDifficulty</c> (<c>004a9ee0</c>), the <b>mission difficulty</b>, 0-3. The shell writes
	/// the player pilot's own skill here in a campaign and the practice missions screen's setting
	/// outside one, which is why every retail file carries 2 (<c>VETERAN</c>). Four things in the
	/// original index a four-entry table with it — see docs/retail/simulation/difficulty.md.
	/// </summary>
	public short Difficulty { get => ReadHeader(14); set => WriteHeader(14, value); }

	/// <summary>
	/// Header offset 18 — the theater variant, 0 day or 1 night: it selects between a theater's two
	/// descriptors, and the shell's practice missions screen writes it from a <c>Day</c> /
	/// <c>Night</c> row. Every retail file carries 0.
	/// </summary>
	public short TheaterVariant { get => ReadHeader(18); set => WriteHeader(18, value); }

	private short ReadHeader(int offset) =>
		HeaderBytes.Length >= offset + 2 ? BitConverter.ToInt16(HeaderBytes, offset) : (short)0;

	private void WriteHeader(int offset, short value) {
		if (HeaderBytes.Length < offset + 2) {
			throw new InvalidOperationException("This script.dat's header is too short to hold that field.");
		}

		BitConverter.GetBytes(value).CopyTo(HeaderBytes, offset);
	}

	/// <summary>Block 1 — row #6 (<see cref="MapPoint22"/>) export: X/Y/Z world positions only.</summary>
	public ScriptCoordinate[] Coordinates { get; set; } = [];

	/// <summary>Block 2 — row #7 (<see cref="Heading10"/>) export: the headings.</summary>
	public ScriptHeading[] Headings { get; set; } = [];

	/// <summary>Block 3 — row #8 (<see cref="WaypointGroup"/>) export: the waypoint ref list only (no GUID/condition).</summary>
	public ScriptWaypointGroup[] WaypointGroups { get; set; } = [];

	/// <summary>Block 4 — row #9 (<see cref="TriggerArea12"/>) export: trigger areas.</summary>
	public ScriptTriggerArea[] TriggerAreas { get; set; } = [];

	/// <summary>Block 5 — row #10 (<see cref="MissionAction82"/>) export: mission actions.</summary>
	public ScriptAction[] Actions { get; set; } = [];

	/// <summary>Block 6 — row #11 (<see cref="ActionTimer30"/>) export: mission timers.</summary>
	public ScriptActionTimer[] ActionTimers { get; set; } = [];

	/// <summary>
	/// Block 7 — row #12 (<see cref="MechRosterEntry144"/>) export, 134 bytes/record: <b>the mech
	/// roster</b>. DBSIM builds one live mech per record a block-11 group activates.
	/// </summary>
	public ScriptMechRecord[] Mechs { get; set; } = [];

	/// <summary>
	/// Block 8 — row #13 (<see cref="FlyerRosterEntry102"/>) export, 92 bytes/record: <b>the flyer
	/// roster</b>, the same arrangement as <see cref="Mechs"/>.
	/// </summary>
	public ScriptFlyerRecord[] Flyers { get; set; } = [];

	/// <summary>
	/// Block 9 — row #14 (<see cref="BaseRosterEntry62"/>) export, 52 bytes/record: <b>the base
	/// roster</b> — structures, turrets and the rest of the static furniture.
	/// </summary>
	public ScriptBaseRecord[] Bases { get; set; } = [];

	/// <summary>
	/// Block 10 — row #15 (<see cref="MissionOrder22"/>) export, 14 bytes/record: <b>mission-group
	/// orders</b>. A group works through the ones its block-11 record names, and a group with no
	/// spawn point of its own starts at the first waypoint of its first order's
	/// <see cref="ScriptOrder.RouteRef"/>.
	/// </summary>
	public ScriptOrder[] Orders { get; set; } = [];

	/// <summary>
	/// Block 11 — row #16 (<see cref="MissionGroup164"/>) export, 156 bytes/record: <b>the groups</b>,
	/// and the reason anything is anywhere. Each record past the first activates roster slots —
	/// <see cref="ScriptGroup.MemberKind"/> picks the roster, <see cref="ScriptGroup.MemberRefs"/>
	/// the slots — and carries the spawn point, heading, formation and orders its members take.
	/// <b>Record 0 is special</b>: it activates nothing and holds the player squad's spawn point,
	/// whose members DBSIM fills from <see cref="Sav.MecFile"/>.
	/// </summary>
	public ScriptGroup[] Groups { get; set; } = [];

	/// <summary>
	/// Block 12 — row #17 (<see cref="MissionObjective58"/>) export, 54 bytes/record, unfiltered (row
	/// #17 has no GUID) — <b>the mission's objectives</b>, the conditions the simulation tests.
	/// </summary>
	public ScriptObjective[] Objectives { get; set; } = [];

	/// <summary>
	/// Block 13 — the flat tail: <b>the mission's objective list as the player is shown it</b>, one
	/// index per line into <c>data\mission.str</c>. <c>DBSim_LoadScriptDat</c> (<c>00424308</c>)
	/// reads the count into <c>DAT_004a9ec8</c> and the shorts into <c>DAT_004a9ecc</c>, and the
	/// in-mission objectives panel (<c>obj_alrt</c>, <c>ObjectivesPanel_Ctor</c> (<c>0045751c</c>)) is the only reader: it
	/// prints one label per entry.
	///
	/// <para><b>It is separate data from block 12</b>, which holds the conditions the simulation
	/// actually tests. Nothing reconciles the two, so a mission can list an objective it does not
	/// test and test one it does not list.</para>
	/// </summary>
	public short[] ObjectiveTextRefs { get; set; } = [];
}

/// <summary>Block 1 entry — 12 bytes (int32 X/Y/Z), a positions-only export of row #6.</summary>
public class ScriptCoordinate {
	/// <inheritdoc cref="MapPoint22.X"/>
	public int X { get; set; }

	/// <inheritdoc cref="MapPoint22.Y"/>
	public int Y { get; set; }

	/// <inheritdoc cref="MapPoint22.Z"/>
	public int Z { get; set; }
}

/// <summary>Block 2 entry — 2 bytes, row #7's heading in degrees.</summary>
public class ScriptHeading {
	/// <inheritdoc cref="Heading10.Degrees"/>
	public short Degrees { get; set; }
}

/// <summary>Block 3 entry — a count, then that many waypoints as block-1 indices.</summary>
public class ScriptWaypointGroup {
	/// <inheritdoc cref="WaypointGroup.Waypoints"/>
	public short[] Waypoints { get; set; } = [];
}

/// <summary>Block 4 entry — 6 bytes, a trigger area; the fields are <see cref="TriggerArea12"/>'s, refs as block-1 indices.</summary>
public class ScriptTriggerArea {
	/// <inheritdoc cref="TriggerArea12.Shape"/>
	public short Shape { get; set; }

	/// <inheritdoc cref="TriggerArea12.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="TriggerArea12.SecondPointOrRadius"/>
	public short SecondPointOrRadius { get; set; }
}

/// <summary>
/// Block 5 entry — 74 bytes, row #10 (<see cref="MissionAction82"/>) less its GUID, condition, 0x04
/// and 0x1A, with its counter pairs split into <see cref="CounterRefs"/> then <see cref="CounterOps"/>
/// and refs as block indices. What DBSIM keeps of it is
/// docs/retail/formats/script-dat.md#block-5-in-memory--58-bytes-0x3a.
/// </summary>
public class ScriptAction {
	/// <inheritdoc cref="MissionAction82.Type"/>
	public short Type { get; set; }

	/// <inheritdoc cref="MissionAction82.Verb"/>
	public short Verb { get; set; }

	/// <inheritdoc cref="MissionAction82.AreaRefs"/>
	public short[] AreaRefs { get; set; } = new short[8];

	/// <summary>The counter ref of each of <see cref="MissionAction82.CounterPairs"/>' ten pairs.</summary>
	public short[] CounterRefs { get; set; } = new short[10];

	/// <summary>The operation of each of <see cref="MissionAction82.CounterPairs"/>' ten pairs, slot for slot with <see cref="CounterRefs"/>.</summary>
	public short[] CounterOps { get; set; } = new short[10];

	/// <summary><see cref="MissionAction82.TextRefs"/>, renumbered into <c>data\mission.str</c> line indices.</summary>
	public short[] TextRefs { get; set; } = new short[5];

	/// <inheritdoc cref="MissionAction82.MessageId"/>
	public short MessageId { get; set; }

	/// <inheritdoc cref="MissionAction82.TargetRef"/>
	public short TargetRef { get; set; }
}

/// <summary>Block 6 entry — 24 bytes, row #11 (<see cref="ActionTimer30"/>) less its GUID, condition and 0x04, refs as block-5 indices.</summary>
public class ScriptActionTimer {
	/// <inheritdoc cref="ActionTimer30.PrimaryActionRef"/>
	public short PrimaryActionRef { get; set; }

	/// <inheritdoc cref="ActionTimer30.Delay"/>
	public short Delay { get; set; }

	/// <inheritdoc cref="ActionTimer30.SequenceRefs"/>
	public short[] SequenceRefs { get; set; } = new short[10];
}

/// <summary>
/// Block 7 entry — 134 bytes, row #12 (<see cref="MechRosterEntry144"/>) less its GUID, condition,
/// variant key, compound-condition partner and pair count. <see cref="HeadBytes"/> is source
/// 0x08-0x2F and <see cref="TailBytes"/> source 0x4C-0x8F with the counter pairs split; both are kept
/// raw for a byte-exact round trip, and the named properties are views over them. Refs are block
/// indices.
///
/// <para><c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) builds one mech per live slot from
/// this record. <c>DBSim_LoadScriptDat</c>'s first pass keeps only <see cref="TypeIndex"/>, to count
/// and allocate (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps).</para>
/// </summary>
public class ScriptMechRecord {
	public byte[] HeadBytes { get; set; } = new byte[40];

	/// <inheritdoc cref="MechRosterEntry144.AiRadarActive"/>
	/// <remarks>The first word of <see cref="HeadBytes"/>.</remarks>
	public short AiRadarActive { get => ReadHead(0); set => ScriptActionRefs.Write(HeadBytes, 0, value); }

	/// <inheritdoc cref="MechRosterEntry144.AiCruiseSpeed"/>
	/// <remarks>The second word of <see cref="HeadBytes"/>.</remarks>
	public short AiCruiseSpeed { get => ReadHead(2); set => ScriptActionRefs.Write(HeadBytes, 2, value); }

	private short ReadHead(int offset) =>
		HeadBytes.Length >= offset + 2 ? BitConverter.ToInt16(HeadBytes, offset) : (short)0;

	/// <inheritdoc cref="MechRosterEntry144.TypeIndex"/>
	public short TypeIndex { get; set; }

	/// <inheritdoc cref="MechRosterEntry144.WeaponRefs"/>
	public short[] WeaponRefs { get; set; } = new short[10];

	/// <inheritdoc cref="MechRosterEntry144.PositionRef"/>
	public short PositionRef { get; set; }

	/// <inheritdoc cref="MechRosterEntry144.HeadingRef"/>
	public short HeadingRef { get; set; }

	public byte[] TailBytes { get; set; } = new byte[68];

	/// <summary>Exported offset <c>0x42</c> — the counter ref of each of <see cref="MechRosterEntry144.OutOfActionReport"/>'s ten pairs.</summary>
	public short[] CounterRefs => ScriptActionRefs.ReadSlots(TailBytes, ScriptActionRefs.CounterRefs);

	/// <summary>Exported offset <c>0x56</c> — the operation of each of those pairs, slot for slot with <see cref="CounterRefs"/>.</summary>
	public short[] CounterOps => ScriptActionRefs.ReadSlots(TailBytes, ScriptActionRefs.CounterOps);

	/// <inheritdoc cref="MechRosterEntry144.WeaponSecondary"/>
	/// <remarks>
	/// Exported offset <c>0x6a</c>. A view over <see cref="TailBytes"/> rather than a field of its own,
	/// so the record still round-trips byte-exact through
	/// <see cref="Io.Transform.Common.ScriptDatTransformer"/>.
	/// </remarks>
	public short[] WeaponSecondary => HasWeaponSecondary
		? Enumerable.Range(0, SlotCount)
			.Select(i => BitConverter.ToInt16(TailBytes, SecondaryOffset + i * 2))
			.ToArray()
		: [];

	/// <summary>
	/// Whether <see cref="TailBytes"/> is long enough to hold the second array — false only for a
	/// record built with a short tail rather than parsed from a real file.
	/// </summary>
	public bool HasWeaponSecondary => TailBytes.Length >= SecondaryOffset + SlotCount * 2;

	/// <summary>
	/// Writes one slot of <see cref="WeaponSecondary"/> back into <see cref="TailBytes"/>.
	/// <see cref="WeaponSecondary"/> hands back a copy, so assigning into what it returns changes
	/// nothing — this is the only way to edit a slot's ammunition type.
	/// </summary>
	public void SetWeaponSecondary(int slot, short value) {
		if (slot < 0 || slot >= SlotCount) {
			throw new ArgumentOutOfRangeException(nameof(slot), slot, $"A loadout has {SlotCount} slots.");
		}

		if (!HasWeaponSecondary) {
			throw new InvalidOperationException("This record's tail is too short to hold the ammunition array.");
		}

		BitConverter.GetBytes(value).CopyTo(TailBytes, SecondaryOffset + slot * 2);
	}

	/// <inheritdoc cref="MechRosterEntry144.EngagementActionRef"/>
	/// <remarks>Exported offset <c>0x80</c>.</remarks>
	public short EngagementActionRef {
		get => ReadTail(EngagementActionOffset);
		set => ScriptActionRefs.Write(TailBytes, EngagementActionOffset, value);
	}

	/// <inheritdoc cref="MechRosterEntry144.DefeatActionRef"/>
	/// <remarks>Exported offset <c>0x82</c>.</remarks>
	public short DefeatActionRef {
		get => ReadTail(DefeatActionOffset);
		set => ScriptActionRefs.Write(TailBytes, DefeatActionOffset, value);
	}

	/// <inheritdoc cref="MechRosterEntry144.StartingCondition"/>
	/// <remarks>Exported offset <c>0x84</c>.</remarks>
	public short StartingCondition {
		get => ReadTail(StartingConditionOffset);
		set => ScriptActionRefs.Write(TailBytes, StartingConditionOffset, value);
	}

	private short ReadTail(int offset) =>
		TailBytes.Length >= offset + 2 ? BitConverter.ToInt16(TailBytes, offset) : (short)-1;

	/// <summary>Where <see cref="EngagementActionRef"/> sits in <see cref="TailBytes"/> (0x80 less 0x42).</summary>
	private const int EngagementActionOffset = 62;

	/// <summary>And <see cref="DefeatActionRef"/> (0x82 less 0x42).</summary>
	private const int DefeatActionOffset = 64;

	/// <summary>And <see cref="StartingCondition"/> (0x84 less 0x42).</summary>
	private const int StartingConditionOffset = 66;

	/// <summary>Where <see cref="WeaponSecondary"/> starts inside <see cref="TailBytes"/> (source 0x74 less 0x4c).</summary>
	private const int SecondaryOffset = 40;

	/// <summary>Slots in both loadout arrays.</summary>
	private const int SlotCount = 10;
}

/// <summary>
/// Block 8 entry — 92 bytes, row #13 (<see cref="FlyerRosterEntry102"/>) less its GUID, condition,
/// variant key, 0x06 and pair count. <see cref="HeadBytes"/> is source 0x08-0x2F (the flag span) and
/// <see cref="TailBytes"/> source 0x38-0x65 with the counter pairs split. Refs are block indices.
///
/// <para>DBSIM's world-spawn pass (<c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>)) builds one flyer/vehicle per live slot from
/// this record, taking its type from <see cref="TypeIndex"/> and its placement from the two refs
/// below.</para>
/// </summary>
public class ScriptFlyerRecord {
	public byte[] HeadBytes { get; set; } = new byte[40];

	/// <inheritdoc cref="FlyerRosterEntry102.PositionRef"/>
	public short PositionRef { get; set; }

	/// <inheritdoc cref="FlyerRosterEntry102.HeadingRef"/>
	public short HeadingRef { get; set; }

	/// <inheritdoc cref="FlyerRosterEntry102.TypeIndex"/>
	public short TypeIndex { get; set; }

	public byte[] TailBytes { get; set; } = new byte[46];

	/// <summary>Exported offset <c>0x2e</c> — the counter ref of each of <see cref="FlyerRosterEntry102.OutOfActionReport"/>'s ten pairs.</summary>
	public short[] CounterRefs => ScriptActionRefs.ReadSlots(TailBytes, ScriptActionRefs.CounterRefs);

	/// <summary>Exported offset <c>0x42</c> — the operation of each of those pairs, slot for slot with <see cref="CounterRefs"/>.</summary>
	public short[] CounterOps => ScriptActionRefs.ReadSlots(TailBytes, ScriptActionRefs.CounterOps);

	/// <inheritdoc cref="FlyerRosterEntry102.EngagementActionRef"/>
	/// <remarks>Exported offset <c>0x56</c>.</remarks>
	public short EngagementActionRef {
		get => ScriptActionRefs.Read(TailBytes, ScriptActionRefs.SmallEngagement);
		set => ScriptActionRefs.Write(TailBytes, ScriptActionRefs.SmallEngagement, value);
	}

	/// <inheritdoc cref="FlyerRosterEntry102.DefeatActionRef"/>
	/// <remarks>Exported offset <c>0x58</c>.</remarks>
	public short DefeatActionRef {
		get => ScriptActionRefs.Read(TailBytes, ScriptActionRefs.SmallDestruction);
		set => ScriptActionRefs.Write(TailBytes, ScriptActionRefs.SmallDestruction, value);
	}
}

/// <summary>
/// Where the two per-object mission-action refs sit inside a flyer's or a structure's exported
/// tail. Both records place them at the same distance into <see cref="ScriptFlyerRecord.TailBytes"/>/
/// <see cref="ScriptBaseRecord.TailBytes"/>, four bytes before the trailing field; a mech's
/// tail starts further back and carries its own offsets.
/// </summary>
internal static class ScriptActionRefs {
	/// <summary>The engaged-action ref's index into either tail.</summary>
	public const int SmallEngagement = 40;

	/// <summary>And the destroyed-action ref's.</summary>
	public const int SmallDestruction = 42;

	/// <summary>
	/// Where the ten mission-counter refs start in any of the three roster tails. Every tail begins
	/// with them, and the ten operations follow at <see cref="CounterOps"/>.
	/// </summary>
	public const int CounterRefs = 0;

	/// <inheritdoc cref="CounterRefs"/>
	public const int CounterOps = 20;

	/// <summary>Mission-counter slots per record.</summary>
	private const int CounterSlots = 10;

	/// <summary>Reads one, answering <c>-1</c> for a tail too short to hold it.</summary>
	public static short Read(byte[] tail, int offset) =>
		tail.Length >= offset + 2 ? BitConverter.ToInt16(tail, offset) : (short)-1;

	/// <summary>
	/// Reads ten shorts starting at <paramref name="offset"/>, answering an empty array for a tail too
	/// short to hold them.
	/// </summary>
	public static short[] ReadSlots(byte[] tail, int offset) {
		if (tail.Length < offset + CounterSlots * 2) {
			return [];
		}

		var slots = new short[CounterSlots];
		for (int i = 0; i < slots.Length; i++) {
			slots[i] = BitConverter.ToInt16(tail, offset + i * 2);
		}

		return slots;
	}

	/// <summary>
	/// Writes a short into a record's raw span — how the named views over <c>HeadBytes</c>/
	/// <c>TailBytes</c> are edited without giving up the byte-exact round trip.
	/// </summary>
	public static void Write(byte[] bytes, int offset, short value) {
		if (bytes.Length < offset + 2) {
			throw new InvalidOperationException("This record's raw span is too short to hold that field.");
		}

		BitConverter.GetBytes(value).CopyTo(bytes, offset);
	}
}

/// <summary>
/// Block 9 entry — 52 bytes, row #14 (<see cref="BaseRosterEntry62"/>) less its GUID, condition,
/// variant key, 0x06 and pair count. <see cref="TailBytes"/> is source 0x10-0x3D with the counter
/// pairs split. Refs are block indices.
///
/// <para>DBSIM's world-spawn pass (<c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>)) builds one base/structure per live slot
/// from this record.</para>
/// </summary>
public class ScriptBaseRecord {
	/// <inheritdoc cref="BaseRosterEntry62.TypeIndex"/>
	public short TypeIndex { get; set; }

	/// <inheritdoc cref="BaseRosterEntry62.PositionRef"/>
	public short PositionRef { get; set; }

	/// <inheritdoc cref="BaseRosterEntry62.HeadingRef"/>
	public short HeadingRef { get; set; }

	public byte[] TailBytes { get; set; } = new byte[46];

	/// <summary>Exported offset <c>0x06</c> — the counter ref of each of <see cref="BaseRosterEntry62.OutOfActionReport"/>'s ten pairs.</summary>
	public short[] CounterRefs => ScriptActionRefs.ReadSlots(TailBytes, ScriptActionRefs.CounterRefs);

	/// <summary>Exported offset <c>0x1a</c> — the operation of each of those pairs, slot for slot with <see cref="CounterRefs"/>. The file always carries the row's own operations; the briefing map reuses the first two slots in its own copy of the block (docs/retail/shell/mission-map.md#what-it-reads).</summary>
	public short[] CounterOps => ScriptActionRefs.ReadSlots(TailBytes, ScriptActionRefs.CounterOps);

	/// <inheritdoc cref="BaseRosterEntry62.StartingCondition"/>
	/// <remarks>Exported offset <c>0x32</c>.</remarks>
	public short StartingCondition {
		get => ScriptActionRefs.Read(TailBytes, 0x32 - 6);
		set => ScriptActionRefs.Write(TailBytes, 0x32 - 6, value);
	}

	/// <inheritdoc cref="BaseRosterEntry62.EngagementActionRef"/>
	/// <remarks>Exported offset <c>0x2e</c>.</remarks>
	public short EngagementActionRef {
		get => ScriptActionRefs.Read(TailBytes, ScriptActionRefs.SmallEngagement);
		set => ScriptActionRefs.Write(TailBytes, ScriptActionRefs.SmallEngagement, value);
	}

	/// <inheritdoc cref="BaseRosterEntry62.DefeatActionRef"/>
	/// <remarks>Exported offset <c>0x30</c>.</remarks>
	public short DefeatActionRef {
		get => ScriptActionRefs.Read(TailBytes, ScriptActionRefs.SmallDestruction);
		set => ScriptActionRefs.Write(TailBytes, ScriptActionRefs.SmallDestruction, value);
	}
}

/// <summary>
/// Block 10 entry — 14 bytes, row #15 (<see cref="MissionOrder22"/>)'s 0x08-0x15: <b>one
/// mission-group order</b>, refs as block indices. See docs/retail/formats/script-dat.md#block-10-in-memory--22-bytes-0x16
/// and docs/retail/simulation/ai-goals.md.
/// </summary>
public class ScriptOrder {
	/// <inheritdoc cref="MissionOrder22.Verb"/>
	public short Verb { get; set; }

	/// <inheritdoc cref="MissionOrder22.FormationId"/>
	public short FormationId { get; set; }

	/// <inheritdoc cref="MissionOrder22.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="MissionOrder22.RouteRef"/>
	public short RouteRef { get; set; }

	/// <inheritdoc cref="MissionOrder22.SubjectKind"/>
	public short SubjectKind { get; set; }

	/// <inheritdoc cref="MissionOrder22.SubjectRef"/>
	public short SubjectRef { get; set; }

	/// <inheritdoc cref="MissionOrder22.ActionRef"/>
	public short ActionRef { get; set; }
}

/// <summary>
/// Block 11 entry — 156 bytes, row #16 (<see cref="MissionGroup164"/>) less its GUID, condition,
/// compound-condition partner and pair count, with its counter pairs split into
/// <see cref="CounterRefs"/> then <see cref="CounterOps"/>. The fields are
/// <see cref="MissionGroup164"/>'s, refs as block indices.
/// </summary>
public class ScriptGroup {
	/// <inheritdoc cref="MissionGroup164.PaintsGround"/>
	public short PaintsGround { get; set; }

	/// <inheritdoc cref="MissionGroup164.NearConstant"/>
	public short NearConstant { get; set; }

	/// <inheritdoc cref="MissionGroup164.DeadZone"/>
	public short[] DeadZone { get; set; } = new short[18];

	/// <inheritdoc cref="MissionGroup164.MemberKind"/>
	public short MemberKind { get; set; }

	/// <inheritdoc cref="MissionGroup164.FormationId"/>
	public short FormationId { get; set; }

	/// <inheritdoc cref="MissionGroup164.PositionRef"/>
	public short PositionRef { get; set; }

	/// <inheritdoc cref="MissionGroup164.HeadingRef"/>
	public short HeadingRef { get; set; }

	/// <inheritdoc cref="MissionGroup164.RouteRef"/>
	public short RouteRef { get; set; }

	/// <inheritdoc cref="MissionGroup164.MemberRefs"/>
	public short[] MemberRefs { get; set; } = new short[20];

	/// <inheritdoc cref="MissionGroup164.OrderRefs"/>
	public short[] OrderRefs { get; set; } = new short[10];

	/// <inheritdoc cref="MissionGroup164.Side"/>
	public short Side { get; set; }

	/// <inheritdoc cref="MissionGroup164.DeploymentActionRef"/>
	public short DeploymentActionRef { get; set; }

	/// <summary>Exported offset <c>0x72</c> — the counter ref of each of <see cref="MissionGroup164.OutOfActionReport"/>'s ten pairs.</summary>
	public short[] CounterRefs { get; set; } = new short[10];

	/// <summary>Exported offset <c>0x86</c> — the operation of each of those pairs, slot for slot with <see cref="CounterRefs"/>.</summary>
	public short[] CounterOps { get; set; } = new short[10];

	/// <inheritdoc cref="MissionGroup164.MapShown"/>
	public short MapShown { get; set; }
}

/// <summary>
/// Block 12 entry — 54 bytes, row #17 (<see cref="MissionObjective58"/>) less its condition and pair
/// count, with its counter pairs split into <see cref="CounterRefs"/> then <see cref="CounterOps"/>
/// and refs as block indices. What DBSIM builds from it is
/// docs/retail/formats/script-dat.md#block-12-in-memory--76-bytes-0x4c.
/// </summary>
public class ScriptObjective {
	/// <inheritdoc cref="MissionObjective58.Required"/>
	public short Required { get; set; }

	/// <inheritdoc cref="MissionObjective58.ConditionCode"/>
	public short ConditionCode { get; set; }

	/// <inheritdoc cref="MissionObjective58.SubjectKind"/>
	public short SubjectKind { get; set; }

	/// <inheritdoc cref="MissionObjective58.SubjectRef"/>
	public short SubjectRef { get; set; }

	/// <inheritdoc cref="MissionObjective58.PointRef"/>
	public short PointRef { get; set; }

	/// <inheritdoc cref="MissionObjective58.RouteRef"/>
	public short RouteRef { get; set; }

	/// <summary><see cref="MissionObjective58.TextRef"/>, renumbered into its <c>data\mission.str</c> line index, or <c>-1</c> for an id with no surviving line.</summary>
	public short TextRef { get; set; }

	/// <summary>The <see cref="CounterPair.CounterRef"/> of each of <see cref="MissionObjective58.Pairs"/>.</summary>
	public short[] CounterRefs { get; set; } = new short[10];

	/// <summary>The <see cref="CounterPair.Op"/> of each of <see cref="MissionObjective58.Pairs"/>, slot for slot with <see cref="CounterRefs"/>.</summary>
	public short[] CounterOps { get; set; } = new short[10];
}
