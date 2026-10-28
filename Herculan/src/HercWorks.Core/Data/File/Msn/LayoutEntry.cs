using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// One thing of a <see cref="MissionLayout"/> that rows 6 to 16 key by GUID: a point, heading, route, trigger area, action,
/// timer, herc, flyer, structure or ground vehicle, order or group, with every record of its GUID.
///
/// <para>Its <see cref="Fields"/> are its <b>base</b>: the records carrying the same condition as its first record, the one
/// that creates it, merged in file order by the row's <see cref="MergeRules"/> (a replace takes the later record whole, an
/// overlay each word that is not its unset value), each keyed record first copying the option it shows
/// (<see cref="LayoutPick"/>). Every record under any other condition is a <see cref="LayoutVariation"/>. Refs in the
/// fields are GUIDs, as the file holds them.</para>
/// </summary>
public class LayoutEntry {
	private readonly WordSource[] _suppliers;

	internal LayoutEntry(int row, short guid, IReadOnlyList<MsnRecordId> records, IReadOnlyList<MsnRecordId> baseRecords, MapObject fields,
			WordSource[] suppliers, LayoutPresence presence, IReadOnlyList<LayoutVariation> variations) {
		Row = row;
		Guid = guid;
		Records = records;
		Base = baseRecords;
		Fields = fields;
		_suppliers = suppliers;
		Presence = presence;
		Variations = variations;
	}

	/// <summary>The <c>.MSN</c> row, 6 to 16.</summary>
	public int Row { get; }

	/// <summary>The GUID every record of the entry carries.</summary>
	public short Guid { get; }

	/// <summary>Every record of the GUID, in file order; the first creates it.</summary>
	public IReadOnlyList<MsnRecordId> Records { get; }

	/// <summary>The base's records, in file order: those of <see cref="Records"/> carrying the first one's condition.</summary>
	public IReadOnlyList<MsnRecordId> Base { get; }

	/// <summary>The base's fields, merged: a copy, never a record of the file.</summary>
	public MapObject Fields { get; }

	/// <summary>When the entry is present, and the option each keyed base record shows.</summary>
	public LayoutPresence Presence { get; }

	/// <summary>Each record of the GUID under a condition other than the base's, in file order.</summary>
	public IReadOnlyList<LayoutVariation> Variations { get; }

	/// <summary>
	/// The record whose value word <paramref name="word"/> of <see cref="Fields"/> shows (the model classes' <c>…Word</c>
	/// constants): the last base record that sets it, else the first record, with the option it was copied from when a keyed
	/// record took it from one. For a route, word <see cref="MergeRules.RouteListWord"/> and every word after it, its waypoint
	/// count and list, are one.
	/// </summary>
	public WordSource Supplier(int word) {
		if (Row == 8) {
			word = Math.Min(word, MergeRules.RouteListWord);
		}

		if (word < 0 || word >= _suppliers.Length) {
			throw new ArgumentOutOfRangeException(nameof(word), word, $"A row-{Row} record has {_suppliers.Length} words.");
		}

		return _suppliers[word];
	}
}

/// <summary>A <see cref="LayoutEntry"/> whose <see cref="Fields"/> are of the row's record type.</summary>
public sealed class LayoutEntry<T> : LayoutEntry where T : MapObject {
	internal LayoutEntry(int row, short guid, IReadOnlyList<MsnRecordId> records, IReadOnlyList<MsnRecordId> baseRecords, T fields,
			WordSource[] suppliers, LayoutPresence presence, IReadOnlyList<LayoutVariation> variations)
		: base(row, guid, records, baseRecords, fields, suppliers, presence, variations) {
	}

	/// <inheritdoc cref="LayoutEntry.Fields"/>
	public new T Fields => (T)base.Fields;
}

/// <summary>
/// A record of an entry's GUID under a condition other than its base's: the condition it carries, its own fields (a copy,
/// the option it shows copied in when it is keyed) and the words it sets over the base when its condition holds — every word
/// for a row that replaces, each overlay word not at its unset value for one that overlays.
/// </summary>
public sealed record LayoutVariation(MsnRecordId Record, short Condition, MapObject Fields, IReadOnlyList<int> Words, LayoutPick? Pick);

/// <summary>
/// When a thing is present: <see cref="Conditions"/>, the row-1 GUIDs that must all hold, none for always: its base's
/// condition, then for a herc, flyer or structure its <see cref="Group"/>'s. <see cref="Picks"/> are the picks its keyed base
/// records make, each naming its table and the option shown.
/// </summary>
/// <param name="Group">The row-16 GUID of the first group, in file order, whose base names the thing as a member, which places it; <c>-1</c> for none or another row.</param>
public sealed record LayoutPresence(IReadOnlyList<short> Conditions, short Group, IReadOnlyList<LayoutPick> Picks) {
	/// <summary>Whether the thing is present whatever the campaign state.</summary>
	public bool Always => Conditions.Count == 0;
}

/// <summary>
/// A keyed record's pick: the type-3 row-1 record whose key it names (the first in row 1), and that table's options in
/// row-1 order. The layout shows <see cref="Shown"/>, the first, as a mission opens with every draw at its lowest.
/// </summary>
/// <param name="User">The record that picks.</param>
/// <param name="Key">Its random table key.</param>
/// <param name="Table">The table's GUID, <c>-1</c> when no type-3 record carries the key.</param>
public sealed record LayoutPick(MsnRecordId User, short Key, short Table, IReadOnlyList<LayoutOption> Options) {
	/// <summary>The option the layout shows, the first; null when the table has none.</summary>
	public LayoutOption? Shown => Options.Count > 0 ? Options[0] : null;
}

/// <summary>
/// One option of a table, as a record picking from it sees: the type-2 range over the table, and the first record of the
/// user's row read before it that the range conditions, the template a pick of it copies; null when there is none.
/// </summary>
public sealed record LayoutOption(short Range, MsnRecordId? Record);

/// <summary>A row-17 objective, by its index in the row: its fields (a copy) and when it is present, its own condition.</summary>
public sealed record LayoutObjective(int Index, MissionObjective58 Fields, LayoutPresence Presence);

/// <summary>A row-1 GUID: every record carrying it, in file order, and the first's fields (a copy).</summary>
public sealed record LayoutCondition(short Guid, IReadOnlyList<MsnRecordId> Records, MissionCondition14 Fields);

/// <summary>
/// A text line, by its <c>.ENG</c> id: the text of the last of its base records, those carrying the first record's
/// condition (a later record of the id replaces the text), which <see cref="Supplier"/> names; each record under another
/// condition is a variation.
/// </summary>
/// <param name="Records">Every <c>.ENG</c> record of the id, by index, in file order.</param>
/// <param name="Supplier">The <c>.ENG</c> record whose text <see cref="Text"/> is.</param>
public sealed record LayoutLine(short Id, IReadOnlyList<int> Records, string Text, int Supplier, LayoutPresence Presence,
	IReadOnlyList<LayoutTextVariation> Variations);

/// <summary>A <c>.ENG</c> record of a line's id under a condition other than its base's, and the text it gives the line then.</summary>
public sealed record LayoutTextVariation(int Record, short Condition, string Text);

/// <summary>
/// Row 2's records as the one header they patch (docs/retail/formats/msn-mission-file.md#the-header-patch--row-2). Its
/// <see cref="Words"/> are its <b>base</b>: the records carrying the same condition as the first, applied in file order over
/// each word's unset value (<see cref="MissionSettingsPatch.Unset"/>), each record writing every word it does not leave at
/// that value, as the load does. Every record under any other condition is a <see cref="LayoutHeaderVariation"/>. A launch
/// that writes over a word (<see cref="MissionGeneration"/>) is not a record and is not shown here.
/// </summary>
public sealed class LayoutHeader {
	private readonly MsnRecordId?[] _setBy;

	internal LayoutHeader(IReadOnlyList<MsnRecordId> records, IReadOnlyList<MsnRecordId> baseRecords, short[] words,
			MsnRecordId?[] setBy, LayoutPresence presence, IReadOnlyList<LayoutHeaderVariation> variations) {
		Records = records;
		Base = baseRecords;
		Words = words;
		_setBy = setBy;
		Presence = presence;
		Variations = variations;
	}

	/// <summary>Every row-2 record, in file order.</summary>
	public IReadOnlyList<MsnRecordId> Records { get; }

	/// <summary>The base's records, in file order: those of <see cref="Records"/> carrying the first one's condition.</summary>
	public IReadOnlyList<MsnRecordId> Base { get; }

	/// <summary>The ten header words as the base patches them (<c>script.dat</c> offset <c>word * 2</c>).</summary>
	public IReadOnlyList<short> Words { get; }

	/// <summary>When the base applies: the first record's condition.</summary>
	public LayoutPresence Presence { get; }

	/// <summary>Each row-2 record under a condition other than the base's, in file order.</summary>
	public IReadOnlyList<LayoutHeaderVariation> Variations { get; }

	/// <summary>
	/// The record whose value header word <paramref name="word"/> of <see cref="Words"/> shows: the last base record that sets
	/// it, else the first record.
	/// </summary>
	public MsnRecordId Supplier(int word) {
		if (word < 0 || word >= _setBy.Length) {
			throw new ArgumentOutOfRangeException(nameof(word), word, $"The header has {_setBy.Length} words.");
		}

		return _setBy[word] ?? Records[0];
	}
}

/// <summary>A row-2 record under a condition other than the header's base's, and the header words it sets then.</summary>
public sealed record LayoutHeaderVariation(MsnRecordId Record, short Condition, IReadOnlyList<int> Words);
