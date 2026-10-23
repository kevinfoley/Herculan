using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The scene's objects, groups, routes and trigger areas as the <c>.MSN</c>'s layout places them (<see cref="LayoutPlacement"/>),
/// keyed by GUID, every record included, a conditional one too: each object and the roster record it stands for
/// (<see cref="ObjectKey"/>); each group's members, setup, pad, roster, side, the action it waits on and its display number;
/// each route's waypoints and the groups walking or naming it; each area's shape and the actions testing it; each action's
/// areas, what else fires it and what follows it (<see cref="TriggerWiring"/>); each objective's subject, its group and the
/// order it reads; and the mission box. Built again by
/// <see cref="SceneSync"/> after every edit, undo and redo, and placed again, keeping its objects, for a drag's preview
/// (<see cref="Replan"/>), which moves nothing the wiring reads.
/// </summary>
internal sealed class MissionIndex {
	private readonly Dictionary<ObjectKey, SceneObject> _byKey = new();
	private readonly Dictionary<SceneObject, ObjectKey> _keyOf = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<short, IReadOnlyList<SceneObject>> _members = new();
	private List<SceneObject> _objects = new();
	private List<MissionBasePad> _pads = new();
	private IReadOnlyDictionary<short, TriggerLinks> _links = new Dictionary<short, TriggerLinks>();

	/// <param name="objects">The object built for each placed record that has one.</param>
	public MissionIndex(LayoutPlan plan, IReadOnlyDictionary<ObjectKey, SceneObject> objects) {
		Plan = plan;
		Reload(plan, objects);
	}

	/// <summary>Where the layout places everything, as last placed: after the last edit, or by the drag under way.</summary>
	public LayoutPlan Plan { get; private set; }

	/// <summary>The layout <see cref="Plan"/> places.</summary>
	public MissionLayout Layout => Plan.Layout;

	/// <summary>Every object in the scene, in <see cref="LayoutPlan.Objects"/>' order.</summary>
	public IReadOnlyList<SceneObject> Objects => _objects;

	/// <summary>Every pad a group paints, in group order, each by its group's display number.</summary>
	public IReadOnlyList<MissionBasePad> BasePads => _pads;

	/// <summary>Every row-16 group, in layout order.</summary>
	public IReadOnlyList<PlannedGroup> Groups => Plan.Groups;

	/// <inheritdoc cref="LayoutPlan.Routes"/>
	public IReadOnlyList<PlannedRoute> Routes => Plan.Routes;

	/// <inheritdoc cref="LayoutPlan.Areas"/>
	public IReadOnlyList<PlannedArea> Areas => Plan.Areas;

	/// <inheritdoc cref="LayoutPlan.Actions"/>
	public IReadOnlyList<PlannedAction> Actions => Plan.Actions;

	/// <summary>The route the player's squad walks, group 0's slot-0 order's, or null.</summary>
	public PlannedRoute? PlayerRoute => Groups.Count > 0 ? Routes.FirstOrDefault(route => route.WalkedBy.Contains(Groups[0].Guid)) : null;

	/// <summary>
	/// The mission box: the extent of every row-6 point, as <see cref="MissionBox"/> is the extent of block 1, which the
	/// generation writes from them. As of the last edit; a drag's preview does not move it.
	/// </summary>
	public MissionBox Box { get; private set; }

	/// <summary>
	/// Builds the index again over <paramref name="plan"/> and <paramref name="objects"/>, the object built for each placed
	/// record that has one.
	/// </summary>
	public void Reload(LayoutPlan plan, IReadOnlyDictionary<ObjectKey, SceneObject> objects) {
		Plan = plan;
		_byKey.Clear();
		_keyOf.Clear();
		_objects = new List<SceneObject>(objects.Count);
		foreach (var planned in plan.Objects) {
			if (objects.TryGetValue(planned.Key, out var placed)) {
				_byKey[planned.Key] = placed;
				_keyOf[placed] = planned.Key;
				_objects.Add(placed);
			}
		}

		_members.Clear();
		foreach (var group in plan.Groups) {
			_members[group.Guid] = group.Members.Select(key => _byKey.GetValueOrDefault(key)).OfType<SceneObject>().ToList();
		}

		_pads = PadsOf(plan);
		_links = TriggerWiring.Build(plan);
		Box = MissionBox.Of(plan.Layout.Entries<MapPoint22>()
			.Select(point => new Vec3i(point.Fields.X, point.Fields.Y, point.Fields.Z)).ToList());
	}

	/// <summary>
	/// Takes <paramref name="plan"/>, which places the same records as <see cref="Plan"/> elsewhere, for the groups' setups
	/// and pads and the routes and areas, keeping the objects: a drag's preview.
	/// </summary>
	public void Replan(LayoutPlan plan) {
		Plan = plan;
		_pads = PadsOf(plan);
	}

	/// <summary>The roster record <paramref name="placed"/> stands for, or null for an object not in the scene.</summary>
	public ObjectKey? KeyOf(SceneObject placed) => _keyOf.TryGetValue(placed, out var key) ? key : null;

	/// <summary>The object standing for <paramref name="key"/>, or null when the scene has none.</summary>
	public SceneObject? ObjectOf(ObjectKey key) => _byKey.GetValueOrDefault(key);

	/// <summary>The object <paramref name="selection"/> names, or null when the scene has none.</summary>
	public SceneObject? ObjectOf(ObjectSelection selection) => ObjectOf(new ObjectKey(selection.Row, selection.Guid));

	/// <summary>The selection of <paramref name="placed"/>, by the record it stands for; null for an object not in the scene.</summary>
	public ObjectSelection? SelectionOf(SceneObject placed) =>
		KeyOf(placed) is { } key ? new ObjectSelection(key.Row, key.Guid) : null;

	/// <summary>How <paramref name="placed"/> is placed, or null for an object not in the scene.</summary>
	public PlannedObject? Planned(SceneObject placed) => KeyOf(placed) is { } key ? Plan.Object(key) : null;

	/// <summary>Row-16 GUID <paramref name="guid"/>'s group, or null when the layout has none.</summary>
	public PlannedGroup? Group(short guid) => Plan.Group(guid);

	/// <summary>The group that places <paramref name="placed"/>, or null for an object not in the scene.</summary>
	public PlannedGroup? GroupOf(SceneObject placed) => Planned(placed) is { } planned ? Group(planned.Group) : null;

	/// <summary>The objects group <paramref name="guid"/> places, in roster order, the squad's in slot order.</summary>
	public IReadOnlyList<SceneObject> MembersOf(short guid) => _members.GetValueOrDefault(guid) ?? [];

	/// <summary>Whether <paramref name="placed"/>'s group waits on an action before it is in the mission.</summary>
	public bool IsWaiting(SceneObject placed) => GroupOf(placed)?.Waiting == true;

	/// <summary>
	/// Whether <paramref name="placed"/> is in the scene, drawn and picked: false for a member of a group kept out of it
	/// (<see cref="PlannedGroup.OffScene"/>), which is still indexed and selected from the outliner.
	/// </summary>
	public bool InScene(SceneObject placed) => GroupOf(placed)?.OffScene != true;

	/// <summary>The pad group <paramref name="guid"/> paints, or null.</summary>
	public MissionBasePad? PadOf(short guid) => Group(guid)?.Pad;

	/// <summary>Row-8 GUID <paramref name="guid"/>'s route, or null when it has no waypoint that stands somewhere.</summary>
	public PlannedRoute? Route(short guid) => Plan.Route(guid);

	/// <summary>Row-9 GUID <paramref name="guid"/>'s trigger area, or null when it stands nowhere.</summary>
	public PlannedArea? Area(short guid) => Plan.Area(guid);

	/// <summary>Row-10 GUID <paramref name="guid"/>'s action, or null when the layout has none.</summary>
	public PlannedAction? Action(short guid) => Plan.Action(guid);

	/// <summary>What fires row-10 GUID <paramref name="guid"/> besides its areas, and what follows it; <see cref="TriggerLinks.None"/> for none.</summary>
	public TriggerLinks Links(short guid) => _links.GetValueOrDefault(guid) ?? TriggerLinks.None;

	/// <summary>
	/// The roster record an objective's or order's subject of <paramref name="kind"/> (1 a herc, 2 a flyer, 3 a structure or
	/// ground vehicle) and GUID <paramref name="guid"/> names; null for a group (kind 0), none or an unknown kind.
	/// </summary>
	public static ObjectKey? SubjectKey(short kind, short guid) => kind switch {
		1 => new ObjectKey(12, guid),
		2 => new ObjectKey(13, guid),
		3 => new ObjectKey(14, guid),
		_ => null,
	};

	/// <summary>
	/// The row-16 GUID of the group whose orders objective <paramref name="fields"/>'s condition 0 reads: its subject group, or
	/// the group that places its subject; <c>-1</c> when there is none.
	/// </summary>
	public short SubjectGroup(MissionObjective58 fields) => fields.SubjectKind == 0
		? Group(fields.SubjectRef) != null ? fields.SubjectRef : (short)-1
		: SubjectKey(fields.SubjectKind, fields.SubjectRef) is { } key && Plan.Object(key) is { } planned ? planned.Group : (short)-1;

	/// <summary>The objects objective <paramref name="fields"/> is asked of: its subject's object, or every member of its subject group.</summary>
	public IReadOnlyList<SceneObject> SubjectObjects(MissionObjective58 fields) => fields.SubjectKind == 0
		? MembersOf(fields.SubjectRef)
		: SubjectKey(fields.SubjectKind, fields.SubjectRef) is { } key && ObjectOf(key) is { } placed ? [placed] : [];

	/// <summary>
	/// The order slot of group <paramref name="group"/> whose completion a condition-0 objective on row-8 GUID
	/// <paramref name="route"/> reads: the first slot whose order names that route, as <c>Mission_GroupOrderCompleteOnRoute</c>
	/// (<c>0041324c</c>) scans them (docs/retail/simulation/ai-goals.md#what-else-reads-an-order); <c>-1</c> for none.
	/// </summary>
	public int SlotOnRoute(short group, short route) {
		if (route == -1 || Group(group) is not { } planned) {
			return -1;
		}

		for (int slot = 0; slot < planned.Orders.Count; slot++) {
			if (planned.Orders[slot] != -1 && Layout.Entry<MissionOrder22>(planned.Orders[slot])?.Fields.RouteRef == route) {
				return slot;
			}
		}

		return -1;
	}

	/// <summary>The objectives whose subject is group <paramref name="group"/>, in row-17 order.</summary>
	public IEnumerable<LayoutObjective> ObjectivesAbout(short group) =>
		Layout.Objectives.Where(objective => objective.Fields.SubjectKind == 0 && objective.Fields.SubjectRef == group);

	/// <summary>The objectives whose subject is the roster record <paramref name="key"/> itself, in row-17 order.</summary>
	public IEnumerable<LayoutObjective> ObjectivesAbout(ObjectKey key) =>
		Layout.Objectives.Where(objective => SubjectKey(objective.Fields.SubjectKind, objective.Fields.SubjectRef) == key);

	/// <summary>
	/// The objectives that read the order in group <paramref name="group"/>'s slot <paramref name="slot"/>: those asking
	/// whether the subject's group has finished its order on a route (condition 0) whose group is this one and whose route is
	/// the first this group's orders name in that slot (<see cref="SlotOnRoute"/>), in row-17 order.
	/// </summary>
	public IReadOnlyList<LayoutObjective> ObjectivesReadingOrder(short group, int slot) => Layout.Objectives
		.Where(objective => objective.Fields.ConditionCode == MissionObjective.ConditionOrderComplete
			&& SubjectGroup(objective.Fields) == group && SlotOnRoute(group, objective.Fields.RouteRef) == slot)
		.ToList();

	private static List<MissionBasePad> PadsOf(LayoutPlan plan) =>
		plan.Groups.Select(group => group.Pad).OfType<MissionBasePad>().ToList();
}
