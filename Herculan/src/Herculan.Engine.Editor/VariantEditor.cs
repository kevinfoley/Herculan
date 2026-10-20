using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Creates, edits and deletes the open mission's variant tables, and sets which table a herc, flyer, structure or ground
/// vehicle outside the squad draws from, each edit one step of <see cref="PointMover"/>'s undo history, through
/// <see cref="VariantEditing"/>'s record edits on the mission's <c>.MSN</c>. Every edit builds the editor's state again
/// (<see cref="SceneSync"/>), as it changes what the records drawing from the table generate; the selection keeps a
/// table by its row-1 GUID and an object as the rebuild keeps one.
///
/// <para>The edits wait for the frame's ImGui to be rendered (<see cref="RunRequest"/>).</para>
/// </summary>
internal sealed class VariantEditor {
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public VariantEditor(MissionDocument source, PointMover mover, SceneSync sync, SelectionState selection) {
		_source = source;
		_mover = mover;
		_sync = sync;
		_selection = selection;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="table"/> is shown without being edited, or null when it is edited.</summary>
	public string? WhyFixed(VariantTable table) => table.WhyFixed;

	/// <summary>The roster row and GUID of a placed object outside the squad, or null for one of the squad.</summary>
	public (int Row, short Guid)? RecordOf(SceneObject placed) {
		var placement = placed.Placement;
		if (placement.IsPlayerLance) {
			return null;
		}

		int block = placement.Kind switch { MissionUnitKind.Mech => 7, MissionUnitKind.Flyer => 8, _ => 9 };
		short guid = _source.GuidOf(block, placement.SlotIndex);
		return guid == -1 ? null : (block + 5, guid);
	}

	/// <summary>Why the object of roster row <paramref name="row"/>'s GUID <paramref name="guid"/> cannot change table at all, or null when it can.</summary>
	public string? WhyNoTables(int row, short guid) => VariantEditing.WhyNoTables(_source, row, guid);

	/// <summary>Asks for a new table of the object's own, which then stays selected.</summary>
	public void RequestCreate(int row, short guid) =>
		Ask(() => WhyNoTables(row, guid), source => VariantEditing.CreateTable(source, row, guid, out _), null);

	/// <summary>Asks for the object to draw from <paramref name="table"/>, or from none for <c>-1</c>.</summary>
	public void RequestTable(int row, short guid, short table) =>
		Ask(() => VariantEditing.WhyNoTable(_source, row, guid, table),
			source => VariantEditing.SetTable(source, row, guid, table), null);

	/// <summary>Asks for an alternative copying range <paramref name="copyOf"/>'s to be added to <paramref name="table"/>.</summary>
	public void RequestAdd(short table, short copyOf) =>
		Ask(() => VariantEditing.WhyNoAdd(_source, table),
			source => VariantEditing.AddAlternative(source, table, copyOf, out _), table);

	/// <summary>Asks for range <paramref name="range"/>'s alternative to be removed from <paramref name="table"/>.</summary>
	public void RequestRemove(short table, short range) =>
		Ask(() => VariantEditing.WhyNoRemove(_source, table, range),
			source => VariantEditing.RemoveAlternative(source, table, range), table);

	/// <summary>Asks for <paramref name="table"/>'s chances to be <paramref name="percents"/>, one per alternative.</summary>
	public void RequestChances(short table, IReadOnlyList<int> percents) =>
		Ask(() => VariantEditing.WhyNoChances(_source, table, percents),
			source => VariantEditing.SetChances(source, table, percents), table);

	/// <summary>Asks for <paramref name="table"/> to be deleted, with its ranges and alternatives.</summary>
	public void RequestDelete(short table) =>
		Ask(() => VariantEditing.WhyNoDelete(_source, table),
			source => VariantEditing.DeleteTable(source, table), -1);

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
				_selection.Current = table != -1 && VariantEditing.IsTable(_source, table) ? new ConditionSelection(table) : null;
			}

			return null;
		};
}
