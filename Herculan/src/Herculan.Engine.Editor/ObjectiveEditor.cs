using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Creates, edits and deletes the open mission's objectives, each edit one step of <see cref="PointMover"/>'s undo
/// history, through <see cref="ObjectiveEditing"/>'s and <see cref="MissionTextEditing"/>'s record edits on the mission's
/// <c>.MSN</c> and <c>.ENG</c> (docs/herculan/handoff-editor-mission-logic.md#objectives). Every edit builds
/// the editor's state again (<see cref="MissionRebuild"/>): block 12 changes, a create or a delete adds or removes a
/// record, and a text edit changes <c>mission.str</c>.
///
/// <para>Objectives are taken by their block-12 index, as the panels list them, and written as the row-17 records that
/// were exported there; subjects and routes by their block index, written as the GUIDs of the records that created them.
/// The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>), since the rebuild changes what the
/// panels list.</para>
/// </summary>
internal sealed class ObjectiveEditor {
	private const int RouteBlock = 3;
	private const int MechBlock = 7;
	private const int BaseBlock = 9;
	private const int GroupBlock = 11;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public ObjectiveEditor(MissionDocument source, PointMover mover, MissionRebuild rebuild, SelectionState selection) {
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="objective"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(int objective) =>
		Record(objective) == null ? $"Objective {objective} is not in the file." : null;

	/// <summary>
	/// The subject an objective about <paramref name="selection"/> would have, as its kind (0 a group, 1 a herc, 3 a
	/// structure) and block index, or why the selection cannot be one.
	/// </summary>
	public (short Kind, int Index)? SubjectOf(EditorSelection? selection, out string? whyNot) {
		whyNot = null;
		switch (selection) {
			case GroupSelection { Group: var group }:
				return (ObjectiveEditing.GroupSubject, group);
			case ObjectSelection { Object.Placement: { Kind: MissionUnitKind.Mech } placement }:
				return (ObjectiveEditing.HercSubject, placement.SlotIndex);
			case ObjectSelection { Object.Placement: { Kind: MissionUnitKind.Base } placement }:
				return (ObjectiveEditing.StructureSubject, placement.SlotIndex);
			case ObjectSelection:
				whyNot = "A flyer is not offered as a subject: no retail objective asks about one.";
				return null;
			default:
				whyNot = "Select a group, a herc or a structure to be its subject.";
				return null;
		}
	}

	/// <summary>Why no objective can be created about <paramref name="selection"/>, or null when one can.</summary>
	public string? WhyNoCreate(EditorSelection? selection) {
		return SubjectOf(selection, out string? whyNot) is { } subject
			? ObjectiveEditing.WhyNoCreate(_source, subject.Kind, Guid(subject.Kind, subject.Index))
			: whyNot;
	}

	/// <summary>Asks for a new objective about <paramref name="selection"/>, which is then selected.</summary>
	public void RequestCreate(EditorSelection? selection) {
		if (SubjectOf(selection, out _) is not { } subject) {
			return;
		}

		_requested = () => {
			if (WhyNoCreate(selection) is { } reason) {
				return reason;
			}

			int record = -1;
			return Commit(() => ObjectiveEditing.Create(_source, subject.Kind, Guid(subject.Kind, subject.Index), out record),
				() => record);
		};
	}

	/// <summary>Why <paramref name="objective"/> cannot be deleted, or null when it can.</summary>
	public string? WhyNoDelete(int objective) =>
		WhyFixed(objective) ?? ObjectiveEditing.WhyNoDelete(_source, Record(objective)!.Value);

	/// <summary>Asks for <paramref name="objective"/> to be deleted.</summary>
	public void RequestDelete(int objective) => Ask(objective, record => ObjectiveEditing.Delete(_source, record), keep: false);

	/// <summary>Asks for <paramref name="objective"/> to be mandatory, or a failure condition.</summary>
	public void RequestRequired(int objective, bool mandatory) =>
		Ask(objective, record => ObjectiveEditing.SetRequired(_source, record, mandatory));

	/// <summary>Asks for <paramref name="objective"/>'s condition to be <paramref name="code"/>.</summary>
	public void RequestCondition(int objective, short code) =>
		Ask(objective, record => ObjectiveEditing.SetCondition(_source, record, code));

	/// <summary>Asks for <paramref name="objective"/>'s subject to be <paramref name="kind"/>'s record <paramref name="index"/>, by its block index.</summary>
	public void RequestSubject(int objective, short kind, int index) =>
		Ask(objective, record => ObjectiveEditing.SetSubject(_source, record, kind, Guid(kind, index)));

	/// <summary>Asks for <paramref name="objective"/>'s route to be block-3 route <paramref name="route"/>.</summary>
	public void RequestRoute(int objective, int route) =>
		Ask(objective, record => ObjectiveEditing.SetRoute(_source, record, Resolved(RouteBlock, route)));

	/// <summary>The routes, by block-3 index, condition 0 of <paramref name="objective"/> can be about (<see cref="ObjectiveEditing.RouteChoices"/>).</summary>
	public IReadOnlyList<int> RouteChoices(int objective) =>
		ObjectiveEditing.RouteChoices(_source, objective);

	/// <summary>Why no counter write can be added to <paramref name="objective"/>, or null when one can.</summary>
	public string? WhyNoAddPair(int objective) =>
		WhyFixed(objective) ?? ObjectiveEditing.WhyNoAddPair(_source, Record(objective)!.Value);

	/// <summary>Asks for a counter write after <paramref name="objective"/>'s last.</summary>
	public void RequestAddPair(int objective) => Ask(objective, record => ObjectiveEditing.AddPair(_source, record));

	/// <summary>Asks for <paramref name="objective"/>'s counter write <paramref name="slot"/> to be <paramref name="counter"/> and <paramref name="op"/>.</summary>
	public void RequestPair(int objective, int slot, short counter, short op) =>
		Ask(objective, record => ObjectiveEditing.SetPair(_source, record, slot, counter, op));

	/// <summary>Asks for <paramref name="objective"/>'s counter write <paramref name="slot"/> to be removed.</summary>
	public void RequestRemovePair(int objective, int slot) =>
		Ask(objective, record => ObjectiveEditing.RemovePair(_source, record, slot));

	/// <summary>
	/// The text of each line of <paramref name="objective"/>'s failure text as it is shown, or null for a line it has no text
	/// for in the editor; empty when it has no failure text.
	/// </summary>
	public IReadOnlyList<string?> FailureLines(int objective) {
		if (Record(objective) is not { } record) {
			return [];
		}

		return MissionTextEditing.IdsOf(_source, TextOwner.Objective(record))
			.Select(id => MissionTextEditing.LineText(_source, id))
			.ToList();
	}

	/// <summary>Whether another record references <paramref name="objective"/>'s failure text too, so an edit copies it first.</summary>
	public bool TextShared(int objective) =>
		Record(objective) is { } record && MissionTextEditing.Shared(_source, TextOwner.Objective(record));

	/// <summary>Asks for line <paramref name="line"/> of <paramref name="objective"/>'s failure text to read <paramref name="text"/>.</summary>
	public void RequestText(int objective, int line, string text) =>
		Ask(objective, record => MissionTextEditing.SetLine(_source, TextOwner.Objective(record), line, text));

	/// <summary>Why <paramref name="objective"/> cannot be given a failure text, or null when it can.</summary>
	public string? WhyNoGiveText(int objective) =>
		WhyFixed(objective) ?? ObjectiveEditing.WhyNoText(_source, Record(objective)!.Value);

	/// <summary>Asks for <paramref name="objective"/>, which has no failure text, to be given one of three empty lines.</summary>
	public void RequestGiveText(int objective) => Ask(objective, record => ObjectiveEditing.GiveText(_source, record));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of <objective>'s row-17 record once the frame is drawn; refused while it is fixed. The objective stays
	// selected unless <keep> is false.
	private void Ask(int objective, Func<int, MsnEdit> edit, bool keep = true) {
		_requested = () => {
			if (WhyFixed(objective) is { } reason) {
				return reason;
			}

			int record = Record(objective)!.Value;
			return Commit(() => edit(record), keep ? () => record : null);
		};
	}

	// The row-17 index of the record block-12 objective <objective> was exported from, or null.
	private int? Record(int objective) => ObjectiveEditing.RecordOf(_source, objective);

	// The GUID of the record that created <kind>'s block record <index>, or -1.
	private short Guid(short kind, int index) => _source.GuidOf(kind switch {
		ObjectiveEditing.GroupSubject => GroupBlock,
		ObjectiveEditing.HercSubject => MechBlock,
		_ => BaseBlock,
	}, index);

	// The GUID of the record that created block <block>'s record <index>; throws for one no record created.
	private short Resolved(int block, int index) =>
		_source.GuidOf(block, index) is not -1 and var guid ? guid
			: throw new InvalidOperationException($"Record {index} of block {block} is not in the file.");

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the objective row-17
	// record <selected> is exported at, when there is one.
	private string? Commit(Func<MsnEdit> edit, Func<int>? selected) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		if (selected != null && ObjectiveEditing.ExportedIndex(_source, selected()) is { } exported) {
			_selection.Current = new ObjectiveSelection(exported);
		}

		return null;
	}
}
