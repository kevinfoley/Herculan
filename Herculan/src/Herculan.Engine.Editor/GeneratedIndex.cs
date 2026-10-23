using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One route of the generated mission — a <c>script.dat</c> block-3 waypoint group, resolved to
/// points.
/// </summary>
internal sealed class EditorRoute {
	/// <param name="points">Its waypoints, in order.</param>
	public EditorRoute(int reference, IReadOnlyList<Vec3i> points, IReadOnlyList<int> walkedBy, IReadOnlyList<int> namedBy) {
		Reference = reference;
		Points = points;
		WalkedBy = walkedBy;
		NamedBy = namedBy;
	}

	/// <summary>The block-3 record index.</summary>
	public int Reference { get; }

	/// <summary>Its waypoints, in order.</summary>
	public IReadOnlyList<Vec3i> Points { get; }

	/// <summary>
	/// The groups whose order slot 0 names it — the only route a group ever walks, see
	/// docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once.
	/// </summary>
	public IReadOnlyList<int> WalkedBy { get; }

	/// <summary>The groups that name it only in a later order slot, which no group walks.</summary>
	public IReadOnlyList<int> NamedBy { get; }

	/// <summary>Whether any group's order names it, in any slot.</summary>
	public bool InAnOrder => WalkedBy.Count > 0 || NamedBy.Count > 0;

	/// <summary>
	/// Whether the route closes on itself — its last waypoint is its first — and so is walked as a loop
	/// that never ends. Compared by position, as the simulation's route cursor compares it (see
	/// <see cref="Sim.MissionGroup.RouteCursor"/>).
	/// </summary>
	public bool Closed => Points.Count > 1 && Points[^1] == Points[0];

	/// <summary>Whether any group walks it.</summary>
	public bool Walked => WalkedBy.Count > 0;
}

/// <summary>One order slot of one group.</summary>
internal readonly record struct OrderSlot(int Group, int Slot, MissionOrder Order);

/// <summary>
/// The editor's cross-reference over the generated mission, by <c>script.dat</c> block index, which the Properties views
/// still on the generation read until they move to the layout (docs/herculan/handoff-editor-msn-scene.md#steps):
/// which groups walk which route, and each group's members, setup and side.
/// Built from the <see cref="World.Mission"/> generated after each edit and the scene's objects (<see cref="MissionIndex"/>),
/// each <see cref="SceneObject.Placement"/> carrying the generation's indices for its record's GUID (<see cref="SceneSync"/>),
/// so an object the generation has none for is in no group or roster slot here. Nothing here follows a drag's preview:
/// what the scene draws is <see cref="MissionIndex"/>'s.
/// </summary>
internal sealed class GeneratedIndex {
	private readonly ScriptDat _document;
	private readonly Dictionary<(MissionUnitKind, int), SceneObject> _bySlot = new();
	private List<MissionTriggerArea> _areas = new();
	private List<MissionGroupSetup> _setups = new();
	private List<MissionBasePad> _basePads = new();
	private List<SceneObject> _objects = new();
	private List<List<SceneObject>> _members = new();
	private List<MissionSide> _sides = new();

	/// <param name="mission">The mission generated as the editor opened it.</param>
	/// <param name="document">The generated <c>script.dat</c>, whose block 3 lists the routes no order names.</param>
	/// <param name="records">Where each selection's record stands in <paramref name="document"/>.</param>
	/// <param name="scene">The scene's objects by identity, whose objects this index lists.</param>
	public GeneratedIndex(Mission mission, ScriptDat document, GeneratedRecords records, MissionIndex scene) {
		_document = document;
		Records = records;
		Scene = scene;
		Mission = mission;
		Reload(mission);
	}

	/// <summary>The scene's objects by the roster record each stands for, which an object's selection names.</summary>
	public MissionIndex Scene { get; }

	/// <summary>
	/// Builds the index again over <paramref name="mission"/>, generated from the document as it now stands, and the scene's
	/// objects as they now stand, each <see cref="SceneObject.Placement"/> naming its record in <paramref name="mission"/>.
	/// </summary>
	public void Reload(Mission mission) {
		var objects = Scene.Objects;
		Mission = mission;
		_objects = objects.ToList();
		_bySlot.Clear();
		_members = new List<List<SceneObject>>(mission.GroupKinds.Count);
		for (int i = 0; i < mission.GroupKinds.Count; i++) {
			_members.Add(new List<SceneObject>());
		}

		foreach (var placed in objects) {
			if (placed.Placement.GroupIndex >= 0 && placed.Placement.GroupIndex < _members.Count) {
				_members[placed.Placement.GroupIndex].Add(placed);
			}

			if (placed.Placement.SlotIndex >= 0) {
				_bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed;
			}
		}

		_areas = BuildAreas(_document);
		_setups = mission.GroupSetups.ToList();
		_sides = mission.GroupSides.ToList();
		_basePads = mission.BasePads.ToList();
		Routes = BuildRoutes(mission, _document);
	}

	public Mission Mission { get; private set; }

	/// <summary>Where each selection's record stands in the generated file this index is built over, and back.</summary>
	public GeneratedRecords Records { get; }

	/// <summary>Every placed object, in spawn order.</summary>
	public IReadOnlyList<SceneObject> Objects => _objects;

	/// <summary>Each group's placed members, by block-11 record index.</summary>
	public IReadOnlyList<IReadOnlyList<SceneObject>> GroupMembers => _members;

	/// <summary>A group's roster, by block-11 record index, or null for an index the mission does not have.</summary>
	public MissionUnitKind? KindOf(int group) =>
		group >= 0 && group < Mission.GroupKinds.Count ? Mission.GroupKinds[group] : null;

	/// <summary>A group's side, by block-11 record index, or null for an index the mission does not have.</summary>
	public MissionSide? SideOf(int group) => group >= 0 && group < _sides.Count ? _sides[group] : null;

	/// <summary>
	/// Every trigger area of block 4 whose points resolve, once each, in block-4 order: those actions test, and those no
	/// action names, or that sit behind an action's first unset slot, which are never tested, such as an area just created.
	/// </summary>
	public IReadOnlyList<MissionTriggerArea> Areas => _areas;

	/// <summary>The base pads of the generated mission, by block-11 index.</summary>
	public IReadOnlyList<MissionBasePad> BasePads => _basePads;

	/// <summary>
	/// Every route in block 3 with a waypoint that resolves, once each, in block-3 order: those orders name, and those
	/// only an objective or a group's own route field names, or nothing, such as a route just created.
	/// </summary>
	public IReadOnlyList<EditorRoute> Routes { get; private set; } = [];

	public MissionTriggerArea? Area(int reference) {
		foreach (var area in Areas) {
			if (area.Reference == reference) {
				return area;
			}
		}

		return null;
	}

	public EditorRoute? Route(int reference) => Routes.FirstOrDefault(route => route.Reference == reference);

	/// <summary>
	/// The placed object a mission ref names — an order, objective or action subject — or null when
	/// nothing was placed in that slot.
	/// </summary>
	public SceneObject? ObjectAt(MissionUnitKind kind, int slot) => _bySlot.GetValueOrDefault((kind, slot));

	/// <summary>A group's point, heading and formation, or null for an index the mission does not have.</summary>
	public MissionGroupSetup? SetupOf(int group) =>
		group >= 0 && group < _setups.Count ? _setups[group] : null;

	/// <summary>
	/// Where a record a mission ref names stands: an object's position, or a group's point. Null when
	/// nothing was placed in that slot.
	/// </summary>
	public Vec3i? PointOf(MissionUnitKind? kind, int reference) => kind is { } objectKind
		? ObjectAt(objectKind, reference)?.Object.Position
		: SetupOf(reference)?.Point;

	// Every block-4 record resolved as the loader resolves an action's (MissionLoader.TriggerArea).
	private static List<MissionTriggerArea> BuildAreas(ScriptDat document) {
		var areas = new List<MissionTriggerArea>(document.TriggerAreas.Length);
		for (short reference = 0; reference < document.TriggerAreas.Length; reference++) {
			if (MissionLoader.TriggerArea(document, reference) is { } area) {
				areas.Add(area);
			}
		}

		return areas;
	}

	private static List<EditorRoute> BuildRoutes(Mission mission, ScriptDat document) {
		var points = new SortedDictionary<int, IReadOnlyList<Vec3i>>();
		var walkedBy = new Dictionary<int, List<int>>();
		var namedBy = new Dictionary<int, List<int>>();

		for (int group = 0; group < mission.GroupOrders.Count; group++) {
			var orders = mission.GroupOrders[group];
			for (int slot = 0; slot < orders.Count; slot++) {
				if (orders[slot] is not { RouteRef: >= 0 } order || order.Route.Count == 0) {
					continue;
				}

				points.TryAdd(order.RouteRef, order.Route);
				var into = slot == 0 ? walkedBy : namedBy;
				if (!into.TryGetValue(order.RouteRef, out var groups)) {
					into[order.RouteRef] = groups = new List<int>();
				}

				if (!groups.Contains(group)) {
					groups.Add(group);
				}
			}
		}

		// The routes no order names, resolved as the loader resolves an order's: a ref past block 1's end is dropped.
		for (int route = 0; route < document.WaypointGroups.Length; route++) {
			if (points.ContainsKey(route)) {
				continue;
			}

			var resolved = document.WaypointGroups[route].Waypoints
				.Where(reference => reference >= 0 && reference < mission.Coordinates.Count)
				.Select(reference => mission.Coordinates[reference])
				.ToArray();
			if (resolved.Length > 0) {
				points[route] = resolved;
			}
		}

		return points
			.Select(entry => {
				var walked = walkedBy.GetValueOrDefault(entry.Key) ?? new List<int>();
				var named = (namedBy.GetValueOrDefault(entry.Key) ?? new List<int>()).Except(walked).ToList();
				return new EditorRoute(entry.Key, entry.Value, walked, named);
			})
			.ToList();
	}
}
