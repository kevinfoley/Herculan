using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Keeps the scene in step with the <c>.MSN</c> after every edit, undo and redo, a move and a side change included
/// (docs/herculan/editor-design.md#the-layout): places every group and roster record again from
/// the document's layout (<see cref="LayoutPlacement"/>), stands each object already built for a record where it now
/// stands, builds one for a record that has none of its type and fit (<see cref="MissionScene.CreateObject"/>), and takes
/// the object of a record no group places off the scene, keeping it, so an undone delete stands the same object, model and
/// all, where it stood. Terrain and models are not loaded again, nothing is written to disk, no mission is loaded and no
/// <c>script.dat</c> is generated.
///
/// <para>It then builds again the <see cref="MissionIndex"/>, the pick spheres, the overlays' lines and the mission check
/// (<see cref="MissionLint"/>), and tells the drawing what left and what joined the scene.</para>
///
/// <para>A selection names a <c>.MSN</c> record, so it needs no mapping: it is kept while the <c>.MSN</c> has its record
/// (<see cref="EditorSelection.InFile"/>), an object while the scene has an object for its record, a route's waypoint while
/// the route still has one there, and is cleared otherwise.</para>
///
/// <para>A drag's preview (<see cref="Preview"/>) places the same records again with the drag's overrides, standing each
/// object where they put it, and builds nothing: only the commit writes the <c>.MSN</c>.</para>
/// </summary>
internal sealed class SceneSync {
	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly SimWorld _world;
	private readonly PlacementTables _tables;
	private readonly MissionIndex _index;
	private readonly MissionOverlay _overlay;
	private readonly ScenePicker _picker;
	private readonly MissionOutliner _outliner;
	private readonly SelectionState _selection;

	// The terrain as it now stands, which replacing the mission's terrain swaps (TerrainChanger).
	private HeightGrid _terrain => _world.Terrain;

	// Every object built for the mission, by the record it stands for, the latest first.
	private readonly Dictionary<ObjectKey, List<SceneObject>> _built = new();

	public SceneSync(MissionDocument source, MissionScene scene, PlacementTables tables, MissionIndex index,
			MissionOverlay overlay, ScenePicker picker, MissionOutliner outliner, SelectionState selection) {
		_source = source;
		_scene = scene;
		_world = scene.World;
		_tables = tables;
		_index = index;
		_overlay = overlay;
		_picker = picker;
		_outliner = outliner;
		_selection = selection;
		foreach (var placed in index.Objects) {
			_built[index.KeyOf(placed)!.Value] = [placed];
		}
	}

	/// <summary>Raised with each object a sync puts in the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Added;

	/// <summary>Raised with each object a sync takes out of the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Removed;

	/// <summary>Raised once a sync is done, for whatever reads the indices.</summary>
	public event Action? Rebuilt;

	/// <summary>Raised with each object whose simulation object has moved, for whatever draws it.</summary>
	public event Action<SceneObject>? Moved;

	/// <summary>Every object built for the mission, in the scene or kept for an undo or redo to put back.</summary>
	public IEnumerable<SceneObject> Built => _built.Values.SelectMany(objects => objects);

	/// <summary>
	/// The objects for the records <paramref name="plan"/> places, each built for its record (<see cref="MissionScene.CreateObject"/>)
	/// at its placement, not yet on the ground; a record of a type the install has nothing for gets none.
	/// </summary>
	public static Dictionary<ObjectKey, SceneObject> Create(MissionScene scene, MissionDocument source, LayoutPlan plan) {
		var objects = new Dictionary<ObjectKey, SceneObject>();
		foreach (var planned in plan.Objects) {
			if (scene.CreateObject(PlacementOf(planned)) is { } placed) {
				objects[planned.Key] = placed;
			}
		}

		return objects;
	}

	/// <summary>
	/// What a planned record's object is built and placed from: its type, fit, condition, place, side and formation slot,
	/// from the layout. The editor binds no action and ticks nothing, so its roster slot and group index, which only the
	/// game's load binds by, are -1, and the actions and writes that load binds are left unset.
	/// </summary>
	public static MissionPlacement PlacementOf(PlannedObject planned) {
		var place = planned.Place;
		return new MissionPlacement(planned.Kind, planned.TypeIndex, planned.TypeName,
			SlotIndex: -1, GroupIndex: -1,
			place.Position, place.Heading, planned.WeaponRefs, planned.WeaponSecondary,
			IsPlayerSquad: planned.Squad,
			Side: planned.Side,
			AiCruiseSpeed: planned.AiCruiseSpeed,
			AiRadarActive: planned.AiRadarActive,
			FormationOffset: place.FormationOffset,
			FlyerFormationOffset: place.FlyerFormationOffset,
			StartingCondition: planned.StartingCondition,
			GroundPoint: place.GroundPoint);
	}

	/// <summary>
	/// Makes <paramref name="edit"/>, which writes the <c>.MSN</c> and builds its layout again, then syncs the scene with it. An
	/// edit that throws has put the records back, and nothing is synced.
	/// </summary>
	public T Run<T>(Func<T> edit) {
		var result = edit();
		Sync();
		return result;
	}

	/// <summary>Puts back the rows <paramref name="change"/> changed as it found them, and syncs.</summary>
	public void Undo(MsnEdit change) => Run(() => {
		_source.Undo(change);
		return change;
	});

	/// <summary>Puts back the rows <paramref name="change"/> changed as it left them, and syncs.</summary>
	public void Redo(MsnEdit change) => Run(() => {
		_source.Redo(change);
		return change;
	});

	/// <summary>Builds the scene's objects and the indices again from the document as it now stands.</summary>
	public void Sync() {
		var plan = LayoutPlacement.Place(_source.Layout, _tables);
		var before = _index.Objects.ToHashSet(ReferenceEqualityComparer.Instance);
		var objects = new Dictionary<ObjectKey, SceneObject>();
		var placedNow = new HashSet<object>(ReferenceEqualityComparer.Instance);
		foreach (var planned in plan.Objects) {
			var placement = PlacementOf(planned);
			var placed = Reuse(planned.Key, placement, placedNow) ?? Build(planned.Key, placement);
			if (placed == null) {
				continue;
			}

			placedNow.Add(placed);
			objects[planned.Key] = placed;
			placed.Placement = placement;
			placed.Object.Side = placement.Side;
			Stand(placed);
		}

		foreach (var gone in _index.Objects.Where(placed => !placedNow.Contains(placed))) {
			Removed?.Invoke(gone);
		}

		_index.Reload(plan, objects);
		_picker.Rebuild(_index.Objects);
		_overlay.Rebuild();
		_outliner.Recheck();
		foreach (var joined in _index.Objects.Where(placed => !before.Contains(placed))) {
			Added?.Invoke(joined);
		}

		Reselect();
		Rebuilt?.Invoke();
	}

	/// <summary>
	/// Places every record again with <paramref name="overrides"/>, a drag's, read in place of the layout's, or as the layout
	/// stands for null, standing each object where it then stands and taking the groups' setups and pads and the routes and
	/// areas into the index and the overlay's lines: a drag's preview, which writes nothing.
	/// </summary>
	public void Preview(PlacementOverrides? overrides) {
		var plan = LayoutPlacement.Place(_source.Layout, _tables, overrides);
		foreach (var planned in plan.Objects) {
			if (_index.ObjectOf(planned.Key) is not { } placed) {
				continue;
			}

			var place = planned.Place;
			placed.Placement = placed.Placement with {
				Position = place.Position, Heading = place.Heading, FormationOffset = place.FormationOffset,
				FlyerFormationOffset = place.FlyerFormationOffset, GroundPoint = place.GroundPoint
			};
			Stand(placed);
		}

		_index.Replan(plan);
		_overlay.Replan();
	}

	/// <summary>Stands <paramref name="sceneObject"/> at a position and heading, for whatever draws and picks it.</summary>
	public void Place(SceneObject sceneObject, Vec3i position, int heading) {
		sceneObject.Object.Position = position;
		sceneObject.Object.Face(heading);
		_picker.Move(sceneObject);
		Moved?.Invoke(sceneObject);
	}

	// Stands an object where its placement puts it, as the spawn pass does and the editor draws it (GroundStanding.Settled);
	// touched only when that moved it.
	private void Stand(SceneObject placed) {
		var placement = placed.Placement;
		var position = GroundStanding.Settled(_terrain, placed.Object, MissionScene.SpawnPosition(_terrain, placed.Object, placement));
		if (position != placed.Object.Position || placement.Heading != placed.Object.Heading) {
			Place(placed, position, placement.Heading);
		}
	}

	// The object built for <key>, of the placement's type, weapon fit and starting condition, and not yet placed by this sync.
	// A type that differs is a chassis changed, or a random table set, a fit that differs a fit changed (MachineFitter), and
	// a condition that differs one set (StartingConditionEditor); each needs an object of its own, since a mech's loadout and
	// a structure's damage are built with it.
	private SceneObject? Reuse(ObjectKey key, MissionPlacement placement, HashSet<object> placedNow) {
		if (!_built.TryGetValue(key, out var objects)) {
			return null;
		}

		var found = objects.Find(built => !placedNow.Contains(built) && built.Placement.Kind == placement.Kind
			&& built.Placement.TypeIndex == placement.TypeIndex && built.Placement.TypeName == placement.TypeName
			&& built.Placement.WeaponRefs.SequenceEqual(placement.WeaponRefs)
			&& built.Placement.WeaponSecondary.SequenceEqual(placement.WeaponSecondary)
			&& built.Placement.StartingCondition == placement.StartingCondition);
		if (found != null) {
			objects.Remove(found);
			objects.Insert(0, found);
		}

		return found;
	}

	// A new object for a record no built object stands for, filed under it; null for a type the install has nothing for,
	// which the open leaves out too.
	private SceneObject? Build(ObjectKey key, MissionPlacement placement) {
		if (_scene.CreateObject(placement) is not { } placed) {
			return null;
		}

		if (!_built.TryGetValue(key, out var objects)) {
			_built[key] = objects = new List<SceneObject>();
		}

		objects.Insert(0, placed);
		return placed;
	}

	// Keeps the selection while the .MSN has the record it names, an object while the scene has one for its record, and a
	// route's waypoint while the route still has one there; or clears it.
	private void Reselect() {
		_selection.Current = _selection.Current switch {
			ObjectSelection selected => _index.ObjectOf(selected) != null ? selected : null,
			RouteSelection { Waypoint: >= 0 } route when EditorSelection.InFile(_source.Mission, route) =>
				route.Waypoint < _index.Route(route.Guid)?.Points.Count ? route : route with { Waypoint = -1 },
			{ } selected when EditorSelection.InFile(_source.Mission, selected) => selected,
			_ => null
		};
	}
}

/// <summary>
/// One step of <see cref="PointMover"/>'s undo history that the scene is synced again after: a move
/// (<see cref="PointMover"/>), a side change (<see cref="SideChanger"/>), an add (<see cref="StructurePlacer"/>,
/// <see cref="HercPlacer"/>, <see cref="FlyerPlacer"/>, <see cref="VehiclePlacer"/>), a delete (<see cref="ObjectDeleter"/>), a
/// fit or type change (<see cref="MachineFitter"/>, <see cref="RosterTypeEditor"/>), a starting condition or heading set
/// (<see cref="StartingConditionEditor"/>, <see cref="HeadingEditor"/>), a squad edit (<see cref="SquadEditor"/>), a
/// route edit (<see cref="RouteEditor"/>), a trigger-area edit (<see cref="AreaEditor"/>), an order edit (<see cref="OrderEditor"/>),
/// an action or wiring edit (<see cref="ActionEditor"/>), a timer edit (<see cref="TimerEditor"/>), a condition edit
/// (<see cref="ConditionEditor"/>), a Present when set (<see cref="PresenceEditor"/>), a random table edit
/// (<see cref="RandomTableEditor"/>), an objective edit (<see cref="ObjectiveEditor"/>) or a text edit (<see cref="MissionTextEditor"/>):
/// the rows and text it changed, put back on undo and redo through <see cref="SceneSync"/>.
/// </summary>
internal sealed record RecordsEdit(SceneSync Sync, MsnEdit Change) : IEditorEdit {
	public void Undo() => Sync.Undo(Change);

	public void Redo() => Sync.Redo(Change);
}
