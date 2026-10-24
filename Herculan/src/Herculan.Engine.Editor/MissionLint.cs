using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One lint finding, with the selection that shows the record it is about, if the editor has one.</summary>
internal sealed record EditorFinding(ScriptLintFinding Finding, EditorSelection? Target);

/// <summary>
/// <see cref="ScriptDatLint"/> over the opened file's document (<see cref="EditorMission.Document"/>), each finding
/// tied to the selection that shows its record.
/// </summary>
internal sealed class MissionLint {
	public MissionLint(MissionIndex index, ScriptDat script) {
		int squad = index.Mission.Placements.Count(placement => placement.IsPlayerSquad);
		Findings = ScriptDatLint.Check(script, squad)
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

		return finding.Block switch {
			ScriptBlock.Routes => index.Route(i) != null ? new RouteSelection(i) : null,
			ScriptBlock.TriggerAreas => index.Area(i) != null ? new AreaSelection(i) : null,
			ScriptBlock.Actions => new ActionSelection(i),
			ScriptBlock.ActionTimers => new TimerSelection(i),
			ScriptBlock.Mechs => ObjectOf(MissionUnitKind.Mech),
			ScriptBlock.Flyers => ObjectOf(MissionUnitKind.Flyer),
			ScriptBlock.Bases => ObjectOf(MissionUnitKind.Base),
			// An order is shown through the first group that names it.
			ScriptBlock.Orders => Array.FindIndex(script.Groups, group => group.OrderRefs.Contains((short)i)) is >= 0 and var g
				? new GroupSelection(g)
				: null,
			ScriptBlock.Groups => new GroupSelection(i),
			ScriptBlock.Objectives => new ObjectiveSelection(i),
			_ => null
		};
	}
}
