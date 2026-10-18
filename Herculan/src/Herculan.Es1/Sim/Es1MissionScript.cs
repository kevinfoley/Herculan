using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// One roster's slot arrays as the first pass fills them (docs/es1/formats/script-dat.md#reading): which slots are live,
/// each slot's number in the roster's pool, and each slot's type.
/// </summary>
public sealed class Es1Roster {
	public Es1Roster(int capacity) {
		Live = new bool[capacity];
		Map = new sbyte[capacity];
		Types = new short[capacity];
	}

	/// <summary>Whether the slot is live: mechs <c>000af7b4</c>, flyers <c>000af8e0</c>, bases <c>000af948</c>.</summary>
	public bool[] Live { get; }

	/// <summary>The slot-to-pool map, the live slots numbered from 0 in slot order and 0 elsewhere: mechs <c>000af818</c>, flyers <c>000af914</c>, bases <c>000af97c</c>.</summary>
	public sbyte[] Map { get; }

	/// <summary>Each slot's type, from its block record or, for the squad's slots, its <c>data\player.mec</c> entry.</summary>
	public short[] Types { get; }

	/// <summary>How many slots are live, the size the pool is made to.</summary>
	public int LiveCount { get; private set; }

	/// <summary>Marks <paramref name="slot"/> live; past the array, which the original writes unchecked, it throws.</summary>
	/// <exception cref="InvalidDataException">The slot is outside the array.</exception>
	public void MarkLive(int slot) {
		if (slot < 0 || slot >= Live.Length) {
			throw new InvalidDataException($"Slot {slot} is past the {Live.Length}-slot array, which the original writes past unchecked.");
		}

		Live[slot] = true;
	}

	/// <summary>Numbers the live slots below <paramref name="count"/> from <see cref="LiveCount"/> on, in slot order.</summary>
	public void Number(int count) {
		for (int slot = 0; slot < count && slot < Live.Length; slot++) {
			if (Live[slot]) {
				Map[slot] = (sbyte)LiveCount++;
			}
		}
	}

	/// <summary>Marks one more slot live and gives it the next number, as the squad's slots are numbered.</summary>
	public void AppendLive(int slot) {
		MarkLive(slot);
		Map[slot] = (sbyte)LiveCount++;
	}
}

/// <summary>A block-4 trigger area as <c>TriggerArea_Resolve</c> (<c>ES1DBSIM 0001cb48</c>) resolves it (docs/es1/simulation/mission-groups.md#trigger-areas).</summary>
/// <param name="Shape">The type: 0 a box between two points, any other a circle about one.</param>
/// <param name="FirstPoint">The block-1 point the second word indexes.</param>
/// <param name="SecondPointOrRadius">For type 0 the block-1 point the third word indexes; otherwise the third word × 10.</param>
public sealed record Es1TriggerArea(short Shape, int FirstPoint, int SecondPointOrRadius);

/// <summary>A block-5 action as the first pass builds its 16-byte record (docs/es1/formats/script-dat.md#reading).</summary>
public sealed class Es1Action {
	/// <inheritdoc cref="Es1ScriptAction.Type"/>
	public short Type { get; init; }

	/// <inheritdoc cref="Es1ScriptAction.Verb"/>
	public short Verb { get; init; }

	/// <summary>The area refs before the first negative one, block-4 indices.</summary>
	public int[] Areas { get; init; } = [];

	/// <summary><c>+0xa</c>, set once the action has fired (docs/es1/simulation/mission-groups.md#actions).</summary>
	public bool Activated { get; set; }

	/// <summary><c>+0xc</c>, the flag the action writes, from <c>.MSN</c> <c>0x1a</c>.</summary>
	public short Flag { get; init; }

	/// <summary><c>+0xe</c>, the operation, from <c>.MSN</c> <c>0x1c</c>.</summary>
	public short Operation { get; init; }
}

/// <summary>
/// <c>DBSim_LoadScriptDat</c> (<c>ES1DBSIM 0001d850</c>), the world load's first pass, over the three files
/// (docs/es1/formats/script-dat.md#reading): the flags, the squad, the header and blocks 1 to 5 kept, and the rosters'
/// slot arrays filled from blocks 6 to 9 and the squad.
/// </summary>
public sealed class Es1MissionScript {
	/// <summary>The mech slot arrays' length, 100 bytes each.</summary>
	public const int MechSlots = 100;

	/// <summary>The flyer and base slot arrays' length, 50 bytes each.</summary>
	public const int FlyerSlots = 50;

	/// <inheritdoc cref="FlyerSlots"/>
	public const int BaseSlots = 50;

	/// <summary>The flag the first pass zeroes, <c>000bc4c6</c>.</summary>
	public const int ZeroedFlag = 7;

	/// <summary>How far left the read shifts a stored heading, <c>SHL EAX,0xe</c> at <c>0001dd69</c>.</summary>
	public const int HeadingShift = 14;

	private Es1MissionScript(Es1MissionFiles files) {
		Files = files;
		Flags = (short[])files.Flags.Clone();
		Flags[ZeroedFlag] = 0;
	}

	/// <summary>The files this was read from; the second pass reads blocks 6 to 9 from <see cref="Es1MissionFiles.Script"/> again.</summary>
	public Es1MissionFiles Files { get; }

	/// <summary>The 100 flags at <c>000bc4b8</c>, flag 7 zeroed.</summary>
	public short[] Flags { get; }

	/// <summary><c>data\player.mec</c>'s entries, at <c>000af9e4</c>.</summary>
	public Es1PlayerMec Player => Files.Player;

	/// <summary>The header, at <c>000bc49c</c>.</summary>
	public short[] Header => Files.Script.Header;

	/// <summary>Block 1, at <c>000bc480</c>.</summary>
	public Vec3i[] Points { get; private set; } = [];

	/// <summary>The least and greatest X and Y of the points, <c>000af7a4</c>–<c>000af7b0</c>; zeros with no point.</summary>
	public (int MinX, int MinY, int MaxX, int MaxY) Bounds { get; private set; }

	/// <summary>Block 2 as kept at <c>000bc484</c>: each stored heading shifted left 14 into 16 bits, a binary angle.</summary>
	public short[] Headings { get; private set; } = [];

	/// <summary>Block 3, at <c>000bc48a</c>: each waypoint group's points, as block-1 indices.</summary>
	public int[][] Routes { get; private set; } = [];

	/// <summary>Block 4, at <c>000bc490</c>.</summary>
	public Es1TriggerArea[] TriggerAreas { get; private set; } = [];

	/// <summary>Block 5, at <c>000bc496</c>.</summary>
	public Es1Action[] Actions { get; private set; } = [];

	/// <summary>The mech roster, the squad's slots after block 6's.</summary>
	public Es1Roster Mechs { get; } = new(MechSlots);

	/// <summary>The slots of the squad's machines, <c>000af87c</c>.</summary>
	public bool[] MechSquadSlots { get; } = new bool[MechSlots];

	/// <summary>The flyer roster.</summary>
	public Es1Roster Flyers { get; } = new(FlyerSlots);

	/// <summary>The base roster.</summary>
	public Es1Roster Bases { get; } = new(BaseSlots);

	/// <summary>Per base slot, the low byte of its group's <c>.MSN</c> <c>0x4a</c> word, <c>000af9b0</c>.</summary>
	public byte[] BaseGroupBytes { get; } = new byte[BaseSlots];

	/// <summary>The mech types in the order <c>MechTypeSlot_Allocate</c> first gives each a slot: the live slots' in slot order.</summary>
	public IReadOnlyList<short> MechTypeSlots { get; private set; } = [];

	/// <summary>The binary angle a stored heading <paramref name="value"/> reads as: (<i>h</i> mod 4) quarter turns.</summary>
	public static short DecodeHeading(short value) => unchecked((short)(value << HeadingShift));

	/// <summary>Runs the first pass over <paramref name="files"/>.</summary>
	/// <exception cref="InvalidDataException">A slot falls past its array, which the original writes past unchecked.</exception>
	public static Es1MissionScript Read(Es1MissionFiles files) {
		var script = new Es1MissionScript(files);
		var dat = files.Script;

		script.Points = dat.Points.Select(p => new Vec3i(p.X, p.Y, p.Z)).ToArray();
		if (script.Points.Length > 0) {
			script.Bounds = (script.Points.Min(p => p.X), script.Points.Min(p => p.Y), script.Points.Max(p => p.X),
				script.Points.Max(p => p.Y));
		}

		script.Headings = dat.Headings.Select(DecodeHeading).ToArray();
		script.Routes = dat.WaypointGroups.Select(group => group.Select(point => (int)point).ToArray()).ToArray();
		script.TriggerAreas = dat.TriggerAreas
			.Select(a => new Es1TriggerArea(a.Shape, a.PointRef, a.Shape == 0 ? a.Word0A : a.Word0A * 10)).ToArray();
		script.Actions = dat.Actions.Select(a => new Es1Action {
			Type = a.Type,
			Verb = a.Verb,
			Areas = a.AreaRefs.TakeWhile(area => area >= 0).Select(area => (int)area).ToArray(),
			Flag = a.Word1A,
			Operation = a.Word1C,
		}).ToArray();

		for (int i = 0; i < dat.Mechs.Length && i < MechSlots; i++) {
			script.Mechs.Types[i] = dat.Mechs[i].Type;
		}

		for (int i = 0; i < dat.Flyers.Length && i < FlyerSlots; i++) {
			script.Flyers.Types[i] = dat.Flyers[i].Type;
		}

		for (int i = 0; i < dat.Bases.Length && i < BaseSlots; i++) {
			script.Bases.Types[i] = dat.Bases[i].Type;
		}

		// Block 9 from its second record on: the first stands for the squad, whose machines are player.mec's.
		foreach (var group in dat.Groups.Skip(1)) {
			foreach (short member in group.MemberRefs) {
				if (member < 0) {
					continue;
				}

				switch (group.MemberKind) {
					case 0:
						script.Mechs.MarkLive(member);
						break;
					case 1:
						script.Flyers.MarkLive(member);
						break;
					case 2:
						script.Bases.MarkLive(member);
						script.BaseGroupBytes[member] = unchecked((byte)group.Word4A);
						break;
				}
			}
		}

		// The slot-to-pool maps. The mech numbering runs on through one slot per player.mec entry.
		script.Mechs.Number(dat.Mechs.Length);
		for (int k = 0; k < files.Player.Entries.Length; k++) {
			int slot = dat.Mechs.Length + k;
			script.Mechs.AppendLive(slot);
			script.MechSquadSlots[slot] = true;
			script.Mechs.Types[slot] = files.Player.Entries[k].Herc?.Chassis ?? 0;
		}

		script.Flyers.Number(dat.Flyers.Length);
		script.Bases.Number(dat.Bases.Length);

		var types = new List<short>();
		for (int slot = 0; slot < MechSlots; slot++) {
			if (script.Mechs.Live[slot] && !types.Contains(script.Mechs.Types[slot])) {
				types.Add(script.Mechs.Types[slot]);
			}
		}

		script.MechTypeSlots = types;
		return script;
	}
}
