using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// The simulation's world after the mission start: the mech pool in build order, the live object list, the groups and the
/// player's machine. <see cref="Spawn"/> is <c>DBSim_SpawnMissionObjects</c> (<c>ES1DBSIM 0001e638</c>), the second pass
/// (docs/es1/formats/script-dat.md#the-second-pass), and <see cref="LocalPlayer"/> the machine <c>Sim_StartMission</c>
/// takes for <c>LocalPlayerMech</c> (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc).
///
/// <para><see cref="Es1Simulation"/> runs the tick on it.</para>
/// </summary>
public sealed class Es1World {
	/// <summary>The position sentinel a negative point ref gives, <c>000afa00</c>.</summary>
	public static readonly Vec3i PositionSentinel = new(0, 0, -1);

	/// <summary>The rotation sentinel a negative heading ref gives, <c>000afa10</c>.</summary>
	public static readonly (short X, short Y, short Z) RotationSentinel = (Es1Mech.RotationSentinel, Es1Mech.RotationSentinel, 0);

	/// <summary><c>Mission_StatusRaised</c>'s value at the second pass's start.</summary>
	public const short StatusRaisedAtStart = -1;

	private readonly Dictionary<short, Es1MechType?> _types;

	private Es1World(Es1MissionScript script, Dictionary<short, Es1MechType?> types) {
		Script = script;
		_types = types;
		Globals.MechPool = MechPool;
	}

	/// <summary>The first pass this world was built from.</summary>
	public Es1MissionScript Script { get; }

	/// <summary><c>g_MechPool</c>'s machines in the order they were built, which <c>Pool_Nth</c> counts.</summary>
	public List<Es1Mech> MechPool { get; } = new();

	/// <summary>The globals the machines' ticks read: the clock, the generator, the live objects and the shapes they walk into.</summary>
	public Es1SimGlobals Globals { get; } = new();

	/// <inheritdoc cref="Es1SimGlobals.LiveObjects"/>
	public List<IEs1SimObject> LiveObjects => Globals.LiveObjects;

	/// <summary><c>MissionGroups</c> (<c>000bc476</c>).</summary>
	public List<Es1MissionGroup> Groups { get; } = new();

	/// <summary><c>PlayerMech</c> (<c>000bc42a</c>), the machine of <c>data\player.mec</c>'s player entry.</summary>
	public Es1Mech? PlayerMech { get; private set; }

	/// <summary><c>Mission_StatusRaised</c> (<c>000bc49a</c>).</summary>
	public short StatusRaised { get; private set; }

	/// <summary>The types loaded for the live slots, by type number; null for a type whose files are missing.</summary>
	public IReadOnlyDictionary<short, Es1MechType?> Types => _types;

	/// <summary>
	/// <c>LocalPlayerMech</c> (<c>000b5b86</c>): the last machine of <c>g_MechPool</c> whose <c>+0x9d</c> is set, or null.
	/// </summary>
	public Es1Mech? LocalPlayer => MechPool.LastOrDefault(mech => mech.IsPlayer);

	/// <summary>
	/// The first pass's type registration for the mechs (<c>MechTypeSlot_Allocate</c>, then <c>Mechs_InitAllTypes</c>) and
	/// the second pass. A type whose files are missing builds no machine; the original asserts.
	/// </summary>
	public static Es1World Spawn(Es1MissionScript script, GameContent content, Es1MechFormations formations) {
		var names = new NameListTransformer().Parse(content.ReadRequired("nam", "MECHS.NAM"))
			?? throw new InvalidDataException("nam\\MECHS.NAM does not read.");
		var types = new Dictionary<short, Es1MechType?>();
		for (short slot = 0; slot < script.MechTypeSlots.Count; slot++) {
			short type = script.MechTypeSlots[slot];
			types[type] = Es1MechType.Load(content, names, type, slot);
		}

		var weapons = new Es1WeaponsSimTransformer().Parse(content.ReadRequired("dat", "WEAPONS.DAT"))
			?? throw new InvalidDataException("dat\\WEAPONS.DAT does not read.");

		// WeaponModels_LoadShapeChunks (0000bc60): dts\mechwpns's chunks, which exist while the HERCs are built. Each
		// mount reads its model from its chunk afresh, so the move to its mount point lands in its copy alone.
		var weaponModels = content.Read("dts", "MECHWPNS.DTS") is { } modelBytes
			? new WeaponModelChunks(modelBytes)
			: new WeaponModelChunks([]);

		var world = new Es1World(script, types);
		// The rounds' phase-2 loads (docs/es1/simulation/projectiles.md#the-projectile-pool).
		world.Globals.Projectiles = Projectiles.Es1ProjectilePool.Load(content, weapons);
		world.Globals.HeaderWord3 = script.Header[3];
		world.Globals.MissionModeFlag = script.Header[2];
		world.Globals.UnlimitedAmmoFlag = script.Header[5];
		world.Globals.HeaderWord6 = script.Header[6];
		world.Globals.HeaderWord7 = script.Header[7];
		world.Globals.HeaderWord8 = script.Header[8];
		world.SpawnObjects(content, weapons, weaponModels, formations);
		return world;
	}

	/// <summary>
	/// The top-level chunks of <c>dts\mechwpns</c>, by the offset each starts at, which <c>WeaponMount_ShapeForMountingCode</c>
	/// loads a mount's model from (docs/es1/simulation/weapon-mounts.md#the-weapon-models).
	/// </summary>
	private sealed class WeaponModelChunks {
		private readonly byte[] _bytes;
		private readonly List<int> _starts = new();

		public WeaponModelChunks(byte[] bytes) {
			_bytes = bytes;
			var reader = new DTSModelTransformer();
			int index = 0;
			while (index < bytes.Length) {
				int start = index;
				if (reader.ReadOneObject(bytes, ref index) != null) {
					_starts.Add(start);
				}
			}
		}

		/// <summary>A fresh read of chunk <paramref name="index"/>, or null past the chunks.</summary>
		public TSObject? Read(int index) {
			if (index < 0 || index >= _starts.Count) {
				return null;
			}

			int at = _starts[index];
			return new DTSModelTransformer().ReadOneObject(_bytes, ref at);
		}
	}

	private void SpawnObjects(GameContent content, Es1Weapons weapons, WeaponModelChunks weaponModels,
			Es1MechFormations formations) {
		StatusRaised = StatusRaisedAtStart;
		var dat = Script.Files.Script;
		var entries = Script.Player.Entries;

		// WeaponMount_ShapeForMountingCode (0000bd70): chunk template[0x22 + code × 2].
		TSObject? ModelFor(Weapons.WeaponMountTemplate template, int code) => weaponModels.Read(template.ModelShapeIndex(code));

		// Block 6, then the squad.
		int slots = dat.Mechs.Length + entries.Length;
		for (int slot = 0; slot < slots; slot++) {
			if (!Script.Mechs.Live[slot] || _types.GetValueOrDefault(Script.Mechs.Types[slot]) is not { } type) {
				continue;
			}

			var mech = new Es1Mech(type, Globals.DwellJitter);
			MechPool.Add(mech);
			Globals.AddLiveObject(mech);

			if (slot < dat.Mechs.Length) {
				SpawnRecordMech(mech, dat.Mechs[slot], weapons, ModelFor);
			} else {
				int k = slot - dat.Mechs.Length;
				SpawnSquadMech(mech, k, weapons, ModelFor);
			}
		}

		// PLACEHOLDER: blocks 7 and 8 build no object: their constructors, 0001ac58 (a flyer from its type) and 00003cbc (a
		// base from its record), are unread (docs/es1/formats/script-dat.md#open). The plan's flyers-and-bases step ports
		// them; the zone's placed shapes still draw.

		// Block 9. A group's target is a pointer into the array the first pass allocated, so a target group later in the
		// array is the record that group's turn fills.
		for (int i = 0; i < dat.Groups.Length; i++) {
			Groups.Add(BuildGroupRecord(i, dat.Groups[i], formations));
		}

		for (int i = 0; i < dat.Groups.Length; i++) {
			var record = dat.Groups[i];
			Groups[i].Target = record.Ref4ERow switch {
				0 => record.Ref4E >= 0 && record.Ref4E < Groups.Count ? Groups[record.Ref4E] : null,
				1 when record.Ref4E >= 0 => PoolNth(MapEntry(Script.Mechs, record.Ref4E)),
				_ => null,
			};
		}

		// The spawn places no shadow: Mech_PlaceLegsOnGround's callers are the move and the ram's tick
		// (docs/es1/simulation/mech-locomotion.md#shadows-and-footfalls--mech_placelegsonground-000144dc), so the shadows
		// stay at their build depth until the machine's first move, as retail's do.
	}

	/// <summary>A block-6 record's machine: its point and heading, its two words, its loadout and its actions.</summary>
	private void SpawnRecordMech(Es1Mech mech, Es1ScriptMech record, Es1Weapons weapons,
			Func<Weapons.WeaponMountTemplate, int, TSObject?> modelFor) {
		if (record.PointRef >= 0 && record.PointRef < Script.Points.Length) {
			var point = Script.Points[record.PointRef];
			mech.Position = new Vec3i(point.X, point.Y, point.Z + mech.Type.Chassis.RideHeight);
		} else {
			mech.Position = PositionSentinel;
		}

		mech.Rotation = record.HeadingRef >= 0 && record.HeadingRef < Script.Headings.Length
			? ((short)0, (short)0, Script.Headings[record.HeadingRef])
			: RotationSentinel;

		mech.Word08Set = record.Words08[0] != 0;
		if (record.Words08[1] >= 0) {
			mech.Word1E6 = record.Words08[1];
		}

		// The record's 49 words, which the loadout's slot index reads unchecked: the fit at 0x2a, the ammunition at 0x46.
		short[] words = [.. record.Words08, record.Type, .. record.Words32, record.PointRef, record.HeadingRef, record.Word4A,
			record.Word4C, .. record.Words4E, record.Word62, record.ActionRef64, record.ActionRef66, record.Word68];
		mech.ConfigureLoadout(slot => WordAt(words, 21 + slot), slot => WordAt(words, 35 + slot), weapons, modelFor);

		mech.Word174 = (record.Word4A, record.Word4C);
		mech.Actions = (ActionAt(record.ActionRef64), ActionAt(record.ActionRef66));

		Es1MechDamage.ApplyStartingCondition(mech, record.Word68, Globals);
	}

	/// <summary>
	/// A <c>data\player.mec</c> entry's machine: both sentinels, the entry's loadout, its pilot word, and for the player's
	/// entry <c>PlayerMech</c> and <c>+0x9d</c>.
	/// </summary>
	private void SpawnSquadMech(Es1Mech mech, int entry, Es1Weapons weapons,
			Func<Weapons.WeaponMountTemplate, int, TSObject?> modelFor) {
		// Right after the constructor, before the sentinels and the loadout, so the loadout builds the player's machine the
		// local mount manager (docs/es1/formats/script-dat.md#the-second-pass).
		if (entry == Script.Player.PlayerEntry) {
			PlayerMech = mech;
			mech.IsPlayer = true;
		}

		mech.Position = PositionSentinel;
		mech.Rotation = RotationSentinel;
		var source = Script.Player.Entries[entry];

		// The entry's 0x7e-byte record as the first pass read it, which the loadout's slot index reads unchecked: the
		// weapons at +8, the mount words at +0x20.
		var herc = source.Herc;
		var record = new short[0x7e / 2];
		record[0] = source.Word1;
		record[1] = source.Word2;
		if (herc != null) {
			record[2] = herc.Chassis;
			record[3] = (short)herc.Weapons.Length;
			herc.Weapons.Take(12).ToArray().CopyTo(record, 4);
			herc.MountWords.Take(13).ToArray().CopyTo(record, 16);
			record[29] = herc.Word;
			herc.Status.CopyTo(record, 30);
		}

		mech.ConfigureLoadout(slot => WordAt(record, 4 + slot), slot => WordAt(record, 16 + slot), weapons, modelFor);

		// Mech_ApplySquadCondition (000107f0) with the entry's +0x6a, +0x56 and +0x3c, the hardpoints', internals' and
		// externals' conditions.
		mech.Damage?.ApplySquadCondition(record[(0x6a / 2)..(0x7e / 2)], record[(0x56 / 2)..(0x6a / 2)], record[(0x3c / 2)..(0x56 / 2)]);
		mech.PilotWord = source.Word1;
	}

	/// <summary>A word of a record by an index the original does not bound; past the record, 0 here.</summary>
	private static short WordAt(short[] words, int index) => index >= 0 && index < words.Length ? words[index] : (short)0;

	private Es1Action? ActionAt(short index) =>
		index >= 0 && index < Script.Actions.Length ? Script.Actions[index] : null;

	/// <summary>
	/// <c>DBSim_BuildGroupRecord</c> (<c>0001cff4</c>) for block-9 record <paramref name="index"/>, and each member attached
	/// (docs/es1/simulation/mission-groups.md#the-group-record).
	/// </summary>
	private Es1MissionGroup BuildGroupRecord(int index, Es1ScriptGroup record, Es1MechFormations formations) {
		var routeRefs = record.WaypointGroupRef >= 0 && record.WaypointGroupRef < Script.Routes.Length
			? Script.Routes[record.WaypointGroupRef]
			: null;
		var route = routeRefs?.Select(PointAt).ToArray();
		short order = index == 0 && record.Word32 == Es1MissionGroup.TravellingOrder ? Es1MissionGroup.SquadPatrolOrder : record.Word32;
		var group = new Es1MissionGroup {
			MemberKind = record.MemberKind,
			Formation = record.Word30,
			Order = order,
			Route = route,
			RoutePointRefs = routeRefs,
			Heading = Es1MissionGroup.HeadingFor(record, Script, route),
			Side = unchecked((byte)record.Word4A),
			Action = ActionAt(record.ActionRef),
			TargetKind = record.Ref4ERow,
			Point = record.PointRef >= 0 && record.PointRef < Script.Points.Length ? Script.Points[record.PointRef] : null,
			OutOfAction = (record.Word54, record.Word56),
		};

		// The member refs through the slot-to-pool map their kind names; the first record's are the squad's machines.
		var refs = new int[record.MemberRefs.Length];
		for (int k = 0; k < refs.Length; k++) {
			refs[k] = index == 0
				? k < Script.Player.Entries.Length ? MapEntry(Script.Mechs, Script.Files.Script.Mechs.Length + k) : -1
				: record.MemberRefs[k] < 0 ? -1 : MapEntry(RosterOf(record.MemberKind), record.MemberRefs[k]);
		}

		for (int k = 0; k < refs.Length; k++) {
			if (refs[k] < 0) {
				continue;
			}

			var member = record.MemberKind == 0 ? PoolNth(refs[k]) : null;
			group.Members.Add(member);
			if (member != null) {
				Es1Formation.Attach(member, k, group, formations);
			}

			// PLACEHOLDER: a flyer or base member is attached by 0001b574 or 000044a4, unread
			// (docs/es1/simulation/mission-groups.md#open); with blocks 7 and 8 unbuilt there is none to attach.
		}

		return group;
	}

	private Es1Roster? RosterOf(short kind) => kind switch {
		0 => Script.Mechs,
		1 => Script.Flyers,
		2 => Script.Bases,
		_ => null,
	};

	private static int MapEntry(Es1Roster? roster, int slot) =>
		roster != null && slot >= 0 && slot < roster.Map.Length ? roster.Map[slot] : -1;

	/// <summary><c>Pool_Nth</c> (<c>00063a78</c>) on <c>g_MechPool</c>: the <i>n</i>-th machine built, or null when the walk ends first.</summary>
	private Es1Mech? PoolNth(int n) => n >= 0 && n < MechPool.Count ? MechPool[n] : null;

	private Vec3i PointAt(int index) => index >= 0 && index < Script.Points.Length ? Script.Points[index] : Vec3i.Zero;
}
