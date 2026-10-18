using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;

namespace Herculan.Engine.Editor;

/// <summary>One filled order slot of a group, as generated: its row-15 GUID, its record, and whether another slot names it too.</summary>
internal sealed record EditableOrder(int Slot, short Guid, ScriptOrder Order, bool Shared);

/// <summary>
/// Edits a group's ten order slots, each edit one step of <see cref="PointMover"/>'s undo history, through
/// <see cref="GroupOrders"/>'s record edits on the mission's <c>.MSN</c>: a slot's verb, route, subject and ending action, and adding,
/// removing and moving orders (docs/herculan/handoff-editor-mission-editing.md#in-place, "Orders"). Every edit builds the
/// editor's state again (<see cref="MissionRebuild"/>), which keeps the group selected by its GUID: an order's verb, route
/// and subject decide which routes are walked, what the group overlay draws and what the mission check finds, all of which
/// the rebuild builds again, and an add, a remove or an edit of an order another slot names adds or removes records.
///
/// <para>Groups, routes and subjects are taken by their <c>script.dat</c> index, as the panels list them, and written as
/// the GUIDs of the <c>.MSN</c> records that created them. The edits wait for the frame's ImGui to be rendered
/// (<see cref="RunRequest"/>), since the rebuild changes what the panels list.</para>
/// </summary>
internal sealed class OrderEditor {
	private const int RouteBlock = 3;
	private const int ActionBlock = 5;
	private const int MechBlock = 7;
	private const int BaseBlock = 9;
	private const int OrderBlock = 10;
	private const int GroupBlock = 11;

	private readonly ScriptDat _document;
	private readonly MissionDocument? _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private Func<string?>? _requested;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s.</param>
	/// <param name="source">The mission as its <c>.MSN</c>, or null for a plain <c>script.dat</c>, which is not edited.</param>
	public OrderEditor(ScriptDat document, MissionDocument? source, PointMover mover, MissionRebuild rebuild) {
		_document = document;
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="group"/>'s orders cannot be edited, or null when they can.</summary>
	public string? WhyFixed(int group) =>
		_mover.WhyReadOnly is { } readOnly ? readOnly
			: group < 0 || group >= _document.Groups.Length || _source!.GuidOf(GroupBlock, group) == -1 ? $"Group {group} is not in the file."
			: null;

	/// <summary>The filled order slots of <paramref name="group"/>, by block-11 index, in slot order.</summary>
	public IReadOnlyList<EditableOrder> Slots(int group) {
		if (group < 0 || group >= _document.Groups.Length) {
			return [];
		}

		var refs = _document.Groups[group].OrderRefs;
		var slots = new List<EditableOrder>();
		for (int slot = 0; slot < refs.Length; slot++) {
			if (refs[slot] < 0 || refs[slot] >= _document.Orders.Length) {
				continue;
			}

			short guid = _source?.GuidOf(OrderBlock, refs[slot]) ?? -1;
			slots.Add(new EditableOrder(slot, guid, _document.Orders[refs[slot]], _source != null && GroupOrders.Shared(_source, guid)));
		}

		return slots;
	}

	/// <summary>Why no order can be added to <paramref name="group"/>, or null when one can.</summary>
	public string? WhyNoAdd(int group) => WhyFixed(group) ?? GroupOrders.WhyNoAdd(_source!, Guid(group));

	/// <summary>Why the order in <paramref name="group"/>'s slot <paramref name="slot"/> cannot be removed, or null when it can.</summary>
	public string? WhyNoRemove(int group, int slot) => WhyFixed(group) ?? GroupOrders.WhyNoRemove(_source!, Guid(group), slot);

	/// <summary>Why the orders in <paramref name="group"/>'s slots <paramref name="slot"/> and <paramref name="to"/> cannot trade places, or null.</summary>
	public string? WhyNoMove(int group, int slot, int to) => WhyFixed(group) ?? GroupOrders.WhyNoMove(_source!, Guid(group), slot, to);

	/// <summary>Why <paramref name="group"/>'s slot <paramref name="slot"/> cannot run no route, or null when it can.</summary>
	public string? WhyNoRouteNone(int group, int slot) => WhyFixed(group) ?? GroupOrders.WhyNoRoute(_source!, Guid(group), slot, -1);

	/// <summary>Asks for a new order after <paramref name="group"/>'s last (<see cref="GroupOrders.NewOrder"/>).</summary>
	public void RequestAdd(int group) => Ask(group, (source, guid) => GroupOrders.Add(source, guid, GroupOrders.NewOrder(source, guid), out _));

	/// <summary>Asks for the order in <paramref name="group"/>'s slot <paramref name="slot"/> to be removed.</summary>
	public void RequestRemove(int group, int slot) => Ask(group, (source, guid) => GroupOrders.Remove(source, guid, slot));

	/// <summary>Asks for the orders in <paramref name="group"/>'s slots <paramref name="slot"/> and <paramref name="to"/> to trade places.</summary>
	public void RequestMove(int group, int slot, int to) => Ask(group, (source, guid) => GroupOrders.Move(source, guid, slot, to));

	/// <summary>Asks for verb <paramref name="verb"/> in <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestVerb(int group, int slot, short verb) =>
		Ask(group, (source, guid) => GroupOrders.SetVerb(source, guid, slot, verb));

	/// <summary>Asks for block-3 route <paramref name="route"/>, or <c>-1</c> for none, in <paramref name="group"/>'s slot <paramref name="slot"/>.</summary>
	public void RequestRoute(int group, int slot, int route) =>
		Ask(group, (source, guid) => GroupOrders.SetRoute(source, guid, slot, route < 0 ? (short)-1 : Resolved(source, RouteBlock, route)));

	/// <summary>
	/// Asks for the subject of <paramref name="group"/>'s slot <paramref name="slot"/> to be <paramref name="kind"/>'s
	/// record <paramref name="subject"/>, by its block index: kind 0 a group, 1 a herc, 3 a structure, <c>-1</c> none.
	/// </summary>
	public void RequestSubject(int group, int slot, short kind, int subject) =>
		Ask(group, (source, guid) => GroupOrders.SetSubject(source, guid, slot, kind, kind switch {
			0 => Resolved(source, GroupBlock, subject),
			1 => Resolved(source, MechBlock, subject),
			3 => Resolved(source, BaseBlock, subject),
			_ => -1,
		}));

	/// <summary>
	/// Asks for block-5 action <paramref name="action"/>, or <c>-1</c> for none, to end the order in <paramref name="group"/>'s
	/// slot <paramref name="slot"/>.
	/// </summary>
	public void RequestAction(int group, int slot, int action) =>
		Ask(group, (source, guid) => GroupOrders.SetAction(source, guid, slot, action < 0 ? (short)-1 : Resolved(source, ActionBlock, action)));

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	// Asks for <edit> of <group>'s orders, made by its row-16 GUID once the frame is drawn; refused while the orders are fixed.
	private void Ask(int group, Func<MissionDocument, short, MsnEdit> edit) {
		short guid = Guid(group);
		_requested = () => WhyFixed(group) ?? Commit(() => edit(_source!, guid));
	}

	private short Guid(int group) => _source?.GuidOf(GroupBlock, group) ?? -1;

	// The GUID of the record that created block <block>'s record <index>; throws for one no record created.
	private static short Resolved(MissionDocument source, int block, int index) =>
		source.GuidOf(block, index) is not -1 and var guid ? guid
			: throw new InvalidOperationException($"Record {index} of block {block} is not in the file.");

	// Makes the change, rebuilding from it, and records it as one step of the history.
	private string? Commit(Func<MsnEdit> edit) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		return null;
	}
}
