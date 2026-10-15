using System.Numerics;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>What <see cref="PointMover"/> moves and the move handles drag.</summary>
internal abstract record MoveTarget;

/// <summary>A placed object, moved through a point of its roster record's own.</summary>
internal sealed record ObjectTarget(SceneObject Object) : MoveTarget;

/// <summary>
/// Waypoint <paramref name="Index"/> of <paramref name="Route"/>. <paramref name="WithLast"/> when it is a closed route's
/// first waypoint, whose last waypoint moves with it so the route stays closed (<see cref="EditorRoute.Closed"/>).
/// <paramref name="Detach"/> when the move gives it a point of its own rather than moving the point it names.
/// </summary>
internal sealed record WaypointTarget(EditorRoute Route, int Index, bool WithLast, bool Detach = false) : MoveTarget;

/// <summary>
/// Group <paramref name="Group"/>, by block-11 record index, moved through the point it stands on: its own, else its
/// route's first waypoint. <paramref name="Detach"/> when the move gives it a point of its own rather than moving the
/// point it stands on.
/// </summary>
internal sealed record GroupTarget(int Group, bool Detach = false) : MoveTarget;

/// <summary>
/// Moves what stands on a <c>script.dat</c> block-1 point — a placed object, a route's waypoint or a group — and keeps an
/// undo history. After every change to the document's points, <see cref="PointFollowers"/> places again whatever else
/// stands on them, and after every move, previews included, <see cref="TerrainLeveller"/> levels the ground under the
/// structures where they now stand.
///
/// <para>An object is given a point of its own (<see cref="ScriptPointRefs.Place"/>), which its roster record's ref then
/// names and which wins over its group's, so the object alone moves
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 2). The height is the spawn pass's (rule 5
/// there): only a flyer's Z is read from the point, so only a flyer's is the mission's to set, and the point written
/// for anything else carries the height the simulator gives it (<see cref="Standing"/>).</para>
///
/// <para>A waypoint moves the point it names, so everything else naming it moves too: retail missions share a point
/// between a waypoint and the trigger area that fires on reaching it, and between a route's first waypoint and the
/// groups standing on it. A detached move (<see cref="WaypointTarget.Detach"/>) gives it a point of its own instead.
/// Either way its point is written with Z 0, as all but one waypoint in the retail missions has it; the simulator reads
/// a waypoint's Z only as the starting altitude of a flight standing on its route's first waypoint and for the
/// heads-down map's route icons (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once).</para>
///
/// <para>A group moves as a waypoint does: the point it stands on — its block-11 record's own, else its slot-0 route's
/// first waypoint (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 3) — moves, and whatever
/// shares it with it; a detached move gives the record a point of its own. Its members with no point of their own, a
/// paints-ground group's pad and, for group 0, the player's squad follow (<see cref="PointFollowers"/>). The move is of
/// the authored point, which a paints-ground structure group's snap then puts in its tile. Only a flyer group's height
/// is read from the point (rule 5), so only a flyer group's is the mission's to set; any other keeps the point's Z.</para>
/// </summary>
internal sealed class PointMover {
	private readonly ScriptDat _document;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly ScenePicker _picker;
	private readonly MissionOverlay _overlay;
	private readonly PointFollowers _followers;
	private readonly TerrainLeveller _leveller;
	private readonly List<Edit> _undo = new();
	private readonly List<Edit> _redo = new();

	// Block 1 as it stood before a drag of a shared point or a group began writing into it, and the dragged group's
	// ref, until the drag commits or is cancelled.
	private ScriptCoordinate[]? _beforeDrag;
	private short[]? _refsBeforeDrag;

	public PointMover(ScriptDat document, MissionScene scene, MissionIndex index, ScenePicker picker, MissionOverlay overlay,
			TerrainMaterialTable materials) {
		_document = document;
		_terrain = scene.World.Terrain;
		_index = index;
		_picker = picker;
		_overlay = overlay;
		_followers = new PointFollowers(document, scene, index, overlay, Place);
		_leveller = new TerrainLeveller(scene, index, materials, overlay, Place);
		LoadedBox = MissionBox.Of(scene.Mission.Coordinates);
	}

	/// <summary>Raised with each object whose simulation object has moved, for whatever draws it.</summary>
	public event Action<SceneObject>? Moved;

	/// <summary>Raised with the run of terrain mesh vertices a move re-levelled, for whatever uploads it.</summary>
	public event Action<int, int>? TerrainChanged {
		add => _leveller.Changed += value;
		remove => _leveller.Changed -= value;
	}

	/// <summary>The mission box as the mission was opened, which a point outside of grows.</summary>
	public MissionBox LoadedBox { get; }

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	/// <summary>What <paramref name="selection"/> names that can be moved: a movable object, a waypoint or a group, else null.</summary>
	public MoveTarget? TargetOf(EditorSelection? selection) => selection switch {
		ObjectSelection { Object: var selected } when WhyFixed(selected) == null => new ObjectTarget(selected),
		RouteSelection { Waypoint: >= 0 } waypoint when _index.Route(waypoint.Route) is { } route
				&& waypoint.Waypoint < route.Points.Count && _followers.WaypointEntries(route.Reference) != null =>
			new WaypointTarget(route, waypoint.Waypoint, waypoint.Waypoint == 0 && route.Closed),
		GroupSelection { Group: var group } when WhyFixed(group) == null => new GroupTarget(group),
		_ => null
	};

	/// <summary>
	/// Why <paramref name="sceneObject"/> cannot be given a point of its own, or null when it can. The player's squad
	/// is not in the rosters: each <c>player.mec</c> entry is placed from block 11's record 0
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 6).
	/// </summary>
	public string? WhyFixed(SceneObject sceneObject) {
		var placement = sceneObject.Placement;
		if (placement.IsPlayerLance) {
			return "The player's squad stands at group 0's point; it has no roster record of its own. Move group 0 to move it.";
		}

		return RecordRef(placement.Kind, placement.SlotIndex) == null
			? $"Its {placement.Kind.ToString().ToLowerInvariant()} roster slot {placement.SlotIndex} is not in the file."
			: null;
	}

	/// <summary>
	/// Why group <paramref name="group"/> cannot be moved, or null when it can. A structure group that paints its ground
	/// with no structure placed has no pad to snap by and nothing standing on its point.
	/// </summary>
	public string? WhyFixed(int group) {
		if (group < 0 || group >= _document.Groups.Length || _index.SetupOf(group) is not { } setup) {
			return $"Group {group}'s record is not in the file.";
		}

		bool snaps = setup.PaintsGround && group < _index.Mission.GroupKinds.Count
			&& _index.Mission.GroupKinds[group] == MissionUnitKind.Base;
		return snaps && _index.BasePads.All(pad => pad.GroupIndex != group)
			? "It paints its ground but has no structure placed: nothing stands on its point."
			: null;
	}

	/// <summary>
	/// How many other fields name the point <paramref name="target"/>'s waypoint names — trigger areas, groups, other
	/// waypoints — which move with it unless it is detached.
	/// </summary>
	public int SharersOf(WaypointTarget target) {
		var refs = ReadRefs(target);
		return refs.Distinct().Sum(reference => ScriptPointRefs.Count(_document, reference)) - refs.Length;
	}

	/// <summary>
	/// How many other fields name the point <paramref name="target"/>'s group stands on — trigger areas, other groups,
	/// waypoints — which move with it unless it is detached. For a group standing on its route's first waypoint, these
	/// are besides that waypoint (<see cref="SharersOf(WaypointTarget)"/>).
	/// </summary>
	public int SharersOf(GroupTarget target) {
		short own = _document.Groups[target.Group].PositionRef;
		if (Coordinate(own) != null) {
			return ScriptPointRefs.Count(_document, own) - 1;
		}

		return StartOf(target.Group) is { } start ? SharersOf(start) : 0;
	}

	/// <summary>Whether a point's Z is the mission's to set for <paramref name="sceneObject"/>: only a flyer's is.</summary>
	public static bool HasOwnHeight(SceneObject sceneObject) => sceneObject.Object is FlyerObject;

	/// <summary>Whether <paramref name="target"/> takes a height: a flyer or a flyer group alone.</summary>
	public bool HasOwnHeight(MoveTarget target) => target switch {
		ObjectTarget { Object: var sceneObject } => HasOwnHeight(sceneObject),
		GroupTarget group => IsFlyerGroup(group.Group),
		_ => false
	};

	/// <summary>
	/// Where <paramref name="target"/> stands now, in world units. A group's is its authored point, before a paints-ground
	/// snap; a flyer group's height is where its first member flies, 5000 for a point at Z 0 (rule 5).
	/// </summary>
	public Vec3i PositionOf(MoveTarget target) => target switch {
		ObjectTarget { Object: var sceneObject } => sceneObject.Object.Position,
		WaypointTarget waypoint => waypoint.Route.Points[waypoint.Index],
		GroupTarget { Group: var group } when _index.SetupOf(group) is { AuthoredPoint: var point } =>
			IsFlyerGroup(group) ? new Vec3i(point.X, point.Y, FlyerHeight(point.Z)) : point,
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// Where the move handles sit, in render space: on an object's position or a flyer group's first member, or on a
	/// waypoint's or another group's marker, which is drawn on the ground under it.
	/// </summary>
	public Vector3 HandleOrigin(MoveTarget target) {
		var position = PositionOf(target);
		return target is WaypointTarget || (target is GroupTarget && !HasOwnHeight(target))
			? WorldScale.ToRender(new Vec3i(position.X, position.Y, _terrain.HeightAtWorld(position.X, position.Y) + DrapedLines.LiftWorld))
			: WorldScale.ToRender(position);
	}

	/// <summary>
	/// Where <paramref name="target"/> stands for a point at (<paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="z"/>): an object where the simulator stands it, <paramref name="z"/> read for a flyer alone; a
	/// waypoint at Z 0; a group at <paramref name="z"/> when it is a flyer group, else at its point's own Z.
	/// </summary>
	public Vec3i Standing(MoveTarget target, int x, int y, int z) => target switch {
		ObjectTarget { Object: var sceneObject } => PointFollowers.Standing(_terrain, sceneObject, x, y, z),
		WaypointTarget => new Vec3i(x, y, 0),
		GroupTarget group => new Vec3i(x, y, HasOwnHeight(group) ? z : PositionOf(group).Z),
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// The waypoint group <paramref name="group"/> stands on when its record names no point of its own: its slot-0
	/// order's route's first, with the last when the route is closed. Null when it has a point of its own, or no route
	/// whose waypoints can be told apart.
	/// </summary>
	public WaypointTarget? StartOf(int group) {
		if (Coordinate(_document.Groups[group].PositionRef) != null || group >= _index.Mission.GroupOrders.Count
				|| _index.Mission.GroupOrders[group] is not { Count: > 0 } orders || orders[0] is not { RouteRef: var routeRef }
				|| _index.Route(routeRef) is not { Points.Count: > 0 } route || _followers.WaypointEntries(route.Reference) == null) {
			return null;
		}

		return new WaypointTarget(route, 0, route.Closed);
	}

	/// <summary>
	/// Stands <paramref name="target"/> at <paramref name="position"/> as a drag does before it lets go. An object or a
	/// detached waypoint moves for the eye alone, the document unchanged until <see cref="Commit"/>; a waypoint moving
	/// its shared point, or a group, writes into the document, so what follows it does, and <see cref="Commit"/> or
	/// <see cref="Cancel"/> settles it.
	/// </summary>
	public void Preview(MoveTarget target, Vec3i position) {
		if (target is WaypointTarget { Detach: false } waypoint) {
			_beforeDrag ??= Snapshot(_document.Coordinates);
			WritePoints(waypoint, position);
			_followers.Sync();
		} else if (target is GroupTarget group) {
			// Each step starts again from the document as the drag found it, so a detached group appends one point at
			// most, however far it is dragged.
			_beforeDrag ??= Snapshot(_document.Coordinates);
			_refsBeforeDrag ??= ReadRefs(group);
			_document.Coordinates = Snapshot(_beforeDrag);
			WriteRefs(group, _refsBeforeDrag);
			try {
				MoveGroup(group, position);
			} catch (InvalidOperationException) {
				// Commit reports it.
			}

			_followers.Sync();
		} else {
			Stand(target, position);
		}

		_leveller.Relevel();
	}

	/// <summary>Puts back what <see cref="Preview"/> moved, for a drag that went nowhere.</summary>
	public void Cancel(MoveTarget target, Vec3i before) {
		if (_beforeDrag is { } points) {
			_document.Coordinates = points;
			if (_refsBeforeDrag is { } refs) {
				WriteRefs(target, refs);
			}

			_beforeDrag = null;
			_refsBeforeDrag = null;
			_followers.Sync();
		} else {
			Stand(target, before);
		}

		_leveller.Relevel();
	}

	/// <summary>
	/// Moves <paramref name="target"/> to (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) for good,
	/// as one step of the undo history. <paramref name="before"/> is where it stood before any <see cref="Preview"/> of
	/// this move.
	/// </summary>
	public void Commit(MoveTarget target, Vec3i before, int x, int y, int z) {
		if (target is ObjectTarget { Object: var sceneObject } && WhyFixed(sceneObject) != null) {
			return;
		}

		var position = Standing(target, x, y, z);
		short[] refs = ReadRefs(target);

		if (target is WaypointTarget { Detach: false } shared) {
			var unmoved = _beforeDrag ?? Snapshot(_document.Coordinates);
			_beforeDrag = null;
			WritePoints(shared, position);
			Record(target, new State(unmoved, refs, before), new State(Snapshot(_document.Coordinates), refs, position));
			return;
		}

		if (target is GroupTarget group) {
			var unmoved = _beforeDrag ?? Snapshot(_document.Coordinates);
			short[] refsBefore = _refsBeforeDrag ?? refs;
			_beforeDrag = null;
			_refsBeforeDrag = null;
			_document.Coordinates = Snapshot(unmoved);
			WriteRefs(group, refsBefore);
			try {
				MoveGroup(group, position);
			} catch (InvalidOperationException ex) {
				Console.Error.WriteLine($"Cannot move {Describe(target)}: {ex.Message}");
				_document.Coordinates = Snapshot(unmoved);
				WriteRefs(group, refsBefore);
				_followers.Sync();
				_leveller.Relevel();
				return;
			}

			Record(target, new State(unmoved, refsBefore, before),
				new State(Snapshot(_document.Coordinates), ReadRefs(group), position));
			return;
		}

		var from = new State(Snapshot(_document.Coordinates), refs, before);
		short[] placed;
		try {
			// A closed route's first and last waypoints name one point between them more often than not, and move
			// as one.
			if (refs.Length > 1 && refs.All(reference => reference == refs[0])) {
				short one = ScriptPointRefs.Place(_document, refs[0], position.X, position.Y, position.Z, refs.Length);
				placed = Array.ConvertAll(refs, _ => one);
			} else {
				placed = Array.ConvertAll(refs, reference =>
					ScriptPointRefs.Place(_document, reference, position.X, position.Y, position.Z));
			}
		} catch (InvalidOperationException ex) {
			Console.Error.WriteLine($"Cannot move {Describe(target)}: {ex.Message}");
			_document.Coordinates = Snapshot(from.Coordinates);
			Stand(target, before);
			_leveller.Relevel();
			return;
		}

		WriteRefs(target, placed);
		Stand(target, position);
		Record(target, from, new State(Snapshot(_document.Coordinates), placed, position));
	}

	public void Undo() => Step(_undo, _redo, edit => edit.Before);

	public void Redo() => Step(_redo, _undo, edit => edit.After);

	private void Record(MoveTarget target, State from, State to) {
		_undo.Add(new Edit(target, from, to));
		_redo.Clear();
		_followers.Sync();
		_overlay.SetBox(MissionBox.Of(Points()));
		_leveller.Relevel();
	}

	private void Step(List<Edit> from, List<Edit> to, Func<Edit, State> state) {
		if (from.Count == 0) {
			return;
		}

		var edit = from[^1];
		from.RemoveAt(from.Count - 1);
		to.Add(edit);

		var target = state(edit);
		_document.Coordinates = Snapshot(target.Coordinates);
		WriteRefs(edit.Target, target.References);
		_followers.Sync();

		// An object's own move is the one thing the followers do not place: undone, its record names no point again
		// and it stands where its group puts it.
		if (edit.Target is ObjectTarget) {
			Stand(edit.Target, target.Position);
		}

		_overlay.SetBox(MissionBox.Of(Points()));
		_leveller.Relevel();
	}

	// Writes a waypoint's new position into each point it names.
	private void WritePoints(WaypointTarget target, Vec3i position) {
		foreach (short reference in ReadRefs(target).Distinct()) {
			var point = _document.Coordinates[reference];
			point.X = position.X;
			point.Y = position.Y;
			point.Z = position.Z;
		}
	}

	// Moves a group's point in the document as it stands: the point it stands on, its own or its route's first
	// waypoint's, unless the move detaches it or it stands on neither; else a point of its own. The Z written is the
	// point's own unless a flyer group's height moved: a flyer group at the point's Z 0 flies at 5000, and writing 5000
	// would lift every member whose slot adds a height of its own.
	private void MoveGroup(GroupTarget target, Vec3i position) {
		short own = _document.Groups[target.Group].PositionRef;
		var start = StartOf(target.Group);
		int pointZ = Coordinate(own)?.Z ?? (start != null ? _document.Coordinates[ReadRefs(start)[0]].Z : 0);
		int z = HasOwnHeight(target) && position.Z != FlyerHeight(pointZ) ? position.Z : pointZ;

		if (!target.Detach && Coordinate(own) != null) {
			var point = _document.Coordinates[own];
			point.X = position.X;
			point.Y = position.Y;
			point.Z = z;
		} else if (!target.Detach && start != null) {
			WritePoints(start, new Vec3i(position.X, position.Y, z));
		} else {
			_document.Groups[target.Group].PositionRef = ScriptPointRefs.Place(_document, own, position.X, position.Y, z);
		}
	}

	private bool IsFlyerGroup(int group) =>
		group < _index.Mission.GroupKinds.Count && _index.Mission.GroupKinds[group] == MissionUnitKind.Flyer;

	// Where a flyer group's first member flies for a point at height z (rule 5).
	private static int FlyerHeight(int z) => z == 0 ? FlyerObject.DefaultHoverHeight : z;

	private Vec3i? Coordinate(short reference) =>
		reference >= 0 && reference < _document.Coordinates.Length
			? new Vec3i(_document.Coordinates[reference].X, _document.Coordinates[reference].Y, _document.Coordinates[reference].Z)
			: null;

	private void Stand(MoveTarget target, Vec3i position) {
		switch (target) {
			case ObjectTarget { Object: var sceneObject }:
				Place(sceneObject, position, sceneObject.Object.Heading);
				break;
			case WaypointTarget waypoint:
				waypoint.Route.Move(waypoint.Index, position);
				if (waypoint.WithLast) {
					waypoint.Route.Move(waypoint.Route.Points.Count - 1, position);
				}

				_overlay.MoveRoute(waypoint.Route);
				break;
		}
	}

	private void Place(SceneObject sceneObject, Vec3i position, int heading) {
		sceneObject.Object.Position = position;
		sceneObject.Object.Heading = heading;
		_picker.Move(sceneObject);
		Moved?.Invoke(sceneObject);
	}

	private static string Describe(MoveTarget target) => target switch {
		ObjectTarget { Object: var sceneObject } => MissionText.Describe(sceneObject),
		WaypointTarget waypoint => $"waypoint {waypoint.Index} of route {waypoint.Route.Reference}",
		GroupTarget group => $"group {group.Group}",
		_ => target.ToString()
	};

	private List<Vec3i> Points() => _document.Coordinates.Select(point => new Vec3i(point.X, point.Y, point.Z)).ToList();

	// The fields that name the target's point: its roster record's or its group record's PositionRef, or its block-3
	// entry — two for a closed route's first waypoint, which is also its last.
	private short[] ReadRefs(MoveTarget target) => target switch {
		ObjectTarget { Object.Placement: var placement } => new[] { RecordRef(placement.Kind, placement.SlotIndex)!.Value },
		WaypointTarget waypoint => Array.ConvertAll(WaypointEntries(waypoint),
			entry => _document.WaypointGroups[waypoint.Route.Reference].Waypoints[entry]),
		GroupTarget group => new[] { _document.Groups[group.Group].PositionRef },
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	private void WriteRefs(MoveTarget target, short[] refs) {
		switch (target) {
			case ObjectTarget { Object.Placement: var placement }:
				SetRecordRef(placement.Kind, placement.SlotIndex, refs[0]);
				break;
			case GroupTarget group:
				_document.Groups[group.Group].PositionRef = refs[0];
				break;
			case WaypointTarget waypoint:
				int[] entries = WaypointEntries(waypoint);
				for (int i = 0; i < entries.Length; i++) {
					_document.WaypointGroups[waypoint.Route.Reference].Waypoints[entries[i]] = refs[i];
				}

				break;
		}
	}

	private int[] WaypointEntries(WaypointTarget waypoint) {
		int[] entries = _followers.WaypointEntries(waypoint.Route.Reference)!;
		return waypoint.WithLast ? new[] { entries[0], entries[^1] } : new[] { entries[waypoint.Index] };
	}

	private short? RecordRef(MissionUnitKind kind, int slot) => kind switch {
		MissionUnitKind.Mech when slot >= 0 && slot < _document.Mechs.Length => _document.Mechs[slot].PositionRef,
		MissionUnitKind.Flyer when slot >= 0 && slot < _document.Flyers.Length => _document.Flyers[slot].PositionRef,
		MissionUnitKind.Base when slot >= 0 && slot < _document.Bases.Length => _document.Bases[slot].PositionRef,
		_ => null
	};

	private void SetRecordRef(MissionUnitKind kind, int slot, short reference) {
		switch (kind) {
			case MissionUnitKind.Mech:
				_document.Mechs[slot].PositionRef = reference;
				break;
			case MissionUnitKind.Flyer:
				_document.Flyers[slot].PositionRef = reference;
				break;
			case MissionUnitKind.Base:
				_document.Bases[slot].PositionRef = reference;
				break;
		}
	}

	// Block 1 is copied whole for each step: a few hundred points at most, and a copy cannot be changed by a later
	// edit's write in place.
	private static ScriptCoordinate[] Snapshot(ScriptCoordinate[] points) =>
		Array.ConvertAll(points, point => new ScriptCoordinate { X = point.X, Y = point.Y, Z = point.Z });

	private sealed record State(ScriptCoordinate[] Coordinates, short[] References, Vec3i Position);

	private sealed record Edit(MoveTarget Target, State Before, State After);
}
