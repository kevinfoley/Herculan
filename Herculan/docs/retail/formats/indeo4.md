# Indeo Video 4.1

Intel's `IV41`, used by one file in `ES2/AVI/`: `ES2DROP3.AVI`, 288x180 at 15 fps, 391 packets, of which 9 are empty. No shell movie id names it. See `docs/retail/formats/avi-video.md` for the container.

Addresses are virtual addresses in `IR41_32.DLL`, the Intel codec the game's installer drops in `ES2/INDEO/`. Its image base is `10000000`. The DLL is a whole codec, encoder included; everything below is the decoder.

Decoding as this document describes reproduces the DLL's YUV output for all 391 packets of `ES2DROP3.AVI`, every sample of all three planes. See [Evidence](#evidence).

## Frame layout

Each `00dc` chunk holds one frame: a picture header, then one band per plane in the order luma, then the two chroma planes. Every header and every block of data starts on a byte boundary. Bits are read least significant first: a field's first bit is bit 0 of the current byte, and a multi-bit field is assembled with its first bit as the low bit.

An intra frame ends with the 19-byte string `\r\nVer 4.11.15.60\r\n`, the encoder's version, after its last band. The codec checks the frame's length: where the last band ends, plus 19 for a type-0 frame, plus 8, rounded down to a multiple of 4, must equal the header's data size. That holds on every frame of the corpus, and each chunk is two bytes longer than the data size.

An empty chunk leaves the picture as it was.

## Picture header

| Bits | Field |
|---|---|
| 18 | sync, `0x3FFF8` |
| 3 | frame type; 7 is malformed |
| 1 | transparency present |
| 1 | must be 0 |
| 1 | data size present; if set, 24 bits of data size follow |
| | *frame types 5 and 6 end here* |
| 1 | access key present; if set, 32 bits follow and the frame decodes only with a matching key |
| 3 | picture size code; 7 is followed by 16 bits of height then 16 of width, 0 to 6 index a table of 640x480, 320x240, 160x120, 704x480, 352x240, 352x288, 176x144 (`100600b8`, `100600e0`) |
| 1 | tile size present; if set, two 4-bit codes give tile height then width from a table of multiples of 32 (`100611c8`), 15 meaning the whole picture |
| 2 | chroma subsampling; 0 is 4:1:0, one chroma sample per 4x4 luma block |
| 2+ | luma band decomposition, see [Bands](#bands) |
| 2+ | chroma band decomposition |
| 1 | frame id present; if set, 20 bits of frame id follow |
| 1 | if set, 8 bits follow, which the codec skips |
| 1 | macroblock codebook present; if set, 3 bits choose one of the predefined codebooks, 7 meaning a custom descriptor follows. Absent means predefined codebook 7 |
| 1 | block codebook present, coded the same way; this is the default for any band that does not name its own |
| 1 | run/value map present; if set, 3 bits choose it. Absent means map 8 |
| 1 | see [Open](#open) |
| 1 | quantiser every macroblock: the luma band reads a quantiser change for every macroblock that is not skipped, coded or not |
| 5 | a picture quantiser |
| 1 | if set, 3 bits follow; non-zero makes the packet carry further frames |
| 1 | if set, 16 bits follow, read only by the access-key check |
| 1 | extension: while set, 8 bits follow and then another flag. The last byte read is the [clamp mask](#output) |
| 1 | if set, one bit per 64x64 area follows, which the codec skips |

A frame whose id equals the previous frame's, and which is not type 0, is treated as type 5.

### Frame types

| Type | Coding | After decoding |
|---|---|---|
| 0 | intra; may change the picture size | becomes the reference |
| 1 | intra | becomes the reference |
| 2 | inter, from the reference | becomes the reference |
| 3 | bidirectional | |
| 4 | inter, from the reference | is shown, and the next frame decodes over it |
| 5 | none: the picture is unchanged | |
| 6 | ends a bidirectional group | |

`ES2DROP3.AVI` uses types 0, 1, 2 and 4: 27, 76, 101 and 178 frames. A type-0 frame comes every 15 packets, plus one at packet 270, and the frames between run 4 2 4 1 4 2 4 1 4 2 4 1 4 2.

## Bands

A band decomposition is a tree read two bits at a time. 3 is a leaf, one band; 0, 1 and 2 split the region in two vertically, in two horizontally, or in four. Every frame of the corpus codes each plane as a single band, `3` at the root.

### Band header

| Bits | Field |
|---|---|
| 2 | plane |
| 4 | band |
| 1 | empty |
| 1 | if set, 16 bits follow: the band header's length in bytes |
| 2 | motion resolution; 0 is whole samples, 1 half samples |
| 1 | if set, 16 bits follow; see [Open](#open) |
| 2 | macroblock size: 16, 8, 4; 3 is malformed. Blocks are 8x8 in a 16x16 macroblock, otherwise the macroblock's size |
| 1 | inherit the macroblock type and motion vector from the luma band |
| 1 | inherit the quantiser change from the luma band |
| 5 | band quantiser, 0 to 31 |
| 1 | keep the previous frame's transform, scan and matrix for this band; if clear, they follow: |
| 5 | transform, see [Transforms](#transforms) |
| 4 | scan order; 15 is a custom one |
| 5 | quantisation matrix; 31 is a custom one |
| 1 | block codebook present, coded as in the picture header; absent means the picture's |
| 1 | run/value map present, 3 bits; absent means the picture's |
| 1 | swaps present: 8 bits of count, at most 61, then that many pairs of 8-bit symbols, see [Run/value maps](#runvalue-maps) |

In the corpus the luma band has 16x16 macroblocks, transform 0, scan 1 and matrix 14, and inherits nothing; both chroma bands have 4x4 macroblocks, transform 11, scan 5 and matrix 15, and inherit both type and motion and the quantiser. Motion is in whole samples throughout.

### Tiles

A band is coded tile by tile; the corpus has one tile per band. A tile opens with an empty flag. An empty tile is one byte; in an inter frame it copies the tile from the reference, and in an intra frame it leaves the buffer as it was. Otherwise a size flag follows and then the tile's size in bytes, counting from the tile's first byte: 8 bits, or `0xFF` followed by 24 bits. The tile's macroblock headers start at the next byte, its blocks at the byte after the macroblock headers end, and the codec checks that the blocks end exactly where the size says.

## Buffers

The codec keeps three buffers, each holding every plane's band one after another. A frame decodes into the first and predicts from the second; the third serves bidirectional frames. After a frame of type 0, 1 or 2 the first two swap. On allocation every sample is set to the value that means 128.

### Samples

A sample is 16 bits: the 8-bit value times 4, plus `0x4000`, so 128 is `0x4000` and there are two fraction bits.

Samples are not stored in raster order. Two sit in each 32-bit word, a low lane and a high lane, a block width apart:

- In a band of 16x16 macroblocks, each group of 16 samples in a row is 8 words, and word *j* holds sample *j* in its low lane and sample *j* + 8 in its high lane. The left 8x8 block of a macroblock is the low lanes and the right one the high lanes of the same eight words.
- In a band of 4x4 macroblocks, each group of 8 samples is 4 words, and word *j* holds samples *j* and *j* + 4. A macroblock at an even column index is the low lanes, the one to its right the high lanes.

The layout is what lets the transforms and stores work on two blocks at once, and it has side effects on the picture: see [Motion compensation](#motion-compensation) and [Stores](#stores).

A row is padded to a multiple of 32 bytes and a band to a multiple of 16 rows, and the codec allocates 1,024 bytes after each band and 1,568 after each buffer. Addresses are linear, so a motion vector reaching past a row's end reads the next row.

## Macroblocks

The macroblock headers of a tile are read in raster order. Values coded with the macroblock codebook are signed, symbol 0, 1, 2, 3, 4, … meaning 0, 1, −1, 2, −2, … (`1005b000`).

| Field | Present when |
|---|---|
| skip, 1 bit | always |
| type: 0 intra, 1 inter | an inter frame, in a band that does not inherit the type; 2 bits in a bidirectional frame |
| coded block pattern, 4 bits for a 16x16 macroblock (bit *n* is block *n*: top-left, top-right, bottom-left, bottom-right), 1 bit otherwise | not skipped |
| quantiser change | not skipped and the band does not inherit it, when the pattern is non-zero or the picture sets quantiser every macroblock; a skipped luma macroblock reads it too when the picture sets that flag |
| vertical then horizontal motion vector change | an inter macroblock in a band that does not inherit motion |

An intra frame's macroblocks are all intra, and its chroma bands inherit no type or motion even when their headers ask to.

The macroblock's quantiser is the band quantiser plus the change. The codec does not clamp it: it indexes 32 step tables for inter macroblocks followed directly by 32 for intra ones, so an inter quantiser above 31 uses intra steps.

### Motion vectors

Each vector component is a byte biased by `0x80`. The tile keeps a running vector, starting at zero; each inter macroblock that reads a change adds it to the running vector as a 16-bit sum with the vertical byte high, and the result is the macroblock's vector.

### Inherited type, motion and quantiser

A chroma macroblock inherits from the luma macroblock with the same raster index. It takes the luma macroblock's type and its vector rescaled to the smaller macroblock: halved for one size step, quartered for two, each component rounded half away from zero, as `((v + 1 + (v ≥ 0)) >> 2)` for a quarter. The arithmetic is on biased bytes and wraps at the extremes.

The inherited quantiser is the chroma band quantiser plus the luma macroblock's change, clamped to 0–31. A skipped luma macroblock that read no change hands on a change of −32.

### Motion compensation

An inter macroblock is filled from the reference at its own position displaced by its vector, as the macroblock header is read and before any block is decoded. What gets written depends on the lane layout:

- A 16x16 macroblock writes its 16x16 samples.
- A 4x4 macroblock in the low lanes writes eight samples across, its own four and the next macroblock's four, both displaced by its own vector. A 4x4 macroblock in the high lanes writes only its own.

So a high-lane macroblock that is not itself inter keeps the prediction its left neighbour wrote into it.

### Skipped macroblocks

A skipped macroblock has no blocks. In an intra frame it is intra; in an inter frame it is inter with no motion and is filled from the reference at the same place, with the same widths as above. A skipped high-lane 4x4 macroblock copies nothing when its left neighbour was inter with a zero vector, since that neighbour's copy already covered it.

## Blocks

Blocks are decoded in the order the macroblocks produced them, two at a time: a 16x16 macroblock's left and right blocks share a pair, and so do a 4x4 macroblock and the one to its right. Blocks whose pattern bit is clear are not coded.

A coded block is a run of symbols in the band's block codebook, each standing for a run and a level. The run advances a position through the scan order, starting before the first position; the level, at that position's quantiser step, adds to the coefficient there. The end-of-block symbol stops the block. The escape symbol is followed by three more symbols from the same codebook: the run less one, then the low and high parts of a level code, *high* × 64 + *low*. A position past 63 is malformed.

Each 4x4 scan order lists 16 positions; a position past the 16th lands on the coefficient at position 0.

### Codebooks

A codebook is a row descriptor: a row count and each row's suffix width. Row *r*'s codes are *r* one bits, then a zero bit unless it is the last row, then the suffix, most significant bit first. A row's codes stand for consecutive symbols following the previous row's. A one-row descriptor with an 8-bit suffix is a plain byte instead, read least significant bit first.

A descriptor may describe more than 256 codes. The codec builds only symbols 0 to 255, and gives symbol 255 the length of the last row's codes. A row wider than 8 bits, or a code longer than 13 bits, makes the descriptor invalid.

The eight predefined macroblock codebooks are at `10060ee0` and the eight block codebooks at `10060f80`, 17 bytes each: a row count, then the widths.

### Run/value maps

Nine maps at `100ae754`, 332 bytes each. Byte 1 onward lists, for runs of 1, 2, 3, …, how many levels that run carries, ending at a zero byte; the counts always total 127. The 256 bytes from offset `0x48` give each symbol a slot. Slot 0 is end-of-block and slot 1 the escape; then each run's slots in turn, first its negative levels from the largest magnitude down, then its positive levels from +1 up.

A level code is odd for a positive level and even for a negative one, magnitude (*code* + 1) / 2.

A band's swaps exchange the run and level of two symbols. End-of-block and escape stay attached to their symbols.

The codec finds end-of-block only through a 10-bit lookup: a codebook whose end-of-block code is longer than 10 bits never ends a block.

### Dequantisation

The step for a coefficient comes from the band's matrix, whether the block is intra or inter, the macroblock's quantiser and the coefficient's position: a table of 16-bit steps at `1006d9f8`, indexed [matrix 0–30][inter, intra][quantiser 0–31][position]. Matrices 0 to 14 are for 8x8 blocks; 15 to 21 are 4x4, stored with four positions to a row; 22 to 30 are a constant 8.

A level of magnitude *n* at step *s* is worth *n* × *s* + ⌊*s* / 2⌋ − (*s* mod 2), or *n* when *s* is 0 or 1. The codec precomputes these values per step, up to magnitude ⌊8192 / *s*⌋; a code beyond that reads past the table.

The Haar 8x8 transform doubles the value at the 16 positions with row and column both 4 or more (`100607e0`).

### Load-time table conversions

The codec rewrites two of its tables when it loads, so neither can be used as stored in the file:

- Each scan order whose last two entries are equal is a 4x4 order stored four positions to a row; its first 16 entries are respaced to eight to a row, *e* becoming (*e* & 12) + *e* (`1002a560`).
- Each row of the step table whose last entry is zero belongs to a 4x4 matrix; its entries 4 to 15 are respaced the same way, last first, leaving the vacated entries as they were (`1002a5a0`).

## DC prediction

Within a tile, the DC of each intra block is coded as a change from a running value, which starts at zero. For each pair, the left block's DC is the running value plus its coded DC, and the running value moves by the coded DC; then the same for the right block. Inter blocks neither use nor move it.

An intra block that is not coded is filled flat at the running value: for 8x8 blocks each sample is (running value with its low three bits cleared) ÷ 2, and for 4x4 blocks (2 × running value + 2) with its low two bits cleared, both plus `0x4000`.

## Transforms

The band header names a transform by number, and the codec carries its name (`100afeb0`, 60 bytes each):

| Number | Name |
|---|---|
| 0 | Inv Haar 8x8 |
| 1 | Inv Haar 1x8 |
| 2 | Inv Haar 8x1 |
| 3 | No Xfrm 8x8 |
| 4 | Inv Slant 8x8 |
| 5 | Inv Slant 1x8 |
| 6 | Inv Slant 8x1 |
| 7 | Inv DCT 8x8 |
| 8 | Inv DCT 1x8 |
| 9 | Inv DCT 8x1 |
| 10 | Inv Haar 4x4 |
| 11 | Inv Slant 4x4 |
| 12 | No Xfrm 4x4 |

The corpus uses 0 for luma and 11 for chroma.

Both run on a pair of blocks at once, the left block's coefficients in each word's low 16 bits and the right block's in the high 16, every coefficient biased: by `0x4000` for Haar 8x8 and `0x800` for slant 4x4. A coefficient is added to its word as a 32-bit sum, the right block's shifted up 16, so a negative left coefficient borrows one from the right one. The transforms are written as 32-bit additions, subtractions, shifts and masks on whole words, with constants chosen to keep the lanes apart; a lane's result depends on the other lane only through carries that the constants make predictable for values in range.

The Haar 8x8 at `10021010` takes columns 4 to 7, then columns 0 to 3, then rows. Each 8-point pass turns inputs *r0*–*r7* into *r0*+*r1*+*r2*+*r4*, *r0*+*r1*+*r2*−*r4*, *r0*+*r1*−*r2*+*r5*, *r0*+*r1*−*r2*−*r5*, *r0*−*r1*+*r3*+*r6*, *r0*−*r1*+*r3*−*r6*, *r0*−*r1*−*r3*+*r7* and *r0*−*r1*−*r3*−*r7*. The column passes mask their results to multiples of 4 and 2 respectively; the row pass halves its inputs first and masks to multiples of 4. The slant 4x4 at `1001e1c0` takes columns then rows.

## Stores

A transformed pair is written into the band by the routine at `100212a0`. Intra blocks replace the samples, *copy*; inter blocks add to the prediction already there, *add*, as a 32-bit sum less `0x40004000` across both lanes, or for the high lane alone as `(sum + 0xC0000000) >> 16`. Which lanes are written:

1. Both blocks intra: copy both if the left is coded, otherwise copy the right if it is coded.
2. Both coded: add both; then copy the right if it is intra, or else the left if it is intra.
3. Only the left coded: if the right is intra, add both; otherwise copy the left if it is intra, or add it.
4. Only the right coded: copy it if it is intra, otherwise add it.

Case 3 adds to an intra right block rather than replacing it, giving whatever the buffer held there plus a flat block at the running DC. Whenever only the left block of a pair is coded, the right block is then also treated as the start of a run of uncoded blocks, and filled flat if it is intra. Both follow from the routines as written.

## Output

A sample becomes a byte by (*sample* >> 2) + `0x80`, wrapping at 10 bits, then a clamp table at `100af450`: 0–255 unchanged, 256–511 to 255, 512–1023, the negative values, to 0. A plane whose bit is set in the clamp mask — 4 for luma, 2 and 1 for the chroma planes — skips the table and keeps the low byte. Every frame of the corpus sets 7 or 3.

The planes are 4:1:0, like Indeo 3's: 288x180 luma and 72x45 chroma for this file. The first chroma plane is V and the second U.

Asked for 24-bit RGB, the DLL dithers: the same YUV gives different RGB at different positions in a 4x4 pattern. See [Open](#open).

## Evidence

The DLL was loaded in a 32-bit process and driven through its `DriverProc` (`10029560`) with `ICM_DECOMPRESS` for each packet of `ES2DROP3.AVI` in turn. It refuses YVU9 as an output format but accepts RGB24; after each decompress, the three planes it had converted its buffers to were read from its decoder state, and those are what the decode was compared against. All 391 packets match in every sample of all three planes. `tools/scripts/indeo4_reference` repeats the comparison for any IV41 movie, given the DLL.

The 382 non-empty frames exercise intra and inter macroblocks, skipped macroblocks, custom macroblock codebooks in picture headers and custom block codebooks in band headers, eight of the nine run/value maps (all but map 1), band swaps on 979 bands, 88,103 escapes, and two empty chroma tiles.

## Provenance

Everything here was read from `IR41_32.DLL`'s disassembly and decompilation, and checked against the DLL's output. No third-party decoder source was consulted. The general shape of the format — wavelet bands, tiles, macroblocks, transform-coded blocks with run/value coding — matches what is publicly documented about Indeo 4 and 5; the specifics are the DLL's.

The macroblock-header pass, the coefficient decoder, the transforms and the stores are hand-written assembly in the DLL, partly reached only through jump tables, and Ghidra's auto-analysis leaves much of them undisassembled.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The scan orders and step tables can be read out of the DLL file as they are. | The codec respaces its 4x4 scans and 4x4 matrix rows when it loads; see [Load-time table conversions](#load-time-table-conversions). Read straight from the file, every chroma coefficient past the first row lands in the wrong place, and only frames with nothing but DC in chroma come out right. |
| A codebook descriptor describing more than 256 codes is malformed. | The codec builds the first 256 and ignores the rest. Custom block codebooks in the corpus describe up to 316. |
| The DLL's RGB24 output is a direct conversion of its YUV planes. | It is dithered; the same Y, U and V give different RGB in a 4x4 pattern of positions. Fitting a conversion matrix to it gives a different answer for each position. |
| Each 32-bit word of a band holds two horizontally adjacent samples. | It holds two samples a block width apart. Read as adjacent pairs, a decoded frame matches the DLL's planes only where the picture is flat. |

## Open

- **Deferred:** more than one tile per band.
- **Deferred:** band decomposition into more than one band per plane.
- **Deferred:** chroma subsampling other than 4:1:0.
- **Deferred:** half-sample motion.
- **Deferred:** bidirectional frames, types 3 and 6.
- **Deferred:** transparency, access keys, and packets carrying more than one frame.
- **Deferred:** custom scan orders and quantisation matrices.
- **Deferred:** transforms other than Haar 8x8 for 16x16 macroblocks and slant 4x4 for 4x4 ones, and 8x8 macroblocks.
- **Deferred:** empty bands, and a chroma band inheriting from an empty luma tile, which reads the luma macroblocks of an earlier frame of the same kind.
- **Deferred:** a luma band that inherits, and a chroma band that does not inherit both its type-and-motion and its quantiser.
- **Deferred:** a band of 4x4 macroblocks with an odd number of them across, for which the codec pads each row.
- **Deferred:** the picture header bit after the run/value map. The codec stores it and the macroblock pass loads it, but no use of it has been traced; every frame of the corpus sets it.
- **Deferred:** the 16-bit band header field after the motion resolution. The codec stores it in the band; no reader has been traced.
- **Deferred:** the matrix and dither pattern the DLL converts YUV to RGB with.
