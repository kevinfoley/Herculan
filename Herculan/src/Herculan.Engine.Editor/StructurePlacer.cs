using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a structure to the open mission: a block-9 roster record, and a block-11 group of its own that places it,
/// standing on a block-1 point appended for it and facing a block-2 heading of 0 degrees, as one step of
/// <see cref="PointMover"/>'s undo history.
///
/// <para>The records take the shape the retail missions give a lone structure most often — 148 of their 537 structure
/// groups (<c>HercWorks.Query orders</c>): one member, in formation 0, standing on the group's own point and facing its
/// own heading, with no orders, no deployment action and no out-of-action writes. The roster record names neither a
/// point nor a heading of its own, so the structure stands on the group's point as the group's slot 0, with the group's
/// heading (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 7); it starts pristine, fires no
/// action and writes no counter, every unused pair <c>-1</c> as in the retail files. A group naming a heading needs no
/// slot-0 order (<see cref="ScriptDatLint"/>). The group does not paint its ground. Which side it is on is the
/// caller's.</para>
///
/// <para>Block 9 is capped at DBSIM's structure slots (<see cref="ScriptDatLint.BaseSlotCap"/>), past which its load
/// writes over the globals that follow (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps):
/// an add that would pass it is refused. A heading of 0 degrees already in block 2 is shared rather than appended.</para>
/// </summary>
internal sealed class StructurePlacer {
	// A structure group's roster and formation, as block 11 numbers them.
	private const short BaseRoster = 2;
	private const short Formation = 0;

	private readonly ScriptDat _document;
	private readonly MissionScene _scene;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly ScenePicker _picker;
	private readonly PointMover _mover;
	private readonly SelectionState _selection;

	public StructurePlacer(ScriptDat document, MissionScene scene, MissionIndex index, ScenePicker picker, PointMover mover,
			SelectionState selection) {
		_document = document;
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

	/// <summary>
	/// Adds a structure of <c>BASES.DAT</c> type <paramref name="typeIndex"/> on <paramref name="side"/>, standing on
	/// the ground at (<paramref name="x"/>, <paramref name="y"/>), and selects it. Answers why it could not, or null
	/// when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y) {
		if (_document.Bases.Length >= ScriptDatLint.BaseSlotCap) {
			return $"The mission already has {_document.Bases.Length} structures, as many as DBSIM holds.";
		}

		if (_index.GroupMembers.Count != _document.Groups.Length || _document.Coordinates.Length >= short.MaxValue
				|| _document.Headings.Length >= short.MaxValue || _document.Groups.Length >= short.MaxValue) {
			return "The mission's records are not as the editor loaded them; nothing was added.";
		}

		short slot = (short)_document.Bases.Length;
		short group = (short)_document.Groups.Length;
		var position = new Vec3i(x, y, _terrain.HeightAtWorld(x, y));
		var placement = new MissionPlacement(MissionUnitKind.Base, typeIndex, null, slot, group, position, 0,
			Array.Empty<short>(), Array.Empty<short>(), Side: side,
			OutOfActionReport: new OutOfActionReport(Unset(10), Unset(10)));
		if (_scene.CreateObject(placement) is not { } placed) {
			return $"The install has no structure type {typeIndex}.";
		}

		var before = Blocks.Of(_document);

		short point = (short)_document.Coordinates.Length;
		_document.Coordinates = [.. _document.Coordinates, new ScriptCoordinate { X = x, Y = y, Z = position.Z }];

		short heading = (short)Array.FindIndex(_document.Headings, existing => existing.Degrees == 0);
		if (heading < 0) {
			heading = (short)_document.Headings.Length;
			_document.Headings = [.. _document.Headings, new ScriptHeading { Degrees = 0 }];
		}

		_document.Bases = [.. _document.Bases, Structure(typeIndex)];
		_document.Groups = [.. _document.Groups, Group(slot, point, heading, side)];

		var setup = new MissionGroupSetup(position, position, MissionGroupPointSource.OwnPoint, 0, false, Formation, false);
		var edit = new AddEdit(this, placed, group, setup, before, Blocks.Of(_document));
		Show(edit);
		_mover.Record(edit);
		_selection.Current = new ObjectSelection(placed);
		return null;
	}

	// The record a new structure gets: no point or heading of its own, pristine, no actions, every counter pair unused.
	private static ScriptBaseRecord Structure(int typeIndex) {
		var record = new ScriptBaseRecord { TypeIndex = (short)typeIndex, PositionRef = -1, HeadingRef = -1 };
		for (int offset = 0; offset + 1 < record.TailBytes.Length; offset += 2) {
			BitConverter.GetBytes((short)-1).CopyTo(record.TailBytes, offset);
		}

		record.StartingCondition = MissionPlacement.PristineCondition;
		return record;
	}

	// The group a new structure gets: it alone, in slot 0, on the given point and heading, with nothing else set.
	private static ScriptGroup Group(short slot, short point, short heading, MissionSide side) {
		var members = Unset(20);
		members[0] = slot;
		return new ScriptGroup {
			MemberKind = BaseRoster,
			FormationId = Formation,
			PositionRef = point,
			HeadingRef = heading,
			RouteRef = -1,
			MemberRefs = members,
			OrderRefs = Unset(10),
			Side = (short)side,
			DeploymentActionRef = -1,
			CounterRefs = Unset(10),
			CounterOps = Unset(10),
			MapShown = -1
		};
	}

	private static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();

	// Puts an added structure and its group in the index, the picker and the scene's drawing.
	private void Show(AddEdit edit) {
		_index.AddGroup(edit.Group, MissionUnitKind.Base, edit.Placed.Placement.Side, edit.Setup);
		_index.AddObject(edit.Placed);
		_picker.Add(edit.Placed);
		Added?.Invoke(edit.Placed);
	}

	// Takes them out again, and the selection off them.
	private void Hide(AddEdit edit) {
		if (_selection.Current is ObjectSelection { Object: var selected } && ReferenceEquals(selected, edit.Placed)
				|| _selection.Current is GroupSelection { Group: var group } && group == edit.Group) {
			_selection.Current = null;
		}

		Removed?.Invoke(edit.Placed);
		_picker.Remove(edit.Placed);
		_index.RemoveObject(edit.Placed);
		_index.RemoveLastAddedGroup();
	}

	// The blocks an add grows, as they stood on one side of it. Block 1's points are copied, since a move rewrites a
	// point in place; the other three are only ever replaced whole or appended to.
	private sealed record Blocks(ScriptCoordinate[] Coordinates, ScriptHeading[] Headings, ScriptBaseRecord[] Bases,
			ScriptGroup[] Groups) {
		public static Blocks Of(ScriptDat document) =>
			new(Copy(document.Coordinates), document.Headings, document.Bases, document.Groups);

		public void WriteTo(ScriptDat document) {
			document.Coordinates = Copy(Coordinates);
			document.Headings = Headings;
			document.Bases = Bases;
			document.Groups = Groups;
		}

		private static ScriptCoordinate[] Copy(ScriptCoordinate[] points) =>
			Array.ConvertAll(points, point => new ScriptCoordinate { X = point.X, Y = point.Y, Z = point.Z });
	}

	// One add, undone and redone as a whole. The undo history is last in, first out, so everything made after the add
	// has been undone before the add is, and the blocks are as the add left them.
	private sealed record AddEdit(StructurePlacer Placer, SceneObject Placed, short Group, MissionGroupSetup Setup,
			Blocks Before, Blocks After) : IEditorEdit {
		public void Undo() {
			Placer.Hide(this);
			Before.WriteTo(Placer._document);
		}

		public void Redo() {
			After.WriteTo(Placer._document);
			Placer.Show(this);
		}
	}
}
