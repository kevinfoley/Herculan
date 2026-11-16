using System.Buffers.Binary;

namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The sound manager's digital driver block, its first bytes, which device <c>0xa005</c>'s open reads through the far
/// pointer at the MIDI block's <c>+0x18</c> (docs/es1/sound/drum-device.md#opening). <c>SoundManager_LoadDrivers</c>
/// (<c>0004ef8c</c>) fills it before the open. The init and hardware blocks at <c>+0x14</c> and <c>+0x1c</c> are the
/// digital driver's open's, module E's (docs/herculan/plan-es1-sound.md), and are not here.
/// </summary>
/// <param name="Device"><c>+0x00</c>: the digital device to open, the <c>[SoundFX]</c> device.</param>
/// <param name="TimerRate"><c>+0x04</c>: the calls a second of the digital driver's timer event.</param>
/// <param name="VoiceLimit"><c>+0x0c</c>: the most voices the device keeps track of at once.</param>
/// <param name="UseVelocity"><c>+0x10</c>: while non-zero, a note's velocity sets its volume.</param>
public readonly record struct Es1DigitalDriverBlock(int Device, int TimerRate, int VoiceLimit, int UseVelocity);

/// <summary>
/// MIDI device <c>0xa005</c>, which the HMI library holds in the program (docs/es1/sound/drum-device.md): each drum note
/// played as a sample of its bank on the digital driver the sound effects play through. Its state is the library's
/// globals, one entry for each of the <see cref="Es1MidiDrivers.Slots"/> driver slots, so one object serves every slot the
/// device opens in.
///
/// <para>The digital driver is this engine's stand-in, <see cref="Es1DigitalDriver"/>, while module E is deferred
/// (docs/herculan/plan-es1-sound.md); it is the program's one digital driver, number 0.</para>
/// </summary>
public sealed class Es1DigiMidiDriver : IEs1MidiDriver {
	/// <summary>The voices the device records for a slot: the size of its ring.</summary>
	public const int VoiceEntries = 32;

	/// <summary><c>HmiDigiMidi_LoadBank</c>'s error for a bank that does not open with <c>HMIDIGIP</c>.</summary>
	public const int BadBank = 0xe;

	/// <summary>The size of a bank entry's start block, which its data follows.</summary>
	public const int BlockSize = 0x74;

	/// <summary>The bank's offset of its first entry.</summary>
	public const int FirstEntry = 0x28;

	/// <summary>A start block's <c>+0x24</c> for which a note's end and a second note on leave a playing note alone.</summary>
	public const int NoStop = unchecked((int)0xffff8000);

	private const int Slots = Es1MidiDrivers.Slots;

	/// <summary><c>HmiDigiMidi_Voices</c> (<c>000a25d8</c>): per slot, 32 entries of the voice, the note, the velocity and the channel, −1 each when free.</summary>
	private readonly int[,,] _voices = new int[Slots, VoiceEntries, 4];

	/// <summary><c>HmiDigiMidi_VoiceNext</c> (<c>000a2fd8</c>).</summary>
	private readonly int[] _next = new int[Slots];

	/// <summary><c>HmiDigiMidi_VoiceOldest</c> (<c>000a2fec</c>).</summary>
	private readonly int[] _oldest = new int[Slots];

	/// <summary><c>HmiDigiMidi_VoiceLimit</c> (<c>000a3000</c>).</summary>
	private readonly int[] _limit = new int[Slots];

	/// <summary><c>HmiDigiMidi_VoiceCount</c> (<c>000a3014</c>).</summary>
	private readonly int[] _count = new int[Slots];

	/// <summary><c>HmiDigiMidi_SlotLevels</c> (<c>000a259c</c>) <c>+0</c>: the slot's volume, 127 in the image.</summary>
	private readonly int[] _volume = Enumerable.Repeat(127, Slots).ToArray();

	/// <summary><c>HmiDigiMidi_SlotLevels</c> <c>+4</c>: the slot's pan, 0 in the image, bit <c>0x80</c> set by the open.</summary>
	private readonly int[] _pan = new int[Slots];

	/// <summary>
	/// <c>HmiDigiMidi_Banks</c> (<c>000a3f48</c>): the bank the slot loaded last, whose start blocks a note on writes in
	/// place, as retail's does.
	/// </summary>
	private readonly byte[]?[] _banks = new byte[Slots][];

	/// <summary><c>HmiDigiMidi_NoteSamples</c> (<c>000a3048</c>): per slot and note, the offset of its start block in the bank, −1 for none.</summary>
	private readonly int[,] _notes = new int[Slots, 128];

	/// <summary>Per slot and note, the data that follows the note's start block, which the block's <c>+0x00</c> points at.</summary>
	private readonly byte[]?[,] _noteData = new byte[Slots, 128][];

	/// <summary>Per slot and note, the stand-in's name for the note's sample, which the block does not carry.</summary>
	private readonly string?[,] _noteNames = new string[Slots, 128];

	/// <summary><c>HmiDigiMidi_DigiDriver</c> (<c>000a3f68</c>): the digital driver the slot plays through.</summary>
	private readonly int[] _digiDriver = new int[Slots];

	/// <summary><c>HmiDigiMidi_TimerAdded</c> (<c>000a3f7c</c>).</summary>
	private readonly bool[] _timerAdded = new bool[Slots];

	/// <summary><c>HmiDigiMidi_UseVelocity</c> (<c>000a3fb8</c>).</summary>
	private readonly int[] _useVelocity = new int[Slots];

	/// <summary><c>HmiDigiMidi_DriverSlot</c> (<c>000a7848</c>): per digital driver, the slot of the device playing through it.</summary>
	private readonly int[] _driverSlot = new int[Slots];

	/// <summary>The banks loaded so far, which tells one bank's sample names from another's.</summary>
	private int _bankLoads;

	/// <summary>The device with no bank and no voices recorded, every slot's volume and pan the image's.</summary>
	/// <param name="digital">The digital driver the samples play on.</param>
	public Es1DigiMidiDriver(Es1DigitalDriver digital) {
		_digital = digital;
		for (int slot = 0; slot < Slots; slot++) {
			ClearVoices(slot, 0);
			for (int note = 0; note < 128; note++) {
				_notes[slot, note] = -1;
			}
		}
	}

	private readonly Es1DigitalDriver _digital;

	/// <summary>
	/// <c>HmiDigiMidi_Open</c> (<c>00081b08</c>)(block, <paramref name="slot"/>, <i>x</i>): the slot given the digital driver
	/// <paramref name="block"/> names, with its timer event, then the block's velocity switch, its voices cleared with the
	/// block's limit, and bit <c>0x80</c> of every slot's pan set; answers 0.
	///
	/// <para>Retail first looks for an open digital driver of the block's device among the library's five and takes it,
	/// owning none; otherwise it opens the device as the first free driver with <c>HmiDigi_InitDriver</c> (<c>0006fe3a</c>),
	/// which this open is the one caller of, and registers the driver's timer handler with <c>HmiTimer_AddEvent</c> at the
	/// block's rate, answering either call's error. PLACEHOLDER (module E, docs/herculan/plan-es1-sound.md): the stand-in
	/// <see cref="Es1DigitalDriver"/> is open from its construction and serviced by the host's frame, so the open takes the
	/// path that opens a driver, as driver 0 with no error, and neither call is made.</para>
	/// </summary>
	public int Open(in Es1DigitalDriverBlock block, int slot) {
		const int driver = 0;
		_digiDriver[slot] = driver;
		_timerAdded[slot] = true;
		_useVelocity[slot] = block.UseVelocity;
		ClearVoices(slot, block.VoiceLimit);
		_driverSlot[driver] = slot;
		for (int i = 0; i < Slots; i++) {
			_pan[i] |= 0x80;
		}

		return 0;
	}

	/// <summary>
	/// <c>HmiDigiMidi_Close</c> (<c>00081c4b</c>): the slot's timer flag cleared, and with it set and the driver its own,
	/// the driver closed and the timer event removed; answers 0. It reads its slot from its third argument, which
	/// <c>HmiMidi_CloseDriver</c> passes as <see cref="Es1MidiDrivers.DriverArg"/>, so it works on slot 0 whatever
	/// <paramref name="slot"/> it is called for (docs/es1/sound/drum-device.md#closing-and-resetting).
	/// PLACEHOLDER (module E): the stand-in has no close, and the timer event no registration, so the close clears the
	/// flag and does nothing more; retail's open takes no driver of its own when it finds one open, which here it never does.
	/// </summary>
	public void Close(int slot) => _timerAdded[Es1MidiDrivers.DriverArg] = false;

	/// <summary><c>HmiDigiMidi_Reset</c> (<c>00081cb0</c>): the slot's recorded voices forgotten, their limit kept, none stopped.</summary>
	public void Reset(int slot) => ClearVoices(slot, _limit[slot]);

	/// <summary>
	/// <c>HmiDigiMidi_LoadBank</c> (<c>00081cd3</c>) (docs/es1/sound/drum-device.md#the-bank): <paramref name="bank"/> kept
	/// as the slot's and its 128 notes cleared; <see cref="BadBank"/> unless it opens with <c>HMIDIGIP</c>; then each entry
	/// from <see cref="FirstEntry"/>, while its offset is below the bank's dword <c>+0x24</c>, given to the note its block's
	/// <c>+0x18</c> names, the next entry starting the block's <c>+0x08</c> + <see cref="BlockSize"/> on; answers 0.
	///
	/// <para>Retail writes the far pointer to the entry's data into the block's <c>+0x00</c> and the far callback
	/// <c>HmiDigiMidi_SampleCallback</c> into its <c>+0x1c</c>; here the note keeps a copy of its data, and a note on gives
	/// the start block <see cref="SampleCallback"/>. A note past 127, which retail writes past the slot's table with, and an
	/// entry that runs past the bank's end, end the walk or are skipped, engine guards.</para>
	/// </summary>
	public int LoadBank(byte[] bank, int slot) {
		_banks[slot] = bank;
		for (int note = 0; note < 128; note++) {
			_notes[slot, note] = -1;
			_noteData[slot, note] = null;
			_noteNames[slot, note] = null;
		}

		if (bank.Length < FirstEntry || !bank.AsSpan(0, 8).SequenceEqual("HMIDIGIP"u8)) {
			return BadBank;
		}

		int load = ++_bankLoads;
		int end = Dword(bank, 0x24);
		for (int offset = FirstEntry; offset < end;) {
			if (offset < 0 || offset + BlockSize > bank.Length) {
				break;
			}

			int length = Dword(bank, offset + 0x08);
			int note = Dword(bank, offset + 0x18);
			if (note is >= 0 and < 128) {
				int data = offset + BlockSize;
				_notes[slot, note] = offset;
				_noteData[slot, note] = bank.AsSpan(data, Math.Clamp(length, 0, bank.Length - data)).ToArray();
				_noteNames[slot, note] = $"HMIDIGIP {load}:{offset:x}";
			}

			offset += length + BlockSize;
		}

		return 0;
	}

	/// <summary>
	/// <c>HmiDigiMidi_SendEvent</c> (<c>00081789</c>) (docs/es1/sound/drum-device.md#playing-a-note): a note on with a
	/// velocity starts its note's sample, a note on with velocity 0 or a note off stops it, controller <c>0x7b</c> stops the
	/// channel's voices, and controllers 7 and 10 set the slot's volume and pan and its voices'. Any other event does
	/// nothing.
	/// </summary>
	public void SendEvent(ReadOnlySpan<byte> midiEvent, int slot) {
		int status = midiEvent[0];
		int channel = status & 0xf;
		int data1 = midiEvent.Length > 1 ? midiEvent[1] : 0;
		int data2 = midiEvent.Length > 2 ? midiEvent[2] : 0;
		if (data1 >= 128) {
			// An engine guard: retail indexes its note table with the byte, past the slot's 128 entries.
			return;
		}

		switch (status & 0xf0) {
			case 0x90:
				int block = _notes[slot, data1];
				if (block < 0 || _banks[slot] is not { } bank) {
					return;
				}

				if (Dword(bank, block + 0x24) != NoStop && TakeNoteVoice(slot, data1, channel) is var held and not -1) {
					_digital.StopSample(held);
				}

				if (data2 == 0) {
					return;
				}

				if (_count[slot] >= _limit[slot] && TakeChannelVoice(slot, channel) is var oldest and not -1) {
					_digital.StopSample(oldest);
				}

				if (_useVelocity[slot] != 0) {
					SetDword(bank, block + 0x14, (_volume[slot] * data2 >> 7) << 8);
				}

				SetDword(bank, block + 0x18, data1);
				if ((_pan[slot] & 0x80) == 0) {
					SetDword(bank, block + 0x40, _pan[slot] << 9);
				}

				int voice = _digital.StartSample(Start(slot, data1, bank, block));
				if (voice != -1) {
					AddVoice(slot, voice, Dword(bank, block + 0x18), data2, channel);
				}

				return;
			case 0x80:
				if (_banks[slot] is { } offBank && _notes[slot, data1] is var offBlock and >= 0 &&
					Dword(offBank, offBlock + 0x24) != NoStop && TakeNoteVoice(slot, data1, channel) is var off and not -1) {
					_digital.StopSample(off);
				}

				return;
			case 0xb0 when data1 == 0x7b:
				while (TakeChannelVoice(slot, channel) is var stopped and not -1) {
					_digital.StopSample(stopped);
				}

				return;
			case 0xb0 when data1 == 7:
				_volume[slot] = data2;
				for (int i = _oldest[slot]; i != _next[slot]; i = (i + 1) & (VoiceEntries - 1)) {
					if (_voices[slot, i, 0] != -1) {
						_digital.SetSampleVolume(_voices[slot, i, 0], (ushort)(data2 * _voices[slot, i, 2] * 2));
					}
				}

				return;
			case 0xb0 when data1 == 10:
				_pan[slot] = data2;
				for (int i = _oldest[slot]; i != _next[slot]; i = (i + 1) & (VoiceEntries - 1)) {
					_digital.SetSamplePan(_voices[slot, i, 0], (ushort)(data2 << 9));
				}

				return;
		}
	}

	/// <summary>
	/// <c>HmiDigiMidi_SampleCallback</c> (<c>00081e1d</c>)(<paramref name="driver"/>, <paramref name="code"/>,
	/// <paramref name="voice"/>), each start block's callback: with event 2, the voice's end, the voice removed from the
	/// slot that plays through the driver.
	/// </summary>
	internal void SampleCallback(int driver, int code, int voice) {
		if (code == 2) {
			RemoveVoice(_driverSlot[driver], voice);
		}
	}

	/// <summary>The start block at <paramref name="block"/> in <paramref name="bank"/>, as the stand-in takes it.</summary>
	private Es1SampleStart Start(int slot, int note, byte[] bank, int block) {
		int driver = _digiDriver[slot];
		return new Es1SampleStart(
			_noteData[slot, note]!, _noteNames[slot, note]!, Dword(bank, block + 0x08), (ushort)Dword(bank, block + 0x0c),
			Dword(bank, block + 0x10), (ushort)Dword(bank, block + 0x14), Dword(bank, block + 0x18),
			(code, voice) => SampleCallback(driver, code, voice), Dword(bank, block + 0x28), Dword(bank, block + 0x30),
			Dword(bank, block + 0x34), (ushort)Dword(bank, block + 0x40), 0);
	}

	/// <summary><c>HmiDigiMidi_ClearVoices</c> (<c>000820c5</c>)(slot, limit): the 32 entries freed, the ring emptied and the limit stored.</summary>
	private void ClearVoices(int slot, int limit) {
		for (int i = 0; i < VoiceEntries; i++) {
			for (int k = 0; k < 4; k++) {
				_voices[slot, i, k] = -1;
			}
		}

		_next[slot] = 0;
		_oldest[slot] = 0;
		_count[slot] = 0;
		_limit[slot] = limit;
	}

	/// <summary>
	/// <c>HmiDigiMidi_AddVoice</c> (<c>0008212c</c>): the entry at the ring's next index written, the count raised and the
	/// index moved on, answering the entry's index; −1 at the limit, the count and limit compared unsigned.
	/// </summary>
	private int AddVoice(int slot, int voice, int note, int velocity, int channel) {
		if ((uint)_limit[slot] <= (uint)_count[slot]) {
			return -1;
		}

		int at = _next[slot];
		_voices[slot, at, 0] = voice;
		_voices[slot, at, 1] = note;
		_voices[slot, at, 2] = velocity;
		_voices[slot, at, 3] = channel;
		_count[slot]++;
		_next[slot] = (at + 1) & (VoiceEntries - 1);
		return at;
	}

	/// <summary><c>HmiDigiMidi_TakeChannelVoice</c> (<c>000821a7</c>): the first entry of the channel with a voice.</summary>
	private int TakeChannelVoice(int slot, int channel) =>
		Take(slot, i => _voices[slot, i, 3] == channel && _voices[slot, i, 0] != -1);

	/// <summary><c>HmiDigiMidi_TakeNoteVoice</c> (<c>0008229d</c>): the first entry of the note and channel with a voice.</summary>
	private int TakeNoteVoice(int slot, int note, int channel) =>
		Take(slot, i => _voices[slot, i, 1] == note && _voices[slot, i, 3] == channel && _voices[slot, i, 0] != -1);

	/// <summary><c>HmiDigiMidi_RemoveVoice</c> (<c>0008239b</c>): the first entry of the voice.</summary>
	private int RemoveVoice(int slot, int voice) => Take(slot, i => _voices[slot, i, 0] == voice);

	/// <summary>
	/// The three takes' shared steps: −1 with no voice recorded; otherwise the first of the 32 entries, by place in the
	/// array, that <paramref name="match"/> accepts, −1 with none. The entries from it back to the oldest move up one
	/// place, wrapping at 32, the oldest place is freed, the oldest index moves on and the count drops; the found entry's
	/// voice is the answer.
	/// </summary>
	private int Take(int slot, Func<int, bool> match) {
		if (_count[slot] == 0) {
			return -1;
		}

		int at = 0;
		while (at < VoiceEntries && !match(at)) {
			at++;
		}

		if (at == VoiceEntries) {
			return -1;
		}

		int voice = _voices[slot, at, 0];
		while (at != _oldest[slot]) {
			int before = (at - 1) & (VoiceEntries - 1);
			for (int k = 0; k < 4; k++) {
				_voices[slot, at, k] = _voices[slot, before, k];
			}

			at = before;
		}

		for (int k = 0; k < 4; k++) {
			_voices[slot, at, k] = -1;
		}

		_oldest[slot] = (_oldest[slot] + 1) & (VoiceEntries - 1);
		_count[slot]--;
		return voice;
	}

	private static int Dword(byte[] bank, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(offset, 4));

	private static void SetDword(byte[] bank, int offset, int value) =>
		BinaryPrimitives.WriteInt32LittleEndian(bank.AsSpan(offset, 4), value);
}
