using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Query;

/// <summary>A group that names a roster record among its members.</summary>
/// <param name="Index">The group's position in row #16.</param>
/// <param name="Slot">Which of its 20 member slots names the record — its formation slot.</param>
/// <param name="Side">0 human, 1 Cybrid, <c>-1</c> unset.</param>
/// <param name="DeploymentActionRef">The row-10 action the group waits for, <c>-1</c> when it starts in the mission.</param>
/// <param name="DeploymentVerb">
/// That action's verb, which picks how the group arrives (docs/retail/simulation/mission-deployment.md,
/// "Arrival"); null when there is no such action.
/// </param>
/// <param name="FirstRecord">
/// Row #16's first record, the player's squad, whose member slots the simulator does not read
/// (<see cref="MissionGroup164.MemberRefs"/>).
/// </param>
internal sealed record GroupPlacement(
	int Index, short Guid, int Slot, short Side, short DeploymentActionRef, short? DeploymentVerb, short ConditionRef,
	string? Condition, bool FirstRecord) {
	public bool DeploymentGated => DeploymentActionRef != -1;
}

/// <summary>A later record of the same row with the same GUID: the load overlays its set fields on the first when its condition holds.</summary>
internal sealed record SameGuidRecord(int Index, short ConditionRef, string? Condition, short TypeIndex, short? StartingCondition);

/// <summary>A roster GUID of the queried type, read from its first record, and everything that decides whether and how it reaches the mission.</summary>
/// <param name="VariantKey">The record's own variant key: unless <c>-1</c>, its type and payload are replaced by a draw.</param>
/// <param name="VariantSourceKey">
/// For a GUID <c>-1</c> record conditioned on a type-2 range, the key of the records that draw it;
/// null otherwise.
/// </param>
/// <param name="DrawnBy">The GUIDs of the records whose variant key is <see cref="VariantSourceKey"/>.</param>
/// <param name="PlacedBy">The groups naming the record, or for a variant source, the records that draw it.</param>
internal sealed record TypeHit(
	int Index, short Guid, short TypeIndex, string? TypeName, short ConditionRef, string? Condition,
	short VariantKey, short? StartingCondition, short? VariantSourceKey, IReadOnlyList<short> DrawnBy,
	IReadOnlyList<GroupPlacement> PlacedBy, IReadOnlyList<SameGuidRecord> SameGuid) {
	public bool Placed => PlacedBy.Count > 0;

	/// <summary>Placed only by groups that wait on a deployment action.</summary>
	public bool DeploymentGated => Placed && PlacedBy.All(g => g.DeploymentGated);

	/// <summary>Placed by at least one group that waits on a deployment action.</summary>
	public bool AnyDeploymentGated => PlacedBy.Any(g => g.DeploymentGated);

	public bool ConditionGated => ConditionRef != -1 || SameGuid.Any(s => s.ConditionRef != -1);
}

internal sealed record TypeQueryMission(string Mission, IReadOnlyList<TypeHit> Hits) {
	public int Records => Hits.Count;
	public int Placed => Hits.Count(h => h.Placed);
	public int DeploymentGated => Hits.Count(h => h.DeploymentGated);
	public int AnyDeploymentGated => Hits.Count(h => h.AnyDeploymentGated);
	public int ConditionGated => Hits.Count(h => h.ConditionGated);

	/// <summary>The distinct sides of the groups placing the hits.</summary>
	public IReadOnlyList<short> Sides => Hits.SelectMany(h => h.PlacedBy).Select(g => g.Side).Distinct().Order().ToList();
}

internal sealed record TypeQueryResult(
	RosterKind Roster, int Row, IReadOnlyList<int> Types, IReadOnlyList<string?> TypeNames,
	int MissionsSearched, IReadOnlyList<TypeQueryMission> Missions) {
	public int Records => Missions.Sum(m => m.Records);
	public int Placed => Missions.Sum(m => m.Placed);
	public int DeploymentGated => Missions.Sum(m => m.DeploymentGated);
	public int AnyDeploymentGated => Missions.Sum(m => m.AnyDeploymentGated);
}

/// <summary>
/// <c>structures</c>, <c>mechs</c> and <c>flyers</c>: every record of a roster whose type field is one
/// of the asked types, with the groups that place it (<see cref="MissionGroup164.MemberKind"/> the
/// roster and its GUID in <see cref="MissionGroup164.MemberRefs"/>) and the records that share its GUID.
/// </summary>
internal static class TypeQuery {
	public static TypeQueryResult Run(RetailData data, RosterKind kind, IReadOnlyList<int> types) {
		var missions = new List<TypeQueryMission>();
		foreach (var mission in data.Missions) {
			var hits = Search(data, mission.File, kind, types);
			if (hits.Count > 0) {
				missions.Add(new TypeQueryMission(mission.Name, hits));
			}
		}

		return new TypeQueryResult(kind, Roster.Row(kind), types, types.Select(t => data.TypeName(kind, t)).ToList(),
			data.Missions.Count, missions);
	}

	/// <summary>
	/// One hit per GUID that any record of the asked types carries, reported from the GUID's first
	/// record, which is the one the load keeps: a later record with the GUID overlays it rather than
	/// adding a structure (docs/retail/formats/msn-mission-file.md#repeated-guids), and is listed in
	/// <see cref="TypeHit.SameGuid"/>. A GUID <c>-1</c> record is never exported, so each is its own hit.
	/// </summary>
	private static List<TypeHit> Search(RetailData data, MissionFile file, RosterKind kind, IReadOnlyList<int> types) {
		var records = Roster.Records(file, kind);
		var matching = records.Where(r => types.Contains(r.TypeIndex)).ToList();
		var reported = matching
			.Where(r => r.Guid != -1)
			.Select(r => r.Guid)
			.Distinct()
			.Select(g => records.First(r => r.Guid == g))
			.Concat(matching.Where(r => r.Guid == -1))
			.OrderBy(r => r.Index);

		var hits = new List<TypeHit>();
		foreach (var record in reported) {
			var sameGuid = record.Guid == -1 ? [] : records
				.Where(r => r.Guid == record.Guid && r.Index != record.Index)
				.Select(r => new SameGuidRecord(r.Index, r.ConditionRef, Conditions.Describe(file, r.ConditionRef), r.TypeIndex, r.StartingCondition))
				.ToList();

			short? sourceKey = record.Guid == -1 && record.ConditionRef != -1 ? Conditions.VariantKeyOf(file, record.ConditionRef) : null;
			var drawnBy = sourceKey is { } key
				? records.Where(r => r.VariantKey == key && r.Guid != -1).Select(r => r.Guid).Distinct().ToList()
				: [];

			var placedBy = (sourceKey == null ? [record.Guid] : drawnBy)
				.Where(g => g != -1)
				.SelectMany(g => Placements(file, kind, g))
				.ToList();

			hits.Add(new TypeHit(
				record.Index, record.Guid, record.TypeIndex, data.TypeName(kind, record.TypeIndex),
				record.ConditionRef, Conditions.Describe(file, record.ConditionRef), record.VariantKey,
				record.StartingCondition, sourceKey, drawnBy, placedBy, sameGuid));
		}

		return hits;
	}

	/// <summary>Every group of <paramref name="kind"/> naming <paramref name="guid"/>, once per slot that names it.</summary>
	private static IEnumerable<GroupPlacement> Placements(MissionFile file, RosterKind kind, short guid) {
		var groups = file.Groups ?? [];
		for (int g = 0; g < groups.Length; g++) {
			var group = groups[g];
			if (group.MemberKind != (short)kind) {
				continue;
			}

			for (int slot = 0; slot < group.MemberRefs.Length; slot++) {
				if (group.MemberRefs[slot] == guid) {
					yield return new GroupPlacement(g, group.GUID, slot, group.Side, group.DeploymentActionRef,
						group.DeploymentActionRef == -1 ? null : file.GetAction(group.DeploymentActionRef)?.Verb,
						group.ConditionRef, Conditions.Describe(file, group.ConditionRef), g == 0);
				}
			}
		}
	}

	/// <summary>
	/// The types <paramref name="spec"/> names: <c>all</c> for every type in the roster's table, a
	/// decimal or <c>0x</c> hex index, or a type name, matched whole and then as a prefix, ignoring case.
	/// Null when nothing matches.
	/// </summary>
	public static IReadOnlyList<int>? ResolveTypes(RetailData data, RosterKind kind, string spec) {
		if (string.Equals(spec, AllTypes, StringComparison.OrdinalIgnoreCase)) {
			int count = data.TypeCount(kind);
			return count > 0 ? Enumerable.Range(0, count).ToList() : null;
		}

		if (spec.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(spec[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex)) {
			return [hex];
		}

		if (int.TryParse(spec, out int index)) {
			return [index];
		}

		var named = Enumerable.Range(0, data.TypeCount(kind))
			.Select(t => (Type: t, Name: data.TypeName(kind, t)))
			.Where(t => t.Name != null)
			.ToList();
		var exact = named.Where(t => string.Equals(t.Name, spec, StringComparison.OrdinalIgnoreCase)).Select(t => t.Type).ToList();
		if (exact.Count > 0) {
			return exact;
		}

		var prefix = named.Where(t => t.Name!.StartsWith(spec, StringComparison.OrdinalIgnoreCase)).Select(t => t.Type).ToList();
		return prefix.Count > 0 ? prefix : null;
	}

	/// <summary>The <c>--type</c> word for every type of the roster. No retail type name starts with it.</summary>
	public const string AllTypes = "all";
}
