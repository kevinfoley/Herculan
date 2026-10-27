using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// A record's name as the editor shows it: what it is and does (<paramref name="Text"/>), and its number in its row
/// (<paramref name="Number"/>), which a view draws dimmed after the text so a link still reads; null for a record
/// with no number, such as the player's squad.
/// </summary>
internal readonly record struct RecordName(string Text, string? Number = null) {
	/// <summary>The text and the number, as one string.</summary>
	public string Full => Number is null ? Text : $"{Text} {Number}";

	public override string ToString() => Full;
}

/// <summary>
/// A roster record's type by name: a herc's chassis and a flyer's type as <c>nam\MECHS.NAM</c> and <c>nam\FLYERS.NAM</c> list
/// them, a structure's or ground vehicle's the name the MFD's status screen prints for it
/// (<see cref="MfdStatusSubject.BaseName"/>); a type a table lacks by its kind and number.
/// </summary>
internal sealed class RosterTypeNames {
	private readonly UnitTypeNames? _mechs;
	private readonly UnitTypeNames? _flyers;
	private readonly BaseTypeTable? _bases;
	private readonly StringFile? _strings;

	public RosterTypeNames(UnitTypeNames? mechs, UnitTypeNames? flyers, BaseTypeTable? bases, StringFile? strings) {
		_mechs = mechs;
		_flyers = flyers;
		_bases = bases;
		_strings = strings;
	}

	/// <summary>No tables: every type by its kind and number.</summary>
	public static RosterTypeNames None { get; } = new(null, null, null, null);

	/// <summary>The tables of DBSIM's mount.</summary>
	public static RosterTypeNames Load(GameContent content) => new(UnitTypeNames.LoadMechs(content),
		UnitTypeNames.LoadFlyers(content), BaseTypeTable.Load(content), SimStrings.Load(content));

	/// <summary><paramref name="record"/>'s type by name, or by its kind and type number for one no table names.</summary>
	public string Name(MapObject? record) => record switch {
		MechRosterEntry144 herc => _mechs?[herc.TypeIndex] ?? $"Herc type {herc.TypeIndex}",
		FlyerRosterEntry102 flyer => _flyers?[flyer.TypeIndex] ?? $"Flyer type {flyer.TypeIndex}",
		BaseRosterEntry62 structure when _bases?[structure.TypeIndex] is { } type
				&& MfdStatusSubject.BaseName(type, _strings) is { } name && !string.IsNullOrWhiteSpace(name) => name,
		BaseRosterEntry62 structure => $"{(IsVehicle(structure) ? "Ground vehicle" : "Structure")} type {structure.TypeIndex}",
		_ => "A record",
	};

	/// <summary>
	/// What a structure or ground vehicle is titled by: its kind and the name the MFD's status screen prints for it
	/// (<see cref="MfdStatusSubject.BaseName"/>), or its kind alone when the MFD has no name for it; its type number is under
	/// Advanced.
	/// </summary>
	public string StructureTitle(BaseRosterEntry62 structure) {
		string kind = IsVehicle(structure) ? "Ground vehicle" : "Structure";
		string? name = _bases?[structure.TypeIndex] is { } type ? MfdStatusSubject.BaseName(type, _strings) : null;
		return string.IsNullOrWhiteSpace(name) ? $"{kind} with no name on the MFD" : $"{kind}: {name}";
	}

	/// <summary>Whether <paramref name="structure"/>'s type is a ground vehicle, which drives.</summary>
	public bool IsVehicle(BaseRosterEntry62 structure) => _bases?[structure.TypeIndex]?.IsVehicle == true;

	/// <summary>
	/// Whether <c>BASES.DAT</c> type <paramref name="type"/> is invulnerable (<see cref="BaseType.Invulnerable"/>), so a
	/// structure of it takes no damage and is never destroyed (docs/retail/formats/bases-dat.md).
	/// </summary>
	public bool IsInvulnerable(int type) => _bases?[type]?.Invulnerable == true;
}

/// <summary>
/// How the editor names a mission's groups, objects, trigger areas, routes, triggers, countdowns and objectives: by what each
/// is and does, built from the <c>.MSN</c>'s layout (<see cref="MissionIndex"/>), with its number in its row as
/// <see cref="RecordName.Number"/>, or leading the text for a group, route or trigger (docs/herculan/handoff-editor-ux-plan.md#5-names-instead-of-numbers). The one source of
/// names for titles, links, pickers, outliner rows, delete blockers and the Toolbox. A name refers to another record by its
/// number alone where naming it in full would loop (an area by the triggers testing it, a trigger by its areas), and a
/// sentence names a group by <see cref="GroupInSentence"/>, its team and roster.
/// </summary>
internal sealed class MissionNames {
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	private readonly MissionIndex _index;

	public MissionNames(MissionIndex index, RosterTypeNames types) {
		_index = index;
		Types = types;
	}

	/// <summary>The type names a roster record is named by.</summary>
	public RosterTypeNames Types { get; }

	private MissionLayout Layout => _index.Layout;

	// ---- Groups ------------------------------------------------------------------------------------

	/// <summary>
	/// Row-16 GUID <paramref name="guid"/> by its number, then its team, roster and members.
	/// </summary>
	public RecordName Group(short guid) {
		if (_index.Group(guid) is not { } group) {
			return new RecordName("A group not in the mission");
		}

		if (IsSquad(group)) {
			return new RecordName("Player's squad");
		}

		return new RecordName($"Group {group.Number}: {Team(group)} {Roster(group)} ({Members(group)})");
	}

	/// <summary>Row-16 GUID <paramref name="guid"/> by its team and roster alone.</summary>
	public RecordName GroupBrief(short guid) {
		if (_index.Group(guid) is not { } group) {
			return new RecordName("A group not in the mission");
		}

		return IsSquad(group) ? new RecordName("Player's squad") : new RecordName($"Group {group.Number}: {Team(group)} {Roster(group)}");
	}

	/// <summary>
	/// Row-16 GUID <paramref name="guid"/> inside a sentence.
	/// </summary>
	public string GroupInSentence(short guid) =>
		_index.Group(guid) is { } group && IsSquad(group) ? "the player's squad" : GroupBrief(guid).Full;

	/// <summary>Whether a sentence about group <paramref name="guid"/> takes a plural verb: every group but the player's squad.</summary>
	public bool GroupPlural(short guid) => _index.Group(guid) is not { } group || !IsSquad(group);

	private static bool IsSquad(PlannedGroup group) => group.IsSquad;

	private static string Team(PlannedGroup group) => group.Side == MissionSide.Cybrid ? "Cybrid" : "Human";

	// The members' kind, plural: hercs, flyers, structures, or ground vehicles when every member drives.
	private string Roster(PlannedGroup group) => group.Kind switch {
		MissionUnitKind.Mech => "HERCs",
		MissionUnitKind.Flyer => "flyers",
		MissionUnitKind.Base => group.Members.Count > 0 && group.Members.All(key => Layout.Entry(key.Row, key.Guid)?.Fields
				is BaseRosterEntry62 structure && Types.IsVehicle(structure))
			? "ground vehicles"
			: "structures",
		_ => "group",
	};

	// Each member type once, in member order, counted when there are several.
	private string Members(PlannedGroup group) {
		if (group.Members.Count == 0) {
			return "no members";
		}

		var counts = new List<(string Type, int Count)>();
		foreach (var key in group.Members) {
			string type = TypeName(key);
			int at = counts.FindIndex(entry => entry.Type == type);
			if (at < 0) {
				counts.Add((type, 1));
			} else {
				counts[at] = (type, counts[at].Count + 1);
			}
		}

		return string.Join(", ", counts.Select(entry => entry.Count > 1 ? $"{entry.Type} x{entry.Count}" : entry.Type));
	}

	// ---- Objects -----------------------------------------------------------------------------------

	/// <summary>The roster record <paramref name="key"/>'s type by name (<see cref="RosterTypeNames.Name"/>).</summary>
	public string TypeName(ObjectKey key) => Types.Name(Layout.Entry(key.Row, key.Guid)?.Fields);

	/// <summary>
	/// The roster record <paramref name="key"/> as its group's outliner row names it: its type, numbered in member order when
	/// its group has more than one of that type; a squad HERC as the player's or a squadmate's, by its slot.
	/// </summary>
	public string MemberName(ObjectKey key) {
		string type = TypeName(key);
		if (_index.Plan.Object(key) is not { } planned) {
			return type;
		}

		if (planned.Squad) {
			return planned.MemberIndex == 0 ? $"{type}, the player's HERC" : $"{type}, squadmate {planned.MemberIndex}'s HERC";
		}

		if (_index.Group(planned.Group) is { } group) {
			var same = group.Members.Where(member => member.Row == key.Row && TypeName(member) == type).ToList();
			if (same.Count > 1) {
				return $"{type} {same.IndexOf(key) + 1} of {same.Count}";
			}
		}

		return type;
	}

	/// <summary>
	/// The roster record <paramref name="key"/> by its <see cref="MemberName"/> and its group (<see cref="GroupBrief"/>); a squad
	/// HERC by its <see cref="MemberName"/> alone; a record no group places by its type, saying it is never in the mission.
	/// </summary>
	public RecordName Object(ObjectKey key) {
		if (_index.Plan.Object(key) is not { } planned) {
			return new RecordName($"{TypeName(key)}, in no group: never in the mission");
		}

		if (planned.Squad) {
			return new RecordName(MemberName(key));
		}

		var group = GroupBrief(planned.Group);
		return new RecordName($"{MemberName(key)} in {group.Text}", group.Number);
	}

	/// <summary>Whether a group places the roster record <paramref name="key"/>, so it is in the mission.</summary>
	public bool Placed(ObjectKey key) => _index.Plan.Object(key) != null;

	/// <summary>
	/// An order's or objective's subject: kind 0 a group (<see cref="Group"/>), 1 a herc, 2 a flyer and 3 a structure or ground
	/// vehicle (<see cref="Object"/>), by GUID; none for kind -1.
	/// </summary>
	public RecordName Subject(short kind, short guid) => kind switch {
		-1 => new RecordName("None"),
		0 => Group(guid),
		1 => Object(new ObjectKey(MechRow, guid)),
		2 => Object(new ObjectKey(FlyerRow, guid)),
		3 => Object(new ObjectKey(BaseRow, guid)),
		_ => new RecordName($"An unknown kind of target ({kind})"),
	};

	// ---- Areas and routes --------------------------------------------------------------------------

	/// <summary>
	/// Row-9 GUID <paramref name="guid"/> by its shape and who its first testing trigger tests, numbered by its place in row 9.
	/// </summary>
	public RecordName Area(short guid) {
		if (MissionText.NumberIn(Layout, AreaRow, guid) is not { } number) {
			return new RecordName("An area not in the mission");
		}

		string shape;
		IReadOnlyList<short> testedBy = [];
		if (_index.Area(guid) is { } area) {
			shape = area.Area.Shape == MissionTriggerShape.Circle ? "Circle" : "Box";
			testedBy = area.TestedBy;
		} else {
			bool circle = Layout.Entry<TriggerArea12>(guid)?.Fields.Shape is { } stored && stored != 0;
			return new RecordName(circle ? "Circle with its center missing" : "Box with a corner missing", $"(area {number})");
		}

		string who = testedBy.Count > 0 && Layout.Entry<MissionAction82>(testedBy[0])?.Fields is { } first
			? $"entered by {Who(first)}"
			: "that no trigger tests";
		return new RecordName($"{shape} {who}", $"(area {number})");
	}

	/// <summary>
	/// Row-8 GUID <paramref name="guid"/> by its number, then who walks it and whether it loops.
	/// The number leads, so it is the text, with no dimmed <see cref="RecordName.Number"/>.
	/// </summary>
	public RecordName Route(short guid) {
		if (MissionText.NumberIn(Layout, RouteRow, guid) is not { } number) {
			return new RecordName("A route not in the mission");
		}

		if (_index.Route(guid) is not { } route) {
			return new RecordName($"Route {number} (no waypoints)");
		}

		string walkedBy = route.WalkedBy.Count switch {
			0 => "not walked",
			1 => _index.Group(route.WalkedBy[0]) is { IsSquad: true } ? "player's squad" : GroupBrief(route.WalkedBy[0]).Text,
			_ => $"{route.WalkedBy.Count} groups",
		};
		return new RecordName($"Route {number} ({walkedBy}{(route.Closed ? ", loop" : "")})");
	}

	/// <summary>Row-8 GUID <paramref name="guid"/> inside a sentence, by its number.</summary>
	public string RouteInSentence(short guid) =>
		MissionText.NumberIn(Layout, RouteRow, guid) is { } number ? $"route {number}" : "no route";

	// ---- Triggers and countdowns -------------------------------------------------------------------

	/// <summary>
	/// Row-10 GUID <paramref name="guid"/> by its number, then what fires it (<see cref="Cause"/>) and what it does. The number
	/// leads, so it is the text, with no dimmed <see cref="RecordName.Number"/>.
	/// </summary>
	public RecordName Trigger(short guid) {
		if (MissionText.NumberIn(Layout, ActionRow, guid) is not { } number) {
			return new RecordName("A trigger not in the mission");
		}

		string cause = Cause(guid);
		return new RecordName($"Trigger {number}: {char.ToUpperInvariant(cause[0])}{cause[1..]} ({Effect(guid)})");
	}

	/// <summary>
	/// What fires row-10 GUID <paramref name="guid"/>, as a clause: who enters its areas, else the countdowns firing it, else the
	/// objects firing it when engaged, else when defeated (<see cref="AnyOf"/>), else that nothing does. Areas and countdowns by
	/// their numbers.
	/// </summary>
	public string Cause(short guid) {
		if (Layout.Entry<MissionAction82>(guid)?.Fields is not { } fields) {
			return "a trigger not in the mission";
		}

		if (_index.Action(guid) is { Areas.Count: > 0 } action) {
			var numbers = action.Areas.Select(area => MissionText.NumberIn(Layout, AreaRow, area)).OfType<int>().ToList();
			return $"{Who(fields)} enters area {Alternatives(numbers)}";
		}

		var links = _index.Links(guid);
		var countdowns = links.FiringCountdowns.Select(countdown => MissionText.NumberIn(Layout, TimerRow, countdown)).OfType<int>().ToList();
		if (countdowns.Count > 0) {
			return $"countdown {Alternatives(countdowns)} runs out";
		}

		if (links.Engaged.Count > 0) {
			return $"{AnyOf(links.Engaged)} is engaged";
		}

		if (links.Defeated.Count > 0) {
			return $"{AnyOf(links.Defeated)} is defeated";
		}

		return "nothing fires it";
	}

	// Numbers as alternatives, the last joined by "or".
	private static string Alternatives(IReadOnlyList<int> numbers) =>
		numbers.Count == 1 ? $"{numbers[0]}" : $"{string.Join(", ", numbers.Take(numbers.Count - 1))} or {numbers[^1]}";

	// Whichever of the objects <keys> fires a trigger, as the subject of "is engaged": one by its type; several of one group as
	// any member of it when they are all of its members, else by their count among its members; else by count and kind, or by
	// count alone for mixed kinds.
	private string AnyOf(IReadOnlyList<ObjectKey> keys) {
		if (keys.Count == 1) {
			return TypeName(keys[0]);
		}

		var groups = keys.Select(key => _index.Plan.Object(key)?.Group).Distinct().ToList();
		if (groups is [short only] && _index.Group(only) is { } group) {
			return group.Members.All(keys.Contains)
				? $"a member of {GroupInSentence(only)}"
				: $"one of {keys.Count} members of {GroupInSentence(only)}";
		}

		var kinds = keys.Select(key => key.Row switch {
			MechRow => "HERCs",
			FlyerRow => "flyers",
			_ => Layout.Entry(key.Row, key.Guid)?.Fields is BaseRosterEntry62 structure && Types.IsVehicle(structure)
				? "ground vehicles"
				: "structures",
		}).Distinct().ToList();
		return $"one of {keys.Count} {(kinds.Count == 1 ? kinds[0] : "units")}";
	}

	// Whose position a trigger's areas test, as the subject of "enters": the player, a unit of a kind, or the particular herc,
	// flyer, structure or group (docs/retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70).
	private string Who(MissionAction82 fields) {
		short target = ActionEditing.TargetBlock(fields.Type) is { } block && Layout.Entry(block + 5, fields.TargetRef) != null
			? fields.TargetRef
			: (short)-1;
		return fields.Type switch {
			MissionAction.SubjectPlayer => "the player",
			MissionAction.SubjectPlayerGroup => "the player's squad",
			MissionAction.SubjectHumanGroups => "a human unit",
			MissionAction.SubjectCybridGroups => "a Cybrid unit",
			MissionAction.SubjectMechGroups => "a herc",
			MissionAction.SubjectFlyerGroups => "a flyer",
			MissionAction.SubjectBaseGroups => "a structure",
			MissionAction.SubjectTargetMech => target == -1 ? "a herc not chosen" : TypeName(new ObjectKey(MechRow, target)),
			MissionAction.SubjectTargetFlyer => target == -1 ? "a flyer not chosen" : TypeName(new ObjectKey(FlyerRow, target)),
			MissionAction.SubjectTargetBase => target == -1 ? "a structure not chosen" : TypeName(new ObjectKey(BaseRow, target)),
			MissionAction.SubjectTargetGroup => target == -1 ? "a group not chosen" : $"a member of {GroupInSentence(target)}",
			_ => "something",
		};
	}

	// What a trigger does: its first kind of effect and whether there are others, or that it does nothing.
	private string Effect(short guid) {
		var follows = _index.Links(guid).Follows;
		var effects = new List<string>();
		var groups = follows.OfType<BringsInGroup>().ToList();
		if (groups.Count > 0) {
			effects.Add(groups.Count == 1 ? $"brings in {GroupInSentence(groups[0].Group)}" : $"brings in {groups.Count} groups");
		}

		var orders = follows.OfType<EndsOrder>().ToList();
		if (orders.Count > 0) {
			effects.Add(orders.Count == 1 ? $"moves {GroupInSentence(orders[0].Group)} on" : $"moves {orders.Count} groups on");
		}

		var countdowns = follows.OfType<StartsCountdown>().ToList();
		if (countdowns.Count > 0) {
			effects.Add(countdowns.Count == 1 && MissionText.NumberIn(Layout, TimerRow, countdowns[0].Countdown) is { } number
				? $"starts countdown {number}"
				: $"starts {countdowns.Count} countdowns");
		}

		if (Layout.Entry<MissionAction82>(guid)?.Fields is { } fields) {
			var variables = Enumerable.Range(0, fields.CounterPairs.Length / 2).Select(pair => fields.CounterPairs[pair * 2])
				.Where(variable => variable >= 0).ToList();
			if (variables.Count > 0) {
				effects.Add(variables.Count == 1 ? $"sets campaign variable {variables[0]}" : $"sets {variables.Count} campaign variables");
			}

			if (fields.MessageId > 0) {
				effects.Add("sends a message");
			}
		}

		return effects.Count switch {
			0 => "does nothing",
			1 => effects[0],
			_ => $"{effects[0]}, and more",
		};
	}

	/// <summary>
	/// Row-11 GUID <paramref name="guid"/> by its delay and what starts it (<see cref="Cause"/>), numbered by its place in row 11.
	/// A start the layout has no trigger for is the mission's start, as the generation resolves it to none.
	/// </summary>
	public RecordName Countdown(short guid) {
		if (MissionText.NumberIn(Layout, TimerRow, guid) is not { } number || Layout.Entry<ActionTimer30>(guid)?.Fields is not { } fields) {
			return new RecordName("A countdown not in the mission");
		}

		string delay = MissionText.Duration(fields.Delay);
		string start = _index.Action(fields.PrimaryActionRef) != null ? $"after {Cause(fields.PrimaryActionRef)}" : "from mission start";
		return new RecordName($"Countdown: {delay} {start}", $"(countdown {number})");
	}

	// ---- Objectives --------------------------------------------------------------------------------

	/// <summary>
	/// <paramref name="objective"/> as one sentence (<see cref="ObjectiveSentence"/>), numbered by its place in row 17.
	/// </summary>
	public RecordName Objective(LayoutObjective objective) {
		int position = 0;
		while (position < Layout.Objectives.Count && Layout.Objectives[position].Index != objective.Index) {
			position++;
		}

		return new RecordName(ObjectiveSentence(objective.Fields), $"(objective {MissionText.Number(position)})");
	}

	/// <summary>Row-17 record <paramref name="record"/> by <see cref="Objective(LayoutObjective)"/>, or a note when the row has none there.</summary>
	public RecordName Objective(int record) =>
		Layout.Objectives.FirstOrDefault(objective => objective.Index == record) is { } found
			? Objective(found)
			: new RecordName("An objective not in the mission");

	/// <summary>
	/// What <paramref name="fields"/> asks, as one sentence: whether it must or must not happen, then its subject and its
	/// condition (docs/retail/simulation/mission-objectives.md#what-each-condition-asks).
	/// </summary>
	public string ObjectiveSentence(MissionObjective58 fields) {
		string role = fields.Required == ObjectiveEditing.Mandatory ? "Must happen" : "Must not happen";
		return $"{role}: {ObjectiveClause(fields)}";
	}

	// The subject and the condition as a clause; a data-link condition names the player, whatever the subject.
	private string ObjectiveClause(MissionObjective58 fields) {
		short code = fields.ConditionCode;
		if (!ObjectiveEditing.TestsSubject(code)) {
			return code is MissionObjective.ConditionDataLink or MissionObjective.ConditionDataLinkAlso
				? "the player completes a data link"
				: "the player has not completed a data link";
		}

		if (fields.SubjectKind == ObjectiveEditing.GroupSubject) {
			string group = _index.Group(fields.SubjectRef) != null ? GroupInSentence(fields.SubjectRef) : "a group not in the mission";
			bool plural = GroupPlural(fields.SubjectRef);
			return code switch {
				MissionObjective.ConditionOrderComplete => plural
					? $"{group} finish their order on {RouteInSentence(fields.RouteRef)}"
					: $"{group} finishes its order on {RouteInSentence(fields.RouteRef)}",
				MissionObjective.ConditionLost => $"{group} {(plural ? "are" : "is")} written off",
				MissionObjective.ConditionClear => $"{group} {(plural ? "are" : "is")} clear of threats and not written off",
				MissionObjective.ConditionEngaged => $"{group} {(plural ? "have" : "has")} a member engaged",
				MissionObjective.ConditionDisarmed => $"{group} {(plural ? "are" : "is")} all disarmed",
				MissionObjective.ConditionUnengaged => $"{group} {(plural ? "have" : "has")} no member engaged",
				_ => $"{group}: an unknown test ({code})",
			};
		}

		string subject = fields.SubjectKind switch {
			ObjectiveEditing.HercSubject => Object(new ObjectKey(MechRow, fields.SubjectRef)).Full,
			2 => Object(new ObjectKey(FlyerRow, fields.SubjectRef)).Full,
			ObjectiveEditing.StructureSubject => Object(new ObjectKey(BaseRow, fields.SubjectRef)).Full,
			_ => $"an unknown subject ({fields.SubjectKind})",
		};

		// A finished order is its group's: named by the group, as the view's Asked of names it.
		if (code == MissionObjective.ConditionOrderComplete && _index.SubjectGroup(fields) is not -1 and var owner) {
			return GroupPlural(owner)
				? $"{GroupInSentence(owner)} finish their order on {RouteInSentence(fields.RouteRef)}"
				: $"{GroupInSentence(owner)} finishes its order on {RouteInSentence(fields.RouteRef)}";
		}

		return code switch {
			MissionObjective.ConditionOrderComplete => $"the group of {subject} finishes its order on {RouteInSentence(fields.RouteRef)}",
			MissionObjective.ConditionLost => $"{subject} is destroyed or immobilized",
			MissionObjective.ConditionClear => $"{subject} is clear of threats",
			MissionObjective.ConditionEngaged => $"{subject} is engaged",
			MissionObjective.ConditionDisarmed => $"{subject} is disarmed",
			MissionObjective.ConditionUnengaged => $"{subject} is not engaged",
			_ => $"{subject}: an unknown test ({code})",
		};
	}
}
