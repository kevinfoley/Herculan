using Herculan.Es1.Sim;

namespace Herculan.Es1.Messages;

/// <summary>
/// A queued message, one of the queue's ten <c>0x2c</c>-byte records (docs/es1/simulation/cockpit-messages.md#the-queue).
/// The records are fixed and filled in place, so a reference to one is the pointer retail keeps to it.
/// </summary>
public sealed class Es1QueuedMessage {
	/// <summary><c>+0</c>, the message id, attribute byte 0 of its string.</summary>
	public short Id { get; internal set; }

	/// <summary>
	/// <c>+2</c>, the subject, the machine the message is about, none for the computer's own lines. Retail copies the
	/// poster's record's bytes <c>+2</c>–<c>+0x15</c> here; the posts hold only the subject in them.
	/// </summary>
	public IEs1SimObject? Subject { get; internal set; }

	/// <summary><c>+0x16</c>, attribute byte 1.</summary>
	public byte Attribute1 { get; internal set; }

	/// <summary><c>+0x17</c>, the text.</summary>
	public string? Text { get; internal set; }

	/// <summary><c>+0x1b</c>, attribute byte 2, the priority: a post goes before the first queued message of a strictly greater one.</summary>
	public byte Priority { get; internal set; }

	/// <summary><c>+0x1c</c>, attribute byte 3 × <c>0x3c</c>: the least time up, a deadline once shown.</summary>
	public uint MinTime { get; internal set; }

	/// <summary><c>+0x20</c>, attribute byte 4 × <c>0x3c</c>: the most time up, a deadline once shown.</summary>
	public uint MaxTime { get; internal set; }

	/// <summary><c>+0x24</c>, the post's time plus attribute byte 5 × <c>0x3c</c>: not due before it.</summary>
	public uint MinWait { get; internal set; }

	/// <summary><c>+0x28</c>, the post's time plus attribute byte 6 × <c>0x3c</c>: dropped unshown after it.</summary>
	public uint MaxWait { get; internal set; }
}

/// <summary>
/// A port's message queue, <c>MessagePort_QueueInit</c> (<c>ES1DBSIM 0002e4e0</c>)'s <c>0x1ec</c> bytes: ten records,
/// the order <c>+0x1b8</c>, ten record indices front first, the in-use bytes <c>+0x1e0</c> and the <c>int16</c> count
/// <c>+0x1ea</c>, with the queue functions (docs/es1/simulation/cockpit-messages.md#the-queue). Now is the clock
/// <c>000981c4</c> (<see cref="Es1SimClock.Clock"/>).
/// </summary>
public sealed class Es1MessageQueue {
	/// <summary>The records the queue holds.</summary>
	public const int Capacity = 10;

	/// <summary>What one unit of an attribute timing is worth in counts of the clock, which runs at 60 a second.</summary>
	public const uint TimingUnit = 0x3c;

	private readonly Es1QueuedMessage[] _records = new Es1QueuedMessage[Capacity];
	private readonly int[] _order = new int[Capacity];
	private readonly bool[] _inUse = new bool[Capacity];

	/// <summary><c>MessagePort_QueueInit</c>: the ten records built, the count and the in-use bytes 0.</summary>
	public Es1MessageQueue() {
		for (int i = 0; i < Capacity; i++) {
			_records[i] = new Es1QueuedMessage();
		}
	}

	/// <summary><c>+0x1ea</c>, the messages queued.</summary>
	public short Count { get; private set; }

	/// <summary>
	/// <c>MessagePort_Enqueue</c> (<c>0002e59c</c>)(queue, <paramref name="slot"/>, record): with ten queued,
	/// <see cref="DropLast"/> first. The first record not in use is marked and filled: <paramref name="subject"/>, the
	/// slot's text, and from its attribute bytes the id (byte 0), byte 1, the priority (byte 2), bytes 3 and 4 times
	/// <c>0x3c</c> and bytes 5 and 6 times <c>0x3c</c> plus <paramref name="now"/>. Its index goes into the order
	/// before the first queued message whose priority is strictly greater, the entries from there to the one before the
	/// last moving up one, or at the end; and the count goes up by 1. With no record free nothing is queued.
	///
	/// <para>The move up is retail's (the disassembly at <c>0002e6e3</c>–<c>0002e726</c>): it starts from the entry before
	/// the last, so an insert before the end overwrites the last entry and leaves the entry at the new count as it was.
	/// It is reached only by a post of a lower priority than one queued, and every retail <c>system.str</c> line's is 0.</para>
	///
	/// <para>An attribute byte past the ones the string carries reads 0 here; retail reads on into the file's next
	/// bytes. Every retail <c>system.str</c> line carries the seven read.</para>
	/// </summary>
	public void Enqueue(string? text, byte[] attributes, IEs1SimObject? subject, uint now) {
		if (Count == Capacity) {
			DropLast();
		}

		int free = Array.IndexOf(_inUse, false);
		if (free < 0) {
			return;
		}

		_inUse[free] = true;
		var record = _records[free];
		record.Subject = subject;
		record.Text = text;
		record.Id = Attribute(0);
		record.Attribute1 = Attribute(1);
		record.Priority = Attribute(2);
		record.MinTime = Attribute(3) * TimingUnit;
		record.MaxTime = Attribute(4) * TimingUnit;
		record.MinWait = Attribute(5) * TimingUnit + now;
		record.MaxWait = Attribute(6) * TimingUnit + now;

		int at = 0;
		for (; at < Count; at++) {
			if (_records[_order[at]].Priority > record.Priority) {
				for (int i = Count - 1; i > at; i--) {
					_order[i] = _order[i - 1];
				}

				break;
			}
		}

		_order[at] = free;
		Count++;

		byte Attribute(int index) => index < attributes.Length ? attributes[index] : (byte)0;
	}

	/// <summary><c>MessagePort_QueueFront</c> (<c>0002e768</c>): the record the order's first entry names, or none with none queued.</summary>
	public Es1QueuedMessage? Front() => Count == 0 ? null : _records[_order[0]];

	/// <summary><c>MessagePort_QueueAt</c> (<c>0002e790</c>): the record order entry <paramref name="index"/> names, or none past the count.</summary>
	public Es1QueuedMessage? At(int index) => index < Count ? _records[_order[index]] : null;

	/// <summary>
	/// <c>MessagePort_RemoveAt</c> (<c>0002e7c8</c>)(queue, <paramref name="index"/>): with any queued, the record order
	/// entry <paramref name="index"/> names freed, the later entries moved down one and the count less 1.
	/// </summary>
	public void RemoveAt(int index) {
		if (Count == 0) {
			return;
		}

		_inUse[_order[index]] = false;
		for (int i = index; i < Count - 1; i++) {
			_order[i] = _order[i + 1];
		}

		Count--;
	}

	/// <summary>
	/// <c>MessagePort_QueueDropLast</c> (<c>0002e85c</c>): the record freed and the count less 1. The record is the one
	/// the order entry indexed by the tenth entry names, <c>order[order[9]]</c>, as its disassembly reads
	/// (<c>0002e865</c>–<c>0002e880</c>), not the tenth entry's own: the tenth's record stays in use and the one freed may
	/// still be queued, so the post that follows can fill it. Reached only with ten messages queued.
	/// </summary>
	public void DropLast() {
		_inUse[_order[_order[Capacity - 1]]] = false;
		Count--;
	}

	/// <summary>
	/// <c>MessagePort_QueueExpire</c> (<c>0002e898</c>): from the last order entry down to entry 1, each whose
	/// <see cref="Es1QueuedMessage.MaxWait"/> is before <paramref name="now"/> removed; the front never is.
	/// </summary>
	public void Expire(uint now) {
		for (int i = Count - 1; i > 0; i--) {
			if (_records[_order[i]].MaxWait < now) {
				RemoveAt(i);
			}
		}
	}

	/// <summary>
	/// <c>MessagePort_QueueRemoveMatch</c> (<c>0002e908</c>)(queue, <paramref name="id"/>, <paramref name="subject"/>):
	/// the first queued message with both removed, answering true; false when none has.
	/// </summary>
	public bool RemoveMatch(short id, IEs1SimObject? subject) {
		for (int i = 0; i < Count; i++) {
			var record = _records[_order[i]];
			if (record.Id == id && ReferenceEquals(record.Subject, subject)) {
				RemoveAt(i);
				return true;
			}
		}

		return false;
	}
}
