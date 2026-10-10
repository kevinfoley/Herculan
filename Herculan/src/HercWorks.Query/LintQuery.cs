using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Query;

internal sealed record LintQueryMission(string Mission, IReadOnlyList<ScriptLintFinding> Findings);

/// <summary>
/// <c>lint</c>: <see cref="ScriptDatLint"/> over the <c>script.dat</c> each mission's load writes, built
/// by <see cref="MissionGenerator"/> with every campaign flag zero and every roll 0 — one variant per
/// mission, as the engine's training launch builds it. The squad is not known, so the block-7 slot
/// cap is checked against the roster alone.
/// </summary>
internal static class LintQuery {
	public static IReadOnlyList<LintQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new LintQueryMission(m.Name, Check(m)))
			.ToList();

	private static IReadOnlyList<ScriptLintFinding> Check(Mission mission) {
		var generator = MissionGenerator.Load(mission.MsnBytes, mission.EngBytes,
			new short[MissionGenerator.CampaignFlagCount], new short[MissionGenerator.ClearListLength], _ => 0);
		var script = new ScriptDatTransformer().Parse(generator.WriteScriptDat()) as ScriptDat
			?? throw new InvalidDataException($"{mission.Name}'s script.dat did not parse.");
		return ScriptDatLint.Check(script);
	}

	public static void Write(TextWriter output, IReadOnlyList<LintQueryMission> missions) {
		int total = 0;
		foreach (var mission in missions) {
			foreach (var finding in mission.Findings) {
				output.WriteLine($"{mission.Mission}  block {(int)finding.Block}  {finding.Message}");
				total++;
			}
		}

		output.WriteLine($"{total} finding(s) across {missions.Count} mission(s).");
	}
}
