using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One route the mission's orders name — a <c>script.dat</c> block-3 waypoint group, resolved to
/// points.
/// </summary>
internal sealed class EditorRoute {
	private readonly Vec3i[] _points;

	/// <param name="points">Its waypoints, in order; copied, so a move here leaves the loaded orders alone.</param>
	public EditorRoute(int reference, IReadOnlyList<Vec3i> points, IReadOnlyList<int> walkedBy, IReadOnlyList<int> namedBy) {
		Reference = reference;
		_points = points.ToArray();
		WalkedBy = walkedBy;
		NamedBy = namedBy;
	}

	/// <summary>The block-3 record index.</summary>
	public int Reference { get; }

	/// <summary>Its waypoints, in order, as the editor's moves have left them.</summary>
	public IReadOnlyList<Vec3i> Points => _points;

	/// <summary>
	/// The groups whose order slot 0 names it — the only route a group ever walks, see
	/// docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once.
	/// </summary>
	public IReadOnlyList<int> WalkedBy { get; }

	/// <summary>The groups that name it only in a later order slot, which no group walks.</summary>
	public IReadOnlyList<int> NamedBy { get; }

	/// <summary>Stands waypoint <paramref name="index"/> at <paramref name="point"/>.</summary>
	public void Move(int index, Vec3i point) => _points[index] = point;

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
/// Everything that activates one mission action, and everything that waits on it — the four
/// activation routes and the three consumers in docs/retail/simulation/mission-deployment.md.
/// </summary>
internal sealed record ActionLinks(
	IReadOnlyList<int> TimersFiringIt,
	IReadOnlyList<int> TimersItStarts,
	IReadOnlyList<SceneObject> EngagedObjects,
	IReadOnlyList<SceneObject> DefeatedObjects,
	IReadOnlyList<int> WaitingGroups,
	IReadOnlyList<OrderSlot> OrdersItEnds);

/// <summary>
/// The editor's cross-reference over one loaded mission, built once: which actions name which
/// trigger area, which groups walk which route, what fires each action and what each one sets off.
/// Which record names which is never recomputed. Where things stand is: the routes' points, the trigger areas, the
/// group setups and the base pads here are the editor's own copies, which <see cref="PointFollowers"/> keeps in step
/// with the document as its points move, and so are the groups' sides, which <see cref="SideChanger"/> sets. The objects
/// and groups an edit adds (<see cref="StructurePlacer"/>) are
/// listed here beside the loaded ones, and an object a delete unlinks (<see cref="ObjectDeleter"/>) is taken off;
/// <see cref="Mission"/> stays as loaded.
/// </summary>
internal sealed class MissionIndex {
	private readonly Dictionary<(MissionUnitKind, int), SceneObject> _bySlot = new();
	private readonly ActionLinks[] _actionLinks;
	private readonly List<MissionTriggerArea> _areas;
	private readonly List<MissionGroupSetup> _setups;
	private readonly List<MissionBasePad> _basePads;
	private readonly List<SceneObject> _objects;
	private readonly List<List<SceneObject>> _members;

	// Every group's side, and the kind of each group an edit added, past the loaded mission's.
	private readonly List<MissionSide> _sides;
	private readonly List<MissionUnitKind> _addedKinds = new();

	public MissionIndex(MissionScene scene, SquadMessages? commandMessages) {
		Mission = scene.Mission;
		CommandMessages = commandMessages;
		_objects = scene.Objects.ToList();

		_members = new List<List<SceneObject>>(Mission.GroupKinds.Count);
		for (int i = 0; i < Mission.GroupKinds.Count; i++) {
			_members.Add(new List<SceneObject>());
		}

		foreach (var placed in scene.Objects) {
			if (placed.Placement.GroupIndex >= 0 && placed.Placement.GroupIndex < _members.Count) {
				_members[placed.Placement.GroupIndex].Add(placed);
			}

			if (placed.Placement.SlotIndex >= 0) {
				_bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed;
			}
		}

		_areas = BuildAreas(Mission);
		_setups = Mission.GroupSetups.ToList();
		_sides = Mission.GroupSides.ToList();
		_basePads = Mission.BasePads.ToList();
		Routes = BuildRoutes(Mission);

		_actionLinks = new ActionLinks[Mission.Actions.Count];
		for (int i = 0; i < _actionLinks.Length; i++) {
			_actionLinks[i] = BuildLinks(i, scene.Objects);
		}
	}

	public Mission Mission { get; }

	/// <summary>
	/// The speakerless message set an action's message is looked up in — <c>COMMAND&lt;n&gt;.STR</c>,
	/// see <see cref="MissionAction.MessageId"/> — or null when the install has none.
	/// </summary>
	public SquadMessages? CommandMessages { get; }

	/// <summary>Every placed object, in spawn order, then those an edit added.</summary>
	public IReadOnlyList<SceneObject> Objects => _objects;

	/// <summary>Each group's placed members, by block-11 record index.</summary>
	public IReadOnlyList<IReadOnlyList<SceneObject>> GroupMembers => _members;

	/// <summary>A group's roster, by block-11 record index, or null for an index the mission does not have.</summary>
	public MissionUnitKind? KindOf(int group) =>
		group >= 0 && group < Mission.GroupKinds.Count ? Mission.GroupKinds[group]
			: group >= Mission.GroupKinds.Count && group - Mission.GroupKinds.Count < _addedKinds.Count
				? _addedKinds[group - Mission.GroupKinds.Count]
				: null;

	/// <summary>A group's side, by block-11 record index, or null for an index the mission does not have.</summary>
	public MissionSide? SideOf(int group) => group >= 0 && group < _sides.Count ? _sides[group] : null;

	/// <summary>Sets a group's side, as <see cref="SideChanger"/> has written it into the document.</summary>
	public void SetSide(int group, MissionSide side) => _sides[group] = side;

	/// <summary>
	/// Lists a group an edit appended to block 11 as record <paramref name="group"/>, which must be the next index, with
	/// no members yet.
	/// </summary>
	public void AddGroup(int group, MissionUnitKind kind, MissionSide side, MissionGroupSetup setup) {
		if (group != _members.Count || group != _setups.Count || group != _sides.Count) {
			throw new InvalidOperationException($"Group {group} is not the next group; {_members.Count} are listed.");
		}

		_addedKinds.Add(kind);
		_sides.Add(side);
		_members.Add(new List<SceneObject>());
		_setups.Add(setup);
	}

	/// <summary>Takes the last group <see cref="AddGroup"/> listed off the list again; its members must be gone.</summary>
	public void RemoveLastAddedGroup() {
		if (_addedKinds.Count == 0) {
			throw new InvalidOperationException("No added group to remove.");
		}

		_addedKinds.RemoveAt(_addedKinds.Count - 1);
		_sides.RemoveAt(_sides.Count - 1);
		_members.RemoveAt(_members.Count - 1);
		_setups.RemoveAt(_setups.Count - 1);
	}

	/// <summary>Lists an object an edit added, in its group and under its roster slot.</summary>
	public void AddObject(SceneObject placed) {
		_objects.Add(placed);
		_members[placed.Placement.GroupIndex].Add(placed);
		_bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed;
	}

	/// <summary>Takes an object <see cref="AddObject"/> listed off every list again.</summary>
	public void RemoveObject(SceneObject placed) {
		_objects.Remove(placed);
		_members[placed.Placement.GroupIndex].Remove(placed);
		_bySlot.Remove((placed.Placement.Kind, placed.Placement.SlotIndex));
	}

	/// <summary>
	/// Takes any placed object, loaded or added, off every list, as a delete does (<see cref="ObjectDeleter"/>), and
	/// answers where it stood in <see cref="Objects"/> and in its group's members, for <see cref="RestoreObject"/>.
	/// </summary>
	public (int Object, int Member) DetachObject(SceneObject placed) {
		var members = _members[placed.Placement.GroupIndex];
		var at = (Object: _objects.FindIndex(o => ReferenceEquals(o, placed)),
			Member: members.FindIndex(o => ReferenceEquals(o, placed)));
		_objects.RemoveAt(at.Object);
		members.RemoveAt(at.Member);
		var key = (placed.Placement.Kind, placed.Placement.SlotIndex);
		if (_bySlot.TryGetValue(key, out var listed) && ReferenceEquals(listed, placed)) {
			_bySlot.Remove(key);
		}

		return at;
	}

	/// <summary>Puts an object <see cref="DetachObject"/> took off back where it stood.</summary>
	public void RestoreObject(SceneObject placed, (int Object, int Member) at) {
		_objects.Insert(at.Object, placed);
		_members[placed.Placement.GroupIndex].Insert(at.Member, placed);
		_bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed;
	}

	/// <summary>
	/// Every trigger area some action tests, once each, in block-4 order. An area no action names, or
	/// that sits behind its action's first unset slot, is never tested and is not listed.
	/// </summary>
	public IReadOnlyList<MissionTriggerArea> Areas => _areas;

	/// <summary>The base pads, as the editor's moves have left their groups.</summary>
	public IReadOnlyList<MissionBasePad> BasePads => _basePads;

	/// <summary>Every route some order names, once each, in block-3 order.</summary>
	public IReadOnlyList<EditorRoute> Routes { get; }

	/// <summary>The route the player's squad walks, or null.</summary>
	public EditorRoute? PlayerRoute => Routes.FirstOrDefault(route => route.WalkedBy.Contains(0));

	public MissionTriggerArea? Area(int reference) {
		foreach (var area in Areas) {
			if (area.Reference == reference) {
				return area;
			}
		}

		return null;
	}

	public EditorRoute? Route(int reference) => Routes.FirstOrDefault(route => route.Reference == reference);

	/// <summary>The actions that test <paramref name="areaReference"/>, in block-5 order.</summary>
	public IEnumerable<int> ActionsTesting(int areaReference) {
		for (int i = 0; i < Mission.Actions.Count; i++) {
			if (Mission.Actions[i].Areas.Any(area => area.Reference == areaReference)) {
				yield return i;
			}
		}
	}

	public ActionLinks LinksOf(int action) => _actionLinks[action];

	/// <summary>
	/// The placed object a mission ref names — an order, objective or action subject — or null when
	/// nothing was placed in that slot.
	/// </summary>
	public SceneObject? ObjectAt(MissionUnitKind kind, int slot) => _bySlot.GetValueOrDefault((kind, slot));

	/// <summary>The action <paramref name="group"/> waits on before it is in the mission, or -1.</summary>
	public int DeploymentActionOf(int group) =>
		group >= 0 && group < Mission.GroupDeploymentActions.Count ? Mission.GroupDeploymentActions[group] : -1;

	/// <summary>Whether <paramref name="placed"/> belongs to a group waiting on an action.</summary>
	public bool IsWaiting(SceneObject placed) => DeploymentActionOf(placed.Placement.GroupIndex) >= 0;

	/// <summary>A group's point, heading and formation, or null for an index the mission does not have.</summary>
	public MissionGroupSetup? SetupOf(int group) =>
		group >= 0 && group < _setups.Count ? _setups[group] : null;

	/// <summary>Replaces the area with <paramref name="area"/>'s reference by <paramref name="area"/>.</summary>
	public void SetArea(MissionTriggerArea area) {
		int at = _areas.FindIndex(existing => existing.Reference == area.Reference);
		if (at >= 0) {
			_areas[at] = area;
		}
	}

	public void SetSetup(int group, MissionGroupSetup setup) => _setups[group] = setup;

	/// <summary>Replaces the pad of <paramref name="pad"/>'s group by <paramref name="pad"/>.</summary>
	public void SetBasePad(MissionBasePad pad) {
		int at = _basePads.FindIndex(existing => existing.GroupIndex == pad.GroupIndex);
		if (at >= 0) {
			_basePads[at] = pad;
		}
	}

	/// <summary>
	/// Where a record a mission ref names stands: an object's position, or a group's point. Null when
	/// nothing was placed in that slot.
	/// </summary>
	public Vec3i? PointOf(MissionUnitKind? kind, int reference) => kind is { } objectKind
		? ObjectAt(objectKind, reference)?.Object.Position
		: SetupOf(reference)?.Point;

	/// <summary>
	/// The objects an objective is asked of: its subject object, or every placed member of its
	/// subject group.
	/// </summary>
	public IReadOnlyList<SceneObject> SubjectObjectsOf(MissionObjective objective) {
		if (UnitKindOf(objective.SubjectKind) is { } kind) {
			return ObjectAt(kind, objective.SubjectRef) is { } placed ? new[] { placed } : Array.Empty<SceneObject>();
		}

		return objective.SubjectKind == MissionObjectiveSubject.Group
				&& objective.SubjectRef >= 0 && objective.SubjectRef < GroupMembers.Count
			? GroupMembers[objective.SubjectRef]
			: Array.Empty<SceneObject>();
	}

	/// <summary>
	/// The group whose orders condition 0 reads: the subject group, or the group the subject object
	/// belongs to. -1 when the subject is an object nothing placed.
	/// </summary>
	public int OrderGroupOf(MissionObjective objective) => UnitKindOf(objective.SubjectKind) is { } kind
		? ObjectAt(kind, objective.SubjectRef)?.Placement.GroupIndex ?? -1
		: objective.SubjectKind == MissionObjectiveSubject.Group ? objective.SubjectRef : -1;

	/// <summary>
	/// The order slot of <paramref name="group"/> whose completion an order-complete objective on
	/// <paramref name="routeRef"/> reads, or -1 when none runs that route — the first slot to run it,
	/// as <see cref="Sim.MissionGroup.OrderCompletedForRoute"/> picks it.
	/// </summary>
	public int SlotOnRoute(int group, int routeRef) {
		if (group < 0 || group >= Mission.GroupOrders.Count || routeRef < 0) {
			return -1;
		}

		var orders = Mission.GroupOrders[group];
		for (int slot = 0; slot < orders.Count; slot++) {
			if (orders[slot]?.RouteRef == routeRef) {
				return slot;
			}
		}

		return -1;
	}

	/// <summary>The objectives whose subject is <paramref name="group"/>.</summary>
	public IEnumerable<int> ObjectivesOfGroup(int group) {
		for (int i = 0; i < Mission.Objectives.Count; i++) {
			if (Mission.Objectives[i] is { SubjectKind: MissionObjectiveSubject.Group } objective
					&& objective.SubjectRef == group) {
				yield return i;
			}
		}
	}

	/// <summary>The objectives whose subject is <paramref name="placed"/> itself.</summary>
	public IEnumerable<int> ObjectivesOfObject(SceneObject placed) {
		for (int i = 0; i < Mission.Objectives.Count; i++) {
			var objective = Mission.Objectives[i];
			if (UnitKindOf(objective.SubjectKind) == placed.Placement.Kind
					&& objective.SubjectRef == placed.Placement.SlotIndex && placed.Placement.SlotIndex >= 0) {
				yield return i;
			}
		}
	}

	/// <summary>The roster an objective subject indexes, or null for a group subject.</summary>
	public static MissionUnitKind? UnitKindOf(MissionObjectiveSubject kind) => kind switch {
		MissionObjectiveSubject.Mech => MissionUnitKind.Mech,
		MissionObjectiveSubject.Flyer => MissionUnitKind.Flyer,
		MissionObjectiveSubject.Base => MissionUnitKind.Base,
		_ => null
	};

	/// <summary>The roster an order subject indexes, or null for a group subject (or none).</summary>
	public static MissionUnitKind? UnitKindOf(MissionOrderSubject kind) => kind switch {
		MissionOrderSubject.Mech => MissionUnitKind.Mech,
		MissionOrderSubject.Flyer => MissionUnitKind.Flyer,
		MissionOrderSubject.Base => MissionUnitKind.Base,
		_ => null
	};

	private static List<MissionTriggerArea> BuildAreas(Mission mission) {
		var byReference = new SortedDictionary<int, MissionTriggerArea>();
		foreach (var action in mission.Actions) {
			foreach (var area in action.Areas) {
				byReference.TryAdd(area.Reference, area);
			}
		}

		return byReference.Values.ToList();
	}

	private static List<EditorRoute> BuildRoutes(Mission mission) {
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

		return points
			.Select(entry => {
				var walked = walkedBy.GetValueOrDefault(entry.Key) ?? new List<int>();
				var named = (namedBy.GetValueOrDefault(entry.Key) ?? new List<int>()).Except(walked).ToList();
				return new EditorRoute(entry.Key, entry.Value, walked, named);
			})
			.ToList();
	}

	private ActionLinks BuildLinks(int action, IReadOnlyList<SceneObject> objects) {
		var firing = new List<int>();
		var starts = new List<int>();
		for (int i = 0; i < Mission.ActionTimers.Count; i++) {
			var timer = Mission.ActionTimers[i];
			if (timer.SequenceRefs.Any(reference => reference == action)) {
				firing.Add(i);
			}

			if (timer.PrimaryActionRef == action) {
				starts.Add(i);
			}
		}

		var waiting = new List<int>();
		for (int group = 0; group < Mission.GroupDeploymentActions.Count; group++) {
			if (Mission.GroupDeploymentActions[group] == action) {
				waiting.Add(group);
			}
		}

		var ends = new List<OrderSlot>();
		for (int group = 0; group < Mission.GroupOrders.Count; group++) {
			var orders = Mission.GroupOrders[group];
			for (int slot = 0; slot < orders.Count; slot++) {
				if (orders[slot] is { } order && order.ActionRef == action) {
					ends.Add(new OrderSlot(group, slot, order));
				}
			}
		}

		return new ActionLinks(
			firing,
			starts,
			objects.Where(o => o.Placement.EngagementActionRef == action).ToList(),
			objects.Where(o => o.Placement.DefeatActionRef == action).ToList(),
			waiting,
			ends);
	}
}
