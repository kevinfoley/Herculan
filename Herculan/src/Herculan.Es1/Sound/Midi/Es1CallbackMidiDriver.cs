namespace Herculan.Es1.Sound.Midi;

/// <summary>A function device <c>0xa003</c> hands each event to, with the slot, answering what the device answers.</summary>
public delegate int Es1MidiCallback(ReadOnlySpan<byte> midiEvent, int slot);

/// <summary>
/// MIDI device <c>0xa003</c>, which the HMI library holds in the program (docs/es1/sound/drum-device.md#the-callback-device):
/// each event of its tracks handed to the function its opener chooses. Its one global serves every slot it opens in.
/// The sound manager opens it only for a <c>[Music]</c> device that names it, which <c>SOUNDSET.INI</c> does not list and
/// which decision 25 of docs/herculan/plan-es1-first-mission.md opens as the FM driver.
/// </summary>
public sealed class Es1CallbackMidiDriver : IEs1MidiDriver {
	/// <summary><c>HmiCallbackMidi_Callback</c> (<c>000a2594</c>): the function each event goes to, none in the image.</summary>
	public Es1MidiCallback? Callback { get; private set; }

	/// <summary>
	/// <c>HmiCallbackMidi_SendEvent</c> (<c>00081719</c>): the event and the slot handed to <see cref="Callback"/>. Retail
	/// far-calls the pointer whatever it holds; with none the event is dropped here, an engine guard.
	/// </summary>
	public void SendEvent(ReadOnlySpan<byte> midiEvent, int slot) => Callback?.Invoke(midiEvent, slot);

	/// <summary><c>HmiCallbackMidi_Reset</c> (<c>0008174f</c>): answers 0 and does nothing.</summary>
	public void Reset(int slot) {
	}

	/// <summary><c>HmiCallbackMidi_Close</c> (<c>00081744</c>): answers 0 and does nothing.</summary>
	public void Close(int slot) {
	}

	/// <summary>
	/// <c>HmiCallbackMidi_SetCallback</c> (<c>0008175a</c>), the device's bank function, for a function: it becomes
	/// <see cref="Callback"/>; answers 0.
	/// </summary>
	public int SetCallback(Es1MidiCallback? callback) {
		Callback = callback;
		return 0;
	}

	/// <summary>
	/// <c>HmiCallbackMidi_SetCallback</c> for a bank, as <see cref="Es1MidiDrivers.LoadBank"/> passes one: retail keeps the
	/// bank's address as the function, so its next event far-calls into the bank's bytes. Here <see cref="Callback"/> is
	/// cleared and the events are dropped, an engine guard. Answers 0.
	/// </summary>
	public int LoadBank(byte[] bank, int slot) => SetCallback(null);
}
