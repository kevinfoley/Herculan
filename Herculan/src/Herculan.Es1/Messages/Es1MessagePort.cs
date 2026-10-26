using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Messages;

/// <summary>
/// <c>MsgPort</c>, the base of the cockpit's two message boxes (docs/es1/simulation/cockpit-messages.md#the-port-object):
/// the queue <c>+0</c>, the current message <c>+0x432</c>, a line is up <c>+0x436</c>, the box's rect <c>+0x43f</c>, the
/// begin and end callbacks, and the due, ready and cancelled latches <c>+0x461</c>–<c>+0x463</c>; with the post, the
/// lifecycle <c>MessagePort_Tick</c>, the withdraw and <c>MessagePort_CopyBoxBetweenPages</c>. The slots of
/// <c>MsgPort</c>'s table <c>00093172</c> are the virtual members; its show, text and take-down slots hold 0, so they
/// are abstract here. The message log at <c>+0x1ec</c> has no reader (docs/es1/simulation/cockpit-messages.md#open)
/// and is not ported.
/// </summary>
public abstract class Es1MessagePort {
	/// <summary>A begin or end callback's slots, two of each.</summary>
	public const int CallbackSlots = 2;

	private readonly List<Action<Es1QueuedMessage>> _beginCallbacks = new(CallbackSlots);
	private readonly List<Action<Es1QueuedMessage>> _endCallbacks = new(CallbackSlots);

	/// <summary><c>SystemMessages_Index</c>'s <i>which</i> for <c>str\system.str</c> and for <c>str\commandr.str</c>.</summary>
	public const sbyte SystemFile = -1, CommandFile = -2;

	/// <summary>The first id a squadmate that leads its group speaks from <c>commandr.str</c>.</summary>
	public const byte CommandIdBase = 0x32;

	/// <summary>
	/// <c>MessagePort_CtorBase</c> (<c>ES1DBSIM 0002eb80</c>)(port, <paramref name="rect"/>)
	/// (docs/es1/simulation/cockpit-messages.md#building-the-two-ports): the queue built, the box a type-0 rectangle region
	/// holding the rect, and the current message, the line, the callback counts and the latches clear; then, while
	/// <paramref name="tables"/>' system table is empty, <see cref="SystemMessagesIndex"/>(0, <see cref="SystemFile"/>).
	/// </summary>
	protected Es1MessagePort(Es1Cockpit cockpit, Es1Rect rect, Es1MessageTables tables) {
		Cockpit = cockpit;
		Box = rect;
		Tables = tables;
		if (!tables.System.Loaded) {
			SystemMessagesIndex(0, SystemFile);
		}
	}

	/// <summary>The cockpit the port draws through and whose globals it reads.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary>The string tables posts are looked up in.</summary>
	public Es1MessageTables Tables { get; }

	/// <summary><c>+0</c>, the message queue.</summary>
	public Es1MessageQueue Queue { get; } = new();

	/// <summary><c>+0x432</c>, the current message, or none.</summary>
	public Es1QueuedMessage? Current { get; protected set; }

	/// <summary><c>+0x436</c>, a line is up.</summary>
	public bool LineUp { get; protected set; }

	/// <summary><c>+0x43f</c>–<c>+0x44b</c>, the box's rect, the region <c>+0x437</c>'s.</summary>
	public Es1Rect Box { get; protected set; }

	/// <summary><c>+0x461</c>, due: the message's least wait has passed and it waits to go up.</summary>
	public bool Due { get; protected set; }

	/// <summary>
	/// <c>+0x462</c>, ready: a due message may go up. Besides the port's own writes, the pilot channel's comm boxes set it
	/// (docs/es1/simulation/heads-down-display.md#starting-a-line).
	/// </summary>
	public bool Ready { get; internal set; }

	/// <summary><c>+0x463</c>, cancelled; the pilot channel's comm boxes also set it, as for <see cref="Ready"/>.</summary>
	public bool Cancelled { get; internal set; }

	/// <summary>The clock <c>000981c4</c>, which the port's functions read as now.</summary>
	protected uint Now => Cockpit.Globals.Clock.Clock;

	/// <summary>
	/// <c>MessagePort_Post</c> (<c>0002ed94</c>)(port, {<paramref name="id"/>, <paramref name="subject"/>})
	/// (docs/es1/simulation/cockpit-messages.md#posting): with no subject the id's slot in the system index. With one, the
	/// squadmate slot <see cref="Es1Cockpit.SquadIndexOf"/> as a byte, and above 3 the first squadmate
	/// (<see cref="Es1Cockpit.SquadmateMachines"/>) as the subject and the slot 0; then, for an id below
	/// <see cref="CommandIdBase"/> or a subject that does not lead its group (<see cref="LeadsGroup"/>), the id's slot in
	/// that squadmate slot's pilot index, otherwise slot <c>id − CommandIdBase</c> of the command index. A count of 2 or
	/// more rolls the variant as <c>Math_RandomNext</c>(<c>000b5c48</c>)'s low word, unsigned, modulo the count; a variant
	/// other than 0 goes through <see cref="Es1MessageTable.StepToVariant"/>; and a slot with a text and attributes is
	/// queued (<see cref="Es1MessageQueue.Enqueue"/>) at now with the subject.
	/// </summary>
	public void Post(byte id, IEs1SimObject? subject) {
		Es1MessageTable table;
		Es1MessageSlot slot;
		if (subject == null) {
			table = Tables.System;
			slot = table[id];
		} else {
			byte squad = (byte)Cockpit.SquadIndexOf(subject);
			if (squad > 3) {
				squad = 0;
				subject = Cockpit.SquadmateMachines[0];
			}

			if (id < CommandIdBase || !LeadsGroup(subject)) {
				table = Tables.Pilots[squad];
				slot = table[id];
			} else {
				table = Tables.Command;
				slot = table[(byte)(id - CommandIdBase)];
			}
		}

		byte variant = slot.Count < 2 ? (byte)0 : (byte)((ushort)Cockpit.Globals.PresentationRandom.Next() % slot.Count);
		if (variant != 0) {
			slot = table.StepToVariant(slot, variant);
		}

		if (slot.Text != null && slot.Attributes != null) {
			Queue.Enqueue(table.Text(slot.Text), slot.Attributes, subject, Now);
		}
	}

	/// <summary>
	/// <c>SystemMessages_Index</c> (<c>0002f1ec</c>)(port, <paramref name="digit"/>, <paramref name="which"/>)
	/// (docs/es1/simulation/cockpit-messages.md#the-string-files): <see cref="SystemFile"/> loads <c>system</c> into the
	/// system table, <see cref="CommandFile"/> <c>commandr</c> into the command table, and a squadmate slot 0 to 2 first,
	/// when that slot's squadmate leads its group (<see cref="LeadsGroup"/>), <c>commandr</c> with
	/// <see cref="CommandFile"/>, then <c>pilot&lt;digit&gt;</c> into the slot's pilot table; each indexed by
	/// <see cref="Es1MessageTable.Index"/>. The Heads-Down Display's <c>HddGauge_LoadPilotFrames</c> (<c>0003dab4</c>) calls
	/// it for each squadmate with <c>Pilot_VoiceBankOf</c> of the portrait, plan step 6c6's part C; until that is ported
	/// no pilot table holds a line. Retail is never handed another <paramref name="which"/>; one indexes nothing here, an
	/// engine guard.
	/// </summary>
	public void SystemMessagesIndex(byte digit, sbyte which) {
		Es1MessageTable table;
		string name;
		switch (which) {
			case SystemFile:
				(table, name) = (Tables.System, "system");
				break;
			case CommandFile:
				(table, name) = (Tables.Command, "commandr");
				break;
			case >= 0 and < Es1MessageTables.PilotSlots:
				if (LeadsGroup(Cockpit.SquadmateMachines[which])) {
					SystemMessagesIndex(0, CommandFile);
				}

				(table, name) = (Tables.Pilots[which], "pilot" + (char)('0' + digit));
				break;
			default:
				return;
		}

		table.Index(Tables.Load(name));
	}

	/// <summary>
	/// Whether <paramref name="machine"/> is entry 0 of its group's (<c>+0x3f</c>) member array <c>+0xe</c>, the test
	/// <see cref="Post"/> and <see cref="SystemMessagesIndex"/> make. Retail reads through a null machine or group; none
	/// leads here, an engine guard.
	/// </summary>
	public static bool LeadsGroup(IEs1SimObject? machine) =>
		machine?.Group is { Members: [var first, ..] } && ReferenceEquals(first, machine);

	/// <summary>
	/// <c>MessagePort_ShiftRect</c> (<c>0002eea0</c>)(port, delta), which the view changes call on the pilot channel with
	/// the cockpit's own move (docs/es1/simulation/cockpit-views.md#cockpitview_applyviewstate-000222c0): the box moved by −delta.
	/// </summary>
	public void ShiftRect(int dx, int dy) => Box = Box.Offset(-dx, -dy);

	/// <summary>
	/// <c>MessagePort_AddBeginCallback</c> (<c>0002eed8</c>)(port, <paramref name="callback"/>): the callback in the
	/// next of two slots, answering true; false when both are taken.
	/// </summary>
	public bool AddBeginCallback(Action<Es1QueuedMessage> callback) => Add(_beginCallbacks, callback);

	/// <summary><c>MessagePort_AddEndCallback</c> (<c>0002ef18</c>)(port, <paramref name="callback"/>): as <see cref="AddBeginCallback"/>.</summary>
	public bool AddEndCallback(Action<Es1QueuedMessage> callback) => Add(_endCallbacks, callback);

	/// <summary>The end callbacks, which the pilot channel's take-down runs (<see cref="Es1PilotChannel.Erase"/>).</summary>
	protected IReadOnlyList<Action<Es1QueuedMessage>> EndCallbacks => _endCallbacks;

	/// <summary>
	/// <c>MessagePort_Withdraw</c> (<c>0002f370</c>)(port, <paramref name="id"/>, <paramref name="subject"/>): when the
	/// current message has both, the cancelled latch set and true, unless it is due, which is left and answers false;
	/// otherwise <see cref="Es1MessageQueue.RemoveMatch"/>.
	/// </summary>
	public bool Withdraw(short id, IEs1SimObject? subject) {
		if (Current is { } current && current.Id == id && ReferenceEquals(current.Subject, subject)) {
			if (Due) {
				return false;
			}

			Cancelled = true;
			return true;
		}

		return Queue.RemoveMatch(id, subject);
	}

	/// <summary>
	/// <c>MessagePort_Tick</c> (<c>0002ef58</c>), <c>MsgPort</c>'s slot <c>+0x10</c>
	/// (docs/es1/simulation/cockpit-messages.md#the-lifecycle): the queue expired; the front taken as current when there
	/// is none, and nothing more with none. Ready or cancelled: a due message not cancelled is shown (<see cref="Show"/>)
	/// while <see cref="Input.Es1Preferences.MessagePortShowEnabled"/> is set, stopping when that answers false, and its
	/// two display times become deadlines and the due latch clears, shown or not; otherwise, cancelled, past its most
	/// time, or past its least with two or more queued, it is taken down (<see cref="Erase"/>) and, when that answers
	/// true, removed, with the current message, the cancelled and the ready latch cleared. Neither: past its most wait it
	/// is removed unshown, with the current message, the line and both latches cleared; past its least wait and not yet
	/// due, it becomes due, ready clears and each begin callback runs with it.
	/// </summary>
	public void Tick() {
		uint now = Now;
		Queue.Expire(now);
		Current ??= Queue.Front();
		if (Current is not { } current) {
			return;
		}

		if (Ready || Cancelled) {
			if (Due && !Cancelled) {
				if (Cockpit.Preferences.MessagePortShowEnabled && !Show()) {
					return;
				}

				current.MinTime += now;
				current.MaxTime += now;
				Due = false;
				return;
			}

			if (!Cancelled && now <= current.MaxTime && (now <= current.MinTime || Queue.Count < 2)) {
				return;
			}

			if (!Erase()) {
				return;
			}

			Queue.RemoveAt(0);
			Current = null;
			Cancelled = false;
			Ready = false;
			return;
		}

		if (current.MaxWait < now) {
			Queue.RemoveAt(0);
			Current = null;
			LineUp = false;
			Cancelled = false;
			Ready = false;
			return;
		}

		if (now <= current.MinWait || Due) {
			return;
		}

		Due = true;
		Ready = false;
		foreach (var callback in _beginCallbacks) {
			callback(current);
		}
	}

	/// <summary>Slot <c>+4</c>, show the current message; answers whether it went up.</summary>
	protected abstract bool Show();

	/// <summary>Slot <c>+8</c>, the line's text for <paramref name="record"/>.</summary>
	protected abstract string SetText(Es1QueuedMessage? record);

	/// <summary>Slot <c>+0xc</c>, take the line down; answers whether it came down.</summary>
	protected abstract bool Erase();

	/// <summary>Slot <c>+0x10</c>, once a frame: <c>MsgPort</c>'s is <see cref="Tick"/>.</summary>
	public virtual void Update() => Tick();

	/// <summary>Slot <c>+0x14</c>, put the background back: <c>MsgPort</c>'s <c>MessagePort_UnpaintNoOp</c> (<c>0002ed7e</c>) does nothing.</summary>
	public virtual void RestoreBackground() {
	}

	/// <summary>Slot <c>+0x18</c>, paint: <c>MsgPort</c>'s <c>MessagePort_PaintNoOp</c> (<c>0002ed83</c>) does nothing.</summary>
	public virtual void Paint() {
	}

	/// <summary>
	/// <c>MessagePort_CopyBoxBetweenPages</c> (<c>0002fcd4</c>)(port, <paramref name="direction"/>), while a line is up:
	/// inside the canvas push, the display's <c>+0x88</c> made its source page and <c>+0x8c</c> its draw page for
	/// direction 0, the other way for 1; the box copied between them as a type-0 region at the offset (0, 0) with the
	/// cursor taken off; the cursor drawn again unless the current view is 4 and no panel is up
	/// (<c>AlertPanel_OpenCount</c>, <c>00094d78</c>), the test the decompile at <c>0002fcd4</c> reads; and the draw page
	/// put back. The source page stays the one it set. The view changes call it on the pilot channel after a slide.
	/// </summary>
	internal void CopyBoxBetweenPages(int direction) {
		if (!LineUp) {
			return;
		}

		var display = Cockpit.Display;
		var canvas = Cockpit.PushCanvasContext();
		int draw = display.DrawPage;
		(display.SourcePage, display.DrawPage) = direction == 1
			? (display.PairDrawn, display.PairVisible)
			: (display.PairVisible, display.PairDrawn);
		display.Cursor.Hide();
		canvas.CopyRegion(Box, 0, 0);
		if (Cockpit.CurrentView != Es1CockpitViews.ExternalView || (Cockpit.Panels?.OpenCount ?? 0) != 0) {
			display.Cursor.Show();
		}

		display.DrawPage = draw;
	}

	private static bool Add(List<Action<Es1QueuedMessage>> callbacks, Action<Es1QueuedMessage> callback) {
		if (callbacks.Count >= CallbackSlots) {
			return false;
		}

		callbacks.Add(callback);
		return true;
	}
}
