using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The roster record a scene object stands for: its <c>.MSN</c> row (12 a herc, 13 a flyer, 14 a structure or ground
/// vehicle) and its GUID. A squad herc is a row-12 record too.
/// </summary>
internal readonly record struct ObjectKey(int Row, short Guid);

/// <summary>What <see cref="LayoutPlacement"/> reads besides the layout: the three formation tables and the type names.</summary>
/// <param name="MechNames">The <c>nam\MECHS.NAM</c> names a herc's type resolves through, or null to leave every name unset.</param>
/// <param name="FlyerNames">The <c>nam\FLYERS.NAM</c> names a flyer's type resolves through, or null.</param>
internal sealed record PlacementTables(MechFormationTable Mechs, FlyerFormationTable Flyers, BaseFormationTable Bases,
		UnitTypeNames? MechNames = null, UnitTypeNames? FlyerNames = null) {
	/// <summary>The tables as <see cref="MissionLoader"/> loads them from DBSIM's mount.</summary>
	public static PlacementTables Load(GameContent content) => new(MechFormationTable.Load(content),
		FlyerFormationTable.Load(content), BaseFormationTable.Load(content), UnitTypeNames.LoadMechs(content),
		UnitTypeNames.LoadFlyers(content));
}

/// <summary>
/// Where a drag stands what it moves before it lets go, which <see cref="LayoutPlacement.Place"/> reads in place of the
/// layout's: a point's coordinates, a group standing on a point of its own, an object standing alone, a waypoint or an area
/// corner on a point of its own, or a circle's radius. Only the commit writes the <c>.MSN</c>.
/// </summary>
internal sealed record PlacementOverrides {
	/// <summary>None: everything where the layout puts it.</summary>
	public static PlacementOverrides None { get; } = new();

	/// <summary>Row-6 GUIDs at these coordinates, in place of their records'.</summary>
	public IReadOnlyDictionary<short, Vec3i> Points { get; init; } = new Dictionary<short, Vec3i>();

	/// <summary>A group, by its row-16 GUID, standing on a point of its own at these coordinates.</summary>
	public (short Group, Vec3i Point)? GroupPoint { get; init; }

	/// <summary>An object standing on a point of its own at these coordinates.</summary>
	public (ObjectKey Object, Vec3i Point)? ObjectPoint { get; init; }

	/// <summary>
	/// Entries of a route's list, by its row-8 GUID, standing on a point of their own at these coordinates: a waypoint dragged
	/// alone, which moves its route's line and nothing else until the commit.
	/// </summary>
	public (short Route, IReadOnlyList<int> Entries, Vec3i Point)? DetachedWaypoint { get; init; }

	/// <summary>
	/// A trigger area's corner (0 a box's first corner or a circle's centre, 1 a box's second), by its row-9 GUID, standing on
	/// a point of its own at these coordinates: a corner dragged alone.
	/// </summary>
	public (short Area, int Corner, Vec3i Point)? DetachedCorner { get; init; }

	/// <summary>A circle trigger area, by its row-9 GUID, of this radius in world units.</summary>
	public (short Area, int Radius)? Radius { get; init; }
}

/// <summary>
/// One roster record as placed: what it is, as its base's fields give it, and where it stands before the ground is read
/// (<see cref="MissionMemberPlace"/>). <paramref name="Group"/> is the row-16 GUID of the group that places it, and
/// <paramref name="MemberIndex"/> its member slot there, which for a squad herc is its entry in the squad.
/// </summary>
internal sealed record PlannedObject(ObjectKey Key, MissionUnitKind Kind, int TypeIndex, string? TypeName,
	IReadOnlyList<short> WeaponRefs, IReadOnlyList<short> WeaponSecondary, short StartingCondition, short AiCruiseSpeed,
	bool AiRadarActive, short Group, int MemberIndex, bool Squad, MissionSide Side, MissionMemberPlace Place);

/// <summary>
/// One row-16 group as placed: its position in the layout's row 16 (<paramref name="Number"/>, its display number, which is
/// its generated block-11 index when the generation drops no group before it), roster, side, the action it waits on
/// (<c>-1</c> for none), its setup (<see cref="MissionPlacementRules.GroupSetup"/>) and the records it places, in roster
/// order, the squad's in slot order.
/// </summary>
/// <param name="DeploymentAction">The row-10 GUID it waits on, or <c>-1</c>.</param>
/// <param name="Waiting">Whether that action is in the layout, so the group is not in the mission until it fires.</param>
/// <param name="ArrivalVerb">
/// That action's verb (<see cref="MissionAction82.Verb"/>), which says how the group arrives when it fires; <c>-1</c> when
/// it is not <paramref name="Waiting"/>.
/// </param>
/// <param name="OwnPoint">The row-6 GUID of its own point when the layout has it, else <c>-1</c>.</param>
/// <param name="StartPoints">
/// The row-6 GUIDs its slot-0 route's first waypoint names, and the last's too when the route closes on its start, the
/// point it stands on when it has none of its own; empty for none.
/// </param>
/// <param name="Orders">Its ten order slots' row-15 GUIDs, <c>-1</c> where unset.</param>
/// <param name="Pad">The ground it paints, read from its first member, or null.</param>
internal sealed record PlannedGroup(short Guid, int Number, MissionUnitKind Kind, MissionSide Side, short DeploymentAction,
		bool Waiting, short ArrivalVerb, MissionGroupSetup Setup, short OwnPoint, IReadOnlyList<short> StartPoints,
		IReadOnlyList<short> Orders, IReadOnlyList<ObjectKey> Members, MissionBasePad? Pad) {
	/// <summary>
	/// Whether the group is kept out of the scene, by the user's choice (docs/herculan/handoff-editor-msn-scene.md#decided-by-the-user):
	/// it waits on an action whose verb brings it in by drop pod or on foot, at a point picked about the player when the
	/// action fires (docs/retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4), so retail neither
	/// draws nor touches it before then and never uses its own point. Its members are still built and indexed, and selected
	/// from the outliner; only the scene leaves them out: not drawn, picked, boxed, labelled, framed or given move handles.
	/// A structure group stays in the scene whatever its action's verb: DBSIM builds a held-back structure at load where it
	/// is placed, footprint and pad included (docs/retail/simulation/mission-deployment.md#held-back-structures-stand-from-the-start).
	/// </summary>
	public bool OffScene => Waiting && Kind != MissionUnitKind.Base && ArrivalVerb is MissionAction.VerbPodWide
		or MissionAction.VerbPodNarrow or MissionAction.VerbWalkBehind or MissionAction.VerbWalkAhead;
}

/// <summary>
/// Every group, every placed roster record, and every route, trigger area and action of a layout, as
/// <see cref="LayoutPlacement.Place"/> places them.
/// </summary>
internal sealed class LayoutPlan {
	private readonly Dictionary<short, PlannedGroup> _groups;
	private readonly Dictionary<ObjectKey, PlannedObject> _objects;
	private readonly Dictionary<short, PlannedRoute> _routes;
	private readonly Dictionary<short, PlannedArea> _areas;
	private readonly Dictionary<short, PlannedAction> _actions;

	public LayoutPlan(MissionLayout layout, IReadOnlyList<PlannedGroup> groups, IReadOnlyList<PlannedObject> objects,
			IReadOnlyList<PlannedRoute> routes, IReadOnlyList<PlannedArea> areas, IReadOnlyList<PlannedAction> actions) {
		Layout = layout;
		Groups = groups;
		Objects = objects;
		Routes = routes;
		Areas = areas;
		Actions = actions;
		_groups = groups.GroupBy(group => group.Guid).ToDictionary(same => same.Key, same => same.First());
		_objects = objects.ToDictionary(placed => placed.Key);
		_routes = routes.ToDictionary(route => route.Guid);
		_areas = areas.ToDictionary(area => area.Guid);
		_actions = actions.ToDictionary(action => action.Guid);
	}

	/// <summary>The layout placed.</summary>
	public MissionLayout Layout { get; }

	/// <summary>Every row-16 entry, in layout order; the first is group 0, the squad's.</summary>
	public IReadOnlyList<PlannedGroup> Groups { get; }

	/// <summary>
	/// Every roster record a group places, in the order the spawn pass walks them: the hercs, the flyers, then the structures
	/// and ground vehicles, each in layout order, then the squad's hercs in group 0's slot order.
	/// </summary>
	public IReadOnlyList<PlannedObject> Objects { get; }

	/// <summary>Every row-8 route with a waypoint that stands somewhere, in layout order.</summary>
	public IReadOnlyList<PlannedRoute> Routes { get; }

	/// <summary>Every row-9 trigger area that stands somewhere, in layout order.</summary>
	public IReadOnlyList<PlannedArea> Areas { get; }

	/// <summary>Every row-10 action, in layout order.</summary>
	public IReadOnlyList<PlannedAction> Actions { get; }

	public PlannedGroup? Group(short guid) => _groups.GetValueOrDefault(guid);

	public PlannedObject? Object(ObjectKey key) => _objects.GetValueOrDefault(key);

	public PlannedRoute? Route(short guid) => _routes.GetValueOrDefault(guid);

	public PlannedArea? Area(short guid) => _areas.GetValueOrDefault(guid);

	public PlannedAction? Action(short guid) => _actions.GetValueOrDefault(guid);
}

/// <summary>
/// Where the mission's groups and the roster records they place stand, from the <c>.MSN</c>'s <see cref="MissionLayout"/>
/// alone, through the game's own placement rules (<see cref="MissionPlacementRules"/>) over the values the layout resolves,
/// keyed by GUID (docs/herculan/handoff-editor-msn-scene.md#4-positions). Each entry's base fields are placed, never a
/// variation: a keyed entry's fields already carry its shown option's point, heading, type and fit.
///
/// <list type="bullet">
/// <item>A group's setup is <see cref="MissionPlacementRules.GroupSetup"/> over its own point (row 6), heading (row 7) and
/// the waypoints of the route its slot-0 order names (rows 15, 8 and 6), its formation and its paints-ground flag; a ref
/// the layout has no entry for resolves to nothing, as the generation resolves it to <c>-1</c>.</item>
/// <item>Claims are <see cref="MissionPlacementRules.ClaimMembers{TKey}"/> over the groups in layout order, keyed by
/// <see cref="ObjectKey"/>; the first entry is group 0, the squad's, which claims nothing.</item>
/// <item>Each claimed record stands by <see cref="MissionPlacementRules.PlaceHerc"/>, <see cref="MissionPlacementRules.PlaceFlyer"/>
/// or <see cref="MissionPlacementRules.PlaceStructure"/> with its own point and heading, and each pad is read from its
/// group's <see cref="MissionPlacementRules.PadLead{TKey}"/>.</item>
/// <item>The squad, by the user's choice (docs/herculan/handoff-editor-msn-scene.md#5-the-squad-in-the-scene): one object
/// per herc group 0's member slots reference, each spread from group 0 by <see cref="MissionPlacementRules.PlaceSquadMember"/>
/// at its slot's position, which is the entry the training squad build gives it (<c>ShellTrainingLaunch.BuildSquad</c>:
/// slot 0 the player, slot <i>i</i> squadmate <i>i</i>). A slot past a gap, which no build reads, spreads by its position
/// too, and so does the squadmate the practice load leaves behind. A record group 0 and a later group both reference is
/// the squad's alone.</item>
/// <item>The routes, trigger areas and actions are <see cref="LayoutFeatures"/>'.</item>
/// </list>
/// </summary>
internal static class LayoutPlacement {
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	/// <summary>
	/// The layout's groups, placed records, routes, areas and actions, with <paramref name="overrides"/> read in place of the
	/// layout's.
	/// </summary>
	public static LayoutPlan Place(MissionLayout layout, PlacementTables tables, PlacementOverrides? overrides = null) {
		overrides ??= PlacementOverrides.None;
		var groupEntries = layout.Entries<MissionGroup164>();
		var groups = new List<PlannedGroup>(groupEntries.Count);
		var members = new List<(MissionUnitKind Kind, IReadOnlyList<ObjectKey?> Members)>(groupEntries.Count);
		for (int number = 0; number < groupEntries.Count; number++) {
			var entry = groupEntries[number];
			var fields = entry.Fields;
			var kind = KindOf(fields.MemberKind);
			short ownPoint = Point(layout, fields.PositionRef, PlacementOverrides.None) != null ? fields.PositionRef : (short)-1;
			var own = overrides.GroupPoint is { } detached && detached.Group == entry.Guid
				? detached.Point
				: Point(layout, fields.PositionRef, overrides);
			var (route, starts) = Route(layout, fields.OrderRefs.Length > 0 ? fields.OrderRefs[0] : (short)-1, overrides);
			var setup = MissionPlacementRules.GroupSetup(kind, own, Heading(layout, fields.HeadingRef), route,
				fields.FormationId, fields.PaintsGround != 0, tables.Bases);
			int? row = RowOf(kind);
			var slots = fields.MemberRefs
				.Select(member => row is { } roster && member != -1 && layout.Entry(roster, member) != null
					? new ObjectKey(roster, member)
					: (ObjectKey?)null)
				.ToArray();
			members.Add((kind, slots));

			short action = fields.DeploymentActionRef;
			short? verb = action != -1 ? layout.Entry<MissionAction82>(action)?.Fields.Verb : null;
			groups.Add(new PlannedGroup(entry.Guid, number, kind,
				fields.Side == (short)MissionSide.Cybrid ? MissionSide.Cybrid : MissionSide.Human, action, verb != null,
				verb ?? -1, setup, ownPoint, starts, fields.OrderRefs.ToArray(), [], null));
		}

		// Group 0's hercs, the squad, each by the first slot that references it.
		var squad = new List<(ObjectKey Key, int Slot)>();
		if (groups.Count > 0 && groups[0].Kind == MissionUnitKind.Mech) {
			var slots = members[0].Members;
			for (int slot = 0; slot < MissionPlacementRules.GroupMemberSlots && slot < slots.Count; slot++) {
				if (slots[slot] is { } key && squad.All(member => member.Key != key)) {
					squad.Add((key, slot));
				}
			}
		}

		var squadKeys = squad.Select(member => member.Key).ToHashSet();
		var mechClaims = MissionPlacementRules.ClaimMembers(MissionUnitKind.Mech, members);
		var flyerClaims = MissionPlacementRules.ClaimMembers(MissionUnitKind.Flyer, members);
		var baseClaims = MissionPlacementRules.ClaimMembers(MissionUnitKind.Base, members);
		var objects = new List<PlannedObject>();

		foreach (var entry in layout.Entries<MechRosterEntry144>()) {
			var key = new ObjectKey(MechRow, entry.Guid);
			if (squadKeys.Contains(key) || !mechClaims.TryGetValue(key, out var claim)) {
				continue;
			}

			var herc = entry.Fields;
			var group = groups[claim.Group];
			var place = MissionPlacementRules.PlaceHerc(group.Setup, tables.Mechs, claim.MemberIndex,
				OwnPoint(layout, key, herc.PositionRef, overrides), Heading(layout, herc.HeadingRef));
			objects.Add(Herc(key, herc, tables, group, claim.MemberIndex, squad: false, place));
		}

		foreach (var entry in layout.Entries<FlyerRosterEntry102>()) {
			var key = new ObjectKey(FlyerRow, entry.Guid);
			if (!flyerClaims.TryGetValue(key, out var claim)) {
				continue;
			}

			var flyer = entry.Fields;
			var group = groups[claim.Group];
			var place = MissionPlacementRules.PlaceFlyer(group.Setup, tables.Flyers, claim.MemberIndex,
				OwnPoint(layout, key, flyer.PositionRef, overrides), Heading(layout, flyer.HeadingRef));
			objects.Add(new PlannedObject(key, MissionUnitKind.Flyer, flyer.TypeIndex, tables.FlyerNames?[flyer.TypeIndex], [], [],
				MissionPlacement.PristineCondition, 0, false, group.Guid, claim.MemberIndex, false, group.Side, place));
		}

		foreach (var entry in layout.Entries<BaseRosterEntry62>()) {
			var key = new ObjectKey(BaseRow, entry.Guid);
			if (!baseClaims.TryGetValue(key, out var claim)) {
				continue;
			}

			var structure = entry.Fields;
			var group = groups[claim.Group];
			var place = MissionPlacementRules.PlaceStructure(group.Setup, tables.Bases, claim.MemberIndex,
				OwnPoint(layout, key, structure.PositionRef, overrides), Heading(layout, structure.HeadingRef));
			objects.Add(new PlannedObject(key, MissionUnitKind.Base, structure.TypeIndex, null, [], [],
				structure.StartingCondition, 0, false, group.Guid, claim.MemberIndex, false, group.Side, place));
		}

		foreach (var (key, slot) in squad) {
			var place = MissionPlacementRules.PlaceSquadMember(groups[0].Setup, tables.Mechs, slot);
			objects.Add(Herc(key, layout.Entry<MechRosterEntry144>(key.Guid)!.Fields, tables, groups[0], slot, squad: true, place));
		}

		// Each group's records: the claims are by layout position, which is the group's number.
		var claimed = groups.Select(_ => new List<ObjectKey>()).ToList();
		foreach (var placed in objects) {
			var claims = placed.Key.Row switch { MechRow => mechClaims, FlyerRow => flyerClaims, _ => baseClaims };
			if (placed.Squad) {
				claimed[MissionPlacementRules.SquadGroupIndex].Add(placed.Key);
			} else if (claims.TryGetValue(placed.Key, out var claim)) {
				claimed[claim.Group].Add(placed.Key);
			}
		}

		var planned = objects.ToDictionary(placed => placed.Key);
		for (int number = 0; number < groups.Count; number++) {
			var group = groups[number];
			MissionBasePad? pad = null;
			if (MissionPlacementRules.PadLayout(group.Kind, group.Setup.PaintsGround, group.Setup.FormationId, tables.Bases) is { } tile
					&& MissionPlacementRules.PadLead(baseClaims, number) is { } lead && planned.TryGetValue(lead, out var leader)) {
				pad = new MissionBasePad(leader.Place.Position, tile, number);
			}

			groups[number] = group with { Members = claimed[number], Pad = pad };
		}

		var (routes, areas, actions) = LayoutFeatures.Place(layout, groups, overrides);
		return new LayoutPlan(layout, groups, objects, routes, areas, actions);
	}

	private static PlannedObject Herc(ObjectKey key, MechRosterEntry144 herc, PlacementTables tables, PlannedGroup group,
			int memberIndex, bool squad, MissionMemberPlace place) =>
		new(key, MissionUnitKind.Mech, herc.TypeIndex, tables.MechNames?[herc.TypeIndex], herc.WeaponRefs.ToArray(),
			herc.WeaponSecondary.ToArray(), herc.StartingCondition, herc.AiCruiseSpeed, herc.AiRadarActive != 0, group.Guid,
			memberIndex, squad, group.Side, place);

	// A roster record's own point: the drag's when it stands alone, else its PositionRef's.
	private static Vec3i? OwnPoint(MissionLayout layout, ObjectKey key, short point, PlacementOverrides overrides) =>
		overrides.ObjectPoint is { } alone && alone.Object == key ? alone.Point : Point(layout, point, overrides);

	/// <summary>The coordinates of row-6 GUID <paramref name="guid"/>, a drag's in place of its record's, or null when the layout has no such point.</summary>
	internal static Vec3i? Point(MissionLayout layout, short guid, PlacementOverrides overrides) {
		if (guid == -1) {
			return null;
		}

		if (overrides.Points.TryGetValue(guid, out var moved)) {
			return moved;
		}

		return layout.Entry<MapPoint22>(guid)?.Fields is { } point ? new Vec3i(point.X, point.Y, point.Z) : null;
	}

	// Row-7 GUID <guid>'s heading as a binary angle, or null when the layout has no such heading.
	private static int? Heading(MissionLayout layout, short guid) =>
		guid != -1 && layout.Entry<Heading10>(guid)?.Fields is { } heading
			? MissionPlacementRules.HeadingFromDegrees(heading.Degrees)
			: null;

	// The waypoints of the route order <order> names, those the layout has a point for, and the row-6 GUIDs of its first,
	// with its last when the route closes on it; the loader likewise drops a waypoint naming no point.
	private static (IReadOnlyList<Vec3i> Points, IReadOnlyList<short> Start) Route(MissionLayout layout, short order,
			PlacementOverrides overrides) {
		if (order == -1 || layout.Entry<MissionOrder22>(order)?.Fields is not { RouteRef: not -1 } slot0
				|| layout.Entry<WaypointGroup>(slot0.RouteRef)?.Fields is not { } route) {
			return ([], []);
		}

		var points = new List<Vec3i>();
		var guids = new List<short>();
		foreach (short waypoint in route.Waypoints) {
			if (Point(layout, waypoint, overrides) is { } at) {
				points.Add(at);
				guids.Add(waypoint);
			}
		}

		if (points.Count == 0) {
			return ([], []);
		}

		return (points, points.Count > 1 && points[^1] == points[0] ? [guids[0], guids[^1]] : [guids[0]]);
	}

	// A row-16 MemberKind as the roster it names; an unknown one names none.
	private static MissionUnitKind KindOf(short discriminator) => discriminator switch {
		0 => MissionUnitKind.Mech,
		1 => MissionUnitKind.Flyer,
		2 => MissionUnitKind.Base,
		_ => (MissionUnitKind)(-1)
	};

	private static int? RowOf(MissionUnitKind kind) => kind switch {
		MissionUnitKind.Mech => MechRow,
		MissionUnitKind.Flyer => FlyerRow,
		MissionUnitKind.Base => BaseRow,
		_ => null
	};
}
