using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>One clause of a <see cref="PresenceRule"/>: campaign variable <paramref name="Variable"/> compared with <paramref name="Value"/>, a row-1 flag test.</summary>
/// <param name="Operator">One of <see cref="ConditionEditing.Operators"/>, read flag on the left (<see cref="ConditionEditing.OperatorText"/>).</param>
public readonly record struct VariableClause(short Variable, short Operator, short Value);

/// <summary>
/// The chance clause of a <see cref="PresenceRule"/>: a row-1 draw below <paramref name="Bound"/> and a range from
/// <paramref name="Low"/> to <paramref name="High"/> over it, compared unsigned as the load compares them
/// (docs/retail/formats/msn-mission-file.md#the-conditions--row-1). A chance read from the file keeps its own bound and ends,
/// so writing a rule back that leaves the chance alone changes neither.
/// </summary>
public readonly record struct ChanceClause(short Bound, short Low, short High) {
	/// <summary>The bound a chance the editor writes draws below, so that each percent is one step (the editor's choice; every retail draw is below 11).</summary>
	public const short PercentBound = 100;

	/// <summary>The fewest and most percent a chance the editor writes holds in: a chance of 0 or 100 is never or always.</summary>
	public const int MinPercent = 1;
	public const int MaxPercent = 99;

	/// <summary>A chance of <paramref name="percent"/>: a draw below <see cref="PercentBound"/>, holding from 0 to one less than <paramref name="percent"/>.</summary>
	public static ChanceClause OfPercent(int percent) => percent is >= MinPercent and <= MaxPercent
		? new ChanceClause(PercentBound, 0, (short)(percent - 1))
		: throw new ArgumentOutOfRangeException(nameof(percent), percent, $"A chance is {MinPercent} to {MaxPercent}%.");

	/// <summary>The share of loads it holds in, 0 to 1: the draws from 0 to one below the bound that lie in the range.</summary>
	public double Share {
		get {
			int low = (ushort)Low, high = Math.Min((ushort)High, Bound - 1);
			return Bound < 1 || high < low ? 0 : (double)(high - low + 1) / Bound;
		}
	}

	/// <summary><see cref="Share"/> as a whole percent, rounded.</summary>
	public int Percent => (int)Math.Round(Share * 100, MidpointRounding.AwayFromZero);
}

/// <summary>
/// When a record is present, as a designer states it: every variable clause holds, and, with a chance, the roll falls in
/// it. Always when it has neither. The variables are innermost first, the order a chain is built and read.
/// </summary>
public sealed class PresenceRule : IEquatable<PresenceRule> {
	public PresenceRule(IReadOnlyList<VariableClause> variables, ChanceClause? chance) {
		Variables = variables.ToArray();
		Chance = chance;
	}

	/// <summary>Present in every version of the mission.</summary>
	public static PresenceRule Always { get; } = new([], null);

	public IReadOnlyList<VariableClause> Variables { get; }

	public ChanceClause? Chance { get; }

	public bool IsAlways => Variables.Count == 0 && Chance == null;

	/// <summary>This rule with clause <paramref name="index"/> replaced by <paramref name="clause"/>.</summary>
	public PresenceRule WithVariable(int index, VariableClause clause) =>
		new(Variables.Select((existing, i) => i == index ? clause : existing).ToList(), Chance);

	/// <summary>This rule with <paramref name="clause"/> added last.</summary>
	public PresenceRule WithVariableAdded(VariableClause clause) => new([.. Variables, clause], Chance);

	/// <summary>This rule without clause <paramref name="index"/>.</summary>
	public PresenceRule WithoutVariable(int index) => new(Variables.Where((_, i) => i != index).ToList(), Chance);

	/// <summary>This rule's variables with <paramref name="chance"/>, or none for null.</summary>
	public PresenceRule WithChance(ChanceClause? chance) => new(Variables, chance);

	public bool Equals(PresenceRule? other) =>
		other != null && Chance == other.Chance && Variables.SequenceEqual(other.Variables);

	public override bool Equals(object? obj) => Equals(obj as PresenceRule);

	public override int GetHashCode() => Variables.Aggregate(Chance.GetHashCode(), (hash, clause) => HashCode.Combine(hash, clause));
}

/// <summary>
/// A record's Present when as read (<see cref="PresenceRules.Read"/>): its <see cref="Rule"/>, null where the clauses cannot
/// describe it; <see cref="WhyFixed"/>, why it is not set as clauses, null where it is; and <see cref="Chain"/>, the row-1
/// GUIDs it is built of, outermost first.
/// </summary>
public sealed record PresenceReading(PresenceRule? Rule, string? WhyFixed, IReadOnlyList<short> Chain) {
	/// <summary>Whether its clauses can be set.</summary>
	public bool Settable => Rule != null && WhyFixed == null;
}

/// <summary>
/// A record's Present when as clauses, campaign variable tests and one chance, read from and written to the row-1 chain its
/// condition field names (docs/herculan/handoff-editor-conditions.md#present-when). Row 1's rules are
/// <see cref="ConditionEditing"/>'s; which records a set writes, and which it refuses, are <see cref="RecordConditions"/>'.
///
/// <list type="bullet">
/// <item><b>Reading.</b> The chain from the target's condition: a range over a draw (the chance), then the draw's requirement,
/// then each flag test's requirement in turn, every one of them a flag test. Anything else is not clauses, and the reading
/// says why: a variant table or a range over one (a random choice's option), a range over a draw another record or range
/// also uses (a shared roll, which makes records appear together), a GUID row 1 carries twice, a type the load does not
/// read, a test that never holds, or a draw or range inside the chain.</item>
/// <item><b>Writing</b>, as one <see cref="MissionDocument.Change"/>: the chain is built innermost first, each record requiring
/// the one before. A variable clause reuses a flag test with the same flag, operator, value and requirement, read after the
/// requirement, and is otherwise added. A chance's draw requires the variables, so the roll is made only when they hold, as
/// each retail draw requires its flag-10 test (docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line), and a range
/// over it holds. A chance this target alone uses is rewritten in place, and a new flag test then goes in just before its
/// draw, so no draw moves; any other chance is a new draw and range, appended, never another record's draw. Every record of
/// the target then names the outermost (<see cref="RecordConditions"/>' write).</item>
/// <item><b>Cleanup.</b> Each condition of the old chain, outermost first, is removed once nothing references it
/// (<see cref="ConditionEditing.Namers"/>), a draw the change orphaned included. Adding or removing a draw changes every
/// later draw and variant pick (docs/herculan/handoff-editor-document.md#conditions-and-variants).</item>
/// </list>
/// </summary>
public static class PresenceRules {
	private const int Row = 1;

	/// <summary>Why a reading is not clauses, in the editor's words.</summary>
	private const string Missing = "The rule it names is not in the file, so it is never present.";
	private const string Twice = "A rule it depends on is carried twice in the file, so it is set under Advanced.";
	private const string Option = "It is one option of a random choice, so it is set under Advanced.";
	private const string Shared = "It shares its roll with other records, so its chance is set under Advanced.";
	private const string Unknown = "Its rule is of a kind these clauses cannot show, so it is set under Advanced.";
	private const string Never = "One of its tests never holds, so it is set under Advanced.";
	private const string Differs = "Its details change between versions of the mission, so its presence cannot be set as one rule.";

	/// <summary>The clauses row-1 GUID <paramref name="condition"/> stands for, or <c>-1</c> for always; no check of who else uses its draw.</summary>
	public static PresenceReading ReadCondition(MissionDocument document, short condition) {
		var chain = new List<short>();
		var variables = new List<VariableClause>();
		ChanceClause? chance = null;
		short current = condition;
		if (current != -1 && ConditionEditing.IndexOf(document, current) != null
				&& ConditionEditing.Record(document, current).Type == ConditionEditing.Range) {
			if (Why(document, current) is { } fixedRange) {
				return new PresenceReading(null, fixedRange, chain);
			}

			var range = ConditionEditing.Record(document, current);
			if (ConditionEditing.ParentOf(document, current) is not { Type: ConditionEditing.Draw } draw) {
				return new PresenceReading(null, Unknown, chain);
			}

			if (Why(document, draw.GUID) is { } fixedDraw) {
				return new PresenceReading(null, fixedDraw, chain);
			}

			chance = new ChanceClause(draw.FlagIndexOrRangeLower, range.FlagIndexOrRangeLower, range.OperatorOrRangeUpperOrResult);
			chain.Add(current);
			chain.Add(draw.GUID);
			if (Requirement(document, draw) is not { } drawRequires) {
				return new PresenceReading(null, Never, chain);
			}

			current = drawRequires;
		}

		while (current != -1) {
			if (chain.Contains(current)) {
				return new PresenceReading(null, Never, chain);
			}

			if (Why(document, current) is { } reason) {
				return new PresenceReading(null, reason, chain);
			}

			var test = ConditionEditing.Record(document, current);
			if (test.Type != ConditionEditing.FlagTest) {
				return new PresenceReading(null, Unknown, chain);
			}

			if (ConditionEditing.OperatorText(test.OperatorOrRangeUpperOrResult) == null || Requirement(document, test) is not { } requires) {
				return new PresenceReading(null, Never, chain);
			}

			variables.Add(new VariableClause(test.FlagIndexOrRangeLower, test.OperatorOrRangeUpperOrResult, test.ComparisonOperand));
			chain.Add(current);
			current = requires;
		}

		variables.Reverse();
		return new PresenceReading(new PresenceRule(variables, chance), null, chain);
	}

	/// <summary>
	/// <paramref name="target"/>'s Present when as clauses. Its rule is read whenever the chain is clauses, a shared roll
	/// included, so a view can state it; it is settable only when nothing refuses it.
	/// </summary>
	public static PresenceReading Read(MissionDocument document, PresenceTarget target) {
		var records = RecordConditions.Records(document, target);
		if (records.Count == 0) {
			return new PresenceReading(null, "It is not in the file.", []);
		}

		if (RecordConditions.Shared(document, target) is not { } condition) {
			return new PresenceReading(null, Differs, []);
		}

		var reading = ReadCondition(document, condition);
		if (reading.Rule?.Chance == null) {
			return reading;
		}

		// The range only the target's records reference, and the draw only the range.
		var own = records.Select(record => record.Record).ToHashSet();
		var rangeUsers = ConditionEditing.Namers(document, reading.Chain[0]);
		var drawUsers = ConditionEditing.Namers(document, reading.Chain[1]);
		int range = ConditionEditing.IndexOf(document, reading.Chain[0])!.Value;
		bool alone = rangeUsers.Text.Count == 0 && rangeUsers.Records.All(own.Contains)
			&& drawUsers.Text.Count == 0 && drawUsers.Records.SequenceEqual([new MsnRecordId(Row, range)]);
		return alone ? reading : reading with { WhyFixed = Shared };
	}

	/// <summary>Why <paramref name="target"/>'s Present when cannot be set as clauses, or null when it can.</summary>
	public static string? WhyFixed(MissionDocument document, PresenceTarget target) =>
		RecordConditions.WhyFixed(document, target) ?? Read(document, target).WhyFixed;

	/// <summary>
	/// Makes <paramref name="target"/> present by <paramref name="rule"/>, as one <see cref="MissionDocument.Change"/>, and
	/// removes what of its old chain nothing references any more. Throws <see cref="InvalidOperationException"/> when
	/// <see cref="WhyFixed"/> refuses it, and <see cref="ArgumentOutOfRangeException"/> for a clause the load cannot test.
	/// <paramref name="written"/> is the GUID whose records now carry it, as <see cref="RecordConditions.SetPresence"/> answers it.
	/// </summary>
	public static MsnEdit SetRule(MissionDocument document, PresenceTarget target, PresenceRule rule, out short written) {
		if (WhyFixed(document, target) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		foreach (var clause in rule.Variables) {
			if (clause.Variable is < 0 or >= ConditionEditing.Flags || !ConditionEditing.Operators.Contains(clause.Operator)) {
				throw new ArgumentOutOfRangeException(nameof(rule), clause, $"A campaign variable is 0 to {ConditionEditing.Flags - 1}, tested by a known operator.");
			}
		}

		if (rule.Chance is { Bound: < 1 or > ConditionEditing.MaxBound }) {
			throw new ArgumentOutOfRangeException(nameof(rule), rule.Chance, $"A roll is below 1 to {ConditionEditing.MaxBound}.");
		}

		var old = Read(document, target);
		short guid = -1;
		var change = document.Change(() => {
			// The old chance is kept in place when the rule keeps one: Read has checked the target alone uses it.
			bool inPlace = rule.Chance != null && old.Rule!.Chance != null;
			short draw = inPlace ? old.Chain[1] : (short)-1;
			short requires = -1;
			foreach (var clause in rule.Variables) {
				int before = inPlace ? ConditionEditing.IndexOf(document, draw)!.Value : ConditionEditing.Records(document).Length;
				requires = FindTest(document, clause, requires, before) ?? AddTest(document, clause, requires, before);
			}

			short outermost = requires;
			if (rule.Chance is { } chance) {
				outermost = inPlace ? RewriteChance(document, old.Chain[0], draw, chance, requires) : AddChance(document, chance, requires);
			}

			guid = RecordConditions.Write(document, target, outermost);
			foreach (short condition in old.Chain) {
				if (ConditionEditing.IndexOf(document, condition) != null && !ConditionEditing.Namers(document, condition).Any) {
					document.Remove(Row, condition);
				}
			}
		});

		written = guid;
		return change;
	}

	// Why <guid> cannot be a link of a chain: not in the file, carried twice, a random choice's, or of no type the load reads.
	private static string? Why(MissionDocument document, short guid) {
		if (ConditionEditing.IndexOf(document, guid) == null) {
			return Missing;
		}

		if (ConditionEditing.Records(document).Count(record => record.GUID == guid) > 1) {
			return Twice;
		}

		return ConditionEditing.InVariantTable(document, guid) ? Option
			: ConditionEditing.WhyFixed(document, guid) != null ? Unknown
			: null;
	}

	// What <record> requires, -1 for nothing; null when that is not read before it, so it never holds.
	private static short? Requirement(MissionDocument document, MissionCondition14 record) {
		if (record.ConditionRef == -1) {
			return -1;
		}

		return ConditionEditing.IndexOf(document, record.ConditionRef) is { } at && at < ConditionEditing.IndexOf(document, record.GUID)!.Value
			? record.ConditionRef
			: null;
	}

	// The first flag test testing <clause>, requiring <requires>, read after it and before index <before>, carried once.
	private static short? FindTest(MissionDocument document, VariableClause clause, short requires, int before) {
		var records = ConditionEditing.Records(document);
		int after = requires == -1 ? -1 : ConditionEditing.IndexOf(document, requires)!.Value;
		for (int i = after + 1; i < before && i < records.Length; i++) {
			var record = records[i];
			if (record.GUID != -1 && record.Type == ConditionEditing.FlagTest && record.ConditionRef == requires
					&& record.FlagIndexOrRangeLower == clause.Variable && record.OperatorOrRangeUpperOrResult == clause.Operator
					&& record.ComparisonOperand == clause.Value && records.Count(other => other.GUID == record.GUID) == 1) {
				return record.GUID;
			}
		}

		return null;
	}

	// A new flag test of <clause> requiring <requires>, put at row-1 index <at>, answering its GUID.
	private static short AddTest(MissionDocument document, VariableClause clause, short requires, int at) =>
		Add(document, at, new MissionCondition14 {
			ConditionRef = requires, Type = ConditionEditing.FlagTest, FlagIndexOrRangeLower = clause.Variable,
			OperatorOrRangeUpperOrResult = clause.Operator, ComparisonOperand = clause.Value,
		});

	// A new draw below <chance>'s bound requiring <requires> and a range over it, appended, answering the range's GUID. The
	// draw's 0x08 and 0x0A are 0 and the range's 0x0A 0, as every retail draw and range has them (ConditionEditing's
	// CreateDraw and CreateRange).
	private static short AddChance(MissionDocument document, ChanceClause chance, short requires) {
		int end = ConditionEditing.Records(document).Length;
		short draw = Add(document, end, new MissionCondition14 {
			ConditionRef = requires, Type = ConditionEditing.Draw, FlagIndexOrRangeLower = chance.Bound,
		});
		return Add(document, end + 1, new MissionCondition14 {
			ConditionRef = draw, Type = ConditionEditing.Range, FlagIndexOrRangeLower = chance.Low, OperatorOrRangeUpperOrResult = chance.High,
		});
	}

	// Draw <draw> made below <chance>'s bound when <requires> holds, and range <range> over it holding its ends, each record
	// written only where it changes; answers the range's GUID.
	private static short RewriteChance(MissionDocument document, short range, short draw, ChanceClause chance, short requires) {
		var drawn = ConditionEditing.Record(document, draw);
		if (drawn.FlagIndexOrRangeLower != chance.Bound || drawn.ConditionRef != requires) {
			document.Rewrite<MissionCondition14>(Row, ConditionEditing.IndexOf(document, draw)!.Value, record => {
				record.FlagIndexOrRangeLower = chance.Bound;
				record.ConditionRef = requires;
			});
		}

		var ends = ConditionEditing.Record(document, range);
		if (ends.FlagIndexOrRangeLower != chance.Low || ends.OperatorOrRangeUpperOrResult != chance.High) {
			document.Rewrite<MissionCondition14>(Row, ConditionEditing.IndexOf(document, range)!.Value, record => {
				record.FlagIndexOrRangeLower = chance.Low;
				record.OperatorOrRangeUpperOrResult = chance.High;
			});
		}

		return range;
	}

	// Puts <record> at row-1 index <at> under the row's largest GUID plus one, as an append would number it, 0x0C 0 as every
	// retail record has it; answers the GUID.
	private static short Add(MissionDocument document, int at, MissionCondition14 record) {
		record.LatestDraw = 0;
		var records = ConditionEditing.Records(document);
		if (at >= records.Length) {
			document.Append(Row, record, out short appended);
			return appended;
		}

		short largest = records.Select(existing => existing.GUID).DefaultIfEmpty((short)-1).Max();
		if (largest == short.MaxValue) {
			throw new InvalidOperationException($"Row {Row}'s GUIDs already reach {short.MaxValue}.");
		}

		record.GUID = (short)(largest + 1);
		document.Insert(Row, at, record);
		return record.GUID;
	}
}
