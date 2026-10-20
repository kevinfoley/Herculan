using System.Numerics;
using HercWorks.Core.Data.File.Msn;
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

/// <summary>
/// One step of <see cref="PointMover"/>'s undo history: a move, an add, a delete, a side change, any edit
/// (<see cref="RecordsEdit"/>), or a theater change. Each puts the <c>.MSN</c> back as the step found or left it
/// (<see cref="MissionDocument.Undo"/>, which generates <c>script.dat</c> again) and syncs the scene with it
/// (<see cref="SceneSync"/>); the history then levels the ground.
///
/// <para>A step that holds block indices keeps them valid without being told of later edits: the history is undone last
/// first, so a step is only ever undone or redone with the <c>.MSN</c> as it stood when the step was made, and the
/// document's one <see cref="MissionGeneration"/> generates the same indices from the same records.</para>
/// </summary>
internal interface IEditorEdit {
	void Undo();

	void Redo();
}

/// <summary>A placed object, by the roster record it stands for, moved through a point of its record's own.</summary>
internal sealed record ObjectTarget(ObjectKey Key) : MoveTarget;

/// <summary>
/// Waypoint <paramref name="Index"/> of <paramref name="Route"/>. <paramref name="WithLast"/> when it is a closed route's
/// first waypoint, whose last waypoint moves with it so the route stays closed (<see cref="EditorRoute.Closed"/>).
/// <paramref name="Detach"/> when the move gives it a point of its own rather than moving the point it names.
/// </summary>
internal sealed record WaypointTarget(EditorRoute Route, int Index, bool WithLast, bool Detach = false) : MoveTarget;

/// <summary>
/// Group <paramref name="Guid"/>, by its row-16 GUID, moved through the point it stands on: its own, else its slot-0
/// route's first waypoint. <paramref name="Detach"/> when the move gives it a point of its own rather than moving the
/// point it stands on.
/// </summary>
internal sealed record GroupTarget(short Guid, bool Detach = false) : MoveTarget;

/// <summary>
/// Corner <paramref name="Corner"/> of trigger area <paramref name="Area"/>, by block-4 record index: 0 a box's first corner
/// or a circle's centre (row 9's <c>0x08</c>), 1 a box's second corner (<c>0x0A</c>). <paramref name="Detach"/> when the
/// move gives the corner a point of its own rather than moving the point it names.
/// </summary>
internal sealed record AreaCornerTarget(int Area, int Corner, bool Detach = false) : MoveTarget;

/// <summary>
/// The radius of circle trigger area <paramref name="Area"/>, by block-4 record index, dragged by a point on its edge: the
/// radius is that point's ground-plane distance from the centre.
/// </summary>
internal sealed record AreaRadiusTarget(int Area) : MoveTarget;

/// <summary>
/// Moves what stands on a point — a placed object, a route's waypoint, a group or a trigger area's corner — and keeps an
/// undo history. A committed move writes the mission's <c>.MSN</c> (<see cref="MissionDocument"/>), which generates
/// <c>script.dat</c> again, and syncs the scene with it (<see cref="SceneSync"/>), as any edit does
/// (docs/herculan/handoff-editor-document.md#the-tools). A drag's preview writes no <c>.MSN</c>: the objects and groups
/// standing on what it moves are placed from the layout with the drag's overrides (<see cref="SceneSync.Preview"/>), and a
/// waypoint's or an area corner's preview writes the generated document's points, which the route and area lines follow
/// (<see cref="PointFollowers"/>) and the commit's generation replaces. After every move, previews included,
/// <see cref="TerrainLeveller"/> levels the ground under the structures where they now stand. Its history also holds the
/// edits made elsewhere (<see cref="Record(IEditorEdit)"/>), so that Undo and Redo step through every edit in the order it
/// was made.
///
/// <para>An object is given a point of its own (<see cref="MissionDocument.PlacePoint"/>), which its roster record's ref
/// then names and which wins over its group's, so the object alone moves
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 2). The height is the spawn pass's (rule 5
/// there): only a flyer's Z is read from the point, so only a flyer's is the mission's to set, and the point written
/// for anything else carries the height the simulator gives it (<see cref="Standing"/>). A point the roster record
/// draws from a variant is shared by every record drawing it, so the move appends one rather than rewrite it.</para>
///
/// <para>A waypoint moves the point it names, so everything else naming it moves too: retail missions share a point
/// between a waypoint and the trigger area that fires on reaching it, and between a route's first waypoint and the
/// groups standing on it. A detached move (<see cref="WaypointTarget.Detach"/>) gives it a point of its own instead.
/// Either way its point is written with Z 0, as all but one waypoint in the retail missions has it; the simulator reads
/// a waypoint's Z only as the starting altitude of a flight standing on its route's first waypoint and for the
/// heads-down map's route icons (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once).</para>
///
/// <para>A group moves as a waypoint does: the point it stands on — its own, else its slot-0 route's first waypoint, as the
/// layout has them (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 3) — moves, and whatever
/// shares it with it; a detached move gives the record a point of its own. Its members with no point of their own, a
/// paints-ground group's pad and, for group 0, the player's squad follow. The move is of the authored point, which a
/// paints-ground structure group's snap then puts in its tile. Only a flyer group's height is read from the point (rule 5),
/// so only a flyer group's is the mission's to set; any other keeps the point's Z.</para>
///
/// <para>A trigger area's corner, or a circle's centre, moves as a waypoint does: the point it names moves, and whatever
/// shares it, or a detached move gives it a point of its own, which its row-9 record then names
/// (<see cref="MissionDocument.SetArea"/>); its point is written with Z 0, which the area test ignores
/// (docs/retail/simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358). A
/// circle's radius moves through the same preview and commit, writing row 9's stored radius
/// (<see cref="TriggerAreaEditing.SetRadius"/>).</para>
/// </summary>
internal sealed class PointMover {
	// script.dat's blocks 1, 3 and 4, by number.
	private const int PointBlock = 1;
	private const int RouteBlock = 3;
	private const int AreaBlock = 4;

	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly GeneratedIndex _generated;
	private readonly SceneSync _sync;
	private readonly MissionOverlay _overlay;
	private PointFollowers _followers;
	private readonly TerrainLeveller _leveller;

	// Block 1 as it stood before a drag of a shared waypoint or area corner began writing into it, until the drag commits or
	// is cancelled.
	private ScriptCoordinate[]? _beforeDrag;

	// A dragged circle's stored radius as the drag found it, until the drag commits or is cancelled.
	private short? _radiusBeforeDrag;

	/// <param name="document">The generated <c>script.dat</c>: <paramref name="source"/>'s.</param>
	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	public PointMover(ScriptDat document, MissionDocument source, MissionScene scene, MissionIndex index, GeneratedIndex generated,
			SceneSync sync, MissionOverlay overlay, TerrainMaterialTable materials) {
		_document = document;
		_source = source;
		_terrain = scene.World.Terrain;
		_index = index;
		_generated = generated;
		_sync = sync;
		_overlay = overlay;
		_followers = new PointFollowers(document, source, generated, overlay);
		_leveller = new TerrainLeveller(scene, index, materials, overlay, sync.Place);
		LoadedBox = MissionBox.Of(scene.Mission.Coordinates);

		// The routes and areas the followers lay are the generated index's, built again by every sync.
		sync.Rebuilt += () => _followers = new PointFollowers(_document, _source, _generated, _overlay);
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
	/// What <paramref name="selection"/> names that can be moved: a movable object or group, by the record it stands for,
	/// or a waypoint, by its generated index (<see cref="GeneratedRecords"/>), none for one the generation has none for;
	/// else null.
	/// </summary>
	public MoveTarget? TargetOf(EditorSelection? selection) => selection switch {
		ObjectSelection { Row: var row, Guid: var guid } when WhyFixed(new ObjectKey(row, guid)) == null =>
			new ObjectTarget(new ObjectKey(row, guid)),
		RouteSelection { Waypoint: >= 0 } waypoint when _generated.Records.Route(waypoint.Guid) is { } reference
				&& _generated.Route(reference) is { } route
				&& waypoint.Waypoint < route.Points.Count && _followers.WaypointEntries(route.Reference) != null =>
			new WaypointTarget(route, waypoint.Waypoint, waypoint.Waypoint == 0 && route.Closed),
		GroupSelection { Guid: var guid } when WhyFixedGroup(guid) == null => new GroupTarget(guid),
		_ => null
	};

	/// <summary>
	/// Everything of <paramref name="selection"/> the move handles drag: a trigger area's corners, or a circle's centre and
	/// radius, the radius first, so its small handle wins where the centre's arrow crosses it; else <see cref="TargetOf"/>'s
	/// one target, or none.
	/// </summary>
	public IReadOnlyList<MoveTarget> TargetsOf(EditorSelection? selection) {
		if (selection is AreaSelection { Guid: var guid }) {
			if (_generated.Records.Area(guid) is not { } reference || WhyFixedArea(reference) != null
					|| _generated.Area(reference) is not { } area) {
				return [];
			}

			return area.Shape == MissionTriggerShape.Circle
				? [new AreaRadiusTarget(reference), new AreaCornerTarget(reference, 0)]
				: [new AreaCornerTarget(reference, 0), new AreaCornerTarget(reference, 1)];
		}

		return TargetOf(selection) is { } target ? [target] : [];
	}

	/// <summary>Why trigger area <paramref name="area"/>, by block-4 index, cannot be moved or edited, or null when it can.</summary>
	public string? WhyFixedArea(int area) =>
		_generated.Area(area) == null || _source.GuidOf(AreaBlock, area) == -1
			? $"Trigger area {area}'s record is not in the file."
			: null;

	/// <summary>
	/// How many other fields name the point <paramref name="target"/>'s corner names — waypoints, other areas, groups —
	/// which move with it unless it is detached.
	/// </summary>
	public int SharersOf(AreaCornerTarget target) => PointNamers(ReadRefs(target)[0]) - 1;

	/// <summary>
	/// Sets circle trigger area <paramref name="area"/>'s radius to <paramref name="radius"/> world units, as one step of the
	/// undo history, as a drag of its radius handle does.
	/// </summary>
	public void CommitRadius(int area, int radius) {
		if (_generated.Area(area) is not { } circle) {
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
	/// How many other fields name the point <paramref name="target"/>'s waypoint names — trigger areas, groups, other
	/// waypoints — which move with it unless it is detached.
	/// </summary>
	public int SharersOf(WaypointTarget target) {
		var refs = ReadRefs(target);
		return refs.Distinct().Sum(PointNamers) - refs.Length;
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

	// How many fields name block-1 point <reference>: every .MSN record naming its point's GUID, whatever its condition.
	private int PointNamers(short reference) => _source.CountPointRefs(PointGuid(reference));

	/// <summary>Whether a point's Z is the mission's to set for <paramref name="sceneObject"/>: only a flyer's is.</summary>
	public static bool HasOwnHeight(SceneObject sceneObject) => sceneObject.Object is FlyerObject;

	/// <summary>Whether <paramref name="target"/> takes a height: a flyer or a flyer group alone.</summary>
	public bool HasOwnHeight(MoveTarget target) => target switch {
		ObjectTarget { Key: var key } => _index.ObjectOf(key) is { } placed && HasOwnHeight(placed),
		GroupTarget group => _index.Group(group.Guid)?.Kind == MissionUnitKind.Flyer,
		_ => false
	};

	/// <summary>
	/// Where <paramref name="target"/> stands now, in world units. A group's is its authored point, before a paints-ground
	/// snap; a flyer group's height is where its first member flies, 5000 for a point at Z 0 (rule 5).
	/// </summary>
	public Vec3i PositionOf(MoveTarget target) => target switch {
		ObjectTarget { Key: var key } when _index.ObjectOf(key) is { } placed => placed.Object.Position,
		WaypointTarget waypoint => waypoint.Route.Points[waypoint.Index],
		GroupTarget { Guid: var guid } when _index.Group(guid) is { Setup.AuthoredPoint: var point } group =>
			group.Kind == MissionUnitKind.Flyer ? new Vec3i(point.X, point.Y, FlyerHeight(point.Z)) : point,
		AreaCornerTarget corner when _generated.Area(corner.Area) is { } area =>
			corner.Corner == 0 ? new Vec3i(area.A.X, area.A.Y, 0) : new Vec3i(area.B.X, area.B.Y, 0),
		AreaRadiusTarget radius when _generated.Area(radius.Area) is { } area => new Vec3i(area.A.X + area.Radius, area.A.Y, 0),
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	/// <summary>
	/// Where the move handles sit, in render space: on an object's position or a flyer group's first member, or on a
	/// waypoint's or another group's marker, which is drawn on the ground under it.
	/// </summary>
	public Vector3 HandleOrigin(MoveTarget target) {
		var position = PositionOf(target);
		return target is WaypointTarget or AreaCornerTarget or AreaRadiusTarget || (target is GroupTarget && !HasOwnHeight(target))
			? WorldScale.ToRender(new Vec3i(position.X, position.Y, _terrain.HeightAtWorld(position.X, position.Y) + DrapedLines.LiftWorld))
			: WorldScale.ToRender(position);
	}

	/// <summary>
	/// Where <paramref name="target"/> stands for a point at (<paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="z"/>): an object where the simulator stands it, <paramref name="z"/> read for a flyer alone; a
	/// waypoint at Z 0; a group at <paramref name="z"/> when it is a flyer group, else at its point's own Z.
	/// </summary>
	public Vec3i Standing(MoveTarget target, int x, int y, int z) => target switch {
		ObjectTarget { Key: var key } when _index.ObjectOf(key) is { } placed => PointFollowers.Standing(_terrain, placed, x, y, z),
		WaypointTarget or AreaCornerTarget or AreaRadiusTarget => new Vec3i(x, y, 0),
		GroupTarget group => new Vec3i(x, y, HasOwnHeight(group) ? z : PositionOf(group).Z),
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
	/// Stands <paramref name="target"/> at <paramref name="position"/> as a drag does before it lets go, writing no
	/// <c>.MSN</c> until <see cref="Commit"/>: the objects and groups standing on what moves are placed from the layout with
	/// the drag's overrides; a waypoint or area corner moving its shared point writes the generated document, so the route
	/// and area lines on it follow, and <see cref="Commit"/> or <see cref="Cancel"/> settles it; a detached one moves for the
	/// eye alone.
	/// </summary>
	public void Preview(MoveTarget target, Vec3i position) {
		switch (target) {
			case WaypointTarget { Detach: false } or AreaCornerTarget { Detach: false }:
				_beforeDrag ??= Snapshot(_document.Coordinates);
				WritePoints(target, position);
				_followers.Sync();
				_sync.Preview(new PlacementOverrides {
					Points = ReadRefs(target).Select(PointGuid).Where(guid => guid != -1).Distinct()
						.ToDictionary(guid => guid, _ => position)
				});
				break;
			case AreaRadiusTarget radius:
				var record = _document.TriggerAreas[radius.Area];
				_radiusBeforeDrag ??= record.SecondPointOrRadius;
				record.SecondPointOrRadius = TriggerAreaEditing.StoredRadius(RadiusAt(radius, position));
				_followers.Sync();
				break;
			case GroupTarget group:
				var overrides = GroupOverrides(group, position);
				_followers.Sync(overrides);
				_sync.Preview(overrides);
				break;
			case ObjectTarget { Key: var key }:
				_sync.Preview(new PlacementOverrides { ObjectPoint = (key, position) });
				break;
			default:
				Stand(target, position);
				break;
		}

		_leveller.Relevel();
	}

	/// <summary>Puts back what <see cref="Preview"/> moved, for a drag that went nowhere.</summary>
	public void Cancel(MoveTarget target, Vec3i before) {
		if (_beforeDrag is { } points) {
			_document.Coordinates = points;
			_beforeDrag = null;
			_followers.Sync();
			_sync.Preview(null);
		} else if (TakeRadiusPreview(target)) {
			_followers.Sync();
		} else if (target is GroupTarget or ObjectTarget) {
			_followers.Sync();
			_sync.Preview(null);
		} else {
			Stand(target, before);
		}

		_leveller.Relevel();
	}

	// Puts back the stored radius a radius drag's preview wrote into the generated document, and says whether there was one.
	private bool TakeRadiusPreview(MoveTarget target) {
		if (_radiusBeforeDrag is not { } stored) {
			return false;
		}

		if (target is AreaRadiusTarget radius) {
			_document.TriggerAreas[radius.Area].SecondPointOrRadius = stored;
		}

		_radiusBeforeDrag = null;
		return true;
	}

	// The radius, in world units, a radius handle at <position> gives: its ground-plane distance from the centre.
	private int RadiusAt(AreaRadiusTarget target, Vec3i position) {
		var centre = _generated.Area(target.Area)?.A ?? position;
		double dx = (double)position.X - centre.X, dy = (double)position.Y - centre.Y;
		return (int)Math.Min(Math.Round(Math.Sqrt(dx * dx + dy * dy)), int.MaxValue);
	}

	/// <summary>
	/// Moves <paramref name="target"/> to (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) for good,
	/// as one step of the undo history: writes the <c>.MSN</c>, which generates <c>script.dat</c> again, and syncs the
	/// scene. <paramref name="before"/> is where it stood before any <see cref="Preview"/> of this move.
	/// </summary>
	public void Commit(MoveTarget target, Vec3i before, int x, int y, int z) {
		if (target is ObjectTarget { Key: var key } && WhyFixed(key) != null
				|| target is GroupTarget { Guid: var fixedGroup } && WhyFixedGroup(fixedGroup) != null
				|| target is AreaCornerTarget { Area: var cornerArea } && WhyFixedArea(cornerArea) != null
				|| target is AreaRadiusTarget { Area: var radiusArea } && WhyFixedArea(radiusArea) != null) {
			Cancel(target, before);
			return;
		}

		var position = Standing(target, x, y, z);

		// What the preview wrote goes, so the refs read below are the generated ones; the generation after the write
		// replaces the blocks again.
		if (_beforeDrag is { } unmoved) {
			_document.Coordinates = unmoved;
		}

		_beforeDrag = null;
		TakeRadiusPreview(target);
		if (target is AreaRadiusTarget radius
				&& TriggerAreaEditing.StoredRadius(RadiusAt(radius, position)) == _document.TriggerAreas[radius.Area].SecondPointOrRadius) {
			// Within the stored radius's tens of units of where it was: nothing to write.
			_followers.Sync();
			return;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => _source.Change(() => Write(target, position)));
		} catch (InvalidOperationException ex) {
			Console.Error.WriteLine($"Cannot move {Describe(target)}: {ex.Message}");
			_followers.Sync();
			_sync.Preview(null);
			if (target is WaypointTarget { Detach: true } or AreaCornerTarget { Detach: true }) {
				Stand(target, before);
			}

			_leveller.Relevel();
			return;
		}

		Record(new RecordsEdit(_sync, change));
	}

	/// <summary>
	/// The block-3 list entries route <paramref name="route"/>'s waypoints came from, in order, or null when they cannot be
	/// told apart (<see cref="PointFollowers.WaypointEntries"/>).
	/// </summary>
	public int[]? WaypointEntries(int route) => _followers.WaypointEntries(route);

	public void Undo() => Step(History.TakeUndo(), undo: true);

	public void Redo() => Step(History.TakeRedo(), undo: false);

	/// <summary>
	/// Adds an edit made elsewhere, already applied, to the history as one step, and levels after it as after a move.
	/// </summary>
	public void Record(IEditorEdit edit) {
		History.Add(edit);
		Settle();
	}

	// Lays the route and area lines on the generated points again, rebuilds the mission box overlay and levels the ground,
	// as after every step.
	private void Settle() {
		_followers.Sync();
		_overlay.SetBox(MissionBox.Of(Points()));
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

		Settle();
	}

	// Writes a waypoint's or an area corner's new position into each point it names, in the generated document, for a preview.
	private void WritePoints(MoveTarget target, Vec3i position) {
		foreach (short reference in ReadRefs(target).Distinct()) {
			var point = _document.Coordinates[reference];
			point.X = position.X;
			point.Y = position.Y;
			point.Z = position.Z;
		}
	}

	// Writes a move into the .MSN: a shared waypoint's or area corner's point records; a group's (WriteGroup); a circle's
	// stored radius; an object's own point (GiveObjectPoint); or a point of its own for a detached waypoint or a detached
	// corner (GiveOwnPoint).
	private void Write(MoveTarget target, Vec3i position) {
		switch (target) {
			case WaypointTarget { Detach: false } or AreaCornerTarget { Detach: false }:
				foreach (short reference in ReadRefs(target).Distinct()) {
					_source.SetPoint(PointGuid(reference), position.X, position.Y, position.Z);
				}

				break;
			case GroupTarget group:
				WriteGroup(group, position);
				break;
			case AreaRadiusTarget radius:
				TriggerAreaEditing.SetRadius(_source, _source.GuidOf(AreaBlock, radius.Area), RadiusAt(radius, position));
				break;
			case ObjectTarget placed:
				GiveObjectPoint(placed, position);
				break;
			default:
				GiveOwnPoint(target, position);
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

	// Gives a detached waypoint or corner a point of its own at position, and points its field at it: the route's list
	// entries, or the area's corner. A closed route's first and last waypoints name one point between them more often than
	// not, and move as one. A list a variant supplies names points every route drawing that variant shares, so none of its
	// namers counts as moving and a point is appended.
	private void GiveOwnPoint(MoveTarget target, Vec3i position) {
		var source = _source;
		short[] refs = ReadRefs(target);
		short[] points = Array.ConvertAll(refs, PointGuid);
		int moving = FromVariant(target) ? 0 : 1;
		short[] placed;
		if (refs.Length > 1 && refs.All(reference => reference == refs[0])) {
			source.PlacePoint(points[0], moving * refs.Length, position.X, position.Y, position.Z, out short one);
			placed = Array.ConvertAll(refs, _ => one);
		} else {
			placed = new short[refs.Length];
			for (int i = 0; i < refs.Length; i++) {
				source.PlacePoint(points[i], moving, position.X, position.Y, position.Z, out placed[i]);
			}
		}

		switch (target) {
			case WaypointTarget waypoint:
				int[] entries = WaypointEntries(waypoint);
				short route = source.GuidOf(RouteBlock, waypoint.Route.Reference);
				for (int i = 0; i < entries.Length; i++) {
					if (placed[i] != points[i]) {
						source.SetWaypoint(route, entries[i], placed[i]);
					}
				}

				break;
			case AreaCornerTarget corner when placed[0] != points[0]:
				short own = placed[0];
				source.SetArea(source.GuidOf(AreaBlock, corner.Area), record => {
					if (corner.Corner == 0) {
						record.PointRef = own;
					} else {
						record.SecondPointOrRadius = own;
					}
				});
				break;
		}
	}

	// Whether a route's list is copied from a variant in the record an edit writes (MissionDocument.SuppliedByVariant).
	private bool FromVariant(MoveTarget target) =>
		target is WaypointTarget waypoint
		&& _source.Layout.Entry(RouteBlock + 5, _source.GuidOf(RouteBlock, waypoint.Route.Reference))
			?.Supplier(MergeRules.RouteListWord).CopiedFrom != null;

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

	// The row-6 GUID of block-1 point <reference>, or -1 for none.
	private short PointGuid(short reference) => _source.GuidOf(PointBlock, reference);

	// Where a flyer group's first member flies for a point at height z (rule 5).
	private static int FlyerHeight(int z) => z == 0 ? FlyerObject.DefaultHoverHeight : z;

	private void Stand(MoveTarget target, Vec3i position) {
		switch (target) {
			case WaypointTarget waypoint:
				waypoint.Route.Move(waypoint.Index, position);
				if (waypoint.WithLast) {
					waypoint.Route.Move(waypoint.Route.Points.Count - 1, position);
				}

				_overlay.MoveRoute(waypoint.Route);
				break;
			case AreaCornerTarget corner when _generated.Area(corner.Area) is { } area:
				var moved = corner.Corner == 0
					? area with { A = position, B = area.Shape == MissionTriggerShape.Circle ? position : area.B }
					: area with { B = position };
				_generated.SetArea(moved);
				_overlay.MoveArea(moved);
				break;
		}
	}

	private string Describe(MoveTarget target) => target switch {
		ObjectTarget { Key: var key } => _index.ObjectOf(key) is { } placed ? MissionText.Describe(placed) : "the object",
		WaypointTarget waypoint => $"waypoint {waypoint.Index} of route {waypoint.Route.Reference}",
		GroupTarget group => $"group {_index.Group(group.Guid)?.Number}",
		AreaCornerTarget corner => $"corner {corner.Corner} of trigger area {corner.Area}",
		AreaRadiusTarget radius => $"the radius of trigger area {radius.Area}",
		_ => target.ToString()
	};

	private List<Vec3i> Points() => _document.Coordinates.Select(point => new Vec3i(point.X, point.Y, point.Z)).ToList();

	// The block-1 refs naming a waypoint's or corner's point: its block-3 entry, two for a closed route's first waypoint,
	// which is also its last; or its row-9 field.
	private short[] ReadRefs(MoveTarget target) => target switch {
		WaypointTarget waypoint => Array.ConvertAll(WaypointEntries(waypoint),
			entry => _document.WaypointGroups[waypoint.Route.Reference].Waypoints[entry]),
		AreaCornerTarget corner => new[] {
			corner.Corner == 0 ? _document.TriggerAreas[corner.Area].PointRef : _document.TriggerAreas[corner.Area].SecondPointOrRadius
		},
		_ => throw new ArgumentOutOfRangeException(nameof(target))
	};

	private int[] WaypointEntries(WaypointTarget waypoint) {
		int[] entries = _followers.WaypointEntries(waypoint.Route.Reference)!;
		return waypoint.WithLast ? new[] { entries[0], entries[^1] } : new[] { entries[waypoint.Index] };
	}

	// Block 1 as a drag found it, copied whole: a few hundred points at most, and a copy cannot be changed by the
	// preview's writes in place.
	private static ScriptCoordinate[] Snapshot(ScriptCoordinate[] points) =>
		Array.ConvertAll(points, point => new ScriptCoordinate { X = point.X, Y = point.Y, Z = point.Z });

	// What a group's move writes: the point it stands on, its own or its route's first waypoint's, or a new one.
	private enum GroupMove {
		OwnPoint,
		RouteStart,
		NewPoint,
	}
}
