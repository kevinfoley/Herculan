namespace HercWorks.Query;

/// <summary>One ES1 row-11 group and the order it holds itself (docs/es1/formats/msn-mission-file.md#groups).</summary>
/// <param name="Index">The group's position in row 11; 0 stands for the player's squad.</param>
/// <param name="Members">How many of the eight member refs are set.</param>
/// <param name="Order">The order, <c>0x32</c>.</param>
/// <param name="Formation">The formation, <c>0x30</c>.</param>
/// <param name="MemberKind">The member kind, <c>0x2e</c>: 0 mechs, 1 flyers, 2 bases.</param>
/// <param name="SideWord">The word at <c>0x4a</c>, whose low byte is the side.</param>
/// <param name="TargetRow">The row <paramref name="TargetRef"/> names, <c>0x4c</c>.</param>
/// <param name="TargetRef">The target, <c>0x4e</c>.</param>
/// <param name="ActionRef">The action ref, <c>0x50</c>.</param>
internal sealed record Es1GroupOrder(
	int Index, short Guid, short ConditionRef, string? Condition, short MemberKind, int Members, short Order, short Formation,
	short SideWord, short TargetRow, short TargetRef, short ActionRef, short WaypointGroupRef) {
	public bool PlayerSquad => Index == 0;
}

internal sealed record Es1OrderQueryMission(string Mission, bool OtherLayout, IReadOnlyList<Es1GroupOrder> Groups);

/// <summary>How many groups hold one order value, and in how many missions.</summary>
internal sealed record Es1OrderCount(short Order, int Groups, int Missions);

/// <summary>The result of <c>orders</c> over an ES1 install.</summary>
/// <param name="OtherLayoutMissions">The missions searched that are in the other layout; their groups are read and counted as the rest are.</param>
internal sealed record Es1OrderQueryResult(
	int MissionsSearched, int Groups, IReadOnlyList<string> OtherLayoutMissions, IReadOnlyList<Es1OrderCount> OrderCensus,
	IReadOnlyList<Es1OrderQueryMission> Missions);

/// <summary>
/// <c>orders</c> over an ES1 install. An ES1 group holds its order, formation and target itself, where ES2's names up to
/// ten order records (docs/es1/formats/msn-mission-file.md#groups), so this lists each group's fields and counts the
/// order values over every group searched. A file in the other layout has the same group record
/// (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout) and is listed with the rest.
/// </summary>
internal static class Es1OrderQuery {
	public static Es1OrderQueryResult Run(RetailData data, IReadOnlyCollection<string> missions) {
		var searched = data.Es1Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new Es1OrderQueryMission(m.Name, m.OtherLayout, m.File.Groups.Select((g, i) => new Es1GroupOrder(
				i, g.GUID, g.ConditionRef, Conditions.Describe(m.File, g.ConditionRef), g.MemberKind, g.MemberRefs.Count(r => r != -1),
				g.Word32, g.Word30, g.Word4A, g.Ref4ERow, g.Ref4E, g.ActionRef, g.WaypointGroupRef)).ToList()))
			.ToList();

		var census = searched
			.SelectMany(m => m.Groups.Select(g => (m.Mission, g.Order)))
			.GroupBy(x => x.Order)
			.OrderBy(g => g.Key)
			.Select(g => new Es1OrderCount(g.Key, g.Count(), g.Select(x => x.Mission).Distinct().Count()))
			.ToList();

		return new Es1OrderQueryResult(searched.Count, searched.Sum(m => m.Groups.Count),
			searched.Where(m => m.OtherLayout).Select(m => m.Mission).ToList(), census, searched);
	}

	public static void Write(TextWriter o, Es1OrderQueryResult result) {
		o.WriteLine($"{result.Groups} row 11 groups across {result.MissionsSearched} missions" +
			(result.OtherLayoutMissions.Count == 0 ? "." : $", {result.OtherLayoutMissions.Count} of them in the other layout " +
				$"({string.Join(", ", result.OtherLayoutMissions)}), whose groups are counted with the rest."));
		o.WriteLine("Order (0x32)  Groups  Missions");
		foreach (var c in result.OrderCensus) {
			o.WriteLine($"{c.Order,12}  {c.Groups,6}  {c.Missions,8}");
		}

		foreach (var m in result.Missions) {
			o.WriteLine();
			o.WriteLine($"{m.Mission}: {m.Groups.Count} groups{(m.OtherLayout ? " (other layout)" : "")}");
			foreach (var g in m.Groups) {
				var line = new List<string> {
					g.Guid == -1 ? $"group #{g.Index} (no GUID)" : $"group {g.Guid} (#{g.Index})",
					$"{g.Members} {OrderQuery.MemberKind(g.MemberKind)}{(g.Members == 1 ? "" : "s")}" + (g.PlayerSquad ? ", the player's squad" : ""),
					$"order {g.Order}",
					$"formation {g.Formation}",
					$"side word {g.SideWord}",
				};
				if (g.TargetRef != -1) {
					line.Add($"target {OrderQuery.SubjectKind(g.TargetRow)} {g.TargetRef}");
				}

				if (g.WaypointGroupRef != -1) {
					line.Add($"waypoint group {g.WaypointGroupRef}");
				}

				if (g.ActionRef != -1) {
					line.Add($"action {g.ActionRef}");
				}

				if (g.ConditionRef != -1) {
					line.Add(TextReport.Cond(g.ConditionRef, g.Condition, true));
				}

				o.WriteLine("  " + string.Join(", ", line));
			}
		}
	}
}
