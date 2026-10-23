using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>What <see cref="PointMover"/> moves and the move handles drag.</summary>
internal abstract record MoveTarget;

/// <summary>
/// One step of <see cref="PointMover"/>'s undo history: a move, an add, a delete, a side change, any edit
/// (<see cref="RecordsEdit"/>), or a theater change. Each puts the <c>.MSN</c> back as the step found or left it
/// (<see cref="MissionDocument.Undo"/>, which generates <c>script.dat</c> again) and syncs the scene with it
/// (<see cref="SceneSync"/>); the history then levels the ground.
/// </summary>
internal interface IEditorEdit {
	void Undo();

	void Redo();
}

/// <summary>A placed object, by the roster record it stands for, moved through a point of its record's own.</summary>
internal sealed record ObjectTarget(ObjectKey Key) : MoveTarget;

/// <summary>
/// Group <paramref name="Guid"/>, by its row-16 GUID, moved through the point it stands on: its own, else its slot-0
/// route's first waypoint. <paramref name="Detach"/> when the move gives it a point of its own rather than moving the
/// point it stands on.
/// </summary>
internal sealed record GroupTarget(short Guid, bool Detach = false) : MoveTarget;

/// <summary>
/// Moves what stands on a point — a placed object, a route's waypoint, a group or a trigger area's corner — and keeps an
/// undo history. A committed move writes the mission's <c>.MSN</c> (<see cref="MissionDocument"/>), which generates
/// <c>script.dat</c> again, and syncs the scene with it (<see cref="SceneSync"/>), as any edit does
/// (docs/herculan/handoff-editor-document.md#the-tools). A drag's preview writes nothing: everything standing on what it
/// moves, the route and area lines included, is placed from the layout with the drag's overrides
/// (<see cref="PlacementOverrides"/>, <see cref="SceneSync.Preview"/>). After every move, previews included,
/// <see cref="TerrainLeveller"/> levels the ground under the structures where they now stand. Its history also holds the
/// edits made elsewhere (<see cref="Record(IEditorEdit)"/>), so that Undo and Redo step through every edit in the order it
/// was made. The waypoint, area corner and radius moves are <see cref="FeatureMoves"/>'.
///
/// <para>An object is given a point of its own (<see cref="MissionDocument.PlacePoint"/>), which its roster record's ref
/// then names and which wins over its group's, so the object alone moves
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 2). The height is the spawn pass's (rule 5
/// there): only a flyer's Z is read from the point, so only a flyer's is the mission's to set, and the point written
/// for anything else carries the height the simulator gives it (<see cref="Standing"/>). A point the roster record
/// draws from a variant is shared by every record drawing it, so the move appends one rather than rewrite it.</para>
///
/// <para>A group moves as a waypoint does: the point it stands on — its own, else its slot-0 route's first waypoint, as the
/// layout has them (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 3) — moves, and whatever
/// shares it with it; a detached move gives the record a point of its own. Its members with no point of their own, a
/// paints-ground group's pad and, for group 0, the player's squad follow. The move is of the authored point, which a
/// paints-ground structure group's snap then puts in its tile. Only a flyer group's height is read from the point (rule 5),
/// so only a flyer group's is the mission's to set; any other keeps the point's Z.</para>
/// </summary>
internal sealed class PointMover {
	private readonly MissionDocument _source;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly SceneSync _sync;
	private readonly FeatureMoves _features;
	private readonly TerrainLeveller _leveller;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	public PointMover(MissionDocument source, MissionScene scene, MissionIndex index, SceneSync sync, MissionOverlay overlay,
			TerrainMaterialTable materials) {
		_source = source;
		_terrain = scene.World.Terrain;
		_index = index;
		_sync = sync;
		_features = new FeatureMoves(source, index);
		_leveller = new TerrainLeveller(scene, index, materials, overlay, sync.Place);
		LoadedBox = index.Box;
	}

	/// <summary>Raised with each object whose simulation object has moved, for whatever draws it.</summary>
	public event Action<SceneObject>? Moved {
		add => _sync.Moved += value;
		remove => _sync.Moved -= value;
	}

	/// <summary>Raised with the run of terrain mesh vertices a move re-levelled, for whatever uploads it.</summary>
	public event Action<int, int>? TerrainChanged {
		add => _leveller.Changed += value;
		remove => _leveller.Changed -= value;
	}

	/// <summary>The mission box as the mission was opened, which a point outside of grows.</summary>
	public MissionBox LoadedBox { get; }

	/// <summary>The undo history, which also says whether the mission has changed since it was last saved or opened.</summary>
	public EditHistory History { get; } = new();

	public bool CanUndo => History.CanUndo;

	public bool CanRedo => History.CanRedo;

	/// <summary>
	/// What <paramref name="selection"/> names that the move handles can drag: a movable object or group in the scene, by
	/// the record it stands for, or a waypoint, by its route's GUID; else null. An object or group out of the scene
	/// (<see cref="PlannedGroup.OffScene"/>) has no handles.
	/// </summary>
	public MoveTarget? TargetOf(EditorSelection? selection) => selection switch {
		ObjectSelection { Row: var row, Guid: var guid } when WhyFixed(new ObjectKey(row, guid)) == null
				&& _index.ObjectOf(new ObjectKey(row, guid)) is { } placed && _index.InScene(placed) =>
			new ObjectTarget(new ObjectKey(row, guid)),
		RouteSelection waypoint => _features.TargetOf(waypoint),
		GroupSelection { Guid: var guid } when WhyFixedGroup(guid) == null && _index.Group(guid) is { OffScene: false } =>
			new GroupTarget(guid),
		_ => null
	};

	/// <summary>
	/// Everything of <paramref name="selection"/> the move handles drag: a trigger area's corners, or a circle's centre and
	/// radius (<see cref="FeatureMoves.TargetsOf"/>); else <see cref="TargetOf"/>'s one target, or none.
	/// </summary>
	public IReadOnlyList<MoveTarget> TargetsOf(EditorSelection? selection) => selection is AreaSelection { Guid: var guid }
		? _features.TargetsOf(guid)
		: TargetOf(selection) is { } target ? [target] : [];

	/// <inheritdoc cref="FeatureMoves.WhyFixedArea"/>
	public string? WhyFixedArea(short area) => _features.WhyFixedArea(area);

	/// <inheritdoc cref="FeatureMoves.SharersOf(AreaCornerTarget)"/>
	public int SharersOf(AreaCornerTarget target) => _features.SharersOf(target);

	/// <inheritdoc cref="FeatureMoves.SharersOf(WaypointTarget)"/>
	public int SharersOf(WaypointTarget target) => _features.SharersOf(target);

	/// <summary>
	/// Sets the radius of circle trigger area <paramref name="area"/>, by its row-9 GUID, to <paramref name="radius"/> world
	/// units, as one step of the undo history, as a drag of its radius handle does.
	/// </summary>
	public void CommitRadius(short area, int radius) {
		if (_index.Area(area)?.Area is not { } circle) {
			return;
		}

		var target = new AreaRadiusTarget(area);
		Commit(target, PositionOf(target), circle.A.X + radius, circle.A.Y, 0);
	}

	/// <summary>Why <paramref name="sceneObject"/> cannot be given a point of its own, or null when it can.</summary>
	public string? WhyFixed(SceneObject sceneObject) =>
		_index.KeyOf(sceneObject) is { } key ? WhyFixed(key) : "Its roster record is not in the file.";

	/// <summary>
	/// Why the object of <paramref name="key"/> cannot be given a point of its own, or null when it can. The player's squad
	/// spreads from group 0 whatever point its records name (docs/retail/formats/script-dat.md#placement--the-actual-rule,
	/// rule 6).
	/// </summary>
	public string? WhyFixed(ObjectKey key) {
		if (_index.Plan.Object(key) is not { } planned || _index.ObjectOf(key) == null
				|| _source.Layout.Entry(key.Row, key.Guid) == null) {
			return "Its roster record is not in the file.";
		}

		return planned.Squad ? "The player's squad spreads from group 0's point: move group 0 to move it." : null;
	}

	/// <summary>
	/// Why group <paramref name="guid"/> cannot be moved, or null when it can. A structure group that paints its ground
	/// with no structure placed has no pad to snap by and nothing standing on its point.
	/// </summary>
	public string? WhyFixedGroup(short guid) {
		if (_index.Group(guid) is not { } group) {
			return "Its record is not in the file.";
		}

		bool snaps = group.Setup.PaintsGround && group.Kind == MissionUnitKind.Base;
		return snaps && group.Pad == null
			? "It paints its ground but has no structure placed: nothing stands on its point."
			: null;
	}

	/// <summary>
	/// How many other fields name the point <paramref name="target"/>'s group stands on — trigger areas, other groups,
	/// waypoints — which move with it unless it is detached. For a group standing on its route's first waypoint, these
	/// are besides that waypoint (and the route's last, when it closes on its first).
	/// </summary>
	public int SharersOf(GroupTarget target) {
		if (_index.Group(target.Guid) is not { } group) {
			return 0;
		}

		return group.OwnPoint != -1
			? _source.CountPointRefs(group.OwnPoint) - 1
			: group.StartPoints.Distinct().Sum(_source.CountPointRefs) - group.StartPoints.Count;
	}

	/// <summary>Whether a point's Z is the mission's to set for <paramref name="sceneObject"/>: only a flyer's is.</summary>
	public static bool HasOwnHeight(SceneObject sceneObject) => sceneObject.Object is FlyerObject;

	/// <summary>Whether <paramref name="target"/> takes a height: a flyer or a flyer group alone.</summary>
	public bool HasOwnHeight(MoveTarget target) => target switch {
		ObjectTarget { Key: var key } => _index.ObjectOf(key) is { } placed && HasOwnHeight(placed),
		GroupTarget group => _index.Group(group.Guid)?.Kind == MissionUnitKind.Flyer,
		_ => false
	};

	/// <summary>
	/// Where <paramref name="target"/> stands now, in world units, a drag's preview included. A group's is its authored
	/// point, before a paints-ground snap; a flyer group's height is where its first member flies, 5000 for a point at Z 0
	/// (rule 5).
	/// </summary>
	public Vec3i PositionOf(MoveTarget target) => target switch {
		ObjectTarget { Key: var key } when _index.ObjectOf(key) is { } placed => placed.Object.Position,
		GroupTarget { Guid: var guid } when _index.Group(guid) is { Setup.AuthoredPoint: var point } group =>
			group.Kind == MissionUnitKind.Flyer ? new Vec3i(point.X, point.Y, FlyerHeight(point.Z)) : point,
		_ when FeatureMoves.Moves(target) => _features.PositionOf(target),
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// Where the move handles sit, in render space: on an object's position or a flyer group's first member, or on a
	/// waypoint's or another group's marker, which is drawn on the ground under it.
	/// </summary>
	public Vector3 HandleOrigin(MoveTarget target) {
		var position = PositionOf(target);
		return FeatureMoves.Moves(target) || (target is GroupTarget && !HasOwnHeight(target))
			? WorldScale.ToRender(new Vec3i(position.X, position.Y, _terrain.HeightAtWorld(position.X, position.Y) + DrapedLines.LiftWorld))
			: WorldScale.ToRender(position);
	}

	/// <summary>
	/// Where <paramref name="target"/> stands for a point at (<paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="z"/>): an object where the simulator stands it, <paramref name="z"/> read for a flyer alone; a
	/// waypoint at Z 0; a group at <paramref name="z"/> when it is a flyer group, else at its point's own Z.
	/// </summary>
	public Vec3i Standing(MoveTarget target, int x, int y, int z) => target switch {
		ObjectTarget { Key: var key } when _index.ObjectOf(key) is { } placed => GroundStanding.Standing(_terrain, placed, x, y, z),
		GroupTarget group => new Vec3i(x, y, HasOwnHeight(group) ? z : PositionOf(group).Z),
		_ when FeatureMoves.Moves(target) => new Vec3i(x, y, 0),
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// Whether group <paramref name="guid"/> stands on a point the layout has, its own or its route's first waypoint's,
	/// rather than at the origin.
	/// </summary>
	public bool StandsOnPoint(short guid) => _index.Group(guid) is { } group && (group.OwnPoint != -1 || group.StartPoints.Count > 0);

	/// <summary>Whether group <paramref name="guid"/> has no point of its own and stands on its slot-0 route's first waypoint.</summary>
	public bool OnRouteStart(short guid) => _index.Group(guid) is { OwnPoint: -1, StartPoints.Count: > 0 };

	/// <summary>
	/// Stands <paramref name="target"/> at <paramref name="position"/> as a drag does before it lets go, writing nothing
	/// until <see cref="Commit"/>: whatever stands on what moves, the route and area lines included, is placed from the
	/// layout with the drag's overrides.
	/// </summary>
	public void Preview(MoveTarget target, Vec3i position) {
		_sync.Preview(target switch {
			GroupTarget group => GroupOverrides(group, position),
			ObjectTarget { Key: var key } => new PlacementOverrides { ObjectPoint = (key, position) },
			_ => _features.Overrides(target, position)
		});
		_leveller.Relevel();
	}

	/// <summary>Puts back what <see cref="Preview"/> moved, for a drag that went nowhere.</summary>
	public void Cancel(MoveTarget target, Vec3i before) {
		_sync.Preview(null);
		_leveller.Relevel();
	}

	/// <summary>
	/// Moves <paramref name="target"/> to (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) for good,
	/// as one step of the undo history: writes the <c>.MSN</c>, which generates <c>script.dat</c> again, and syncs the
	/// scene. <paramref name="before"/> is where it stood before any <see cref="Preview"/> of this move.
	/// </summary>
	public void Commit(MoveTarget target, Vec3i before, int x, int y, int z) {
		string? fixedNow = target switch {
			ObjectTarget { Key: var key } => WhyFixed(key),
			GroupTarget { Guid: var guid } => WhyFixedGroup(guid),
			_ => _features.WhyFixed(target)
		};

		if (fixedNow != null) {
			Cancel(target, before);
			return;
		}

		var position = Standing(target, x, y, z);
		if (target is AreaRadiusTarget radius && _features.Unchanged(radius, position)) {
			// Within the stored radius's tens of units of where it was: nothing to write.
			Cancel(target, before);
			return;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => _source.Change(() => Write(target, position)));
		} catch (InvalidOperationException ex) {
			Console.Error.WriteLine($"Cannot move {Describe(target)}: {ex.Message}");
			Cancel(target, before);
			return;
		}

		Record(new RecordsEdit(_sync, change));
	}

	public void Undo() => Step(History.TakeUndo(), undo: true);

	public void Redo() => Step(History.TakeRedo(), undo: false);

	/// <summary>
	/// Adds an edit made elsewhere, already applied, to the history as one step, and levels after it as after a move.
	/// </summary>
	public void Record(IEditorEdit edit) {
		History.Add(edit);
		_leveller.Relevel();
	}

	private void Step(IEditorEdit? edit, bool undo) {
		if (edit == null) {
			return;
		}

		if (undo) {
			edit.Undo();
		} else {
			edit.Redo();
		}

		_leveller.Relevel();
	}

	// Writes a move into the .MSN: a group's (WriteGroup); an object's own point (GiveObjectPoint); or a waypoint's, an area
	// corner's or a radius (FeatureMoves.Write).
	private void Write(MoveTarget target, Vec3i position) {
		switch (target) {
			case GroupTarget group:
				WriteGroup(group, position);
				break;
			case ObjectTarget placed:
				GiveObjectPoint(placed, position);
				break;
			default:
				_features.Write(target, position);
				break;
		}
	}

	// Gives an object a point of its own at position, which its roster record's PositionRef then names: the point it names
	// rewritten when that field is all that names it, else a point appended. A PositionRef a variant supplies names a point
	// every record drawing that variant shares, so it does not count as moving and a point is appended.
	private void GiveObjectPoint(ObjectTarget target, Vec3i position) {
		var (row, guid) = target.Key;
		var entry = _source.Layout.Entry(row, guid) ?? throw new InvalidOperationException("Its roster record is not in the file.");
		var (own, word) = entry.Fields switch {
			MechRosterEntry144 herc => (herc.PositionRef, MechRosterEntry144.PositionRefWord),
			FlyerRosterEntry102 flyer => (flyer.PositionRef, FlyerRosterEntry102.PositionRefWord),
			BaseRosterEntry62 structure => (structure.PositionRef, BaseRosterEntry62.PositionRefWord),
			_ => throw new InvalidOperationException($"Row {row} is not a roster.")
		};

		short point = own != -1 && _source.Layout.Entry<MapPoint22>(own) != null ? own : (short)-1;
		int moving = entry.Supplier(word).CopiedFrom != null ? 0 : 1;
		_source.PlacePoint(point, moving, position.X, position.Y, position.Z, out short placed);
		if (placed != point) {
			_source.SetField(MsnField.RosterPosition(row), guid, placed);
		}
	}

	// How a group moves, as the layout has it: the point it stands on, its own or its route's first waypoint's, unless the
	// move detaches it or it stands on neither; else a point of its own. The Z written is the point's own unless a flyer
	// group's height moved: a flyer group at the point's Z 0 flies at 5000, and writing 5000 would lift every member whose
	// slot adds a height of its own.
	private (GroupMove Move, short Own, IReadOnlyList<short> Start, int Z) PlanGroup(GroupTarget target, Vec3i position) {
		var group = _index.Group(target.Guid) ?? throw new InvalidOperationException("Its record is not in the file.");
		short own = group.OwnPoint;
		var start = group.StartPoints;
		int pointZ = Point(own)?.Z ?? (start.Count > 0 ? Point(start[0])?.Z ?? 0 : 0);
		int z = HasOwnHeight(target) && position.Z != FlyerHeight(pointZ) ? position.Z : pointZ;
		var move = !target.Detach && own != -1 ? GroupMove.OwnPoint
			: !target.Detach && start.Count > 0 ? GroupMove.RouteStart
			: GroupMove.NewPoint;
		return (move, own, start, z);
	}

	// Where a group's drag stands it before the commit: the point it stands on at the drag's place, or, for a move that
	// gives it a point of its own, the group alone there.
	private PlacementOverrides GroupOverrides(GroupTarget target, Vec3i position) {
		var (move, own, start, z) = PlanGroup(target, position);
		var at = new Vec3i(position.X, position.Y, z);
		return move switch {
			GroupMove.OwnPoint => new PlacementOverrides { Points = new Dictionary<short, Vec3i> { [own] = at } },
			GroupMove.RouteStart => new PlacementOverrides { Points = start.Distinct().ToDictionary(guid => guid, _ => at) },
			_ => new PlacementOverrides { GroupPoint = (target.Guid, at) }
		};
	}

	// A group's move written into the .MSN.
	private void WriteGroup(GroupTarget target, Vec3i position) {
		var source = _source;
		var (move, own, start, z) = PlanGroup(target, position);
		switch (move) {
			case GroupMove.OwnPoint:
				source.SetPoint(own, position.X, position.Y, z);
				break;
			case GroupMove.RouteStart:
				foreach (short point in start.Distinct()) {
					source.SetPoint(point, position.X, position.Y, z);
				}

				break;
			default:
				source.PlacePoint(own, 1, position.X, position.Y, z, out short placed);
				if (placed != own) {
					source.SetField(MsnField.GroupPosition, target.Guid, placed);
				}

				break;
		}
	}

	// Row-6 GUID <guid>'s coordinates as the layout has them, or null.
	private Vec3i? Point(short guid) =>
		guid != -1 && _source.Layout.Entry<MapPoint22>(guid)?.Fields is { } point ? new Vec3i(point.X, point.Y, point.Z) : null;

	// Where a flyer group's first member flies for a point at height z (rule 5).
	private static int FlyerHeight(int z) => z == 0 ? FlyerObject.DefaultHoverHeight : z;

	private string Describe(MoveTarget target) => target switch {
		ObjectTarget { Key: var key } => _index.Plan.Object(key)?.TypeName ?? "the object",
		GroupTarget group => $"group {_index.Group(group.Guid)?.Number}",
		_ => _features.Describe(target)
	};

	// What a group's move writes: the point it stands on, its own or its route's first waypoint's, or a new one.
	private enum GroupMove {
		OwnPoint,
		RouteStart,
		NewPoint,
	}
}
