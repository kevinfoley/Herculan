using Herculan.Engine.Audio.Opl;

namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// <c>fmmidi.com</c>, the image of device <c>0xa002</c> in <c>hmimdrv.386</c> (docs/es1/sound/fm-driver.md): the MIDI events
/// the song player sends it turned into register writes of an OPL2 chip, nine two-operator voices, each given an
/// instrument from the melodic or the drum bank when a note starts. Addresses are offsets into the image.
///
/// <para>The driver keeps its state in <see cref="Image"/>, the bytes <c>HmiMidi_LoadDriverImage</c> loaded, at the offsets
/// retail's code uses: the state lasts as long as the image does, as retail's does in the block that keeps it
/// (<see cref="Es1MidiDriverBlock"/>), and a read past one of the driver's tables lands where retail's does. A read past
/// the image's end gives 0 and a write there is dropped, an engine guard: retail's lands in the rest of its allocation.
/// The two banks' far pointers are kept as the banks themselves, since they point into the game's memory, not the
/// image's; no table read of the driver's reaches their bytes (<c>0x2450</c>–<c>0x247b</c>). The copies each event
/// handler makes of an event's bytes (<c>0x269a</c>–<c>0x26ab</c>) are not kept: the driver reads them only as the
/// arguments they pass.</para>
///
/// <para>Retail's writes go to the port the open is given, the status port read after each for the chip's settling time
/// (<c>Opl_Write</c>, <c>00001c64</c>); here each is a write of <see cref="IOplChip.WriteRegister"/>, and the port is kept in
/// the image as retail keeps it but chooses no chip. Retail's far functions, each of which loads the patched selector,
/// sets the busy byte <c>0xeb</c>, switches the stack and disables interrupts around its worker, have no counterpart.</para>
/// </summary>
public sealed class Es1FmDriver : IEs1MidiDriver {
	/// <summary>The voices the chip has.</summary>
	public const int Voices = 9;

	/// <summary>The drum channel: its notes play the drum bank's instruments.</summary>
	public const int DrumChannel = 9;

	/// <summary>The size of an instrument record of a bank (docs/es1/sound/fm-driver.md#the-banks).</summary>
	private const int RecordSize = 30;

	/// <summary>The size of a name record of a bank.</summary>
	private const int NameSize = 12;

	/// <summary>The note-offs a channel holds at most while its sustain pedal is down.</summary>
	private const int HeldLimit = 32;

	// The image's state (docs/es1/sound/fm-driver.md#the-chip): dwords per channel or voice unless named as bytes.
	private const int PortWord = 0x4fe; // word: the open's port, kept by FmMidi_OpenFar
	private const int OpenFlag = 0x2444;
	private const int InstrumentCount = 0x2448; // the melodic bank's +0x08
	private const int MelodicLoaded = 0x2462;
	private const int DrumLoaded = 0x2478;
	private const int BendSwitch = 0x2480;
	private const int Programs = 0x2484;
	private const int VoiceChannels = 0x24c4;
	private const int Bends = 0x24e8; // the pitch bend, the event's third byte; 0x40 is none
	private const int HadBend = 0x2528;
	private const int BendRanges = 0x2568; // semitones
	private const int BankFlip = 0x25a8;
	private const int Volumes = 0x25ac;
	private const int Velocities = 0x25ec; // by voice, nine of them; the resets store one by channel
	private const int VolumeSet = 0x2610; // by channel: 1 once controller 7 sets it, 0 after a reset
	private const int Sustain = 0x2650;
	private const int HeldCounts = 0x26ac;
	private const int HeldNotes = 0x26ec; // bytes: 32 note-offs of 3 bytes a channel
	private const int InitPort = 0x2cec;
	private const int OpenPort = 0x2cf0;
	private const int ChipPort = 0x2cf8;
	private const int Levels = 0x2d10; // bytes by operator offset: the 0x40 byte the instrument gives
	private const int Releases = 0x2d30; // bytes by operator offset: the 0x80 byte
	private const int FrequencyLow = 0x2d40; // bytes by voice: the 0xa0 byte last written
	private const int FrequencyHigh = 0x2d49; // bytes by voice: the 0xb0 byte last written
	private const int Rhythm = 0x2d64; // byte: the 0xbd byte
	private const int PatchLoaded = 0x2d65; // 11 bytes
	private const int VoiceNotes = 0x2d70; // bytes by voice: the note, 0 for a free voice
	private const int ChipPortCopy = 0x2d7b;
	private const int ChipOpen = 0x2d7f; // byte
	private const int ChipFlag = 0x2d80; // byte
	private const int OperatorOffsets = 0x2d81; // bytes: the modulator and the carrier of each voice
	private const int LevelCurve = 0x2d9c; // 64 bytes
	private const int Frequencies = 0x2de8; // dwords: the frequency word of each note from 12
	private const int Half = 0x2f84; // dwords: the F-numbers of B down to C halved, then 24 more

	private readonly IOplChip _chip;
	private byte[]? _melodic;
	private int _melodicNames;
	private int _melodicRecords;
	private byte[]? _drum;
	private int _drumNames;
	private int _drumRecords;

	/// <summary>
	/// The image asked for its functions, then its open called with <paramref name="portBlock"/>, as
	/// <c>HmiMidi_InitDriver</c> (<c>00071076</c>) does: <c>FmMidi_OpenFar</c> (<c>0000054a</c>), whose answer is 0.
	/// </summary>
	/// <param name="image">The image, which keeps the driver's state.</param>
	/// <param name="chip">The chip the writes go to.</param>
	/// <param name="portBlock">The caller's port block; the open reads its <c>+0</c>, the chip's port.</param>
	public Es1FmDriver(byte[] image, IOplChip chip, int[] portBlock) {
		Image = image;
		_chip = chip;
		Open(portBlock[0]);
	}

	/// <summary>The image the driver runs in, its state at retail's offsets.</summary>
	public byte[] Image { get; }

	/// <inheritdoc/>
	public void SendEvent(ReadOnlySpan<byte> midiEvent, int slot) => HandleEvent(midiEvent);

	/// <summary>Entry 3, <c>FmMidi_CloseFar</c> (<c>000005b0</c>), the close's function: <see cref="Shutdown"/>.</summary>
	public void Reset(int slot) => Shutdown();

	/// <summary>Entry 2, <c>FmMidi_CloseFar</c> (<c>000005b0</c>): <see cref="Shutdown"/>.</summary>
	public void Close(int slot) => Shutdown();

	/// <summary>
	/// Entry 4, <c>FmMidi_LoadBankFar</c> (<c>00000610</c>) and <c>FmMidi_LoadBank</c> (<c>000007bb</c>): the bank taken
	/// (<see cref="SetBank"/>), answering 0.
	/// </summary>
	public int LoadBank(byte[] bank, int slot) {
		SetBank(bank);
		return 0;
	}

	/// <summary>
	/// <c>FmMidi_OpenFar</c> (<c>0000054a</c>), <c>FmMidi_Open</c> (<c>00000744</c>) and <c>FmMidi_Init</c>
	/// (<c>00000a09</c>) (docs/es1/sound/fm-driver.md#opening-and-closing): the port's low word kept at <c>0x4fe</c> and the
	/// port at <c>0x2cf0</c> and <c>0x2cec</c>, <see cref="OplInit"/> and <see cref="OplReset"/>, then the driver marked open.
	/// </summary>
	private void Open(int port) {
		SetWord(PortWord, port);
		SetDword(OpenPort, port);
		SetDword(InitPort, Dword(OpenPort));
		OplInit(InitPort);
		OplReset();
		SetDword(OpenFlag, 1);
	}

	/// <summary>
	/// <c>FmMidi_Shutdown</c> (<c>00000a7b</c>), the close's worker: <see cref="OplInit"/> on the kept port, then
	/// <see cref="OplSilence"/>, then <c>Opl_Close</c> (<c>00001d9d</c>), which clears the chip's open byte, and the driver
	/// marked closed. The per-channel state is left as it is.
	/// </summary>
	private void Shutdown() {
		SetDword(InitPort, Dword(ChipPort));
		OplInit(InitPort);
		OplSilence();
		SetByte(ChipOpen, 0);
		SetDword(OpenFlag, 0);
	}

	/// <summary>
	/// <c>Opl_Init</c> (<c>00001b39</c>)(port's address): the port kept at <c>0x2d7b</c> and <c>0x2cf8</c>, register
	/// <c>0x01</c> written with <c>0x20</c>, which lets the waveform registers work, the <c>0xbd</c> byte cleared,
	/// <see cref="OplReset"/>, and the chip marked open.
	/// </summary>
	private void OplInit(int portAt) {
		SetDword(ChipPortCopy, Dword(portAt));
		SetDword(ChipPort, Dword(portAt));
		Write(0x01, 0x20);
		SetByte(Rhythm, 0);
		OplReset();
		SetByte(ChipOpen, 1);
	}

	/// <summary>
	/// <c>Opl_Reset</c> (<c>00001bb8</c>): each voice's <c>0xb0</c> byte cleared and written, the 11 bytes at <c>0x2d65</c>
	/// and the byte <c>0x2d80</c> cleared, and the <c>0xbd</c> byte written with its top two bits alone kept.
	/// </summary>
	private void OplReset() {
		for (int voice = 0; voice < Voices; voice++) {
			SetByte(FrequencyHigh + voice, 0);
			Write(0xb0 + voice, Byte(FrequencyHigh + voice));
		}

		for (int i = 0; i < 11; i++) {
			SetByte(PatchLoaded + i, 0);
		}

		SetByte(ChipFlag, 0);
		SetByte(Rhythm, Byte(Rhythm) & 0xc0);
		Write(0xbd, Byte(Rhythm));
	}

	/// <summary>
	/// <c>Opl_Silence</c> (<c>00001caf</c>), with the chip open: <c>0xbd</c> cleared and written, each voice's <c>0xb0</c>
	/// byte written with its key-on bit clear, <c>0xff</c> to each carrier's <c>0x40</c> register, and the 11 bytes at
	/// <c>0x2d65</c> cleared. With the chip closed, nothing.
	/// </summary>
	private void OplSilence() {
		if (Byte(ChipOpen) == 0) {
			return;
		}

		SetByte(Rhythm, 0);
		Write(0xbd, 0);
		for (int voice = 0; voice < Voices; voice++) {
			Write(0xb0 + voice, Byte(FrequencyHigh + voice) & 0xdf);
		}

		for (int voice = 0; voice < Voices; voice++) {
			Write(0x40 + Carrier(voice), 0xff);
		}

		for (int i = 0; i < 11; i++) {
			SetByte(PatchLoaded + i, 0);
		}
	}

	/// <summary>
	/// <c>FmMidi_SetBank</c> (<c>00000bc8</c>) (docs/es1/sound/fm-driver.md#the-banks): <paramref name="bank"/> packed in
	/// place (<see cref="PackBank"/>), then taken by the flag at <c>0x25a8</c>, which each call flips. With the flag clear the
	/// bank is the melodic one: its instrument count is kept at <c>0x2448</c>, its name and instrument records found from
	/// its own header, <c>0x2462</c> set, and channels 0–8 given program 0. With it set the bank is the drum one, whose two
	/// record offsets are the melodic bank's, and <c>0x2478</c> is set.
	/// </summary>
	private void SetBank(byte[] bank) {
		PackBank(bank);
		if (Dword(BankFlip) == 0) {
			SetDword(BankFlip, 1);
			_melodic = bank;
			SetDword(InstrumentCount, Int16(bank, 0x08));
			_melodicNames = Int32(bank, 0x0c);
			_melodicRecords = Int32(bank, 0x10);
			SetDword(MelodicLoaded, 1);
			for (int channel = 0; channel < Voices; channel++) {
				ProgramChange(0, channel);
			}
		} else {
			SetDword(BankFlip, 0);
			_drum = bank;
			_drumNames = Int32(_melodic, 0x0c);
			_drumRecords = Int32(_melodic, 0x10);
			SetDword(DrumLoaded, 1);
		}
	}

	/// <summary>
	/// <c>FmMidi_PackBank</c> (<c>00000d21</c>): the fields of each instrument record rewritten in place as the chip's
	/// register bytes (docs/es1/sound/fm-driver.md#the-banks), for the header's count at <c>+0x08</c> less 2 of them from
	/// the first.
	/// </summary>
	private static void PackBank(byte[] bank) {
		int count = Int16(bank, 0x08) - 2;
		int record = Int32(bank, 0x10);
		for (int i = 0; i < count; i++, record += RecordSize) {
			int Field(int at) => BankByte(bank, record + at);
			SetBankByte(bank, record + 0x0b, Field(0x0b) << 7 | Field(0x0c) << 6 | Field(0x07) << 5 | Field(0x0d) << 4 | Field(0x03));
			SetBankByte(bank, record + 0x02, Field(0x02) << 6 | Field(0x0a));
			SetBankByte(bank, record + 0x05, Field(0x05) << 4 | Field(0x08));
			SetBankByte(bank, record + 0x06, Field(0x06) << 4 | Field(0x09));
			SetBankByte(bank, record + 0x0e, Field(0x04) * 2 | Field(0x0e));
			SetBankByte(bank, record + 0x18, Field(0x18) << 7 | Field(0x19) << 6 | Field(0x14) << 5 | Field(0x1a) << 4 | Field(0x10));
			SetBankByte(bank, record + 0x0f, Field(0x0f) << 6 | Field(0x17));
			SetBankByte(bank, record + 0x12, Field(0x12) << 4 | Field(0x15));
			SetBankByte(bank, record + 0x13, Field(0x13) << 4 | Field(0x16));
		}
	}

	/// <summary>
	/// <c>FmMidi_HandleEvent</c> (<c>0000080c</c>) (docs/es1/sound/fm-driver.md#events): by the status byte's high nibble,
	/// the low nibble the channel, a note off (<c>0x8c</c>), a note on or, with velocity 0, a note off (<c>0x9c</c>), a
	/// controller (<c>0xbc</c>), a program change (<c>0xcc</c>) or a pitch bend from the third byte alone (<c>0xec</c>); any
	/// other status does nothing. A data byte past the event's end reads 0, an engine guard: retail reads the byte after it.
	/// </summary>
	private void HandleEvent(ReadOnlySpan<byte> midiEvent) {
		if (midiEvent.IsEmpty) {
			return;
		}

		int status = midiEvent[0] & 0xf0;
		int channel = midiEvent[0] & 0x0f;
		byte data1 = midiEvent.Length > 1 ? midiEvent[1] : (byte)0;
		byte data2 = midiEvent.Length > 2 ? midiEvent[2] : (byte)0;
		switch (status) {
			case 0x80:
				NoteOff(data1, data2, channel);
				break;
			case 0x90 when data2 == 0:
				NoteOff(data1, data2, channel);
				break;
			case 0x90:
				NoteOn(data1, data2, channel);
				break;
			case 0xb0:
				Controller(channel, data1, data2);
				break;
			case 0xc0:
				ProgramChange(data1, channel);
				break;
			case 0xe0:
				PitchBend(channel, data2);
				break;
		}
	}

	/// <summary><c>FmMidi_ProgramChange</c> (<c>000016fd</c>): <paramref name="program"/> kept as the channel's; no write.</summary>
	private void ProgramChange(int program, int channel) => SetDword(Programs + channel * 4, program);

	/// <summary>
	/// <c>FmMidi_NoteOn</c> (<c>00000f4c</c>) (docs/es1/sound/fm-driver.md#note-on), for a channel below 16: a voice picked
	/// (<see cref="AllocVoice"/>) and turned off, and the channel kept as its; five times over, the carrier's and then the
	/// modulator's <c>0x80</c> register written with its kept byte and the release rate 15; the instrument loaded, the
	/// channel's program in the melodic bank or, on <see cref="DrumChannel"/>, the note's number in the drum bank; the
	/// velocity kept and <see cref="Level"/> written to the carrier, and first to the modulator on a melodic channel whose
	/// instrument's packed <c>+0x0e</c> is 0; then the note started with its frequency word, on the drum channel that of the
	/// note in the drum's name record <c>+0x02</c>, and kept as the voice's. On a melodic channel that has had a pitch bend,
	/// with the bend switch <c>0x2480</c> on, the channel's bend is then applied (<see cref="BendFrequency"/>).
	/// </summary>
	private void NoteOn(byte note, byte velocity, int channel) {
		if (channel >= 16) {
			return;
		}

		int voice = AllocVoice(channel);
		OplKeyOff(voice);
		SetDword(VoiceChannels + voice * 4, channel);
		int modulator = Modulator(voice);
		int carrier = Carrier(voice);
		for (int i = 0; i < 5; i++) {
			Write(0x80 + carrier, Byte(Releases + carrier) | 0x0f);
			Write(0x80 + modulator, Byte(Releases + modulator) | 0x0f);
		}

		bool drum = channel == DrumChannel;
		byte[]? bank = drum ? _drum : _melodic;
		int record = drum
			? _drumRecords + note * RecordSize
			: _melodicRecords + Dword(Programs + channel * 4) * RecordSize;
		OplLoadPatch(bank, record, voice);
		SetDword(Velocities + voice * 4, velocity);
		int level = Level(voice, Dword(Volumes + channel * 4));
		if (!drum && BankByte(bank, record + 0x0e) == 0) {
			Write(0x40 + modulator, level | Byte(Levels + modulator) & 0xc0);
		}

		Write(0x40 + carrier, level | Byte(Levels + carrier) & 0xc0);
		int pitch = drum ? BankByte(_drum, _drumNames + note * NameSize + 2) : note;
		OplKeyOn(voice, Dword(Frequencies + (pitch - 12) * 4));
		SetByte(VoiceNotes + voice, note);
		if (!drum && Dword(BendSwitch) != 0 && Dword(HadBend + channel * 4) != 0) {
			OplSetFrequency(voice, BendFrequency((uint)Dword(Bends + channel * 4), note, voice));
		}
	}

	/// <summary>
	/// The level of <paramref name="voice"/>'s note (docs/es1/sound/fm-driver.md#the-level), as <c>FmMidi_NoteOn</c> and
	/// <c>FmMidi_SetChannelVolume</c> work it out: the attenuation from the voice's kept velocity, the channel's
	/// <paramref name="volume"/>, the curve at <c>0x2d9c</c> and the low six bits of the carrier's kept <c>0x40</c> byte.
	/// The caller joins it with the top two bits of the operator's own byte.
	/// </summary>
	private int Level(int voice, int volume) {
		int scaled = Dword(Velocities + voice * 4) * ((volume << 7) / 0x7f) >> 7 & 0xff;
		int curve = Byte(LevelCurve + (scaled >> 1));
		int own = Byte(Levels + Carrier(voice)) & 0x3f;
		return (int)((uint)(0x2000 - (0x40 - curve) * 2 * (0x40 - own)) >> 7) & 0xff;
	}

	/// <summary>
	/// <c>FmMidi_NoteOff</c> (<c>0000143c</c>) (docs/es1/sound/fm-driver.md#note-off), for a channel below 16: with the
	/// channel's sustain pedal up, or <see cref="HeldLimit"/> note-offs already held for it, every voice whose note and
	/// channel match turned off and marked free; otherwise the note, velocity and channel added to the channel's held list.
	/// </summary>
	private void NoteOff(byte note, byte velocity, int channel) {
		if (channel >= 16) {
			return;
		}

		uint held = (uint)Dword(HeldCounts + channel * 4);
		if (Dword(Sustain + channel * 4) == 0 || held >= HeldLimit) {
			for (int voice = 0; voice < Voices; voice++) {
				if (Byte(VoiceNotes + voice) == note && (uint)Dword(VoiceChannels + voice * 4) == (uint)channel) {
					OplKeyOff(voice);
					SetByte(VoiceNotes + voice, 0);
				}
			}

			return;
		}

		int at = HeldNotes + channel * HeldLimit * 3 + (int)held * 3;
		SetByte(at, note);
		SetByte(at + 1, velocity);
		SetByte(at + 2, channel);
		SetDword(HeldCounts + channel * 4, (int)held + 1);
	}

	/// <summary>
	/// <c>FmMidi_Controller</c> (<c>0000174a</c>) (docs/es1/sound/fm-driver.md#controllers), by <paramref name="controller"/>:
	/// 7 the channel's volume (<see cref="SetChannelVolume"/>); <c>0x40</c> the sustain pedal kept and, at 0, each held
	/// note-off of the channel given to <see cref="NoteOff"/>, the last held first, the count taken down before each;
	/// <c>0x66</c> the bend range kept; <c>0x79</c> <see cref="ResetControllers"/>; <c>0x7b</c> <see cref="AllNotesOff"/>.
	/// Any other controller does nothing.
	/// </summary>
	private void Controller(int channel, byte controller, byte value) {
		switch (controller) {
			case 0x07:
				SetChannelVolume(channel, value);
				break;
			case 0x40:
				SetDword(Sustain + channel * 4, value);
				if (value != 0) {
					break;
				}

				while (Dword(HeldCounts + channel * 4) != 0) {
					int held = Dword(HeldCounts + channel * 4) - 1;
					SetDword(HeldCounts + channel * 4, held);
					int at = HeldNotes + held * 3 + channel * HeldLimit * 3;
					NoteOff(Byte(at), Byte(at + 1), Byte(at + 2));
				}

				break;
			case 0x66:
				SetDword(BendRanges + channel * 4, value);
				break;
			case 0x79:
				ResetControllers(channel);
				break;
			case 0x7b:
				AllNotesOff(channel);
				break;
		}
	}

	/// <summary>
	/// <c>FmMidi_SetChannelVolume</c> (<c>00002286</c>), for a channel below 16: <paramref name="volume"/> kept as the
	/// channel's, <c>0x2610</c> set, and <see cref="Level"/> written again for each playing voice of the channel from its kept
	/// velocity, to the carrier and, first, to the modulator when the packed <c>+0x0e</c> of the channel's program in the
	/// melodic bank is 0. That test reads the melodic bank for a drum voice too.
	/// </summary>
	private void SetChannelVolume(int channel, byte volume) {
		if (channel >= 16) {
			return;
		}

		SetDword(Volumes + channel * 4, volume);
		SetDword(VolumeSet + channel * 4, 1);
		for (int voice = 0; voice < Voices; voice++) {
			if (Byte(VoiceNotes + voice) == 0 || (uint)Dword(VoiceChannels + voice * 4) != (uint)channel) {
				continue;
			}

			int level = Level(voice, volume);
			int program = Dword(Programs + Dword(VoiceChannels + voice * 4) * 4);
			if (BankByte(_melodic, _melodicRecords + program * RecordSize + 0x0e) == 0) {
				Write(0x40 + Modulator(voice), level | Byte(Levels + Modulator(voice)) & 0xc0);
			}

			Write(0x40 + Carrier(voice), level | Byte(Levels + Carrier(voice)) & 0xc0);
		}
	}

	/// <summary>
	/// <c>FmMidi_ResetControllers</c> (<c>00001653</c>), controller <c>0x79</c>, for a channel below 16: the channel's volume
	/// to <c>0x7f</c>, <c>0x2610</c> and the sustain to 0, the bend to <c>0x40</c> and the range to 2, and <c>0x7f</c> stored
	/// at <c>0x25ec</c> + channel × 4, in the voices' velocity table (docs/es1/sound/fm-driver.md#controllers). No write;
	/// the had-a-bend flag and the held note-offs stay.
	/// </summary>
	private void ResetControllers(int channel) {
		if (channel >= 16) {
			return;
		}

		SetDword(Volumes + channel * 4, 0x7f);
		SetDword(Velocities + channel * 4, 0x7f);
		SetDword(VolumeSet + channel * 4, 0);
		SetDword(Sustain + channel * 4, 0);
		SetDword(Bends + channel * 4, 0x40);
		SetDword(BendRanges + channel * 4, 2);
	}

	/// <summary>
	/// <c>FmMidi_AllNotesOff</c> (<c>000015aa</c>), controller <c>0x7b</c>, for a channel below 16: the channel's volume to
	/// <c>0x7f</c>, <c>0x2610</c> and the sustain to 0, <c>0x7f</c> stored at <c>0x25ec</c> + channel × 4 as the reset does,
	/// and every voice that last played for the channel turned off and marked free. The had-a-bend flag and the held
	/// note-offs stay.
	/// </summary>
	private void AllNotesOff(int channel) {
		if (channel >= 16) {
			return;
		}

		SetDword(Volumes + channel * 4, 0x7f);
		SetDword(Velocities + channel * 4, 0x7f);
		SetDword(VolumeSet + channel * 4, 0);
		SetDword(Sustain + channel * 4, 0);
		for (int voice = 0; voice < Voices; voice++) {
			if ((uint)Dword(VoiceChannels + voice * 4) == (uint)channel) {
				OplKeyOff(voice);
				SetByte(VoiceNotes + voice, 0);
			}
		}
	}

	/// <summary>
	/// <c>FmMidi_PitchBend</c> (<c>000018bf</c>) (docs/es1/sound/fm-driver.md#pitch-bend), for a channel below 16 other than
	/// <see cref="DrumChannel"/>: <paramref name="bend"/> kept as the channel's, the channel marked as having had a bend,
	/// and each playing voice of the channel given its note's bent frequency (<see cref="BendFrequency"/>).
	/// </summary>
	private void PitchBend(int channel, byte bend) {
		if (channel >= 16 || channel == DrumChannel) {
			return;
		}

		SetDword(Bends + channel * 4, bend);
		SetDword(HadBend + channel * 4, 1);
		for (int voice = 0; voice < Voices; voice++) {
			if (Byte(VoiceNotes + voice) != 0 && (uint)Dword(VoiceChannels + voice * 4) == (uint)channel) {
				OplSetFrequency(voice, BendFrequency(bend, Byte(VoiceNotes + voice), voice));
			}
		}
	}

	/// <summary>
	/// <c>FmMidi_BendFrequency</c> (<c>00001981</c>)(bend, note, voice) (docs/es1/sound/fm-driver.md#pitch-bend): the
	/// frequency word between the note's own and that of the note the range away, the range that of the channel the voice
	/// last played for. Every operation is unsigned 32-bit, as retail's, and a table read past the image reads 0. The note's
	/// place in its octave is the remainder, the number retail's subtraction loop reaches, without the loop's time.
	/// </summary>
	private int BendFrequency(uint bend, int note, int voice) {
		uint index = (uint)(note - 12);
		uint inOctave = index % 12;
		uint range = (uint)Dword(BendRanges + Dword(VoiceChannels + voice * 4) * 4);
		uint frequency = FrequencyAt(index);
		uint step;
		if (bend < 0x40) {
			step = frequency - FrequencyAt(index - range);
			if (step > 0x2cf) {
				step = (frequency & 0x3ff) - (uint)Dword(Half + (int)(range - 1) * 4) & 0x3ff;
			}

			return (int)(frequency - step * ((0x3f - bend) * 1000 >> 6) / 1000);
		}

		step = FrequencyAt(index + range) - frequency;
		if (step > 0x2cf) {
			frequency = (frequency & 0x1c00) + 0x400 | (uint)Dword(Half + (int)(11 - inOctave) * 4);
			step = FrequencyAt(index + range) - frequency;
		}

		return (int)(frequency + step * ((bend - 0x40) * 1000 >> 6) / 1000);
	}

	/// <summary>The dword of the frequency table at <paramref name="index"/>, an unsigned index with no bound.</summary>
	private uint FrequencyAt(uint index) => (uint)Dword(Frequencies + (int)(index * 4));

	/// <summary>
	/// <c>FmMidi_AllocVoice</c> (<c>00001dde</c>) (docs/es1/sound/fm-driver.md#voice-allocation): the lowest free voice; with
	/// all playing, for each channel from 0 that has had no pitch bend, the lowest voice that last played for it; failing
	/// both, the voice numbered as <paramref name="channel"/>, less 9 above 8.
	/// </summary>
	private int AllocVoice(int channel) {
		for (int voice = 0; voice < Voices; voice++) {
			if (Byte(VoiceNotes + voice) == 0) {
				return voice;
			}
		}

		for (int other = 0; other < 16; other++) {
			if (Dword(HadBend + other * 4) != 0) {
				continue;
			}

			for (int voice = 0; voice < Voices; voice++) {
				if ((uint)Dword(VoiceChannels + voice * 4) == (uint)other) {
					return voice;
				}
			}
		}

		return channel > 8 ? channel - 9 : channel;
	}

	/// <summary>
	/// <c>Opl_KeyOff</c> (<c>00001ea5</c>): for a voice with a note, its <c>0xb0</c> byte written with the key-on bit clear
	/// and the voice marked free; for a free voice, nothing.
	/// </summary>
	private void OplKeyOff(int voice) {
		if (Byte(VoiceNotes + voice) == 0) {
			return;
		}

		SetByte(FrequencyHigh + voice, Byte(FrequencyHigh + voice) & 0xdf);
		Write(0xb0 + voice, Byte(FrequencyHigh + voice));
		SetByte(VoiceNotes + voice, 0);
	}

	/// <summary>
	/// <c>Opl_LoadPatch</c> (<c>00001f25</c>)(record, voice) (docs/es1/sound/fm-driver.md#note-on, step 3): the packed
	/// record's bytes written to the voice's operators, the modulator's <c>0x20</c>, <c>0x40</c>, <c>0x60</c>, <c>0x80</c>,
	/// the voice's <c>0xc0</c> and the modulator's <c>0xe0</c>, then the carrier's <c>0x20</c>, <c>0x60</c>, <c>0x80</c> and
	/// <c>0xe0</c>; each operator's <c>0x40</c> and <c>0x80</c> bytes kept by its offset, and the voice's byte at
	/// <c>0x2d65</c> set. The carrier's <c>0x40</c> byte is kept and not written. A record past the bank's end, or a bank not
	/// yet loaded, reads 0, an engine guard: retail reads whatever the far pointer reaches.
	/// </summary>
	private void OplLoadPatch(byte[]? bank, int record, int voice) {
		int Field(int at) => BankByte(bank, record + at);
		int modulator = Modulator(voice);
		int carrier = Carrier(voice);
		SetByte(Releases + carrier, Field(0x13));
		SetByte(Releases + modulator, Field(0x06));
		Write(0x20 + modulator, Field(0x0b));
		Write(0x40 + modulator, Field(0x02));
		SetByte(Levels + modulator, Field(0x02));
		Write(0x60 + modulator, Field(0x05));
		Write(0x80 + modulator, Field(0x06));
		Write(0xc0 + voice, Field(0x0e));
		Write(0xe0 + modulator, Field(0x1c));
		Write(0x20 + carrier, Field(0x18));
		SetByte(Levels + carrier, Field(0x0f));
		Write(0x60 + carrier, Field(0x12));
		Write(0x80 + carrier, Field(0x13));
		Write(0xe0 + carrier, Field(0x1d));
		SetByte(PatchLoaded + voice, 1);
	}

	/// <summary>
	/// <c>Opl_KeyOn</c> (<c>0000212e</c>)(voice, word): the word's low byte kept and written to <c>0xa0</c> + voice, its high
	/// byte with the key-on bit kept and written to <c>0xb0</c> + voice first with the bit clear, then set; the voice's note
	/// byte set to 1, which the note on then overwrites with the note.
	/// </summary>
	private void OplKeyOn(int voice, int word) {
		SetByte(FrequencyLow + voice, word);
		SetByte(FrequencyHigh + voice, (word >> 8) | 0x20);
		Write(0xa0 + voice, Byte(FrequencyLow + voice));
		Write(0xb0 + voice, Byte(FrequencyHigh + voice) & 0xdf);
		Write(0xb0 + voice, Byte(FrequencyHigh + voice));
		SetByte(VoiceNotes + voice, 1);
	}

	/// <summary>
	/// <c>Opl_SetFrequency</c> (<c>000021f0</c>)(voice, word): the word's low byte and its high byte with the key-on bit set
	/// kept and written to <c>0xa0</c> + voice and <c>0xb0</c> + voice.
	/// </summary>
	private void OplSetFrequency(int voice, int word) {
		SetByte(FrequencyLow + voice, word);
		SetByte(FrequencyHigh + voice, (word >> 8) | 0x20);
		Write(0xa0 + voice, Byte(FrequencyLow + voice));
		Write(0xb0 + voice, Byte(FrequencyHigh + voice));
	}

	/// <summary>The modulator's operator offset of <paramref name="voice"/>, from the pairs at <c>0x2d81</c>.</summary>
	private int Modulator(int voice) => Byte(OperatorOffsets + voice * 2);

	/// <summary>The carrier's operator offset of <paramref name="voice"/>, from the pairs at <c>0x2d81</c>.</summary>
	private int Carrier(int voice) => Byte(OperatorOffsets + voice * 2 + 1);

	/// <summary><c>Opl_Write</c> (<c>00001c64</c>)(register, value): the register a byte, as retail's.</summary>
	private void Write(int register, int value) => _chip.WriteRegister(register & 0xff, (byte)value);

	private byte Byte(int at) => (uint)at < (uint)Image.Length ? Image[at] : (byte)0;

	private void SetByte(int at, int value) {
		if ((uint)at < (uint)Image.Length) {
			Image[at] = (byte)value;
		}
	}

	private int Dword(int at) => Byte(at) | Byte(at + 1) << 8 | Byte(at + 2) << 16 | Byte(at + 3) << 24;

	private void SetWord(int at, int value) {
		SetByte(at, value);
		SetByte(at + 1, value >> 8);
	}

	private void SetDword(int at, int value) {
		SetWord(at, value);
		SetWord(at + 2, value >> 16);
	}

	private static int BankByte(byte[]? bank, int at) => bank != null && (uint)at < (uint)bank.Length ? bank[at] : 0;

	private static void SetBankByte(byte[] bank, int at, int value) {
		if ((uint)at < (uint)bank.Length) {
			bank[at] = (byte)value;
		}
	}

	private static int Int16(byte[]? bank, int at) => (short)(BankByte(bank, at) | BankByte(bank, at + 1) << 8);

	private static int Int32(byte[]? bank, int at) =>
		BankByte(bank, at) | BankByte(bank, at + 1) << 8 | BankByte(bank, at + 2) << 16 | BankByte(bank, at + 3) << 24;
}
