using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// Edits the open mission's objective, briefing and intelligence lines (row 4) and their text, each edit one step of
/// <see cref="PointMover"/>'s undo history, through <see cref="MissionTextEditing"/>'s record edits on the mission's
/// <c>.MSN</c> and <c>.ENG</c> (docs/herculan/handoff-editor-mission-logic.md#the-missions-text). Every
/// edit builds the editor's state again (<see cref="SceneSync"/>), which reloads <c>mission.str</c> and block 13.
/// The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).
/// </summary>
internal sealed class MissionTextEditor {
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public MissionTextEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <inheritdoc cref="MissionTextEditing.Lines"/>
	public int LineCount => MissionTextEditing.Lines(_source);

	/// <summary><paramref name="list"/>'s filled slots, with the id each references and its text, null for an id with no text in this state.</summary>
	public IReadOnlyList<(int Slot, short Id, string? Text)> Lines(MissionTextList list) =>
		MissionTextEditing.Filled(_source, list)
			.Select(entry => (entry.Slot, entry.Id, MissionTextEditing.LineText(_source, entry.Id)))
			.ToList();

	/// <summary>Why no line can be added to <paramref name="list"/>, or null when one can.</summary>
	public string? WhyNoAdd(MissionTextList list) => MissionTextEditing.WhyNoAdd(_source, list);

	/// <summary>Asks for an empty line after <paramref name="list"/>'s last.</summary>
	public void RequestAdd(MissionTextList list) =>
		Ask(() => WhyNoAdd(list) ?? Commit(() => MissionTextEditing.AddLine(_source, list, out _)));

	/// <summary>Asks for the line in <paramref name="list"/>'s slot <paramref name="slot"/> to be removed, the later lines moving up.</summary>
	public void RequestRemove(MissionTextList list, int slot) =>
		Ask(() => Commit(() => MissionTextEditing.RemoveLine(_source, list, slot)));

	/// <summary>Asks for the lines in <paramref name="list"/>'s slots <paramref name="slot"/> and <paramref name="to"/> to trade places.</summary>
	public void RequestMove(MissionTextList list, int slot, int to) =>
		Ask(() => Commit(() => MissionTextEditing.MoveLine(_source, list, slot, to)));

	/// <summary>Asks for the line in <paramref name="list"/>'s slot <paramref name="slot"/> to read <paramref name="text"/>.</summary>
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

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	}
}
