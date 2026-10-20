using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Shell;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Keeps the scene in step with the <c>.MSN</c> after every edit, undo and redo, a move and a side change included
/// (docs/herculan/handoff-editor-document.md#rebuilding-after-an-edit): places every group and roster record again from
/// the document's layout (<see cref="LayoutPlacement"/>), stands each object already built for a record where it now
/// stands, builds one for a record that has none of its type and fit (<see cref="MissionScene.CreateObject"/>), and takes
/// the object of a record no group places off the scene, keeping it, so an undone delete stands the same object, model and
/// all, where it stood. Terrain and models are not loaded again.
///
/// <para>It then builds again what the views still read from the generation: the generated <see cref="World.Mission"/>
/// (<see cref="MissionLoader.Load(GameContent, ScriptDat, string)"/>, after writing the generated <c>mission.str</c> and, for a
/// mission loaded in training mode, the squad's <c>player.mec</c> beside the handoff, as the launch would for the
/// generation, <see cref="MissionSquad"/>) and <see cref="GeneratedIndex"/>, each object's <see cref="SceneObject.Placement"/>
/// carrying the generation's indices for its record's GUID (<see cref="PlacementOf"/>); then the new
/// <see cref="MissionIndex"/>, the pick spheres, the overlays' lines and the mission check, and tells the drawing what
/// left and what joined the scene.</para>
///
/// <para>A selection names a <c>.MSN</c> record, so it needs no mapping: it is kept while the <c>.MSN</c> has its record
/// (<see cref="EditorSelection.InFile"/>), an object while the scene has an object for its record, a route's waypoint while
/// the route still has one there, and is cleared otherwise.</para>
///
/// <para>A drag's preview (<see cref="Preview"/>) places the same records again with the drag's overrides, standing each
/// object where they put it, and builds nothing: only the commit writes the <c>.MSN</c>.</para>
/// </summary>
internal sealed class SceneSync {
	private readonly GameContent _content;
	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly HeightGrid _terrain;
	private readonly PlacementTables _tables;
	private readonly MissionIndex _index;
	private readonly GeneratedIndex _generated;
	private readonly MissionOverlay _overlay;
	private readonly ScenePicker _picker;
	private readonly MissionOutliner _outliner;
	private readonly SelectionState _selection;

	// Every object built for the mission, by the record it stands for, the latest first.
	private readonly Dictionary<ObjectKey, List<SceneObject>> _built = new();

	/// <param name="squad">Which Hercs the squad pilots and how its <c>player.mec</c> is built again.</param>
	public SceneSync(GameContent content, MissionDocument source, MissionScene scene, PlacementTables tables, MissionIndex index,
			GeneratedIndex generated, MissionOverlay overlay, ScenePicker picker, MissionOutliner outliner,
			SelectionState selection, MissionSquad squad) {
		_content = content;
		_source = source;
		_scene = scene;
		_terrain = scene.World.Terrain;
		_tables = tables;
		_index = index;
		_generated = generated;
		_overlay = overlay;
		_picker = picker;
		_outliner = outliner;
		_selection = selection;
		Squad = squad;
		foreach (var placed in index.Objects) {
			_built[index.KeyOf(placed)!.Value] = [placed];
		}
	}

	/// <summary>Which Hercs the squad pilots and how its <c>player.mec</c> is built again.</summary>
	public MissionSquad Squad { get; }

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
			if (scene.CreateObject(PlacementOf(planned, source)) is { } placed) {
				objects[planned.Key] = placed;
			}
		}

		return objects;
	}

	/// <summary>
	/// What a planned record's object is built and placed from: its type, fit, condition, place, side and formation slot,
	/// from the layout; and from the generation, for the views that still read it, the block-7, 8 or 9 slot its GUID was
	/// exported to, the block-11 index of its group's, its two actions' block-5 indices and its out-of-action writes, as the
	/// generated record holds them (-1 and none where the generation has no record of the GUID).
	/// </summary>
	public static MissionPlacement PlacementOf(PlannedObject planned, MissionDocument source) {
		var script = source.Script;
		int slot = source.ExportedIndex(planned.Key.Row, planned.Key.Guid) ?? -1;
		(short Engaged, short Defeated, OutOfActionReport Report)? generated = planned.Kind switch {
			MissionUnitKind.Mech when slot >= 0 && slot < script.Mechs.Length => Generated(script, script.Mechs[slot].EngagementActionRef,
				script.Mechs[slot].DefeatActionRef, script.Mechs[slot].CounterRefs, script.Mechs[slot].CounterOps),
			MissionUnitKind.Flyer when slot >= 0 && slot < script.Flyers.Length => Generated(script,
				script.Flyers[slot].EngagementActionRef, script.Flyers[slot].DefeatActionRef, script.Flyers[slot].CounterRefs,
				script.Flyers[slot].CounterOps),
			MissionUnitKind.Base when slot >= 0 && slot < script.Bases.Length => Generated(script, script.Bases[slot].EngagementActionRef,
				script.Bases[slot].DefeatActionRef, script.Bases[slot].CounterRefs, script.Bases[slot].CounterOps),
			_ => null
		};

		var place = planned.Place;
		return new MissionPlacement(planned.Kind, planned.TypeIndex, planned.TypeName, slot,
			source.ExportedIndex(16, planned.Group) ?? -1, place.Position, place.Heading, planned.WeaponRefs,
			planned.WeaponSecondary,
			IsPlayerLance: planned.Squad,
			Side: planned.Side,
			AiCruiseSpeed: planned.AiCruiseSpeed,
			AiRadarActive: planned.AiRadarActive,
			FormationOffset: place.FormationOffset,
			FlyerFormationOffset: place.FlyerFormationOffset,
			EngagementActionRef: planned.Squad ? -1 : generated?.Engaged ?? -1,
			DefeatActionRef: planned.Squad ? -1 : generated?.Defeated ?? -1,
			StartingCondition: planned.StartingCondition,
			OutOfActionReport: planned.Squad ? null : generated?.Report,
			GroundPoint: place.GroundPoint);
	}

	// A generated roster record's two actions, a ref outside block 5 read as -1 as the loader reads it, and its writes.
	private static (short, short, OutOfActionReport) Generated(ScriptDat script, short engaged, short defeated,
			IReadOnlyList<short> counterRefs, IReadOnlyList<short> counterOps) {
		short Action(short reference) => reference >= 0 && reference < script.Actions.Length ? reference : (short)-1;
		return (Action(engaged), Action(defeated), new OutOfActionReport(counterRefs, counterOps));
	}

	/// <summary>
	/// Makes <paramref name="edit"/>, which writes the <c>.MSN</c> and generates again, then syncs the scene with it. An edit
	/// that throws has put the records back, and nothing is synced.
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
		if (Squad.PlayerMec(_source.Generator) is { } squadFile) {
			File.WriteAllBytes(MissionLoader.PlayerPathFor(_scene.Mission.SourcePath), squadFile);
		}

		File.WriteAllBytes(MissionLoader.TextPathFor(_scene.Mission.SourcePath), _source.MissionText);
		var mission = MissionLoader.Load(_content, _source.Script, _scene.Mission.SourcePath);
		var plan = LayoutPlacement.Place(_source.Layout, _tables);
		var before = _index.Objects.ToHashSet(ReferenceEqualityComparer.Instance);
		var objects = new Dictionary<ObjectKey, SceneObject>();
		var placedNow = new HashSet<object>(ReferenceEqualityComparer.Instance);
		foreach (var planned in plan.Objects) {
			var placement = PlacementOf(planned, _source);
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
		_generated.Reload(mission);
		_picker.Rebuild(_index.Objects);
		_overlay.Rebuild();
		_outliner.Recheck(new MissionLint(_generated, _source.Script, _source.Provenance.Lines.Count));
		foreach (var joined in _index.Objects.Where(placed => !before.Contains(placed))) {
			Added?.Invoke(joined);
		}

		Reselect();
		Rebuilt?.Invoke();
	}

	/// <summary>
	/// Places every record again with <paramref name="overrides"/>, a drag's, read in place of the layout's, or as the layout
	/// stands for null, standing each object where it then stands and taking the groups' setups and pads into the index and
	/// the pads' outlines: a drag's preview, which writes nothing.
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
		_overlay.RebuildPads();
	}

	/// <summary>Stands <paramref name="sceneObject"/> at a position and heading, for whatever draws and picks it.</summary>
	public void Place(SceneObject sceneObject, Vec3i position, int heading) {
		sceneObject.Object.Position = position;
		sceneObject.Object.Heading = heading;
		_picker.Move(sceneObject);
		Moved?.Invoke(sceneObject);
	}

	// Stands an object where its placement puts it, as the spawn pass does and the editor draws it (PointFollowers.Settled);
	// touched only when that moved it.
	private void Stand(SceneObject placed) {
		var placement = placed.Placement;
		var position = PointFollowers.Settled(_terrain, placed.Object, MissionScene.SpawnPosition(_terrain, placed.Object, placement));
		if (position != placed.Object.Position || placement.Heading != placed.Object.Heading) {
			Place(placed, position, placement.Heading);
		}
	}

	// The object built for <key>, of the placement's type and weapon fit, and not yet placed by this sync. A type that
	// differs is a chassis changed, or a variant table set, and a fit that differs a fit changed (MachineFitter); either
	// needs an object of its own, since a mech's loadout is built with it.
	private SceneObject? Reuse(ObjectKey key, MissionPlacement placement, HashSet<object> placedNow) {
		if (!_built.TryGetValue(key, out var objects)) {
			return null;
		}

		var found = objects.Find(built => !placedNow.Contains(built) && built.Placement.Kind == placement.Kind
			&& built.Placement.TypeIndex == placement.TypeIndex && built.Placement.TypeName == placement.TypeName
			&& built.Placement.WeaponRefs.SequenceEqual(placement.WeaponRefs)
			&& built.Placement.WeaponSecondary.SequenceEqual(placement.WeaponSecondary));
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
			MissionSelection => _selection.Current,
			RouteSelection { Waypoint: >= 0 } route when EditorSelection.InFile(_source.Mission, route) =>
				_generated.Records.Route(route.Guid) is { } kept && route.Waypoint < _generated.Route(kept)?.Points.Count
					? route
					: route with { Waypoint = -1 },
			{ } selected when EditorSelection.InFile(_source.Mission, selected) => selected,
			_ => null
		};
	}
}

/// <summary>
/// One step of <see cref="PointMover"/>'s undo history that the scene is synced again after: a move
/// (<see cref="PointMover"/>), a side change (<see cref="SideChanger"/>), an add (<see cref="StructurePlacer"/>,
/// <see cref="HercPlacer"/>, <see cref="FlyerPlacer"/>, <see cref="VehiclePlacer"/>), a delete (<see cref="ObjectDeleter"/>), a
/// fit or type change (<see cref="MachineFitter"/>, <see cref="RosterTypeEditor"/>), a squad edit (<see cref="SquadEditor"/>), a
/// route edit (<see cref="RouteEditor"/>), a trigger-area edit (<see cref="AreaEditor"/>), an order edit (<see cref="OrderEditor"/>),
/// an action or wiring edit (<see cref="ActionEditor"/>), a timer edit (<see cref="TimerEditor"/>), a condition edit
/// (<see cref="ConditionEditor"/>), a Present when set (<see cref="PresenceEditor"/>), a variant table edit
/// (<see cref="VariantEditor"/>), an objective edit (<see cref="ObjectiveEditor"/>) or a text edit (<see cref="MissionTextEditor"/>):
/// the rows and text it changed, put back on undo and redo through <see cref="SceneSync"/>.
/// </summary>
internal sealed record RecordsEdit(SceneSync Sync, MsnEdit Change) : IEditorEdit {
	public void Undo() => Sync.Undo(Change);

	public void Redo() => Sync.Redo(Change);
}
