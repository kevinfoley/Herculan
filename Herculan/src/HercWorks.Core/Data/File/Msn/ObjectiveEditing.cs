using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Creates, edits and deletes a <see cref="MissionDocument"/>'s objectives, row 17, as record edits. What each field means
/// is docs/retail/simulation/mission-objectives.md's, and the record's layout
/// docs/retail/formats/msn-mission-file.md#row-17-field-decode--the-objective-record-dat_0047064a-58-bytesrecord.
///
/// <list type="bullet">
/// <item>Row 17 has no GUID and nothing references an objective: the load keeps every surviving record, renumbering none of
/// them into anything else (<c>MsnGen_ParseMsnFile</c> (<c>00417b67</c>)), and in DBSIM the objective array
/// (<c>004a9ec4</c>, its count <c>004a9ec0</c>) is referenced only by <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>),
/// which fills it, and <c>Mission_EvaluateObjectives</c> (<c>00413280</c>), which walks it (<c>es2_xref.py</c>). So an edit
/// writes the record itself, a conditioned one included, and a delete removes it with nothing to renumber.</item>
/// <item>A record is named here by its index in row 17 (<see cref="RecordOf"/> maps a block-12 index to it).</item>
/// <item>The conditions offered are those <c>Mission_EvaluateObjectives</c> has a case for, 0 to 4 and 6 to 10
/// (<see cref="Conditions"/>); 5 has none. The subjects offered are a group, a herc and a structure: no retail objective
/// asks about a flyer (the user's choice). A record already holding either keeps it until it is changed.</item>
/// <item>The counter writes stay filled from the first pair with the pair count at <c>0x10</c> equal to the filled count,
/// as in every retail record (docs/retail/formats/msn-mission-file.md#recurring-patterns).</item>
/// </list>
/// </summary>
public static class ObjectiveEditing {
	private const int Row = 17;
	private const int RouteBlock = 3;
	private const int ObjectiveBlock = 12;

	/// <summary><see cref="MissionObjective58.Required"/> for a mandatory objective, 91 of the 127 retail records.</summary>
	public const short Mandatory = 1;

	/// <summary><see cref="MissionObjective58.Required"/> for a failure condition: 0, the other value retail uses, in the other 36.</summary>
	public const short Failure = 0;

	/// <summary>Condition 1: the subject is destroyed, or a group written off.</summary>
	public const short Lost = 1;

	/// <summary>Condition 0: the subject's group has finished its order on the record's route.</summary>
	public const short OrderComplete = 0;

	/// <summary>The condition codes offered: every code <c>Mission_EvaluateObjectives</c> has a case for.</summary>
	public static IReadOnlyList<short> Conditions { get; } = [0, 1, 2, 3, 4, 6, 7, 8, 9, 10];

	/// <summary>Conditions 3, 4, 9 and 10, which test the player's data link and not the subject.</summary>
	public static bool TestsSubject(short code) => code is not (3 or 4 or 9 or 10);

	/// <summary>Subject kinds: a group (row 16), a herc (row 12), a structure or ground vehicle (row 14).</summary>
	public const short GroupSubject = 0;
	public const short HercSubject = 1;
	public const short StructureSubject = 3;

	/// <summary>The counter pairs a record carries.</summary>
	public const int Pairs = 10;

	/// <summary>The objective layer's four counter operations (docs/retail/simulation/mission-objectives.md#the-record).</summary>
	public const short CounterSet = 4;
	public const short CounterClear = 5;
	public const short CounterIncrement = 6;
	public const short CounterDecrement = 7;

	/// <summary>How many mission counters there are, the 1,000 of docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4.</summary>
	public const int Counters = MissionGenerator.CampaignFlagCount;

	/// <summary>
	/// A new pair's counter and operation: counter 52 incremented (6), the first pair 4 of the 31 retail records that write a
	/// counter carry, the most of any (<c>HercWorks.Query values --row 17 --field Pairs[0] --where PairCount!=0</c>).
	/// </summary>
	public const short NewPairCounter = 52;
	public const short NewPairOp = CounterIncrement;

	/// <summary>The row-17 index of the record block-12 objective <paramref name="objective"/> was exported from, or null.</summary>
	public static int? RecordOf(MissionDocument document, int objective) =>
		document.Provenance.Slot(ObjectiveBlock, objective)?.Created.Index;

	/// <summary>The block-12 index row-17 record <paramref name="record"/> is exported at, or null when it fails its condition.</summary>
	public static int? ExportedIndex(MissionDocument document, int record) =>
		document.Provenance.ExportedSlot(new MsnRecordId(Row, record))?.Index;

	/// <summary>Row-17 record <paramref name="record"/>, throwing for one the row does not have.</summary>
	public static MissionObjective58 Record(MissionDocument document, int record) =>
		document.Mission.Objectives is { } row && record >= 0 && record < row.Length && row[record] is { } found
			? found
			: throw new InvalidOperationException($"There is no objective record {record}.");

	/// <summary>How many of <paramref name="objective"/>'s counter pairs are filled, from the first.</summary>
	public static int FilledPairs(MissionObjective58 objective) =>
		Array.FindLastIndex(objective.Pairs, pair => pair.CounterRef != -1 || pair.Op != -1) + 1;

	/// <summary>
	/// Why no objective can be created about <paramref name="kind"/>'s GUID <paramref name="subject"/>, or null when one can: a
	/// subject the generation does not export, a mission file that ends inside its objectives (<c>DEMO2</c>), and the three
	/// lines of its failure text taking the mission's text past <see cref="MissionTextEditing.LineCap"/>.
	/// </summary>
	public static string? WhyNoCreate(MissionDocument document, short kind, short subject) =>
		document.Mission.TruncatedRow17Tail != null ? "The mission file ends inside its objectives."
			: WhyNoSubject(document, kind, subject) ?? MissionTextEditing.WhyNoLines(document, MissionTextEditing.FailureLines);

	/// <summary>
	/// Appends an objective about <paramref name="kind"/>'s GUID <paramref name="subject"/>, answering its row-17 index in
	/// <paramref name="record"/>. The record is the editor's default: unconditioned, mandatory, condition 1 (the subject is
	/// destroyed or written off), no route, no counter writes, the dead point <c>0x0A</c> <c>-1</c> as in all 127 retail
	/// records, and a failure text of three new unconditioned <c>.ENG</c> records with consecutive new ids and no text,
	/// which the author fills in (the editor's choice). The unused pairs are <c>-1</c>/<c>-1</c>, as every retail record's
	/// (<c>HercWorks.Query values --row 17 --field Pairs[5]</c>).
	/// </summary>
	public static MsnEdit Create(MissionDocument document, short kind, short subject, out int record) {
		if (WhyNoCreate(document, kind, subject) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short text = MissionTextEditing.NewIds(document, MissionTextEditing.FailureLines);
		int lines = MissionTextEditing.Lines(document);
		int added = (document.Mission.Objectives ?? []).Length;
		var change = document.Change(() => {
			document.AppendText(Enumerable.Range(0, MissionTextEditing.FailureLines)
				.Select(line => MissionTextEditing.Entry((short)(text + line), string.Empty)));
			document.AppendUnkeyed(Row, new MissionObjective58 {
				ConditionRef = -1, Required = Mandatory, ConditionCode = Lost, SubjectKind = kind, SubjectRef = subject,
				PointRef = -1, RouteRef = -1, TextRef = text, PairCount = 0,
				Pairs = Enumerable.Range(0, Pairs).Select(_ => new CounterPair()).ToArray(),
			});
			MissionTextEditing.CheckLines(document, lines);
		});
		record = added;
		return change;
	}

	/// <summary>Why row-17 record <paramref name="record"/> cannot be deleted, or null when it can.</summary>
	public static string? WhyNoDelete(MissionDocument document, int record) =>
		document.Mission.TruncatedRow17Tail != null ? "The mission file ends inside its objectives." : null;

	/// <summary>
	/// Removes row-17 record <paramref name="record"/>, and the <c>.ENG</c> records of each id of its failure text that no
	/// field of rows 4, 5, 10 and 17 references any more, whatever its record's condition.
	/// </summary>
	public static MsnEdit Delete(MissionDocument document, int record) {
		if (WhyNoDelete(document, record) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var ids = MissionTextEditing.FailureIds(document.Text, Record(document, record).TextRef);
		return document.Change(() => {
			document.RemoveUnkeyed(Row, record);
			MissionTextEditing.RemoveUnreferenced(document, [.. ids]);
		});
	}

	/// <summary>Makes row-17 record <paramref name="record"/> mandatory, or a failure condition.</summary>
	public static MsnEdit SetRequired(MissionDocument document, int record, bool mandatory) =>
		Write(document, record, objective => objective.Required = mandatory ? Mandatory : Failure);

	/// <summary>
	/// Sets row-17 record <paramref name="record"/>'s condition, one of <see cref="Conditions"/>. A change to condition 0 on a
	/// record with no route gives it the route its subject's group's slot-0 order runs, when that order runs one.
	/// </summary>
	public static MsnEdit SetCondition(MissionDocument document, int record, short code) {
		if (!Conditions.Contains(code)) {
			throw new ArgumentOutOfRangeException(nameof(code), code, "A condition is 0 to 4 or 6 to 10.");
		}

		short route = -1;
		if (code == OrderComplete && Record(document, record).RouteRef == -1 && ExportedIndex(document, record) is { } objective
				&& SubjectGroup(document, objective) is >= 0 and var group && document.Script.Groups[group].OrderRefs[0] is >= 0 and var order
				&& order < document.Script.Orders.Length && document.Script.Orders[order].RouteRef is >= 0 and var slot0Route) {
			route = document.GuidOf(RouteBlock, slot0Route);
		}

		return Write(document, record, objective => {
			objective.ConditionCode = code;
			if (route != -1) {
				objective.RouteRef = route;
			}
		});
	}

	/// <summary>Why <paramref name="kind"/>'s GUID <paramref name="subject"/> cannot be an objective's subject, or null.</summary>
	public static string? WhyNoSubject(MissionDocument document, short kind, short subject) {
		int? row = kind switch { GroupSubject => 16, HercSubject => 12, StructureSubject => 14, _ => null };
		if (row == null) {
			return "Only a group, a herc or a structure can be an objective's subject.";
		}

		return subject == -1 || document.ExportedIndex(row.Value, subject) == null ? "It is not in this state of the mission." : null;
	}

	/// <summary>Sets row-17 record <paramref name="record"/>'s subject: a group, herc or structure GUID of <paramref name="kind"/>.</summary>
	public static MsnEdit SetSubject(MissionDocument document, int record, short kind, short subject) {
		if (WhyNoSubject(document, kind, subject) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return Write(document, record, objective => {
			objective.SubjectKind = kind;
			objective.SubjectRef = subject;
		});
	}

	/// <summary>Sets row-17 record <paramref name="record"/>'s route, a row-8 GUID, or <c>-1</c> for none.</summary>
	public static MsnEdit SetRoute(MissionDocument document, int record, short route) =>
		Write(document, record, objective => objective.RouteRef = route);

	/// <summary>
	/// The block-11 group whose orders block-12 objective <paramref name="objective"/>'s condition 0 reads: its subject group,
	/// or the group its subject herc or structure is a member of; <c>-1</c> for none.
	/// </summary>
	public static int SubjectGroup(MissionDocument document, int objective) {
		var script = document.Script;
		if (objective < 0 || objective >= script.Objectives.Length) {
			return -1;
		}

		var record = script.Objectives[objective];
		short members = record.SubjectKind switch { HercSubject => 0, StructureSubject => 2, _ => -1 };
		if (record.SubjectKind == GroupSubject) {
			return record.SubjectRef >= 0 && record.SubjectRef < script.Groups.Length ? record.SubjectRef : -1;
		}

		return members == -1 || record.SubjectRef < 0 ? -1
			: Array.FindIndex(script.Groups, group => group.MemberKind == members && group.MemberRefs.Contains(record.SubjectRef));
	}

	/// <summary>
	/// The routes, by block-3 index, that block-12 objective <paramref name="objective"/>'s condition 0 can be about: those
	/// its subject's group's order slots run, in slot order, once each.
	/// </summary>
	public static IReadOnlyList<int> RouteChoices(MissionDocument document, int objective) {
		var script = document.Script;
		int group = SubjectGroup(document, objective);
		if (group < 0) {
			return [];
		}

		return script.Groups[group].OrderRefs
			.Where(order => order >= 0 && order < script.Orders.Length)
			.Select(order => (int)script.Orders[order].RouteRef)
			.Where(route => route >= 0)
			.Distinct()
			.ToList();
	}

	/// <summary>Why no counter write can be added to row-17 record <paramref name="record"/>, or null when one can.</summary>
	public static string? WhyNoAddPair(MissionDocument document, int record) =>
		FilledPairs(Record(document, record)) >= Pairs ? $"All {Pairs} counter writes are filled." : null;

	/// <summary>Adds a counter write after row-17 record <paramref name="record"/>'s last (<see cref="NewPairCounter"/>, <see cref="NewPairOp"/>).</summary>
	public static MsnEdit AddPair(MissionDocument document, int record) {
		if (WhyNoAddPair(document, record) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return Write(document, record, objective => {
			var pair = objective.Pairs[FilledPairs(objective)];
			pair.CounterRef = NewPairCounter;
			pair.Op = NewPairOp;
			objective.PairCount = (short)FilledPairs(objective);
		});
	}

	/// <summary>
	/// Sets row-17 record <paramref name="record"/>'s counter write <paramref name="slot"/>, a filled one, to counter
	/// <paramref name="counter"/>, 0 to <see cref="Counters"/> - 1, and operation <paramref name="op"/>, 4 to 7.
	/// </summary>
	public static MsnEdit SetPair(MissionDocument document, int record, int slot, short counter, short op) {
		if (slot < 0 || slot >= FilledPairs(Record(document, record))) {
			throw new InvalidOperationException($"There is no counter write {slot}.");
		}

		if (counter is < 0 or >= Counters) {
			throw new ArgumentOutOfRangeException(nameof(counter), counter, $"A counter is 0 to {Counters - 1}.");
		}

		if (op is < CounterSet or > CounterDecrement) {
			throw new ArgumentOutOfRangeException(nameof(op), op, "An operation is 4 (set), 5 (clear), 6 (+1) or 7 (-1).");
		}

		return Write(document, record, objective => {
			objective.Pairs[slot].CounterRef = counter;
			objective.Pairs[slot].Op = op;
		});
	}

	/// <summary>Removes row-17 record <paramref name="record"/>'s counter write <paramref name="slot"/>, moving every later one up.</summary>
	public static MsnEdit RemovePair(MissionDocument document, int record, int slot) {
		int filled = FilledPairs(Record(document, record));
		if (slot < 0 || slot >= filled) {
			throw new InvalidOperationException($"There is no counter write {slot}.");
		}

		return Write(document, record, objective => {
			for (int at = slot; at < filled - 1; at++) {
				objective.Pairs[at].CounterRef = objective.Pairs[at + 1].CounterRef;
				objective.Pairs[at].Op = objective.Pairs[at + 1].Op;
			}

			objective.Pairs[filled - 1].CounterRef = -1;
			objective.Pairs[filled - 1].Op = -1;
			objective.PairCount = (short)FilledPairs(objective);
		});
	}

	/// <summary>Why row-17 record <paramref name="record"/> cannot be given a failure text, or null when it can.</summary>
	public static string? WhyNoText(MissionDocument document, int record) =>
		Record(document, record).TextRef != -1 ? "It has a failure text." : MissionTextEditing.WhyNoLines(document, MissionTextEditing.FailureLines);

	/// <summary>Gives row-17 record <paramref name="record"/>, which has none, a failure text of three new empty lines, as <see cref="Create"/> does.</summary>
	public static MsnEdit GiveText(MissionDocument document, int record) {
		if (WhyNoText(document, record) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short text = MissionTextEditing.NewIds(document, MissionTextEditing.FailureLines);
		int lines = MissionTextEditing.Lines(document);
		return document.Change(() => {
			document.AppendText(Enumerable.Range(0, MissionTextEditing.FailureLines)
				.Select(line => MissionTextEditing.Entry((short)(text + line), string.Empty)));
			document.Rewrite<MissionObjective58>(Row, record, objective => objective.TextRef = text);
			MissionTextEditing.CheckLines(document, lines);
		});
	}

	private static MsnEdit Write(MissionDocument document, int record, Action<MissionObjective58> write) {
		Record(document, record);
		return document.Rewrite(Row, record, write);
	}
}
