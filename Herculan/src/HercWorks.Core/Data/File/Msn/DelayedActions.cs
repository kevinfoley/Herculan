namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// What removing a timer with <see cref="DelayedActions.Remove"/> takes with it, by GUID: the row-11 timers deleted, the
/// one asked for first; the row-10 actions deleted, each one of those timers' own (<see cref="DelayedActions.Of"/>); the row-16
/// groups that waited on one of those actions and are then in the mission from the start; and the row-15 orders that ended on
/// one and then end on none. <see cref="Refusal"/> is why it cannot be made, or null.
/// </summary>
public sealed record TimerRemoval(IReadOnlyList<short> Timers, IReadOnlyList<short> Actions, IReadOnlyList<short> Groups,
		IReadOnlyList<short> Orders, string? Refusal);

/// <summary>
/// Edits a <see cref="MissionDocument"/>'s action timers, row 11, together with the row-10 actions they fire, each as one
/// record edit: a timer made with a new action to fire, a new action added to a timer's fired ones, and a timer removed with
/// the action that is its own. What a timer does is
/// docs/retail/simulation/mission-deployment.md#action-timers--actiontimer_tick-004230a4; how the editor presents the pair is
/// docs/herculan/handoff-editor-mission-logic.md#countdowns.
///
/// <list type="bullet">
/// <item>A timer's own action (<see cref="Of"/>) is the one action it fires when nothing else can fire that action: no area,
/// no object engaged or defeated, no other timer, and not the action that starts the timer. Each of the 69 of the 338 retail
/// actions a timer fires is fired by that timer alone and tests no area, and no retail timer fires two
/// (<c>HercWorks.Query actions</c>). Every record of each GUID counts, whatever its condition.</item>
/// <item>The records are the ones <see cref="TimerEditing.Create"/> and <see cref="ActionEditing.Create"/> append, a new
/// action testing no area.</item>
/// <item>A remove deletes the timer, and with its own action everything that action alone wires: a group waiting on it is in
/// the mission from the start, an order ending on it ends on none (unset in every record of the GUID, so every slot naming a
/// shared order loses it at once), and each timer it starts is removed by the same rule. A removal reaching an action already
/// being deleted, or the action that starts the first timer, stops there. A template of a random table or an order no group
/// runs that references a deleted action refuses it, since no view can unhook them.</item>
/// </list>
/// </summary>
public static class DelayedActions {
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int OrderRow = 15;
	private const int GroupRow = 16;

	/// <summary>
	/// The row-10 GUID of row-11 timer <paramref name="timer"/>'s own action (the class summary), or <c>-1</c> when it has none
	/// or the layout has no such timer.
	/// </summary>
	public static short Of(MissionDocument document, short timer) {
		if (document.Layout.Entry(TimerRow, timer) == null) {
			return -1;
		}

		var records = (document.Mission.ActionTimers ?? []).Where(record => record.GUID == timer).ToList();
		var fired = records.SelectMany(record => record.SequenceRefs)
			.Where(action => action != -1 && document.Layout.Entry(ActionRow, action) != null).Distinct().ToList();
		if (fired is not [var own] || records.Any(record => record.PrimaryActionRef == own)) {
			return -1;
		}

		bool testsArea = (document.Mission.Actions ?? []).Where(record => record.GUID == own)
			.Any(record => record.AreaRefs.Take(ActionEditing.AreaSlots).Any(area => area != -1 && document.Layout.Entry(AreaRow, area) != null));
		var namers = ActionEditing.Namers(document, own);
		bool firedOtherwise = namers.Objects.Count > 0 || namers.Timers
			.Select(id => (ActionTimer30)document.RecordAt(id)!)
			.Any(record => record.GUID != timer && record.SequenceRefs.Contains(own));
		return testsArea || firedOtherwise ? (short)-1 : own;
	}

	/// <summary>
	/// Appends a new timer started by row-10 action <paramref name="start"/>, or by none for <c>-1</c>, and a new action testing
	/// no area in its fired slot 0, as one <see cref="MissionDocument.Change"/>; answers their GUIDs in <paramref name="timer"/>
	/// and <paramref name="action"/>.
	/// </summary>
	public static MsnEdit Create(MissionDocument document, short start, out short timer, out short action) {
		ActionWiring.CheckAction(document, start);
		short createdTimer = -1;
		short createdAction = -1;
		var change = document.Change(() => {
			document.Append(ActionRow, ActionEditing.NewRecord(-1), out createdAction);
			var record = TimerEditing.NewRecord(start);
			record.SequenceRefs[0] = createdAction;
			document.Append(TimerRow, record, out createdTimer);
		});
		timer = createdTimer;
		action = createdAction;
		return change;
	}

	/// <summary>
	/// Appends a new action testing no area and adds it after row-11 timer <paramref name="timer"/>'s last fired one, as one
	/// <see cref="MissionDocument.Change"/>, answering its GUID in <paramref name="action"/>. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="TimerEditing.WhyNoAddFired"/> refuses it.
	/// </summary>
	public static MsnEdit AddNew(MissionDocument document, short timer, out short action) {
		int slot = TimerEditing.NextFiredSlot(document, timer, -1);
		short created = -1;
		var change = document.Change(() => {
			document.Append(ActionRow, ActionEditing.NewRecord(-1), out created);
			document.SetField(MsnField.TimerFired(slot), timer, created);
		});
		action = created;
		return change;
	}

	/// <summary>What <see cref="Remove"/> of row-11 timer <paramref name="timer"/> takes with it, by the class summary's rule.</summary>
	public static TimerRemoval Removal(MissionDocument document, short timer) {
		TimerEditing.Fields(document, timer);
		var timers = new List<short>();
		var actions = new List<short>();
		var groups = new List<short>();
		var orders = new List<short>();
		string? refusal = null;
		short first = TimerEditing.Start(document, timer);

		void Visit(short visited) {
			if (timers.Contains(visited)) {
				return;
			}

			timers.Add(visited);
			short own = Of(document, visited);
			if (own == -1 || own == first || actions.Contains(own)) {
				return;
			}

			actions.Add(own);
			var namers = ActionEditing.Namers(document, own);
			var started = new List<short>();
			foreach (var id in namers.Groups.Concat(namers.Orders).Concat(namers.Timers)) {
				var record = document.RecordAt(id)!;
				if (record.GUID == -1) {
					refusal ??= "A template of a random table references a trigger this would delete.";
				} else if (id.Row == GroupRow) {
					groups.Add(record.GUID);
				} else if (id.Row == OrderRow) {
					if (GroupOrders.SlotsNaming(document, record.GUID) == 0) {
						refusal ??= "An order no group runs references a trigger this would delete.";
					}

					orders.Add(record.GUID);
				} else if (((ActionTimer30)record).PrimaryActionRef == own) {
					started.Add(record.GUID);
				}
			}

			foreach (short next in started.Distinct()) {
				Visit(next);
			}
		}

		Visit(timer);
		return new TimerRemoval(timers, actions, groups.Distinct().ToList(), orders.Distinct().ToList(), refusal);
	}

	/// <summary>
	/// Removes row-11 timer <paramref name="timer"/> and what <see cref="Removal"/> finds with it, as one
	/// <see cref="MissionDocument.Change"/>: every record of each timer and action, the actions' unreferenced text as
	/// <see cref="ActionEditing.Delete"/> removes it, and the groups' and orders' references unset. Throws
	/// <see cref="InvalidOperationException"/> with <see cref="TimerRemoval.Refusal"/>.
	/// </summary>
	public static MsnEdit Remove(MissionDocument document, short timer) {
		var removal = Removal(document, timer);
		if (removal.Refusal is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return document.Change(() => {
			foreach (short removed in removal.Timers) {
				document.Remove(TimerRow, removed);
			}

			foreach (short group in removal.Groups) {
				document.SetField(MsnField.GroupDeployment, group, -1);
			}

			foreach (short order in removal.Orders) {
				document.SetField(MsnField.OrderAction, order, -1);
			}

			foreach (short action in removal.Actions) {
				ActionEditing.RemoveRecords(document, action);
			}
		});
	}
}
