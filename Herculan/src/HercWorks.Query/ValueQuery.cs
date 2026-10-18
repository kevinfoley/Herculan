using System.Reflection;
using System.Text.RegularExpressions;
using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One value of the asked field, as <c>records</c> writes it, and how many records carry it.</summary>
internal sealed record ValueCount(string Value, int Records);

/// <summary>One <c>--where</c> test: the field as <c>--field</c> names it, and the value it must or must not print as.</summary>
internal sealed record ValueFilter(string Field, bool Equal, string Value) {
	public override string ToString() => $"{Field}{(Equal ? "=" : "!=")}{Value}";
}

/// <summary>The result of <c>values</c>.</summary>
/// <param name="RowName">The <see cref="MissionFile"/> property holding the row.</param>
/// <param name="Field">The field as resolved: its property name, with <c>[i]</c> when one element is asked.</param>
/// <param name="Population">Which of the row's records are counted, in words; empty for all of them.</param>
/// <param name="Missions">How many of the missions searched hold a counted record.</param>
/// <param name="Values">By count, most first; a tie by value.</param>
internal sealed record ValueQueryResult(
	int Row, string RowName, string Field, string? Kind, bool NonSquad, IReadOnlyList<ValueFilter> Where, string Population,
	int MissionsSearched, int Missions, int Records, IReadOnlyList<ValueCount> Values);

/// <summary>
/// <c>values</c>: how many raw records of one typed <c>.MSN</c> row carry each value of one field, read as <c>records</c>
/// reads them: no condition applied, no variant drawn, no repeated GUID merged. A field is a public property of the row's
/// record type; an array property takes an element index, or without one counts each whole array as a value.
///
/// <para><c>--non-squad</c> leaves out the player's squad: group 0's records in row #16 (record 0 and every later record
/// with its GUID), the row #12 records those name as members and the GUID <c>-1</c> alternatives their variant keys draw,
/// and keeps in row #15 only the orders a group outside the squad names. <c>--kind</c> keeps row #16 records naming a member whose member kind
/// <see cref="OrderQuery.MemberKind"/> calls that word, and the row #15 orders such a group names. Each <c>--where</c> then
/// keeps the records whose field prints, as <see cref="RecordQuery.Value"/> prints it, as the value given (or not).</para>
/// </summary>
internal static class ValueQuery {
	private const int MechRow = 12;
	private const int OrderRow = 15;
	private const int GroupRow = 16;

	/// <summary>The rows <c>values</c> takes: those <see cref="MissionFile"/> holds as typed records.</summary>
	public static readonly int[] Rows = [1, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17];

	/// <summary>The rows <c>--non-squad</c> and <c>--kind</c> each mean something for.</summary>
	private static readonly int[] SquadRows = [MechRow, OrderRow, GroupRow];
	private static readonly int[] KindRows = [OrderRow, GroupRow];

	/// <summary>The <c>--kind</c> words: the member kinds <see cref="OrderQuery.MemberKind"/> names.</summary>
	public static readonly string[] Kinds = [OrderQuery.MemberKind(0), OrderQuery.MemberKind(1), OrderQuery.MemberKind(2)];

	private static readonly Regex FieldSpec = new(@"^(\w+)(?:\[(\d+)\])?$");

	/// <summary>Null with <paramref name="error"/> set when the row, field, kind or flag does not fit together.</summary>
	public static ValueQueryResult? Run(RetailData data, IReadOnlyCollection<string> missions, int row, string field,
			string? kind, bool nonSquad, IReadOnlyList<string> where, out string? error) {
		error = null;
		if (!Rows.Contains(row)) {
			error = $"values takes a typed row: {string.Join(", ", Rows)}.";
			return null;
		}

		if (nonSquad && !SquadRows.Contains(row)) {
			error = $"--non-squad means something for rows {string.Join(", ", SquadRows)} only; row #{row} holds nothing of the player's squad.";
			return null;
		}

		if (kind != null && (!KindRows.Contains(row) || !Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase))) {
			error = $"--kind takes {string.Join(", ", Kinds)}, on rows {string.Join(" and ", KindRows)}.";
			return null;
		}

		kind = kind?.ToLowerInvariant();
		string rowName = RowName(row);
		var recordType = typeof(MissionFile).GetProperty(rowName)!.PropertyType.GetElementType()!;
		var properties = RecordQuery.Properties(recordType).ToList();
		if (Resolve(properties, row, rowName, field, out error) is not { } asked) {
			return null;
		}

		var filters = new List<(ValueFilter Filter, PropertyInfo Property, int? Index)>();
		foreach (string test in where) {
			int at = test.IndexOf('=');
			bool equal = at > 0 && test[at - 1] != '!';
			string name = at <= 0 ? "" : test[..(equal ? at : at - 1)];
			if (name.Length == 0) {
				error = $"--where takes FIELD=VALUE or FIELD!=VALUE, not {test}.";
				return null;
			}

			if (Resolve(properties, row, rowName, name, out error) is not { } on) {
				return null;
			}

			filters.Add((new ValueFilter(Name(on.Property, on.Index), equal, test[(at + 1)..]), on.Property, on.Index));
		}

		var (property, index) = asked;
		var searched = data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.ToList();
		var counted = searched
			.Select(m => (m.Name, Records: Population(m.File, row, kind, nonSquad)
				.Where(r => filters.All(f => (Value(f.Property, r, f.Index) == f.Filter.Value) == f.Filter.Equal))
				.ToList()))
			.ToList();

		if (index is { } i) {
			int longest = counted.SelectMany(m => m.Records).Select(r => (property.GetValue(r) as Array)?.Length ?? 0).DefaultIfEmpty().Max();
			if (counted.Any(m => m.Records.Count > 0) && i >= longest) {
				error = $"{property.Name} has {longest} elements; the index is 0-{longest - 1}.";
				return null;
			}
		}

		var values = counted
			.SelectMany(m => m.Records)
			.Select(r => Value(property, r, index))
			.GroupBy(v => v)
			.Select(g => new ValueCount(g.Key, g.Count()))
			.OrderByDescending(v => v.Records)
			.ThenBy(v => short.TryParse(v.Value, out short n) ? n : int.MaxValue)
			.ThenBy(v => v.Value, StringComparer.Ordinal)
			.ToList();

		var described = filters.Select(f => f.Filter).ToList();
		return new ValueQueryResult(row, rowName, Name(property, index), kind, nonSquad, described,
			Describe(row, kind, nonSquad, described), searched.Count, counted.Count(m => m.Records.Count > 0), values.Sum(v => v.Records), values);
	}

	public static void Write(TextWriter o, ValueQueryResult result) {
		string population = result.Population.Length == 0 ? "records" : "records " + result.Population;
		o.WriteLine($"Row #{result.Row} {result.RowName}.{result.Field}: {result.Records} {population}, from {result.Missions} of " +
			$"{result.MissionsSearched} missions; raw: no condition applied, no variant drawn, no repeated GUID merged.");
		if (result.Values.Count == 0) {
			return;
		}

		o.WriteLine();
		o.WriteLine("Records  Share  Value");
		foreach (var v in result.Values) {
			o.WriteLine($"{v.Records,7}  {100.0 * v.Records / result.Records,4:0}%  {v.Value}");
		}
	}

	/// <summary>The <see cref="MissionFile"/> property holding a row, as <c>records</c> names it.</summary>
	private static string RowName(int row) => row switch {
		1 => nameof(MissionFile.Conditions),
		3 => nameof(MissionFile.Variants),
		4 => nameof(MissionFile.Texts),
		6 => nameof(MissionFile.Points),
		7 => nameof(MissionFile.Headings),
		8 => nameof(MissionFile.WaypointGroups),
		9 => nameof(MissionFile.TriggerAreas),
		10 => nameof(MissionFile.Actions),
		11 => nameof(MissionFile.ActionTimers),
		12 => nameof(MissionFile.Mechs),
		13 => nameof(MissionFile.Flyers),
		14 => nameof(MissionFile.Bases),
		15 => nameof(MissionFile.Orders),
		16 => nameof(MissionFile.Groups),
		_ => nameof(MissionFile.Objectives),
	};

	private static string Value(PropertyInfo property, object record, int? index) {
		object? value = property.GetValue(record);
		if (index is not { } i) {
			return RecordQuery.Value(value);
		}

		return value is Array array && i < array.Length ? RecordQuery.Value(array.GetValue(i)) : "(no element)";
	}

	private static string Describe(int row, string? kind, bool nonSquad, IReadOnlyList<ValueFilter> where) {
		string squad = nonSquad ? " outside the player's squad" : "";
		string population = row switch {
			GroupRow when kind != null => $"of {kind} groups naming a member{squad}",
			OrderRow when kind != null || nonSquad => $"named by a{(kind != null ? " " + kind : "")} group{squad}",
			_ => squad.TrimStart(),
		};
		string filters = where.Count == 0 ? "" : "where " + string.Join(" and ", where);
		return string.Join(", ", new[] { population, filters }.Where(t => t.Length > 0));
	}

	private static string Name(PropertyInfo property, int? index) => index is { } at ? $"{property.Name}[{at}]" : property.Name;

	/// <summary>
	/// The property and element index <paramref name="spec"/> names, <c>Name</c> or <c>Name[i]</c> in any case; null with
	/// <paramref name="error"/> naming the row's fields when it names none, or an index on a field that is no array.
	/// </summary>
	private static (PropertyInfo Property, int? Index)? Resolve(List<PropertyInfo> properties, int row, string rowName, string spec,
			out string? error) {
		error = null;
		var match = FieldSpec.Match(spec);
		var property = match.Success
			? properties.FirstOrDefault(p => string.Equals(p.Name, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
			: null;
		if (property == null) {
			var array = properties.FirstOrDefault(p => p.PropertyType.IsArray);
			error = $"Row #{row} {rowName} has no field {spec}. Its fields: " +
				string.Join(", ", properties.Select(p => p.PropertyType.IsArray ? p.Name + "[]" : p.Name)) + "." +
				(array != null ? $" An array field takes an element index, as in {array.Name}[0]." : "");
			return null;
		}

		int? index = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : null;
		if (index != null && !property.PropertyType.IsArray) {
			error = $"{property.Name} is not an array; it takes no index.";
			return null;
		}

		return (property, index);
	}

	/// <summary>The row's records <paramref name="kind"/> and <paramref name="nonSquad"/> keep, in file order.</summary>
	private static IEnumerable<object> Population(MissionFile file, int row, string? kind, bool nonSquad) {
		var squad = nonSquad ? SquadOf(file) : null;
		var groups = file.Groups ?? [];
		bool KeepGroup(int i) {
			var g = groups[i];
			return (squad == null || !squad.GroupRecords.Contains(i))
				&& (kind == null || (OrderQuery.MemberKind(g.MemberKind) == kind && g.MemberRefs.Any(m => m != -1)));
		}

		switch (row) {
			case GroupRow:
				return groups.Where((_, i) => KeepGroup(i));
			case OrderRow when kind != null || nonSquad:
				var named = Enumerable.Range(0, groups.Length).Where(KeepGroup)
					.SelectMany(i => groups[i].OrderRefs)
					.Where(r => r != -1)
					.ToHashSet();
				return (file.Orders ?? []).Where(o => o.GUID != -1 && named.Contains(o.GUID));
			case MechRow when squad != null:
				return (file.Mechs ?? []).Where(m => !squad.Holds(file, m));
		}

		// Row #17's one null, the record DEMO2.MSN ends inside, is no record to count.
		return (typeof(MissionFile).GetProperty(RowName(row))!.GetValue(file) as Array)?.OfType<object>() ?? [];
	}

	/// <summary>The player's squad in one file: group 0's row #16 records, the GUIDs they name, and those members' variant keys.</summary>
	private sealed record Squad(HashSet<int> GroupRecords, HashSet<short> Members, HashSet<short> VariantKeys) {
		/// <summary>A squad member's record, or a GUID <c>-1</c> alternative one of their variant keys draws.</summary>
		public bool Holds(MissionFile file, MechRosterEntry144 mech) => mech.GUID != -1
			? Members.Contains(mech.GUID)
			: mech.ConditionRef != -1 && Conditions.VariantKeyOf(file, mech.ConditionRef) is { } key && VariantKeys.Contains(key);
	}

	private static Squad SquadOf(MissionFile file) {
		var groups = file.Groups ?? [];
		var records = new HashSet<int>();
		if (groups.Length > 0) {
			short guid = groups[0].GUID;
			records.UnionWith(Enumerable.Range(0, groups.Length).Where(i => i == 0 || (guid != -1 && groups[i].GUID == guid)));
		}

		var members = records.SelectMany(i => groups[i].MemberRefs).Where(r => r != -1).ToHashSet();
		var keys = (file.Mechs ?? []).Where(m => m.GUID != -1 && members.Contains(m.GUID) && m.VariantKey != -1)
			.Select(m => m.VariantKey)
			.ToHashSet();
		return new Squad(records, members, keys);
	}
}
