using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Creates, edits and deletes the open mission's random tables, and sets which table a herc, flyer, structure or ground
/// vehicle outside the squad picks from, or that it picks from none, each edit one step of <see cref="PointMover"/>'s undo history, through
/// <see cref="RandomTableEditing"/>'s record edits on the mission's <c>.MSN</c>. Every edit builds the editor's state again
/// (<see cref="SceneSync"/>), as it changes what the records picking from the table generate; the selection keeps a
/// table by its row-1 GUID and an object as the rebuild keeps one.
///
/// <para>The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class RandomTableEditor {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public RandomTableEditor(MissionDocument source, MissionIndex index, PointMover mover, SceneSync sync, SelectionState selection) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// The roster row and GUID of the record a placed object outside the squad stands for, from the layout; null for one of the
	/// squad.
	/// </summary>
	public (int Row, short Guid)? RecordOf(SceneObject placed) =>
		_index.Planned(placed) is { Squad: false } planned ? (planned.Key.Row, planned.Key.Guid) : null;

	/// <summary>Why the object of roster row <paramref name="row"/>'s GUID <paramref name="guid"/> cannot change table at all, or null when it can.</summary>
	public string? WhyNoTables(int row, short guid) => RandomTableEditing.WhyNoTables(_source, row, guid);

	/// <summary>Asks for a new table of the object's own, which then stays selected.</summary>
	public void RequestCreate(int row, short guid) =>
		Ask(() => WhyNoTables(row, guid), source => RandomTableEditing.CreateTable(source, row, guid, out _), null);

	/// <summary>Asks for the object to pick from <paramref name="table"/> (<see cref="RandomTableEditing.SetTable"/>).</summary>
	public void RequestTable(int row, short guid, short table) =>
		Ask(() => RandomTableEditing.WhyNoTable(_source, row, guid, table),
			source => RandomTableEditing.SetTable(source, row, guid, table), null);

	/// <summary>
	/// Asks for the object to pick from no table, keeping range <paramref name="range"/>'s option as its own
	/// (<see cref="RandomTableEditing.SetFixed"/>).
	/// </summary>
	public void RequestFixed(int row, short guid, short range) =>
		Ask(() => RandomTableEditing.WhyNoFixed(_source, row, guid, range),
			source => RandomTableEditing.SetFixed(source, row, guid, range), null);

	/// <summary>
	/// Asks for an option copying range <paramref name="copyOf"/>'s template to be added to <paramref name="table"/>, which is
	/// then selected when <paramref name="selectTable"/>; <paramref name="added"/> is told the new option's range.
	/// </summary>
	public void RequestAdd(short table, short copyOf, bool selectTable = true, Action<short>? added = null) =>
		Ask(() => RandomTableEditing.WhyNoAdd(_source, table), source => {
			var edit = RandomTableEditing.AddOption(source, table, copyOf, out short range);
			added?.Invoke(range);
			return edit;
		}, selectTable ? table : null);

	/// <summary>
	/// Asks for range <paramref name="range"/>'s option to be removed from <paramref name="table"/>, which is then selected
	/// when <paramref name="selectTable"/>.
	/// </summary>
	public void RequestRemove(short table, short range, bool selectTable = true) =>
		Ask(() => RandomTableEditing.WhyNoRemove(_source, table, range),
			source => RandomTableEditing.RemoveOption(source, table, range), selectTable ? table : null);

	/// <summary>
	/// Asks for <paramref name="table"/>'s chances to be <paramref name="percents"/>, one percent to one decimal per option;
	/// the table is then selected when <paramref name="selectTable"/>.
	/// </summary>
	public void RequestChances(short table, IReadOnlyList<decimal> percents, bool selectTable = true) =>
		Ask(() => RandomTableEditing.WhyNoChances(_source, table, percents),
			source => RandomTableEditing.SetChances(source, table, percents), selectTable ? table : null);

	/// <summary>Asks for <paramref name="table"/> to be deleted, with its ranges and templates.</summary>
	public void RequestDelete(short table) =>
		Ask(() => RandomTableEditing.WhyNoDelete(_source, table),
			source => RandomTableEditing.DeleteTable(source, table), -1);

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit>, refused by <why>: made once the frame is drawn, rebuilding from it and recorded as one step of the
	// history; <select> is the table to select afterwards, -1 for none, or null to keep the selection the rebuild keeps.
	private void Ask(Func<string?> why, Func<MissionDocument, MsnEdit> edit, short? select) =>
		_requested = () => {
			if (why() is { } reason) {
				return reason;
			}

			MsnEdit change;
			try {
				change = _sync.Run(() => edit(_source));
			} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
				return ex.Message;
			}

			_mover.Record(new RecordsEdit(_sync, change));
			if (select is { } table) {
				_selection.Current = table != -1 && RandomTableEditing.IsTable(_source, table) ? new ConditionSelection(table) : null;
			}

			return null;
		};
}
