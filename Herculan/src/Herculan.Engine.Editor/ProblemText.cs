using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Problems list's sentence for a mission check finding (<see cref="MissionCheck"/>), in the editor's own words by the
/// finding's kind (docs/herculan/handoff-editor-viewing.md#mission-check): the record it names, as the views name it
/// ("Trigger 3", "Cybrid hercs (group 4)"), marked "(in some versions)" when only some versions of the mission have the
/// problem, what is wrong in play, and what to change, a control by its label. The check's own message, in <c>.MSN</c> terms,
/// is the HercWorks tools'. Save's warning lists the same sentences.
/// </summary>
internal static class ProblemText {
	private const int ConditionRow = 1;
	private const int SettingsRow = 2;
	private const int PointRow = 6;
	private const int HeadingRow = 7;
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	/// <summary><paramref name="finding"/>'s sentence, its record shown by its selection (<see cref="EditorFinding.Target"/>), if any.</summary>
	public static string Of(EditorFinding finding, MissionNames names, MissionLayout layout) {
		var (found, target) = finding;
		string named = Who(found, target, names, layout);
		string who = named + (found.InSomeVersions ? " (in some versions)" : "");
		return found.Kind switch {
			MissionCheckKind.MissingRecord when found.RefersTo == ConditionRow => MissingCondition(found, named),
			MissionCheckKind.MissingRecord =>
				$"{who} references a missing {Thing(found.RefersTo)}.",
			MissionCheckKind.NoTarget =>
				$"{who} has no target chosen under \"Fires when\", so it tests nothing in the mission: choose one there.",
			MissionCheckKind.MessageNeverPosted =>
				$"{who} never sends its message: HQ sends it once for each campaign variable the trigger sets, and it sets none. "
				+ "Add one with \"Add...\" > \"Set a campaign variable\", or remove the message.",
			MissionCheckKind.AreaAfterGap =>
				$"{who} never tests an area listed after an empty slot: remove that area and add it again.",
			MissionCheckKind.RosterFull =>
				$"Over maximum {RosterLimits.Noun(found.Row)} limit ({RosterLimits.Cap(found.Row)}) by {found.Value - RosterLimits.Cap(found.Row)}.",
			MissionCheckKind.NoHeadingNoOrder =>
				$"{who} has neither a heading nor a first order, so the mission crashes as it loads: give it a first order.",
			MissionCheckKind.UnknownRoster =>
				$"{who} is of a kind of group the mission has no case for ({found.Value}): only HERC, flyer and structure groups exist.",
			MissionCheckKind.HercGroupNoOrder =>
				$"{who} has no first order, so the mission crashes once its HERCs start to act: give it a first order.",
			MissionCheckKind.UnknownCondition =>
				$"{who} asks a test the mission has no case for ({found.Value}), so it answers as the one before it does: choose "
				+ "one under \"What happens\".",
			MissionCheckKind.UnknownSubject =>
				$"{who} has a kind of subject the mission has no case for ({found.Value}), so it answers as the one before it "
				+ "does: choose one under \"Who\".",
			MissionCheckKind.TextTooLong =>
				$"The mission's text runs to {found.Value} lines, and only the first {MissionTextEditing.LineCap} are read: remove "
				+ "lines under \"Text\" in Edit > Mission Settings.",
			MissionCheckKind.OrderTargetsOwnSide =>
				$"{who}'s {MissionText.Ordinal(found.Value + 1)} order is \"Search and destroy\" on its own team, which it never "
				+ "attacks, and it goes after no other enemy: choose an enemy under \"Target\", or \"Patrol\" under \"Does\".",
			MissionCheckKind.EmptyInSomeStates =>
				$"{who} has no members in the loads where they are absent, which the mission does not handle: a win or lose "
				+ "condition, an order or a trigger that asks about the group can crash it or fire by mistake. Set \"Present "
				+ "when\" on the group instead of its members.",
			MissionCheckKind.NeverWrittenOff when found.Value == ObjectiveEditing.StructureSubject =>
				$"{who} can never come true: the structure it asks about can't be destroyed.",
			MissionCheckKind.NeverWrittenOff =>
				$"{who} can never come true: the group it asks about holds a structure that can't be destroyed, and a Cybrid group "
				+ "is written off only once every member is destroyed or immobilized.",
			MissionCheckKind.PodCarriesLeaderOnly =>
				$"{who} arrives by drop pod with {found.Value} members, and the pod brings in only the first: the others come in "
				+ "where the mission placed them instead. Keep one member and place each other in a new group with "
				+ "\"Waits on\" set to the same trigger, or choose another arrival under \"Arrives\" in the trigger.",
			MissionCheckKind.SkipArrayFull when found.Row == RouteRow =>
				$"{who} is over maximum waypoint limit ({SkipArrayLimits.Cap(RouteRow)}) by "
				+ $"{found.Value - SkipArrayLimits.Cap(RouteRow)}, so the mission crashes as it loads: remove waypoints.",
			MissionCheckKind.SkipArrayFull =>
				$"Over maximum {Thing(found.Row)} limit ({SkipArrayLimits.Cap(found.Row)}) by "
				+ $"{found.Value - SkipArrayLimits.Cap(found.Row)}, so the mission crashes as it loads: {Fewer(found.Row)}.",
			MissionCheckKind.MapMarkersFull =>
				$"Over maximum map marker limit ({MapMarkerLimits.Cap}) by {found.Value - MapMarkerLimits.Cap}, so the mission "
				+ "crashes as it starts: remove HERCs, flyers, structures or vehicles, or waypoints from the route of the squad's "
				+ "first order.",
			_ => found.Message,
		};
	}

	// How to bring a skipped row's count down, by what adds its records.
	private static string Fewer(int row) => row switch {
		PointRow => "remove waypoints, trigger areas, or positions of their own from groups and objects",
		HeadingRow => "remove headings of their own from groups and objects",
		_ => "remove trigger areas",
	};

	// A condition ref no row-1 record carries, which drops the record that carries it as a condition that never holds would: a
	// condition's own, a record of another version's, or the record's own, which a view opening with Present when can change.
	private static string MissingCondition(MissionCheckFinding finding, string who) {
		if (finding.Row == ConditionRow) {
			return $"{who} depends on a missing condition, so it never holds.";
		}

		if (finding.InSomeVersions) {
			return $"{who} has a version that depends on a missing condition, so that version never appears.";
		}

		return finding.Row is >= RouteRow and <= ObjectiveRow
			? $"{who} depends on a missing condition, so it never appears: choose another under \"Present when\"."
			: $"{who} depends on a missing condition, so it never appears.";
	}

	// The record a finding names, as a sentence's subject: by the views' names, or by its kind for one the layout has no number for.
	private static string Who(MissionCheckFinding finding, EditorSelection? target, MissionNames names, MissionLayout layout) {
		short guid = (short)finding.Key;
		switch (finding.Row) {
			case ConditionRow:
				return $"Condition {guid}";
			case SettingsRow:
				return "The mission's settings";
			case PointRow:
				return "A position";
			case HeadingRow:
				return "A heading";
			case RouteRow:
				return MissionText.NumberIn(layout, RouteRow, guid) is { } route ? $"Route {route}" : "A route";
			case AreaRow:
				return MissionText.NumberIn(layout, AreaRow, guid) is { } area ? $"Trigger area {area}" : "A trigger area";
			case ActionRow:
				return MissionText.NumberIn(layout, ActionRow, guid) is { } trigger ? $"Trigger {trigger}" : "A trigger";
			case TimerRow:
				return MissionText.NumberIn(layout, TimerRow, guid) is { } countdown ? $"Countdown {countdown}" : "A countdown";
			case >= MechRow and <= BaseRow when finding.Key != -1:
				return names.Object(new ObjectKey(finding.Row, guid)).Full;
			case OrderRow:
				return target is GroupSelection group ? $"An order of {names.GroupInSentence(group.Guid)}" : "An order";
			case GroupRow:
				return Capitalized(names.GroupInSentence(guid));
			case ObjectiveRow when layout.Objectives.FirstOrDefault(found => found.Index == finding.Key) is { } objective:
				string role = objective.Fields.Required == ObjectiveEditing.Mandatory ? "Win condition" : "Lose condition";
				return $"{role} {names.Objective(objective).Number}";
			case ObjectiveRow:
				return "A win or lose condition";
			case MissionCheckFinding.TextRow:
				return "A line of the mission's text";
			default:
				return "The mission";
		}
	}

	// What a ref to a row names.
	private static string Thing(int? row) => row switch {
		PointRow => "position",
		HeadingRow => "heading",
		RouteRow => "route",
		AreaRow => "trigger area",
		ActionRow => "trigger",
		TimerRow => "countdown",
		MechRow => "HERC",
		FlyerRow => "flyer",
		BaseRow => "structure",
		OrderRow => "order",
		GroupRow => "group",
		_ => "target",
	};

	private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
