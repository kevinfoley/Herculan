using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>One mission check finding, with the selection that shows the record it is about, if the editor has one.</summary>
internal sealed record EditorFinding(MissionCheckFinding Finding, EditorSelection? Target);

/// <summary>
/// <see cref="MissionCheck"/> over the document's <c>.MSN</c>, every version at once, each finding tied to the selection that
/// shows the record it names (docs/herculan/handoff-editor-viewing.md#mission-check): a route, area, trigger, countdown, group
/// or condition by its GUID, an objective by its row-17 index, a herc, flyer or structure as its object in the scene, and an
/// order as the first group, in layout order, whose orders reference it.
/// </summary>
internal sealed class MissionLint {
	private const int ConditionRow = 1;
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int MechRow = 12;
	private const int BaseRow = 14;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	/// <param name="index">The layout's groups and objects, which an order's and an object's finding is shown through.</param>
	public MissionLint(MissionDocument source, MissionIndex index) {
		// Two missing refs of one record to the same row, in other fields or to other GUIDs, read as one sentence
		// (ProblemText), so the second is left out.
		Findings = MissionCheck.Run(source.Layout, source.Mission)
			.DistinctBy(finding => (finding.Kind, finding.Row, finding.Key, finding.RefersTo, finding.InSomeVersions,
				finding.Kind == MissionCheckKind.MissingRecord ? 0 : finding.Value))
			.Select(finding => new EditorFinding(finding, TargetOf(finding, index)))
			.ToList();
	}

	public IReadOnlyList<EditorFinding> Findings { get; }

	private static EditorSelection? TargetOf(MissionCheckFinding finding, MissionIndex index) {
		short guid = (short)finding.Key;
		return finding.Row switch {
			ConditionRow => new ConditionSelection(guid),
			RouteRow => new RouteSelection(guid),
			AreaRow => new AreaSelection(guid),
			ActionRow => new ActionSelection(guid),
			TimerRow => new TimerSelection(guid),
			// A roster record no group places has no object in the scene, and a roster as a whole is no record.
			>= MechRow and <= BaseRow when finding.Key != -1 && index.ObjectOf(new ObjectKey(finding.Row, guid)) != null =>
				new ObjectSelection(finding.Row, guid),
			OrderRow => index.Groups.FirstOrDefault(group => group.Orders.Contains(guid)) is { } group
				? new GroupSelection(group.Guid)
				: null,
			GroupRow => new GroupSelection(guid),
			ObjectiveRow => new ObjectiveSelection(finding.Key),
			_ => null,
		};
	}
}
