# Runtime library — memory pool and streams

VSHELL and DBSIM link the same support library. Its asserts name the source files (`src\mymem.cpp`, `src\volrstrm.cpp`, `src\strings.cpp`, `src\fileutil.cpp`, `.\inc\sfx_fix.h`); DBSIM's copy has most of them compiled out. Addresses are VSHELL's, with DBSIM's copy in parentheses where it has been identified.

## The memory pool

The global `operator new`, `Mem_New` (`00408107`; DBSIM `00477390`), serves a request from one fixed pool, `g_ShellPool` (`0048239c`; DBSIM `g_SimPool`, `006bb37c`), when it is smaller than the pool's free byte count and no larger than its largest free block less `0x6c`; anything else is `calloc`'d. The array form, `Mem_NewArray` (`0040815a`; DBSIM `004773e4`), is the same with 8 bytes added to the size, and returns the block start, so the 8 bytes are slack rather than a header; the RTL's `_vector_new_` (`Rtl_VectorNew`) allocates through it. Both return the block zeroed. `Mem_Delete` (`004081b0`; DBSIM `0047743c`) and its array twin `Mem_DeleteArray` (`004081f1`; DBSIM `00477480`), which has the same body, send a pointer back to the pool when it is a tagged pool block and `free` it otherwise.

The pool object is `0x14` bytes: base, byte size, free-list head, free bytes, lowest free bytes seen. Every block has an 8-byte header. An allocated block holds `KLBA` (`0x41424c4b`) at `+0` and its size, header included, at `+4`, with the caller's data at `+8`. A free block holds the next free block at `+0`, its size at `+4` and `KLBF` (`0x46424c4b`) at `+8`. `Memory_Alloc` (`004078e7`) is first fit over the free list, rounding a request up to `(size + 15) & ~7` and splitting a block that would leave at least `0x10` bytes. `Memory_Free` (`00407a63`) inserts the block in address order and merges it with free neighbours.

VSHELL's arena is `Shell_PoolSize` (`0046c090`) bytes, 4,000,000 in the image, which [its startup](shell/startup.md#the-startup--shell_main-00401525) takes from `Mem_NewArray` before the pool exists — so from `calloc` — and hands to `Memory_Init` (`004077c5`), and frees on the way out.

### The overrun check checks nothing

`Memory_Free` and `Memory_Realloc` (`00407be9`) assert `Block overrun` when `Memory_CheckOverrun` (`00407870`) fails, and it cannot fail for a block they accept. It first requires the block to pass `Memory_IsAllocatedBlock` (`00407803`), the same test behind the preceding `Bad pointer` assert. Then it asks whether the block ends at the pool's base plus its size times 8, but the size is already in bytes, so no block inside the pool ends there. Otherwise it returns `Memory_IsBlockHeader` (`00407831`) for the block's **own** header rather than the header after it. That header is tagged `KLBA` and lies at least 16 bytes before the pool's end (the smallest block is 16 bytes), so the test passes. The bytes past a block are never inspected, and a write past the end of a pool block goes unreported until it corrupts something else.

## Streams

Most file access goes through a small stream hierarchy (the C runtime's `FILE` functions are imported too). The class names come from the Borland RTTI behind each vtable. `StreamIO` is the base and holds a status word at `+4`; the concrete classes are `FileRStream`, `FileWStream`, `FileRWStream`, `VolRStream` (a loose file or a `.VOL` entry, [vol-archive.md](formats/vol-archive.md)), `MemRWStream`, and the decompression filters `RLERStream` and `LZHRStream`. The status codes seen are 0 ok, 1 I/O error, 2 end of data, 3 close failed, 4 write to a read-only stream, 5 no file, and 6 filter not attached.

### The decompression filters

`RLERStream` and `LZHRStream` decode the stream they are attached to as it is read: `.VOL` compression types 7 and 9 ([vol-archive.md](formats/vol-archive.md#the-per-entry-prefix--fixed-9-bytes)) and bitmap packing types 1 and 3 ([`rendering/dts-billboards.md`](rendering/dts-billboards.md#tsbitmappart_render-004762e8)). Neither stream carries a length; the reader asks for as many bytes as it knows the data unpacks to.

**RLE.** `RLERStream_Read` (`0044f10c`; DBSIM `0047b5f8`) reads a control byte. With bit 7 set it is a run, `control & 0x7f` copies of the byte that follows; with bit 7 clear it is that many literal bytes, which follow it. A control byte of `0x00` or `0x80` produces nothing.

**LZH** is Okumura's LZHUF. `LZHRStream_Read` (`0044f4b8`; DBSIM `0047b9ac`) keeps a 4096-byte window whose first 4036 bytes `LZHRStream_Open` (DBSIM `0047b868`) fills with spaces, writing from position 4036. Each symbol comes from an adaptive Huffman code over 314 symbols (`LZHuffman_Start`, DBSIM `0047bbd0`; `LZHuffman_DecodeChar`, DBSIM `0047bd18`). A symbol below 256 is a literal byte; any other is a match of `symbol - 253` bytes, 3 to 60, copied from window position `write - distance - 1`. `LZHuffman_DecodePosition` (DBSIM `0047bd68`) reads the 12-bit distance: a byte indexes two tables for the upper six bits and a bit count, and that count less two further bits are shifted into the byte, whose low six bits are the rest. The tables are LZHUF's `d_code` and `d_len` byte for byte.

### The errno test reads an import stub

When a read, seek or handle check fails, the stream code tests `errno`: `VolRStream_SetIoError` (`0040339d`; DBSIM `00473474`) and `FileRStream_SetIoError` (`0044e914`; DBSIM `0047ae54`) store status 1 when it is nonzero and 3 otherwise, and `VolRStream`'s handle constructor, `setFile` and `read` assert `Bad file Handle.` when it is `EBADF` (6). None of these reads `errno`. They read the dword at `0046b6f6` (DBSIM `004966ea`), which is the jump stub the linker made for `cw3220.dll`'s `_errno` import. Its value is the stub's own instruction bytes, `FF 25 3C E3` (`0xE33C25FF`; DBSIM `FF 25 10 C3`), which are never 0 and never 6. So a failed stream operation always gets status 1, and the `Bad file Handle.` asserts never fire.

## Open

- **Deferred:** whether any caller treats status 1 differently from 3, which would make the `errno` stub read visible beyond the missing assert.
