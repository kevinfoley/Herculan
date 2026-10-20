using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A read-only view of a <see cref="MissionFile"/> and its <c>.ENG</c> with every record present, conditional ones included:
/// one entry per thing a designer sees, keyed as the <c>.MSN</c> keys it. Points, headings, routes, trigger areas, actions,
/// timers, hercs, flyers, structures and ground vehicles, orders and groups are <see cref="LayoutEntry"/>s by row and GUID;
/// row 2's header patches are one <see cref="LayoutHeader"/>; objectives are by their index in row 17; conditions by row-1
/// GUID; text lines by <c>.ENG</c> id. Each field's supplier is the record an edit of it writes. Refs stay GUIDs, for a reader
/// to resolve through <see cref="Entry(int, short)"/>.
///
/// <para>It is built in one pass and holds copies, never the file's records. Unlike
/// <see cref="MissionGenerator"/>, the game's load, it tests no condition, draws nothing and renumbers nothing: a GUID's
/// records merge by condition (<see cref="LayoutEntry"/>), and a keyed record shows the first option of its table
/// (<see cref="LayoutDraw"/>), where it stands when the mission opens with every draw at its lowest. What it shares with the
/// load is the per-row merge and variant copy, <see cref="MergeRules"/>. A row-6 point whose sum flag is set keeps the
/// words it holds; no retail point sets it (docs/retail/formats/msn-mission-file.md#row-6-field-decode--the-point-record-dat_0047064e-22-bytesrecord).
/// The rules it follows are docs/herculan/handoff-editor-msn-scene.md#core-missionlayout.</para>
/// </summary>
public sealed class MissionLayout {
	private const int FirstGuidRow = 6;
	private const int LastGuidRow = 16;
	private const int RouteRow = 8;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;
	private const int ConditionRow = 1;
	private const int SettingsRow = 2;

	/// <summary>Row 1's types: a range over a draw or a table, and a variant table (docs/retail/formats/msn-mission-file.md#the-conditions--row-1).</summary>
	private const short RangeType = 2;
	private const short TableType = 3;

	private static readonly Dictionary<Type, int> RowOfType = new() {
		[typeof(MapPoint22)] = 6,
		[typeof(Heading10)] = 7,
		[typeof(WaypointGroup)] = 8,
		[typeof(TriggerArea12)] = 9,
		[typeof(MissionAction82)] = 10,
		[typeof(ActionTimer30)] = 11,
		[typeof(MechRosterEntry144)] = 12,
		[typeof(FlyerRosterEntry102)] = 13,
		[typeof(BaseRosterEntry62)] = 14,
		[typeof(MissionOrder22)] = 15,
		[typeof(MissionGroup164)] = 16,
	};

	private readonly Dictionary<int, List<LayoutEntry>> _rows = new();
	private readonly Dictionary<(int Row, short Guid), LayoutEntry> _entries = new();
	private readonly List<LayoutCondition> _conditions = new();
	private readonly Dictionary<short, LayoutCondition> _conditionOf = new();
	private readonly List<LayoutLine> _lines = new();
	private readonly Dictionary<short, LayoutLine> _lineOf = new();
	private readonly List<LayoutObjective> _objectives = new();

	private MissionLayout() {
	}

	/// <summary>Row 2's header patches as one header, or null when the row has no record.</summary>
	public LayoutHeader? Header { get; private set; }

	/// <summary>Row 17's objectives, by index; an entry the file ends inside of (<see cref="MissionFile.Objectives"/>) is left out.</summary>
	public IReadOnlyList<LayoutObjective> Objectives => _objectives;

	/// <summary>Every row-1 GUID but <c>-1</c>, in the order of its first record.</summary>
	public IReadOnlyList<LayoutCondition> Conditions => _conditions;

	/// <summary>Every <c>.ENG</c> id but <c>-1</c>, in the order of its first record; empty without a <c>.ENG</c>.</summary>
	public IReadOnlyList<LayoutLine> Lines => _lines;

	/// <summary>Row <paramref name="row"/>'s entries (6 to 16), in the order of each GUID's first record.</summary>
	public IReadOnlyList<LayoutEntry> Entries(int row) =>
		row is >= FirstGuidRow and <= LastGuidRow
			? _rows[row]
			: throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 6 to 16 are the rows keyed by GUID.");

	/// <summary>The entries of <typeparamref name="T"/>'s row, in the order of each GUID's first record.</summary>
	public IReadOnlyList<LayoutEntry<T>> Entries<T>() where T : MapObject =>
		Entries(RowOf<T>()).Cast<LayoutEntry<T>>().ToList();

	/// <summary>Row <paramref name="row"/>'s entry for <paramref name="guid"/>, or null when no record of the row carries it.</summary>
	public LayoutEntry? Entry(int row, short guid) => _entries.GetValueOrDefault((row, guid));

	/// <summary>The entry of <typeparamref name="T"/>'s row for <paramref name="guid"/>, or null when no record carries it.</summary>
	public LayoutEntry<T>? Entry<T>(short guid) where T : MapObject => (LayoutEntry<T>?)Entry(RowOf<T>(), guid);

	/// <summary>Row-1 GUID <paramref name="guid"/>, or null when no record carries it.</summary>
	public LayoutCondition? Condition(short guid) => _conditionOf.GetValueOrDefault(guid);

	/// <summary>The line of <c>.ENG</c> id <paramref name="id"/>, or null when no record carries it.</summary>
	public LayoutLine? Line(short id) => _lineOf.GetValueOrDefault(id);

	/// <summary>The layout of <paramref name="file"/>, with <paramref name="text"/>, its <c>.ENG</c>, for the lines.</summary>
	public static MissionLayout Build(MissionFile file, MissionStringFile? text = null) {
		var layout = new MissionLayout();
		var conditions = file.Conditions ?? [];
		var codec = new MissionFileTransformer();
		var rows = new Dictionary<int, List<Merged>>();
		for (int row = FirstGuidRow; row <= LastGuidRow; row++) {
			rows[row] = Merge(file, row, conditions, codec);
		}

		var claims = Claims(rows[GroupRow], codec);
		for (int row = FirstGuidRow; row <= LastGuidRow; row++) {
			var entries = new List<LayoutEntry>();
			foreach (var merged in rows[row]) {
				var entry = merged.ToEntry(codec, claims.TryGetValue((row, merged.Guid), out var group) ? group : null);
				entries.Add(entry);
				layout._entries[(row, merged.Guid)] = entry;
			}

			layout._rows[row] = entries;
		}

		layout.AddHeader(file);
		layout.AddObjectives(file);
		layout.AddConditions(conditions);
		layout.AddLines(text);
		return layout;
	}

	private static int RowOf<T>() where T : MapObject =>
		RowOfType.TryGetValue(typeof(T), out int row)
			? row
			: throw new ArgumentException($"A {typeof(T).Name} is not a record of rows 6 to 16.", nameof(T));

	// ---- Rows 6 to 16 ---------------------------------------------------------------------------

	/// <summary>
	/// Row <paramref name="row"/>'s GUIDs, each merged: every record read as words, a keyed record with its shown option's
	/// spans copied in (its earlier records first, so an option that draws itself is copied as it then stands), then each
	/// GUID's base merged in file order.
	/// </summary>
	private static List<Merged> Merge(MissionFile file, int row, MissionCondition14[] conditions, MissionFileTransformer codec) {
		var records = MsnRows.Find(file, row) is { } found ? found.Cast<MapObject>().ToArray() : [];
		var words = new short[records.Length][];
		var origins = new WordSource[records.Length][];
		var draws = new LayoutDraw?[records.Length];
		bool keyed = MergeRules.VariantCopy(row).Count > 0;
		for (int i = 0; i < records.Length; i++) {
			var id = new MsnRecordId(row, i);
			words[i] = codec.Words(records[i]);
			origins[i] = new WordSource[row == RouteRow ? MergeRules.RouteListWord + 1 : words[i].Length];
			Array.Fill(origins[i], new WordSource(id, null));
			short key = keyed ? words[i][MergeRules.VariantKeyWord] : (short)-1;
			if (key == -1) {
				continue;
			}

			draws[i] = Draw(id, key, conditions, words);
			if (draws[i]!.Shown?.Record is { } option) {
				CopyOption(row, words, origins, option.Index, i);
			}
		}

		var merged = new List<Merged>();
		var byGuid = new Dictionary<short, Merged>();
		for (int i = 0; i < records.Length; i++) {
			short guid = records[i].GUID;
			if (guid == -1) {
				continue;
			}

			if (!byGuid.TryGetValue(guid, out var entry)) {
				byGuid[guid] = entry = new Merged(row, guid, words[i][MergeRules.ConditionWord], words[i], origins[i]);
				merged.Add(entry);
			}

			entry.Add(i, words[i], origins[i], draws[i]);
		}

		return merged;
	}

	/// <summary>Copies the <see cref="MergeRules.VariantCopy"/> spans of record <paramref name="source"/> into record <paramref name="target"/>, as the load copies a drawn option.</summary>
	private static void CopyOption(int row, short[][] words, WordSource[][] origins, int source, int target) {
		var id = new MsnRecordId(row, target);
		foreach (var span in MergeRules.VariantCopy(row)) {
			if (row == RouteRow) {
				words[target] = [.. words[target][..span.First], .. words[source][span.First..]];
			} else {
				Array.Copy(words[source], span.First, words[target], span.First, span.Count);
			}

			for (int word = span.First; word < span.First + span.Count; word++) {
				var origin = origins[source][word];
				origins[target][word] = new WordSource(id, origin.CopiedFrom ?? origin.Record);
			}
		}
	}

	/// <summary>
	/// What record <paramref name="drawer"/> draws by <paramref name="key"/>: the first type-3 row-1 record of the key, and
	/// each range over it in row-1 order with the first record of the drawer's row read before it that the range conditions.
	/// </summary>
	private static LayoutDraw Draw(MsnRecordId drawer, short key, MissionCondition14[] conditions, short[][] row) {
		var table = conditions.FirstOrDefault(record => record.Type == TableType && record.FlagIndexOrRangeLower == key);
		if (table == null) {
			return new LayoutDraw(drawer, key, -1, []);
		}

		var options = new List<LayoutOption>();
		foreach (var range in conditions.Where(record => record.Type == RangeType && record.ConditionRef == table.GUID)) {
			MsnRecordId? record = null;
			for (int i = 0; i < drawer.Index; i++) {
				if (row[i][MergeRules.ConditionWord] == range.GUID) {
					record = new MsnRecordId(drawer.Row, i);
					break;
				}
			}

			options.Add(new LayoutOption(range.GUID, record));
		}

		return new LayoutDraw(drawer, key, table.GUID, options);
	}

	/// <summary>
	/// Which group places each roster record: the first group, in the order of its first record, whose base names it in a
	/// member slot of its member kind (0 hercs, 1 flyers, 2 structures), with that group's base condition.
	/// </summary>
	private static Dictionary<(int Row, short Guid), (short Group, short Condition)> Claims(List<Merged> groups,
			MissionFileTransformer codec) {
		var claims = new Dictionary<(int Row, short Guid), (short Group, short Condition)>();
		foreach (var merged in groups) {
			var group = (MissionGroup164)codec.Record(GroupRow, merged.Words);
			int? row = group.MemberKind switch {
				0 => 12,
				1 => 13,
				2 => 14,
				_ => null,
			};

			if (row == null) {
				continue;
			}

			foreach (short member in group.MemberRefs) {
				if (member != -1) {
					claims.TryAdd((row.Value, member), (group.GUID, merged.Condition));
				}
			}
		}

		return claims;
	}

	// ---- Rows 2, 17 and 1, and the .ENG ---------------------------------------------------------

	private void AddHeader(MissionFile file) {
		var patches = file.SettingsPatches ?? [];
		if (patches.Length == 0) {
			return;
		}

		short condition = patches[0].Data[0];
		var records = new List<MsnRecordId>();
		var baseRecords = new List<MsnRecordId>();
		var variations = new List<LayoutHeaderVariation>();
		var words = new short[MissionGeneration.HeaderWords];
		var setBy = new MsnRecordId?[MissionGeneration.HeaderWords];
		for (int word = 0; word < words.Length; word++) {
			words[word] = MissionSettingsPatch.Unset(word);
		}

		for (int i = 0; i < patches.Length; i++) {
			var id = new MsnRecordId(SettingsRow, i);
			var data = patches[i].Data;
			var sets = Enumerable.Range(0, words.Length)
				.Where(word => data[MissionSettingsPatch.HeaderWordsStart + word] != MissionSettingsPatch.Unset(word)).ToList();
			records.Add(id);
			if (data[0] != condition) {
				variations.Add(new LayoutHeaderVariation(id, data[0], sets));
				continue;
			}

			baseRecords.Add(id);
			foreach (int word in sets) {
				words[word] = data[MissionSettingsPatch.HeaderWordsStart + word];
				setBy[word] = id;
			}
		}

		Header = new LayoutHeader(records, baseRecords, words, setBy, Presence(condition, null, []), variations);
	}

	private void AddObjectives(MissionFile file) {
		var objectives = file.Objectives ?? [];
		for (int i = 0; i < objectives.Length; i++) {
			if (objectives[i] is { } objective) {
				_objectives.Add(new LayoutObjective(i, (MissionObjective58)MsnRows.CopyRecord(objective),
					Presence(objective.ConditionRef, null, [])));
			}
		}
	}

	private void AddConditions(MissionCondition14[] conditions) {
		var records = new Dictionary<short, List<MsnRecordId>>();
		var order = new List<short>();
		for (int i = 0; i < conditions.Length; i++) {
			short guid = conditions[i].GUID;
			if (guid == -1) {
				continue;
			}

			if (!records.TryGetValue(guid, out var list)) {
				records[guid] = list = new();
				order.Add(guid);
			}

			list.Add(new MsnRecordId(ConditionRow, i));
		}

		foreach (short guid in order) {
			var condition = new LayoutCondition(guid, records[guid],
				(MissionCondition14)MsnRows.CopyRecord(conditions[records[guid][0].Index]));
			_conditions.Add(condition);
			_conditionOf[guid] = condition;
		}
	}

	private void AddLines(MissionStringFile? text) {
		var strings = text?.Strings ?? [];
		var records = new Dictionary<short, List<int>>();
		var order = new List<short>();
		for (int i = 0; i < strings.Length; i++) {
			short id = strings[i].Guid;
			if (id == -1) {
				continue;
			}

			if (!records.TryGetValue(id, out var list)) {
				records[id] = list = new();
				order.Add(id);
			}

			list.Add(i);
		}

		foreach (short id in order) {
			var indices = records[id];
			short condition = strings[indices[0]].ConditionRef;
			int supplier = indices.Last(i => strings[i].ConditionRef == condition);
			var variations = indices.Where(i => strings[i].ConditionRef != condition)
				.Select(i => new LayoutTextVariation(i, strings[i].ConditionRef, strings[i].Val ?? "")).ToList();
			var line = new LayoutLine(id, indices, strings[supplier].Val ?? "", supplier, Presence(condition, null, []), variations);
			_lines.Add(line);
			_lineOf[id] = line;
		}
	}

	/// <summary>A presence of <paramref name="condition"/> and, when one places it, <paramref name="group"/>'s; <c>-1</c> conditions are left out.</summary>
	private static LayoutPresence Presence(short condition, (short Group, short Condition)? group, IReadOnlyList<LayoutDraw> draws) {
		var conditions = new List<short>();
		if (condition != -1) {
			conditions.Add(condition);
		}

		if (group is { Condition: not -1 } placing && !conditions.Contains(placing.Condition)) {
			conditions.Add(placing.Condition);
		}

		return new LayoutPresence(conditions, group?.Group ?? -1, draws);
	}

	/// <summary>One GUID of a row as it is merged: its base so far, its variations, and the draws its base records make.</summary>
	private sealed class Merged {
		private readonly List<MsnRecordId> _records = new();
		private readonly List<MsnRecordId> _base = new();
		private readonly List<(int Index, short[] Words, LayoutDraw? Draw)> _variations = new();
		private readonly List<LayoutDraw> _draws = new();
		private WordSource[] _suppliers;

		public Merged(int row, short guid, short condition, short[] words, WordSource[] origins) {
			Row = row;
			Guid = guid;
			Condition = condition;
			Words = (short[])words.Clone();
			_suppliers = (WordSource[])origins.Clone();
		}

		public int Row { get; }

		public short Guid { get; }

		/// <summary>The condition of the GUID's first record, which its base records carry.</summary>
		public short Condition { get; }

		/// <summary>The base's words as merged so far.</summary>
		public short[] Words { get; private set; }

		/// <summary>Adds the GUID's record <paramref name="index"/>: merged into the base when it carries the base's condition, else a variation.</summary>
		public void Add(int index, short[] words, WordSource[] origins, LayoutDraw? draw) {
			var id = new MsnRecordId(Row, index);
			_records.Add(id);
			if (words[MergeRules.ConditionWord] != Condition) {
				_variations.Add((index, words, draw));
				return;
			}

			if (draw != null) {
				_draws.Add(draw);
			}

			if (_base.Count > 0) {
				if (MergeRules.Replaces(Row)) {
					Words = (short[])words.Clone();
					_suppliers = (WordSource[])origins.Clone();
				} else {
					foreach (var span in MergeRules.Overlay(Row)) {
						for (int word = span.First; word < span.First + span.Count; word++) {
							if (words[word] != span.Unset) {
								Words[word] = words[word];
								_suppliers[word] = origins[word];
							}
						}
					}
				}
			}

			_base.Add(id);
		}

		public LayoutEntry ToEntry(MissionFileTransformer codec, (short Group, short Condition)? group) {
			var variations = _variations.Select(variation => new LayoutVariation(new MsnRecordId(Row, variation.Index),
				variation.Words[MergeRules.ConditionWord], codec.Record(Row, variation.Words), SetWords(variation.Words),
				variation.Draw)).ToList();
			var presence = Presence(Condition, group, _draws);
			var fields = codec.Record(Row, Words);
			return fields switch {
				MapPoint22 point => new LayoutEntry<MapPoint22>(Row, Guid, _records, _base, point, _suppliers, presence, variations),
				Heading10 heading => new LayoutEntry<Heading10>(Row, Guid, _records, _base, heading, _suppliers, presence, variations),
				WaypointGroup route => new LayoutEntry<WaypointGroup>(Row, Guid, _records, _base, route, _suppliers, presence, variations),
				TriggerArea12 area => new LayoutEntry<TriggerArea12>(Row, Guid, _records, _base, area, _suppliers, presence, variations),
				MissionAction82 action => new LayoutEntry<MissionAction82>(Row, Guid, _records, _base, action, _suppliers, presence, variations),
				ActionTimer30 timer => new LayoutEntry<ActionTimer30>(Row, Guid, _records, _base, timer, _suppliers, presence, variations),
				MechRosterEntry144 herc => new LayoutEntry<MechRosterEntry144>(Row, Guid, _records, _base, herc, _suppliers, presence, variations),
				FlyerRosterEntry102 flyer => new LayoutEntry<FlyerRosterEntry102>(Row, Guid, _records, _base, flyer, _suppliers, presence, variations),
				BaseRosterEntry62 structure => new LayoutEntry<BaseRosterEntry62>(Row, Guid, _records, _base, structure, _suppliers, presence, variations),
				MissionOrder22 order => new LayoutEntry<MissionOrder22>(Row, Guid, _records, _base, order, _suppliers, presence, variations),
				MissionGroup164 grouped => new LayoutEntry<MissionGroup164>(Row, Guid, _records, _base, grouped, _suppliers, presence, variations),
				_ => throw new InvalidOperationException($"Row {Row} is not keyed by GUID."),
			};
		}

		/// <summary>The words a variation sets over the base: every word of a row that replaces; each overlay word not at its unset value.</summary>
		private IReadOnlyList<int> SetWords(short[] words) =>
			MergeRules.Replaces(Row)
				? Enumerable.Range(0, Row == RouteRow ? MergeRules.RouteListWord + 1 : words.Length).ToList()
				: MergeRules.Overlay(Row)
					.SelectMany(span => Enumerable.Range(span.First, span.Count).Where(word => words[word] != span.Unset))
					.ToList();
	}
}
