using HercWorks.Core.Data.File.Msn;

namespace Herculan.Es2.Editor;

/// <summary>
/// Edits the open mission's objective, briefing and intelligence lines (row 4) and their text, each edit one step of
/// the <see cref="EditHistory">undo history</see>, through <see cref="MissionTextEditing"/>'s record edits on the mission's
/// <c>.MSN</c> and <c>.ENG</c> (docs/herculan/editor-design.md#the-missions-text). Every
/// edit builds the editor's state again (<see cref="SceneSync"/>).
/// The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).
/// </summary>
internal sealed class MissionTextEditor {
	private readonly MissionDocument _source;
	private readonly EditHistory _history;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public MissionTextEditor(MissionDocument source, EditHistory history, SceneSync sync) {
		_source = source;
		_history = history;
		_sync = sync;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <inheritdoc cref="MissionTextEditing.Lines"/>
	public int LineCount => MissionTextEditing.Lines(_source);

	/// <summary>A list's filled slots, with the id each references and its text, null for an id with no text in this state.</summary>
	/// <param name="list">The list.</param>
	public IReadOnlyList<(int Slot, short Id, string? Text)> Lines(MissionTextList list) =>
		MissionTextEditing.Filled(_source, list)
			.Select(entry => (entry.Slot, entry.Id, MissionTextEditing.LineText(_source, entry.Id)))
			.ToList();

	/// <summary>Why no line can be added to a list, or null when one can.</summary>
	/// <param name="list">The list.</param>
	public string? WhyNoAdd(MissionTextList list) => MissionTextEditing.WhyNoAdd(_source, list);

	/// <summary>Asks for an empty line after a list's last.</summary>
	/// <param name="list">The list.</param>
	public void RequestAdd(MissionTextList list) =>
		Ask(() => WhyNoAdd(list) ?? Commit(() => MissionTextEditing.AddLine(_source, list, out _)));

	/// <summary>Asks for the line in a list's slot to be removed, the later lines moving up.</summary>
	/// <param name="list">The list.</param>
	/// <param name="slot">The slot.</param>
	public void RequestRemove(MissionTextList list, int slot) =>
		Ask(() => Commit(() => MissionTextEditing.RemoveLine(_source, list, slot)));

	/// <summary>Asks for the lines in two of a list's slots to trade places.</summary>
	/// <param name="list">The list.</param>
	/// <param name="slot">One slot.</param>
	/// <param name="to">The other slot.</param>
	public void RequestMove(MissionTextList list, int slot, int to) =>
		Ask(() => Commit(() => MissionTextEditing.MoveLine(_source, list, slot, to)));

	/// <summary>Asks for the line in a list's slot to read a text.</summary>
	/// <param name="list">The list.</param>
	/// <param name="slot">The slot.</param>
	/// <param name="text">The text.</param>
	public void RequestText(MissionTextList list, int slot, string text) =>
		Ask(() => MissionTextEditing.PackageRecord(_source) is { } record
			? Commit(() => MissionTextEditing.SetLine(_source, TextOwner.Package(record, list, slot), 0, text))
			: "The mission has no text package.");

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	private void Ask(Func<string?> edit) => _requested = edit;

	// Makes the change, rebuilding from it, and records it as one step of the history.
	private string? Commit(Func<MsnEdit> edit) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_history.Record(new RecordsEdit(_sync, change));
		return null;
	}
}
