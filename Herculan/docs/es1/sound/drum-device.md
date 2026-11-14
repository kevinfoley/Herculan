# DBSIM.EXE and GO.EXE — the drum device

**The songs' drums are recordings: MIDI device `0xa005`, which both programs' HMI library holds in the program itself, plays each drum note as one of the 19 samples in `sos\drum32.dig`, through the digital driver that plays the sound effects.** The device is also what opens that digital driver. This page is the device, its bank and `0xa003`, the other device the library holds in the program; the song player that sends them a song's events is [`song-player.md`](song-player.md), and the FM driver that plays the rest of a song is [`fm-driver.md`](fm-driver.md). Addresses are `ES1DBSIM` image addresses; `ES1GO`'s copies are [below](#the-copies).

## The devices in the program

`SoundManager_LoadDrivers` starts the two halves of the HMI library before it opens a device ([the driver](../formats/sound-samples.md#the-driver)), each with the path `sos\`. `HmiDigi_InitSystem` (`0006fdbb`) starts the digital half. `HmiMidi_InitSystem` (`00070ed8`) starts the MIDI half: it copies the path to the library's driver path `000a575c` ([loading an image](../formats/hmi-driver-container.md#loading-an-image)), keeps DS's absolute address at `0009db14`, calls `00070985`, fills the two devices' tables of far function pointers, and sets `HmiMidi_SystemStarted` (`000a2570`), which `HmiMidi_UninitSystem` (`00071064`) clears. `HmiDigi_UninitSystem` (`0006fe28`) is the digital half's stop.

`HmiMidi_InitDriver` (`00071076`) gives a slot of either device its six far functions from the device's table ([the drivers](song-player.md#the-drivers)): `HmiDigiMidi_GetFunctions` (`00081781`) answers `HmiDigiMidi_Functions` (`000a3028`) for `0xa005`, and `HmiCallbackMidi_GetFunctions` (`00081711`) answers `HmiCallbackMidi_Functions` (`000a2574`) for `0xa003`. Each table holds five, 0 in the image until `HmiMidi_InitSystem` writes them; the sixth entry, `+0x1e`, is a copy of the six bytes after the table. It then calls the open with the far pointer at the caller's block `+0x18`, the slot and `HmiMidi_DriverArg` (`000a5984`); no driver image is loaded, and `HmiMidi_CloseDriver` (`0007144b`) frees none.

| Entry | `0xa005` | `0xa003` |
|---|---|---|
| `+0x00`, send an event | `HmiDigiMidi_SendEvent` (`00081789`) | `HmiCallbackMidi_SendEvent` (`00081719`) |
| `+0x06`, open | `HmiDigiMidi_Open` (`00081b08`) | `HmiCallbackMidi_Open` (`00081739`) |
| `+0x0c`, close | `HmiDigiMidi_Close` (`00081c4b`) | `HmiCallbackMidi_Close` (`00081744`) |
| `+0x12`, reset | `HmiDigiMidi_Reset` (`00081cb0`) | `HmiCallbackMidi_Reset` (`0008174f`) |
| `+0x18`, load a bank | `HmiDigiMidi_LoadBank` (`00081cd3`) | `HmiCallbackMidi_SetCallback` (`0008175a`) |

All ten are far functions, their arguments left on the stack for the caller to remove.

## Opening

**`0xa005` opens the digital driver the manager's settings name, and runs it from the library's timer at 120 calls a second.** `SoundManager_LoadDrivers` opens it on the manager's block `+0xa0`, whose `+0x18` is a far pointer to the manager, so `HmiDigiMidi_Open` reads the manager's first bytes, its digital driver block ([the sound manager](../formats/sound-samples.md#the-sound-manager)):

| Block | Value | Read as |
|---|---|---|
| `+0x00` | the `[SoundFX]` device | the digital device to open |
| `+0x04` | `0x78` | the rate of the digital driver's timer event, in calls a second: 120 |
| `+0x0c` | 8 | the most notes the device keeps track of at once ([the voices](#the-voices)) |
| `+0x10` | 1 | while non-zero, a note's velocity sets its volume |
| `+0x14` | far pointer | the init block |
| `+0x1c` | far pointer | the hardware block |

1. When one of the five digital drivers' entries at `000a231c` (`0x5c` bytes each, the device at `+0x14`) holds the block's device, the slot takes that driver and does not own it (`HmiDigiMidi_OwnsDriver`, `000a3fa4`, 0).
2. Otherwise the slot owns it: `HmiDigi_InitDriver` (`0006fe3a`)(device, hardware block, init block, far handle) opens the driver and answers its number. The open leaves the driver's own far timer handler at the init block's `+0x1c`, which `HmiDigiMidi_Open` registers with `HmiTimer_AddEvent` at the block's `+0x04`, keeping the event number at `HmiDigiMidi_TimerEvent` (`000a3f90`) and setting `HmiDigiMidi_TimerAdded` (`000a3f7c`). An error from either call is the answer.
3. It keeps the driver number at `HmiDigiMidi_DigiDriver` (`000a3f68`), the block's `+0x10` at `HmiDigiMidi_UseVelocity` (`000a3fb8`) and the slot at `HmiDigiMidi_DriverSlot` (`000a7848`)[driver], clears the slot's voices with the block's `+0x0c` as their limit (`HmiDigiMidi_ClearVoices`, `000820c5`), sets bit `0x80` of the pan of all five slots (`HmiDigiMidi_SlotLevels`, `000a259c`), and answers 0.

`es2_xref` finds one call of `HmiDigi_InitDriver` in each program, this one, so `0xa005` opens the program's digital driver as driver 0, the number `SoundManager_LoadDrivers` stores at `000bd8c8` for the sound effects. `HmiDigi_InitDriver` takes the first of five free driver numbers, loads the device's driver image when the init block holds none, takes or allocates a buffer by the init block's `+0x00` and `+0x0c`, and hands the image the rate at the init block's `+0x10`, 11,025, as its call 2 (`HmiDigi_CallDriver2`, `00080a69`); what the image does with the rate and the buffer is the digital driver's ([Open](#open)).

## Closing and resetting

`HmiDigiMidi_Close` (`00081c4b`) reads its slot from its third argument. With the slot's timer flag set it clears it and, when the slot owns the driver, closes the driver with `HmiDigi_UninitDriver` (`000705c5`)(driver, 1, 1) and removes the timer event with `HmiTimer_RemoveEvent`; it ends with `HmiDigiMidi_CloseNoOp` (`00082127`), which answers its argument, and answers 0.

`HmiMidi_CloseDriver` passes `HmiMidi_DriverArg` (`000a5984`) as that argument, a dword 0 in the image that `es2_xref --decode` finds read five times and never written ([Open](#open)). So the close works on slot 0, which in both programs is the setup's MIDI device, opened before `0xa005`'s slot 1, and for that slot it does nothing: the digital driver stays open and its timer event registered. `SoundManager_UnloadDrivers` then removes the game's timer callback, which is what runs the library's timer, and closes the driver itself with the same `HmiDigi_UninitDriver` call ([the driver](../formats/sound-samples.md#the-driver)).

`HmiDigiMidi_Reset` (`00081cb0`)(far pointer, slot, x) clears the slot's voices, keeping their limit, and stops none. `HmiMidi_ResetDriver` sends controllers `0x79` and `0x7b` on all 16 channels first, and each `0x7b` stops that channel's voices ([playing a note](#playing-a-note)).

## The bank

**`sos\drum32.dig` is a list of the digital driver's start blocks, one per drum note, each followed by its sample.** `SoundManager_LoadDrivers` loads it into `0xa005` through `HmiMidi_LoadBank` (`00072275`), which far-calls a slot's `+0x18` with the data's far pointer, the slot and its own fourth argument. `HmiDigiMidi_LoadBank` (`00081cd3`):

1. keeps the bank's far pointer at `HmiDigiMidi_Banks` (`000a3f48`) and clears the slot's 128 note entries, 6-byte far pointers at `HmiDigiMidi_NoteSamples` (`000a3048`) + `0x300` × slot + 6 × note;
2. answers `0xe` unless the bank opens with `HMIDIGIP` (`HmiDigiMidi_BankSignature`, `000a3fcc`);
3. walks the entries from offset `0x28` while the offset is below the bank's dword `+0x24`: each is a `0x74`-byte start block followed by its data. The note entry of the block's `+0x18` gets a far pointer to the block, the block's `+0x00` a far pointer to its data at `+0x74`, and its `+0x1c` the far callback `HmiDigiMidi_SampleCallback` ([the voices](#the-voices)); the next entry starts the block's `+0x08` + `0x74` on;
4. answers 0.

| Offset | Contents |
|---|---|
| `0x00` | `HMIDIGIP` |
| `0x08`–`0x23` | bytes the device does not read |
| `0x24` | dword, the offset the entries end at |
| `0x28` | the entries |

The start block is the one `HmiDigi_StartSample` takes ([playing](../formats/sound-samples.md#playing)). `DRUM32.DIG` is 160,043 bytes, which its `+0x24` holds, with 19 entries: notes 27–33, 36, 38–41, 43, 45, 47–50 and 53, of 352 to 22,294 bytes of data each, every data byte between 38 and 217. Every block holds its length at `+0x08` and `+0x2c`, its note at `+0x18`, 2 at `+0x10`, `0x7fff` at `+0x14`, `0xffff8000` at `+0x24`, the flags `0x100` at `+0x28`, `0x3e00` at `+0x30` and 1 at `+0x34`; `+0x0c` and `+0x40` are 0, and `+0x50`–`+0x73` hold the same bytes in every entry.

## Playing a note

**With the retail bank, a drum plays from its note on to the end of its sample, unless eight others are playing when a ninth starts.** `HmiDigiMidi_SendEvent` (`00081789`)(far event, length, slot) answers 0 to every event, and acts on three kinds, its channel the status byte's low four bits:

- **A note on with a velocity**, for a note with a sample:
  1. unless the block's `+0x24` is `0xffff8000`, it stops the voice that plays the note on this channel;
  2. when the slot keeps track of as many voices as its limit, it stops the first voice of this channel ([the voices](#the-voices)); with none on this channel it stops none;
  3. with the velocity switch set, it writes ((the slot's volume × velocity) >> 7) << 8 to the block's `+0x14`;
  4. it writes the note to `+0x18` and, with bit `0x80` of the slot's pan clear, the pan << 9 to `+0x40`;
  5. it starts the block on the slot's digital driver (`HmiDigi_StartSample`) and keeps the voice it answers, with the note, the velocity and the channel. When step 2 found no voice to stop, the slot is still at its limit and the new voice is not kept: it plays on beyond the reach of this device's stops and changes.
- **A note on with velocity 0, and a note off**, for a note with a sample whose `+0x24` is not `0xffff8000`: stops the voice that plays the note on this channel.
- **Controller `0x7b`** stops every voice of the channel. **Controller 7** becomes the slot's volume, and each kept voice's volume becomes the value × its velocity × 2 (`HmiDigi_SetSampleVolume`). **Controller 10** becomes the slot's pan, and each kept voice's pan the value << 9 (`HmiDigi_SetSamplePan`, `0006e265`, which writes the voice's word `+0x54`, where the block's `+0x40` goes).

Any other event, a program change, another controller or a pitch bend, does nothing. The volume and the pan are the slot's, not a channel's: the last controller 7 or 10 on any channel sets them for every note. The volume starts at 127. The pan starts with bit `0x80` set, so until a controller 10 arrives a note plays with its block's own `+0x40`. The controller 7 the song player sends is already scaled by the song's and the master volume ([volume](song-player.md#volume)), so the drums follow the MUSIC level.

Every `DRUM32.DIG` block holds `0xffff8000` at `+0x24`, so a note's end and a second note on leave a playing drum alone, and a drum struck again while it sounds plays twice. Only the limit stops one: a note on with eight drums kept stops the first of its channel by place in the ring's array ([the voices](#the-voices)), which is not always the oldest.

### The voices

The slot keeps what it has started in 32 entries of four dwords at `HmiDigiMidi_Voices` (`000a25d8`) + `0x200` × slot: the digital driver's voice, the note, the velocity and the channel, -1 each when free. They form a ring: `HmiDigiMidi_VoiceOldest` (`000a2fec`) indexes the oldest, `HmiDigiMidi_VoiceNext` (`000a2fd8`) the next to write, `HmiDigiMidi_VoiceCount` (`000a3014`) counts them and `HmiDigiMidi_VoiceLimit` (`000a3000`) is the most kept, 8.

- **`HmiDigiMidi_AddVoice` (`0008212c`)**, (slot, voice, note, velocity, channel), writes the next entry and answers its index, or -1 at the limit.
- **`HmiDigiMidi_TakeChannelVoice` (`000821a7`)**, (slot, channel), finds the first entry of the channel by its place in the 32, not the oldest, moves the entries from the oldest to it up one place, frees the oldest place, and answers the found entry's voice, or -1. **`HmiDigiMidi_TakeNoteVoice` (`0008229d`)**, (slot, note, channel), does the same for an entry of that note and channel, and **`HmiDigiMidi_RemoveVoice` (`0008239b`)**, (slot, voice), for an entry of that voice. **`HmiDigiMidi_FindNoteVoice` (`00082483`)** answers the voice `HmiDigiMidi_TakeNoteVoice` would take and keeps the entry; `es2_xref` finds no caller.
- **`HmiDigiMidi_SampleCallback` (`00081e1d`)**, (driver, event, voice), the arguments `DigiSound_DoneCallback` (`0004e7fc`) reads ([the driver's calls back](../formats/sound-samples.md#the-vtables)): with event 2, the voice's end, it removes the voice from the slot `HmiDigiMidi_DriverSlot`[driver] holds.

## What the songs send

**Twelve songs hold drums: 26 tracks list `0xa005` alone** ([the devices](song-player.md#the-devices)): four in `BATTLE1B`, `INTRO1` and `INTRO2`, three in `WIN2`, two in `LOSING`, `MENU` and `VOLCANO`, and one in `BATTLE2B`, `BATTLE3B`, `DEATH`, `RUBBLE` and `SHELLC`. Their notes are all on channel 9 and are 27–30, 32, 33, 36, 39–41, 43, 45 and 49, each of which has a sample. Each note ends with a note on of velocity 0, and the tracks hold no note off and no controller `0x7b`. 25 of the tracks send a controller 7 once, and 17 a controller 10 of 64, which gives every later note the pan `0x8000`; the rest are program changes, controller `0x63` and the song player's own `0x6c`–`0x6f` ([the branch controllers](song-player.md#the-branch-controllers)).

## The callback device

**Device `0xa003` hands each event of its tracks to a far function its opener chooses.** `HmiCallbackMidi_SendEvent` (`00081719`)(far event, length, slot) far-calls `HmiCallbackMidi_Callback` (`000a2594`), 0 in the image, with the same arguments and answers what it answers. `HmiCallbackMidi_SetCallback` (`0008175a`), the device's bank function, stores its far pointer argument there and answers 0. The open, close and reset (`00081739`, `00081744`, `0008174f`) answer 0.

`SoundManager_LoadDrivers` opens `0xa003` only when the `[Music]` `Driver#` names it, which `SOUNDSET.INI` does not list ([the devices](song-player.md#the-devices)), and would then load the melodic and drum banks through its bank function, leaving the drum bank's address as the function each event is sent to.

## The copies

`ES1GO` holds the same code, each function's statements those of its `ES1DBSIM` original with its own addresses.

| `ES1DBSIM` | `ES1GO` |
|---|---|
| `HmiDigi_InitSystem`, `HmiDigi_UninitSystem`, `HmiDigi_InitDriver`, `HmiDigi_UninitDriver`, `HmiDigi_SetSamplePan` | `000606ff`, `0006076c`, `0006077e`, `00060f09`, `0005ea1d` |
| `HmiMidi_InitSystem`, `HmiMidi_UninitSystem`, `HmiMidi_LoadBank`, `HmiDigi_CallDriver2` | `0006181c`, `000619a8`, `00062bb9`, `0006da25` |
| `HmiCallbackMidi_GetFunctions`, `HmiCallbackMidi_SendEvent`, `HmiCallbackMidi_Open`, `HmiCallbackMidi_Close`, `HmiCallbackMidi_Reset`, `HmiCallbackMidi_SetCallback` | `0006e6cd`, `0006e6d5`, `0006e6f5`, `0006e700`, `0006e70b`, `0006e716` |
| `HmiDigiMidi_GetFunctions`, `HmiDigiMidi_SendEvent`, `HmiDigiMidi_Open`, `HmiDigiMidi_Close`, `HmiDigiMidi_Reset`, `HmiDigiMidi_LoadBank`, `HmiDigiMidi_SampleCallback` | `0006e73d`, `0006e745`, `0006eac4`, `0006ec07`, `0006ec6c`, `0006ec8f`, `0006edd9` |
| `HmiDigiMidi_ClearVoices`, `HmiDigiMidi_CloseNoOp`, `HmiDigiMidi_AddVoice`, `HmiDigiMidi_TakeChannelVoice`, `HmiDigiMidi_TakeNoteVoice`, `HmiDigiMidi_RemoveVoice`, `HmiDigiMidi_FindNoteVoice` | `0006f081`, `0006f0e3`, `0006f0e8`, `0006f163`, `0006f259`, `0006f357`, `0006f43f` |
| the tables and globals from `HmiMidi_SystemStarted` (`000a2570`) to `HmiDigiMidi_BankSignature` (`000a3fcc`) | `0x13a98` lower: `0008ead8`–`00090534` |
| `HmiMidi_DriverArg`, `HmiDigiMidi_DriverSlot` | `00091eec`, `00093db0` |

## Open

- **Open:** what the digital driver does with the start block's fields the drum bank sets, for module E: `+0x24` `0xffff8000` (the voice's word `+0x4a`), `+0x10` 2, `+0x30` `0x3e00`, `+0x34` 1, the flags `0x100`, and the pan 0 a note plays with before a controller 10; and what its image does with the init block's rate, buffer and `+0x14`.
- **Open:** a writer of `HmiMidi_DriverArg` (`000a5984`) through a pointer or a block base: `es2_xref --decode`'s absolute-operand search finds none, and no other search was run.
- **Open:** the drums against the MUSIC level in the running game. A retail check: in a mission playing `BATTLE1B`, `BATTLE2B` or `BATTLE3B`, lowering the F12 panel's MUSIC level lowers the drums with the melody.
