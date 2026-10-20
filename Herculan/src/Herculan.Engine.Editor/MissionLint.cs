using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One lint finding, with the selection that shows the record it is about, if the editor has one.</summary>
internal sealed record EditorFinding(ScriptLintFinding Finding, EditorSelection? Target);

/// <summary>
/// <see cref="ScriptDatLint"/> over the opened file's document (<see cref="EditorMission.Document"/>), each finding
/// tied to the selection that shows its record: the <c>.MSN</c> record that created the generated record it is about.
/// </summary>
internal sealed class MissionLint {
	/// <param name="textLines">How many lines the generated <c>mission.str</c> has.</param>
	public MissionLint(MissionIndex index, ScriptDat script, int textLines) {
		int squad = index.Mission.Placements.Count(placement => placement.IsPlayerLance);
		Findings = ScriptDatLint.Check(script, squad, textLines)
			.Select(finding => new EditorFinding(finding, TargetOf(finding, index, script)))
			.ToList();
	}

	public IReadOnlyList<EditorFinding> Findings { get; }

	private static EditorSelection? TargetOf(ScriptLintFinding finding, MissionIndex index, ScriptDat script) {
		int i = finding.Index;
		if (i < 0) {
			return null;
		}

		EditorSelection? ObjectOf(MissionUnitKind kind) =>
			index.ObjectAt(kind, i) is { } placed ? new ObjectSelection(placed) : null;

		var records = index.Records;
		return finding.Block switch {
			ScriptBlock.Routes => index.Route(i) != null ? records.RouteAt(i) : null,
			ScriptBlock.TriggerAreas => index.Area(i) != null ? records.AreaAt(i) : null,
			ScriptBlock.Actions => records.ActionAt(i),
			ScriptBlock.ActionTimers => records.TimerAt(i),
			ScriptBlock.Mechs => ObjectOf(MissionUnitKind.Mech),
			ScriptBlock.Flyers => ObjectOf(MissionUnitKind.Flyer),
			ScriptBlock.Bases => ObjectOf(MissionUnitKind.Base),
			// An order is shown through the first group that names it.
			ScriptBlock.Orders => Array.FindIndex(script.Groups, group => group.OrderRefs.Contains((short)i)) is >= 0 and var g
				? records.GroupAt(g)
				: null,
			ScriptBlock.Groups => records.GroupAt(i),
			ScriptBlock.Objectives => records.ObjectiveAt(i),
			_ => null
		};
	}
}
