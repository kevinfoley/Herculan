using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>One alternative of a variant table: the range that chooses it and the record it is.</summary>
/// <param name="Range">The type-2 range's GUID.</param>
/// <param name="Record">The GUID <c>-1</c> record the range conditions, which a record drawing it copies from.</param>
/// <param name="Draws">How many of the table's draws, 0 to its bound less one, take it: those its range holds that no range before it in row 1 holds.</param>
public sealed record VariantAlternative(short Range, MsnRecordId Record, int Draws);

/// <summary>
/// A variant table, a type-3 row-1 record, as <see cref="VariantEditing.Table"/> reads it: its key, its bound, what it
/// requires, the row its alternatives are of and each alternative in its ranges' row-1 order, the other tables under its key,
/// and why the editor shows it without editing it.
/// </summary>
public sealed record VariantTable(short Guid, short Key, short Bound, short Requires, int Row,
		IReadOnlyList<VariantAlternative> Alternatives, IReadOnlyList<short> SameKey, string? WhyFixed) {
	/// <summary>How many of the table's draws no range holds.</summary>
	public int Leftover => Math.Max(Bound, (short)0) - Alternatives.Sum(alternative => alternative.Draws);

	/// <summary>A share of the draws, <paramref name="draws"/> of <see cref="Bound"/>, as a percent.</summary>
	public double Percent(int draws) => Bound > 0 ? draws * 100.0 / Bound : 0;
}

/// <summary>
/// Creates, edits and deletes a <see cref="MissionDocument"/>'s variant tables, and sets which table a herc, flyer, structure
/// or ground vehicle draws from, as record edits. How a record draws is docs/retail/formats/msn-mission-file.md#variants: a
/// record whose variant key is set draws below the bound of the first surviving type-3 record of that key, takes the first
/// range over it holding the draw, and copies its row's span from the first GUID <c>-1</c> record read before it that the
/// range conditions.
///
/// <list type="bullet">
/// <item>A table is edited when each of its ranges conditions one GUID <c>-1</c> record of row 12, 13 or 14 drawing nothing
/// itself, all of one row, no other table carries its key, and the player's squad draws nothing from it
/// (<see cref="Table"/>'s <see cref="VariantTable.WhyFixed"/>). The rest, row 8's waypoint lists among them, are shown
/// only.</item>
/// <item>The chances are percents to one decimal: each edit of them writes the ranges over the table, contiguous from 0 in the
/// ranges' row-1 order, and the bound <see cref="ChanceBound"/> gives them, 100 when every chance is a whole percent, else
/// 1000 (<see cref="SetChances"/>). A draw below any bound of 1 or more takes one step of VSHELL's generator,
/// <c>ShellRandom_Below</c> (<c>004659ec</c>) being <c>(ShellRandom_Next() &amp; 0x7fff) % n</c>
/// (docs/retail/shell/campaign-loop.md#the-shells-generator), so writing the bound moves no later draw; no retail table's
/// bound is below 1. A draw no range holds takes no alternative from this table (the editor's chances leave that as the
/// leftover).</item>
/// <item>An alternative is read before every record that draws it, so a new one goes just before the record it is made
/// from, or just after the alternative it copies, and setting a record's table moves the table's alternatives read after it
/// to just before it. A GUID <c>-1</c> record is never exported or merged, so no <c>script.dat</c> index moves.</item>
/// <item>Making a table, setting a record's table and taking it away each change whether the record draws, which changes
/// what every later draw comes out as (docs/herculan/handoff-editor-document.md#conditions-and-variants).</item>
/// </list>
/// </summary>
public static class VariantEditing {
	private const int ConditionRow = 1;

	/// <summary>The bound a chance edit writes when every chance is a whole percent, and a new table's.</summary>
	public const short PercentBound = 100;

	/// <summary>The bound a chance edit writes when a chance has a tenth of a percent.</summary>
	public const short TenthBound = 1000;

	/// <summary>The bound <paramref name="percents"/> are written over: <see cref="PercentBound"/> when each is a whole percent, else <see cref="TenthBound"/>.</summary>
	public static short ChanceBound(IReadOnlyList<decimal> percents) =>
		percents.All(percent => percent % 1 == 0) ? PercentBound : TenthBound;

	// ---- Reading -----------------------------------------------------------------------------------

	/// <summary>Whether row-1 GUID <paramref name="guid"/> is a variant table.</summary>
	public static bool IsTable(MissionDocument document, short guid) =>
		ConditionEditing.IndexOf(document, guid) != null && ConditionEditing.Record(document, guid).Type == ConditionEditing.VariantTable;

	/// <summary>The table range <paramref name="range"/> is over, or null when it is not a range over a table.</summary>
	public static short? TableOfRange(MissionDocument document, short range) =>
		ConditionEditing.IndexOf(document, range) != null && ConditionEditing.Record(document, range).Type == ConditionEditing.Range
			&& ConditionEditing.ParentOf(document, range) is { Type: ConditionEditing.VariantTable } table
			? table.GUID
			: null;

	/// <summary>Every variant table of the mission, by GUID in file order.</summary>
	public static IReadOnlyList<short> Tables(MissionDocument document) =>
		ConditionEditing.Records(document).Where(record => record.Type == ConditionEditing.VariantTable && record.GUID != -1)
			.Select(record => record.GUID).Distinct().ToList();

	/// <summary>Table <paramref name="guid"/> as the file holds it, or null when it is not a table.</summary>
	public static VariantTable? Table(MissionDocument document, short guid) {
		if (!IsTable(document, guid)) {
			return null;
		}

		var records = ConditionEditing.Records(document);
		var table = ConditionEditing.Record(document, guid);
		var sameKey = records.Where(other => other.Type == ConditionEditing.VariantTable && other.GUID != guid
			&& other.FlagIndexOrRangeLower == table.FlagIndexOrRangeLower).Select(other => other.GUID).ToList();
		var ranges = records.Where(record => record.Type == ConditionEditing.Range && TableOfRange(document, record.GUID) == guid).ToList();

		bool first = records.First(other => other.Type == ConditionEditing.VariantTable
			&& other.FlagIndexOrRangeLower == table.FlagIndexOrRangeLower).GUID == guid;
		string? why = records.Count(other => other.GUID == guid) > 1 ? "Its condition number is repeated in the file."
			: table.ComparisonOperand < 1 ? "Its roll is below 1, so no load picks an option."
			: sameKey.Count > 0 && first ? "A load that picks Nothing here picks from a second random choice, so the pair is shown only."
			: sameKey.Count > 0 ? "A load picks from it only when the first random choice of its pair picks Nothing, so the pair is shown only."
			: ranges.Count == 0 ? "It has no options."
			: null;

		var alternatives = new List<VariantAlternative>();
		int row = 0;
		var claimed = new bool[Math.Max((int)table.ComparisonOperand, 0)];
		foreach (var range in ranges) {
			var gated = ConditionEditing.Namers(document, range.GUID);
			var alternative = gated.Records.Count == 1 ? gated.Records[0] : (MsnRecordId?)null;
			why ??= records.Count(other => other.GUID == range.GUID) > 1 ? "An option's condition number is repeated in the file."
				: gated.Records.Count == 0 && gated.Text.Count == 0 ? "An option of it is empty."
				: gated.Records.Count != 1 || gated.Text.Count > 0 ? "An option of it is more than one thing."
				: WhyNoAlternative(document, alternative!.Value, row);
			if (alternative is { } found) {
				row = row == 0 ? found.Row : row;
				int draws = 0;
				for (int draw = 0; draw < claimed.Length; draw++) {
					if (!claimed[draw] && (ushort)range.FlagIndexOrRangeLower <= draw && draw <= (ushort)range.OperatorOrRangeUpperOrResult) {
						claimed[draw] = true;
						draws++;
					}
				}

				alternatives.Add(new VariantAlternative(range.GUID, found, draws));
			}
		}

		why ??= DrawnBySquad(document, table.FlagIndexOrRangeLower) ? "The player's squad picks from it, so it is shown only." : null;
		return new VariantTable(guid, table.FlagIndexOrRangeLower, table.ComparisonOperand, table.ConditionRef, row,
			alternatives, sameKey, why);
	}

	// Why the record a range conditions is not an alternative the editor edits, given the row of those before it (0 for none).
	private static string? WhyNoAlternative(MissionDocument document, MsnRecordId record, int row) =>
		record.Row == 8 ? "Its options are routes, so it is shown only; a route's view edits the waypoints of the option it shows."
		: record.Row is not (>= 12 and <= 14) || document.RecordAt(record) is not { GUID: -1 } alternative
			? "An option of it is not a herc, flyer, structure or ground vehicle."
		: KeyOf(alternative) != -1 ? "An option of it picks from a random choice of its own."
		: row != 0 && row != record.Row ? "Its options are of more than one kind."
		: null;

	/// <summary>
	/// The percents <paramref name="table"/>'s alternatives show, to one decimal: each one's share of the draws, rounded so that
	/// they add up to the rounded share of every alternative together, the largest fractions rounded up first (the editor's
	/// choice).
	/// </summary>
	public static IReadOnlyList<decimal> Percents(VariantTable table) {
		int bound = Math.Max((int)table.Bound, 1);
		var counts = table.Alternatives.Select(alternative => alternative.Draws).ToList();
		var tenths = counts.Select(count => count * 1000 / bound).ToList();
		int target = (counts.Sum() * 2000 + bound) / (2 * bound);
		foreach (int at in Enumerable.Range(0, counts.Count).OrderByDescending(at => counts[at] * 1000 % bound).ThenBy(at => at)
				.Take(Math.Max(target - tenths.Sum(), 0))) {
			tenths[at]++;
		}

		return tenths.Select(tenth => tenth / 10m).ToList();
	}

	/// <summary>Whether <see cref="Percents"/> shows each of <paramref name="table"/>'s chances exactly.</summary>
	public static bool Exact(VariantTable table) =>
		table.Bound > 0 && table.Alternatives.All(alternative => alternative.Draws * 1000 % table.Bound == 0);

	/// <summary>
	/// Every record that draws from <paramref name="table"/>'s key, in any state: those of rows 6 to 8 and 12 to 15 whose
	/// variant key is the table's, a variant alternative's own included, in row and file order.
	/// </summary>
	public static IReadOnlyList<MsnRecordId> Drawers(MissionDocument document, short key) {
		var drawers = new List<MsnRecordId>();
		foreach (int row in new[] { 6, 7, 8, 12, 13, 14, 15 }) {
			var records = MsnRows.Get(document.Mission, row);
			for (int i = 0; i < records.Length; i++) {
				if (records.GetValue(i) is MapObject record && KeyOf(record) == key) {
					drawers.Add(new MsnRecordId(row, i));
				}
			}
		}

		return drawers;
	}

	/// <summary>
	/// The record of roster row <paramref name="row"/>'s GUID <paramref name="guid"/> that carries its variant key: the one of
	/// its records that sets it, or when none does its first record, the one that creates it. Null when more than one sets it,
	/// or no record carries the GUID.
	/// </summary>
	public static MsnRecordId? KeyRecord(MissionDocument document, int row, short guid) {
		var keyed = RecordsOf(document, row, guid).Where(id => KeyOf(document.RecordAt(id)!) != -1).ToList();
		if (keyed.Count > 0) {
			return keyed.Count == 1 ? keyed[0] : null;
		}

		return document.Layout.Entry(row, guid)?.Records[0];
	}

	/// <summary>The table roster row <paramref name="row"/>'s GUID <paramref name="guid"/> draws from: the first of its key's; null for none.</summary>
	public static short? TableOf(MissionDocument document, int row, short guid) {
		if (KeyRecord(document, row, guid) is not { } id) {
			return null;
		}

		short key = KeyOf(document.RecordAt(id)!);
		if (key == -1) {
			return null;
		}

		return ConditionEditing.Records(document).FirstOrDefault(record => record.Type == ConditionEditing.VariantTable
			&& record.FlagIndexOrRangeLower == key)?.GUID;
	}

	/// <summary>
	/// The draw record <paramref name="record"/> makes in the layout, its table and the options it sees, the first of them the one
	/// shown (<see cref="LayoutDraw.Shown"/>); null when it draws nothing or is a variant alternative, which no entry holds.
	/// </summary>
	public static LayoutDraw? DrawOf(MissionDocument document, MsnRecordId record) {
		if (record.Row is < 6 or > 16 || document.RecordAt(record) is not { GUID: not -1 } found
				|| document.Layout.Entry(record.Row, found.GUID) is not { } entry) {
			return null;
		}

		return entry.Presence.Draws.Concat(entry.Variations.Select(variation => variation.Draw).OfType<LayoutDraw>())
			.FirstOrDefault(draw => draw.Drawer == record);
	}

	// ---- Why not -----------------------------------------------------------------------------------

	/// <summary>Why roster row <paramref name="row"/>'s GUID <paramref name="guid"/> cannot be given a table or have it taken away, or null when it can.</summary>
	public static string? WhyNoTables(MissionDocument document, int row, short guid) {
		if (row is not (>= 12 and <= 14)) {
			return "Only hercs, flyers, structures and ground vehicles pick from a random choice here.";
		}

		if (document.Layout.Entry(row, guid) == null) {
			return "It is not in the mission.";
		}

		if (RosterDeletion.Check(document, row, guid).InSquad) {
			return "A squad HERC's random choice is not set here.";
		}

		return KeyRecord(document, row, guid) == null ? "More than one of its records picks at random." : null;
	}

	/// <summary>Why roster row <paramref name="row"/>'s GUID <paramref name="guid"/> cannot draw from <paramref name="table"/>, or none for <c>-1</c>; null when it can.</summary>
	public static string? WhyNoTable(MissionDocument document, int row, short guid, short table) {
		string? reason = WhyNoTables(document, row, guid);
		if (reason != null || table == -1) {
			return reason;
		}

		if (Table(document, table) is not { } read) {
			return $"Condition {table} is not a random choice.";
		}

		return read.WhyFixed ?? (read.Row != row ? $"Its options are {KindOf(read.Row)}s." : null);
	}

	/// <summary>
	/// Why <paramref name="table"/>'s chances cannot be set to <paramref name="percents"/>, one per alternative, each a percent
	/// to one decimal; null when they can.
	/// </summary>
	public static string? WhyNoChances(MissionDocument document, short table, IReadOnlyList<decimal> percents) =>
		Table(document, table) is not { } read ? $"Condition {table} is not a random choice."
		: read.WhyFixed is { } reason ? reason
		: percents.Count != read.Alternatives.Count ? $"It has {read.Alternatives.Count} options."
		: percents.Any(percent => percent is < 0 or > 100) ? "A chance is 0% to 100%."
		: percents.Any(percent => decimal.Round(percent, 1) != percent) ? "A chance takes one decimal at most."
		: percents.Sum() > 100 ? "The chances add up to more than 100%."
		: null;

	/// <summary>Why an alternative cannot be added to <paramref name="table"/>, or null when it can.</summary>
	public static string? WhyNoAdd(MissionDocument document, short table) =>
		Table(document, table) is not { } read ? $"Condition {table} is not a random choice."
		: read.WhyFixed ?? (Percents(read).Max() < 0.2m ? "No option has a chance of 0.2% or more to share." : null);

	/// <summary>Why <paramref name="range"/>'s alternative cannot be removed from <paramref name="table"/>, or null when it can.</summary>
	public static string? WhyNoRemove(MissionDocument document, short table, short range) =>
		Table(document, table) is not { } read ? $"Condition {table} is not a random choice."
		: read.WhyFixed is { } reason ? reason
		: read.Alternatives.All(alternative => alternative.Range != range) ? $"Condition {range} is not one of its options."
		: read.Alternatives.Count == 1 ? "A random choice keeps at least one option."
		: null;

	/// <summary>Why <paramref name="table"/> cannot be deleted, or null when it can (<see cref="PickedFrom"/>, <see cref="Referenced"/>).</summary>
	public static string? WhyNoDelete(MissionDocument document, short table) {
		if (Table(document, table) is not { } read) {
			return $"Condition {table} is not a random choice.";
		}

		if (read.WhyFixed is { } reason) {
			return reason;
		}

		if (Drawers(document, read.Key).Count > 0) {
			return PickedFrom;
		}

		return Referencers(document, read).Any ? Referenced : null;
	}

	/// <summary><see cref="WhyNoDelete"/>'s reason while a record draws by the table's key.</summary>
	public const string PickedFrom = "Records pick from it.";

	/// <summary><see cref="WhyNoDelete"/>'s reason while a record other than its ranges, or a text line, references the table.</summary>
	public const string Referenced = "Records other than its options reference it.";

	/// <summary>
	/// What references <paramref name="table"/> other than its own ranges: the records whose condition is its GUID, and the
	/// <c>.ENG</c> records by index (<see cref="ConditionEditing.Namers"/>).
	/// </summary>
	public static ConditionNamers Referencers(MissionDocument document, VariantTable table) {
		var ranges = table.Alternatives.Select(alternative => alternative.Range).ToHashSet();
		var namers = ConditionEditing.Namers(document, table.Guid);
		return new ConditionNamers(namers.Records.Where(id => id.Row != ConditionRow
			|| !ranges.Contains(ConditionEditing.Records(document)[id.Index].GUID)).ToList(), namers.Text);
	}

	// ---- Editing -----------------------------------------------------------------------------------

	/// <summary>
	/// Gives roster row <paramref name="row"/>'s GUID <paramref name="guid"/> a table of its own, as one
	/// <see cref="MissionDocument.Change"/>: a table appended last in row 1 under a key no table and no record carries
	/// (<see cref="NewKey"/>), bound 100 and requiring nothing; one range over it, 0 to 99; and one alternative, a copy of what
	/// the layout shows the record as (the option its draw shows, <see cref="DrawOf"/>, or else its key record), inserted just
	/// before its key record, which then carries the key. It looks as it did; every later draw changes, as it now draws.
	/// <paramref name="table"/> is the new table's GUID.
	/// </summary>
	public static MsnEdit CreateTable(MissionDocument document, int row, short guid, out short table) {
		if (WhyNoTables(document, row, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var keyRecord = KeyRecord(document, row, guid)!.Value;
		var source = DrawOf(document, keyRecord)?.Shown?.Record ?? keyRecord;
		var alternative = (MapObject)MsnRows.CopyRecord(document.RecordAt(source)!);
		short key = NewKey(document);
		short created = -1;
		var change = document.Change(() => {
			document.Append(ConditionRow, new MissionCondition14 {
				ConditionRef = -1, Type = ConditionEditing.VariantTable, FlagIndexOrRangeLower = key, ComparisonOperand = PercentBound,
			}, out created);
			document.Append(ConditionRow, new MissionCondition14 {
				ConditionRef = created, Type = ConditionEditing.Range, FlagIndexOrRangeLower = 0,
				OperatorOrRangeUpperOrResult = PercentBound - 1,
			}, out short range);
			MakeAlternative(alternative, range);
			document.RewriteAt<MapObject>(row, keyRecord.Index, record => SetKey(record, key));
			document.Insert(row, keyRecord.Index, alternative);
		});
		table = created;
		return change;
	}

	/// <summary>
	/// Sets the table roster row <paramref name="row"/>'s GUID <paramref name="guid"/> draws from, its key record's variant
	/// key, as one <see cref="MissionDocument.Change"/>: <paramref name="table"/>'s key, the table's alternatives read after
	/// the key record moved to just before it; or, for <c>-1</c>, none, the record then carrying as its own the option the
	/// layout shows it with (<see cref="DrawOf"/>; the editor's choice, so it stays as shown).
	/// </summary>
	public static MsnEdit SetTable(MissionDocument document, int row, short guid, short table) {
		if (WhyNoTable(document, row, guid, table) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var keyRecord = KeyRecord(document, row, guid)!.Value;
		if (table == -1) {
			var drawn = DrawOf(document, keyRecord)?.Shown?.Record is { } source ? document.RecordAt(source) : null;
			return document.Change(() => document.RewriteAt<MapObject>(row, keyRecord.Index, record => {
				if (drawn != null) {
					CopyPayload(drawn, record);
				}

				SetKey(record, -1);
			}));
		}

		var read = Table(document, table)!;
		return document.Change(() => {
			document.RewriteAt<MapObject>(row, keyRecord.Index, record => SetKey(record, read.Key));
			var after = read.Alternatives.Select(alternative => alternative.Record).Where(id => id.Index > keyRecord.Index)
				.OrderByDescending(id => id.Index).ToList();
			var moving = after.Select(id => document.RecordAt(id)!).Reverse().ToList();
			foreach (var id in after) {
				document.RemoveAt(row, id.Index);
			}

			for (int i = 0; i < moving.Count; i++) {
				document.Insert(row, keyRecord.Index + i, moving[i]);
			}
		});
	}

	/// <summary>
	/// Adds an alternative to <paramref name="table"/>, a copy of the one range <paramref name="copyOf"/> chooses, inserted just
	/// after it, under a new range appended last in row 1, as one <see cref="MissionDocument.Change"/>. The new one takes half
	/// the chance of the alternative with the largest, the first of those that tie, rounded down to one decimal (the editor's
	/// choice), and the ranges are written as <see cref="SetChances"/> writes them. <paramref name="range"/> is the new range's
	/// GUID.
	/// </summary>
	public static MsnEdit AddAlternative(MissionDocument document, short table, short copyOf, out short range) {
		if (WhyNoAdd(document, table) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var read = Table(document, table)!;
		var copied = read.Alternatives.FirstOrDefault(alternative => alternative.Range == copyOf)
			?? throw new InvalidOperationException($"Condition {copyOf} is not one of its options.");
		var percents = Percents(read).ToList();
		int largest = percents.IndexOf(percents.Max());
		decimal share = Math.Floor(percents[largest] * 5) / 10;
		percents[largest] -= share;
		percents.Add(share);
		var alternative = (MapObject)MsnRows.CopyRecord(document.RecordAt(copied.Record)!);
		short created = -1;
		var change = document.Change(() => {
			document.Append(ConditionRow, new MissionCondition14 { ConditionRef = table, Type = ConditionEditing.Range }, out created);
			MakeAlternative(alternative, created);
			document.Insert(read.Row, copied.Record.Index + 1, alternative);
			WriteChances(document, table, [.. read.Alternatives.Select(other => other.Range), created], percents);
		});
		range = created;
		return change;
	}

	/// <summary>
	/// Removes <paramref name="range"/>'s alternative from <paramref name="table"/>, the range and the record it chooses, as
	/// one <see cref="MissionDocument.Change"/>; its chance goes to the alternative before it, or after it for the first (the
	/// editor's choice), and the ranges are written as <see cref="SetChances"/> writes them.
	/// </summary>
	public static MsnEdit RemoveAlternative(MissionDocument document, short table, short range) {
		if (WhyNoRemove(document, table, range) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var read = Table(document, table)!;
		var percents = Percents(read).ToList();
		int at = read.Alternatives.ToList().FindIndex(alternative => alternative.Range == range);
		percents[at == 0 ? 1 : at - 1] += percents[at];
		percents.RemoveAt(at);
		var kept = read.Alternatives.Where(alternative => alternative.Range != range).Select(alternative => alternative.Range).ToList();
		return document.Change(() => {
			document.RemoveAt(read.Row, read.Alternatives[at].Record.Index);
			document.Remove(ConditionRow, range);
			WriteChances(document, table, kept, percents);
		});
	}

	/// <summary>
	/// Sets <paramref name="table"/>'s chances, one percent to one decimal per alternative in its ranges' row-1 order, adding up
	/// to 100 at most, as one <see cref="MissionDocument.Change"/>: the bound <see cref="ChanceBound"/> gives them, and each range
	/// the next run of draws from 0 for its percent, a 0% range the bound to the bound, which no draw below it reaches. A draw
	/// past the last run takes no alternative here.
	/// </summary>
	public static MsnEdit SetChances(MissionDocument document, short table, IReadOnlyList<decimal> percents) {
		if (WhyNoChances(document, table, percents) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var ranges = Table(document, table)!.Alternatives.Select(alternative => alternative.Range).ToList();
		return document.Change(() => WriteChances(document, table, ranges, percents));
	}

	/// <summary>
	/// Deletes <paramref name="table"/>, its ranges and their alternatives, as one <see cref="MissionDocument.Change"/>; refused
	/// while a record draws by its key, or anything else references it (<see cref="WhyNoDelete"/>).
	/// </summary>
	public static MsnEdit DeleteTable(MissionDocument document, short table) {
		if (WhyNoDelete(document, table) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var read = Table(document, table)!;
		return document.Change(() => {
			foreach (var alternative in read.Alternatives.OrderByDescending(alternative => alternative.Record.Index)) {
				document.RemoveAt(alternative.Record.Row, alternative.Record.Index);
			}

			foreach (var alternative in read.Alternatives) {
				document.Remove(ConditionRow, alternative.Range);
			}

			document.Remove(ConditionRow, table);
		});
	}

	/// <summary>
	/// Sets <paramref name="field"/> of the alternative GUID <paramref name="guid"/>'s base copies it from to
	/// <paramref name="value"/>, as one <see cref="MissionDocument.Change"/>, so every record drawing that alternative changes
	/// (<see cref="MissionDocument.SetAlternativeField"/>); <see cref="MissionDocument.SetField"/> writes the one GUID's overlay
	/// instead. Refused for a field no alternative supplies, and an alternative of a table the editor shows only.
	/// </summary>
	public static MsnEdit SetDrawnField(MissionDocument document, MsnField field, short guid, short value) {
		if (WhyNoDrawnField(document, field, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => document.SetAlternativeField(field, guid, value));
	}

	/// <summary>Why <see cref="SetDrawnField"/> refuses <paramref name="field"/> of GUID <paramref name="guid"/>, or null when it writes it.</summary>
	public static string? WhyNoDrawnField(MissionDocument document, MsnField field, short guid) {
		if (field.Row is not (>= 12 and <= 14) || document.Layout.Entry(field.Row, guid) == null) {
			return "It is not a herc, flyer or structure in the mission.";
		}

		if (document.AlternativeOf(field, guid) is not { } alternative) {
			return "That field is its own, not an alternative's.";
		}

		var range = (document.RecordAt(alternative) switch {
			MechRosterEntry144 mech => mech.ConditionRef,
			FlyerRosterEntry102 flyer => flyer.ConditionRef,
			BaseRosterEntry62 structure => structure.ConditionRef,
			_ => (short)-1,
		});
		return TableOfRange(document, range) is { } table && Table(document, table) is { WhyFixed: null } ? null
			: "The table it draws from is shown only.";
	}

	/// <summary>A variant key no type-3 record and no record of rows 6 to 8 and 12 to 15 carries: one past the largest of them, at least 0 (the editor's choice).</summary>
	public static short NewKey(MissionDocument document) {
		var keys = ConditionEditing.Records(document).Where(record => record.Type == ConditionEditing.VariantTable)
			.Select(record => record.FlagIndexOrRangeLower).ToList();
		foreach (int row in new[] { 6, 7, 8, 12, 13, 14, 15 }) {
			keys.AddRange(MsnRows.Get(document.Mission, row).Cast<MapObject>().Select(KeyOf));
		}

		short largest = keys.DefaultIfEmpty((short)-1).Max();
		return largest == short.MaxValue
			? throw new InvalidOperationException($"The variant keys already reach {short.MaxValue}.")
			: (short)Math.Max(largest + 1, 0);
	}

	// ---- Internals ---------------------------------------------------------------------------------

	// Writes <table>'s bound for <percents> (ChanceBound) and <ranges>' ends from them, inside the caller's change.
	private static void WriteChances(MissionDocument document, short table, IReadOnlyList<short> ranges, IReadOnlyList<decimal> percents) {
		short bound = ChanceBound(percents);
		if (ConditionEditing.Record(document, table).ComparisonOperand != bound) {
			document.Rewrite<MissionCondition14>(ConditionRow, ConditionEditing.IndexOf(document, table)!.Value,
				record => record.ComparisonOperand = bound);
		}

		int start = 0;
		for (int i = 0; i < ranges.Count; i++) {
			int draws = (int)(percents[i] * bound / 100);
			short low = (short)(draws > 0 ? start : bound), high = (short)(draws > 0 ? start + draws - 1 : bound);
			start += draws;
			var record = ConditionEditing.Record(document, ranges[i]);
			if (record.FlagIndexOrRangeLower != low || record.OperatorOrRangeUpperOrResult != high) {
				document.Rewrite<MissionCondition14>(ConditionRow, ConditionEditing.IndexOf(document, ranges[i])!.Value, range => {
					range.FlagIndexOrRangeLower = low;
					range.OperatorOrRangeUpperOrResult = high;
				});
			}
		}
	}

	// Makes <record>, a copy, an alternative chosen by <range>: GUID -1, conditioned on the range, drawing nothing itself.
	private static void MakeAlternative(MapObject record, short range) {
		record.GUID = -1;
		SetKey(record, -1);
		switch (record) {
			case MechRosterEntry144 mech: mech.ConditionRef = range; break;
			case FlyerRosterEntry102 flyer: flyer.ConditionRef = range; break;
			case BaseRosterEntry62 structure: structure.ConditionRef = range; break;
			default: throw new InvalidOperationException($"A {record.GetType().Name} is not an alternative the editor makes.");
		}
	}

	/// <summary>
	/// Copies the span a variant copies (docs/retail/formats/msn-mission-file.md#variants) from <paramref name="from"/> into
	/// <paramref name="to"/>, records of the same roster row: row 12's <c>0x08</c>-<c>0x8f</c> but <c>0x4a</c>, row 13's
	/// <c>0x08</c>-<c>0x65</c> but <c>0x36</c>, row 14's <c>0x08</c>-<c>0x3d</c> but <c>0x0e</c>; the pair count each leaves
	/// out stays <paramref name="to"/>'s.
	/// </summary>
	private static void CopyPayload(MapObject from, MapObject to) {
		switch ((from, to)) {
			case (MechRosterEntry144 a, MechRosterEntry144 b):
				b.AiRadarActive = a.AiRadarActive; b.AiCruiseSpeed = a.AiCruiseSpeed; b.DeadZone = (short[])a.DeadZone.Clone();
				b.TypeIndex = a.TypeIndex; b.WeaponRefs = (short[])a.WeaponRefs.Clone(); b.PositionRef = a.PositionRef;
				b.HeadingRef = a.HeadingRef; b.OutOfActionReport = (short[])a.OutOfActionReport.Clone();
				b.WeaponSecondary = (short[])a.WeaponSecondary.Clone(); b.Constant2 = a.Constant2;
				b.EngagementActionRef = a.EngagementActionRef; b.DefeatActionRef = a.DefeatActionRef;
				b.StartingCondition = a.StartingCondition;
				break;
			case (FlyerRosterEntry102 a, FlyerRosterEntry102 b):
				b.FlagSpan = (short[])a.FlagSpan.Clone(); b.PositionRef = a.PositionRef; b.HeadingRef = a.HeadingRef;
				b.TypeIndex = a.TypeIndex; b.OutOfActionReport = (short[])a.OutOfActionReport.Clone();
				b.EngagementActionRef = a.EngagementActionRef; b.DefeatActionRef = a.DefeatActionRef; b.UnkVal_100 = a.UnkVal_100;
				break;
			case (BaseRosterEntry62 a, BaseRosterEntry62 b):
				b.TypeIndex = a.TypeIndex; b.PositionRef = a.PositionRef; b.HeadingRef = a.HeadingRef;
				b.OutOfActionReport = (short[])a.OutOfActionReport.Clone(); b.EngagementActionRef = a.EngagementActionRef;
				b.DefeatActionRef = a.DefeatActionRef; b.StartingCondition = a.StartingCondition;
				break;
			default:
				throw new InvalidOperationException($"A {from.GetType().Name}'s payload is not copied into a {to.GetType().Name}.");
		}
	}

	private static IEnumerable<MsnRecordId> RecordsOf(MissionDocument document, int row, short guid) {
		var records = MsnRows.Get(document.Mission, row);
		for (int i = 0; i < records.Length; i++) {
			if (records.GetValue(i) is MapObject record && record.GUID == guid) {
				yield return new MsnRecordId(row, i);
			}
		}
	}

	// Whether a herc group 0 references draws by <key>: a squad record a table drives, which the editor leaves alone.
	private static bool DrawnBySquad(MissionDocument document, short key) =>
		SquadRoster.Squad(document) != -1
		&& Drawers(document, key).Any(id => id.Row == 12 && document.RecordAt(id) is { GUID: not -1 } herc
			&& RosterDeletion.Check(document, 12, herc.GUID).InSquad);

	/// <summary>A record's variant key, <c>-1</c> for one that draws nothing or a row that has none.</summary>
	private static short KeyOf(MapObject record) => record switch {
		MapPoint22 point => point.VariantKey,
		Heading10 heading => heading.VariantKey,
		WaypointGroup route => route.VariantKey,
		MechRosterEntry144 mech => mech.VariantKey,
		FlyerRosterEntry102 flyer => flyer.VariantKey,
		BaseRosterEntry62 structure => structure.VariantKey,
		MissionOrder22 order => order.VariantKey,
		_ => -1,
	};

	private static void SetKey(MapObject record, short key) {
		switch (record) {
			case MechRosterEntry144 mech: mech.VariantKey = key; break;
			case FlyerRosterEntry102 flyer: flyer.VariantKey = key; break;
			case BaseRosterEntry62 structure: structure.VariantKey = key; break;
			default: throw new InvalidOperationException($"A {record.GetType().Name}'s variant key is not set here.");
		}
	}

	private static string KindOf(int row) => row switch {
		12 => "herc",
		13 => "flyer",
		14 => "structure",
		8 => "route",
		_ => "record",
	};
}
