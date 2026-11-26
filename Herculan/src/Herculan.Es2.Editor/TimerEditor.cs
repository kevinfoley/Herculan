using HercWorks.Core.Data.File.Msn;

namespace Herculan.Es2.Editor;

/// <summary>
/// Creates, edits and deletes the open mission's countdowns (row-11 action timers), each edit one step of
/// the <see cref="EditHistory">undo history</see>, through <see cref="TimerEditing"/>'s record edits on the mission's <c>.MSN</c>.
/// Every edit builds the editor's state again (<see cref="SceneSync"/>), so the trigger view lists what a countdown starts on
/// and fires as it now stands.
///
/// <para>Countdowns and triggers are taken by their GUIDs, as the selections and the views' choices name them, and any
/// countdown the layout has is edited. The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class TimerEditor {
	private const int TimerRow = 11;

	private readonly MissionDocument _source;
	private readonly EditHistory _history;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public TimerEditor(MissionDocument source, EditHistory history, SceneSync sync, SelectionState selection) {
		_source = source;
		_history = history;
		_sync = sync;
		_selection = selection;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose countdowns the countdown view reads.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why a row-11 countdown cannot be edited, or null when it can.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	public string? WhyFixed(short timer) => _source.Layout.Entry(TimerRow, timer) == null ? "That countdown is not in the mission." : null;

	/// <summary>Why no trigger can be added to a countdown's fired ones, or null when one can.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	public string? WhyNoAddFired(short timer) => WhyFixed(timer) ?? TimerEditing.WhyNoAddFired(_source, timer);

	/// <summary>Asks for a new timer, which is then selected.</summary>
	public void RequestCreate() {
		_requested = () => {
			short created = -1;
			return Commit(() => TimerEditing.Create(_source, out created), () => created);
		};
	}

	/// <summary>Asks for a countdown to be deleted.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	public void RequestDelete(short timer) => Ask(timer, () => TimerEditing.Delete(_source, timer));

	/// <summary>Asks for a countdown to start when a row-10 trigger fires, or from mission start for <c>-1</c>.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	/// <param name="action">The trigger's row-10 GUID, or <c>-1</c> for mission start.</param>
	public void RequestStart(short timer, short action) => Ask(timer, () => TimerEditing.SetStart(_source, timer, action));

	/// <summary>Asks for a countdown's delay to change.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	/// <param name="seconds">The delay in seconds.</param>
	public void RequestDelay(short timer, int seconds) => Ask(timer, () => TimerEditing.SetDelay(_source, timer, seconds));

	/// <summary>Asks for a row-10 trigger to be added after a countdown's last fired one.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	/// <param name="action">The trigger's row-10 GUID.</param>
	public void RequestAddFired(short timer, short action) => Ask(timer, () => TimerEditing.AddFired(_source, timer, action));

	/// <summary>Asks for a new trigger after a countdown's last fired one (<see cref="DelayedActions.AddNew"/>), staying on the countdown.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	public void RequestNewFired(short timer) => Ask(timer, () => DelayedActions.AddNew(_source, timer, out _));

	/// <summary>Asks for a row-10 trigger in a countdown's fired slot.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	/// <param name="slot">The fired slot.</param>
	/// <param name="action">The trigger's row-10 GUID.</param>
	public void RequestFired(short timer, int slot, short action) =>
		Ask(timer, () => TimerEditing.SetFired(_source, timer, slot, action));

	/// <summary>Asks for the trigger in a countdown's fired slot to be removed.</summary>
	/// <param name="timer">The countdown's row-11 GUID.</param>
	/// <param name="slot">The fired slot.</param>
	public void RequestRemoveFired(short timer, int slot) => Ask(timer, () => TimerEditing.RemoveFired(_source, timer, slot));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	private void Ask(short timer, Func<MsnEdit> edit) => _requested = () => WhyFixed(timer) ?? Commit(edit);

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the timer of row-11 GUID
	// <selected>, when one is given.
	private string? Commit(Func<MsnEdit> edit, Func<short>? selected = null) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_history.Record(new RecordsEdit(_sync, change));
		if (selected?.Invoke() is { } guid && guid != -1) {
			_selection.Current = new TimerSelection(guid);
		}

		return null;
	}
}
