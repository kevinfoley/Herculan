using HercWorks.Core.Io.Transform;

namespace HercWorks.Query;

/// <summary>One row-2 header patch.</summary>
/// <param name="Index">The patch's position in row 2.</param>
/// <param name="Words">The ten header words, as authored.</param>
/// <param name="ClearedFlags">ES2's flag indices that are not <c>-1</c>, which the load zeroes; empty for ES1, whose patch has none.</param>
internal sealed record HeaderPatch(int Index, short ConditionRef, string? Condition, IReadOnlyList<short> Words, IReadOnlyList<short> ClearedFlags);

/// <param name="FileHeaderWords">
/// For an ES1 file in the other layout, the ten header words it holds after the revision word in place of a header-patch
/// row (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout); null otherwise.
/// </param>
internal sealed record PatchQueryMission(string Mission, IReadOnlyList<HeaderPatch> Patches, IReadOnlyList<short>? FileHeaderWords);

internal sealed record PatchQueryResult(RetailGame Game, IReadOnlyList<PatchQueryMission> Missions);

/// <summary>
/// <c>patches</c>: every row-2 header patch, its condition and its ten header words. An ES1 patch's word 0 is the
/// theater and word 1 the zone (docs/es1/formats/msn-mission-file.md#header-patches); ES2's words are listed as they stand
/// (docs/retail/formats/msn-mission-file.md#the-header-patch--row-2).
/// </summary>
internal static class PatchQuery {
	/// <summary>An ES1 patch's words the load does not copy: 1 for word 0, 0 for the others.</summary>
	private static short UnsetValue(int word) => word == 0 ? (short)1 : (short)0;

	/// <summary>ES2's row-2 record: the condition, ten header words, then thirty flag indices.</summary>
	private const int Es2HeaderWords = 10;

	public static PatchQueryResult Run(RetailData data, IReadOnlyCollection<string> missions) {
		bool Named(string name) => missions.Count == 0 || missions.Contains(name, StringComparer.OrdinalIgnoreCase);
		if (data.Game == RetailGame.Earthsiege1) {
			return new PatchQueryResult(data.Game, data.Es1Missions.Where(m => Named(m.Name))
				.Select(m => new PatchQueryMission(m.Name, m.File.HeaderPatches
					.Select((p, i) => new HeaderPatch(i, p.ConditionRef, Conditions.Describe(m.File, p.ConditionRef), p.HeaderWords, []))
					.ToList(), m.File.HeaderWords))
				.ToList());
		}

		return new PatchQueryResult(data.Game, data.Missions.Where(m => Named(m.Name))
			.Select(m => new PatchQueryMission(m.Name, (m.File.SettingsPatches ?? [])
				.Select((p, i) => new HeaderPatch(i, p.Data[0], Conditions.Describe(m.File, p.Data[0]),
					p.Data.Skip(1).Take(Es2HeaderWords).ToList(), p.Data.Skip(1 + Es2HeaderWords).Where(f => f != -1).ToList()))
				.ToList(), null))
			.ToList());
	}

	public static void Write(TextWriter o, PatchQueryResult result) {
		bool es1 = result.Game == RetailGame.Earthsiege1;
		foreach (var m in result.Missions) {
			if (m.FileHeaderWords is { } words) {
				o.WriteLine($"{m.Mission}: other layout, no header-patch row; the file's own ten header words: {string.Join(" ", words)}");
				continue;
			}

			o.WriteLine($"{m.Mission}: {m.Patches.Count} header patch{(m.Patches.Count == 1 ? "" : "es")}");
			foreach (var p in m.Patches) {
				var line = new List<string> { $"#{p.Index}", p.ConditionRef == -1 ? "unconditioned" : TextReport.Cond(p.ConditionRef, p.Condition, true) };
				if (es1) {
					line.Add(p.Words[0] == UnsetValue(0) ? "theater unset (1)" : $"theater {p.Words[0]}");
					line.Add(p.Words[1] == UnsetValue(1) ? "zone unset (0)" : $"zone {p.Words[1]}");
					line.Add($"words 2-9: {string.Join(" ", p.Words.Skip(2))}");
				} else {
					line.Add($"header words {string.Join(" ", p.Words)}");
					if (p.ClearedFlags.Count > 0) {
						line.Add($"clears flags {string.Join(", ", p.ClearedFlags)}");
					}
				}

				o.WriteLine("  " + string.Join(", ", line));
			}
		}

		int otherLayout = result.Missions.Count(m => m.FileHeaderWords != null);
		o.WriteLine($"{result.Missions.Sum(m => m.Patches.Count)} header patch(es) in {result.Missions.Count} mission(s)" +
			(otherLayout > 0 ? $"; {otherLayout} in the other layout, which has no header-patch row." : "."));
	}
}
