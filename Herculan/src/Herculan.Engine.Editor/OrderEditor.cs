using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Editor;

/// <summary>One filled order slot of a group, from the layout: its row-15 GUID and the order's base fields.</summary>
internal sealed record EditableOrder(int Slot, short Guid, MissionOrder22 Order);

/// <summary>
/// Edits a group's ten order slots, each edit one step of <see cref="PointMover"/>'s undo history, through
/// <see cref="GroupOrders"/>'s record edits on the mission's <c>.MSN</c>: a slot's verb, route, subject and ending action, and adding,
/// removing and moving orders (docs/herculan/handoff-editor-mission-logic.md#orders). Every edit builds the
/// editor's state again (<see cref="SceneSync"/>): an order's verb, route and subject decide which routes are walked,
/// what the group overlay draws and what the mission check finds, all of which the rebuild builds again, and an add, a
/// remove or an edit of an order another slot names adds or removes records.
///
/// <para>Groups, routes, subjects and actions are taken by their GUIDs, as the selections and the views' choices name them,
/// and any group the layout has is edited, a conditional one included. The edits wait for the frame's ImGui to be rendered
/// (<see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class OrderEditor {
	private const int GroupRow = 16;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	public OrderEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-16 GUID <paramref name="group"/>'s orders cannot be edited, or null when they can.</summary>
	public string? WhyFixed(short group) => _source.Layout.Entry(GroupRow, group) == null ? "That group is not in the mission." : null;

	/// <summary>
	/// The filled order slots of row-16 GUID <paramref name="group"/>, from the layout, in slot order: each slot whose order the
	/// layout has (<see cref="GroupOrders.Orders"/>), with that order's base fields.
	/// </summary>
	public IReadOnlyList<EditableOrder> Slots(short group) {
		var orders = GroupOrders.Orders(_source, group);
		var slots = new List<EditableOrder>();
		for (int slot = 0; slot < orders.Length; slot++) {
			if (orders[slot] != -1 && _source.Layout.Entry<MissionOrder22>(orders[slot])?.Fields is { } fields) {
				slots.Add(new EditableOrder(slot, orders[slot], fields));
			}
		}

		return slots;
	}

	/// <summary>Why no order can be added to <paramref name="group"/>, or null when one can.</summary>
	public string? WhyNoAdd(short group) => WhyFixed(group) ?? GroupOrders.WhyNoAdd(_source, group);

	/// <summary>Why the order in <paramref name="group"/>'s slot <paramref name="slot"/> cannot be removed, or null when it can.</summary>
	public string? WhyNoRemove(short group, int slot) => WhyFixed(group) ?? GroupOrders.WhyNoRemove(_source, group, slot);

	/// <summary>Why the orders in <paramref name="group"/>'s slots <paramref name="slot"/> and <paramref name="to"/> cannot trade places, or null.</summary>
	public string? WhyNoMove(short group, int slot, int to) => WhyFixed(group) ?? GroupOrders.WhyNoMove(_source, group, slot, to);

	/// <summary>Why <paramref name="group"/>'s slot <paramref name="slot"/> cannot run no route, or null when it can.</summary>
	public string? WhyNoRouteNone(short group, int slot) => WhyFixed(group) ?? GroupOrders.WhyNoRoute(_source, group, slot, -1);

	/// <summary>Asks for a new order after <paramref name="group"/>'s last (<see cref="GroupOrders.NewOrder"/>).</summary>
	public void RequestAdd(short group) =>
		Ask(group, () => GroupOrders.Add(_source, group, GroupOrders.NewOrder(_source, group, BaseObject.IsGroundVehicle), out _));

	/// <summary>Asks for the order in <paramref name="group"/>'s slot <paramref name="slot"/> to be removed.</summary>
	public void RequestRemove(short group, int slot) => Ask(group, () => GroupOrders.Remove(_source, group, slot));

	/// <summary>Asks for the orders in <paramref name="group"/>'s slots <paramref name="slot"/> and <paramref name="to"/> to trade places.</summary>
	public void RequestMove(short group, int slot, int to) => Ask(group, () => GroupOrders.Move(_source, group, slot, to));

	/// <summary>Asks for verb <paramref name="verb"/> in <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestVerb(short group, int slot, short verb) => Ask(group, () => GroupOrders.SetVerb(_source, group, slot, verb));

	/// <summary>Asks for row-8 GUID <paramref name="route"/>, or <c>-1</c> for none, in <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestRoute(short group, int slot, short route) =>
		Ask(group, () => GroupOrders.SetRoute(_source, group, slot, route));

	/// <summary>
	/// Asks for the subject of <paramref name="group"/>'s slot <paramref name="slot"/> to be <paramref name="kind"/>'s record
	/// of GUID <paramref name="subject"/>: kind 0 a group, 1 a herc, 3 a structure, <c>-1</c> none.
	/// </summary>
	public void RequestSubject(short group, int slot, short kind, short subject) =>
		Ask(group, () => GroupOrders.SetSubject(_source, group, slot, kind, subject));

	/// <summary>
	/// Asks for row-10 GUID <paramref name="action"/>, or <c>-1</c> for none, to end the order in <paramref name="group"/>'s
	/// slot <paramref name="slot"/>.
	/// </summary>
	public void RequestAction(short group, int slot, short action) =>
		Ask(group, () => GroupOrders.SetAction(_source, group, slot, action));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of <group>'s orders once the frame is drawn; refused while the orders are fixed.
	private void Ask(short group, Func<MsnEdit> edit) => _requested = () => WhyFixed(group) ?? Commit(edit);

	// Makes the change, rebuilding from it, and records it as one step of the history.
	private string? Commit(Func<MsnEdit> edit) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	}
}
