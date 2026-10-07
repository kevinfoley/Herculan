# Indeo Video 3.2

Intel's `IV32`, the compression behind 80 of the 93 files in `ES2/AVI/` — both halves of the intro and every mission briefing. All are 240x180 or 288x180 at 15 fps. See `docs/retail/formats/avi-video.md` for the container they sit in.

`Codecs/Indeo3/Indeo3Decoder.cs` in `HercWorks.Video` is the decoder. Every frame of all 80 files decodes without a malformed-stream rejection.

Addresses are virtual addresses in `IR32_32.DLL`, the Intel codec the game's installer drops in `ES2/INDEO/`. Its image base is `10000000`.

## Frame layout

Each `00dc` chunk holds one frame: a 16-byte frame header, a 48-byte bitstream header, then the three planes' data. A chunk can run past the frame's stated size; the excess is padding.

Frame header, four little-endian `uint32`: frame number, a field that is zero throughout this corpus, a checksum, and the size of the bitstream in bytes. The checksum is the XOR of the other three with `FRMH` read big-endian (`0x46524D48`), which holds on all 12,027 frames of the corpus.

Bitstream header:

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | version; `0x20` for 3.2 |
| 2 | 2 | frame flags |
| 4 | 4 | bitstream size **in bits** |
| 8 | 1 | `cb_offset`, added to every cell's codebook index |
| 9 | 1 | reserved |
| 10 | 2 | checksum, zero throughout this corpus |
| 12 | 2 | height |
| 14 | 2 | width |
| 16 | 4 | Y plane offset |
| 20 | 4 | V plane offset |
| 24 | 4 | U plane offset |
| 28 | 4 | reserved |
| 32 | 16 | `alt_quant`: sixteen codebook pairs, primary in the high nibble |

Plane offsets are relative to the start of the bitstream header, and the planes are laid out U, V, Y — not the order the header lists them in. Each plane runs to the next plane's start, or to the end of the bitstream.

A bitstream of exactly 16 bytes is a sync frame, with no picture; it leaves the display unchanged.

Frame flags:

| Bit | Meaning |
|---|---|
| 1 | 8-bit pixels |
| 2 | key frame |
| 4, 5 | half-pel motion vectors, vertical and horizontal |
| 8 | not referenced by a later frame |
| 9 | which of the two frame buffers this frame decodes into |

Bits 1, 4 and 5 are set on no frame in the corpus. The decoder rejects a frame that sets them.

`alt_quant` is the same in every frame of `INTR_PT1.AVI`: high nibbles running 0 to 15 and low nibbles cycling 2, 4, 6, 8, 10, 12, 14, 15.

## Planes and buffers

Pixels are 7-bit, doubled to 8 on output. Chroma is 4:1:0: one U and one V sample per 4x4 luma block, so a 288x180 frame carries a 288x180 luma plane and two 72x48 chroma planes. The chroma plane is the luma size divided by four each way and then rounded up to a multiple of 4, which is where 48 comes from rather than 45.

The codec keeps two buffers per plane. Frame flag bit 9 names the one a frame decodes into and displays, and inter cells read from the other. A frame is not always decoded on top of the one before it: `INTR_PT1.AVI` opens with two key frames into buffer 0, then frames into buffer 1 that reference buffer 0.

Each buffer carries one extra row above the picture, filled with `0x40`. Intra cells on the top edge predict from it.

## The cell tree

A plane's data opens with a `uint32` motion-vector count, at most 256, then that many (y, x) pairs of signed bytes. The rest is the cell tree, and the cells' data is interleaved in it.

The tree is read two bits at a time, MSB first:

| Code | Motion tree | VQ tree |
|---|---|---|
| 0 | split horizontally | split horizontally |
| 1 | split vertically | split vertically |
| 2 | intra: enter the VQ tree with no motion vector | null cell: one more code follows |
| 3 | inter: a motion-vector index byte follows, then enter the VQ tree | coded cell: its data follows |

Decoding starts in the motion tree, with the whole plane as one cell. A split cuts off the first half, which is decoded first, and the following codes apply to the second half. A cell is measured in 4x4 blocks, and a split of *n* blocks takes `((n + 2) >> 2) << 1` of them, or 1 when *n* is 2 or less.

Planes are cut into vertical strips first: 40 blocks wide for luma (160 pixels) and 10 for chroma. A vertical split of a cell wider than a strip cuts at a strip boundary rather than in half.

A null cell's extra code is 0 or 1. Either way the cell is copied from the reference buffer, displaced by its motion vector. A null cell in an intra region is malformed.

A motion-vector index byte and a coded cell's data both start at the next byte boundary after the two-bit code that introduced them. The bit cursor does not skip the bytes they used straight away. It carries on through the rest of the current byte, and skips them the next time it reaches a byte boundary.

Motion vectors displace a cell into the other buffer. A displaced cell may reach the prediction row above the picture, but no further.

## Cell data

A coded cell's first byte is its descriptor: the mode in the high nibble and a codebook index in the low.

| Mode | Block | Coded rows | Allowed |
|---|---|---|---|
| 0 | 4x4 | every row | intra, inter |
| 1 | 4x4 | every row, alternating codebooks | intra, inter |
| 3 | 4x8 | odd rows; even rows interpolated | intra only |
| 4 | 4x8 | as mode 3, alternating codebooks | intra only |
| 10 | 8x8 | odd rows, each delta doubled across two pixels; even rows interpolated | intra |
| 10 | 8x8 | every row, each coded line applied to a pair of rows | inter |
| 11 | 4x8 | every row, each coded line applied to a pair of rows | inter only |

Modes 0, 3, 10 and 11 read one codebook, the descriptor's index plus `cb_offset`. Modes 1 and 4 look the index up in `alt_quant`. The low nibble of that pair is the codebook for even lines and the high nibble is the codebook for odd lines, each plus `cb_offset`. A codebook number of 24 or more is malformed.

The prediction is the row above the cell for an intra cell, or the displaced block in the other buffer for inter modes 0 and 1. Inter modes 10 and 11 copy the displaced cell into place first, then add their deltas to it in place.

Before a cell decodes, its prediction row is remapped through the [requantisation table](#the-requantisation-table) when the codebook index is 8 or more. The row is rewritten in whichever buffer it sits in. For modes 1 and 4 the test uses the descriptor's own nibble; for the others it uses the nibble plus `cb_offset`. Either way the table row is that value's low three bits.

Blocks are visited left to right and then top to bottom within the cell. Each block is four coded lines, and each line is one byte:

- **Below the codebook's dyad count:** a dyad code. The next byte is a second dyad code. The first byte's dyad goes on pixels 2–3, the second byte's on pixels 0–1.
- **From the dyad count to `0xF7`:** a quad code. Subtract the dyad count and divide by the codebook's quad side: the quotient is the dyad for pixels 0–1, the remainder the dyad for pixels 2–3. Codebooks 16 and up swap the two.
- **`0xF8` and above:** escapes.

A dyad is added to its two pixels as one 16-bit value and masked with `0x7F7F`, so a negative first delta borrows from the second pixel. That borrow is already built into the dyad's value; see [The seed area](#the-seed-area). Mode 10 intra uses the widened dyad instead, `(a, a, b, b)`, added across four pixels. At the top of a cell its prediction is the row above with every even pixel copied over its odd neighbour.

An interpolated row is the truncating average of the rows above and below it. At the plane's top edge it is a copy of the row below it instead.

Escapes, where "null" means the prediction is copied unchanged:

| Byte | Meaning |
|---|---|
| `F8` | malformed |
| `F9` | as `FA`, and the next block is skipped too |
| `FA` | only at line 0. Inter: the block is a null. Intra: the block is left untouched. |
| `FB` *n* | null to the end of this block, then *n* − 1 more blocks. *n* runs from 1 to 31. Adding `0x20` makes the run a skip, which in an intra cell of modes 0 to 4 leaves the blocks untouched. *n* of 64 or more is malformed. |
| `FC` | null to the end of this block, and for the next block |
| `FD` | null to the end of this block |
| `FE` | null to the end of line 2 |
| `FF` | null to the end of line 1 |

`FE` after line 2, or `FF` after line 1, is malformed. A skipped block in a mode 10 intra cell is filled from the row above like a null one.

## Codebooks

A cell's pixel data is a stream of one-byte VQ codes, and a codebook is what turns one of those bytes into the pixel deltas it stands for. Decode a cell against the wrong codebook and it comes out as noise.

The shipped codec does not carry them. The region from `10043000` runs 16 KB of zeroes, and the eight 512-byte objects at `10043e4c` that have exactly a codebook's shape are inside it. `.bss` is 144 KB with no file backing behind it, which is where the tables are built: codec init expands a much smaller seed into a 96 KB image, and the zero-filled objects are where the results are indexed from.

So the codebooks are derived rather than read. `Codecs/Indeo3/Indeo3Codebooks.cs` is that derivation.

### The seed area

`1004d26a` in `.data`, 5,251 bytes, ending at a zero count byte at `1004e6ec`. `Codecs/Indeo3/Indeo3SeedData.cs` holds it verbatim.

It is a run of variable-length blocks:

```
block := count            ; one byte, UNSIGNED
         pair[count]      ; two signed bytes each
         expand           ; one byte, SIGNED
area  := block* 0x00      ; a zero count byte ends it
```

The count being unsigned is load-bearing. The first block's is `0xC3`; read as -61 the walk desynchronises at the very first block, and — because a desynchronised walk still finds a zero byte eventually — it terminates rather than failing, so the mistake surfaces as a bad picture much later.

Walked correctly the area is exactly 24 blocks, one per codebook. Their counts run 195, 159, 133, 115, 101, 93, 87, 77 and then the same eight again, then 128 and seven 79s; their expansion bytes 7, 9, 10, 11, 12, 12, 12, 13 twice, then -11 and seven -13s. That regularity is itself the check that the walk stayed in step.

A block's pairs are its dyads, so the count is the codebook's dyad count, and the magnitude of the expansion byte is its quad side. The negative expansion bytes are codebooks 16 to 23, which are the ones that swap a quad's two dyads.

A pair's two bytes are the deltas for two adjacent pixels. Read together as one signed 16-bit value, high byte first, they are the unit the expansion arithmetic works in, and folding that value to 16 bits matters: carrying the wider intermediate through the shift in the expansion step below produces a different but still well-formed image.

### The expansion

Codec init builds a 96 KB image, tiled as two sets of 24 blocks of `0x800` bytes:

| Image offset | Contents |
|---|---|
| `+0x0000` | 24 blocks of paired deltas |
| `+0xc000` | 24 blocks of the same, byte-replicated |

Each `0x800` block is two 256-entry sub-tables, at `+0x000` and `+0x400`, addressed as 32-bit words.

Three passes, in this order, because each overwrites the last:

1. **Prefill.** Every word takes its own byte offset from the image base; word 0 is left alone. These are not codebook entries. They are what an index past the end of a block's real content reads, and reproducing them matters only so a stream that indexes out of its block gets the same garbage the retail codec gets rather than a zero.

2. **Seeding.** Each of the block's `N` pairs becomes entry `k` of both its sub-tables, as the pair's 16-bit value biased by `0x8000` and moved into the top half of the word. The bias is what keeps the two coded pixels independent: it puts the delta add's carry out at bit 31, where it can be tested, instead of letting it borrow into the neighbouring pixel. The byte-replicated set gets the pair's two bytes as `(b, b, a, a)`, accumulated with 32-bit addition rather than assembled by OR — not the same thing, because a negative low byte borrows upward, so `(-2, -2)` gives `0xFDFDFDFE` and not `0xFEFEFEFE`. The second sub-table of that set holds the same word with bit 31 flipped.

3. **Expansion.** A further `d*d` entries, `d` being the magnitude of the block's expansion byte, over ordered pairs `(i, j)` drawn from the first `d` seeds: `(value[i] << 16) + value[j]` as a 32-bit sum. These are the quad codes. The sign of the expansion byte decides which index runs outermost, and for a negative byte the replicated set's second sub-table again takes the bit-31 flip.

Entries from `count + d*d` up to 255 keep their prefill.

### Applying a mode byte

The retail codec's form of the dyad add. A row predictor holds four 7-bit pixels packed one per byte, so one 32-bit add against a codebook entry updates all four at once.

The entry is read from the block's `+0x400` sub-table at the mode byte's index. Bit 31 of the sum is the carry the `0x8000` bias arranged for, and it doubles as the signal that the byte was a dyad code: when it comes back set, the stream carries a second byte, whose entry's top half is the correction for the low half of the result. A second byte that still leaves bit 31 set is the retail codec's error code 2 — a malformed stream.

`Indeo3Codebooks.RowDelta` reproduces this. The decoder uses the equivalent plain form — two 16-bit dyad adds per line, as described under [Cell data](#cell-data) — which needs no bias.

### Evidence

Three independent checks, in `tests/HercWorks.Video.Tests/Indeo3CodebookTests.cs`:

- The 5,251 seed bytes compiled into the assembly match `IR32_32.DLL` byte for byte, checked against the retail file whenever it is present in the tree.
- The whole expanded image matches an FNV-1a digest of `60d8ce28421b3ef5`.
- Two entries reproduce values from a real decoded `IV32` luma plane: from the strip boundary predictor `40404040`, mode byte `6c` with its continuation byte gives four pixels of 10, and the byte after it gives four of 8.

The first two only say the derivation is self-consistent with its input. The third is the one that would catch a derivation that is wrong in the same way throughout. `Indeo3DecoderTests` repeats it through the whole decoder, on a hand-built frame.

The grammar and the three passes come from the OxideAV `oxideav-indeo` project (MIT), which recovered them from this same DLL, and were re-derived independently against our own copy before being ported. The 24 blocks' dyad counts and quad sides match the VQ table in the published format description, block for block.

## The requantisation table

`1003d088`, `[8][128]`, initialised in the image and readable straight out. Row *q* is a staircase of step *q*+2: row 0 runs 0, 2, 2, 4, 4, 6, 6, 8, row 1 steps by 3, row 2 by 4. It remaps a pixel before a delta is added so the sum cannot leave the 7-bit range, and the rows top out at 126 and 127 accordingly.

`Codecs/Indeo3/Indeo3Requant.cs` builds it from the staircase formula, `(p + offset) / step * step + bias`, with a handful of cells patched. The division truncates toward zero, which is why rows 3 and 4 start at 4. `Indeo3DecoderTests` checks the result against the DLL byte for byte.

## Provenance

The cell layer — the tree, the modes, the escapes and the two-buffer scheme — follows the published format description of Indeo 3: the MultimediaWiki page and the open-source decoders built from it. It was not read out of `IR32_32.DLL`. Decoding every frame of the corpus and looking at the frames is what confirms it. `INTR_PT1.AVI` stays clean from the first frame to the last, deep into runs of inter frames, which it could not do with a motion or buffer-selection error.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `IV32` frame data begins at the start of the `00dc` chunk. | It begins 16 bytes in, after the frame header. The plane offsets are relative to the start of the *bitstream* header, not to the chunk. |
| The plane offsets are listed in the order the planes appear. | The header lists Y, then V, then U, but the data is laid out U, then V, then Y. On the first frame of `INTR_PT1.AVI` the U offset is `0x30` — immediately after the header — and Y is last. |
| The frame-header checksum is the XOR of the other three fields. | It is that XOR, XORed again with the tag `FRMH`. Without the tag it matches none of the corpus's frames. |
| "Quarter-resolution chroma" means half each way, 144x90 for a 288x180 frame. | It is a quarter each way — 4:1:0, one sample per 4x4 luma block — and then rounded up to a multiple of 4: 72x48. |
| The codebook seed is the interleaved region at `1004eb56`. | The region is real and its head does read as small signed pairs, and it even walks cleanly to a terminator under the seed grammar above — but the blocks it yields have no regularity at all (counts from 1 to 211, expansion bytes including 0 and -103), where the true area's 24 blocks fall into three obvious groups. It is a coincidental parse. The seed area is at `1004d26a`. |
| The codebooks can be lifted from the DLL, given the right offset. | The eight objects at `10043e4c` shaped exactly like eight codebooks of 256 two-byte entries are zero on disk, as is the rest of the 16 KB around them, and so are the 1 KB objects near `1003f04c`. There is nothing to lift; the generator is what had to be recovered. |
| `1004f253` is codebook data. | It is large — 17,953 bytes — but triangular, and nothing in a codebook is. Each of its 16 identical-shaped tables is 1,122 bytes: a leading index *K* counting down from 32 to 0, each followed by 2(32−*K*)+1 entries, with one spare byte at the end of the region. The entries look like indices rather than deltas. `IR32_32.DLL` is an encoder as well as a decoder, and this has an encoder search table's shape, as does the 512-byte table at `1004e954` holding *n*² for *n* = 0…255. |

## Open

- **Open:** the matrix and range `IR32_32.DLL` converts YUV to RGB with.
## Not planned
- The null cell's second code. 0 and 1 are both decoded as a copy. What the retail codec does differently for 1 has not been read, and no frame in the corpus uses it: all 80,237 null cells across the 80 files carry 0.
- 8-bit pixels and half-pel motion vectors (frame flag bits 1, 4 and 5). No frame in the corpus sets them, and the decoder rejects a frame that does.
