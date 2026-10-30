# DBSIM.EXE, GO.EXE and ESCD.EXE — the X-32 executable

ES1's two game executables and its launcher, `ESCD.EXE`, are DOS `MZ` files carrying a 32-bit protected-mode program behind a DOS extender of the FlashTek X-32 family. Each file is three parts back to back: a real-mode stub, the extender's 16-bit kernel, and the program's flat 32-bit image. Nothing follows the image. `DBSIM.EXE` and `GO.EXE` carry one version of the stub and kernel (`DOS extender Copyright 1991-1994 by Doug Huffman`); `ESCD.EXE` carries another (`DOS extender Copyright 1991 1993 by Doug Huffman`), which loads the image the same way from variables at other offsets ([`ESCD.EXE`'s kernel](#escdexes-kernel)).

| Part | `DBSIM.EXE` | `GO.EXE` | `ESCD.EXE` |
|---|---|---|---|
| stub, with its own `MZ` header | `0x0000`, 1,341 bytes | `0x0000`, the same 1,341 bytes | `0x0000`, 1,329 bytes |
| kernel `MZ` header | `0x053d`, 160 bytes | `0x053d`, 160 bytes | `0x0531`, 160 bytes |
| kernel | `0x05dd`, 17,184 bytes (`0x432` paragraphs) | `0x05dd`, 17,184 bytes | `0x05d1`, 16,992 bytes (`0x426` paragraphs) |
| image | `0x48fd`, 711,896 bytes, to end of file | `0x48fd`, 631,472 bytes, to end of file | `0x4831`, 25,980 bytes, to end of file |

The image runs at address 0 of its own segment: its first file byte is address 0, and every address its code uses is an offset into it. Its memory runs on past the file bytes, to `0xbdd40` in `DBSIM.EXE`, `0xab7c0` in `GO.EXE` and `0x67c0` in `ESCD.EXE`. Its entry point is `0xd` in all three.

Image addresses are written `ES1DBSIM 000654bc`, `ES1GO 00051918` and `ES1ESCD 000005cc`. Kernel code is cited by its offset in the kernel's code segment (load segment + `0x74`, file offset `0x0d1d`) as `DBSIM.EXE` has it; `GO.EXE`'s kernel carries the same instructions `0x30` bytes lower at every location cited here. `ESCD.EXE`'s kernel code is cited as "`ESCD.EXE` kernel", by its offset in its own code segment (load segment + `0x6f`, file offset `0x0cc1`). Kernel data offsets (`+0x12c`) count from the kernel's first byte, which is also the start of its data segment.

## The stub

Its `MZ` header declares exactly its own 1,341 bytes (three pages, `0x13d` used in the last), two relocations and entry `0000:0154`. Its code, in order:

1. Grows its DOS memory block to the largest available and keeps the segment at its top.
2. Selects a host — DPMI (`INT 2Fh` `AX=1687h`, with 32-bit support), VCPI (`INT 67h` `AX=DE00h`), or neither, after an 80386 check — and passes the choice to the kernel. Its failure messages include `Fatal error, 80386 processor is required`, `Fatal error, DPMI host does not support 32 bit applications` and `Previously installed software is neither VCPI nor DPMI compatible`.
3. Opens its own file, by the path after the environment block's variables, reads the page fields of its own header and computes its size as `(pages − 1) × 512 + last-page bytes`: `0x53d`, the offset of the kernel header.
4. Reads the first `0x20` bytes of the kernel header over its own start, so that file offset `0x53d` is offset 0 of the stub's segment from here on, and adds the kernel's load segment to the header's initial `CS`.
5. Reads the kernel's first word, its length in paragraphs, and refuses to go on unless that length plus `0x440` paragraphs fits below the top of memory. It sets `SS` to the kernel's first paragraph and `SP` to `(length + 0x7e) × 16`.
6. Reads `(length − 2 + header paragraphs) × 16` bytes from file offset `0x55d` — the rest of the kernel header and exactly the kernel — over its own code. It issues that read as a far jump into the `INT 21h` handler, with an interrupt frame on the stack whose return address is the kernel's entry and whose flags are `0x3200`, so the read returns into the kernel.

The kernel starts with `BX` holding the file handle, positioned at the image; `ESI` holding the byte count it reads for the image; and five dwords on the stack describing the host, all zero under DPMI. The count is the kernel header's `pages × 512 + last-page bytes`, less what step 6 read, less `0x20`. It does not discount the partial last page, so it is `0x200` more than the bytes left in the file, and the image read ends at end of file.

`ESCD.EXE`'s stub is another build of the same code. Its header declares its 1,329 bytes (three pages, `0x131` used in the last), two relocations and entry `0000:0152`; it takes the same six steps, so the kernel header is at `0x531` and step 6 reads from `0x551`, and it computes `ESI` by the same formula (stub `0x485`–`0x4bb`): `0x677c`, `0x200` more than the image's bytes. Under DPMI it leaves four zero dwords for the host rather than five (stub `0x475`), and the kernel pops four (`ESCD.EXE` kernel `0x78`) where `DBSIM.EXE`'s pops five (kernel `0xb2`).

## The kernel header

A standard `MZ` header, `0xa` paragraphs long.

| Offset | `DBSIM.EXE` | `GO.EXE` | `ESCD.EXE` | Holds |
|---|---|---|---|---|
| `+0x02`, `+0x04` | `0x98`, `0x591` | `0x70`, `0x4f4` | `0x7c`, `0x55` | size: the rest of the file exactly |
| `+0x06` | 32 | 32 | 31 | relocation count |
| `+0x08` | `0xa` | `0xa` | `0xa` | header paragraphs |
| `+0x0a`, `+0x0c` | `0x1007`, `0xffff` | `0x1151`, `0xffff` | `0x25`, `0xffff` | minimum and maximum extra paragraphs ([Open](#open)) |
| `+0x14` | `0074:0000` | `0074:0000` | `006f:0000` | entry `CS:IP` |
| `+0x18` | `0x1c` | `0x1c` | `0x1c` | relocation table offset |

The stub reads `+0x02`, `+0x04`, `+0x08` and `+0x14`. The kernel reads `+0x06` and `+0x18` on entry (kernel `0x69`; `ESCD.EXE` kernel `0x36`) and applies the relocation table to itself: it adds its load segment at each `segment:offset`, skipping any entry whose segment is not below its length in paragraphs. It reads that length before the loop, so the `0000:0000` entry — the last of `DBSIM.EXE`'s and `GO.EXE`'s 32, the 22nd of `ESCD.EXE`'s 31 — adds the load segment to the kernel's first word without changing the loop's bound.

## The kernel

Its first word is its length in paragraphs, `0x432` in `DBSIM.EXE` and `GO.EXE` and `0x426` in `ESCD.EXE`, and the image starts right after it. The rest is the extender: host setup for DPMI, VCPI and raw XMS (`Cannot enable the A20 line, XMS memory manager required`, `Fatal error making VCPI call`), fault reporting (`A PAGE FAULT HAS OCCURRED WHILE __X386_VM_DISABLED WAS NON-ZERO`, `INTERRUPT 0DH, GENERAL PROTECTION FAULT`), and the loader below. `DBSIM.EXE`'s and `GO.EXE`'s kernels differ in their bytes: `GO.EXE`'s code sits `0x30` lower, and the program addresses below differ.

### The address table

Seven dwords at `+0x118`, one set per program:

| Offset | `DBSIM.EXE` | `GO.EXE` | Holds |
|---|---|---|---|
| `+0x118` | `000973e4` | `00083cd0` | image word the DPMI path sets to the kernel's data selector (kernel `0x3ae5`); the image ships it as `0x1b`, the kernel data selector of its own descriptor table |
| `+0x11c` | `0009003a` | `0007d566` | image word set to the flat data selector, base 0 and a 4 GB limit (kernel `0x35a2`) |
| `+0x120` | `000973e6` | `00083cd2` | [Open](#open) |
| `+0x124` | `0009003c` | `0007d568` | image dword the DPMI path sets to minus the image's linear base (kernel `0x3a76`), and rewrites when the base-setting routine at kernel `0x3200` runs again (it is also called from `0x19ce`); the image ships it as `0xf0000000`, minus the base the other hosts use |
| `+0x128` | `0009003c` | `0007d568` | the same address as `+0x124` |
| `+0x12c` | `000bdd40` | `000ab7c0` | the image's memory size |
| `+0x130` | `0000000d` | `0000000d` | the entry point |

The kernel reads `+0x12c` from the table (kernel `0x352e`, `0x396b`). The other values appear in its code as immediate operands: each address as the target of the store cited, and the entry point as the operand of the push before the jump to it.

### Loading the image

The image's code, data and stack selectors share one linear base. Under DPMI (kernel `0x37f7`) the kernel allocates four descriptors — flat data, then the image's stack, code and data — and a linear block of `(memory size + 0x4fff)` rounded down to a 4 KB multiple, and bases the three image selectors at the block's first page boundary. Under VCPI and raw XMS the kernel's own descriptor table (`+0x10`, loaded by `lgdt` from `+0x110`) holds the same four, the three image selectors based at the dword at `+0x51c`, which ships as `0x10000000`.

Under every host the image's code selector has a 4 GB limit, and its data selector is expand-down with limit `0x7f`, so a data access to offsets `0`–`0x7f` faults.

One routine (kernel `0x3514`) then reads the image for all hosts: the smaller of `ESI` and the memory size, from the still-open file to the image's linear base, through the flat data selector. It closes the file. The memory between the end of the file bytes and the memory size is not read, and the program's own startup zero-fills it: `ES1DBSIM 0000003c` loads `0xbdd40` and `0xadcd8` and clears the span between them with `rep stosd`, `ES1GO` does the same with `0xab7c0` and `0x9a2b0`, and `ES1ESCD 0000002f` with `0x67c0` and `0x657c`.

### Hand-off

Both paths end in a 32-bit far return to `0xd` in the image's code selector, after `push 0xd` (DPMI: kernel `0x3af1`; VCPI and raw XMS: kernel `0x3734`). At entry `DS` and `ES` are the image's data selector, `FS` the PSP's, `GS` the environment's, and `SS:ESP` the image's stack selector and the dword at `+0x198`. Under DPMI that dword is the length of the allocated block from its first page boundary (kernel `0x39a9`), less `0x126a` that the image-read routine takes off it (kernel `0x3519`), so the stack grows down from just below the block's end towards the memory size. The startup at `0xd` saves `FS` and reads the command tail at its offset `0x80`.

### `ESCD.EXE`'s kernel

`ESCD.EXE`'s kernel runs the same loader as `DBSIM.EXE`'s from code at other offsets, and keeps its variables `0x40`–`0x54` bytes lower in its data segment:

| `DBSIM.EXE` | `ESCD.EXE` | Holds |
|---|---|---|
| `+0x118` | `+0xd8` | the address table |
| `+0x1d2` | `+0x184` | `ESI`, the image byte count, stored on entry (kernel `0x73`; `ESCD.EXE` kernel `0x3c`) |
| `+0x198` | `+0x148` | the image's initial stack pointer |
| `+0x51c` | `+0x4c8` | the image's linear base under VCPI and raw XMS; ships as `0x10000000` |
| kernel `0x3514` | kernel `0x347e` | the image-read routine |

Its address table:

| Offset | `ESCD.EXE` | Holds |
|---|---|---|
| `+0xd8` | `00005e92` | image word the DPMI path sets to the kernel's data selector (`ESCD.EXE` kernel `0x3a63`), as `+0x118` in `DBSIM.EXE`; the image ships it as `0x1b` |
| `+0xdc` | `000057a6` | image word set to the flat data selector (`0x350c`), as `+0x11c` |
| `+0xe0` | `00005794` | image dword the kernel sets to the memory size, below |
| `+0xe4` | `00005798` | image dword the kernel sets to the memory size, below |
| `+0xe8` | `00005794` | the same address as `+0xe0` |
| `+0xec` | `000067c0` | the image's memory size |
| `+0xf0` | `0000000d` | the entry point |

The kernel reads `+0xec` from the table (`ESCD.EXE` kernel `0x3498` in the image-read routine, and `0x38a5`, where the DPMI path allocates its block by the same formula). The image-read routine caps the read at the smaller of `+0x184` and the memory size (`0x34bd`) and closes the file (`0x3505`). After each of the routine's three calls the kernel stores the memory size into image dwords `0x5794` and `0x5798`, as the immediate `0x67c0` (`0x0190`, `0x36dc`, `0x39c9`), where `DBSIM.EXE`'s returns straight after the first two (kernel `0x1b1`, `0x37ca`). The dword that holds minus the image's linear base, `+0x124`'s in `DBSIM.EXE`, is image `0x57a8` here, outside the table: the DPMI path stores it at `0x3996` and the base-setting routine at `0x31c7`, and the image ships it as `0xf0000000`.

`X32_LargestFreeBlock` (`ES1ESCD 00002e63`), which the launcher's memory check calls ([starting](../shell/launcher.md#starting--launcher_main-000005cc)), reads `0x5794` at `00002e69` as its high-water mark: it takes the extender's figure (`INT 21h` `AX=350Bh`) plus the rest of the 4 KB page above the mark, `0x840` bytes above `0x67c0`, and answers the larger of that and the largest size on the list headed at `0x6696`, less 4, or 0.

Under DPMI the stack pointer is the block's length from its first page boundary (`0x38e3`), less `0x1200` that the image-read routine takes off it (`0x3483`). The two hand-offs push `0xd` at `0x3a7f` (DPMI, followed by the far return at `0x3a85`) and `0x365e` (VCPI and raw XMS), in the same sequences as `DBSIM.EXE`'s.

## The image

Address 0 is the image's first file byte. Each of the 41 source-path strings in `ES1DBSIM`, 42 in `ES1GO` and 13 in `ES1ESCD` occurs exactly once in its image as a 4-byte little-endian value equal to its own offset, and every one of those is the operand of a `push` followed by a push of a line number (`ES1DBSIM 000654e2` pushes `0x98a48`, `C:\P\M\s\util\ovl.cpp`, then line `0x15`).

The kernel's read runs to end of file, and the three parts cover every byte of each file, so the program has no code or data in its executable outside the image.

## Open

- **Open:** which FlashTek product and version the extender is. The stub and the kernel name none.
- **Open:** what the image word at the address table's `+0x120` receives.
- **Open:** what reads the kernel header's minimum and maximum extra paragraphs.
- **Open:** where the image's stack sits under VCPI and raw XMS hosts; the kernel computes the dword at `+0x198` for them in the image-read routine.
- **Open:** what the kernel keeps in the `0x126a` bytes (`0x1200` in `ESCD.EXE`) between the image's initial stack pointer and the end of its DPMI block.
- **Open:** what `ES1ESCD` does with the memory size the kernel stores at `0x5798`, and with `0x5794` at `000001eb`, in the startup.
