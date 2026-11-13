# FMMIDI.COM — the FM driver

**`fmmidi.com` turns the MIDI events [the song player](song-player.md) sends it into register writes of a Yamaha OPL2 chip: nine two-operator voices, each given an instrument from one of two AdLib bank files when a note starts.** It is the image of device `0xa002` in [`SOS\HMIMDRV.386`](../formats/hmi-driver-container.md#layout), the device the songs are written for ([the devices](song-player.md#the-devices)). Addresses are offsets into the image, which the tools name `ES1FMMIDI`.

## The image's interface

The library [enters the image](../formats/hmi-driver-container.md#loading-an-image) at offset 0, a jump to `FmMidi_Entry` (`00000504`). The entry writes the data selector it is given in `FS` into six places of the image (the operand of the `MOV AX,imm16` that opens each far function below, and the selector words of two far pointers in the info block), then calls the function its table at `0xbb` holds for the request in `AX`:

| Request | Function | Answer |
|---|---|---|
| 0 | `FmMidi_GetInfo` (`00000699`) | `EDX` = `0x73`, the info block |
| 1 | `FmMidi_GetFunctions` (`0000069f`) | `EDI` = `0xc3`, the function table |

The table has two entries; a request above 1 calls through whatever dword follows it.

The info block at `0x73` opens with the name `OPL2 MIDI` in 32 bytes and holds a dword 1 at `0x93`, a far pointer at `0x9f` to the word list `0x388`, `0x338` ended by `0xffff` at `0xb3`, a far pointer at `0xa7` to an empty list at `0xb9`, the word `0xc0` after each, and the device id `0xa002` at `0xaf`.

The function table at `0xc3` is five 8-byte entries, an offset and a zero dword each. Each function is a far function that loads `DS` and `ES` with the patched selector, sets the busy byte `0xeb`, disables interrupts, calls its near worker and returns with `RETF`:

| Entry | Far function | Stack arguments | Does |
|---|---|---|---|
| 0, send | `FmMidi_SendFar` (`000006a5`) | far event pointer; the length and slot are not read | [handles the event](#events) |
| 1, open | `FmMidi_OpenFar` (`0000054a`) | the fourth dword's low word is the chip's port, the port block's `+0` | [opens the chip](#opening-and-closing) |
| 2, close | `FmMidi_CloseFar` (`000005b0`) | none read | [closes the chip](#opening-and-closing) |
| 3, reset | `FmMidi_CloseFar` again | | the same function as close |
| 4, load a bank | `FmMidi_LoadBankFar` (`00000610`) | far bank pointer; the slot and a fourth dword are not used | [takes a bank](#the-banks); returns 0 |

A reset is therefore a close: `HmiMidi_ResetDriver`'s call of entry 3 silences the chip and marks it closed, and the driver's per-channel state is left as it is.

## The banks

**`MELODIC.BNK` and `DRUM.BNK` are AdLib instrument banks of 128 instruments, 30 bytes each.** The game reads both whole and gives the driver a far pointer to each ([the banks](../formats/sound-samples.md#the-banks)).

```
+0x00  2      version, 0 0
+0x02  6      "ADLIB-"
+0x08  int16  instruments used, 128
+0x0a  int16  instruments in the file, 128
+0x0c  int32  offset of the name records, 0x1c
+0x10  int32  offset of the instrument records, 0x61c
+0x14  8      zero
name records, 12 bytes each:
       +0x00  int16  the instrument's index, its own position in both files
       +0x02  byte   in DRUM.BNK, the MIDI note the drum is played at; 10 in every record of MELODIC.BNK
       +0x03  9      the name, zero-terminated
instrument records, 30 bytes each:
       +0x00  byte   (not read; 0 in both files)
       +0x01  byte   (not read; 0 in both files)
       +0x02  13     the modulator's fields, a byte each
       +0x0f  13     the carrier's fields, a byte each
       +0x1c  byte   the modulator's waveform
       +0x1d  byte   the carrier's waveform
```

An operator's 13 fields, in order: key scale level, frequency multiple, feedback, attack, sustain level, sustained flag, decay, release, total level, tremolo, vibrato, key scale rate, connection. The driver reads the feedback and connection of the modulator alone.

`FmMidi_SetBank` (`00000bc8`) is the load function's worker. It first packs the bank in place with `FmMidi_PackBank` (`00000d21`), which rewrites the fields of each record into the chip's register bytes:

| Record byte | Becomes | Register |
|---|---|---|
| `+0x0b`, `+0x18` | tremolo << 7 \| vibrato << 6 \| sustained << 5 \| key scale rate << 4 \| multiple | `0x20` |
| `+0x02`, `+0x0f` | key scale level << 6 \| total level | `0x40` |
| `+0x05`, `+0x12` | attack << 4 \| decay | `0x60` |
| `+0x06`, `+0x13` | sustain level << 4 \| release | `0x80` |
| `+0x0e` | feedback × 2 \| connection | `0xc0` |

It packs the records from the first to the one before the last two: the count is the header's `+0x08` less 2, so instruments 126 and 127 keep their unpacked fields.

The driver takes two banks, by a flag (`0x25a8`) each call flips. The first call's bank is the melodic one: it keeps the far pointers to the file, its name records and its instrument records (`0x2450`, `0x2456`, `0x245c`), and sets the program of channels 0–8 to 0. The second call's is the drum bank (`0x2466`, `0x246c`, `0x2472`), whose two record offsets it reads from the melodic bank's header, not the drum bank's own; both files hold the same two. The game loads the melodic bank first.

## The chip

`Opl_Write` (`00001c64`) (register, value) is every write: the register to the port (`0x2cf8`), six reads of port + 1, the value to port + 1, then 28 reads of the port, for the chip's settling time.

A voice *v* (0–8) is a modulator and a carrier operator, whose register offsets are the byte pairs at `0x2d81`:

| Voice | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|
| Modulator | `0x00` | `0x01` | `0x02` | `0x08` | `0x09` | `0x0a` | `0x10` | `0x11` | `0x12` |
| Carrier | `0x03` | `0x04` | `0x05` | `0x0b` | `0x0c` | `0x0d` | `0x13` | `0x14` | `0x15` |

The driver's state:

| Address | Per | Holds | In the image |
|---|---|---|---|
| `0x2484` | channel | the program | 0 |
| `0x24c4` | voice | the channel the voice last played for | 0 |
| `0x24e8` | channel | the pitch bend, the event's third byte | `0x40` |
| `0x2528` | channel | set once the channel has had a pitch bend | 0 |
| `0x2568` | channel | the bend range in semitones | 2 |
| `0x25ac` | channel | the volume, controller 7 | `0x7f` |
| `0x25ec` | voice | the velocity of the voice's note | `0x7f` |
| `0x2650` | channel | the sustain pedal, controller `0x40` | 0 |
| `0x26ac`, `0x26ec` | channel | the count of held note-offs and up to 32 of them, 3 bytes each | 0 |
| `0x2d10` | operator offset | the operator's `0x40` byte as the instrument gives it | 0 |
| `0x2d30` | operator offset | the operator's `0x80` byte | 0 |
| `0x2d40`, `0x2d49` | voice | the bytes last written to `0xa0` + *v* and `0xb0` + *v* | 0 |
| `0x2d70` | voice | the note the voice plays, 0 for a free voice | 0 |
| `0x2480` | | the pitch bend switch | 1 |

A search of the disassembly for `0x2480` finds the one read, in the note on, and no store.

### Opening and closing

`FmMidi_Init` (`00000a09`), the open's worker, keeps the port, then `Opl_Init` (`00001b39`) writes register `0x01` with `0x20`, which lets the waveform registers work, and `Opl_Reset` (`00001bb8`) writes 0 to `0xb0`–`0xb8` and to `0xbd`; `Opl_Reset` runs a second time before the driver marks itself open. `FmMidi_Shutdown` (`00000a7b`), the close's worker, runs `Opl_Init` on the kept port again and then `Opl_Silence` (`00001caf`): 0 to `0xbd`, each voice's `0xb0` byte with its key-on bit cleared, and `0xff` to every carrier's `0x40` register.

Neither touches the per-channel state, so it keeps the image's values until an event changes it. `FmMidi_ResetState` (`00000ae0`), which sets it all back, is called only from a function at `0x79d` that `es2_xref.py` finds no reference to.

## Events

`FmMidi_HandleEvent` (`0000080c`) reads the event's status byte and acts by its high nibble; *c* is the low nibble, the channel:

| Status | Does |
|---|---|
| `0x8c` | [note off](#note-off) |
| `0x9c` | [note on](#note-on), or note off when the velocity is 0 |
| `0xbc` | [a controller](#controllers) |
| `0xcc` | stores the second byte as the channel's program |
| `0xec` | [pitch bend](#pitch-bend), from the third byte alone |
| any other | nothing |

Channel 9 is the drum channel. A program change makes no write: the instrument reaches the chip at the channel's next note.

### Note on

`FmMidi_NoteOn` (`00000f4c`) (note, velocity, channel), in order:

1. [picks a voice](#voice-allocation) and turns it off if it is playing (`Opl_KeyOff`, `00001ea5`: the `0xb0` byte with bit `0x20` cleared), and notes the channel as the voice's.
2. Five times over, writes the carrier's and then the modulator's `0x80` register with its kept byte and the release rate forced to 15, which cuts the old note's tail.
3. Loads the instrument (`Opl_LoadPatch`, `00001f25`). The record is the channel's program in the melodic bank, or, on channel 9, the note's number in the drum bank. The writes, in order, with *m* and *c* the operator offsets: `0x20` + *m*, `0x40` + *m*, `0x60` + *m*, `0x80` + *m*, `0xc0` + *v*, `0xe0` + *m*, then `0x20` + *c*, `0x60` + *c*, `0x80` + *c*, `0xe0` + *c*. The carrier's `0x40` byte is kept and not written here.
4. Keeps the velocity and writes [the level](#the-level) to the carrier's `0x40` register; on a melodic channel, when the instrument's packed `+0x0e` is 0, it writes the same level to the modulator's `0x40` first.
5. Starts the note (`Opl_KeyOn`, `0000212e`) with the note's [frequency word](#frequencies), on channel 9 the word of the note in the drum's name record `+0x02`: the low byte to `0xa0` + *v*, the high byte to `0xb0` + *v* with the key-on bit clear, then with it set (`0x20`).
6. Records the note as the voice's. On a melodic channel that has had a pitch bend, with the bend switch on, it then writes the [bent frequency](#pitch-bend) (`Opl_SetFrequency`, `000021f0`: `0xa0` + *v*, then `0xb0` + *v* with the key-on bit set).

A note on a channel above 15 is dropped.

### The level

The level byte is the low six bits of a `0x40` register, the attenuation, joined with the top two bits of the instrument's own byte for that operator. With *T* the low six bits of the carrier's instrument byte and the curve the 64 bytes at `0x2d9c`:

- *a* = (velocity × ((channel volume << 7) / 127)) >> 7, kept as a byte
- *k* = curve[*a* >> 1]
- level = (`0x2000` − (64 − *k*) × 2 × (64 − *T*)) >> 7

Full volume and velocity give *k* 0 and the level *T*, the instrument's own; the modulator, when it is written, gets the same level, worked from the carrier's *T*. The curve:

```
3f 3a 35 30 2c 29 25 24 23 22 21 20 1f 1e 1d 1c
1b 1a 19 18 17 16 15 14 13 12 11 10 0f 0e 0e 0d
0d 0c 0c 0b 0b 0a 0a 09 09 08 08 07 07 06 06 06
05 05 05 04 04 04 04 03 03 03 02 02 02 01 01 00
```

### Voice allocation

`FmMidi_AllocVoice` (`00001dde`) (channel, note) picks, in order:

1. the lowest voice whose note is 0;
2. with all nine playing, for each channel from 0 that has had no pitch bend, the lowest voice that last played for that channel;
3. failing both, the voice numbered as the channel, less 9 for channels 9–15.

The voice's old note is cut by step 1 of the note on. The note argument is not read.

### Note off

`FmMidi_NoteOff` (`0000143c`) (note, velocity, channel), for a channel below 16:

- With the channel's sustain pedal up, or 32 note-offs already held for it, it turns off every voice whose note and channel match and marks it free.
- Otherwise it adds the three bytes to the channel's held list.

A voice turned off is left to its instrument's release, which step 2 of the next note on that voice cuts.

### Controllers

`FmMidi_Controller` (`0000174a`), by the second byte, *x* the third:

| Controller | Does |
|---|---|
| 7 | `FmMidi_SetChannelVolume` (`00002286`): keeps *x* as the channel's volume and, for each playing voice of the channel, writes [the level](#the-level) again from the voice's kept velocity, to the carrier and, when the packed `+0x0e` of the channel's program in the melodic bank is 0, the modulator first |
| `0x40` | keeps *x* as the sustain pedal; at 0 it gives each held note-off of the channel to `FmMidi_NoteOff`, the last held first |
| `0x66` | keeps *x* as the channel's bend range |
| `0x79` | `FmMidi_ResetControllers` (`00001653`): the channel's volume to `0x7f`, sustain to 0, bend to `0x40` and range to 2 |
| `0x7b` | `FmMidi_AllNotesOff` (`000015aa`): the channel's volume to `0x7f` and sustain to 0, and every voice of the channel turned off and marked free |
| any other | nothing |

So the pan (controller 10) the songs' branch entries send has no effect on this device. Neither `0x79` nor `0x7b` clears the channel's had-a-bend flag or its held note-offs' count. Both also store `0x7f` at `0x25ec` + channel × 4, the velocity table, which has nine entries: for a channel below 9 that is a voice's velocity, and above it a dword of the table after it. The volume's test of the modulator reads the melodic bank for a drum voice too.

### Frequencies

A frequency word is the chip's block number << 10 | F-number, the two bytes `0xa0` and `0xb0` take. The table at `0x2de8` holds one for each note from 12 up, 103 entries, and a note *n* reads entry *n* − 12 with no bound: a note below 12 reads the dwords before the table, the end of the level's curve among them, and a note above 114 the `half` table after it.

- Notes 12–107: block (*n* − 12) / 12, and the F-number of the note in its octave, from C: `0x157`, `0x16b`, `0x181`, `0x198`, `0x1b0`, `0x1ca`, `0x1e5`, `0x202`, `0x220`, `0x241`, `0x263`, `0x287`.
- Notes 108–114: `0x1eae`, `0x1eb7`, `0x1f02`, `0x1f30`, `0x1f60`, `0x1f94`, `0x1fca`.

### Pitch bend

`FmMidi_PitchBend` (`000018bf`), for a channel below 16 other than 9, keeps the event's third byte *b* (`0x40` is no bend), marks the channel as having had a bend, and writes a new frequency to each playing voice of the channel with `Opl_SetFrequency`.

`FmMidi_BendFrequency` (`00001981`) (*b*, note, voice) works the word out between the note's own and the word of the note the channel's range *r* away. With *i* = note − 12, *f* = table[*i*] and *s* = *i* mod 12:

- **Down, *b* < `0x40`:** *d* = *f* − table[*i* − *r*]. When *d* is above `0x2cf` (the two notes are in different blocks), *d* = ((*f* & `0x3ff`) − half[*r* − 1]) & `0x3ff`. The result is *f* − *d* × (((`0x3f` − *b*) × 1000) >> 6) / 1000.
- **Up, *b* ≥ `0x40`:** *d* = table[*i* + *r*] − *f*. When *d* is above `0x2cf`, *f* becomes the same note written in the next block, ((*f* & `0x1c00`) + `0x400`) | half[11 − *s*], and *d* is taken again from it. The result is *f* + *d* × (((*b* − `0x40`) × 1000) >> 6) / 1000.

The comparisons and divisions are unsigned. `half` is the 36 dwords at `0x2f84`, the first twelve the F-numbers of B down to C halved:

```
144 132 121 110 101 0f8 0e5 0d8 0cc 0c1 0b6 0ac
0a1 090 099 086 081 07c 073 06c 066 060 05b 056
2ae 2d6 302 330 360 394 3de 404 440 482 4c6 50e
```

The range comes from the voice's channel as `0x24c4` holds it, the channel the voice last played for.

## Open

- **Open:** the info block's dword at `0x93`, its two lists and the words `0xc0`, and what the library does with the `0x6a` bytes; `es2_xref.py` finds no call of the request in the game ([loading an image](../formats/hmi-driver-container.md#loading-an-image)).
- **Open:** the image's bytes from `0xf2` to the entry at `0x504`, zero but for a few bytes, in which a search of the disassembly finds one address used, the word at `0x4fe`, where the open keeps the port.
- **Open:** whether a song reaches the cases the code handles without a check: a note outside 12–114, an instrument 126 or 127, a channel above 8 given controller `0x79` or `0x7b`.
- **Open:** `fmmidi3.com`, device `0xa009`, which the FM setup does not load.
- **Open:** the level and the frequencies in the running game. A retail check: DOSBox-X's capture of the chip's writes for a song, which holds only the writes that change a register, compared with the writes these rules give for the same song.
