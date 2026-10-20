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
/// (<see cref="MissionRebuild"/>), which keeps the action selected by its GUID: an action's areas decide how the overlay
/// colours each area, its verb how its waiting groups arrive, and a create or a delete adds or removes a block-5 record.
///
/// <para>Actions, areas, targets and groups are taken by their <c>script.dat</c> index, as the panels list them, and written as
/// the GUIDs of the <c>.MSN</c> records that created them. The edits wait for the frame's ImGui to be rendered
/// (<see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class ActionEditor {
	private const int AreaBlock = 4;
	private const int ActionBlock = 5;
	private const int GroupBlock = 11;
	private const int ActionRow = 10;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly SelectionState _selection;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public ActionEditor(MissionDocument source, PointMover mover, MissionRebuild rebuild, SelectionState selection,
			ObjectDeleter deleter) {
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
		_selection = selection;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="action"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(int action) => Guid(action) == -1 ? $"Action {action}'s record is not in the file." : null;

	/// <summary>Why <paramref name="action"/> cannot be made type <paramref name="type"/>, or null when it can.</summary>
	public string? WhyNoType(int action, short type) => WhyFixed(action) ?? ActionEditing.WhyNoType(_source, Guid(action), type);

	/// <summary>Why no area can be added to <paramref name="action"/>, or null when one can.</summary>
	public string? WhyNoAddArea(int action) => WhyFixed(action) ?? ActionEditing.WhyNoAddArea(_source, Guid(action), -1);

	/// <summary>Why no counter write can be added to <paramref name="action"/>, or null when one can.</summary>
	public string? WhyNoAddPair(int action) => WhyFixed(action) ?? ActionEditing.WhyNoAddPair(_source, Guid(action));

	/// <summary>What keeps <paramref name="action"/> from being deleted; <see cref="ActionDeleteBlockers.None"/> when nothing does.</summary>
	public ActionDeleteBlockers BlockersOf(int action) {
		if (WhyFixed(action) is { } reason) {
			return ActionDeleteBlockers.Because(reason);
		}

		var source = _source;
		var namers = ActionEditing.Namers(source, Guid(action));
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

	/// <summary>Asks for a new action, testing block-4 area <paramref name="area"/> or none for <c>-1</c>, which is then selected.</summary>
	public void RequestCreate(int area = -1) {
		_requested = () => {
			short areaGuid = area >= 0 ? _source.GuidOf(AreaBlock, area) : (short)-1;
			if (area >= 0 && areaGuid == -1) {
				return $"Area {area}'s record is not in the file.";
			}

			short created = -1;
			return Commit(() => ActionEditing.Create(_source, areaGuid, out created), () => created);
		};
	}

	/// <summary>Asks for <paramref name="action"/> to be deleted.</summary>
	public void RequestDelete(int action) {
		short guid = Guid(action);
		_requested = () => {
			var blockers = BlockersOf(action);
			if (!blockers.None) {
				return blockers.Fixed ?? "Something references it; its view says what.";
			}

			return Commit(() => ActionEditing.Delete(_source, guid));
		};
	}

	/// <summary>Asks for <paramref name="action"/>'s type to be <paramref name="type"/>.</summary>
	public void RequestType(int action, short type) => Ask(action, guid => ActionEditing.SetType(_source, guid, type));

	/// <summary>Asks for <paramref name="action"/>'s target to be the record its type names at block index <paramref name="target"/>.</summary>
	public void RequestTarget(int action, short type, int target) => Ask(action, guid =>
		ActionEditing.SetTarget(_source, guid, ActionEditing.TargetBlock(type) is { } block ? Resolved(block, target) : (short)-1));

	/// <summary>Asks for <paramref name="action"/>'s verb to be <paramref name="verb"/>.</summary>
	public void RequestVerb(int action, short verb) => Ask(action, guid => ActionEditing.SetVerb(_source, guid, verb));

	/// <summary>Asks for block-4 area <paramref name="area"/> to be added after <paramref name="action"/>'s last.</summary>
	public void RequestAddArea(int action, int area) =>
		Ask(action, guid => ActionEditing.AddArea(_source, guid, Resolved(AreaBlock, area)));

	/// <summary>Asks for block-4 area <paramref name="area"/> in <paramref name="action"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestArea(int action, int slot, int area) =>
		Ask(action, guid => ActionEditing.SetArea(_source, guid, slot, Resolved(AreaBlock, area)));

	/// <summary>Asks for the area in <paramref name="action"/>'s slot <paramref name="slot"/> to be removed.</summary>
	public void RequestRemoveArea(int action, int slot) => Ask(action, guid => ActionEditing.RemoveArea(_source, guid, slot));

	/// <summary>Asks for a counter write after <paramref name="action"/>'s last.</summary>
	public void RequestAddPair(int action) => Ask(action, guid => ActionEditing.AddPair(_source, guid));

	/// <summary>Asks for <paramref name="action"/>'s counter write <paramref name="slot"/> to be <paramref name="counter"/> and <paramref name="op"/>.</summary>
	public void RequestPair(int action, int slot, short counter, short op) =>
		Ask(action, guid => ActionEditing.SetPair(_source, guid, slot, counter, op));

	/// <summary>Asks for <paramref name="action"/>'s counter write <paramref name="slot"/> to be removed.</summary>
	public void RequestRemovePair(int action, int slot) => Ask(action, guid => ActionEditing.RemovePair(_source, guid, slot));

	/// <summary>Asks for <paramref name="action"/> to post message <paramref name="id"/>, or none for <c>-1</c>.</summary>
	public void RequestMessage(int action, short id) => Ask(action, guid => ActionEditing.SetMessage(_source, guid, id));

	/// <summary>Why <paramref name="group"/>'s deployment action cannot be set, or null when it can.</summary>
	public string? WhyNoDeployment(int group) =>
		GroupGuid(group) is not -1 and var guid ? ActionWiring.WhyNoDeployment(_source, guid) : $"Group {group} is not in the file.";

	/// <summary>Asks for <paramref name="group"/> to wait on block-5 action <paramref name="action"/>, or on none for <c>-1</c>.</summary>
	public void RequestDeployment(int group, int action) {
		short guid = GroupGuid(group);
		_requested = () => WhyNoDeployment(group)
			?? Commit(() => ActionWiring.SetDeployment(_source, guid, ActionGuid(action)));
	}

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
	/// Asks for <paramref name="placement"/> to fire block-5 action <paramref name="action"/>, or none for <c>-1</c>, when engaged,
	/// or with <paramref name="defeated"/> when defeated.
	/// </summary>
	public void RequestObjectAction(MissionPlacement placement, bool defeated, int action) {
		var (row, guid) = Roster(placement);
		_requested = () => WhyNoObjectAction(placement)
			?? Commit(() => ActionWiring.SetObjectAction(_source, row, guid, defeated, ActionGuid(action)));
	}

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of <action>'s records, made by its row-10 GUID once the frame is drawn; refused while it is fixed.
	private void Ask(int action, Func<short, MsnEdit> edit) {
		short guid = Guid(action);
		_requested = () => WhyFixed(action) ?? Commit(() => edit(guid));
	}

	private short Guid(int action) => _source.GuidOf(ActionBlock, action);

	private short GroupGuid(int group) => _source.GuidOf(GroupBlock, group);

	// The row-10 GUID of block-5 action <action>, or -1 for none; throws for one no record created.
	private short ActionGuid(int action) => action < 0 ? (short)-1 : Resolved(ActionBlock, action);

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

	// The GUID of the record that created block <block>'s record <index>; throws for one no record created.
	private short Resolved(int block, int index) =>
		_source.GuidOf(block, index) is not -1 and var guid ? guid
			: throw new InvalidOperationException($"Record {index} of block {block} is not in the file.");

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the action of row-10 GUID
	// <selected> where it is exported now, when one is given.
	private string? Commit(Func<MsnEdit> edit, Func<short>? selected = null) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		if (selected?.Invoke() is { } guid && guid != -1 && _source.ExportedIndex(ActionRow, guid) is { } exported) {
			_selection.Current = new ActionSelection(exported);
		}

		return null;
	}
}
