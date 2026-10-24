using System.Collections;
using System.Globalization;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;

namespace HercWorks.Query;

/// <summary>A <see cref="HercSimDat"/> member and the record bytes the reader takes it from.</summary>
/// <param name="Offset">Its first record offset.</param>
/// <param name="Length">How many bytes it spans; null for a list that runs to its first negative byte.</param>
/// <param name="Type"><c>short</c>, <c>short[12]</c>, <c>byte[20]</c>, or <c>byte[]</c> for such a list.</param>
internal sealed record ChassisField(string Name, int Offset, int? Length, string Type) {
	public int TypeRecordOffset => Offset + ChassisQuery.TypeRecordBase;

	public bool Contains(int offset) => Length is { } length && offset >= Offset && offset < Offset + length;
}

/// <summary>One chassis' value of the queried field.</summary>
/// <param name="Value">
/// A number, a list of numbers, or a text field's text; null when the install has no record for the
/// chassis or the record is too short.
/// </param>
/// <param name="Hex">The same in hex, null for text.</param>
internal sealed record ChassisValue(int Index, string Name, object? Value, string? Hex);

/// <param name="Field">The member the query names, null for an offset that does not start one.</param>
/// <param name="Located">Where the offset falls, for an offset that does not start a member: inside which, or in none.</param>
/// <param name="AlsoStarting">Other members that start at the offset — the overlapping per-entry views.</param>
internal sealed record ChassisQueryResult(
	int Offset, int TypeRecordOffset, string Type, ChassisField? Field, string? Located,
	IReadOnlyList<string> AlsoStarting, IReadOnlyList<ChassisValue> Values);

/// <summary>A game's chassis record model, which the query reflects over, and the HercWorks.Core reader that fills it.</summary>
internal sealed record ChassisModel(Type Type, Func<byte[], object?> Parse) {
	/// <summary>ES2's 216-byte record, read by <see cref="HercSimDataTransformer"/>.</summary>
	public static readonly ChassisModel Es2 = new(typeof(HercSimDat), b => new HercSimDataTransformer().Parse(b));

	/// <summary>ES1's 192-byte record, read by <see cref="Es1HercSimDataTransformer"/>.</summary>
	public static readonly ChassisModel Es1 = new(typeof(Es1HercSimDat), b => new Es1HercSimDataTransformer().Parse(b));
}

/// <summary>
/// <c>chassis</c>: one field of every chassis' <c>dat\&lt;name&gt;.DAT</c> type record, as the install's
/// <see cref="ChassisModel"/> reads it.
///
/// <para>Which member sits at which record offset is not tabled here: <see cref="Fields"/> finds it with
/// <see cref="RecordLayout.Probe"/>, so it follows the reader.</para>
/// </summary>
internal static class ChassisQuery {
	/// <summary>
	/// Record offset N is the exe's type-record offset N+2 in both games: each simulator loads the record at its type
	/// record's <c>+2</c> (<see cref="HercSimDat"/>, <see cref="Es1HercSimDat"/>).
	/// </summary>
	public const int TypeRecordBase = 2;

	/// <summary>The reader's members, in record order, as found over a record of <paramref name="recordLength"/> bytes.</summary>
	public static IReadOnlyList<ChassisField> Fields(ChassisModel model, int recordLength) {
		var zero = model.Parse(new byte[recordLength])!;
		var full = model.Parse(Enumerable.Repeat((byte)0xff, recordLength).ToArray())!;
		return RecordLayout.Probe(model.Type, model.Parse, recordLength)
			.Select(member => {
				var (p, offsets) = member;
				// A list whose length a byte's sign decides parses to different lengths from all-zero and all-0xff bytes.
				bool variable = p.GetValue(zero) is IList a && p.GetValue(full) is IList b && a.Count != b.Count;
				string type = p.GetValue(zero) switch {
					short => "short",
					_ when variable => "byte[]",
					IList list => $"{p.PropertyType.GetElementType()!.Name switch { "Int16" => "short", _ => "byte" }}[{list.Count}]",
					_ => p.PropertyType.Name,
				};
				return new ChassisField(p.Name, offsets[0], variable ? null : offsets.Count, type);
			})
			.OrderBy(f => f.Offset)
			.ThenBy(f => f.Length == null)
			.ToList();
	}

	/// <summary>
	/// The field <paramref name="spec"/> names: a member name, matched whole and then as a prefix, ignoring
	/// case, or a decimal or <c>0x</c> hex record offset. Sets <paramref name="error"/> when it names none,
	/// or several.
	/// </summary>
	public static int? ResolveOffset(IReadOnlyList<ChassisField> fields, string spec, int recordLength, out ChassisField? named, out string? error) {
		named = null;
		error = null;
		if (RecordLayout.TryParseNumber(spec, out int offset)) {
			if (offset < 0 || offset + 1 >= recordLength) {
				error = $"A record offset is 0-{recordLength - 2}; the record is {recordLength} bytes.";
				return null;
			}

			return offset;
		}

		named = RecordLayout.MatchName(fields, f => f.Name, spec, $"No chassis field is named {spec}; `chassis` with no --field lists them.", out error);
		return named?.Offset;
	}

	/// <summary>
	/// Every chassis' value at <paramref name="offset"/>: the member's, through the reader, when a member
	/// starts there (<paramref name="named"/> picks it when several do); otherwise the little-endian word
	/// the record holds there.
	/// </summary>
	public static ChassisQueryResult Run(ChassisModel model, IReadOnlyList<Chassis> chassis, IReadOnlyList<ChassisField> fields, int offset, ChassisField? named) {
		var field = named ?? fields.FirstOrDefault(f => f.Offset == offset && f.Length != null) ?? fields.FirstOrDefault(f => f.Offset == offset);
		var also = fields.Where(f => f.Offset == offset && f != field).Select(f => f.Name).ToList();
		if (field == null) {
			var inside = fields.Where(f => f.Contains(offset)).Select(f => Inside(f, offset)).ToList();
			string located = inside.Count == 0 ? "in no member" : string.Join("; ", inside);
			var words = chassis.Select(c => c.Bytes is { } bytes && offset + 1 < bytes.Length
				? Number(c, (short)(bytes[offset] | bytes[offset + 1] << 8))
				: new ChassisValue(c.Index, c.Name, null, null)).ToList();
			return new ChassisQueryResult(offset, offset + TypeRecordBase, "short", null, located, also, words);
		}

		var property = model.Type.GetProperty(field.Name)!;
		bool text = IsText(chassis.Select(c => c.Record == null ? null : property.GetValue(c.Record)).ToList());
		var values = chassis.Select(c => c.Record == null ? new ChassisValue(c.Index, c.Name, null, null) : property.GetValue(c.Record) switch {
			short s => Number(c, s),
			byte[] b when text => new ChassisValue(c.Index, c.Name, System.Text.Encoding.ASCII.GetString(b).TrimEnd('\0'), null),
			IList list => new ChassisValue(c.Index, c.Name,
				list.Cast<object>().Select(Convert.ToInt32).ToArray(),
				string.Join(" ", list.Cast<object>().Select(v => v is short s ? $"0x{(ushort)s:x}" : $"0x{v:x2}"))),
			var other => new ChassisValue(c.Index, c.Name, other, null),
		}).ToList();
		return new ChassisQueryResult(offset, offset + TypeRecordBase, field.Type, field, null, also, values);
	}

	/// <summary>Where <paramref name="offset"/> falls inside <paramref name="field"/>, which it does not start.</summary>
	private static string Inside(ChassisField field, int offset) {
		string span = $"{field.Name} (record offsets {field.Offset}-{field.Offset + field.Length - 1})";
		int into = offset - field.Offset;
		return field.Type switch {
			"short" => $"byte {into} of {span}",
			_ when field.Type.StartsWith("short[") => $"{(into % 2 == 0 ? "" : "byte 1 of ")}element {into / 2} of {span}",
			_ => $"element {into} of {span}",
		};
	}

	private static ChassisValue Number(Chassis chassis, short value) => new(chassis.Index, chassis.Name, value, $"0x{(ushort)value:x}");

	/// <summary>A byte field that holds NUL-padded printable ASCII on every chassis, and something on at least one.</summary>
	private static bool IsText(IReadOnlyList<object?> values) {
		var arrays = values.OfType<byte[]>().ToList();
		return arrays.Count > 0 && arrays.Count == values.Count(v => v != null)
			&& arrays.Any(b => b.Length > 0 && b[0] != 0)
			&& arrays.All(b => b.SkipWhile(x => x is >= 0x20 and < 0x7f).All(x => x == 0));
	}

	/// <summary>The record's members and the bytes no member covers, in record order.</summary>
	public static void WriteFields(TextWriter output, IReadOnlyList<ChassisField> fields, int recordLength) {
		output.WriteLine($"{"Record",6}  {"Hex",-5} {"Type record",11}  {"Field",-26}Type");
		var rows = fields.Select(f => (f.Offset, Line: Row(f.Offset, f.Name, f.Type))).ToList();
		for (int k = 0; k < recordLength;) {
			if (fields.Any(f => f.Contains(k))) {
				k++;
				continue;
			}

			int start = k;
			while (k < recordLength && !fields.Any(f => f.Contains(k))) {
				k++;
			}

			rows.Add((start, Row(start, "(no member)", $"{k - start} bytes")));
		}

		foreach (var row in rows.OrderBy(r => r.Offset)) {
			output.WriteLine(row.Line);
		}

		output.WriteLine($"Record offset N is the exe's type-record offset N+{TypeRecordBase}."
			+ (fields.Any(f => f.Length == null) ? " A byte[] runs to its first negative byte." : ""));
	}

	private static string Row(int offset, string name, string type) =>
		$"{offset,6}  0x{offset:x2}  {"+0x" + (offset + TypeRecordBase).ToString("x2"),11}  {name,-26}{type}";

	public static void Write(TextWriter output, ChassisQueryResult result) {
		string where = $"record offset {result.Offset} (0x{result.Offset:x2}), type record +0x{result.TypeRecordOffset:x2}";
		output.WriteLine(result.Field is { } field
			? $"{field.Name}: {where}, {field.Type}"
			: $"{where}, {result.Located}: the word stored there");
		if (result.AlsoStarting.Count > 0) {
			output.WriteLine($"Also starting here: {string.Join(", ", result.AlsoStarting)}.");
		}

		foreach (var value in result.Values) {
			string shown = value.Value switch {
				null => "no record",
				string s => $"\"{s}\"",
				int[] list => $"[{string.Join(", ", list)}]",
				var v => Convert.ToString(v, CultureInfo.InvariantCulture)!,
			};
			output.WriteLine(value.Hex == null
				? $"{value.Index,3}  {value.Name,-10}  {shown}"
				: value.Value is int[]
					? $"{value.Index,3}  {value.Name,-10}  {shown}  ({value.Hex})"
					: $"{value.Index,3}  {value.Name,-10}  {shown,6}  {value.Hex}");
		}

		var numbers = result.Values.Select(v => v.Value).OfType<short>().ToList();
		if (numbers.Count > 0) {
			output.WriteLine($"{numbers.Count} chassis: {numbers.Min()} to {numbers.Max()}, {numbers.Distinct().Count()} distinct value(s).");
		}
	}
}
