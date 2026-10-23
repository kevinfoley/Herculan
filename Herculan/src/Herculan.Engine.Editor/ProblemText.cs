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
				+ "lines under \"Text\" when you click \"Mission\" at the top of the Mission panel.",
			_ => found.Message,
		};
	}

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
