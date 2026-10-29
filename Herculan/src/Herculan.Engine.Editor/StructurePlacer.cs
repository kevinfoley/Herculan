using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a structure to the open mission, as one step of <see cref="PointMover"/>'s undo history: a row-14 roster record,
/// and a row-16 group of its own that places it, standing on a row-6 point appended for it and facing a row-7 heading of
/// 0 degrees. Each record is appended to the <c>.MSN</c> unconditioned, with no random table key and a new GUID
/// (<see cref="MissionDocument.Append"/>), so every version of the mission has it; the scene is then synced
/// with the document (<see cref="SceneSync"/>), which builds the structure's object for the record
/// (docs/herculan/editor-design.md#which-record-an-edit-writes).
///
/// <para>The records take the shape the retail missions give a lone structure most often — 148 of their 537 structure
/// groups (<c>HercWorks.Query orders</c>): one member, in formation 0, standing on the group's own point and facing its
/// own heading, with no orders, no deployment action and no out-of-action writes. The roster record names neither a
/// point nor a heading of its own, so the structure stands on the group's point as the group's slot 0, with the group's
/// heading (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 7); it starts pristine, fires no
/// action and writes no counter, every unused pair <c>-1</c> as in the retail files. A group naming a heading needs no
/// first order (<see cref="MissionCheckKind.NoHeadingNoOrder"/>). The group does not paint its ground. Which side it is on is the
/// caller's. A row-7 heading of 0 degrees that is its GUID's one record, unconditioned and picking from no random table, is shared
/// rather than appended (<see cref="MissionDocument.ShareableHeading"/>).</para>
///
/// <para>A structure can instead join an existing structure group (<see cref="WhyNotJoin"/>): its record then names a
/// point of its own where it was put, and it takes the group's first member slot that no record of the group's GUID
/// fills in any state (<see cref="MissionDocument.FreeMemberSlot"/>), with the group's side and heading.</para>
///
/// <para>A structure or ground vehicle can also be put in a chosen member slot of a structure group from the group's view
/// (<see cref="RequestJoin"/>, <see cref="WhyNotJoinSlot"/>): a slot no record of the group's GUID fills in any state
/// (<see cref="MissionDocument.MemberSlotFree"/>), its record naming no point of its own, so it stands on that slot's
/// formation spot, or on the group's point past the formation's slots (rule 7). It is not selected, so the group's view
/// stays. A ground vehicle joins a convoy only up to <see cref="VehicleGroups.LastJoinSlot"/>, as the Toolbox's join does.
/// This edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
///
/// <para>A whole base can be added at once (<see cref="AddBase"/>): one structure group in a <see cref="BaseTemplate"/>'s
/// formation, laying its pad, with one record per member slot that names no point or heading of its own, so each stands on
/// its formation spot (docs/retail/formats/script-dat.md#base-formation-terrain).</para>
///
/// <para>Block 9 is capped at DBSIM's structure slots (<see cref="ScriptDatLint.BaseSlotCap"/>), past which its load
/// writes over the globals that follow (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps):
/// an add that would pass it in any version of the mission is refused, counted as <see cref="RosterLimits"/> counts it.</para>
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
	private readonly BaseFormationTable _formations;
	private Func<string?>? _requested;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="formations">The structure formations, which place a base's members (<see cref="BasePlaces"/>).</param>
	public StructurePlacer(MissionDocument source, MissionScene scene, MissionIndex index,
			PointMover mover, SceneSync sync, SelectionState selection, BaseFormationTable formations) {
		_source = source;
		_scene = scene;
		_terrain = scene.World.Terrain;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_formations = formations;
	}

	/// <summary>Why no structure can be added, or null when one can: block 9 is full in some version of the mission.</summary>
	public string? WhyNoAdd => RosterLimits.WhyNoRoom(_source.Layout, BaseRow, 1);

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
			short heading = ZeroHeading(source);
			source.Append(BaseRow, Structure(typeIndex), out structure);
			source.Append(GroupRow, Group([structure], point, heading, side, Formation, paintsGround: false), out _);
		}), () => structure);
	}

	/// <summary>
	/// Why <paramref name="template"/> cannot be added, or null when it can: block 9 has no room for all its members in some
	/// version of the mission.
	/// </summary>
	public string? WhyNoBase(BaseTemplate template) => RosterLimits.WhyNoRoom(_source.Layout, BaseRow, template.Types.Count);

	/// <summary>
	/// Adds <paramref name="template"/> on <paramref name="side"/> as one structure group, its point on the ground at
	/// (<paramref name="x"/>, <paramref name="y"/>), and selects the group. Answers why it could not, or null when it did.
	///
	/// <para>The group is in the template's formation and lays its pad, so its point only picks the ground tile, the
	/// formation's layout fixing where in it the group stands (docs/retail/formats/script-dat.md#base-formation-terrain); it
	/// faces the shared heading of 0 degrees, since the pad is painted on a tile aligned with the map while the members'
	/// formation spots turn with the heading. Each member slot from 0 holds a new record of the template's type there, naming
	/// no point or heading of its own, so it stands on its formation spot turned by its slot
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 7). Everything else is as
	/// <see cref="Add"/> writes it for a lone structure.</para>
	/// </summary>
	public string? AddBase(BaseTemplate template, MissionSide side, int x, int y) {
		if (WhyNoBase(template) is { } refused) {
			return refused;
		}

		var source = _source;
		var position = new Vec3i(x, y, _terrain.HeightAtWorld(x, y));
		var structures = new short[template.Types.Count];
		short group = -1;
		return Commit(() => source.Change(() => {
			source.Append(PointRow, Point(position), out short point);
			short heading = ZeroHeading(source);
			for (int slot = 0; slot < structures.Length; slot++) {
				source.Append(BaseRow, Structure(template.Types[slot]), out structures[slot]);
			}

			source.Append(GroupRow, Group(structures, point, heading, side, (short)template.FormationId, paintsGround: true), out group);
		}), () => structures.Select((guid, slot) => (guid, template.Types[slot])).ToArray(), () => new GroupSelection(group));
	}

	/// <summary>
	/// Where each member of <paramref name="template"/> would stand, by member slot, were it added at (<paramref name="x"/>,
	/// <paramref name="y"/>): the group's point snapped into its ground tile by the formation's pad layout, then each slot
	/// placed from it at heading 0, through the rules the sync places <see cref="AddBase"/>'s records by
	/// (<see cref="MissionPlacementRules.GroupSetup"/>, <see cref="MissionPlacementRules.PlaceStructure"/>).
	/// </summary>
	public IReadOnlyList<MissionMemberPlace> BasePlaces(BaseTemplate template, int x, int y) {
		var setup = MissionPlacementRules.GroupSetup(MissionUnitKind.Base, new Vec3i(x, y, _terrain.HeightAtWorld(x, y)),
			MissionPlacementRules.HeadingFromDegrees(0), [], template.FormationId, paintsGround: true, _formations);
		return template.Types
			.Select((_, slot) => MissionPlacementRules.PlaceStructure(setup, _formations, slot, ownPoint: null, ownHeading: null))
			.ToArray();
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

	/// <summary>Why the last slot join run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// Why nothing can be put in member slot <paramref name="slot"/> of <paramref name="group"/>, by its row-16 GUID, or null
	/// when something can: what refuses any add to the group (<see cref="WhyNotJoin"/>, but for a free slot elsewhere), and a
	/// slot some record of the group fills.
	/// </summary>
	public string? WhyNotFillSlot(short group, int slot) =>
		WhyNoAdd is { } full ? full
			: _index.Group(group) is not { } planned ? EditorToolbox.GroupGone
			: planned.IsSquad || planned.Kind != MissionUnitKind.Base ? "It is not a group of structures."
			: slot is < 0 or >= MissionPlacementRules.GroupMemberSlots ? "A group holds 20 members."
			: !_source.MemberSlotFree(group, slot) ? "Another version of the mission has a member in this place."
			: null;

	/// <summary>
	/// Why a structure or ground vehicle of <c>BASES.DAT</c> type <paramref name="typeIndex"/> cannot be put in member slot
	/// <paramref name="slot"/> of <paramref name="group"/>, or null when it can: <see cref="WhyNotFillSlot"/>, and a ground
	/// vehicle past a convoy's first <see cref="VehicleGroups.LastJoinSlot"/> + 1 slots.
	/// </summary>
	public string? WhyNotJoinSlot(short group, int slot, int typeIndex) =>
		WhyNotFillSlot(group, slot) is { } reason ? reason
			: slot > VehicleGroups.LastJoinSlot && BaseObject.IsGroundVehicle(typeIndex) && VehicleGroups.IsConvoy(_source, group, BaseObject.IsGroundVehicle)
				? $"A convoy takes ground vehicles in its first {VehicleGroups.LastJoinSlot + 1} places, the ones that can lead it."
			: null;

	/// <summary>
	/// Asks for a structure or ground vehicle of type <paramref name="typeIndex"/> to be added in member slot
	/// <paramref name="slot"/> of structure group <paramref name="group"/>, with no point of its own.
	/// </summary>
	public void RequestJoin(short group, int slot, int typeIndex) => _requested = () => JoinSlot(typeIndex, group, slot);

	/// <summary>Makes the slot join asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Adds a record in member slot <slot> of an existing structure group, naming no point or heading of its own, so it stands
	// on the slot's formation spot and faces the group's heading turned by the slot
	// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 7).
	private string? JoinSlot(int typeIndex, short groupGuid, int slot) {
		if (WhyNotJoinSlot(groupGuid, slot, typeIndex) is { } reason) {
			return reason;
		}

		var source = _source;
		short structure = -1;
		return Commit(typeIndex, () => source.Change(() => {
			source.Append(BaseRow, Structure(typeIndex), out structure);
			source.SetField(MsnField.GroupMember(slot), groupGuid, structure);
		}), () => structure, select: false);
	}

	// Makes an add of one structure of <typeIndex>, its row-14 GUID <structure>, selecting it when <select>.
	private string? Commit(int typeIndex, Func<MsnEdit> add, Func<short> structure, bool select = true) =>
		Commit(add, () => [(structure(), typeIndex)], select ? () => new ObjectSelection(BaseRow, structure()) : null);

	// Makes an add's change and syncs the scene with it, then records it as one step and selects what <selected> answers,
	// when given; or, when no object was built for one of the <structures> it wrote (row-14 GUID and type), puts the
	// records back and says which type.
	private string? Commit(Func<MsnEdit> add, Func<IReadOnlyList<(short Guid, int TypeIndex)>> structures,
			Func<EditorSelection>? selected) {
		MsnEdit change;
		try {
			change = _sync.Run(add);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		foreach (var (added, typeIndex) in structures()) {
			if (_index.ObjectOf(new ObjectKey(BaseRow, added)) == null) {
				_sync.Undo(change);
				return $"The install has no structure type {typeIndex}.";
			}
		}

		_mover.Record(new RecordsEdit(_sync, change));
		if (selected != null) {
			_selection.Current = selected();
		}

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
		new() { ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, X = position.X, Y = position.Y, Z = position.Z };

	// The record a new structure gets: no point or heading of its own, pristine, no actions, every counter pair unused.
	private static BaseRosterEntry62 Structure(int typeIndex) => new() {
		ConditionRef = -1,
		RandomTableKey = -1,
		Unk06 = -1,
		TypeIndex = (short)typeIndex,
		PositionRef = -1,
		HeadingRef = -1,
		OutOfActionReport = Unset(20),
		EngagementActionRef = -1,
		DefeatActionRef = -1,
		StartingCondition = MissionPlacement.PristineCondition,
	};

	// The shared row-7 heading of 0 degrees, appended when no such heading can be shared.
	private static short ZeroHeading(MissionDocument source) {
		short heading = source.ShareableHeading(0);
		if (heading == -1) {
			source.Append(HeadingRow, new Heading10 { ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, Degrees = 0 }, out heading);
		}

		return heading;
	}

	// The group new structures get: them in slots 0 up, in <formation>, on the given point and heading, laying its pad when
	// <paintsGround>, with nothing else set.
	private static MissionGroup164 Group(IReadOnlyList<short> structures, short point, short heading, MissionSide side,
			short formation, bool paintsGround) {
		var members = Unset(MissionPlacementRules.GroupMemberSlots);
		for (int slot = 0; slot < structures.Count; slot++) {
			members[slot] = structures[slot];
		}

		return new MissionGroup164 {
			ConditionRef = -1,
			CompoundConditionPartner = -1,
			PaintsGround = (short)(paintsGround ? 1 : 0),
			MemberKind = BaseRoster,
			FormationId = formation,
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
