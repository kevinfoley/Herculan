using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Scene;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Builds again what the editor holds by <c>script.dat</c> block index after an edit that adds or removes <c>.MSN</c>
/// records: an add, a delete, and the undo and redo of either (<see cref="RecordsEdit"/>). Removing a record moves every
/// later generated record of its block down one, and an add's records are exported last, so the indices the index, the
/// scene objects' placements and the selection hold no longer name what they named
/// (docs/herculan/handoff-editor-authoring.md#holding-the-msn-as-the-document, "Regenerating on each edit").
///
/// <para>A rebuild loads a <see cref="Mission"/> from the generated <c>script.dat</c> (<see cref="MissionLoader.Load(GameContent, ScriptDat, string)"/>)
/// and stands each of its placements on a scene object: the one already built for the same <c>.MSN</c> record, matched
/// by the GUID of the record that created its slot (<see cref="MissionDocument.GuidOf"/>) and by type, its
/// <see cref="SceneObject.Placement"/> pointed at the new indices; or, for a record no object was built for, a new one
/// (<see cref="MissionScene.CreateObject"/>). A mech's object is reused only for the same weapon fit, since its loadout
/// is built with it. The player's squad is matched by its order in <c>player.mec</c>, which is built again first from the
/// generation, as the launch builds it (<see cref="MissionSquad"/>), when the squad pilots Hercs the mission supplies. Every
/// object built is kept, so an undone delete stands the same object, model and all, where it stood, and a step of the
/// undo history naming it still names what is in the scene. Terrain and models are not loaded again. The index, the
/// pick spheres, the overlays' lines, the followers and the mission check are then built over the new mission, and the
/// drawing is told what left and what joined the scene.</para>
///
/// <para>The selection survives when what it names does: an object by identity, or else the object now standing for the
/// same record; a group by its row-16 GUID, and a route by its row-8 GUID; anything else is checked against the new
/// index, and is cleared when it is gone.</para>
/// </summary>
internal sealed class MissionRebuild {
	private const int RouteBlock = 3;
	private const int RouteRow = 8;
	private const int GroupBlock = 11;
	private const int GroupRow = 16;

	// The squad's objects are keyed under this row, by their order in player.mec.
	private const int SquadRow = 0;

	private readonly GameContent _content;
	private readonly ScriptDat _document;
	private readonly MissionDocument? _source;
	private readonly MissionScene _scene;
	private readonly MissionIndex _index;
	private readonly MissionOverlay _overlay;
	private readonly ScenePicker _picker;
	private readonly PointMover _mover;
	private readonly MissionOutliner _outliner;
	private readonly SelectionState _selection;
	private readonly MissionSquad? _squad;

	// The record the selected object stood for when the edit began, whose new object is selected when it was replaced.
	private (int Row, int Key)? _selectedKey;

	// Every object built for the mission, by the .MSN record it stands for, the latest first.
	private readonly Dictionary<(int Row, int Key), List<SceneObject>> _built = new();

	public MissionRebuild(GameContent content, ScriptDat document, MissionDocument? source, MissionScene scene,
			MissionIndex index, MissionOverlay overlay, ScenePicker picker, PointMover mover, MissionOutliner outliner,
			SelectionState selection, MissionSquad? squad) {
		_content = content;
		_document = document;
		_source = source;
		_scene = scene;
		_index = index;
		_overlay = overlay;
		_picker = picker;
		_mover = mover;
		_outliner = outliner;
		_selection = selection;
		_squad = squad;
	}

	/// <summary>Raised with each object a rebuild puts in the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Added;

	/// <summary>Raised with each object a rebuild takes out of the scene, for whatever draws it.</summary>
	public event Action<SceneObject>? Removed;

	/// <summary>Raised once a rebuild is done, for whatever reads the index's groups.</summary>
	public event Action? Rebuilt;

	/// <summary>Every object a rebuild has filed, in the scene or kept for an undo or redo to put back.</summary>
	public IEnumerable<SceneObject> Built => _built.Values.SelectMany(objects => objects);

	/// <summary>
	/// Makes <paramref name="edit"/>, which writes the <c>.MSN</c> and generates again, then builds the editor's state
	/// again from the generated file. An edit that throws has put the records back, and nothing is rebuilt.
	/// </summary>
	public T Run<T>(Func<T> edit) {
		var source = _source ?? throw new InvalidOperationException("A plain script.dat is not edited.");
		Remember(source);
		short selectedGroup = _selection.Current is GroupSelection { Group: var group } ? source.GuidOf(GroupBlock, group) : (short)-1;
		short selectedRoute = _selection.Current is RouteSelection { Route: var route } ? source.GuidOf(RouteBlock, route) : (short)-1;
		var result = edit();
		Rebuild(source, selectedGroup, selectedRoute);
		return result;
	}

	/// <summary>Puts back the rows <paramref name="change"/> changed as it found them, and rebuilds.</summary>
	public void Undo(MsnEdit change) => Run(() => {
		_source!.Undo(change);
		return change;
	});

	/// <summary>Puts back the rows <paramref name="change"/> changed as it left them, and rebuilds.</summary>
	public void Redo(MsnEdit change) => Run(() => {
		_source!.Redo(change);
		return change;
	});

	// Files every object in the scene under the record it stands for in the generation about to be replaced.
	private void Remember(MissionDocument source) {
		int squad = 0;
		_selectedKey = null;
		foreach (var placed in _index.Objects) {
			var key = KeyOf(source, placed.Placement, ref squad);
			if (_selection.Current is ObjectSelection { Object: var selected } && ReferenceEquals(selected, placed)) {
				_selectedKey = key;
			}

			if (!_built.TryGetValue(key, out var objects)) {
				_built[key] = objects = new List<SceneObject>();
			}

			objects.RemoveAll(built => ReferenceEquals(built, placed));
			objects.Insert(0, placed);
		}
	}

	private void Rebuild(MissionDocument source, short selectedGroup, short selectedRoute) {
		// The squad the launch would build from this generation, which the load below places.
		if (_squad?.PlayerMec(source.Generator) is { } squadFile) {
			File.WriteAllBytes(MissionLoader.PlayerPathFor(_scene.Mission.SourcePath), squadFile);
		}

		var mission = MissionLoader.Load(_content, _document, _scene.Mission.SourcePath);
		var terrain = _scene.World.Terrain;
		var before = _index.Objects.ToHashSet(ReferenceEqualityComparer.Instance);
		var objects = new List<SceneObject>(mission.Placements.Count);
		var placedNow = new HashSet<object>(ReferenceEqualityComparer.Instance);
		SceneObject? replacing = null;
		int squad = 0;
		foreach (var placement in mission.Placements) {
			var key = KeyOf(source, placement, ref squad);
			var placed = Reuse(key, placement, placedNow) ?? Build(key, placement);
			if (placed == null) {
				continue;
			}

			if (key == _selectedKey) {
				replacing = placed;
			}

			placedNow.Add(placed);
			objects.Add(placed);
			placed.Placement = placement;
			placed.Object.Side = placement.Side;
			var position = PointFollowers.Settled(terrain, placed.Object, MissionScene.SpawnPosition(terrain, placed.Object, placement));
			if (position != placed.Object.Position || placement.Heading != placed.Object.Heading) {
				_mover.Place(placed, position, placement.Heading);
			}
		}

		foreach (var gone in _index.Objects.Where(placed => !placedNow.Contains(placed))) {
			Removed?.Invoke(gone);
		}

		_index.Reload(mission, objects);
		_picker.Rebuild(objects);
		_overlay.Rebuild();
		_mover.Rebuild();
		_outliner.Recheck(new MissionLint(_index, _document));
		foreach (var joined in objects.Where(placed => !before.Contains(placed))) {
			Added?.Invoke(joined);
		}

		Reselect(source, selectedGroup, selectedRoute, replacing);
		Rebuilt?.Invoke();
	}

	// The object built for the record a placement stands for, of the placement's type and weapon fit, and not yet placed
	// by this rebuild. A type that differs is a variant drawn differently after the edit, or a chassis changed, and a fit
	// that differs a fit changed (MachineFitter); either needs an object of its own.
	private SceneObject? Reuse((int Row, int Key) key, MissionPlacement placement, HashSet<object> placedNow) {
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

	// A new object for a placement no built object stands for, filed under its record; null for a type the install has
	// nothing for, which the scene's load leaves out too.
	private SceneObject? Build((int Row, int Key) key, MissionPlacement placement) {
		if (_scene.CreateObject(placement) is not { } placed) {
			return null;
		}

		if (!_built.TryGetValue(key, out var objects)) {
			_built[key] = objects = new List<SceneObject>();
		}

		objects.Insert(0, placed);
		return placed;
	}

	// The .MSN record a placement stands for: its roster row and the GUID of the record that created its slot, or, for
	// the player's squad, its order in player.mec. A slot no record created (none in a generated file) is keyed by the
	// slot itself.
	private static (int Row, int Key) KeyOf(MissionDocument source, MissionPlacement placement, ref int squad) {
		if (placement.IsPlayerLance) {
			return (SquadRow, squad++);
		}

		int block = placement.Kind switch {
			MissionUnitKind.Mech => 7,
			MissionUnitKind.Flyer => 8,
			_ => 9
		};
		short guid = source.GuidOf(block, placement.SlotIndex);
		return guid != -1 ? (block + 5, guid) : (-block, placement.SlotIndex);
	}

	// Keeps the selection on what it named, or on <replacing>, the object now standing for the record a selected object
	// stood for; or clears it. A route is kept by its row-8 GUID, as a route delete moves every later route down one, and
	// its waypoint while the route still has one there.
	private void Reselect(MissionDocument source, short selectedGroup, short selectedRoute, SceneObject? replacing) {
		_selection.Current = _selection.Current switch {
			ObjectSelection { Object: var placed } when _index.Objects.Any(kept => ReferenceEquals(kept, placed)) => _selection.Current,
			ObjectSelection when replacing != null => new ObjectSelection(replacing),
			GroupSelection when selectedGroup != -1 && source.ExportedIndex(GroupRow, selectedGroup) is { } group =>
				new GroupSelection(group),
			AreaSelection { Area: var area } when _index.Area(area) != null => _selection.Current,
			RouteSelection { Waypoint: var waypoint } when selectedRoute != -1
					&& source.ExportedIndex(RouteRow, selectedRoute) is { } route && _index.Route(route) is { } kept =>
				new RouteSelection(route, waypoint < kept.Points.Count ? waypoint : -1),
			ActionSelection { Action: var action } when action < _index.Mission.Actions.Count => _selection.Current,
			TimerSelection { Timer: var timer } when timer < _index.Mission.ActionTimers.Count => _selection.Current,
			ObjectiveSelection { Objective: var objective } when objective < _index.Mission.Objectives.Count => _selection.Current,
			MissionSelection => _selection.Current,
			_ => null
		};
	}
}

/// <summary>
/// One step of <see cref="PointMover"/>'s undo history that the editor's state is built again after — an add
/// (<see cref="StructurePlacer"/>, <see cref="HercPlacer"/>), a delete (<see cref="ObjectDeleter"/>), a route edit
/// (<see cref="RouteEditor"/>) or an order edit (<see cref="OrderEditor"/>): the rows it changed, put back on undo and redo
/// through <see cref="MissionRebuild"/>.
/// </summary>
internal sealed record RecordsEdit(MissionRebuild Rebuild, MsnEdit Change) : IEditorEdit {
	public void Undo() => Rebuild.Undo(Change);

	public void Redo() => Rebuild.Redo(Change);
}
