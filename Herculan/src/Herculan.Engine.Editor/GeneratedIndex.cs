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
/// The editor's cross-reference over the generated mission, by <c>script.dat</c> block index, built from the
/// <see cref="World.Mission"/> generated after each edit and the scene's objects (<see cref="MissionIndex"/>). Only the generated
/// <see cref="World.Mission"/> itself is read, for the squad the block-7 cap counts and the window title's zone; the routes,
/// rosters and records here have no reader left (docs/herculan/handoff-editor-msn-scene.md#what-reads-the-generated-scriptdat-today).
/// Nothing here follows a drag's preview: what the scene draws is <see cref="MissionIndex"/>'s.
/// </summary>
internal sealed class GeneratedIndex {
	private readonly ScriptDat _document;
	private List<SceneObject> _objects = new();

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

	/// <summary>Builds the index again over <paramref name="mission"/>, generated from the document as it now stands.</summary>
	public void Reload(Mission mission) {
		Mission = mission;
		_objects = Scene.Objects.ToList();
		Routes = BuildRoutes(mission, _document);
	}

	public Mission Mission { get; private set; }

	/// <summary>Where each selection's record stands in the generated file this index is built over, and back.</summary>
	public GeneratedRecords Records { get; }

	/// <summary>Every placed object, in spawn order.</summary>
	public IReadOnlyList<SceneObject> Objects => _objects;

	/// <summary>A group's roster, by block-11 record index, or null for an index the mission does not have.</summary>
	public MissionUnitKind? KindOf(int group) =>
		group >= 0 && group < Mission.GroupKinds.Count ? Mission.GroupKinds[group] : null;

	/// <summary>
	/// Every route in block 3 with a waypoint that resolves, once each, in block-3 order: those orders name, and those
	/// only an objective or a group's own route field names, or nothing, such as a route just created.
	/// </summary>
	public IReadOnlyList<EditorRoute> Routes { get; private set; } = [];

	public EditorRoute? Route(int reference) => Routes.FirstOrDefault(route => route.Reference == reference);

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
