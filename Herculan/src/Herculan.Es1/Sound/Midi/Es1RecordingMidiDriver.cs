namespace Herculan.Es1.Sound.Midi;

/// <summary>What an <see cref="Es1RecordingMidiDriver"/> was called with.</summary>
public enum Es1MidiCallKind {
	/// <summary><see cref="IEs1MidiDriver.SendEvent"/>.</summary>
	Event,

	/// <summary><see cref="IEs1MidiDriver.Reset"/>.</summary>
	Reset,

	/// <summary><see cref="IEs1MidiDriver.Close"/>.</summary>
	Close,
}

/// <summary>One call to an <see cref="Es1RecordingMidiDriver"/>: its kind, slot and, for an event, its bytes.</summary>
public readonly record struct Es1MidiCall(Es1MidiCallKind Kind, int Slot, byte[] Bytes);

/// <summary>
/// A driver that plays nothing and keeps each call, in order. It stands in for a device nothing is written for yet and
/// is how the song player's output is read without listening: which events reach which slot, and when.
/// </summary>
public sealed class Es1RecordingMidiDriver : IEs1MidiDriver {
	/// <summary>The calls made so far, oldest first.</summary>
	public List<Es1MidiCall> Calls { get; } = [];

	/// <inheritdoc/>
	public void SendEvent(ReadOnlySpan<byte> midiEvent, int slot) => Calls.Add(new Es1MidiCall(Es1MidiCallKind.Event, slot, midiEvent.ToArray()));

	/// <inheritdoc/>
	public void Reset(int slot) => Calls.Add(new Es1MidiCall(Es1MidiCallKind.Reset, slot, []));

	/// <inheritdoc/>
	public void Close(int slot) => Calls.Add(new Es1MidiCall(Es1MidiCallKind.Close, slot, []));
}
