using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>One set member slot of a structure group, read from the first row #14 record with its GUID.</summary>
/// <param name="Slot">Its index among the group's 20 member refs, which picks its formation slot.</param>
/// <param name="TypeIndex">The record's <c>BASES.DAT</c> type; null when row #14 has no record with the GUID.</param>
/// <param name="OwnPoint">
/// Whether the record names a row #6 point the mission has, which wins over its formation slot
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 2).
/// </param>
/// <param name="Picks">Whether the record's random table key is set, so a picked template replaces the type stored.</param>
internal sealed record FormationMember(int Slot, short Guid, short? TypeIndex, string? TypeName, bool OwnPoint, bool Picks);

/// <summary>One row #16 structure group record, raw: no condition applied and no repeated GUID merged.</summary>
/// <param name="Index">The record's position in row #16.</param>
/// <param name="PaintsGround"><see cref="MissionGroup164.PaintsGround"/> set.</param>
internal sealed record FormationGroup(string Mission, int Index, short Guid, string? Condition, bool PaintsGround,
	short DeploymentActionRef, IReadOnlyList<FormationMember> Members);

/// <summary>Every structure group record naming one <c>BFORMS.DAT</c> formation.</summary>
internal sealed record FormationUse(short FormationId, IReadOnlyList<FormationGroup> Groups);

/// <summary>
/// <c>formations</c>: every row #16 record of a structure group (<see cref="MissionGroup164.MemberKind"/> 2), by its
/// <see cref="MissionGroup164.FormationId"/>, with the type standing in each member slot. What a formation's slots and pad
/// do is docs/retail/formats/script-dat.md#placement--the-actual-rule and #base-formation-terrain.
/// </summary>
internal static class FormationQuery {
	public static IReadOnlyList<FormationUse> Run(RetailData data, IReadOnlyCollection<string> missions) {
		var groups = new List<(short Formation, FormationGroup Group)>();
		foreach (var mission in data.Missions.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))) {
			var file = mission.File;
			var bases = Roster.Records(file, RosterKind.Base);
			var records = file.Groups ?? [];
			for (int i = 0; i < records.Length; i++) {
				var group = records[i];
				if (group.MemberKind != (short)RosterKind.Base) {
					continue;
				}

				var members = new List<FormationMember>();
				for (int slot = 0; slot < group.MemberRefs.Length; slot++) {
					short guid = group.MemberRefs[slot];
					if (guid == -1) {
						continue;
					}

					var record = bases.FirstOrDefault(r => r.Guid == guid);
					members.Add(new FormationMember(slot, guid, record?.TypeIndex,
						record != null ? data.TypeName(RosterKind.Base, record.TypeIndex) : null,
						record != null && file.GetPoint(record.PositionRef) != null, record is { RandomTableKey: not -1 }));
				}

				groups.Add((group.FormationId, new FormationGroup(mission.Name, i, group.GUID,
					Conditions.Describe(file, group.ConditionRef), group.PaintsGround != 0, group.DeploymentActionRef, members)));
			}
		}

		return groups.GroupBy(g => g.Formation).OrderBy(g => g.Key)
			.Select(g => new FormationUse(g.Key, g.Select(x => x.Group).ToList()))
			.ToList();
	}

	public static void Write(TextWriter output, IReadOnlyList<FormationUse> uses) {
		output.WriteLine("Member slots as \"slot TYPE\": * names a position of its own, ? has its type picked from a random table, (none) names no row #14 record.");
		foreach (var use in uses) {
			output.WriteLine();
			output.WriteLine($"formation {use.FormationId}: {use.Groups.Count} group records, {use.Groups.Count(g => g.PaintsGround)} lay a pad");
			foreach (var group in use.Groups) {
				string pad = group.PaintsGround ? "pad" : "no pad";
				string condition = group.Condition != null ? $" if {group.Condition}" : "";
				string waits = group.DeploymentActionRef != -1 ? $", waits on action {group.DeploymentActionRef}" : "";
				output.WriteLine($"  {group.Mission} group {group.Index} (GUID {group.Guid}){condition}, {pad}{waits}:");
				foreach (var member in group.Members) {
					string type = member.TypeIndex is { } t ? $"{member.TypeName ?? "type " + t}" : "(none)";
					string marks = (member.OwnPoint ? "*" : "") + (member.Picks ? "?" : "");
					output.WriteLine($"    {member.Slot,2} {type}{marks}");
				}
			}
		}
	}
}
