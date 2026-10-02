# AVI cutscenes

The game's full-motion video sits loose in `ES2/AVI/` as 93 `.AVI` files — mission briefings, the two-part intro, the territory maps on the campaign screen, the ending and the credits. They are ordinary Microsoft RIFF AVI containers holding one video stream and, usually, one uncompressed PCM audio stream.

## What the corpus uses

| Compression | Files | Geometry | Decoder |
|---|---|---|---|
| `IV32` — Indeo Video 3.2 | 80 | 240x180, 288x180 | yes, see `docs/formats/indeo3.md` |
| `CRAM` — Microsoft Video 1 | 6 | 292x200, 640x480 | yes |
| `BI_RLE8` — Microsoft RLE | 4 | 295x226 | yes |
| `cvid` — Cinepak | 2 | 576x360, 288x180 | yes |
| `IV41` — Indeo Video 4.1 | 1 | 288x180 | yes, see `docs/formats/indeo4.md` |

Video runs at 15 fps except the Microsoft Video 1 files, which are 10 fps, and `ESTAB2.AVI`, which is three frames at 1 fps. Audio, where present, is format tag 1 — uncompressed PCM — as either 8-bit stereo at 11025 Hz or 16-bit mono at 22050 Hz. The four MS-RLE thumbnails and all six Microsoft Video 1 files have no audio stream at all.

Every one of the 93 files parses as a container. Only the codecs differ.

The five 292x200 Microsoft Video 1 files — `ALPHA`, `BRAVO`, `DELTA`, `OMICRON` and `LUNA` — carry a second video stream after the first: `AASC` (Autodesk Animator), 8-bit, 295x200, with as many frames as the first. Its first frame fills the picture with palette index `0xff` and every later one changes nothing. The shell's MCI `open` and `play` strings name no stream, and the `avivideo` device shows the first video stream unless told otherwise.

## Container

Standard RIFF: a chain of `[fourcc][int32 length][body]` records with bodies padded to an even length, where `RIFF` and `LIST` bodies begin with a further four-character code and hold chunks of their own. `AviFile` reads the `hdrl` list for stream headers and the `movi` list for packets.

Streams are numbered by position — the n-th `strl` list is stream n — and that number is what the two ASCII digits leading each `movi` chunk id refer to. The suffix says what the payload is: `dc` or `db` for video, `wb` for audio. A packet is only accepted when the suffix and the stream header agree about the kind, so a mislabelled chunk cannot land in the wrong queue, and only from the first stream of its kind, so the second video stream above does not interleave its packets with the first's.

Packets are grouped into `rec ` lists in some files and loose in others, so the walk descends through lists rather than assuming either layout.

### The index is not followed

`idx1`, where present, is a table of offsets and lengths supplied by the file. Following it means dereferencing attacker-chosen pointers. `AviFile` instead walks the `movi` list, so every packet's bounds were established by the walk itself.

This costs nothing: the index is not needed to enumerate frames, and because every codec in this corpus is interframe, seeking to an arbitrary frame would require decoding from the previous key frame anyway.

## Microsoft RLE

The four `*_TH.AVI` territory thumbnails. Eight bits per pixel over a palette carried in the stream format header as BGRX quads, rows bottom-up.

The payload is a stream of two-byte opcodes. A non-zero first byte is a run of that many pixels in the colour named by the second. A zero first byte makes the second an escape:

| Escape | Meaning |
|---|---|
| 0 | end of row — drop a line, return to column zero |
| 1 | end of frame |
| 2 | delta — two bytes follow giving a (right, up) skip |
| 3 and above | that many literal palette indices follow, padded to an even length |

Skipped pixels keep what the previous frame left, which is why the codec is interframe and why the thumbnails work: each is a still map with one territory blinking, so every frame after the first is deltas and end-of-row escapes over an unchanged background.

The 4bpp variant, which packs two indices per byte, appears nowhere in the corpus and is rejected rather than guessed at.

## Microsoft Video 1

The six `CRAM` files. Sixteen bits per pixel as RGB555, over 4x4 blocks. Blocks run left to right along a block row, block rows bottom-up, and within a block the pixel rows are bottom-up too, so the codec shares the DIB row order. A 292x200 picture is 73x50 blocks.

A packet is a stream of little-endian words, each opening one of four forms:

| Word | Form | Words after it |
|---|---|---|
| `0x8400`-`0x87ff` | skip: the low ten bits count blocks left as the previous frame had them | none |
| any other with bit 15 set | one colour, the word's low 15 bits, over the whole block | none |
| below `0x8000`, next word's bit 15 clear | two colours: the word is 16 flags, then colours A and B | 2 |
| below `0x8000`, next word's bit 15 set | eight colours: the word is 16 flags, then eight colours, one A/B pair per 2x2 quadrant | 8 |

Flag bit n is pixel n in stream order — four to a row, the block's bottom row first — and a set bit picks colour A. The eight-colour pairs go bottom-left, bottom-right, top-left, top-right. Bit 15 of the first colour is only the eight-colour marker; the colour is its low 15 bits.

Every packet in the corpus codes exactly as many blocks as the picture has, then one `0x0000` word.

## Cinepak

`CREDITS.AVI`, which the shell plays as movie `0x54`, and `ES2CREDC.AVI`, which no movie id names. The same credits, blue on navy in the first and navy on orange in the second. 24 bits per pixel, over 4x4 blocks whose colours come from codebooks, held in horizontal strips: `CREDITS` has three of 120 rows, `ES2CREDC` one of 180. Every multi-byte field is big-endian. Unlike the other codecs here, rows run top-down whatever the sign of the format header's height.

A packet is a 10-byte frame header, then its strips:

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | flags: bit 0 clear makes each strip after the first start from a copy of the previous strip's codebooks |
| 1 | 3 | packet length |
| 4 | 2 | width |
| 6 | 2 | height |
| 8 | 2 | strip count |

A strip is a 12-byte header — an id, `0x10` for a key strip and `0x11` otherwise, a 3-byte length counting the header, then top, left, bottom and right as 2-byte fields — followed by chunks, each a 1-byte id and a 3-byte length counting its own 4-byte header. Every strip in the corpus has top and left 0, bottom its own height and right the picture width, so strips stack down the picture in order.

Each strip keeps two codebooks of 256 entries across frames, V1 and V4. An entry is six bytes: four luma values for a 2x2 patch — top-left, top-right, bottom-left, bottom-right — then signed chroma U and V, shared by all four. A pixel's colour is R = Y + 2V, G = Y − U/2 − V, B = Y + 2U, clamped to 0-255, with U/2 truncating toward zero.

| Chunk | Meaning |
|---|---|
| `0x20` | V4 codebook, entries loaded in order from 0 until the chunk ends |
| `0x21` | V4 codebook, selective: a 32-bit flag word before each 32 entries, most significant bit first, and only flagged entries follow |
| `0x22` | V1 codebook, as `0x20` |
| `0x23` | V1 codebook, as `0x21` |
| `0x30` | blocks, a flag per block: set is V4 — four indices — and clear is V1, one index |
| `0x31` | blocks, as `0x30` but each block first has a flag of its own: clear leaves it as the previous frame had it, and only a set one reads the V4/V1 flag |
| `0x32` | blocks, no flags: every block is V1 |

Blocks run left to right, then top to bottom through the strip. In `0x30` and `0x31` the flags come from one 32-bit stream read most significant bit first, with a fresh word taken whenever the last is used up — interleaved with the indices, so a word can fall between a block's update flag and its V4/V1 flag. A V1 block scales its entry up: each of the four luma values covers a 2x2 quarter of the block. A V4 block takes four entries, one per 2x2 quarter in the order top-left, top-right, bottom-left, bottom-right, each painting its own quarter pixel for pixel.

Key frames — those whose flags byte is 0 — lead with a key strip carrying `0x20` and `0x22`; every other strip carries `0x21` and `0x23`. The corpus uses no chunk outside the table: the luma-only codebooks `0x24`-`0x27`, which carry four bytes per entry, appear in neither file. Ten of `ES2CREDC`'s packets are empty, which leaves the picture as it was.

Decoding both files this way matches Windows' own Cinepak codec, `iccvid.dll`, pixel for pixel.

## Audio

`AviAudioTrack` concatenates a stream's packets and widens them to signed 16-bit. 8-bit WAVE data is unsigned with `0x80` as silence, so it is widened about that midpoint rather than shifted.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Dispatch the decoder on the `strh` handler fourcc | The handler and the format compression disagree. The six Microsoft Video 1 files name `msvc` as their handler but carry `CRAM` in `strf`, and the four MS-RLE files name `mrle` but carry the numeric `BI_RLE8`. Dispatching on the handler finds none of the ten; the compression in `strf` is what names the codec. |
| A Microsoft Video 1 word with bit 15 set is a colour carrying a flag | Words like `0x98f0` sit where a flag word is expected and differ from ordinary colour values only in bit 15, which reads as a flag on a colour. Outside the skip range such a word is a one-colour block and consumes nothing after it; runs of them are flat areas of the picture. |
| A Cinepak strip's top and bottom are rows of the picture | Every strip in `CREDITS` reads top 0, bottom 120, so taken as picture rows all three strips paint the top third. The bottom is the strip's height, and strips stack in order. |
| Cinepak follows the DIB row order like the other codecs | Cinepak's rows are top-down regardless of the format header, so flipping it by the sign of `biHeight`, as Microsoft RLE and Microsoft Video 1 need, turns the credits upside down. |
| A positive `biHeight` means top-down rows | It means bottom-up, which is the DIB convention and what every file in this corpus uses. A negative height is the top-down case. Reading the sign the other way renders every frame vertically mirrored — recognisable, so it survives a careless glance. |
