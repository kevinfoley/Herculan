using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Query;

/// <summary>An ES1 group that names a roster record among its eight member refs (<see cref="Es1Group.MemberRefs"/>).</summary>
/// <param name="Index">The group's position in row 11.</param>
/// <param name="Slot">Which member slot names the record.</param>
/// <param name="FirstRecord">Row 11's first record, which stands for the player's squad.</param>
internal sealed record Es1GroupPlacement(int Index, short Guid, int Slot, short Order, short Formation, short ConditionRef, string? Condition,
	bool FirstRecord);

/// <summary>A roster GUID of the queried type, read from its first record, as <see cref="TypeHit"/> is for ES2.</summary>
/// <param name="VariantSourceKey">For a GUID <c>-1</c> record conditioned on a type-2 range, the key of the records that draw it.</param>
internal sealed record Es1TypeHit(
	int Index, short Guid, short TypeIndex, string? TypeName, short ConditionRef, string? Condition, short VariantKey,
	short? VariantSourceKey, IReadOnlyList<short> DrawnBy, IReadOnlyList<Es1GroupPlacement> PlacedBy, IReadOnlyList<SameGuidRecord> SameGuid);

internal sealed record Es1TypeQueryMission(string Mission, bool OtherLayout, IReadOnlyList<Es1TypeHit> Hits) {
	public int Placed => Hits.Count(h => h.PlacedBy.Count > 0);
	public int ConditionGated => Hits.Count(h => h.ConditionRef != -1 || h.SameGuid.Any(s => s.ConditionRef != -1));
}

internal sealed record Es1TypeQueryResult(
	RosterKind Roster, int Row, IReadOnlyList<int> Types, IReadOnlyList<string?> TypeNames, int MissionsSearched,
	IReadOnlyList<Es1TypeQueryMission> Missions) {
	public int Records => Missions.Sum(m => m.Hits.Count);
	public int Placed => Missions.Sum(m => m.Placed);
}

/// <summary>
/// <c>structures</c>, <c>mechs</c> and <c>flyers</c> over an ES1 install: rows 10, 8 and 9
/// (docs/es1/formats/msn-mission-file.md#the-layout), with the groups whose member kind <c>0x2e</c> names the roster and
/// whose member refs name the GUID. A record with a GUID an earlier one has overlays it, as in ES2
/// (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats), so each GUID is reported once from its first record.
/// </summary>
internal static class Es1TypeQuery {
	/// <summary>The ES1 row a roster is.</summary>
	public static int Row(RosterKind kind) => kind switch {
		RosterKind.Mech => 8,
		RosterKind.Flyer => 9,
		_ => 10,
	};

	public static Es1TypeQueryResult Run(RetailData data, RosterKind kind, IReadOnlyList<int> types) {
		var missions = data.Es1Missions
			.Select(m => new Es1TypeQueryMission(m.Name, m.OtherLayout, Search(data, m.File, kind, types)))
			.Where(m => m.Hits.Count > 0)
			.ToList();
		return new Es1TypeQueryResult(kind, Row(kind), types, types.Select(t => data.TypeName(kind, t)).ToList(), data.Es1Missions.Count, missions);
	}

	/// <summary>The fields the query reads from a mech, flyer or base record.</summary>
	private sealed record Record(int Index, short Guid, short ConditionRef, short VariantKey, short TypeIndex);

	private static List<Record> Records(Es1MissionFile file, RosterKind kind) => kind switch {
		RosterKind.Mech => file.Mechs.Select((r, i) => new Record(i, r.GUID, r.ConditionRef, r.VariantKey, r.Type)).ToList(),
		RosterKind.Flyer => file.Flyers.Select((r, i) => new Record(i, r.GUID, r.ConditionRef, r.VariantKey, r.Type)).ToList(),
		_ => file.Bases.Select((r, i) => new Record(i, r.GUID, r.ConditionRef, r.VariantKey, r.Type)).ToList(),
	};

	private static List<Es1TypeHit> Search(RetailData data, Es1MissionFile file, RosterKind kind, IReadOnlyList<int> types) {
		var records = Records(file, kind);
		var matching = records.Where(r => types.Contains(r.TypeIndex)).ToList();
		var reported = matching
			.Where(r => r.Guid != -1)
			.Select(r => r.Guid)
			.Distinct()
			.Select(g => records.First(r => r.Guid == g))
			.Concat(matching.Where(r => r.Guid == -1))
			.OrderBy(r => r.Index);

		var hits = new List<Es1TypeHit>();
		foreach (var record in reported) {
			var sameGuid = record.Guid == -1 ? [] : records
				.Where(r => r.Guid == record.Guid && r.Index != record.Index)
				.Select(r => new SameGuidRecord(r.Index, r.ConditionRef, Conditions.Describe(file, r.ConditionRef), r.TypeIndex, null))
				.ToList();

			short? sourceKey = record.Guid == -1 && record.ConditionRef != -1 ? Conditions.VariantKeyOf(file, record.ConditionRef) : null;
			var drawnBy = sourceKey is { } key
				? records.Where(r => r.VariantKey == key && r.Guid != -1).Select(r => r.Guid).Distinct().ToList()
				: [];

			var placedBy = (sourceKey == null ? [record.Guid] : drawnBy)
				.Where(g => g != -1)
				.SelectMany(g => Placements(file, kind, g))
				.ToList();

			hits.Add(new Es1TypeHit(record.Index, record.Guid, record.TypeIndex, data.TypeName(kind, record.TypeIndex), record.ConditionRef,
				Conditions.Describe(file, record.ConditionRef), record.VariantKey, sourceKey, drawnBy, placedBy, sameGuid));
		}

		return hits;
	}

	/// <summary>Every group whose member kind is <paramref name="kind"/> naming <paramref name="guid"/>, once per slot that names it.</summary>
	private static IEnumerable<Es1GroupPlacement> Placements(Es1MissionFile file, RosterKind kind, short guid) {
		for (int g = 0; g < file.Groups.Length; g++) {
			var group = file.Groups[g];
			if (group.MemberKind != (short)kind) {
				continue;
			}

			for (int slot = 0; slot < group.MemberRefs.Length; slot++) {
				if (group.MemberRefs[slot] == guid) {
					yield return new Es1GroupPlacement(g, group.GUID, slot, group.Word32, group.Word30, group.ConditionRef,
						Conditions.Describe(file, group.ConditionRef), g == 0);
				}
			}
		}
	}

	public static void Write(TextWriter o, RetailData data, Es1TypeQueryResult result, bool variants) {
		string noun = Roster.Noun(result.Roster);
		string types = string.Join(", ", result.Types.Select((t, i) => TextReport.Label(t, result.TypeNames[i])));
		o.WriteLine($"ES1 row {result.Row} ({noun}) type {types}: {result.Missions.Count} of {result.MissionsSearched} missions, " +
			$"{result.Records} records, {result.Placed} placed by a group.");
		if (result.Missions.Count == 0) {
			return;
		}

		o.WriteLine();
		o.WriteLine("Mission    Records  Placed  Cond-gated");
		foreach (var m in result.Missions) {
			o.WriteLine($"{m.Mission,-10} {m.Hits.Count,7}  {m.Placed,6}  {m.ConditionGated,10}{(m.OtherLayout ? "  (other layout)" : "")}");
		}

		foreach (var m in result.Missions) {
			o.WriteLine();
			o.WriteLine(m.Mission);
			foreach (var hit in m.Hits) {
				var line = new List<string> { hit.Guid == -1 ? $"#{hit.Index} (no GUID)" : $"GUID {hit.Guid}" };
				if (result.Types.Count > 1 || !result.Types.Contains(hit.TypeIndex)) {
					line.Add(TextReport.Label(hit.TypeIndex, hit.TypeName));
				}

				if (hit.ConditionRef != -1) {
					line.Add(TextReport.Cond(hit.ConditionRef, hit.Condition, variants));
				}

				if (hit.VariantKey != -1) {
					line.Add($"variant key {hit.VariantKey}: type and payload drawn");
				}

				if (hit.VariantSourceKey is { } key) {
					line.Add($"variant of key {key}, drawn by {(hit.DrawnBy.Count == 0 ? "nothing" : "GUID " + string.Join(", ", hit.DrawnBy))}");
				}

				o.WriteLine("  " + string.Join(", ", line));
				if (hit.PlacedBy.Count == 0) {
					o.WriteLine("    not placed by any group");
				}

				foreach (var g in hit.PlacedBy) {
					var parts = new List<string> { $"placed by group {g.Guid} slot {g.Slot}", $"order {g.Order}", $"formation {g.Formation}" };
					if (g.ConditionRef != -1) {
						parts.Add(TextReport.Cond(g.ConditionRef, g.Condition, variants));
					}

					if (g.FirstRecord) {
						parts.Add("row 11's first record, the player's squad");
					}

					o.WriteLine("    " + string.Join(", ", parts));
				}

				foreach (var s in hit.SameGuid) {
					var parts = new List<string> {
						$"same GUID, #{s.Index}",
						s.ConditionRef == -1 ? "unconditioned" : TextReport.Cond(s.ConditionRef, s.Condition, variants),
						TextReport.Label(s.TypeIndex, data.TypeName(result.Roster, s.TypeIndex)),
					};
					o.WriteLine("    " + string.Join(", ", parts));
				}
			}
		}
	}
}
