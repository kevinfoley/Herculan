using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// Creates, edits and deletes the open mission's action timers, each edit one step of <see cref="PointMover"/>'s undo history,
/// through <see cref="TimerEditing"/>'s record edits on the mission's <c>.MSN</c>. Every edit builds the editor's state again
/// (<see cref="MissionRebuild"/>), which keeps the timer selected by its GUID: a create or a delete adds or removes a block-6
/// record, and what a timer starts on and fires is what the action view lists.
///
/// <para>Timers and actions are taken by their <c>script.dat</c> index, as the panels list them, and written as the GUIDs of
/// the <c>.MSN</c> records that created them. The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class TimerEditor {
	private const int ActionBlock = 5;
	private const int TimerBlock = 6;
	private const int TimerRow = 11;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public TimerEditor(MissionDocument source, PointMover mover, MissionRebuild rebuild, SelectionState selection) {
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="timer"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(int timer) =>
		Guid(timer) == -1 ? $"Timer {timer}'s record is not in the file." : null;

	/// <summary>Why no action can be added to <paramref name="timer"/>'s fired ones, or null when one can.</summary>
	public string? WhyNoAddFired(int timer) => WhyFixed(timer) ?? TimerEditing.WhyNoAddFired(_source, Guid(timer));

	/// <summary>Asks for a new timer, which is then selected.</summary>
	public void RequestCreate() {
		_requested = () => {
			short created = -1;
			return Commit(() => TimerEditing.Create(_source, out created), () => created);
		};
	}

	/// <summary>Asks for <paramref name="timer"/> to be deleted.</summary>
	public void RequestDelete(int timer) => Ask(timer, guid => TimerEditing.Delete(_source, guid));

	/// <summary>Asks for <paramref name="timer"/> to start when block-5 action <paramref name="action"/> fires, or from mission start for <c>-1</c>.</summary>
	public void RequestStart(int timer, int action) =>
		Ask(timer, guid => TimerEditing.SetStart(_source, guid, ActionGuid(action)));

	/// <summary>Asks for <paramref name="timer"/>'s delay to be <paramref name="seconds"/>.</summary>
	public void RequestDelay(int timer, int seconds) => Ask(timer, guid => TimerEditing.SetDelay(_source, guid, seconds));

	/// <summary>Asks for block-5 action <paramref name="action"/> to be added after <paramref name="timer"/>'s last fired one.</summary>
	public void RequestAddFired(int timer, int action) =>
		Ask(timer, guid => TimerEditing.AddFired(_source, guid, ActionGuid(action)));

	/// <summary>Asks for block-5 action <paramref name="action"/> in <paramref name="timer"/>'s fired slot <paramref name="slot"/>.</summary>
	public void RequestFired(int timer, int slot, int action) =>
		Ask(timer, guid => TimerEditing.SetFired(_source, guid, slot, ActionGuid(action)));

	/// <summary>Asks for the action in <paramref name="timer"/>'s fired slot <paramref name="slot"/> to be removed.</summary>
	public void RequestRemoveFired(int timer, int slot) => Ask(timer, guid => TimerEditing.RemoveFired(_source, guid, slot));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	private void Ask(int timer, Func<short, MsnEdit> edit) {
		short guid = Guid(timer);
		_requested = () => WhyFixed(timer) ?? Commit(() => edit(guid));
	}

	private short Guid(int timer) => _source.GuidOf(TimerBlock, timer);

	// The row-10 GUID of block-5 action <action>, or -1 for none; throws for one no record created.
	private short ActionGuid(int action) =>
		action < 0 ? (short)-1
			: _source.GuidOf(ActionBlock, action) is not -1 and var guid ? guid
			: throw new InvalidOperationException($"Action {action}'s record is not in the file.");

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the timer of row-11 GUID
	// <selected> where it is exported now, when one is given.
	private string? Commit(Func<MsnEdit> edit, Func<short>? selected = null) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		if (selected?.Invoke() is { } guid && guid != -1 && _source.ExportedIndex(TimerRow, guid) is { } exported) {
			_selection.Current = new TimerSelection(exported);
		}

		return null;
	}
}
