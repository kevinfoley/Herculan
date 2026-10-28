using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>One option of a random table: the range that chooses it and the template it copies.</summary>
/// <param name="Range">The type-2 range's GUID.</param>
/// <param name="Template">The GUID <c>-1</c> record the range conditions, which a user picking the option copies from.</param>
/// <param name="Draws">How many of the table's draws, 0 to its bound less one, take it: those its range holds that no range before it in row 1 holds.</param>
public sealed record RandomTableOption(short Range, MsnRecordId Template, int Draws);

/// <summary>
/// A random table, a type-3 row-1 record, as <see cref="RandomTableEditing.Table"/> reads it: its key, its bound, what it
/// requires, the row its templates are of and each option in its ranges' row-1 order, the other tables under its key,
/// and why the editor shows it without editing it.
/// </summary>
public sealed record RandomTable(short Guid, short Key, short Bound, short Requires, int Row,
		IReadOnlyList<RandomTableOption> Options, IReadOnlyList<short> SameKey, string? WhyFixed) {
	/// <summary>How many of the table's draws no range holds.</summary>
	public int Leftover => Math.Max(Bound, (short)0) - Options.Sum(option => option.Draws);

	/// <summary>A share of the draws, <paramref name="draws"/> of <see cref="Bound"/>, as a percent.</summary>
	public double Percent(int draws) => Bound > 0 ? draws * 100.0 / Bound : 0;
}

/// <summary>
/// Creates, edits and deletes a <see cref="MissionDocument"/>'s random tables, and sets which table a herc, flyer, structure
/// or ground vehicle picks from, as record edits. How a record picks is docs/retail/formats/msn-mission-file.md#random-tables:
/// a record whose random table key is set draws below the bound of the first surviving type-3 record of that key, takes the
/// first range over it holding the draw, and copies its row's span from that option's template, the first GUID <c>-1</c>
/// record read before it that the range conditions.
///
/// <list type="bullet">
/// <item>A table is edited when each of its ranges conditions one GUID <c>-1</c> record of row 12, 13 or 14 picking from no
/// table itself, all of one row, no other table carries its key, and the player's squad picks nothing from it
/// (<see cref="Table"/>'s <see cref="RandomTable.WhyFixed"/>). The rest, row 8's waypoint lists among them, are shown
/// only.</item>
/// <item>The chances are percents to one decimal: each edit of them writes the ranges over the table, contiguous from 0 in the
/// ranges' row-1 order, and the bound <see cref="ChanceBound"/> gives them, 100 when every chance is a whole percent, else
/// 1000 (<see cref="SetChances"/>). A draw below any bound of 1 or more takes one step of VSHELL's generator,
/// <c>ShellRandom_Below</c> (<c>004659ec</c>) being <c>(ShellRandom_Next() &amp; 0x7fff) % n</c>
/// (docs/retail/shell/campaign-loop.md#the-shells-generator), so writing the bound moves no later draw; no retail table's
/// bound is below 1. A draw no range holds takes no option from this table (the editor's chances leave that as the
/// leftover).</item>
/// <item>A template is read before every user that picks it, so a new one goes just before the record it is made from, or
/// just after the template it copies, and setting a record's table moves the table's templates read after it to just before
/// it. A GUID <c>-1</c> record is never exported or merged, so no <c>script.dat</c> index moves.</item>
/// <item>Making a table, setting a record's table and taking it away each change whether the record picks, which changes
/// what every later draw comes out as (docs/herculan/handoff-editor-document.md#conditions-and-random-tables).</item>
/// </list>
/// </summary>
public static class RandomTableEditing {
	private const int ConditionRow = 1;

	/// <summary>The bound a chance edit writes when every chance is a whole percent, and a new table's.</summary>
	public const short PercentBound = 100;

	/// <summary>The bound a chance edit writes when a chance has a tenth of a percent.</summary>
	public const short TenthBound = 1000;

	/// <summary>The bound <paramref name="percents"/> are written over: <see cref="PercentBound"/> when each is a whole percent, else <see cref="TenthBound"/>.</summary>
	public static short ChanceBound(IReadOnlyList<decimal> percents) =>
		percents.All(percent => percent % 1 == 0) ? PercentBound : TenthBound;

	// ---- Reading -----------------------------------------------------------------------------------

	/// <summary>Whether row-1 GUID <paramref name="guid"/> is a random table.</summary>
	public static bool IsTable(MissionDocument document, short guid) =>
		ConditionEditing.IndexOf(document, guid) != null && ConditionEditing.Record(document, guid).Type == ConditionEditing.RandomTable;

	/// <summary>The table range <paramref name="range"/> is over, or null when it is not a range over a table.</summary>
	public static short? TableOfRange(MissionDocument document, short range) =>
		ConditionEditing.IndexOf(document, range) != null && ConditionEditing.Record(document, range).Type == ConditionEditing.Range
			&& ConditionEditing.ParentOf(document, range) is { Type: ConditionEditing.RandomTable } table
			? table.GUID
			: null;

	/// <summary>Every random table of the mission, by GUID in file order.</summary>
	public static IReadOnlyList<short> Tables(MissionDocument document) =>
		ConditionEditing.Records(document).Where(record => record.Type == ConditionEditing.RandomTable && record.GUID != -1)
			.Select(record => record.GUID).Distinct().ToList();

	/// <summary>Table <paramref name="guid"/> as the file holds it, or null when it is not a table.</summary>
	public static RandomTable? Table(MissionDocument document, short guid) {
		if (!IsTable(document, guid)) {
			return null;
		}

		var records = ConditionEditing.Records(document);
		var table = ConditionEditing.Record(document, guid);
		var sameKey = records.Where(other => other.Type == ConditionEditing.RandomTable && other.GUID != guid
			&& other.FlagIndexOrRangeLower == table.FlagIndexOrRangeLower).Select(other => other.GUID).ToList();
		var ranges = records.Where(record => record.Type == ConditionEditing.Range && TableOfRange(document, record.GUID) == guid).ToList();

		bool first = records.First(other => other.Type == ConditionEditing.RandomTable
			&& other.FlagIndexOrRangeLower == table.FlagIndexOrRangeLower).GUID == guid;
		string? why = records.Count(other => other.GUID == guid) > 1 ? "Its condition number is repeated in the file."
			: table.ComparisonOperand < 1 ? "Its roll is below 1, so no load picks an option."
			: sameKey.Count > 0 && first ? "A load that picks Nothing here picks from a second random table, so the pair is shown only."
			: sameKey.Count > 0 ? "A load picks from it only when the first random table of its pair picks Nothing, so the pair is shown only."
			: ranges.Count == 0 ? "It has no options."
			: null;

		var options = new List<RandomTableOption>();
		int row = 0;
		var claimed = new bool[Math.Max((int)table.ComparisonOperand, 0)];
		foreach (var range in ranges) {
			var gated = ConditionEditing.Namers(document, range.GUID);
			var template = gated.Records.Count == 1 ? gated.Records[0] : (MsnRecordId?)null;
			why ??= records.Count(other => other.GUID == range.GUID) > 1 ? "An option's condition number is repeated in the file."
				: gated.Records.Count == 0 && gated.Text.Count == 0 ? "An option of it is empty."
				: gated.Records.Count != 1 || gated.Text.Count > 0 ? "An option of it is more than one thing."
				: WhyNoOption(document, template!.Value, row);
			if (template is { } found) {
				row = row == 0 ? found.Row : row;
				int draws = 0;
				for (int draw = 0; draw < claimed.Length; draw++) {
					if (!claimed[draw] && (ushort)range.FlagIndexOrRangeLower <= draw && draw <= (ushort)range.OperatorOrRangeUpperOrResult) {
						claimed[draw] = true;
						draws++;
					}
				}

				options.Add(new RandomTableOption(range.GUID, found, draws));
			}
		}

		why ??= PickedBySquad(document, table.FlagIndexOrRangeLower) ? "The player's squad picks from it, so it is shown only." : null;
		return new RandomTable(guid, table.FlagIndexOrRangeLower, table.ComparisonOperand, table.ConditionRef, row,
			options, sameKey, why);
	}

	// Why the record a range conditions is not a template the editor edits, given the row of those before it (0 for none).
	private static string? WhyNoOption(MissionDocument document, MsnRecordId record, int row) =>
		record.Row == 8 ? "Its options are routes, so it is shown only; a route's view edits the waypoints of the option it shows."
		: record.Row is not (>= 12 and <= 14) || document.RecordAt(record) is not { GUID: -1 } template
			? "An option of it is not a herc, flyer, structure or ground vehicle."
		: KeyOf(template) != -1 ? "An option of it picks from a random table of its own."
		: row != 0 && row != record.Row ? "Its options are of more than one kind."
		: null;

	/// <summary>
	/// The percents <paramref name="table"/>'s options show, to one decimal: each one's share of the draws, rounded so that
	/// they add up to the rounded share of every option together, the largest fractions rounded up first (the editor's
	/// choice).
	/// </summary>
	public static IReadOnlyList<decimal> Percents(RandomTable table) {
		int bound = Math.Max((int)table.Bound, 1);
		var counts = table.Options.Select(option => option.Draws).ToList();
		var tenths = counts.Select(count => count * 1000 / bound).ToList();
		int target = (counts.Sum() * 2000 + bound) / (2 * bound);
		foreach (int at in Enumerable.Range(0, counts.Count).OrderByDescending(at => counts[at] * 1000 % bound).ThenBy(at => at)
				.Take(Math.Max(target - tenths.Sum(), 0))) {
			tenths[at]++;
		}

		return tenths.Select(tenth => tenth / 10m).ToList();
	}

	/// <summary>Whether <see cref="Percents"/> shows each of <paramref name="table"/>'s chances exactly.</summary>
	public static bool Exact(RandomTable table) =>
		table.Bound > 0 && table.Options.All(option => option.Draws * 1000 % table.Bound == 0);

	/// <summary>
	/// Every user of random table key <paramref name="key"/>, in any state: those of rows 6 to 8 and 12 to 15 whose random
	/// table key it is, a template's own included, in row and file order.
	/// </summary>
	public static IReadOnlyList<MsnRecordId> Users(MissionDocument document, short key) {
		var users = new List<MsnRecordId>();
		foreach (int row in new[] { 6, 7, 8, 12, 13, 14, 15 }) {
			var records = MsnRows.Get(document.Mission, row);
			for (int i = 0; i < records.Length; i++) {
				if (records.GetValue(i) is MapObject record && KeyOf(record) == key) {
					users.Add(new MsnRecordId(row, i));
				}
			}
		}

		return users;
	}

	/// <summary>
	/// The record of roster row <paramref name="row"/>'s GUID <paramref name="guid"/> that carries its random table key: the one of
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

	/// <summary>The table roster row <paramref name="row"/>'s GUID <paramref name="guid"/> picks from: the first of its key's; null for none.</summary>
	public static short? TableOf(MissionDocument document, int row, short guid) {
		if (KeyRecord(document, row, guid) is not { } id) {
			return null;
		}

		short key = KeyOf(document.RecordAt(id)!);
		if (key == -1) {
			return null;
		}

		return ConditionEditing.Records(document).FirstOrDefault(record => record.Type == ConditionEditing.RandomTable
			&& record.FlagIndexOrRangeLower == key)?.GUID;
	}

	/// <summary>
	/// The pick record <paramref name="record"/> makes in the layout, its table and the options it sees, the first of them the one
	/// shown (<see cref="LayoutPick.Shown"/>); null when it picks from no table or is a template, which no entry holds.
	/// </summary>
	public static LayoutPick? PickOf(MissionDocument document, MsnRecordId record) {
		if (record.Row is < 6 or > 16 || document.RecordAt(record) is not { GUID: not -1 } found
				|| document.Layout.Entry(record.Row, found.GUID) is not { } entry) {
			return null;
		}

		return entry.Presence.Picks.Concat(entry.Variations.Select(variation => variation.Pick).OfType<LayoutPick>())
			.FirstOrDefault(pick => pick.User == record);
	}

	// ---- Why not -----------------------------------------------------------------------------------

	/// <summary>Why roster row <paramref name="row"/>'s GUID <paramref name="guid"/> cannot be given a table or have it taken away, or null when it can.</summary>
	public static string? WhyNoTables(MissionDocument document, int row, short guid) {
		if (row is not (>= 12 and <= 14)) {
			return "Only hercs, flyers, structures and ground vehicles pick from a random table here.";
		}

		if (document.Layout.Entry(row, guid) == null) {
			return "It is not in the mission.";
		}

		if (RosterDeletion.Check(document, row, guid).InSquad) {
			return "A squad HERC's random table is not set here.";
		}

		return KeyRecord(document, row, guid) == null ? "More than one of its records picks at random." : null;
	}

	/// <summary>Why roster row <paramref name="row"/>'s GUID <paramref name="guid"/> cannot pick from <paramref name="table"/>, or none for <c>-1</c>; null when it can.</summary>
	public static string? WhyNoTable(MissionDocument document, int row, short guid, short table) {
		string? reason = WhyNoTables(document, row, guid);
		if (reason != null || table == -1) {
			return reason;
		}

		if (Table(document, table) is not { } read) {
			return $"Condition {table} is not a random table.";
		}

		return read.WhyFixed ?? (read.Row != row ? $"Its options are {KindOf(read.Row)}s." : null);
	}

	/// <summary>
	/// Why <paramref name="table"/>'s chances cannot be set to <paramref name="percents"/>, one per option, each a percent
	/// to one decimal; null when they can.
	/// </summary>
	public static string? WhyNoChances(MissionDocument document, short table, IReadOnlyList<decimal> percents) =>
		Table(document, table) is not { } read ? $"Condition {table} is not a random table."
		: read.WhyFixed is { } reason ? reason
		: percents.Count != read.Options.Count ? $"It has {read.Options.Count} options."
		: percents.Any(percent => percent is < 0 or > 100) ? "A chance is 0% to 100%."
		: percents.Any(percent => decimal.Round(percent, 1) != percent) ? "A chance takes one decimal at most."
		: percents.Sum() > 100 ? "The chances add up to more than 100%."
		: null;

	/// <summary>Why an option cannot be added to <paramref name="table"/>, or null when it can.</summary>
	public static string? WhyNoAdd(MissionDocument document, short table) =>
		Table(document, table) is not { } read ? $"Condition {table} is not a random table."
		: read.WhyFixed ?? (Percents(read).Max() < 0.2m ? "No option has a chance of 0.2% or more to share." : null);

	/// <summary>Why <paramref name="range"/>'s option cannot be removed from <paramref name="table"/>, or null when it can.</summary>
	public static string? WhyNoRemove(MissionDocument document, short table, short range) =>
		Table(document, table) is not { } read ? $"Condition {table} is not a random table."
		: read.WhyFixed is { } reason ? reason
		: read.Options.All(option => option.Range != range) ? $"Condition {range} is not one of its options."
		: read.Options.Count == 1 ? "A random table keeps at least one option."
		: null;

	/// <summary>Why <paramref name="table"/> cannot be deleted, or null when it can (<see cref="PickedFrom"/>, <see cref="Referenced"/>).</summary>
	public static string? WhyNoDelete(MissionDocument document, short table) {
		if (Table(document, table) is not { } read) {
			return $"Condition {table} is not a random table.";
		}

		if (read.WhyFixed is { } reason) {
			return reason;
		}

		if (Users(document, read.Key).Count > 0) {
			return PickedFrom;
		}

		return Referencers(document, read).Any ? Referenced : null;
	}

	/// <summary><see cref="WhyNoDelete"/>'s reason while a record picks by the table's key.</summary>
	public const string PickedFrom = "Records pick from it.";

	/// <summary><see cref="WhyNoDelete"/>'s reason while a record other than its ranges, or a text line, references the table.</summary>
	public const string Referenced = "Records other than its options reference it.";

	/// <summary>
	/// What references <paramref name="table"/> other than its own ranges: the records whose condition is its GUID, and the
	/// <c>.ENG</c> records by index (<see cref="ConditionEditing.Namers"/>).
	/// </summary>
	public static ConditionNamers Referencers(MissionDocument document, RandomTable table) {
		var ranges = table.Options.Select(option => option.Range).ToHashSet();
		var namers = ConditionEditing.Namers(document, table.Guid);
		return new ConditionNamers(namers.Records.Where(id => id.Row != ConditionRow
			|| !ranges.Contains(ConditionEditing.Records(document)[id.Index].GUID)).ToList(), namers.Text);
	}

	// ---- Editing -----------------------------------------------------------------------------------

	/// <summary>
	/// Gives roster row <paramref name="row"/>'s GUID <paramref name="guid"/> a table of its own, as one
	/// <see cref="MissionDocument.Change"/>: a table appended last in row 1 under a key no table and no record carries
	/// (<see cref="NewKey"/>), bound 100 and requiring nothing; one range over it, 0 to 99; and one option, its template a copy
	/// of what the layout shows the record as (the option its pick shows, <see cref="PickOf"/>, or else its key record),
	/// inserted just before its key record, which then carries the key. It looks as it did; every later draw changes, as it now
	/// picks.
	/// <paramref name="table"/> is the new table's GUID.
	/// </summary>
	public static MsnEdit CreateTable(MissionDocument document, int row, short guid, out short table) {
		if (WhyNoTables(document, row, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var keyRecord = KeyRecord(document, row, guid)!.Value;
		var source = PickOf(document, keyRecord)?.Shown?.Record ?? keyRecord;
		var template = (MapObject)MsnRows.CopyRecord(document.RecordAt(source)!);
		short key = NewKey(document);
		short created = -1;
		var change = document.Change(() => {
			document.Append(ConditionRow, new MissionCondition14 {
				ConditionRef = -1, Type = ConditionEditing.RandomTable, FlagIndexOrRangeLower = key, ComparisonOperand = PercentBound,
			}, out created);
			document.Append(ConditionRow, new MissionCondition14 {
				ConditionRef = created, Type = ConditionEditing.Range, FlagIndexOrRangeLower = 0,
				OperatorOrRangeUpperOrResult = PercentBound - 1,
			}, out short range);
			MakeTemplate(template, range);
			document.RewriteAt<MapObject>(row, keyRecord.Index, record => SetKey(record, key));
			document.Insert(row, keyRecord.Index, template);
		});
		table = created;
		return change;
	}

	/// <summary>
	/// Sets the table roster row <paramref name="row"/>'s GUID <paramref name="guid"/> picks from, its key record's random
	/// table key, as one <see cref="MissionDocument.Change"/>: <paramref name="table"/>'s key, the table's templates read after
	/// the key record moved to just before it; or, for <c>-1</c>, none, the record then carrying as its own the option the
	/// layout shows it with (<see cref="PickOf"/>; the editor's choice, so it stays as shown).
	/// </summary>
	public static MsnEdit SetTable(MissionDocument document, int row, short guid, short table) {
		if (WhyNoTable(document, row, guid, table) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var keyRecord = KeyRecord(document, row, guid)!.Value;
		if (table == -1) {
			var shown = PickOf(document, keyRecord)?.Shown?.Record is { } source ? document.RecordAt(source) : null;
			return document.Change(() => document.RewriteAt<MapObject>(row, keyRecord.Index, record => {
				if (shown != null) {
					CopyPayload(shown, record);
				}

				SetKey(record, -1);
			}));
		}

		var read = Table(document, table)!;
		return document.Change(() => {
			document.RewriteAt<MapObject>(row, keyRecord.Index, record => SetKey(record, read.Key));
			var after = read.Options.Select(option => option.Template).Where(id => id.Index > keyRecord.Index)
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
	/// Adds an option to <paramref name="table"/>, its template a copy of the one range <paramref name="copyOf"/> chooses,
	/// inserted just after it, under a new range appended last in row 1, as one <see cref="MissionDocument.Change"/>. The new
	/// one takes half the chance of the option with the largest, the first of those that tie, rounded down to one decimal (the editor's
	/// choice), and the ranges are written as <see cref="SetChances"/> writes them. <paramref name="range"/> is the new range's
	/// GUID.
	/// </summary>
	public static MsnEdit AddOption(MissionDocument document, short table, short copyOf, out short range) {
		if (WhyNoAdd(document, table) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var read = Table(document, table)!;
		var copied = read.Options.FirstOrDefault(option => option.Range == copyOf)
			?? throw new InvalidOperationException($"Condition {copyOf} is not one of its options.");
		var percents = Percents(read).ToList();
		int largest = percents.IndexOf(percents.Max());
		decimal share = Math.Floor(percents[largest] * 5) / 10;
		percents[largest] -= share;
		percents.Add(share);
		var template = (MapObject)MsnRows.CopyRecord(document.RecordAt(copied.Template)!);
		short created = -1;
		var change = document.Change(() => {
			document.Append(ConditionRow, new MissionCondition14 { ConditionRef = table, Type = ConditionEditing.Range }, out created);
			MakeTemplate(template, created);
			document.Insert(read.Row, copied.Template.Index + 1, template);
			WriteChances(document, table, [.. read.Options.Select(other => other.Range), created], percents);
		});
		range = created;
		return change;
	}

	/// <summary>
	/// Removes <paramref name="range"/>'s option from <paramref name="table"/>, the range and the template it chooses, as
	/// one <see cref="MissionDocument.Change"/>; its chance goes to the option before it, or after it for the first (the
	/// editor's choice), and the ranges are written as <see cref="SetChances"/> writes them.
	/// </summary>
	public static MsnEdit RemoveOption(MissionDocument document, short table, short range) {
		if (WhyNoRemove(document, table, range) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var read = Table(document, table)!;
		var percents = Percents(read).ToList();
		int at = read.Options.ToList().FindIndex(option => option.Range == range);
		percents[at == 0 ? 1 : at - 1] += percents[at];
		percents.RemoveAt(at);
		var kept = read.Options.Where(option => option.Range != range).Select(option => option.Range).ToList();
		return document.Change(() => {
			document.RemoveAt(read.Row, read.Options[at].Template.Index);
			document.Remove(ConditionRow, range);
			WriteChances(document, table, kept, percents);
		});
	}

	/// <summary>
	/// Sets <paramref name="table"/>'s chances, one percent to one decimal per option in its ranges' row-1 order, adding up
	/// to 100 at most, as one <see cref="MissionDocument.Change"/>: the bound <see cref="ChanceBound"/> gives them, and each range
	/// the next run of draws from 0 for its percent, a 0% range the bound to the bound, which no draw below it reaches. A draw
	/// past the last run takes no option here.
	/// </summary>
	public static MsnEdit SetChances(MissionDocument document, short table, IReadOnlyList<decimal> percents) {
		if (WhyNoChances(document, table, percents) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var ranges = Table(document, table)!.Options.Select(option => option.Range).ToList();
		return document.Change(() => WriteChances(document, table, ranges, percents));
	}

	/// <summary>
	/// Deletes <paramref name="table"/>, its ranges and their templates, as one <see cref="MissionDocument.Change"/>; refused
	/// while a record picks by its key, or anything else references it (<see cref="WhyNoDelete"/>).
	/// </summary>
	public static MsnEdit DeleteTable(MissionDocument document, short table) {
		if (WhyNoDelete(document, table) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var read = Table(document, table)!;
		return document.Change(() => {
			foreach (var option in read.Options.OrderByDescending(option => option.Template.Index)) {
				document.RemoveAt(option.Template.Row, option.Template.Index);
			}

			foreach (var option in read.Options) {
				document.Remove(ConditionRow, option.Range);
			}

			document.Remove(ConditionRow, table);
		});
	}

	/// <summary>
	/// Sets <paramref name="field"/> of the template GUID <paramref name="guid"/>'s base copies it from to
	/// <paramref name="value"/>, as one <see cref="MissionDocument.Change"/>, so every user picking that template changes
	/// (<see cref="MissionDocument.SetTemplateField"/>); <see cref="MissionDocument.SetField"/> writes the one GUID's overlay
	/// instead. Refused for a field no template supplies, and a template of a table the editor shows only.
	/// </summary>
	public static MsnEdit SetTemplateField(MissionDocument document, MsnField field, short guid, short value) {
		if (WhyNoTemplateField(document, field, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => document.SetTemplateField(field, guid, value));
	}

	/// <summary>Why <see cref="SetTemplateField"/> refuses <paramref name="field"/> of GUID <paramref name="guid"/>, or null when it writes it.</summary>
	public static string? WhyNoTemplateField(MissionDocument document, MsnField field, short guid) {
		if (field.Row is not (>= 12 and <= 14) || document.Layout.Entry(field.Row, guid) == null) {
			return "It is not a herc, flyer or structure in the mission.";
		}

		if (document.TemplateOf(field, guid) is not { } template) {
			return "That field is its own, not a template's.";
		}

		var range = (document.RecordAt(template) switch {
			MechRosterEntry144 mech => mech.ConditionRef,
			FlyerRosterEntry102 flyer => flyer.ConditionRef,
			BaseRosterEntry62 structure => structure.ConditionRef,
			_ => (short)-1,
		});
		return TableOfRange(document, range) is { } table && Table(document, table) is { WhyFixed: null } ? null
			: "The table it picks from is shown only.";
	}

	/// <summary>A random table key no type-3 record and no record of rows 6 to 8 and 12 to 15 carries: one past the largest of them, at least 0 (the editor's choice).</summary>
	public static short NewKey(MissionDocument document) {
		var keys = ConditionEditing.Records(document).Where(record => record.Type == ConditionEditing.RandomTable)
			.Select(record => record.FlagIndexOrRangeLower).ToList();
		foreach (int row in new[] { 6, 7, 8, 12, 13, 14, 15 }) {
			keys.AddRange(MsnRows.Get(document.Mission, row).Cast<MapObject>().Select(KeyOf));
		}

		short largest = keys.DefaultIfEmpty((short)-1).Max();
		return largest == short.MaxValue
			? throw new InvalidOperationException($"The random table keys already reach {short.MaxValue}.")
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

	// Makes <record>, a copy, a template chosen by <range>: GUID -1, conditioned on the range, picking from no table itself.
	private static void MakeTemplate(MapObject record, short range) {
		record.GUID = -1;
		SetKey(record, -1);
		switch (record) {
			case MechRosterEntry144 mech: mech.ConditionRef = range; break;
			case FlyerRosterEntry102 flyer: flyer.ConditionRef = range; break;
			case BaseRosterEntry62 structure: structure.ConditionRef = range; break;
			default: throw new InvalidOperationException($"A {record.GetType().Name} is not a template the editor makes.");
		}
	}

	/// <summary>
	/// Copies the span a user takes from its template (docs/retail/formats/msn-mission-file.md#random-tables) from <paramref name="from"/> into
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

	// Whether a herc group 0 references picks by <key>: a squad record a table drives, which the editor leaves alone.
	private static bool PickedBySquad(MissionDocument document, short key) =>
		SquadRoster.Squad(document) != -1
		&& Users(document, key).Any(id => id.Row == 12 && document.RecordAt(id) is { GUID: not -1 } herc
			&& RosterDeletion.Check(document, 12, herc.GUID).InSquad);

	/// <summary>A record's random table key, <c>-1</c> for one that picks from no table or a row that has none.</summary>
	private static short KeyOf(MapObject record) => record switch {
		MapPoint22 point => point.RandomTableKey,
		Heading10 heading => heading.RandomTableKey,
		WaypointGroup route => route.RandomTableKey,
		MechRosterEntry144 mech => mech.RandomTableKey,
		FlyerRosterEntry102 flyer => flyer.RandomTableKey,
		BaseRosterEntry62 structure => structure.RandomTableKey,
		MissionOrder22 order => order.RandomTableKey,
		_ => -1,
	};

	private static void SetKey(MapObject record, short key) {
		switch (record) {
			case MechRosterEntry144 mech: mech.RandomTableKey = key; break;
			case FlyerRosterEntry102 flyer: flyer.RandomTableKey = key; break;
			case BaseRosterEntry62 structure: structure.RandomTableKey = key; break;
			default: throw new InvalidOperationException($"A {record.GetType().Name}'s random table key is not set here.");
		}
	}

	private static string KindOf(int row) => row switch {
		12 => "HERC",
		13 => "flyer",
		14 => "structure",
		8 => "route",
		_ => "record",
	};
}
