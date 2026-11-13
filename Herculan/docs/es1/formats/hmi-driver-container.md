# The HMI driver containers

**The sound drivers ship as three files in `SOS\`, each a run of `.com` driver images behind a short header.** `HMIMDRV.386` holds the MIDI drivers the [music](hmp-music.md#the-devices) plays through, `HMIDRV.386` the digital drivers the [sounds](sound-samples.md#the-driver) play through, and `HMIDET.386` the drivers that detect a card. The three share one layout.

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

An image is flat 32-bit code and data together, addressed from 0: `fmmidi.com`'s code reads and writes its own data through absolute operands that are offsets into the image, such as the word at `0x4fe`. Its jump lands on `0x504`, as `fmmidi3.com`'s does. `ES1FMMIDI` names `fmmidi.com`'s image, and addresses given for it are offsets into the image.

## Open

- **Open:** the header's byte `0x33` and its `+0x28`, and an entry's flags.
- **Open:** what an image holds between its short name and its entry, and how the library calls the entry; `fmmidi.com`'s code runs to `0x243f` of its `0x3028` bytes.
