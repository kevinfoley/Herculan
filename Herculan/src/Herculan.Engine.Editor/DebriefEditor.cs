using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// Edits the open mission's debrief text (row 5's lines), each edit one step of <see cref="PointMover"/>'s undo history,
/// through <see cref="MissionTextEditing.SetLineUnder"/>'s record edits on the mission's <c>.ENG</c>
/// (docs/herculan/editor-design.md#the-missions-text). Every edit builds the editor's state again
/// (<see cref="SceneSync"/>). The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).
/// </summary>
internal sealed class DebriefEditor {
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	/// <param name="campaign">Whether the mission is played as a campaign mission, the one kind whose debrief reloads its text.</param>
	public DebriefEditor(MissionDocument source, PointMover mover, SceneSync sync, Func<bool> campaign) {
		Source = source;
		_mover = mover;
		_sync = sync;
		Campaign = campaign;
	}

	public MissionDocument Source { get; }

	/// <summary>Whether the mission is played as a campaign mission.</summary>
	public Func<bool> Campaign { get; }

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// Asks for debrief line <paramref name="line"/> to read <paramref name="text"/> where <paramref name="condition"/> holds,
	/// its record under that condition, <c>-1</c> for its unconditioned text.
	/// </summary>
	public void RequestText(DebriefLine line, short condition, string text) =>
		Ask(() => DebriefText.Record(Source) is { } record
			? Commit(() => MissionTextEditing.SetLineUnder(Source, new TextOwner(5, record, null, line.Slot), 0, condition, text))
			: "The mission has no debrief.");

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
