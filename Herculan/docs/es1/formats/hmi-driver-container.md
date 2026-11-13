# The HMI driver containers

**The sound drivers ship as three files in `SOS\`, each a run of `.com` driver images behind a short header.** `HMIMDRV.386` holds the MIDI drivers the [music](../sound/song-player.md#the-devices) plays through, `HMIDRV.386` the digital drivers the [sounds](sound-samples.md#the-driver) play through, and `HMIDET.386` the drivers that detect a card. The three share one layout.

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

**The game's HMI library reads one image out of `hmimdrv.386` into memory of its own and calls it through two selectors.** `HmiMidi_LoadDriverImage` (`ES1DBSIM 00081365`, `ES1GO 0006e321`), for a device id from `0xa000` to `0xa200` and a free driver slot:

1. opens the library's driver path (`ES1DBSIM 000a575c`) with `hmimdrv.386` appended, returning `0xf` when it does not open, and reads the `0x2c`-byte header;
2. reads entry headers of `0x30` bytes, one more than the image count at most, skipping each image whose device id is not the one asked for by its size `+0x24`;
3. for the match, allocates the size plus `0x1068` bytes, takes the first 4 KB boundary in it, makes two selectors for that address, one for data and one given the code segment's access rights, reads the image there and locks its memory;
4. returns 7 when no entry matched, and otherwise 0 with the image's address, the two far pointers (offset 0 of each selector) and the allocation.

The library enters the image by a far call to offset 0 of the code selector, the image's opening jump, with a request number in `EAX` and the data selector in `FS` (`EDI` 0):

| `EAX` | Asked by | The image answers |
|---|---|---|
| 0 | `HmiDriver_CopyInfo` (`ES1DBSIM 00081649`), for which `es2_xref.py` finds no call | `EDX`, the offset of `0x6a` bytes the library copies out |
| 1 | `HmiDriver_GetFunctions` (`ES1DBSIM 00081687`), from `HmiMidi_InitDriver` | `EDI`, the offset of a table of five 8-byte entries, each opening with a function's offset; the library pairs each offset with the code selector as [the driver slot's](../sound/song-player.md#the-drivers) first five far functions |
| 2 | `HmiDriver_Call` (`ES1DBSIM 000816cd`), which passes any number, from `HmiMidi_InitDriver` for device `0xa00a` alone | a far function the library registers with its timer at 120 calls a second |

Every later call is a far call to one of the five functions, its arguments on the stack.

## Open

- **Open:** the header's byte `0x33` and its `+0x28`, and an entry's flags.
- **Open:** what the images other than `fmmidi.com` hold between their short name and their entry.
- **Open:** which registers the other images' entries read; `fmmidi.com`'s reads `AX` and `FS`, and what the `0x6a` bytes of its request 0 mean is [the FM driver's](../sound/fm-driver.md#open) item.
