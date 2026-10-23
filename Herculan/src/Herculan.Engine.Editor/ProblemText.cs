using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Problems list's sentence for a mission check finding (<see cref="ScriptDatLint"/>), in the editor's own words by the
/// finding's kind (docs/herculan/handoff-editor-viewing.md#mission-check): the record it is about, named from the layout as the
/// views name it ("Trigger 3", "Cybrid hercs (group 4)"), what is wrong in play, and what to change, a control by its label.
/// The lint's own message, in <c>script.dat</c> terms, stays the HercWorks tools'. A record the selection has none for is
/// named by its kind alone ("A trigger").
/// </summary>
internal static class ProblemText {
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;

	/// <summary><paramref name="finding"/>'s sentence; <paramref name="target"/> is the selection that shows its record, or null.</summary>
	public static string Of(ScriptLintFinding finding, EditorSelection? target, MissionNames names, MissionLayout layout) {
		string who = Who(finding.Block, target, names, layout);
		return finding.Kind switch {
			ScriptLintKind.BrokenRef =>
				$"{who} references {Thing(finding.RefersTo)} that is not in the mission: choose another in its view.",
			ScriptLintKind.NoTarget =>
				$"{who} has no target chosen under Fires when, so it tests nothing in the mission: choose one there.",
			ScriptLintKind.MessageNeverPosted =>
				$"{who} never sends its message: HQ sends it once for each campaign variable the trigger sets, and it sets none. "
				+ "Add one with Add... > Set a campaign variable, or remove the message.",
			ScriptLintKind.AreaAfterGap =>
				$"{who} never tests an area listed after an empty slot: remove that area and add it again.",
			ScriptLintKind.RosterFull => RosterFull(finding),
			ScriptLintKind.NoHeadingNoOrder =>
				$"{who} has neither a heading nor a first order, so the mission crashes as it loads: give it a first order.",
			ScriptLintKind.UnknownRoster =>
				$"{who} is of a kind of group the mission has no case for ({finding.Value}): only herc, flyer and structure groups exist.",
			ScriptLintKind.HercGroupNoOrder =>
				$"{who} has no first order, so the mission crashes once its hercs start to act: give it a first order.",
			ScriptLintKind.UnknownCondition =>
				$"{who} asks a test the mission has no case for ({finding.Value}), so it answers as the one before it does: choose "
				+ "one under What happens.",
			ScriptLintKind.UnknownSubject =>
				$"{who} has a kind of subject the mission has no case for ({finding.Value}), so it answers as the one before it "
				+ "does: choose one under Who.",
			ScriptLintKind.TextTooLong =>
				$"The mission's text runs to {finding.Value} lines, and only the first {MissionTextEditing.LineCap} are read: remove "
				+ "lines under Text when you click Mission at the top of the Mission panel.",
			_ => finding.Message,
		};
	}

	// Too many records for a roster's slots in play: how many, how many it holds, and how many to delete.
	private static string RosterFull(ScriptLintFinding finding) {
		(string what, int cap) = finding.Block switch {
			ScriptBlock.Mechs => ("hercs, the squad's included", ScriptDatLint.MechSlotCap),
			ScriptBlock.Flyers => ("flyers", ScriptDatLint.FlyerSlotCap),
			_ => ("structures and ground vehicles", ScriptDatLint.BaseSlotCap),
		};

		return $"Too many {what}: {finding.Value}, and the mission holds {cap}. Loading more overwrites other data of the "
			+ $"mission: delete {finding.Value - cap}.";
	}

	// The record a finding is about, as a sentence's subject: by the views' names, or by its kind for none.
	private static string Who(ScriptBlock block, EditorSelection? target, MissionNames names, MissionLayout layout) {
		switch (target) {
			case RouteSelection route when MissionText.NumberIn(layout, RouteRow, route.Guid) is { } number:
				return $"Route {number}";
			case AreaSelection area when MissionText.NumberIn(layout, AreaRow, area.Guid) is { } number:
				return $"Trigger area {number}";
			case ActionSelection action when MissionText.NumberIn(layout, ActionRow, action.Guid) is { } number:
				return $"Trigger {number}";
			case TimerSelection timer when MissionText.NumberIn(layout, TimerRow, timer.Guid) is { } number:
				return $"Countdown {number}";
			case GroupSelection group when block == ScriptBlock.Orders:
				return $"An order of {names.GroupInSentence(group.Guid)}";
			case GroupSelection group:
				return Capitalized(names.GroupInSentence(group.Guid));
			case ObjectSelection placed:
				return names.Object(new ObjectKey(placed.Row, placed.Guid)).Full;
			case ObjectiveSelection objective when layout.Objectives.FirstOrDefault(found => found.Index == objective.Record) is { } found:
				string role = found.Fields.Required == ObjectiveEditing.Mandatory ? "Win condition" : "Lose condition";
				return $"{role} {names.Objective(found).Number}";
		}

		return block switch {
			ScriptBlock.Routes => "A route",
			ScriptBlock.TriggerAreas => "A trigger area",
			ScriptBlock.Actions => "A trigger",
			ScriptBlock.ActionTimers => "A countdown",
			ScriptBlock.Mechs => "A herc",
			ScriptBlock.Flyers => "A flyer",
			ScriptBlock.Bases => "A structure",
			ScriptBlock.Orders => "An order",
			ScriptBlock.Groups => "A group",
			ScriptBlock.Objectives => "A win or lose condition",
			_ => "The mission",
		};
	}

	// What a ref of a block names, with its article.
	private static string Thing(ScriptBlock? block) => block switch {
		ScriptBlock.Points => "a position",
		ScriptBlock.Headings => "a heading",
		ScriptBlock.Routes => "a route",
		ScriptBlock.TriggerAreas => "a trigger area",
		ScriptBlock.Actions => "a trigger",
		ScriptBlock.ActionTimers => "a countdown",
		ScriptBlock.Mechs => "a herc",
		ScriptBlock.Flyers => "a flyer",
		ScriptBlock.Bases => "a structure",
		ScriptBlock.Orders => "an order",
		ScriptBlock.Groups => "a group",
		ScriptBlock.Objectives => "a win or lose condition",
		_ => "a target",
	};

	private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
