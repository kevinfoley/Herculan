using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// What keeps an action from being deleted (<see cref="ActionEditor.BlockersOf"/>), by block index: the timers that start on
/// it or fire it, the groups waiting on it, the objects firing it when engaged or defeated, as roster kind and slot, and the
/// order slots it ends. <see cref="Elsewhere"/> counts the referencing records not shown in the editor.
/// </summary>
internal sealed record ActionDeleteBlockers(string? Fixed, IReadOnlyList<int> Timers, IReadOnlyList<int> Groups,
		IReadOnlyList<(MissionUnitKind Kind, int Slot)> Objects, IReadOnlyList<OrderSlot> Orders, int Elsewhere) {
	/// <summary>Whether nothing does, and the action can be deleted.</summary>
	public bool None => Fixed == null && Timers.Count == 0 && Groups.Count == 0 && Objects.Count == 0 && Orders.Count == 0
		&& Elsewhere == 0;

	public static ActionDeleteBlockers Because(string reason) => new(reason, [], [], [], [], 0);
}

/// <summary>
/// Creates, edits and deletes the open mission's actions, and wires them into the groups waiting on them and the objects
/// firing them, each edit one step of <see cref="PointMover"/>'s undo history, through <see cref="ActionEditing"/>'s and
/// <see cref="ActionWiring"/>'s record edits on the mission's <c>.MSN</c>. Every edit builds the editor's state again
/// (<see cref="SceneSync"/>): an action's areas decide how the overlay colours each area, its verb how its waiting
/// groups arrive, and a create or a delete adds or removes a block-5 record.
///
/// <para>Actions, areas and groups are taken by their GUIDs, as the selections and the views' choices name them, and an
/// action not shown in the editor is not edited. The edits wait for the frame's ImGui to be rendered
/// (<see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class ActionEditor {
	private readonly MissionDocument _source;
	private readonly GeneratedRecords _records;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public ActionEditor(MissionDocument source, GeneratedRecords records, PointMover mover, SceneSync sync,
			SelectionState selection, ObjectDeleter deleter) {
		_source = source;
		_records = records;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-10 GUID <paramref name="action"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(short action) => _records.Action(action) == null ? GeneratedRecords.NotShown : null;

	/// <summary>Why <paramref name="action"/> cannot be made type <paramref name="type"/>, or null when it can.</summary>
	public string? WhyNoType(short action, short type) => WhyFixed(action) ?? ActionEditing.WhyNoType(_source, action, type);

	/// <summary>Why no area can be added to <paramref name="action"/>, or null when one can.</summary>
	public string? WhyNoAddArea(short action) => WhyFixed(action) ?? ActionEditing.WhyNoAddArea(_source, action, -1);

	/// <summary>Why no counter write can be added to <paramref name="action"/>, or null when one can.</summary>
	public string? WhyNoAddPair(short action) => WhyFixed(action) ?? ActionEditing.WhyNoAddPair(_source, action);

	/// <summary>What keeps <paramref name="action"/> from being deleted; <see cref="ActionDeleteBlockers.None"/> when nothing does.</summary>
	public ActionDeleteBlockers BlockersOf(short action) {
		if (WhyFixed(action) is { } reason) {
			return ActionDeleteBlockers.Because(reason);
		}

		var source = _source;
		var namers = ActionEditing.Namers(source, action);
		int elsewhere = 0;
		List<int> Exported(IEnumerable<MsnRecordId> records) {
			var indices = new List<int>();
			foreach (var record in records) {
				if (source.Provenance.ExportedSlot(record) is { } slot) {
					indices.Add(slot.Index);
				} else {
					elsewhere++;
				}
			}

			return indices;
		}

		var timers = Exported(namers.Timers);
		var groups = Exported(namers.Groups);
		var objects = new List<(MissionUnitKind, int)>();
		foreach (var kind in new[] { MissionUnitKind.Mech, MissionUnitKind.Flyer, MissionUnitKind.Base }) {
			int row = RosterBlock(kind) + 5;
			objects.AddRange(Exported(namers.Objects.Where(record => record.Row == row)).Select(slot => (kind, slot)));
		}

		var orders = _deleter.Resolve(source, new MsnNamers([], [], namers.Orders));
		return new ActionDeleteBlockers(null, timers.Distinct().ToList(), groups.Distinct().ToList(), objects.Distinct().ToList(),
			orders.Orders, elsewhere + orders.Elsewhere);
	}

	/// <summary>Asks for a new action, testing row-9 GUID <paramref name="area"/> or none for <c>-1</c>, which is then selected.</summary>
	public void RequestCreate(short area = -1) {
		_requested = () => {
			short created = -1;
			return Commit(() => ActionEditing.Create(_source, area, out created), () => created);
		};
	}

	/// <summary>Asks for <paramref name="action"/> to be deleted.</summary>
	public void RequestDelete(short action) {
		_requested = () => {
			var blockers = BlockersOf(action);
			if (!blockers.None) {
				return blockers.Fixed ?? "Something references it; its view says what.";
			}

			return Commit(() => ActionEditing.Delete(_source, action));
		};
	}

	/// <summary>Asks for <paramref name="action"/>'s type to be <paramref name="type"/>.</summary>
	public void RequestType(short action, short type) => Ask(action, () => ActionEditing.SetType(_source, action, type));

	/// <summary>Asks for <paramref name="action"/>'s target to be the record of GUID <paramref name="target"/> in the row its type names.</summary>
	public void RequestTarget(short action, short target) => Ask(action, () => ActionEditing.SetTarget(_source, action, target));

	/// <summary>Asks for <paramref name="action"/>'s verb to be <paramref name="verb"/>.</summary>
	public void RequestVerb(short action, short verb) => Ask(action, () => ActionEditing.SetVerb(_source, action, verb));

	/// <summary>Asks for row-9 GUID <paramref name="area"/> to be added after <paramref name="action"/>'s last.</summary>
	public void RequestAddArea(short action, short area) => Ask(action, () => ActionEditing.AddArea(_source, action, area));

	/// <summary>Asks for row-9 GUID <paramref name="area"/> in <paramref name="action"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestArea(short action, int slot, short area) =>
		Ask(action, () => ActionEditing.SetArea(_source, action, slot, area));

	/// <summary>Asks for the area in <paramref name="action"/>'s slot <paramref name="slot"/> to be removed.</summary>
	public void RequestRemoveArea(short action, int slot) => Ask(action, () => ActionEditing.RemoveArea(_source, action, slot));

	/// <summary>Asks for a counter write after <paramref name="action"/>'s last.</summary>
	public void RequestAddPair(short action) => Ask(action, () => ActionEditing.AddPair(_source, action));

	/// <summary>Asks for <paramref name="action"/>'s counter write <paramref name="slot"/> to be <paramref name="counter"/> and <paramref name="op"/>.</summary>
	public void RequestPair(short action, int slot, short counter, short op) =>
		Ask(action, () => ActionEditing.SetPair(_source, action, slot, counter, op));

	/// <summary>Asks for <paramref name="action"/>'s counter write <paramref name="slot"/> to be removed.</summary>
	public void RequestRemovePair(short action, int slot) => Ask(action, () => ActionEditing.RemovePair(_source, action, slot));

	/// <summary>Asks for <paramref name="action"/> to post message <paramref name="id"/>, or none for <c>-1</c>.</summary>
	public void RequestMessage(short action, short id) => Ask(action, () => ActionEditing.SetMessage(_source, action, id));

	/// <summary>Why row-16 GUID <paramref name="group"/>'s deployment action cannot be set, or null when it can.</summary>
	public string? WhyNoDeployment(short group) =>
		_records.Group(group) == null ? GeneratedRecords.Absent : ActionWiring.WhyNoDeployment(_source, group);

	/// <summary>Asks for <paramref name="group"/> to wait on row-10 GUID <paramref name="action"/>, or on none for <c>-1</c>.</summary>
	public void RequestDeployment(short group, short action) =>
		_requested = () => WhyNoDeployment(group) ?? Commit(() => ActionWiring.SetDeployment(_source, group, action));

	/// <summary>Why <paramref name="placement"/>'s own actions cannot be set, or null when they can.</summary>
	public string? WhyNoObjectAction(MissionPlacement placement) {
		if (placement.IsPlayerLance) {
			return "A machine of the player's squad is built from player.mec, not its record.";
		}

		var (row, guid) = Roster(placement);
		return guid == -1 ? "Its record is not in the file." : ActionWiring.WhyNoObjectAction(_source, row, guid);
	}

	/// <summary>Why <paramref name="placement"/>'s engaged action, or with <paramref name="defeated"/> its defeated one, cannot be none, or null.</summary>
	public string? WhyNoNone(MissionPlacement placement, bool defeated) {
		if (WhyNoObjectAction(placement) != null) {
			return null;
		}

		var (row, guid) = Roster(placement);
		return ActionWiring.WhyNoNone(_source, MsnField.RosterAction(row, defeated), guid);
	}

	/// <summary>
	/// Asks for <paramref name="placement"/> to fire row-10 GUID <paramref name="action"/>, or none for <c>-1</c>, when engaged,
	/// or with <paramref name="defeated"/> when defeated.
	/// </summary>
	public void RequestObjectAction(MissionPlacement placement, bool defeated, short action) {
		var (row, guid) = Roster(placement);
		_requested = () => WhyNoObjectAction(placement)
			?? Commit(() => ActionWiring.SetObjectAction(_source, row, guid, defeated, action));
	}

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of <action>'s records once the frame is drawn; refused while it is fixed.
	private void Ask(short action, Func<MsnEdit> edit) => _requested = () => WhyFixed(action) ?? Commit(edit);

	// The roster row and the GUID of the record that created a placement's slot.
	private (int Row, short Guid) Roster(MissionPlacement placement) {
		int block = RosterBlock(placement.Kind);
		return (block + 5, _source.GuidOf(block, placement.SlotIndex));
	}

	private static int RosterBlock(MissionUnitKind kind) => kind switch {
		MissionUnitKind.Mech => 7,
		MissionUnitKind.Flyer => 8,
		_ => 9
	};

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the action of row-10 GUID
	// <selected>, when one is given.
	private string? Commit(Func<MsnEdit> edit, Func<short>? selected = null) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		if (selected?.Invoke() is { } guid && guid != -1) {
			_selection.Current = new ActionSelection(guid);
		}

		return null;
	}
}
