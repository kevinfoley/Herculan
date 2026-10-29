using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// What keeps a trigger from being deleted (<see cref="ActionEditor.BlockersOf"/>), by GUID, every record whatever its
/// condition: the countdowns that start on it or fire it, the groups waiting on it, the objects firing it when engaged or
/// defeated, and the order slots it ends. <see cref="Templates"/> counts the templates of random tables
/// (GUID <c>-1</c>) that reference it, and <see cref="UnrunOrders"/> the orders referencing it that no group's slot runs.
/// </summary>
internal sealed record TriggerDeleteBlockers(string? Fixed, IReadOnlyList<short> Countdowns, IReadOnlyList<short> Groups,
		IReadOnlyList<ObjectKey> Objects, IReadOnlyList<EndsOrder> Orders, int Templates, int UnrunOrders) {
	/// <summary>Whether nothing does, and the trigger can be deleted.</summary>
	public bool None => Fixed == null && Countdowns.Count == 0 && Groups.Count == 0 && Objects.Count == 0 && Orders.Count == 0
		&& Templates == 0 && UnrunOrders == 0;

	public static TriggerDeleteBlockers Because(string reason) => new(reason, [], [], [], [], 0, 0);
}

/// <summary>
/// Creates, edits and deletes the open mission's triggers (row-10 actions), and wires them into the records that wait on them
/// or fire them, each edit one step of <see cref="PointMover"/>'s undo history, through <see cref="ActionEditing"/>'s,
/// <see cref="ActionWiring"/>'s and <see cref="TriggerFollowers"/>' record edits on the mission's <c>.MSN</c>. Every edit
/// builds the editor's state again (<see cref="SceneSync"/>): a trigger's areas decide how the overlay colors each area, and
/// its arrival how its waiting groups arrive.
///
/// <para>Triggers, areas, groups and countdowns are taken by their GUIDs, as the selections and the views' choices name them,
/// and any trigger the layout has is edited, a conditional one included; an object's own two triggers by the roster record it
/// stands for (<see cref="RequestObjectAction"/>). The edits wait for the frame's ImGui to be rendered
/// (<see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class ActionEditor {
	private const int ActionRow = 10;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private Func<string?>? _requested;

	public ActionEditor(MissionDocument source, PointMover mover, SceneSync sync, SelectionState selection) {
		_source = source;
		_mover = mover;
		_sync = sync;
		_selection = selection;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose campaign variable uses the views' variable choices list.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-10 GUID <paramref name="action"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(short action) => _source.Layout.Entry(ActionRow, action) == null ? "That trigger is not in the mission." : null;

	/// <summary>Why <paramref name="action"/> cannot be made type <paramref name="type"/>, or null when it can.</summary>
	public string? WhyNoType(short action, short type) => WhyFixed(action) ?? ActionEditing.WhyNoType(_source, action, type);

	/// <summary>
	/// Why row-9 GUID <paramref name="area"/>, or with <c>-1</c> any area, cannot be added to <paramref name="action"/>, or null
	/// when it can.
	/// </summary>
	public string? WhyNoAddArea(short action, short area = -1) => WhyFixed(action) ?? ActionEditing.WhyNoAddArea(_source, action, area);

	/// <summary>Why no campaign variable write can be added to <paramref name="action"/>, or null when one can.</summary>
	public string? WhyNoAddPair(short action) => WhyFixed(action) ?? ActionEditing.WhyNoAddPair(_source, action);

	/// <summary>Why <paramref name="follower"/> cannot be made to wait on a trigger, or null when it can.</summary>
	public string? WhyNoFollow(TriggerFollower follower) => TriggerFollowers.WhyNoAdd(_source, follower);

	/// <summary>What keeps <paramref name="action"/> from being deleted; <see cref="TriggerDeleteBlockers.None"/> when nothing does.</summary>
	public TriggerDeleteBlockers BlockersOf(short action) {
		if (WhyFixed(action) is { } reason) {
			return TriggerDeleteBlockers.Because(reason);
		}

		var namers = ActionEditing.Namers(_source, action);
		int options = 0;
		List<T> ByGuid<T>(IEnumerable<MsnRecordId> records, Func<MsnRecordId, short, T> identity) {
			var found = new List<T>();
			foreach (var record in records) {
				if (_source.RecordAt(record) is not { } named) {
					continue;
				}

				if (named.GUID == -1) {
					options++;
				} else {
					found.Add(identity(record, named.GUID));
				}
			}

			return found.Distinct().ToList();
		}

		var countdowns = ByGuid(namers.Timers, (_, guid) => guid);
		var groups = ByGuid(namers.Groups, (_, guid) => guid);
		var objects = ByGuid(namers.Objects, (record, guid) => new ObjectKey(record.Row, guid));
		var orderGuids = ByGuid(namers.Orders, (_, guid) => guid);

		// Each order by the group slots whose base runs it; one no slot runs is counted apart.
		var orders = new List<EndsOrder>();
		int unrun = 0;
		foreach (short order in orderGuids) {
			int before = orders.Count;
			foreach (var group in _source.Layout.Entries<MissionGroup164>()) {
				var refs = group.Fields.OrderRefs;
				for (int slot = 0; slot < refs.Length; slot++) {
					if (refs[slot] == order) {
						orders.Add(new EndsOrder(group.Guid, slot));
					}
				}
			}

			if (orders.Count == before) {
				unrun++;
			}
		}

		return new TriggerDeleteBlockers(null, countdowns, groups, objects, orders.Distinct().ToList(), options, unrun);
	}

	/// <summary>Asks for a new trigger, testing row-9 GUID <paramref name="area"/> or none for <c>-1</c>, which is then selected.</summary>
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

	/// <summary>Asks for <paramref name="action"/>'s type, whose position its areas test, to be <paramref name="type"/>.</summary>
	public void RequestType(short action, short type) => Ask(action, () => ActionEditing.SetType(_source, action, type));

	/// <summary>Asks for <paramref name="action"/>'s target to be the record of GUID <paramref name="target"/> in the row its type names.</summary>
	public void RequestTarget(short action, short target) => Ask(action, () => ActionEditing.SetTarget(_source, action, target));

	/// <summary>Asks for <paramref name="action"/>'s verb, how the groups waiting on it arrive, to be <paramref name="verb"/>.</summary>
	public void RequestVerb(short action, short verb) => Ask(action, () => ActionEditing.SetVerb(_source, action, verb));

	/// <summary>Asks for row-9 GUID <paramref name="area"/> to be added after <paramref name="action"/>'s last.</summary>
	public void RequestAddArea(short action, short area) => Ask(action, () => ActionEditing.AddArea(_source, action, area));

	/// <summary>Asks for row-9 GUID <paramref name="area"/> in <paramref name="action"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestArea(short action, int slot, short area) =>
		Ask(action, () => ActionEditing.SetArea(_source, action, slot, area));

	/// <summary>Asks for the area in <paramref name="action"/>'s slot <paramref name="slot"/> to be removed.</summary>
	public void RequestRemoveArea(short action, int slot) => Ask(action, () => ActionEditing.RemoveArea(_source, action, slot));

	/// <summary>Asks for a campaign variable write after <paramref name="action"/>'s last.</summary>
	public void RequestAddPair(short action) => Ask(action, () => ActionEditing.AddPair(_source, action));

	/// <summary>Asks for <paramref name="action"/>'s write <paramref name="slot"/> to be campaign variable <paramref name="counter"/> and <paramref name="op"/>.</summary>
	public void RequestPair(short action, int slot, short counter, short op) =>
		Ask(action, () => ActionEditing.SetPair(_source, action, slot, counter, op));

	/// <summary>Asks for <paramref name="action"/>'s write <paramref name="slot"/> to be removed.</summary>
	public void RequestRemovePair(short action, int slot) => Ask(action, () => ActionEditing.RemovePair(_source, action, slot));

	/// <summary>Asks for <paramref name="action"/> to post message <paramref name="id"/>, or none for <c>-1</c>.</summary>
	public void RequestMessage(short action, short id) => Ask(action, () => ActionEditing.SetMessage(_source, action, id));

	/// <summary>Asks for <paramref name="follower"/> to wait on <paramref name="action"/> (<see cref="TriggerFollowers.Add"/>).</summary>
	public void RequestFollow(short action, TriggerFollower follower) =>
		Ask(action, () => TriggerFollowers.Add(_source, action, follower));

	/// <summary>Asks for <paramref name="follower"/> to wait on no trigger (<see cref="TriggerFollowers.Remove"/>).</summary>
	public void RequestUnfollow(short action, TriggerFollower follower) =>
		Ask(action, () => TriggerFollowers.Remove(_source, follower));

	/// <summary>
	/// Asks for a new countdown that <paramref name="action"/> starts, firing a new trigger (<see cref="DelayedActions.Create"/>),
	/// staying on the trigger.
	/// </summary>
	public void RequestNewCountdown(short action) => Ask(action, () => DelayedActions.Create(_source, action, out _, out _));

	/// <summary>
	/// Asks for row-11 GUID <paramref name="countdown"/>, which <paramref name="action"/> starts, to be deleted with the trigger
	/// that is its own and what that trigger wires (<see cref="DelayedActions.Remove"/>).
	/// </summary>
	public void RequestRemoveCountdown(short action, short countdown) => Ask(action, () => DelayedActions.Remove(_source, countdown));

	/// <summary>Asks for row-11 GUID <paramref name="countdown"/>'s delay to be <paramref name="seconds"/>, from the view of <paramref name="action"/>.</summary>
	public void RequestCountdownDelay(short action, short countdown, int seconds) =>
		Ask(action, () => TimerEditing.SetDelay(_source, countdown, seconds));

	/// <summary>Why row-16 GUID <paramref name="group"/>'s deployment action cannot be set, or null when it can.</summary>
	public string? WhyNoDeployment(short group) => ActionWiring.WhyNoDeployment(_source, group);

	/// <summary>Asks for <paramref name="group"/> to wait on row-10 GUID <paramref name="action"/>, or on none for <c>-1</c>.</summary>
	public void RequestDeployment(short group, short action) =>
		_requested = () => WhyNoDeployment(group) ?? Commit(() => ActionWiring.SetDeployment(_source, group, action));

	/// <summary>Why the roster record <paramref name="key"/>'s own triggers cannot be set, or null when they can.</summary>
	public string? WhyNoObjectAction(ObjectKey key) =>
		_source.Layout.Entry(key.Row, key.Guid) == null ? EditorToolbox.ObjectGone(key.Row) : ActionWiring.WhyNoObjectAction(_source, key.Row, key.Guid);

	/// <summary>Why <paramref name="key"/>'s engaged trigger, or with <paramref name="defeated"/> its defeated one, cannot be none, or null.</summary>
	public string? WhyNoNone(ObjectKey key, bool defeated) =>
		WhyNoObjectAction(key) != null ? null : ActionWiring.WhyNoNone(_source, MsnField.RosterAction(key.Row, defeated), key.Guid);

	/// <summary>
	/// Asks for the roster record <paramref name="key"/> to fire row-10 GUID <paramref name="action"/>, or none for <c>-1</c>,
	/// when engaged, or with <paramref name="defeated"/> when defeated.
	/// </summary>
	public void RequestObjectAction(ObjectKey key, bool defeated, short action) =>
		_requested = () => WhyNoObjectAction(key) ?? Commit(() => ActionWiring.SetObjectAction(_source, key.Row, key.Guid, defeated, action));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of <action>'s records, or of the records wired to it, once the frame is drawn; refused while it is fixed.
	private void Ask(short action, Func<MsnEdit> edit) => _requested = () => WhyFixed(action) ?? Commit(edit);

	// Makes the change, rebuilding from it, records it as one step of the history, and selects the trigger of row-10 GUID
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
