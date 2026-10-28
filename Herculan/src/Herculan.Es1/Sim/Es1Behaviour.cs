using System.Buffers.Binary;

namespace Herculan.Es1.Sim;

/// <summary>A behaviour state's think, move or reassess: one function pointer called with the machine, its answer an <c>int16</c>.</summary>
public delegate short Es1StateFunction(Es1Mech mech, Es1SimGlobals globals);

/// <summary>
/// One <c>0x28</c>-byte descriptor of <c>BehaviourStateTable</c> (<c>ES1DBSIM 000af26c</c>)
/// (docs/es1/simulation/shields-and-behaviour-states.md#the-hercs-state-table, docs/retail/simulation/ai-dispatch.md#descriptor-layout--0x3e-bytes).
/// </summary>
public sealed class Es1BehaviourDescriptor {
	public Es1BehaviourDescriptor(string name, int dwell, ushort mask, Es1StateFunction? think, Es1StateFunction? move,
			Es1StateFunction? reassess, short lineIndex) {
		Name = name;
		Dwell = dwell;
		Flags = ExpandFlagBits(mask);
		Think = think;
		Move = move;
		Reassess = reassess;
		LineIndex = lineIndex;
	}

	/// <summary><c>+0x00</c>, the state's name.</summary>
	public string Name { get; }

	/// <summary><c>+0x04</c>, the dwell, in the timer's unit.</summary>
	public int Dwell { get; }

	/// <summary><c>+0x08</c>, the 16 flag bytes; flag 0 stops the dwell countdown (docs/retail/simulation/ai-dispatch.md#the-flag-bits).</summary>
	public IReadOnlyList<bool> Flags { get; }

	/// <summary><c>+0x18</c>, the think, or null.</summary>
	public Es1StateFunction? Think { get; }

	/// <summary><c>+0x1c</c>, the move, or null.</summary>
	public Es1StateFunction? Move { get; }

	/// <summary><c>+0x20</c>, the reassess, or null.</summary>
	public Es1StateFunction? Reassess { get; }

	/// <summary><c>+0x24</c>, the squad line's string index.</summary>
	public short LineIndex { get; }

	/// <summary><c>Behaviour_ExpandFlagBits</c> (<c>00010578</c>): one byte per bit of the 16-bit mask, bit 0 first.</summary>
	private static bool[] ExpandFlagBits(ushort mask) =>
		Enumerable.Range(0, 16).Select(bit => ((mask >> bit) & 1) != 0).ToArray();
}

/// <summary>
/// A machine's behaviour block at <c>+0x47</c>, ES2's <c>+0x4d</c> with the same layout
/// (docs/es1/simulation/shields-and-behaviour-states.md#the-behaviour-block, docs/retail/simulation/ai-dispatch.md#the-mechs-behaviour-block--mech0x4d).
/// </summary>
public sealed class Es1BehaviourBlock {
	/// <summary><c>+0x00</c>, the descriptor; null until the constructor's <see cref="SetState"/>.</summary>
	public Es1BehaviourDescriptor? Descriptor { get; private set; }

	/// <summary><c>+0x05</c>, the dwell countdown, the <c>int32</c> after the countdown object's byte at <c>+0x04</c>.</summary>
	public int Countdown { get; set; }

	/// <summary><c>+0x09</c>, the ticks counted since the state was installed, the first not counted.</summary>
	public int TickCount { get; private set; }

	/// <summary><c>+0x0d</c>, the <c>0x28</c> bytes each state lays its own fields over.</summary>
	public byte[] Scratch { get; } = new byte[0x28];

	/// <summary>The machine offset of <see cref="Scratch"/>'s first byte: the block's <c>+0x0d</c> on the block at <c>+0x47</c>.</summary>
	public const int ScratchOffset = 0x54;

	/// <summary>The <c>int16</c> of <see cref="Scratch"/> at machine offset <paramref name="offset"/>.</summary>
	public short ScratchInt16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(Scratch.AsSpan(offset - ScratchOffset));

	/// <summary>The <c>int16</c> of <see cref="Scratch"/> at machine offset <paramref name="offset"/> written.</summary>
	public void SetScratchInt16(int offset, short value) =>
		BinaryPrimitives.WriteInt16LittleEndian(Scratch.AsSpan(offset - ScratchOffset), value);

	/// <summary>The <c>int32</c> of <see cref="Scratch"/> at machine offset <paramref name="offset"/>.</summary>
	public int ScratchInt32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Scratch.AsSpan(offset - ScratchOffset));

	/// <summary>The <c>int32</c> of <see cref="Scratch"/> at machine offset <paramref name="offset"/> written.</summary>
	public void SetScratchInt32(int offset, int value) =>
		BinaryPrimitives.WriteInt32LittleEndian(Scratch.AsSpan(offset - ScratchOffset), value);

	/// <summary>
	/// The scratch's dword at machine offset <c>+0x59</c> when a state keeps an object there: <c>travelling</c>'s look-at
	/// (docs/es1/simulation/mech-ai.md#navigation). A reference does not fit <see cref="Scratch"/>'s bytes, so it is kept
	/// here, and <see cref="SetState"/> clears it with the scratch.
	/// </summary>
	public IEs1SimObject? LookAt { get; set; }

	/// <summary>
	/// The scratch's dword at machine offset <c>+0x5b</c> when <c>fleeing</c> keeps there the object it runs from
	/// (docs/es1/simulation/mech-ai.md#the-combat-states); kept here as <see cref="LookAt"/> is, and cleared with it.
	/// </summary>
	public IEs1SimObject? FleeingFrom { get; set; }

	/// <summary>
	/// The scratch's dword at machine offset <c>+0x54</c> when <c>skirting</c> keeps there the descriptor it interrupted
	/// (docs/es1/simulation/mech-ai.md#skirting); kept here as <see cref="LookAt"/> is, and cleared with it.
	/// </summary>
	public Es1BehaviourDescriptor? Stashed { get; set; }

	/// <summary><c>+0x35</c>, the 16 bytes from which <c>+0x35</c> is "counted once" and <c>+0x36</c> "just installed".</summary>
	public byte[] Latches { get; } = new byte[0x10];

	/// <summary>
	/// <c>Behaviour_SetState</c> (<c>0000fdfc</c>): the descriptor, the count 0, the countdown its dwell plus the jitter, the
	/// latches and the scratch zeroed, then <c>+0x36</c> set.
	/// </summary>
	public void SetState(Es1BehaviourDescriptor descriptor, Es1DwellJitter jitter) {
		Descriptor = descriptor;
		TickCount = 0;
		Countdown = descriptor.Dwell + jitter.Next();
		Array.Clear(Latches);
		Latches[1] = 1;
		Array.Clear(Scratch);
		LookAt = null;
		FleeingFrom = null;
		Stashed = null;
	}

	/// <summary><c>Behaviour_CountTick</c> (<c>0000fe84</c>): with <c>+0x36</c> set it clears it; otherwise <c>+0x35</c> set and the count up by 1.</summary>
	public void CountTick() {
		if (Latches[1] != 0) {
			Latches[1] = 0;
		} else {
			Latches[0] = 1;
			TickCount++;
		}
	}
}
