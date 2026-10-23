using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;

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
	public MissionLint(MissionDocument source, MissionIndex index) {
		Findings = Check(source)
			.Select(finding => new EditorFinding(finding, TargetOf(finding, source, index)))
			.ToList();
	}

	public IReadOnlyList<EditorFinding> Findings { get; }

	/// <summary>
	/// <see cref="ScriptDatLint"/> over <paramref name="source"/>'s generated <c>script.dat</c>, the squad's machines, which
	/// share the herc roster's slot cap, counted one per squad position its layout's group 0 gives
	/// (<see cref="SquadRoster.Positions"/>), as an add counts them (<see cref="HercGroups.WhyNoRoom"/>).
	/// </summary>
	public static List<ScriptLintFinding> Check(MissionDocument source) =>
		ScriptDatLint.Check(source.Script, SquadRoster.Positions(source.Layout), source.Provenance.Lines.Count);

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
