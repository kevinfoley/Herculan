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
/// (<see cref="MissionDocument.Append"/>), and <c>script.dat</c> is generated again; a record with a new GUID is exported
/// last in its block, and the scene object takes the indices the generation gave it
/// (docs/herculan/handoff-editor-mission-editing.md#holding-the-msn-as-the-document, "The tools").
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
/// an add that would pass it in the generated file is refused.</para>
/// </summary>
internal sealed class StructurePlacer {
	// A structure group's roster and formation, as row 16 numbers them.
	private const short BaseRoster = 2;
	private const short Formation = 0;

	private const int PointRow = 6;
	private const int HeadingRow = 7;
	private const int BaseRow = 14;
	private const int GroupRow = 16;
	private const int GroupBlock = 11;

	private readonly ScriptDat _document;
	private readonly MissionDocument? _source;
	private readonly MissionScene _scene;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly ScenePicker _picker;
	private readonly PointMover _mover;
	private readonly SelectionState _selection;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s, which the scene was loaded from.</param>
	/// <param name="source">The mission as its <c>.MSN</c>, or null for a plain <c>script.dat</c>, to which nothing is added.</param>
	public StructurePlacer(ScriptDat document, MissionDocument? source, MissionScene scene, MissionIndex index,
			ScenePicker picker, PointMover mover, SelectionState selection) {
		_document = document;
		_source = source;
		_scene = scene;
		_terrain = scene.World.Terrain;
		_index = index;
		_picker = picker;
		_mover = mover;
		_selection = selection;
	}

	/// <summary>Raised with each object an add or a redo puts in the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Added;

	/// <summary>Raised with each object an undo takes out of the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Removed;

	/// <summary>Why nothing can be added to the mission at all, or null when something can.</summary>
	public string? WhyNoAdd => _mover.WhyReadOnly;

	/// <summary>
	/// Adds a structure of <c>BASES.DAT</c> type <paramref name="typeIndex"/> on <paramref name="side"/>, standing on
	/// the ground at (<paramref name="x"/>, <paramref name="y"/>), in a group of its own or, given
	/// <paramref name="joinGroup"/>, as a member of that structure group; and selects it. Answers why it could not, or
	/// null when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y, int? joinGroup = null) {
		if (WhyNoAdd is { } readOnly) {
			return readOnly;
		}

		if (_document.Bases.Length >= ScriptDatLint.BaseSlotCap) {
			return $"The mission already has {_document.Bases.Length} structures, as many as DBSIM holds.";
		}

		if (_index.GroupMembers.Count != _document.Groups.Length) {
			return "The mission's records are not as the editor loaded them; nothing was added.";
		}

		if (joinGroup is { } joined) {
			return Join(typeIndex, joined, x, y);
		}

		var source = _source!;
		var position = new Vec3i(x, y, _terrain.HeightAtWorld(x, y));
		short structure = -1, group = -1;
		MsnEdit change;
		try {
			change = source.Change(() => {
				source.Append(PointRow, Point(position), out short point);
				short heading = source.ShareableHeading(0);
				if (heading == -1) {
					source.Append(HeadingRow, new Heading10 { ConditionRef = -1, VariantKey = -1, Unk06 = -1, Degrees = 0 }, out heading);
				}

				source.Append(BaseRow, Structure(typeIndex), out structure);
				source.Append(GroupRow, Group(structure, point, heading, side), out group);
			});
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		if (source.ExportedIndex(BaseRow, structure) is not { } slot || source.ExportedIndex(GroupRow, group) is not { } groupIndex
				|| groupIndex != _index.GroupMembers.Count) {
			source.Undo(change);
			return "The added records were not generated as the last of their blocks; nothing was added.";
		}

		if (_scene.CreateObject(Placement(typeIndex, side, slot, groupIndex, position)) is not { } placed) {
			source.Undo(change);
			return $"The install has no structure type {typeIndex}.";
		}

		var setup = new MissionGroupSetup(position, position, MissionGroupPointSource.OwnPoint, 0, false, Formation, false);
		var edit = new AddEdit(this, placed, groupIndex, setup, change);
		Show(edit);
		_mover.Record(edit);
		_selection.Current = new ObjectSelection(placed);
		return null;
	}

	/// <summary>
	/// Why a structure cannot be added to <paramref name="group"/>, or null when it can: the group must be a structure
	/// group in the file, other than the player's squad, with a member slot no record of it fills in any state.
	/// </summary>
	public string? WhyNotJoin(int group) =>
		WhyNoAdd is { } readOnly ? readOnly
			: group <= 0 || group >= _document.Groups.Length || _index.KindOf(group) != MissionUnitKind.Base
				|| _source!.GuidOf(GroupBlock, group) == -1
				? $"Group {group} is not a structure group."
				: _source.FreeMemberSlot(_source.GuidOf(GroupBlock, group)) < 0
					? $"Group {group} has each of its 20 member slots filled by one of its records."
					: null;

	// Adds a structure to an existing structure group, in the first member slot no record of the group fills. DBSIM walks
	// all 20 member refs by their raw index (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 7), so a
	// slot free earlier in the list serves as well as one at its end. The record names a point of its own at (x, y), which
	// wins over the slot's formation offset from the group's point (rule 2), and no heading, so it faces the group's way;
	// its side is the group's.
	private string? Join(int typeIndex, int group, int x, int y) {
		if (WhyNotJoin(group) is { } reason) {
			return reason;
		}

		var source = _source!;
		short groupGuid = source.GuidOf(GroupBlock, group);
		int memberSlot = source.FreeMemberSlot(groupGuid);
		var side = _index.SideOf(group) ?? MissionSide.Human;
		int heading = _index.SetupOf(group)?.Heading ?? 0;
		var position = new Vec3i(x, y, _terrain.HeightAtWorld(x, y));
		short structure = -1;
		MsnEdit change;
		try {
			change = source.Change(() => {
				source.Append(PointRow, Point(position), out short point);
				var record = Structure(typeIndex);
				record.PositionRef = point;
				source.Append(BaseRow, record, out structure);
				source.SetField(MsnField.GroupMember(memberSlot), groupGuid, structure);
			});
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		if (source.ExportedIndex(BaseRow, structure) is not { } slot) {
			source.Undo(change);
			return "The added record was not generated; nothing was added.";
		}

		if (_scene.CreateObject(Placement(typeIndex, side, slot, group, position, heading)) is not { } placed) {
			source.Undo(change);
			return $"The install has no structure type {typeIndex}.";
		}

		var edit = new AddEdit(this, placed, group, null, change);
		Show(edit);
		_mover.Record(edit);
		_selection.Current = new ObjectSelection(placed);
		return null;
	}

	/// <summary>
	/// The structure <see cref="Add"/> would add of <paramref name="typeIndex"/> on <paramref name="side"/>, built the
	/// same way but in no roster slot or group and recorded nowhere, for <see cref="DropPreview"/> to draw; null for a
	/// type the install has nothing for.
	/// </summary>
	public SceneObject? Ghost(int typeIndex, MissionSide side) =>
		_scene.CreateObject(Placement(typeIndex, side, -1, -1, Vec3i.Zero));

	private static MissionPlacement Placement(int typeIndex, MissionSide side, int slot, int group, Vec3i position,
			int heading = 0) =>
		new(MissionUnitKind.Base, typeIndex, null, slot, group, position, heading, Array.Empty<short>(), Array.Empty<short>(),
			Side: side, OutOfActionReport: new OutOfActionReport(Unset(10), Unset(10)));

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

	// Puts an added structure, and its group when it has one of its own, in the index, the picker and the scene's
	// drawing.
	private void Show(AddEdit edit) {
		if (edit.Setup is { } setup) {
			_index.AddGroup(edit.Group, MissionUnitKind.Base, edit.Placed.Placement.Side, setup);
		}

		_index.AddObject(edit.Placed);
		_picker.Add(edit.Placed);
		Added?.Invoke(edit.Placed);
	}

	// Takes them out again, and the selection off them.
	private void Hide(AddEdit edit) {
		if (_selection.Current is ObjectSelection { Object: var selected } && ReferenceEquals(selected, edit.Placed)
				|| edit.Setup != null && _selection.Current is GroupSelection { Group: var group } && group == edit.Group) {
			_selection.Current = null;
		}

		Removed?.Invoke(edit.Placed);
		_picker.Remove(edit.Placed);
		_index.RemoveObject(edit.Placed);
		if (edit.Setup != null) {
			_index.RemoveLastAddedGroup();
		}
	}

	// One add, undone and redone as a whole: Setup is the group the add made, null when it joined Group instead. The undo
	// history is last in, first out, so everything made after the add has been undone before the add is, and the .MSN
	// rows are as the add left them.
	private sealed record AddEdit(StructurePlacer Placer, SceneObject Placed, int Group, MissionGroupSetup? Setup,
			MsnEdit Change) : IEditorEdit {
		public void Undo() {
			Placer.Hide(this);
			Placer._source!.Undo(Change);
		}

		public void Redo() {
			Placer._source!.Redo(Change);
			Placer.Show(this);
		}
	}
}
