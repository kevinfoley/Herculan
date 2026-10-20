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
/// One step of <see cref="PointMover"/>'s undo history that is not a move — a side change (<see cref="SideChanger"/>),
/// or an add or a delete (<see cref="RecordsEdit"/>). Each puts the <c>.MSN</c> back as the step found or left it
/// (<see cref="MissionDocument.Undo"/>, which generates <c>script.dat</c> again), and the index and the scene with it;
/// the history then places again what stands on the generated points and levels the ground.
///
/// <para>A step that holds block indices — a move's target, a side change's group — keeps them valid without being
/// told of later edits: the history is undone last first, so a step is only ever undone or redone with the <c>.MSN</c> as
/// it stood when the step was made, and the document's one <see cref="MissionGeneration"/> generates the same indices from
/// the same records. Every step that adds or removes records rebuilds the editor's index from the generated file as it is
/// undone or redone (<see cref="MissionRebuild"/>), so the index the step reads is that generation's.</para>
/// </summary>
internal interface IEditorEdit {
	void Undo();

	void Redo();
}

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
/// Moves what stands on a <c>script.dat</c> block-1 point — a placed object, a route's waypoint or a group — and keeps an
/// undo history. A committed move writes the mission's <c>.MSN</c> (<see cref="MissionDocument"/>), which generates
/// <c>script.dat</c> again into the document the editor reads; a drag's preview writes that generated document alone,
/// and the commit's generation replaces what it wrote
/// (docs/herculan/handoff-editor-document.md#the-tools). After every change to
/// the document's points, <see cref="PointFollowers"/> places again whatever else stands on them, and after every move,
/// previews included, <see cref="TerrainLeveller"/> levels the ground under the structures where they now stand. Its
/// history also holds the edits made elsewhere (<see cref="Record(IEditorEdit)"/>), so that Undo and Redo step through
/// every edit in the order it was made. A mission opened from a plain <c>script.dat</c> has no <c>.MSN</c> and moves
/// nothing (<see cref="WhyReadOnly"/>).
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
/// <para>A group moves as a waypoint does: the point it stands on — its block-11 record's own, else its slot-0 route's
/// first waypoint (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 3) — moves, and whatever
/// shares it with it; a detached move gives the record a point of its own. Its members with no point of their own, a
/// paints-ground group's pad and, for group 0, the player's squad follow (<see cref="PointFollowers"/>). The move is of
/// the authored point, which a paints-ground structure group's snap then puts in its tile. Only a flyer group's height
/// is read from the point (rule 5), so only a flyer group's is the mission's to set; any other keeps the point's Z.</para>
///
/// <para>A trigger area's corner, or a circle's centre, moves as a waypoint does: the point it names moves, and whatever
/// shares it, or a detached move gives it a point of its own, which its row-9 record then names
/// (<see cref="MissionDocument.SetArea"/>); its point is written with Z 0, which the area test ignores
/// (docs/retail/simulation/mission-deployment.md#the-areas--block-4-resolved-by-triggerarea_resolve-00423358). A
/// circle's radius moves through the same preview and commit, writing row 9's stored radius
/// (<see cref="TriggerAreaEditing.SetRadius"/>).</para>
/// </summary>
internal sealed class PointMover {
	// script.dat's blocks 1, 3, 4, 7 to 9 and 11, by number.
	private const int PointBlock = 1;
	private const int RouteBlock = 3;
	private const int AreaBlock = 4;
	private const int GroupBlock = 11;

	// Row 8's word 4, its waypoint count and the list after it, as SlotProvenance.Word takes it.
	private const int RouteListWord = 4;

	private readonly ScriptDat _document;
	private readonly MissionDocument? _source;
	private readonly MissionScene _scene;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;
	private readonly ScenePicker _picker;
	private readonly MissionOverlay _overlay;
	private PointFollowers _followers;
	private readonly TerrainLeveller _leveller;

	// Block 1 as it stood before a drag of a shared point or a group began writing into it, and the dragged group's
	// ref, until the drag commits or is cancelled.
	private ScriptCoordinate[]? _beforeDrag;
	private short[]? _refsBeforeDrag;

	// A dragged circle's stored radius as the drag found it, until the drag commits or is cancelled.
	private short? _radiusBeforeDrag;

	/// <param name="document">The <c>script.dat</c> the scene was loaded from: <paramref name="source"/>'s generated one, when there is a source.</param>
	/// <param name="source">The mission as its <c>.MSN</c>, or null for a plain <c>script.dat</c>, which is not edited.</param>
	public PointMover(ScriptDat document, MissionDocument? source, MissionScene scene, MissionIndex index, ScenePicker picker,
			MissionOverlay overlay, TerrainMaterialTable materials) {
		_document = document;
		_source = source;
		_scene = scene;
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

	/// <summary>Why nothing in a mission opened from a plain <c>script.dat</c> is edited.</summary>
	public const string ReadOnlyReason = "Opened read-only: a plain script.dat has no .MSN to edit. File > Load from VOL "
		+ "opens a mission the editor can change.";

	/// <summary>Why the mission cannot be edited at all, or null when it can.</summary>
	public string? WhyReadOnly => _source == null ? ReadOnlyReason : null;

	/// <summary>The undo history, which also says whether the mission has changed since it was last saved or opened.</summary>
	public EditHistory History { get; } = new();

	public bool CanUndo => History.CanUndo;

	public bool CanRedo => History.CanRedo;

	/// <summary>What <paramref name="selection"/> names that can be moved: a movable object, a waypoint or a group, else null.</summary>
	public MoveTarget? TargetOf(EditorSelection? selection) => selection switch {
		ObjectSelection { Object: var selected } when WhyFixed(selected) == null => new ObjectTarget(selected),
		RouteSelection { Waypoint: >= 0 } waypoint when _source != null && _index.Route(waypoint.Route) is { } route
				&& waypoint.Waypoint < route.Points.Count && _followers.WaypointEntries(route.Reference) != null =>
			new WaypointTarget(route, waypoint.Waypoint, waypoint.Waypoint == 0 && route.Closed),
		GroupSelection { Group: var group } when WhyFixed(group) == null => new GroupTarget(group),
		_ => null
	};

	/// <summary>
	/// Everything of <paramref name="selection"/> the move handles drag: a trigger area's corners, or a circle's centre and
	/// radius, the radius first, so its small handle wins where the centre's arrow crosses it; else <see cref="TargetOf"/>'s
	/// one target, or none.
	/// </summary>
	public IReadOnlyList<MoveTarget> TargetsOf(EditorSelection? selection) {
		if (selection is AreaSelection { Area: var reference }) {
			if (WhyFixedArea(reference) != null || _index.Area(reference) is not { } area) {
				return [];
			}

			return area.Shape == MissionTriggerShape.Circle
				? [new AreaRadiusTarget(reference), new AreaCornerTarget(reference, 0)]
				: [new AreaCornerTarget(reference, 0), new AreaCornerTarget(reference, 1)];
		}

		return TargetOf(selection) is { } target ? [target] : [];
	}

	/// <summary>Why trigger area <paramref name="area"/>, by block-4 index, cannot be moved or edited, or null when it can.</summary>
	public string? WhyFixedArea(int area) {
		if (WhyReadOnly is { } readOnly) {
			return readOnly;
		}

		return _index.Area(area) == null || _source!.GuidOf(AreaBlock, area) == -1
			? $"Trigger area {area}'s record is not in the file."
			: null;
	}

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
		if (_index.Area(area) is not { } circle) {
			return;
		}

		var target = new AreaRadiusTarget(area);
		Commit(target, PositionOf(target), circle.A.X + radius, circle.A.Y, 0);
	}

	/// <summary>
	/// Why <paramref name="sceneObject"/> cannot be given a point of its own, or null when it can. The player's squad
	/// is not in the rosters: each <c>player.mec</c> entry is placed from block 11's record 0
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 6).
	/// </summary>
	public string? WhyFixed(SceneObject sceneObject) {
		var placement = sceneObject.Placement;
		if (WhyReadOnly is { } readOnly) {
			return readOnly;
		}

		if (placement.IsPlayerLance) {
			return "The player's squad stands at group 0's point; it has no roster record of its own. Move group 0 to move it.";
		}

		return RecordRef(placement.Kind, placement.SlotIndex) == null
				|| _source!.GuidOf(RosterBlock(placement.Kind), placement.SlotIndex) == -1
			? $"Its {placement.Kind.ToString().ToLowerInvariant()} roster slot {placement.SlotIndex} is not in the file."
			: null;
	}

	/// <summary>
	/// Why group <paramref name="group"/> cannot be moved, or null when it can. A structure group that paints its ground
	/// with no structure placed has no pad to snap by and nothing standing on its point.
	/// </summary>
	public string? WhyFixed(int group) {
		if (WhyReadOnly is { } readOnly) {
			return readOnly;
		}

		if (group < 0 || group >= _document.Groups.Length || _index.SetupOf(group) is not { } setup
				|| _source!.GuidOf(GroupBlock, group) == -1) {
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
		return refs.Distinct().Sum(PointNamers) - refs.Length;
	}

	/// <summary>
	/// How many other fields name the point <paramref name="target"/>'s group stands on — trigger areas, other groups,
	/// waypoints — which move with it unless it is detached. For a group standing on its route's first waypoint, these
	/// are besides that waypoint (<see cref="SharersOf(WaypointTarget)"/>).
	/// </summary>
	public int SharersOf(GroupTarget target) {
		short own = _document.Groups[target.Group].PositionRef;
		if (Coordinate(own) != null) {
			return PointNamers(own) - 1;
		}

		return StartOf(target.Group) is { } start ? SharersOf(start) : 0;
	}

	// How many fields name block-1 point <reference>: every .MSN record naming its point's GUID, whatever its condition,
	// or, for a plain script.dat, its own fields.
	private int PointNamers(short reference) =>
		_source != null ? _source.CountPointRefs(_source.GuidOf(PointBlock, reference)) : ScriptPointRefs.Count(_document, reference);

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
		AreaCornerTarget corner when _index.Area(corner.Area) is { } area =>
			corner.Corner == 0 ? new Vec3i(area.A.X, area.A.Y, 0) : new Vec3i(area.B.X, area.B.Y, 0),
		AreaRadiusTarget radius when _index.Area(radius.Area) is { } area => new Vec3i(area.A.X + area.Radius, area.A.Y, 0),
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
		ObjectTarget { Object: var sceneObject } => PointFollowers.Standing(_terrain, sceneObject, x, y, z),
		WaypointTarget or AreaCornerTarget or AreaRadiusTarget => new Vec3i(x, y, 0),
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
	/// Whether group <paramref name="group"/> stands on a block-1 point, its own or its route's first waypoint's
	/// (<see cref="StartOf"/>), rather than at the origin.
	/// </summary>
	public bool StandsOnPoint(int group) => Coordinate(_document.Groups[group].PositionRef) != null || StartOf(group) != null;

	/// <summary>
	/// Stands <paramref name="target"/> at <paramref name="position"/> as a drag does before it lets go. An object or a
	/// detached waypoint moves for the eye alone, the document unchanged until <see cref="Commit"/>; a waypoint moving
	/// its shared point, or a group, writes into the generated document, so what follows it does, and <see cref="Commit"/>
	/// or <see cref="Cancel"/> settles it.
	/// </summary>
	public void Preview(MoveTarget target, Vec3i position) {
		if (target is WaypointTarget { Detach: false } or AreaCornerTarget { Detach: false }) {
			_beforeDrag ??= Snapshot(_document.Coordinates);
			WritePoints(target, position);
			_followers.Sync();
		} else if (target is AreaRadiusTarget radius) {
			var record = _document.TriggerAreas[radius.Area];
			_radiusBeforeDrag ??= record.SecondPointOrRadius;
			record.SecondPointOrRadius = TriggerAreaEditing.StoredRadius(RadiusAt(radius, position));
			_followers.Sync();
		} else if (target is GroupTarget group) {
			// Each step starts again from the document as the drag found it, so a detached group appends one point at
			// most, however far it is dragged.
			_beforeDrag ??= Snapshot(_document.Coordinates);
			_refsBeforeDrag ??= ReadRefs(group);
			_document.Coordinates = Snapshot(_beforeDrag);
			WriteRefs(group, _refsBeforeDrag);
			try {
				PreviewGroup(group, position);
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
		} else if (TakeRadiusPreview(target)) {
			_followers.Sync();
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
		var centre = _index.Area(target.Area)?.A ?? position;
		double dx = (double)position.X - centre.X, dy = (double)position.Y - centre.Y;
		return (int)Math.Min(Math.Round(Math.Sqrt(dx * dx + dy * dy)), int.MaxValue);
	}

	/// <summary>
	/// Moves <paramref name="target"/> to (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) for good,
	/// as one step of the undo history: writes the <c>.MSN</c> and generates <c>script.dat</c> again.
	/// <paramref name="before"/> is where it stood before any <see cref="Preview"/> of this move.
	/// </summary>
	public void Commit(MoveTarget target, Vec3i before, int x, int y, int z) {
		if (_source == null || target is ObjectTarget { Object: var sceneObject } && WhyFixed(sceneObject) != null
				|| target is GroupTarget { Group: var fixedGroup } && WhyFixed(fixedGroup) != null
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
			if (_refsBeforeDrag is { } refs) {
				WriteRefs(target, refs);
			}
		}

		_beforeDrag = null;
		_refsBeforeDrag = null;
		TakeRadiusPreview(target);
		if (target is AreaRadiusTarget radius
				&& TriggerAreaEditing.StoredRadius(RadiusAt(radius, position)) == _document.TriggerAreas[radius.Area].SecondPointOrRadius) {
			// Within the stored radius's tens of units of where it was: nothing to write.
			_followers.Sync();
			return;
		}

		MsnEdit change;
		try {
			change = _source.Change(() => Write(target, position));
		} catch (InvalidOperationException ex) {
			Console.Error.WriteLine($"Cannot move {Describe(target)}: {ex.Message}");
			_followers.Sync();
			if (target is ObjectTarget or WaypointTarget { Detach: true } or AreaCornerTarget { Detach: true }) {
				Stand(target, before);
			}

			_leveller.Relevel();
			return;
		}

		if (target is ObjectTarget or WaypointTarget { Detach: true } or AreaCornerTarget { Detach: true }) {
			Stand(target, position);
		}

		Record(new Edit(this, target, change, before, position));
	}

	/// <summary>
	/// The block-3 list entries route <paramref name="route"/>'s waypoints came from, in order, or null when they cannot be
	/// told apart (<see cref="PointFollowers.WaypointEntries"/>).
	/// </summary>
	public int[]? WaypointEntries(int route) => _followers.WaypointEntries(route);

	public void Undo() => Step(History.TakeUndo(), undo: true);

	/// <summary>
	/// Builds again what places followers from the index (<see cref="PointFollowers"/>), for an index built again
	/// (<see cref="MissionIndex.Reload"/>).
	/// </summary>
	public void Rebuild() => _followers = new PointFollowers(_document, _scene, _index, _overlay, Place);

	public void Redo() => Step(History.TakeRedo(), undo: false);

	/// <summary>
	/// Adds an edit made elsewhere, already applied, to the history as one step, and places and levels after it as
	/// after a move.
	/// </summary>
	public void Record(IEditorEdit edit) {
		History.Add(edit);
		Settle();
	}

	// Places again what stands on the generated points, rebuilds the mission box overlay and levels the ground, as after
	// every step.
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

	// Puts the .MSN back as one side of a move left it, which generates the document again.
	private void Restore(MoveTarget moved, MsnEdit change, bool undo, Vec3i position) {
		if (undo) {
			_source!.Undo(change);
		} else {
			_source!.Redo(change);
		}

		_followers.Sync();

		// An object's own move is the one thing the followers do not place: undone, its record names no point again
		// and it stands where its group puts it.
		if (moved is ObjectTarget) {
			Stand(moved, position);
		}
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
	// stored radius; or a point of its own for an object, a detached waypoint or a detached corner (GiveOwnPoint).
	private void Write(MoveTarget target, Vec3i position) {
		switch (target) {
			case WaypointTarget { Detach: false } or AreaCornerTarget { Detach: false }:
				foreach (short reference in ReadRefs(target).Distinct()) {
					_source!.SetPoint(PointGuid(reference), position.X, position.Y, position.Z);
				}

				break;
			case GroupTarget group:
				WriteGroup(group, position);
				break;
			case AreaRadiusTarget radius:
				TriggerAreaEditing.SetRadius(_source!, _source!.GuidOf(AreaBlock, radius.Area), RadiusAt(radius, position));
				break;
			default:
				GiveOwnPoint(target, position);
				break;
		}
	}

	// Gives an object, or a detached waypoint, a point of its own at position, and points its field at it: the roster
	// record's own point, or the route's list entries. A closed route's first and last waypoints name one point between
	// them more often than not, and move as one. A field a variant supplies names a point every record drawing that
	// variant shares, so none of its namers counts as moving and a point is appended.
	private void GiveOwnPoint(MoveTarget target, Vec3i position) {
		var source = _source!;
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
			case ObjectTarget { Object.Placement: var placement } when placed[0] != points[0]:
				source.SetField(MsnField.RosterPosition(RosterBlock(placement.Kind) + 5),
					source.GuidOf(RosterBlock(placement.Kind), placement.SlotIndex), placed[0]);
				break;
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

	// Whether the field naming the target's point is copied from a variant: an object's roster point, or a route's list.
	private bool FromVariant(MoveTarget target) {
		var (block, index, word) = target switch {
			ObjectTarget { Object.Placement: var placement } => (RosterBlock(placement.Kind), placement.SlotIndex,
				placement.Kind switch {
					MissionUnitKind.Mech => MechRosterEntry144.PositionRefWord,
					MissionUnitKind.Flyer => FlyerRosterEntry102.PositionRefWord,
					_ => BaseRosterEntry62.PositionRefWord
				}),
			WaypointTarget waypoint => (RouteBlock, waypoint.Route.Reference, RouteListWord),
			_ => (0, 0, 0)
		};
		return block > 0 && _source!.Provenance.Slot(block, index)?.Word(word).CopiedFrom != null;
	}

	// How a group moves, as the generated document stands: the point it stands on, its own or its route's first
	// waypoint's, unless the move detaches it or it stands on neither; else a point of its own. The Z written is the
	// point's own unless a flyer group's height moved: a flyer group at the point's Z 0 flies at 5000, and writing 5000
	// would lift every member whose slot adds a height of its own.
	private (GroupMove Move, short Own, WaypointTarget? Start, int Z) PlanGroup(GroupTarget target, Vec3i position) {
		short own = _document.Groups[target.Group].PositionRef;
		var start = StartOf(target.Group);
		int pointZ = Coordinate(own)?.Z ?? (start != null ? _document.Coordinates[ReadRefs(start)[0]].Z : 0);
		int z = HasOwnHeight(target) && position.Z != FlyerHeight(pointZ) ? position.Z : pointZ;
		var move = !target.Detach && Coordinate(own) != null ? GroupMove.OwnPoint
			: !target.Detach && start != null ? GroupMove.RouteStart
			: GroupMove.NewPoint;
		return (move, own, start, z);
	}

	// A group's move written into the generated document, for a preview.
	private void PreviewGroup(GroupTarget target, Vec3i position) {
		var (move, own, start, z) = PlanGroup(target, position);
		switch (move) {
			case GroupMove.OwnPoint:
				var point = _document.Coordinates[own];
				point.X = position.X;
				point.Y = position.Y;
				point.Z = z;
				break;
			case GroupMove.RouteStart:
				WritePoints(start!, new Vec3i(position.X, position.Y, z));
				break;
			default:
				_document.Groups[target.Group].PositionRef = ScriptPointRefs.Place(_document, own, position.X, position.Y, z);
				break;
		}
	}

	// A group's move written into the .MSN.
	private void WriteGroup(GroupTarget target, Vec3i position) {
		var source = _source!;
		var (move, own, start, z) = PlanGroup(target, position);
		switch (move) {
			case GroupMove.OwnPoint:
				source.SetPoint(PointGuid(own), position.X, position.Y, z);
				break;
			case GroupMove.RouteStart:
				foreach (short reference in ReadRefs(start!).Distinct()) {
					source.SetPoint(PointGuid(reference), position.X, position.Y, z);
				}

				break;
			default:
				short point = PointGuid(own);
				source.PlacePoint(point, 1, position.X, position.Y, z, out short placed);
				if (placed != point) {
					source.SetField(MsnField.GroupPosition, source.GuidOf(GroupBlock, target.Group), placed);
				}

				break;
		}
	}

	// The row-6 GUID of block-1 point <reference>, or -1 for none.
	private short PointGuid(short reference) => _source!.GuidOf(PointBlock, reference);

	// The script.dat block a roster kind's records are in: 7 mechs, 8 flyers, 9 structures; its .MSN row is 5 more.
	private static int RosterBlock(MissionUnitKind kind) => kind switch {
		MissionUnitKind.Mech => 7,
		MissionUnitKind.Flyer => 8,
		_ => 9
	};

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
			case AreaCornerTarget corner when _index.Area(corner.Area) is { } area:
				var moved = corner.Corner == 0
					? area with { A = position, B = area.Shape == MissionTriggerShape.Circle ? position : area.B }
					: area with { B = position };
				_index.SetArea(moved);
				_overlay.MoveArea(moved);
				break;
		}
	}

	/// <summary>Stands <paramref name="sceneObject"/> at a position and heading, for whatever draws and picks it.</summary>
	public void Place(SceneObject sceneObject, Vec3i position, int heading) {
		sceneObject.Object.Position = position;
		sceneObject.Object.Heading = heading;
		_picker.Move(sceneObject);
		Moved?.Invoke(sceneObject);
	}

	private static string Describe(MoveTarget target) => target switch {
		ObjectTarget { Object: var sceneObject } => MissionText.Describe(sceneObject),
		WaypointTarget waypoint => $"waypoint {waypoint.Index} of route {waypoint.Route.Reference}",
		GroupTarget group => $"group {group.Group}",
		AreaCornerTarget corner => $"corner {corner.Corner} of trigger area {corner.Area}",
		AreaRadiusTarget radius => $"the radius of trigger area {radius.Area}",
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
		AreaCornerTarget corner => new[] {
			corner.Corner == 0 ? _document.TriggerAreas[corner.Area].PointRef : _document.TriggerAreas[corner.Area].SecondPointOrRadius
		},
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
			case AreaCornerTarget { Corner: 0 } corner:
				_document.TriggerAreas[corner.Area].PointRef = refs[0];
				break;
			case AreaCornerTarget corner:
				_document.TriggerAreas[corner.Area].SecondPointOrRadius = refs[0];
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

	// One move: the .MSN rows it changed, and where the target stood on either side of it.
	private sealed record Edit(PointMover Mover, MoveTarget Target, MsnEdit Change, Vec3i Before, Vec3i After) : IEditorEdit {
		public void Undo() => Mover.Restore(Target, Change, undo: true, Before);

		public void Redo() => Mover.Restore(Target, Change, undo: false, After);
	}
}
