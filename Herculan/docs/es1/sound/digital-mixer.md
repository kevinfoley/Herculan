# SB168M.COM, TR8M.COM and TR8ST.COM — the digital driver and its mixer

**The Sound Blaster 16's 8-bit driver images only program the card; a second image, the mixer, adds the busy voices into the buffer the card plays, at a gain linear in each voice's volume.** `HmiDigi_InitDriver` loads both from `SOS\HMIDRV.386` and opens the mixer on the buffer ([loading an image](../formats/hmi-driver-container.md#loading-an-image), [opening](drum-device.md#opening)); the sound objects reach the mixer's voice records through the library's calls ([playing](../formats/sound-samples.md#playing)). Addresses in `sb168m.com` are `ES1SB168M` image offsets. The mixers `tr8m.com` (device `0x1000`, `0x1f34` bytes, for `e015`) and `tr8st.com` (`0x1001`, `0x212a` bytes, for `e016`) are not registered binaries; their addresses are offsets into the image, as `es1_hmi_extract.py` extracts it.

## The card image

`sb168m.com`'s code runs from `0x1e1` to `0x5f5`. `SbDigi_Entry` (`000001e1`) calls the function the request table holds at the request number in `AX` ([loading an image](../formats/hmi-driver-container.md#loading-an-image)):

| Request | Function | Does |
|---|---|---|
| 0 | `SbDigi_Init` (`00000221`) | keeps the card's port, DMA channel and IRQ, resets the DSP and installs the interrupt handler |
| 1 | `SbDigi_Uninit` (`0000032b`) | DSP command `0xda`, exit 8-bit auto-initialized DMA, and the interrupt vector restored |
| 2 | `SbDigi_SetRate` (`00000372`) | DSP command `0x41`, the output rate |
| 3 | `SbDigi_SetDmaMode` (`000003a2`) | keeps the DMA mode's transfer bits |
| 4 | `SbDigi_StartDma` (`000003dc`) | programs the DMA channel with the buffer and starts DSP command `0xc4`, 8-bit auto-initialized output, with mode byte 0, mono and unsigned |
| 5 | `SbDigi_Stop` (`00000349`) | masks the IRQ and sends `0xda` |
| 6 | `SbDigi_Pause` (`000004c4`) | DSP command `0xd0` |
| 7 | `SbDigi_Resume` (`000004d4`) | DSP command `0xd4` |
| 8 | `SbDigi_GetInfo` (`000004e4`) | answers `0x76`, the information block, whose dword `+0x68` is the mixer's device id |
| 9, 10 | `SbDigi_NoOp` (`000004ea`) | return at once |

The rest is `SbDigi_WaitWrite` (`000004eb`), the wait for the DSP to take a byte; `SbDigi_InstallHandler` (`00000504`) and `SbDigi_RestoreHandler` (`0000054c`), the interrupt vector's install and restore through the DOS extender's `INT 21h` functions `0x2502`, `0x2503`, `0x2506` and `0x2507`; `SbDigi_UnmaskIrq` (`0000056c`) and `SbDigi_MaskIrq` (`0000058e`); and `SbDigi_InterruptHandler` (`000005b0`, `0x5b4` in `sb168s.com`), the image's last bytes, which acknowledges the card and the interrupt controllers and returns with `IRETD`. Nothing in the card image mixes.

## The mixer image

The mixer's request table is at `0x142a`. Request 8 (`0x1486`) answers `0x13c0` in `EDX`; requests 9 and 10 run `0x148c`. Request 10, the open `HmiDigi_OpenMixer` makes, keeps the driver number at `0x1320`, the DMA count port at `0x1456`, the buffer's size at `0x1312` and its selector and offset at `0x1308` and `0x1304`, and the library's data segment, from `GS`, at `0x131a` for the callbacks. It answers the mix handler `0x14c6` in `EDI`, the 32 voice records at `0x380`, `0x6c` bytes apart, in `ESI`, and `0x12e0` in `ECX`. The library's voice pointers point into those records, so `HmiDigi_StartSample` and `HmiDigi_SetSampleVolume` write the mixer's own data.

## The mix

**The handler (`0x14c6`) is the timer event the drum device's open registers at 120 calls a second ([opening](drum-device.md#opening)); each call mixes the part of the buffer the card has played since the last.** Guarded against re-entry by `0x1316`, it:

1. lists the voices whose word `+0x30` has bit `0x8000` set and bit `0x1000` clear, the list at `0x11d8` and its count at `0x1258`;
2. reads the DMA channel's count port, two bytes, doubled for a 16-bit channel, to find where the card is playing;
3. fills the region to mix with `0x80`, silence;
4. for each listed voice, calls the routine for the voice's key, (flags & `0x7b0`) << 5 | the byte `+0x36`, from a cache of 31 at `0x1340`, building the routine on a miss;
5. then handles the voice's end: its repeat count `+0x38`, its loop (bit `0x40` of the flags, with `+0x10`, `+0x20` and `+0x2c`) and its callback `+0x3c`, called with the driver, an event 0, 1 or 2 and the voice ([Open](#open)).

### The fragments

A mix routine is built from code fragments. The fragment table at `0x15` has 36 entries of 10 bytes: the fragment's offset, a dword, then a word at `+4` marking a forward branch and a word at `+6` marking a branch target. Each fragment is copied up to its first `0x90` byte. A recipe (`0x1c8e`) lists the fragments for a key, and the assembler (`0x1e53`) joins them into a routine at `0x1f34` + `0x200` × the cache index, past the image's end in the `0x4000` bytes the loader reads after it. The two mixers' fragments and their table (`0x15`–`0x380`) are the same bytes; only the recipes differ. The byte `+0x36`, the start block's `+0x10`, picks the layout: with 2, every digital sound's, each source sample is written to both bytes of a stereo pair, and the header bytes `0x0d` + *k* and `0x11` + *k* pick the add and store fragments.

### The gain

**For a voice with flag `0x100`, which every digital sound and drum note has, the gain is linear in the voice's volume `+0x32`, re-read on every mix call.** Fragment 3 in `tr8m.com`, `0xa` in `tr8st.com`, multiplies the word `+0x32` by the master word `0x12e4` (`0x7fff` in both images) with a word `IMUL`, then doubles `EDX`, leaving in `BH` bits 23 to 30 of the product: a gain *g* from 0 to 127, about the volume / 256. Per sample, fragment `0x14` turns the byte into a signed one (`XOR 0x80`) and fragment `0x15` multiplies it by *g* (`IMUL BH`) and shifts the product right by 7: the output is (*s* × *g*) >> 7. Fragment 2 adds it to the buffer's byte, taken as signed, with saturation at −128 and 127, and stores it back as unsigned.

A volume below `0x100` is silent, and a sound's loudness follows its volume in 128 steps. `HmiDigi_SetSampleVolume` writes `+0x32`, so a voice already playing takes a new volume at the next mix call. A placed sound's volume is linear in its distance ([placed sounds](../formats/sound-samples.md#placed-sounds)), so its fade is linear in amplitude: the mixer adds no curve of its own.

### The stereo pan

**`tr8st.com` pans a voice with flag `0x200`, a placed sound, by the word `+0x54`, the start block's `+0x40`; a voice without it plays at its gain on both channels.** For such a voice fragment `0x17` replaces `0x15`. It adds the word `+0x56`, the block's `+0x44`, which is 0 for every placed sound, to *p* = `+0x54` and keeps the sum there. The left level is `0xffff` − *p* when *p* ≥ `0x8000`, else `0x7fff`; the right level is *p* when *p* < `0x8000`, else `0x7fff`. The two gains are (volume × level) >> 23, kept at `0x1333` for the left and `0x132f` for the right, and fragments `0x18` and `0x19` apply them to the first byte of a pair, the left, and the second, the right.

So the center, `0x8000`, plays at full gain on both sides, and each end silences one side: `0x000f`, a placed sound straight along the world's x axis from the camera, plays in the left channel alone, and `0xffff`, one straight against it, in the right alone. A drum note starts with flag `0x100` alone ([the bank](drum-device.md#the-bank)), so it plays on both channels whatever pan its channel's controller 10 sets.

## Open

- **Open:** a voice's end in the handler's step 5: what a repeat count of 0 and of 255 play, how the loop range is walked, and which of the callback's events 0, 1 and 2 comes when.
- **Open:** the rate the mixer mixes at against the output rate request 2 sends the card, 11,025 Hz ([opening](drum-device.md#opening)): whether a sample is mixed one byte to one buffer byte.
- **Open:** a writer of the master word `0x12e4`. A search of both mixers' listings finds only reads, and `ES1DBSIM`'s listing has no operand `0x12e4`; no search through the library's far pointers into the image was run.
- **Open:** the 16-bit Sound Blaster 16 devices' mixers (`e017`, `e018`): whether they mix differently from `tr8m.com` and `tr8st.com`.
- **Open:** what flag `0x4000`, which every digital sound's start block carries, selects in the mixer.
