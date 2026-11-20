# The HMI driver containers

**The sound drivers ship as three files in `SOS\`, each a run of `.com` driver images behind a short header.** `HMIMDRV.386` holds the MIDI drivers the [music](../sound/song-player.md#the-devices) plays through, `HMIDRV.386` the digital drivers the [sounds](../sound/sound-drivers.md#the-driver) play through, and `HMIDET.386` the drivers that detect a card. The three share one layout.

## Layout

```
+0x00  byte       0x33; 31 zero bytes follow
+0x20  int32      image count
+0x24  int32      offset of the first entry, 0x2c
+0x28  int32      the low 16 bits of the file's length, sign-extended
+0x2c  the entries, back to back:
         +0x00  32     the image's name, zero-terminated
         +0x20  int32  the image's size + 0x5c
         +0x24  int32  the image's size
         +0x28  int32  the device id
         +0x2c  int32  flags
         +0x30         the image
```

In all three files the last entry's image ends at the file's end, and every image's word at `+5` repeats its entry's device id. The bytes of a name's field after its terminator are not zero in every entry.

| File | Size | Images | Device ids | Flags |
|---|---|---|---|---|
| `HMIMDRV.386` | 115,909 | 8 | `0xa000`–`0xa00a` | 0 in all |
| `HMIDRV.386` | 261,092 | 96, of 82 ids | `0x1000`–`0xe107` | `0x4000`, `0x8000` or `0xc000` |
| `HMIDET.386` | 87,250 | 81, of 65 ids | `0xe000`–`0xe068` | `0x4000`, `0x8000` or `0xc000` |

`HMIMDRV.386`'s images, in file order:

| Name | Device id | Size | Its text names |
|---|---|---|---|
| `fmmidi.com` | `0xa002` | `0x3028` | OPL2 |
| `fmmidi3.com` | `0xa009` | `0x3608` | OPL3 |
| `awemidi.com` | `0xa008` | `0xa08d` | AWE32 |
| `smii.com` | `0xa000` | `0x1f3` | Sound Master II |
| `mpu401.com` | `0xa001` | `0x2a5` | MPU 401 |
| `mt32.com` | `0xa004` | `0x298` | MT 32 |
| `gusmidi.com` | `0xa00a` | `0xb228` | Gravis UltraSound |
| `intspkr.com` | `0xa006` | `0x304` | PC Speaker |

## An image

```
+0x00  5      a 32-bit jump (E9) to the image's entry
+0x05  word   the device id
+0x07         text: "HMI MIDI Driver For <device>", a copyright line and
              "All Rights Reserved", each ending CR LF, then 0x1a
              a zero-terminated short name, "<device> MIDI" in HMIMDRV.386
```

An image is flat 32-bit code and data together, addressed from 0: `fmmidi.com`'s code reads and writes its own data through absolute operands that are offsets into the image, such as the word at `0x4fe`. Its jump lands on `0x504`, as `fmmidi3.com`'s does. `ES1FMMIDI` names `fmmidi.com`'s image, and addresses given for it are offsets into the image; what its entry and its five functions do is [the FM driver](../sound/fm-driver.md#the-images-interface).

## Loading an image

**The game's HMI library reads one image out of `hmimdrv.386` into memory of its own and calls it through two selectors.** `HmiMidi_LoadDriverImage` (`ES1DBSIM 00081365`, `ES1GO 0006e321`)(device, slot):

1. answers 10 for a slot above 5, 9 when the slot's flag (`ES1DBSIM 000a12e4` + 4 × slot) is set, and 6 for a device outside `0xa000`–`0xa200`; slot 5, one past the five driver slots, passes the first test and reads its flag from `000a12f8`, the first entry of step 6's table;
2. opens the library's driver path (`ES1DBSIM 000a575c`) with `hmimdrv.386` appended, answering `0xf` when it does not open, and reads the `0x2c`-byte header;
3. reads entry headers of `0x30` bytes until one's device id is the one asked for, up to the image count + 1 of them, seeking past each other image by its size `+0x24`;
4. for the match, allocates the size plus `0x1068` bytes, answering 5 when that fails, takes the first 4 KB boundary in it, makes two selectors for that address, one for data and one given the code segment's access rights, reads the image there and locks its memory;
5. closes the file;
6. stores the device id of the last entry header read at `000a12f8` + 4 × slot and sets the slot's flag, whether or not an entry matched;
7. answers 7 when no entry matched, and otherwise 0 with the image's address, the two far pointers (offset 0 of each selector) and the allocation.

`HmiDigi_LoadDriverImage` (`ES1DBSIM 00080dd1`, `ES1GO 0006dd8d`), which `HmiDigi_InitDriver` calls ([opening](../sound/drum-device.md#opening)), reads `hmidrv.386` the same way, with its own flags, device ids and path copy, and three differences: it answers 6 for a device outside `0xe000`–`0xe200`; an entry matches only when its flags have bit `0x4000`, so of a device id's two images (14 of the 82 ids have two) it loads the one with `0x4000`; and it answers 0 when no entry matched, as for `e05b` and `e05c`, whose one image each has flags without `0x4000`. For the Sound Blaster 16's 8-bit devices that is `sb168m.com` (`e015`, mono) and `sb168s.com` (`e016`, stereo), not `sw168m.com` and `sw168s.com`, whose flags are `0x8000`. Each opens with the text `HMI DMA Driver For SB16 8 Mono` or `Stereo`, and its entry calls the function whose offset a table of dwords holds at the request number in `AX`, eleven of them non-zero (the table is at `0x102` in `sb168m.com`, `0x104` in `sb168s.com`), with the caller's `BX` moved to `AX` (`SbDigi_Entry`, `ES1SB168M 000001e1`). `ES1SB168M` and `ES1SB168S` name the two images.

**A digital driver image does not mix: the library loads a second image from `hmidrv.386`, a mixer, which mixes the voices into the buffer the card plays.** The card image's request 8 answers the offset of its information block (`0x76` in `sb168m.com`), whose dword `+0x68` is the mixer's device id: `0x1000`, `tr8m.com`, for `sb168m.com`, and `0x1001`, `tr8st.com`, for `sb168s.com`. The container's last 11 entries are the mixers, the `tr*.com` images, with ids from `0x1000` to `0x100d`. `HmiDigi_LoadMixerImage` (`ES1DBSIM 00081082`, `ES1GO 0006e03e`)(device, …) reads one the way `HmiDigi_LoadDriverImage` reads a driver, with three differences: it answers 6 for a device outside `0x1000`–`0x1023`; it allocates the size plus `0x5068` bytes and reads the size plus `0x4000` there; and it keeps no slot flag or device id. `HmiDigi_InitDriver` loads the mixer the init block's `+0x3c` names, or the card image's when that is 0, unless the init block's far pointer `+0x30` already holds one ([opening](../sound/drum-device.md#opening)). Its request 10, from `HmiDigi_OpenMixer` (`ES1DBSIM 000809b8`), takes the buffer in `ES:EDI`, its size in `ECX`, the DMA channel's count port in `EDX` and the driver number in `EBX`, and answers the mix handler's offset in `EDI` and the offset of its 32 voice records, `0x6c` bytes apart, in `ESI`. What the card image and the mixer do is [the mixer](../sound/digital-mixer.md).

`HmiMidi_FreeDriverImage` (`ES1DBSIM 00081599`)(slot) is the reverse: 10 for a slot above 5 or one whose flag is clear, otherwise the allocation freed, the flag cleared, the two selectors freed and 0.

`HmiMidi_InitDriver` ([the drivers](../sound/song-player.md#the-drivers)) loads an image only when its caller's block holds none, its far pointer `+0x04` being 0. It keeps the new image's data and code far pointers, address and allocation in the block's `+0x04`, `+0x0c`, `+0x14` and `+0x28`, and in the slot's tables (`ES1DBSIM 000a127c`, `000a129c`, `000a12bc`, `000a12d0`). When the block already holds an image, it stores the block's values in the slot's tables except the code far pointer `000a129c`. It then asks the image for its functions, sets the slot's flag, gives the caller the slot and calls the image's open. An error from the load or the open is its answer, and the slot keeps the device id: after the load's 7 or a failed open, the slot's flag stays set too.

The library enters the image by a far call to offset 0 of the code selector, the image's opening jump, with a request number in `EAX` and the data selector in `FS` (`EDI` 0):

| `EAX` | Asked by | The image answers |
|---|---|---|
| 0 | `HmiDriver_CopyInfo` (`ES1DBSIM 00081649`), for which `es2_xref.py` finds no call | `EDX`, the offset of `0x6a` bytes the library copies out |
| 1 | `HmiDriver_GetFunctions` (`ES1DBSIM 00081687`), from `HmiMidi_InitDriver` | `EDI`, the offset of a table of five 8-byte entries, each opening with a function's offset; the library pairs each offset with the code selector as [the driver slot's](../sound/song-player.md#the-drivers) first five far functions |
| 2 | `HmiDriver_Call` (`ES1DBSIM 000816cd`), which passes any number, from `HmiMidi_InitDriver` for device `0xa00a` alone | a far function the library registers with its timer at 120 calls a second |

Every later call is a far call to one of the five functions, its arguments on the stack.

## Open

- **Open:** the header's byte `0x33` and its `+0x28`, and what an entry's flag `0x8000` marks; the digital load skips an entry that has it without `0x4000`.
- **Open:** what the images other than `fmmidi.com` hold between their short name and their entry.
- **Open:** which registers the other images' entries read; `fmmidi.com`'s reads `AX` and `FS`, and what the `0x6a` bytes of its request 0 mean is [the FM driver's](../sound/fm-driver.md#open) item.
