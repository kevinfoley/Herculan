using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a ground vehicle to the open mission, as one step of <see cref="PointMover"/>'s undo history: through
/// <see cref="VehicleGroups"/>'s record edits in a convoy of its own, standing on a route of one waypoint where it was put and
/// travelling it, or joining the selected convoy; or, given any other structure group, as a structure joins one
/// (<see cref="StructurePlacer"/>), where it stands as a building unless it is the group's first member
/// (docs/retail/simulation/ground-vehicles.md#the-ground-vehicle-tick--0046a5d0). The scene is then synced with the document
/// (<see cref="SceneSync"/>), which builds the vehicle's object for the record, and the vehicle
/// is selected (docs/herculan/handoff-editor-roster.md#ground-vehicles). A convoy is a structure
/// group whose first member is of a type <see cref="BaseObject.IsGroundVehicle"/> answers true for.
/// </summary>
internal sealed class VehiclePlacer {
	private const int BaseRow = 14;

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly StructurePlacer _structures;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="structures">Adds a vehicle to a structure group that is not a convoy, and builds its ghost.</param>
	public VehiclePlacer(MissionDocument source, MissionIndex index, PointMover mover,
			SceneSync sync, SelectionState selection, StructurePlacer structures) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_structures = structures;
	}

	/// <summary>
	/// Why no ground vehicle can be added, or null when one can: block 9 is full
	/// (<see cref="VehicleGroups.WhyNoAdd"/>).
	/// </summary>
	public string? WhyNoAdd => VehicleGroups.WhyNoAdd(_source);

	/// <summary>
	/// Adds a ground vehicle of <c>BASES.DAT</c> type <paramref name="typeIndex"/> on <paramref name="side"/> at
	/// (<paramref name="x"/>, <paramref name="y"/>), in a convoy of its own or, given <paramref name="joinGroup"/>, as a
	/// member of that structure group; and selects it. Answers why it could not, or null when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y, short? joinGroup = null) {
		if (WhyNoAdd is { } refused) {
			return refused;
		}

		if (!BaseObject.IsGroundVehicle(typeIndex)) {
			return $"Structure type {typeIndex} is not a ground vehicle.";
		}

		var source = _source;
		var vehicle = VehicleGroups.Machine((short)typeIndex);
		short added = -1;
		if (joinGroup is { } joined) {
			if (!IsConvoy(joined)) {
				return _structures.Add(typeIndex, side, x, y, joined);
			}

			if (WhyNotJoin(joined) is { } reason) {
				return reason;
			}

			return Commit(typeIndex, () => VehicleGroups.Join(source, joined, vehicle, BaseObject.IsGroundVehicle, out added),
				() => added);
		}

		return Commit(typeIndex, () => VehicleGroups.Create(source, vehicle, (short)side, x, y, out added, out _), () => added);
	}

	/// <summary>
	/// Why a ground vehicle cannot be added to group <paramref name="group"/>, by its row-16 GUID, or null when it can: a
	/// convoy's limit (<see cref="VehicleGroups.WhyNoJoin"/>), or for any other group a structure's
	/// (<see cref="StructurePlacer.WhyNotJoin"/>).
	/// </summary>
	public string? WhyNotJoin(short group) =>
		IsConvoy(group) ? VehicleGroups.WhyNoJoin(_source, group, BaseObject.IsGroundVehicle) : _structures.WhyNotJoin(group);

	/// <summary>
	/// The vehicle <see cref="Add"/> would add of <paramref name="typeIndex"/> on <paramref name="side"/>, built as a
	/// structure's ghost is (<see cref="StructurePlacer.Ghost"/>), for <see cref="DropPreview"/> to draw.
	/// </summary>
	public SceneObject? Ghost(int typeIndex, MissionSide side) => _structures.Ghost(typeIndex, side);

	// Whether row-16 group <group> is a convoy.
	private bool IsConvoy(short group) =>
		_index.Group(group) != null && VehicleGroups.IsConvoy(_source, group, BaseObject.IsGroundVehicle);

	// Makes an add's change and syncs the scene with it, then records it as one step and selects the object built for
	// the vehicle's row-14 GUID; or, when none was built, puts the records back and says why.
	private string? Commit(int typeIndex, Func<MsnEdit> add, Func<short> vehicle) {
		MsnEdit change;
		try {
			change = _sync.Run(add);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		short added = vehicle();
		if (_index.ObjectOf(new ObjectKey(BaseRow, added)) == null) {
			_sync.Undo(change);
			return $"The install has no structure type {typeIndex}.";
		}

		_mover.Record(new RecordsEdit(_sync, change));
		_selection.Current = new ObjectSelection(BaseRow, added);
		return null;
	}
}
