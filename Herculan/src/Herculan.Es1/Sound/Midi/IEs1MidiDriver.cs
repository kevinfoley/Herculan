namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// A MIDI driver the song player plays through (docs/es1/sound/song-player.md#the-drivers): the functions of one entry of
/// the library's driver table that the song player calls, each with the slot the driver was given, since one driver can
/// keep state per slot. The entry's open function is the driver's own constructor, and its bank function (<c>+0x18</c>)
/// is the driver's own loading, so neither is here.
/// </summary>
public interface IEs1MidiDriver {
	/// <summary>
	/// Entry <c>+0x00</c>: handles one MIDI event, its status byte first, as <c>HmiMidi_SendEvent</c> (<c>000715c5</c>)
	/// passes it. The retail function's answer, 0 from the devices held in the program, is not used.
	/// </summary>
	void SendEvent(ReadOnlySpan<byte> midiEvent, int slot);

	/// <summary>Entry <c>+0x12</c>: reset, called by <see cref="Es1MidiDrivers.Reset"/> after its controllers.</summary>
	void Reset(int slot);

	/// <summary>Entry <c>+0x0c</c>: close, called by <see cref="Es1MidiDrivers.Close"/> before the slot is freed.</summary>
	void Close(int slot);
}
