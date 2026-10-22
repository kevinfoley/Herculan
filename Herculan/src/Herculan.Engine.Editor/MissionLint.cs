using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>One lint finding, with the selection that shows the record it is about, if the editor has one.</summary>
internal sealed record EditorFinding(ScriptLintFinding Finding, EditorSelection? Target);

/// <summary>
/// <see cref="ScriptDatLint"/> over the document's generated <c>script.dat</c> (<see cref="MissionDocument.Script"/>), each
/// finding tied to the selection that shows its record (docs/herculan/handoff-editor-viewing.md#mission-check): the
/// <c>.MSN</c> record that created the generated record it is about, through the generation's provenance
/// (<see cref="MissionDocument.GuidOf"/>, <see cref="ObjectiveEditing.RecordOf"/>), selected by its GUID or row-17 index.
/// </summary>
internal sealed class MissionLint {
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	/// <param name="index">The layout's groups and objects, which an order's and an object's finding is shown through.</param>
	/// <param name="squad">How many machines the squad brings into the generated mission (<see cref="SquadOf"/>).</param>
	public MissionLint(MissionDocument source, MissionIndex index, int squad) {
		Findings = ScriptDatLint.Check(source.Script, squad, source.Provenance.Lines.Count)
			.Select(finding => new EditorFinding(finding, TargetOf(finding, source, index)))
			.ToList();
	}

	public IReadOnlyList<EditorFinding> Findings { get; }

	/// <summary>
	/// How many machines the squad brings into <paramref name="generated"/>, the mission loaded from the generation, which
	/// share the herc roster's slot cap: those its <c>player.mec</c> places.
	/// </summary>
	public static int SquadOf(Mission generated) => generated.Placements.Count(placement => placement.IsPlayerLance);

	private static EditorSelection? TargetOf(ScriptLintFinding finding, MissionDocument source, MissionIndex index) {
		int i = finding.Index;
		if (i < 0) {
			return null;
		}

		if (finding.Block == ScriptBlock.Objectives) {
			return ObjectiveEditing.RecordOf(source, i) is { } record ? new ObjectiveSelection(record) : null;
		}

		short guid = finding.Block is >= ScriptBlock.Routes and <= ScriptBlock.Groups ? source.GuidOf((int)finding.Block, i) : (short)-1;
		if (guid == -1) {
			return null;
		}

		// An object finding selects the object the scene has for its record; a record no group places has none.
		EditorSelection? ObjectOf(int row) => index.ObjectOf(new ObjectKey(row, guid)) != null ? new ObjectSelection(row, guid) : null;

		return finding.Block switch {
			ScriptBlock.Routes => new RouteSelection(guid),
			ScriptBlock.TriggerAreas => new AreaSelection(guid),
			ScriptBlock.Actions => new ActionSelection(guid),
			ScriptBlock.ActionTimers => new TimerSelection(guid),
			ScriptBlock.Mechs => ObjectOf(MechRow),
			ScriptBlock.Flyers => ObjectOf(FlyerRow),
			ScriptBlock.Bases => ObjectOf(BaseRow),
			// An order is shown through the first group, in layout order, whose base names it in an order slot.
			ScriptBlock.Orders => index.Groups.FirstOrDefault(group => group.Orders.Contains(guid)) is { } group
				? new GroupSelection(group.Guid)
				: null,
			ScriptBlock.Groups => new GroupSelection(guid),
			_ => null
		};
	}
}
