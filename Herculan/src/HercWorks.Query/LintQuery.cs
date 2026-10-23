using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Query;

/// <summary>One mission's findings: <see cref="MissionCheckFinding"/>s, or with <c>--generated</c> <see cref="ScriptLintFinding"/>s.</summary>
internal sealed record LintQueryMission(string Mission, IReadOnlyList<object> Findings);

/// <summary>
/// <c>lint</c>: <see cref="MissionCheck"/> over each mission's <c>.MSN</c> and <c>.ENG</c>, every version at once, each finding
/// marked when only some versions have it. <c>lint --generated</c>: <see cref="ScriptDatLint"/> over the <c>script.dat</c>
/// each mission's load writes instead, built by <see cref="MissionGenerator"/> with every campaign flag zero and every roll 0
/// — one version per mission, as the engine's training launch builds it — and the length of the <c>mission.str</c> it writes
/// beside it; the squad is not known there, so the block-7 slot cap is checked against the roster alone.
/// </summary>
internal static class LintQuery {
	public static IReadOnlyList<LintQueryMission> Run(RetailData data, IReadOnlyCollection<string> missions, bool generated) =>
		data.Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new LintQueryMission(m.Name, generated ? Generated(m) : Check(m)))
			.ToList();

	private static IReadOnlyList<object> Check(Mission mission) =>
		MissionCheck.Run(MissionLayout.Build(mission.File, mission.Text), mission.File).ToList<object>();

	private static IReadOnlyList<object> Generated(Mission mission) {
		var generator = MissionGenerator.Load(mission.MsnBytes, mission.EngBytes,
			new short[MissionGenerator.CampaignFlagCount], new short[MissionGenerator.ClearListLength], _ => 0);
		var script = new ScriptDatTransformer().Parse(generator.WriteScriptDat()) as ScriptDat
			?? throw new InvalidDataException($"{mission.Name}'s script.dat did not parse.");
		return ScriptDatLint.Check(script, textLines: generator.TextLines.Count).ToList<object>();
	}

	public static void Write(TextWriter output, IReadOnlyList<LintQueryMission> missions) {
		int total = 0;
		foreach (var mission in missions) {
			foreach (var finding in mission.Findings) {
				string line = finding switch {
					MissionCheckFinding { InSomeVersions: true } found => $"{found.Message} (some versions)",
					MissionCheckFinding found => found.Message,
					ScriptLintFinding found => $"block {(int)found.Block}  {found.Message}",
					_ => finding.ToString() ?? "",
				};
				output.WriteLine($"{mission.Mission}  {line}");
				total++;
			}
		}

		output.WriteLine($"{total} finding(s) across {missions.Count} mission(s).");
	}
}
