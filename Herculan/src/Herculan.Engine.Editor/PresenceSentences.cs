using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// A Present when, and what a record's variations change, in sentences a designer reads
/// (docs/herculan/handoff-editor-conditions.md#present-when): "Present only when campaign variable 30 is more than 0 and in
/// about 50% of loads.", "Its chassis and weapons change when campaign variable 250 is 1."
/// </summary>
internal static class PresenceSentences {
	/// <summary>The operators' words, flag on the left, in <see cref="ConditionEditing.Operators"/>' order.</summary>
	private static readonly string[] OperatorWords = ["is", "is not", "is less than", "is at most", "is more than", "is at least"];

	/// <summary>Operator <paramref name="op"/> in words ("is more than"), flag on the left; its code for one the load does not read.</summary>
	public static string Operator(short op) =>
		ConditionEditing.Operators.ToList().IndexOf(op) is >= 0 and var index ? OperatorWords[index] : $"(code {op})";

	/// <summary>The whole sentence: "Present: always.", "Present only when ... .", "Present in about 50% of loads."; for null, that it varies.</summary>
	public static string Presence(PresenceRule? rule) => rule switch {
		null => "Present only in some versions of the mission.",
		{ IsAlways: true } => "Present: always.",
		{ Variables.Count: 0 } => $"Present {Clauses(rule)}.",
		_ => $"Present only when {Clauses(rule)}.",
	};

	/// <summary>
	/// What changes when <paramref name="rule"/> holds, the fields <paramref name="aspects"/> of a row-<paramref name="row"/>
	/// record: "Its chassis and weapons change when ... .", or "Some of its details change ..." where none is named.
	/// </summary>
	public static string Variation(int row, IReadOnlyList<RecordAspect> aspects, PresenceRule? rule) {
		string when = rule switch {
			null => "in some versions of the mission",
			{ IsAlways: true } => "in every version of the mission",
			{ Variables.Count: 0 } => Clauses(rule),
			_ => $"when {Clauses(rule)}",
		};

		var named = aspects.Where(aspect => aspect != RecordAspect.Other).Select(aspect => Aspect(row, aspect)).ToList();
		if (named.Count == 0) {
			return $"Some of its details change {when}.";
		}

		if (aspects.Contains(RecordAspect.Other)) {
			named.Add(("other details", true));
		}

		bool plural = named.Count > 1 || named[0].Plural;
		return $"Its {List(named.Select(part => part.Words).ToList())} {(plural ? "change" : "changes")} {when}.";
	}

	/// <summary>
	/// A debrief outcome's or loss case's heading: "When the mission was won and campaign variable 250 is 1", its variables
	/// as the debrief's reload reads them (<see cref="CampaignVariableLabels.Phrase(int, bool)"/>), its roll made at the debrief.
	/// </summary>
	public static string When(PresenceRule rule) => $"When {Clauses(rule, atDebrief: true)}";

	// "campaign variable 30 is more than 0 and in about 50% of loads", "the previous mission was won"; the variables, then
	// the chance.
	private static string Clauses(PresenceRule rule, bool atDebrief = false) {
		var parts = rule.Variables.Select(clause => CampaignVariableLabels.MeaningOf(clause, atDebrief)?.Sentence
			?? $"{CampaignVariableLabels.Phrase(clause.Variable, atDebrief)} {Operator(clause.Operator)} {clause.Value}").ToList();
		// The roll is made only where the variables hold (PresenceRules), so its share is of those loads.
		string rolls = atDebrief ? "debriefs" : "loads";
		if (rule.Chance is { } chance) {
			return parts.Count == 0
				? $"in about {chance.Percent}% of {rolls}"
				: $"{string.Join(" and ", parts)}, and then in about {chance.Percent}% of those {rolls}";
		}

		return string.Join(" and ", parts);
	}

	// "a", "a and b", "a, b and c".
	private static string List(IReadOnlyList<string> parts) =>
		parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

	// An aspect as it reads after "Its", and whether it takes a plural verb.
	private static (string Words, bool Plural) Aspect(int row, RecordAspect aspect) => aspect switch {
		RecordAspect.Type => (row == 12 ? "chassis" : "type", false),
		RecordAspect.Weapons => ("weapons", true),
		RecordAspect.Guidance => ("missile guidance", false),
		RecordAspect.Radar => ("radar setting", false),
		RecordAspect.Speed => ("speed", false),
		RecordAspect.Position => ("position", false),
		RecordAspect.Heading => ("heading", false),
		RecordAspect.StartingCondition => ("starting condition", false),
		RecordAspect.OutOfFightWrites => (row switch {
			12 => "campaign variables set when destroyed or immobilized",
			16 => "campaign variables set when every member is destroyed or immobilized",
			_ => "campaign variables set when destroyed",
		}, true),
		RecordAspect.FiredTriggers => ("triggers fired when engaged or defeated", true),
		RecordAspect.Members => ("members", true),
		RecordAspect.Orders => ("orders", true),
		RecordAspect.Formation => ("formation", false),
		RecordAspect.Route => ("route", false),
		RecordAspect.Team => ("team", false),
		RecordAspect.WaitsOn => ("arrival trigger", false),
		RecordAspect.OrderKind => ("order", false),
		RecordAspect.Target => ("target", false),
		RecordAspect.EndsOn => ("ending trigger", false),
		RecordAspect.BasePad => ("base pad", false),
		_ => ("other details", true),
	};
}
