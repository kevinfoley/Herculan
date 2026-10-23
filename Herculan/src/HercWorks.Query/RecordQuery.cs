using System.Reflection;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Query;

/// <summary>One <c>.MSN</c> row as <see cref="MissionFile"/> holds it.</summary>
/// <param name="Row">1 to 17.</param>
/// <param name="Name">The <see cref="MissionFile"/> property holding it.</param>
/// <param name="Records">
/// The row's records in file order: the typed record itself, a <see cref="SettingsPatchRecord"/> for row 2, a
/// <see cref="DebriefRecord"/> for row 5, null for the row-17 record the file ends inside.
/// </param>
/// <param name="TrailingBytes">Bytes left over: the truncated row-17 record's, or row 5's past its last whole record.</param>
internal sealed record RawRow(int Row, string Name, IReadOnlyList<object?> Records, int TrailingBytes);

internal sealed record RecordQueryMission(string Mission, IReadOnlyList<RawRow> Rows);

/// <summary>One of the ten header words, by its <c>script.dat</c> header offset.</summary>
/// <param name="Field">The <see cref="ScriptDat"/> property over the offset, null for the two it names none for.</param>
internal sealed record HeaderWord(int Offset, string? Field, short Value);

/// <summary>A row-2 record: <see cref="MissionSettingsPatch.Data"/> split into its condition, header words and clear list.</summary>
internal sealed record SettingsPatchRecord(short Condition, IReadOnlyList<HeaderWord> Header, short[] ClearList);

/// <summary>A row-5 record, 32 words as <see cref="MissionGenerator.LoadDebriefText"/> reads it.</summary>
/// <param name="TextRefs">Thirty <c>.ENG</c> ids.</param>
/// <param name="VariantRef">A row-3 GUID, the debrief movie.</param>
internal sealed record DebriefRecord(short Condition, short[] TextRefs, short VariantRef);

/// <summary>Row 2 as authored, beside the header the mission's load writes.</summary>
internal sealed record HeaderQueryMission(string Mission, IReadOnlyList<SettingsPatchRecord> Patches, IReadOnlyList<HeaderWord> Generated);

/// <summary>
/// <c>records</c> and <c>header</c>: a mission's <c>.MSN</c> rows as <see cref="MissionFile"/> parsed them, no condition
/// applied and no variant drawn, and row 2 beside the header the load generates from it.
/// </summary>
internal static class RecordQuery {
	public const int RowCount = 17;

	/// <summary>Row 5's record size in words: a condition, thirty text refs, a row-3 ref.</summary>
	private const int DebriefWords = 32;

	private const int WrapColumn = 118;

	/// <summary>The ten header words' <see cref="ScriptDat"/> names, by offset / 2.</summary>
	private static readonly string?[] HeaderFields = [
		nameof(ScriptDat.TheaterIndex), nameof(ScriptDat.ZoneIndex), null, nameof(ScriptDat.ObjectiveType),
		nameof(ScriptDat.TrainingMissionNumber), nameof(ScriptDat.UnlimitedAmmunition), nameof(ScriptDat.PlayerInvulnerable),
		nameof(ScriptDat.Difficulty), null, nameof(ScriptDat.TheaterVariant),
	];

	public static RecordQueryMission Records(Mission mission, int? row) {
		var file = mission.File;
		var rows = new List<RawRow>();
		void Add(int number, string name, IEnumerable<object?>? records, int trailing = 0) {
			if (row == null || row == number) {
				rows.Add(new RawRow(number, name, records?.ToList() ?? [], trailing));
			}
		}

		byte[] debrief = file.DebriefBytes ?? [];
		Add(1, nameof(file.Conditions), file.Conditions);
		Add(2, nameof(file.SettingsPatches), file.SettingsPatches?.Select(Patch));
		Add(3, nameof(file.Variants), file.Variants);
		Add(4, nameof(file.Texts), file.Texts);
		Add(5, "Debrief", Debrief(debrief), debrief.Length % (DebriefWords * 2));
		Add(6, nameof(file.Points), file.Points);
		Add(7, nameof(file.Headings), file.Headings);
		Add(8, nameof(file.WaypointGroups), file.WaypointGroups);
		Add(9, nameof(file.TriggerAreas), file.TriggerAreas);
		Add(10, nameof(file.Actions), file.Actions);
		Add(11, nameof(file.ActionTimers), file.ActionTimers);
		Add(12, nameof(file.Mechs), file.Mechs);
		Add(13, nameof(file.Flyers), file.Flyers);
		Add(14, nameof(file.Bases), file.Bases);
		Add(15, nameof(file.Orders), file.Orders);
		Add(16, nameof(file.Groups), file.Groups);
		Add(17, nameof(file.Objectives), file.Objectives, file.TruncatedRow17Tail?.Length ?? 0);
		return new RecordQueryMission(mission.Name, rows);
	}

	/// <summary>Row 2, and the header <see cref="MissionGenerator"/> writes with every campaign flag zero and every roll 0, as <c>lint --generated</c> generates it.</summary>
	public static HeaderQueryMission Header(Mission mission) {
		var generator = MissionGenerator.Load(mission.MsnBytes, mission.EngBytes,
			new short[MissionGenerator.CampaignFlagCount], new short[MissionGenerator.ClearListLength], _ => 0);
		byte[] script = generator.WriteScriptDat();
		var header = Enumerable.Range(0, HeaderFields.Length)
			.Select(word => new HeaderWord(word * 2, HeaderFields[word], BitConverter.ToInt16(script, word * 2)))
			.ToList();
		return new HeaderQueryMission(mission.Name, (mission.File.SettingsPatches ?? []).Select(Patch).ToList(), header);
	}

	private static SettingsPatchRecord Patch(MissionSettingsPatch patch) => new(
		patch.Data[0],
		Enumerable.Range(0, HeaderFields.Length)
			.Select(word => new HeaderWord(word * 2, HeaderFields[word], patch.Data[MissionSettingsPatch.HeaderWordsStart + word]))
			.ToList(),
		patch.Data[MissionSettingsPatch.ClearListStart..]);

	private static IEnumerable<object?> Debrief(byte[] bytes) {
		int size = DebriefWords * 2;
		for (int offset = 0; offset + size <= bytes.Length; offset += size) {
			var words = new short[DebriefWords];
			Buffer.BlockCopy(bytes, offset, words, 0, size);
			yield return new DebriefRecord(words[0], words[1..31], words[31]);
		}
	}

	public static void WriteRecords(TextWriter o, RecordQueryMission mission) {
		foreach (var row in mission.Rows) {
			o.WriteLine($"{mission.Mission}  row {row.Row} {row.Name}: {row.Records.Count} record(s)");
			for (int i = 0; i < row.Records.Count; i++) {
				if (row.Records[i] is not { } record) {
					o.WriteLine($"  #{i} (the file ends inside this record)");
					continue;
				}

				WriteWrapped(o, $"  #{i}", record switch {
					SettingsPatchRecord patch => PatchFields(patch),
					DebriefRecord d => [$"Condition={d.Condition}", $"TextRefs={Compact(d.TextRefs)}", $"VariantRef={d.VariantRef}"],
					_ => Fields(record),
				});
			}

			if (row.TrailingBytes > 0) {
				o.WriteLine($"  {row.TrailingBytes} trailing byte(s)");
			}

			o.WriteLine();
		}
	}

	public static void WriteHeader(TextWriter o, HeaderQueryMission mission) {
		o.WriteLine($"{mission.Mission}  row 2 SettingsPatches, raw: {mission.Patches.Count} record(s)");
		for (int i = 0; i < mission.Patches.Count; i++) {
			WriteWrapped(o, $"  #{i}", PatchFields(mission.Patches[i]));
		}

		o.WriteLine($"{mission.Mission}  script.dat header, generated with every flag 0 and every roll 0");
		WriteWrapped(o, " ", mission.Generated.Select(Word));
	}

	private static IEnumerable<string> PatchFields(SettingsPatchRecord patch) =>
		[$"Condition={patch.Condition}", .. patch.Header.Select(Word), $"ClearList={Compact(patch.ClearList)}"];

	private static string Word(HeaderWord word) => $"+{word.Offset}{(word.Field != null ? " " + word.Field : "")}={word.Value}";

	/// <summary>
	/// A typed record's public properties as <c>Name=value</c>: <see cref="MapObject.GUID"/> first, then the record's own
	/// in declaration order.
	/// </summary>
	private static IEnumerable<string> Fields(object record) =>
		Properties(record.GetType()).Select(p => $"{p.Name}={Value(p.GetValue(record))}");

	/// <summary>A record type's public properties in the order <c>records</c> writes them.</summary>
	public static IEnumerable<PropertyInfo> Properties(Type recordType) =>
		recordType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.OrderBy(p => p.DeclaringType != typeof(MapObject))
			.ThenBy(p => p.MetadataToken);

	/// <summary>A field's value as <c>records</c> writes it: an array <see cref="Compact(IEnumerable{string})"/>, a counter pair <c>ref:op</c>.</summary>
	public static string Value(object? value) => value switch {
		null => "null",
		short[] words => Compact(words.Select(w => w.ToString())),
		CounterPair[] pairs => Compact(pairs.Select(Value)),
		CounterPair pair => $"{pair.CounterRef}:{pair.Op}",
		_ => value.ToString() ?? "",
	};

	private static string Compact(short[] words) => Compact(words.Select(w => w.ToString()));

	/// <summary>A list in brackets, a run of three or more equal values written once as <c>value*count</c>.</summary>
	private static string Compact(IEnumerable<string> values) {
		var items = values.ToList();
		var parts = new List<string>();
		for (int i = 0; i < items.Count;) {
			int run = 1;
			while (i + run < items.Count && items[i + run] == items[i]) {
				run++;
			}

			if (run >= 3) {
				parts.Add($"{items[i]}*{run}");
			} else {
				parts.AddRange(items.Skip(i).Take(run));
			}

			i += run;
		}

		return $"[{string.Join(",", parts)}]";
	}

	/// <summary>Writes <paramref name="fields"/> after <paramref name="lead"/>, wrapping before <see cref="WrapColumn"/>.</summary>
	private static void WriteWrapped(TextWriter o, string lead, IEnumerable<string> fields) {
		var line = new System.Text.StringBuilder(lead);
		foreach (string field in fields) {
			if (line.Length + 1 + field.Length > WrapColumn && line.Length > lead.Length) {
				o.WriteLine(line.ToString());
				line.Clear().Append(' ', lead.Length);
			}

			line.Append(' ').Append(field);
		}

		o.WriteLine(line.ToString());
	}
}
