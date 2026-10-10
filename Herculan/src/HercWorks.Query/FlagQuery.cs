using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>A record whose condition field names a given row-1 GUID.</summary>
/// <param name="Row">The <c>.MSN</c> row, or <c>ENG</c> for the mission's text.</param>
/// <param name="Id">The record's GUID, or for the rows without one (2, 4, 5, 17) its index; the text id for <c>ENG</c>.</param>
internal sealed record GatedRecord(string Row, short Id, string Label);

/// <summary>A type-0 row-1 record comparing the flag.</summary>
internal sealed record FlagTest(string Mission, int Index, short ConditionGuid, string Test, string Chain, IReadOnlyList<GatedRecord> Gates);

/// <summary>A record that writes the flag.</summary>
/// <param name="Subject">What the record is, e.g. <c>base GUID 73, type 0x22 TRANSPORT</c>.</param>
internal sealed record FlagWrite(string Mission, int Row, int Index, string Subject, short Op, string Effect, string? Condition);

internal sealed record FlagQueryResult(short Flag, int MissionsSearched, IReadOnlyList<FlagTest> Tests, IReadOnlyList<FlagWrite> Writes);

/// <summary>
/// <c>flag</c>: every record of every mission that tests campaign flag <c>n</c> — a type-0 row-1
/// record — and every record that writes it: the out-of-action reports of rows #12, #13, #14 and #16,
/// row #10's action counters, row #17's objective counters, and row #2's clear list. The mission
/// counters are the campaign flag array for the length of a mission
/// (docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4).
/// </summary>
internal static class FlagQuery {
	/// <summary>Row #2's words: the condition, ten header words, then thirty flag indices to clear.</summary>
	private const int ClearListWord = 11;

	/// <summary>Row #5's records, kept raw by the model: 64 bytes, the condition ref first.</summary>
	private const int DebriefRecordLength = 64;

	public static FlagQueryResult Run(RetailData data, short flag) {
		var tests = new List<FlagTest>();
		var writes = new List<FlagWrite>();
		foreach (var mission in data.Missions) {
			var file = mission.File;
			var conditions = file.Conditions ?? [];
			for (int i = 0; i < conditions.Length; i++) {
				var c = conditions[i];
				if (c.Type != 0 || c.FlagIndexOrRangeLower != flag) {
					continue;
				}

				string test = Conditions.Operator(c.OperatorOrRangeUpperOrResult) is { } op
					? $"flag {flag} {op} {c.ComparisonOperand}"
					: $"flag {flag} op 0x{(ushort)c.OperatorOrRangeUpperOrResult:x} (fails)";
				tests.Add(new FlagTest(mission.Name, i, c.GUID, test, Conditions.Describe(file, c.GUID)!, Gates(mission, c.GUID)));
			}

			Writes(data, mission, flag, writes);
		}

		return new FlagQueryResult(flag, data.Missions.Count, tests, writes);
	}

	private static void Writes(RetailData data, Mission mission, short flag, List<FlagWrite> writes) {
		var file = mission.File;
		string? Cond(short guid) => Conditions.Describe(file, guid);

		void Pairs(int row, int index, string subject, short[] pairs, CounterLayer layer, short conditionRef) {
			for (int p = 0; p + 1 < pairs.Length; p += 2) {
				if (pairs[p] == flag) {
					writes.Add(new FlagWrite(mission.Name, row, index, subject, pairs[p + 1], CounterOps.Describe(layer, pairs[p + 1]), Cond(conditionRef)));
				}
			}
		}

		foreach (var kind in new[] { RosterKind.Mech, RosterKind.Flyer, RosterKind.Base }) {
			foreach (var r in Roster.Records(file, kind)) {
				string type = r.TypeIndex == -1 ? "no type" : $"type 0x{r.TypeIndex:x2} {data.TypeName(kind, r.TypeIndex)}".TrimEnd();
				Pairs(Roster.Row(kind), r.Index, $"{Roster.Noun(kind)} GUID {r.Guid}, {type}", r.OutOfActionReport, CounterLayer.OutOfAction, r.ConditionRef);
			}
		}

		var groups = file.Groups ?? [];
		for (int g = 0; g < groups.Length; g++) {
			var group = groups[g];
			Pairs(16, g, $"group GUID {group.GUID}, {(RosterKind)group.MemberKind} members, side {Side(group.Side)}", group.OutOfActionReport, CounterLayer.OutOfAction, group.ConditionRef);
		}

		var actions = file.Actions ?? [];
		for (int a = 0; a < actions.Length; a++) {
			Pairs(10, a, $"action GUID {actions[a].GUID}", actions[a].CounterPairs, CounterLayer.Action, actions[a].ConditionRef);
		}

		var objectives = file.Objectives ?? [];
		for (int o = 0; o < objectives.Length; o++) {
			if (objectives[o] is not { } objective) {
				continue;
			}

			foreach (var pair in objective.Pairs.Where(p => p.CounterRef == flag)) {
				writes.Add(new FlagWrite(mission.Name, 17, o, $"objective {o}", pair.Op, CounterOps.Describe(CounterLayer.Objective, pair.Op), Cond(objective.ConditionRef)));
			}
		}

		var patches = file.SettingsPatches ?? [];
		for (int p = 0; p < patches.Length; p++) {
			if (patches[p].Data.Skip(ClearListWord).Contains(flag)) {
				writes.Add(new FlagWrite(mission.Name, 2, p, $"settings patch {p}", -1, CounterOps.Describe(CounterLayer.LoadClear, -1), Cond(patches[p].Data[0])));
			}
		}
	}

	/// <summary>Every record of the mission, its text included, whose condition field is <paramref name="guid"/>.</summary>
	internal static List<GatedRecord> Gates(Mission mission, short guid) {
		var file = mission.File;
		var gates = new List<GatedRecord>();
		void Add<T>(int row, T[]? records, Func<T, short> condition, Func<T, int, short> id, string noun, Func<T, string>? detail = null) {
			for (int i = 0; i < (records?.Length ?? 0); i++) {
				if (records![i] is { } record && condition(record) == guid) {
					short key = id(record, i);
					gates.Add(new GatedRecord(row.ToString(), key, $"row {row} {noun} {key}{detail?.Invoke(record)}"));
				}
			}
		}

		// A roster overlay often changes only its type or the variant key it draws from.
		static string RosterDetail(short type, short variantKey) =>
			(type == -1 ? "" : $" type 0x{type:x2}") + (variantKey == -1 ? "" : $" variant key {variantKey}");

		Add(1, file.Conditions, r => r.ConditionRef, (r, _) => r.GUID, "cond");
		Add(2, file.SettingsPatches, r => r.Data[0], (_, i) => (short)i, "patch", r => $" (header words {string.Join(" ", r.Data.Skip(1).Take(ClearListWord - 1))})");
		Add(3, file.Variants, r => r.ConditionRef, (r, _) => r.GUID, "value GUID");
		Add(4, file.Texts, r => r.ConditionRef, (_, i) => (short)i, "text");
		if (file.DebriefBytes is { } debrief) {
			for (int i = 0; i * DebriefRecordLength + 1 < debrief.Length; i++) {
				if (BitConverter.ToInt16(debrief, i * DebriefRecordLength) == guid) {
					gates.Add(new GatedRecord("5", (short)i, $"row 5 debrief {i}"));
				}
			}
		}

		Add(6, file.Points, r => r.ConditionRef, (r, _) => r.GUID, "point GUID");
		Add(7, file.Headings, r => r.ConditionRef, (r, _) => r.GUID, "heading GUID");
		Add(8, file.WaypointGroups, r => r.ConditionRef, (r, _) => r.GUID, "route GUID");
		Add(9, file.TriggerAreas, r => r.ConditionRef, (r, _) => r.GUID, "area GUID");
		Add(10, file.Actions, r => r.ConditionRef, (r, _) => r.GUID, "action GUID");
		Add(11, file.ActionTimers, r => r.ConditionRef, (r, _) => r.GUID, "timer GUID");
		Add(12, file.Mechs, r => r.ConditionRef, (r, _) => r.GUID, "mech GUID", r => RosterDetail(r.TypeIndex, r.VariantKey));
		Add(13, file.Flyers, r => r.ConditionRef, (r, _) => r.GUID, "flyer GUID", r => RosterDetail(r.TypeIndex, r.VariantKey));
		Add(14, file.Bases, r => r.ConditionRef, (r, _) => r.GUID, "base GUID", r => RosterDetail(r.TypeIndex, r.VariantKey));
		Add(15, file.Orders, r => r.ConditionRef, (r, _) => r.GUID, "order GUID");
		Add(16, file.Groups, r => r.ConditionRef, (r, _) => r.GUID, "group GUID");
		Add(17, file.Objectives, r => r!.ConditionRef, (_, i) => (short)i, "objective");

		foreach (var entry in mission.Text?.Strings ?? []) {
			if (entry.ConditionRef == guid) {
				gates.Add(new GatedRecord("ENG", entry.Guid, $"ENG id {entry.Guid}"));
			}
		}

		return gates;
	}

	public static string Side(short side) => side switch {
		0 => "human",
		1 => "Cybrid",
		_ => side.ToString(),
	};
}
