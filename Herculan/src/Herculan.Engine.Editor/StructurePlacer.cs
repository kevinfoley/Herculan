using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a structure to the open mission, as one step of <see cref="PointMover"/>'s undo history: a row-14 roster record,
/// and a row-16 group of its own that places it, standing on a row-6 point appended for it and facing a row-7 heading of
/// 0 degrees. Each record is appended to the <c>.MSN</c> unconditioned, with no variant key and a new GUID
/// (<see cref="MissionDocument.Append"/>), so every version of the mission has it; the scene is then synced
/// with the document (<see cref="SceneSync"/>), which builds the structure's object for the record
/// (docs/herculan/handoff-editor-document.md#the-tools).
///
/// <para>The records take the shape the retail missions give a lone structure most often — 148 of their 537 structure
/// groups (<c>HercWorks.Query orders</c>): one member, in formation 0, standing on the group's own point and facing its
/// own heading, with no orders, no deployment action and no out-of-action writes. The roster record names neither a
/// point nor a heading of its own, so the structure stands on the group's point as the group's slot 0, with the group's
/// heading (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 7); it starts pristine, fires no
/// action and writes no counter, every unused pair <c>-1</c> as in the retail files. A group naming a heading needs no
/// slot-0 order (<see cref="ScriptDatLint"/>). The group does not paint its ground. Which side it is on is the
/// caller's. A row-7 heading of 0 degrees that is its GUID's one record, unconditioned and drawing no variant, is shared
/// rather than appended (<see cref="MissionDocument.ShareableHeading"/>).</para>
///
/// <para>A structure can instead join an existing structure group (<see cref="WhyNotJoin"/>): its record then names a
/// point of its own where it was put, and it takes the group's first member slot that no record of the group's GUID
/// fills in any state (<see cref="MissionDocument.FreeMemberSlot"/>), with the group's side and heading.</para>
///
/// <para>Block 9 is capped at DBSIM's structure slots (<see cref="ScriptDatLint.BaseSlotCap"/>), past which its load
/// writes over the globals that follow (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps):
/// an add that would pass it in any version of the mission is refused, counting every row-14 GUID of the layout whatever its
/// condition.</para>
/// </summary>
internal sealed class StructurePlacer {
	// A structure group's roster and formation, as row 16 numbers them.
	private const short BaseRoster = 2;
	private const short Formation = 0;

	private const int PointRow = 6;
	private const int HeadingRow = 7;
	private const int BaseRow = 14;
	private const int GroupRow = 16;

	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	public StructurePlacer(MissionDocument source, MissionScene scene, MissionIndex index,
			PointMover mover, SceneSync sync, SelectionState selection) {
		_source = source;
		_scene = scene;
		_terrain = scene.World.Terrain;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
	}

	/// <summary>Why no structure can be added, or null when one can: block 9 is full in some version of the mission.</summary>
	public string? WhyNoAdd {
		get {
			int structures = _source.Layout.Entries(BaseRow).Count;
			return structures >= ScriptDatLint.BaseSlotCap
				? $"At maximum structure limit ({ScriptDatLint.BaseSlotCap})."
				: null;
		}
	}

	/// <summary>
	/// Adds a structure of <c>BASES.DAT</c> type <paramref name="typeIndex"/> on <paramref name="side"/>, standing on
	/// the ground at (<paramref name="x"/>, <paramref name="y"/>), in a group of its own or, given
	/// <paramref name="joinGroup"/>, as a member of that structure group; and selects it. Answers why it could not, or
	/// null when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y, short? joinGroup = null) {
		if (WhyNoAdd is { } refused) {
			return refused;
		}

		if (joinGroup is { } joined) {
			return Join(typeIndex, joined, x, y);
		}

		var source = _source;
		var position = new Vec3i(x, y, _terrain.HeightAtWorld(x, y));
		short structure = -1;
		return Commit(typeIndex, () => source.Change(() => {
			source.Append(PointRow, Point(position), out short point);
			short heading = source.ShareableHeading(0);
			if (heading == -1) {
				source.Append(HeadingRow, new Heading10 { ConditionRef = -1, VariantKey = -1, Unk06 = -1, Degrees = 0 }, out heading);
			}

			source.Append(BaseRow, Structure(typeIndex), out structure);
			source.Append(GroupRow, Group(structure, point, heading, side), out _);
		}), () => structure);
	}

	/// <summary>
	/// Why a structure cannot be added to <paramref name="group"/>, by its row-16 GUID, or null when it can: the group must be
	/// a structure group of the mission, other than the player's squad, with a member slot no record of it fills in any state.
	/// </summary>
	public string? WhyNotJoin(short group) =>
		WhyNoAdd is { } full ? full
			: _index.Group(group) is not { } planned ? EditorToolbox.GroupGone
			: planned.IsSquad || planned.Kind != MissionUnitKind.Base ? "It is not a group of structures."
			: _source.FreeMemberSlot(group) < 0 ? "It already has 20 members, as many as a group holds."
			: null;

	// Adds a structure to an existing structure group, in the first member slot no record of the group fills. DBSIM walks
	// all 20 member refs by their raw index (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 7), so a
	// slot free earlier in the list serves as well as one at its end. The record names a point of its own at (x, y), which
	// wins over the slot's formation offset from the group's point (rule 2), and no heading, so it faces the group's way;
	// its side is the group's.
	private string? Join(int typeIndex, short groupGuid, int x, int y) {
		if (WhyNotJoin(groupGuid) is { } reason) {
			return reason;
		}

		var source = _source;
		int memberSlot = source.FreeMemberSlot(groupGuid);
		var position = new Vec3i(x, y, _terrain.HeightAtWorld(x, y));
		short structure = -1;
		return Commit(typeIndex, () => source.Change(() => {
			source.Append(PointRow, Point(position), out short point);
			var record = Structure(typeIndex);
			record.PositionRef = point;
			source.Append(BaseRow, record, out structure);
			source.SetField(MsnField.GroupMember(memberSlot), groupGuid, structure);
		}), () => structure);
	}

	// Makes an add's change and syncs the scene with it, then records it as one step and selects the object built for
	// the structure's row-14 GUID; or, when none was built, puts the records back and says why.
	private string? Commit(int typeIndex, Func<MsnEdit> add, Func<short> structure) {
		MsnEdit change;
		try {
			change = _sync.Run(add);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		short added = structure();
		if (_index.ObjectOf(new ObjectKey(BaseRow, added)) == null) {
			_sync.Undo(change);
			return $"The install has no structure type {typeIndex}.";
		}

		_mover.Record(new RecordsEdit(_sync, change));
		_selection.Current = new ObjectSelection(BaseRow, added);
		return null;
	}

	/// <summary>
	/// The structure <see cref="Add"/> would add of <paramref name="typeIndex"/> on <paramref name="side"/>, built the
	/// same way but in no roster slot or group and recorded nowhere, for <see cref="DropPreview"/> to draw; null for a
	/// type the install has nothing for.
	/// </summary>
	public SceneObject? Ghost(int typeIndex, MissionSide side) =>
		_scene.CreateObject(new MissionPlacement(MissionUnitKind.Base, typeIndex, null, -1, -1, Vec3i.Zero, 0,
			Array.Empty<short>(), Array.Empty<short>(), Side: side, OutOfActionReport: new OutOfActionReport(Unset(10), Unset(10))));

	// The point a new structure stands on, at the ground's height.
	private static MapPoint22 Point(Vec3i position) =>
		new() { ConditionRef = -1, VariantKey = -1, Unk06 = -1, X = position.X, Y = position.Y, Z = position.Z };

	// The record a new structure gets: no point or heading of its own, pristine, no actions, every counter pair unused.
	private static BaseRosterEntry62 Structure(int typeIndex) => new() {
		ConditionRef = -1,
		VariantKey = -1,
		Unk06 = -1,
		TypeIndex = (short)typeIndex,
		PositionRef = -1,
		HeadingRef = -1,
		OutOfActionReport = Unset(20),
		EngagementActionRef = -1,
		DefeatActionRef = -1,
		StartingCondition = MissionPlacement.PristineCondition,
	};

	// The group a new structure gets: it alone, in slot 0, on the given point and heading, with nothing else set.
	private static MissionGroup164 Group(short structure, short point, short heading, MissionSide side) {
		var members = Unset(20);
		members[0] = structure;
		return new MissionGroup164 {
			ConditionRef = -1,
			CompoundConditionPartner = -1,
			MemberKind = BaseRoster,
			FormationId = Formation,
			PositionRef = point,
			HeadingRef = heading,
			RouteRef = -1,
			MemberRefs = members,
			OrderRefs = Unset(10),
			Side = (short)side,
			DeploymentActionRef = -1,
			OutOfActionReport = Unset(20),
			MapShown = -1
		};
	}

	private static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();
}
