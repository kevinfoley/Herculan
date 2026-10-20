using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a flyer to the open mission, as one step of <see cref="PointMover"/>'s undo history, through
/// <see cref="FlyerGroups"/>'s record edits: in a new group of its own, standing on a route of one waypoint where it was put
/// and hunting the player's squad, or joining the selected flyer group. The editor's state is then built again from the
/// generated file (<see cref="MissionRebuild"/>), which builds the flyer's object for the record, and the flyer is
/// selected (docs/herculan/handoff-editor-roster.md#flyers).
/// </summary>
internal sealed class FlyerPlacer {
	private const int FlyerRow = 13;
	private const int GroupBlock = 11;

	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private readonly UnitTypeNames _names;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s, which the scene was loaded from.</param>
	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="names">The <c>nam\FLYERS.NAM</c> type names, which a ghost's model is found by.</param>
	public FlyerPlacer(ScriptDat document, MissionDocument source, MissionScene scene, MissionIndex index, PointMover mover,
			MissionRebuild rebuild, SelectionState selection, UnitTypeNames names) {
		_document = document;
		_source = source;
		_scene = scene;
		_index = index;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
		_names = names;
	}

	/// <summary>
	/// Why no flyer can be added, or null when one can: block 8 is full
	/// (<see cref="FlyerGroups.WhyNoAdd"/>).
	/// </summary>
	public string? WhyNoAdd => FlyerGroups.WhyNoAdd(_source);

	/// <summary>
	/// Adds a flyer of <c>FLYERS.NAM</c> type <paramref name="typeIndex"/> on <paramref name="side"/> above
	/// (<paramref name="x"/>, <paramref name="y"/>), in a group of its own or, given <paramref name="joinGroup"/>, as a
	/// member of that flyer group; and selects it. Answers why it could not, or null when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y, int? joinGroup = null) {
		if (WhyNoAdd is { } refused) {
			return refused;
		}

		if (_names[typeIndex] == null) {
			return $"There is no flyer type {typeIndex}.";
		}

		var source = _source;
		var flyer = FlyerGroups.Machine((short)typeIndex);
		short added = -1;
		if (joinGroup is { } joined) {
			if (WhyNotJoin(joined) is { } reason) {
				return reason;
			}

			short group = source.GuidOf(GroupBlock, joined);
			return Commit(typeIndex, () => FlyerGroups.Join(source, group, flyer, out added), () => added);
		}

		return Commit(typeIndex, () => FlyerGroups.Create(source, flyer, (short)side, x, y, out added, out _), () => added);
	}

	/// <summary>Why a flyer cannot be added to group <paramref name="group"/>, by block-11 index, or null when it can.</summary>
	public string? WhyNotJoin(int group) =>
		group < 0 || group >= _document.Groups.Length || _source.GuidOf(GroupBlock, group) == -1 ? $"Group {group} is not in the file."
			: FlyerGroups.WhyNoJoin(_source, _source.GuidOf(GroupBlock, group));

	/// <summary>
	/// The flyer <see cref="Add"/> would add of <paramref name="typeIndex"/> on <paramref name="side"/>, built the same way
	/// but in no roster slot or group and recorded nowhere, for <see cref="DropPreview"/> to draw; null for a type the
	/// install has nothing for.
	/// </summary>
	public SceneObject? Ghost(int typeIndex, MissionSide side) =>
		_names[typeIndex] is { } name
			? _scene.CreateObject(new MissionPlacement(MissionUnitKind.Flyer, typeIndex, name, -1, -1, Vec3i.Zero, 0, [], [],
				Side: side, OutOfActionReport: OutOfActionReport.None))
			: null;

	// Makes an add's change and rebuilds from it, then records it as one step and selects the object the rebuild built
	// for the flyer's row-13 GUID; or, when it built none, puts the records back and says why.
	private string? Commit(int typeIndex, Func<MsnEdit> add, Func<short> flyer) {
		var source = _source;
		MsnEdit change;
		try {
			change = _rebuild.Run(add);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		int? slot = source.ExportedIndex(FlyerRow, flyer());
		if (slot is not { } exported || _index.ObjectAt(MissionUnitKind.Flyer, exported) is not { } placed) {
			_rebuild.Undo(change);
			return slot == null
				? "The added flyer was not generated; nothing was added."
				: $"The install has no flyer type {typeIndex}.";
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		_selection.Current = new ObjectSelection(placed);
		return null;
	}
}
