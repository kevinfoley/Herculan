using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Creates, edits and deletes the open mission's objectives, each edit one step of <see cref="PointMover"/>'s undo
/// history, through <see cref="ObjectiveEditing"/>'s and <see cref="MissionTextEditing"/>'s record edits on the mission's
/// <c>.MSN</c> and <c>.ENG</c> (docs/herculan/handoff-editor-mission-logic.md#objectives). Every edit builds
/// the editor's state again (<see cref="SceneSync"/>): block 12 changes, a create or a delete adds or removes a
/// record, and a text edit changes <c>mission.str</c>.
///
/// <para>Objectives are taken by their index in row 17, as the selections name them, any record of the row, a conditional
/// one included; subjects and routes by their GUIDs. The edits wait for the frame's ImGui to be rendered
/// (<see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class ObjectiveEditor {
	private const int MechRow = 12;
	private const int BaseRow = 14;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public ObjectiveEditor(MissionDocument source, PointMover mover, SceneSync sync, SelectionState selection) {
		_source = source;
		_mover = mover;
		_sync = sync;
		_selection = selection;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose campaign variable uses the views' variable choices list.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-17 record <paramref name="record"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(int record) =>
		_source.Layout.Objectives.Any(objective => objective.Index == record) ? null : "That objective is not in the mission.";

	/// <summary>
	/// The subject an objective about <paramref name="selection"/> would have, as its kind (0 a group, 1 a herc, 3 a
	/// structure) and GUID, or why the selection cannot be one.
	/// </summary>
	public (short Kind, short Guid)? SubjectOf(EditorSelection? selection, out string? whyNot) {
		whyNot = null;
		switch (selection) {
			case GroupSelection { Guid: var group }:
				return (ObjectiveEditing.GroupSubject, group);
			case ObjectSelection { Row: MechRow, Guid: var herc }:
				return (ObjectiveEditing.HercSubject, herc);
			case ObjectSelection { Row: BaseRow, Guid: var structure }:
				return (ObjectiveEditing.StructureSubject, structure);
			case ObjectSelection:
				whyNot = "A flyer is not offered as a subject: no retail objective asks about one.";
				return null;
			default:
				whyNot = "Select a group, a HERC or a structure to be its subject.";
				return null;
		}
	}

	/// <summary>Why no objective can be created about <paramref name="selection"/>, or null when one can.</summary>
	public string? WhyNoCreate(EditorSelection? selection) {
		return SubjectOf(selection, out string? whyNot) is { } subject
			? ObjectiveEditing.WhyNoCreate(_source, subject.Kind, subject.Guid)
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
			return Commit(() => ObjectiveEditing.Create(_source, subject.Kind, subject.Guid, out record), () => record);
		};
	}

	/// <summary>Why row-17 record <paramref name="record"/> cannot be deleted, or null when it can.</summary>
	public string? WhyNoDelete(int record) => WhyFixed(record) ?? ObjectiveEditing.WhyNoDelete(_source, record);

	/// <summary>Asks for row-17 record <paramref name="record"/> to be deleted; a selection of it is cleared.</summary>
	public void RequestDelete(int record) =>
		_requested = () => WhyNoDelete(record) ?? Commit(() => ObjectiveEditing.Delete(_source, record), null, deleted: record);

	/// <summary>Asks for <paramref name="record"/> to be mandatory, or a failure condition.</summary>
	public void RequestRequired(int record, bool mandatory) =>
		Ask(record, () => ObjectiveEditing.SetRequired(_source, record, mandatory));

	/// <summary>Asks for <paramref name="record"/>'s condition to be <paramref name="code"/>.</summary>
	public void RequestCondition(int record, short code) => Ask(record, () => ObjectiveEditing.SetCondition(_source, record, code));

	/// <summary>Asks for <paramref name="record"/>'s subject to be <paramref name="kind"/>'s record of GUID <paramref name="subject"/>.</summary>
	public void RequestSubject(int record, short kind, short subject) =>
		Ask(record, () => ObjectiveEditing.SetSubject(_source, record, kind, subject));

	/// <summary>Asks for <paramref name="record"/>'s route to be row-8 GUID <paramref name="route"/>.</summary>
	public void RequestRoute(int record, short route) => Ask(record, () => ObjectiveEditing.SetRoute(_source, record, route));

	/// <summary>
	/// The routes, by row-8 GUID, condition 0 of row-17 record <paramref name="record"/> can be about
	/// (<see cref="ObjectiveEditing.RouteChoices"/>); none when the row has no such record.
	/// </summary>
	public IReadOnlyList<short> RouteChoices(int record) => WhyFixed(record) == null ? ObjectiveEditing.RouteChoices(_source, record) : [];

	/// <summary>Why no counter write can be added to <paramref name="record"/>, or null when one can.</summary>
	public string? WhyNoAddPair(int record) => WhyFixed(record) ?? ObjectiveEditing.WhyNoAddPair(_source, record);

	/// <summary>Asks for a counter write after <paramref name="record"/>'s last.</summary>
	public void RequestAddPair(int record) => Ask(record, () => ObjectiveEditing.AddPair(_source, record));

	/// <summary>Asks for <paramref name="record"/>'s counter write <paramref name="slot"/> to be <paramref name="counter"/> and <paramref name="op"/>.</summary>
	public void RequestPair(int record, int slot, short counter, short op) =>
		Ask(record, () => ObjectiveEditing.SetPair(_source, record, slot, counter, op));

	/// <summary>Asks for <paramref name="record"/>'s counter write <paramref name="slot"/> to be removed.</summary>
	public void RequestRemovePair(int record, int slot) => Ask(record, () => ObjectiveEditing.RemovePair(_source, record, slot));

	/// <summary>
	/// The text of each line of <paramref name="record"/>'s failure text as it is shown, or null for a line it has no text
	/// for in the editor; empty when it has no failure text or the row has no such record.
	/// </summary>
	public IReadOnlyList<string?> FailureLines(int record) {
		if (WhyFixed(record) != null) {
			return [];
		}

		return MissionTextEditing.IdsOf(_source, TextOwner.Objective(record))
			.Select(id => MissionTextEditing.LineText(_source, id))
			.ToList();
	}

	/// <summary>Asks for line <paramref name="line"/> of <paramref name="record"/>'s failure text to read <paramref name="text"/>.</summary>
	public void RequestText(int record, int line, string text) =>
		Ask(record, () => MissionTextEditing.SetLine(_source, TextOwner.Objective(record), line, text));

	/// <summary>Why <paramref name="record"/> cannot be given a failure text, or null when it can.</summary>
	public string? WhyNoGiveText(int record) => WhyFixed(record) ?? ObjectiveEditing.WhyNoText(_source, record);

	/// <summary>Asks for <paramref name="record"/>, which has no failure text, to be given one of three empty lines.</summary>
	public void RequestGiveText(int record) => Ask(record, () => ObjectiveEditing.GiveText(_source, record));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of row-17 record <record> once the frame is drawn; refused while it is fixed. The objective stays
	// selected.
	private void Ask(int record, Func<MsnEdit> edit) => _requested = () => WhyFixed(record) ?? Commit(edit, () => record);

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the objective of row-17
	// record <selected>, when one is given; a selection of the <deleted> record is cleared, as the record after it now has
	// its index.
	private string? Commit(Func<MsnEdit> edit, Func<int>? selected, int? deleted = null) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		if (selected != null) {
			_selection.Current = new ObjectiveSelection(selected());
		} else if (deleted is { } record && _selection.Current is ObjectiveSelection { Record: var current } && current == record) {
			_selection.Current = null;
		}

		return null;
	}
}
