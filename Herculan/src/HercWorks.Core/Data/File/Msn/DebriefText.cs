using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>One line of the debrief: its slot in the row-5 record and the <c>.ENG</c> id it references.</summary>
public readonly record struct DebriefLine(int Slot, short Id);

/// <summary>A case of the debrief's loss line: its <c>.ENG</c> record, by index, the condition it carries, that condition's clauses and the text.</summary>
public sealed record LossCase(int Record, short Condition, PresenceRule Rule, string Text);

/// <summary>The debrief's loss line: the debrief line it is, its blank base record, by index, and its ten cases in file order.</summary>
public sealed record LossLine(DebriefLine Line, int Blank, IReadOnlyList<LossCase> Cases);

/// <summary>
/// The debrief as the mission's outcomes read it: row 5's record, the <c>.ENG</c> lines it references, the row-3 movie it
/// references, and the conditions their replacements carry (docs/retail/formats/msn-mission-file.md#row-5--the-debrief).
///
/// <list type="bullet">
/// <item>The record is the row's first: the debrief's reload keeps its first survivor, and every retail mission with a debrief
/// carries one unconditioned record (54 of the 62; the other 8 carry none).</item>
/// <item>An <b>outcome</b> is a row-1 condition a replacement of a debrief line or a movie record carries, in the order the
/// <c>.ENG</c> and then row 3 first use it. A line reads, when the condition holds, as its last record under it, else its
/// unconditioned text (<see cref="MissionTextEditing.RecordUnder"/>), the movie likewise as row 3 replaces a GUID.</item>
/// <item>The <b>loss line</b> is the line carrying the standard eleven records, a blank and ten cases keyed on campaign flags
/// 8, 9 and 10 and a draw (docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line); it is matched by its conditions'
/// clauses alone, the texts left free, and its conditions are not outcomes.</item>
/// </list>
/// </summary>
public static class DebriefText {
	/// <summary>Bytes a row-5 record takes: its condition, thirty text refs and a row-3 ref.</summary>
	public const int RecordLength = 64;

	/// <summary>Text slots a row-5 record has.</summary>
	public const int Slots = 30;

	// The word of a row-5 record holding its row-3 ref, after its condition and thirty text refs.
	private const int MovieWord = 1 + Slots;

	// The draw the loss line's harsher pilot's-death case rolls below, and the ranges its two cases take of it.
	private const short LossDrawBound = 11;

	/// <summary>The loss line's ten cases' clauses, in file order (docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line).</summary>
	public static IReadOnlyList<PresenceRule> LossCases { get; } = [
		Test(8, MissionGenerator.OpEqual, 1), Test(8, MissionGenerator.OpEqual, 2), Test(8, MissionGenerator.OpGreater, 2),
		Test(9, MissionGenerator.OpEqual, 1), Test(9, MissionGenerator.OpEqual, 2), Test(9, MissionGenerator.OpGreater, 2),
		Test(10, MissionGenerator.OpEqual, 1).WithChance(new ChanceClause(LossDrawBound, 0, 9)),
		Test(10, MissionGenerator.OpEqual, 1).WithChance(new ChanceClause(LossDrawBound, 10, 10)),
		Test(10, MissionGenerator.OpEqual, 2), Test(10, MissionGenerator.OpEqual, 2),
	];

	/// <summary>The byte offset of text slot <paramref name="slot"/> of row-5 record <paramref name="record"/>.</summary>
	public static int SlotOffset(int record, int slot) => record * RecordLength + (1 + slot) * 2;

	/// <summary>The row-5 record the debrief reads, by its index in the row; null for a mission with none.</summary>
	public static int? Record(MissionDocument document) =>
		(document.Mission.DebriefBytes?.Length ?? 0) >= RecordLength ? 0 : null;

	/// <summary>The debrief's filled slots, in slot order, each with the id it references.</summary>
	public static IReadOnlyList<DebriefLine> Lines(MissionDocument document) {
		if (Record(document) is not { } record) {
			return [];
		}

		var bytes = document.Mission.DebriefBytes!;
		var lines = new List<DebriefLine>();
		for (int slot = 0; slot < Slots; slot++) {
			short id = BitConverter.ToInt16(bytes, SlotOffset(record, slot));
			if (id != -1) {
				lines.Add(new DebriefLine(slot, id));
			}
		}

		return lines;
	}

	/// <summary>The row-3 GUID the debrief's movie is, <c>-1</c> for none.</summary>
	public static short MovieRef(MissionDocument document) =>
		Record(document) is { } record ? BitConverter.ToInt16(document.Mission.DebriefBytes!, record * RecordLength + MovieWord * 2) : (short)-1;

	/// <summary>
	/// The debrief movie's value under <paramref name="condition"/>, <c>-1</c> for none: the last row-3 record of the movie's
	/// GUID carrying it; null when none does.
	/// </summary>
	public static short? MovieUnder(MissionDocument document, short condition) {
		short guid = MovieRef(document);
		var values = document.Mission.MovieChoices ?? [];
		for (int i = values.Length - 1; guid != -1 && i >= 0; i--) {
			if (values[i].GUID == guid && values[i].ConditionRef == condition) {
				return values[i].Value;
			}
		}

		return null;
	}

	/// <summary>The debrief's loss line, or null when no line carries the standard records (<see cref="LossCases"/>).</summary>
	public static LossLine? LossLineOf(MissionDocument document) {
		var strings = document.Text?.Strings ?? [];
		foreach (var line in Lines(document)) {
			var records = Enumerable.Range(0, strings.Length).Where(i => strings[i].Guid == line.Id).ToList();
			if (records.Count != LossCases.Count + 1 || strings[records[0]].ConditionRef != -1) {
				continue;
			}

			var cases = new List<LossCase>();
			for (int i = 0; i < LossCases.Count; i++) {
				var entry = strings[records[i + 1]];
				if (PresenceRules.ReadCondition(document, entry.ConditionRef).Rule is not { } rule || !rule.Equals(LossCases[i])) {
					break;
				}

				cases.Add(new LossCase(records[i + 1], entry.ConditionRef, rule, MissionTextEditing.Shown(entry.Val)));
			}

			if (cases.Count == LossCases.Count) {
				return new LossLine(line, records[0], cases);
			}
		}

		return null;
	}

	/// <summary>
	/// The debrief's outcomes: each condition a record of a debrief line other than the loss line, or a row-3 record of its
	/// movie, carries, in the order the <c>.ENG</c> and then row 3 first use it.
	/// </summary>
	public static IReadOnlyList<short> Outcomes(MissionDocument document) {
		short? loss = LossLineOf(document)?.Line.Id;
		var ids = Lines(document).Select(line => line.Id).Where(id => id != loss).ToHashSet();
		var outcomes = new List<short>();
		foreach (var entry in document.Text?.Strings ?? []) {
			if (ids.Contains(entry.Guid) && entry.ConditionRef != -1 && !outcomes.Contains(entry.ConditionRef)) {
				outcomes.Add(entry.ConditionRef);
			}
		}

		short movie = MovieRef(document);
		foreach (var value in document.Mission.MovieChoices ?? []) {
			if (movie != -1 && value.GUID == movie && value.ConditionRef != -1 && !outcomes.Contains(value.ConditionRef)) {
				outcomes.Add(value.ConditionRef);
			}
		}

		return outcomes;
	}

	private static PresenceRule Test(short flag, short op, short value) => new([new VariableClause(flag, op, value)], null);
}
