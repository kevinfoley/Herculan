# DBSIM.EXE and GO.EXE — memory.cpp, the heap

Both ES1 programs replace the runtime's allocator with their own heap, in `memory.cpp`. It is the module ES2 links as `src\mymem.cpp`, with the same pool object, block headers, first-fit allocation and functions, described in [the memory pool](../../retail/runtime-library.md#the-memory-pool); this page holds ES1's addresses and where ES1 differs. Addresses are in `ES1DBSIM` unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)). [The two programs](#the-two-programs) lists where `ES1GO`'s copy differs from `ES1DBSIM`'s.

## The heap

**While the heap is on, every new block the allocators hand out comes zero-filled from one block taken from the extender at startup; once it is off, allocations come from the extender directly and frees do nothing.** `Mem_InitHeap` (`00001524`) takes one block from the extender and makes it the heap `g_SimPool` (`000ae4ac`) with `Memory_Init` (`00000d8c`). The block is the size asked for, capped at the largest free block the extender reports (`X32_LargestFreeBlock`, `00060cac`) less 25,000 bytes; if the extender cannot supply it, `Assert_Report` stops the program with `Memory alloc failed` ([asserts](asserts.md)). It then sets `Mem_UseHeap` (`000901d4`), and while that is set `Mem_New`, `malloc` and `calloc` allocate from the heap and `Mem_Delete` and `free` return blocks to it. `Mem_HeapOff` (`00001598`) clears it: the allocators then take memory straight from the extender (`X32_AllocMemory`, `000001d7`, in 4 KB multiples) and the frees do nothing. ES2 has no such switch.

Where `ES1DBSIM`'s heap differs from ES2's pool:

- The tags are the same four letters in the other byte order: an allocated block holds `ABLK` (`0x4b4c4241`) at `+0` and a free block `FBLK` (`0x4b4c4246`) at `+8`, where ES2's hold `KLBA` and `KLBF`.
- `Mem_New` (`000015a4`) returns `Memory_Alloc`'s block, which `Memory_Alloc` zero-fills, and clears nothing itself, so a block it takes from the extender while the heap is off is returned as the extender gives it. ES2's `Mem_New` clears every block it returns, from the pool or from `calloc`.
- `Mem_Delete` (`000015f8`) passes any pointer to `Memory_Free` while the heap is on, where ES2's first tests it with `Memory_IsAllocatedBlock` and sends the rest to `free`.
- `Memory_LargestFreeBlock` (`00000e88`) answers the largest free block's size less its 8-byte header, where ES2's subtracts `0x6c` and clamps at 0. `Arena_Push` sizes the loaders' arenas with it ([`../formats/dts-shape.md`](../formats/dts-shape.md#differences-from-es2)).
- `Memory_Ctor` (`00000d4c`) allocates its `0x14`-byte object with `Mem_New` when `this` is null and returns `this`. It stores `X32_FreeListHead`'s (`000bc166`) own address into it, and zeroes the base, size, free-list head and free bytes only of a heap other than `g_SimPool`. `X32_FreeListHead` is the node the pointer `0009002c` holds, the head of the circular list `X32_LargestFreeBlock` walks until it comes back to the head, so the store leaves that list with no other node ([Open](#open)).
- `Memory_BlockIterBegin` (`000014b0`) also allocates its 8-byte walker when `this` is null, and returns it.
- `Memory_Realloc` (`000010ec`) moves a block with `memmove` where ES2's uses `memcpy`.
- The functions other than the C entry points remove their own arguments, so in stack order the arguments after the heap are ES2's reversed: `Memory_Init` (heap, block, size), `Memory_Alloc` (heap, trackLow, size), `Memory_Realloc` (heap, size, payload), `Memory_DrawUsageMap` (heap, rows, columns, top, left), `maybe_Memory_OperatorNew` (heap, size).
- `Memory_CheckOverrun` (`00000e3c`) and `ES1GO`'s copy have ES2's fault ([the overrun check checks nothing](../../retail/runtime-library.md#the-overrun-check-checks-nothing)).

| ES2's name | `ES1DBSIM` | `ES1GO` | ES2 (DBSIM) |
|---|---|---|---|
| `Memory_Ctor` | `00000d4c` | `00000ef4` | `00476ccc` |
| `Memory_Init` | `00000d8c` | `00000f45` | `00476ce8` |
| `Memory_IsAllocatedBlock` | `00000dd0` | `00000f97` | `00476d20` |
| `Memory_IsBlockHeader` | `00000e00` | `00000fd9` | `00476d50` |
| `Memory_CheckOverrun` | `00000e3c` | `00001028` | `00476d8c` |
| `Memory_LargestFreeBlock` | `00000e88` | `00001091` | `00476dd0` |
| `Memory_Alloc` | `00000ec0` | `000010d9` | `00476e00` |
| `Memory_Free` | `0000100c` | `00001253` | `00476f00` |
| `Memory_Realloc` | `000010ec` | `000013a0` | `00476f9c` |
| `Memory_DrawUsageMap` | `00001304` | `000016b9` | `004771b4` |
| `Memory_PrevLinkedHeader` | `00001474` | `0000188b` | `004772e8` |
| `Memory_BlockIterBegin` | `000014b0` | `000018d1` | `00477310` |
| `Memory_BlockIterNext` | `000014e0` | `0000190b` | `00477324` |
| `maybe_Memory_OperatorNew` | `000015e4` | `00001a2c` | `00477358` |
| `Mem_New` | `000015a4` | `000019f0` | `00477390` |
| `Mem_Delete` | `000015f8` | `00001a44` | `0047743c` |
| `Mem_StaticInit` | `000016dc` | `00001b5c` | `004774c4` |

`Memory_DrawUsageMap` prints through `Mono_Printf` (`00060e78`, `ES1GO 0004f904`). `es2_xref` finds no caller of it, of `Memory_CheckOverrun` or of `maybe_Memory_OperatorNew` in either program ([Open](#open)); `Memory_IsAllocatedBlock`'s one caller it finds is `Memory_CheckOverrun`.

## The entry points

| | C++ convention (callee cleans) | C convention |
|---|---|---|
| allocate | `Mem_New` (`000015a4`) | `malloc` (`00001618`) |
| release | `Mem_Delete` (`000015f8`) | `free` (`00001654`) |
| resize | — | `realloc` (`00001670`) |
| allocate zeroed | — | `calloc` (`00001688`) |

Each pair shares one body; the C entry points are the game's own, where ES2 imports the runtime's. `maybe_Memory_OperatorNew` (`000015e4`) allocates from a heap it is passed rather than from `g_SimPool`, as ES2's does. `realloc` goes to the heap whether or not `Mem_UseHeap` is set. `calloc` clears its block over the size in the header before it; a block taken from the extender while the heap is off has no header, and the 4 bytes before it are read as one.

## Who makes the heap

Before `main`, `Mem_StaticInit` (`000016dc`) runs `Memory_Ctor` on `g_SimPool`, as ES2's does: `push 000ae4ac; call 00000d4c; ret`, from the static constructor table's entry `000adbb8`, the second from the bottom, which `Runtime_RunStaticCtors` runs next to last ([static construction](../formats/compiler.md#static-construction-and-exit)). `ES1GO`'s, `00001b5c`, is the table's bottom entry, `0009a218`, run last.

Each program's `main` makes the heap first and turns it off last:

| | `Sim_Main` (`00059350`) | `Shell_Main` (`ES1GO 00012ec7`) |
|---|---|---|
| asks for | 6,000,000 | the dword `0007e738`, 3,700,000 |
| fatal assert when it gets 100,000 or fewer | `main.cpp` line `0xe3` | `src\main.cpp` line `0x1f8` |
| after mounting the volumes | an assert when under `0x26e8f0` (2,550,000); `g_LowMemory` set when under `0x3c45b0` (3,950,000) ([low memory](../formats/chassis-dat.md#low-memory)) | an assert when under `BND\MAIN.BND`'s +12, 2,400,000; the byte `Shell_LowMemory` (`0007fd09`) set when under its +8, 3,700,000 ([`MAIN.BND`](../formats/bnd-notes.md#bndmainbnd)) |
| `Mem_HeapOff` | after the frame loop, and before the early exit when `000b5bc4` is clear | after the shell's loop, and before the early exit when `Shell_ParseCommandLine` (`0001272d`) returns non-zero |

## The two programs

`ES1GO`'s heap is `g_ShellPool` (`0009a308`), switched by `Mem_UseHeap` (`0007d7b4`), with the same functions, block format, entry points and allocation policy. Where it differs from `ES1DBSIM`'s:

- The heap object has no low mark: it is the four dwords `+0` to `+0xc`. `Memory_Ctor` allocates `0x10` bytes for a null `this`, `Memory_Init` sets no `+0x10`, `Memory_Alloc` (`ES1GO 000010d9`) takes no `trackLow`, and `Memory_Realloc` (`ES1GO 000013a0`) lowers no mark.
- `Mem_InitHeap` (`ES1GO 0000195f`) leaves 15,000 bytes of the extender's largest free block unused, where `ES1DBSIM`'s leaves 25,000.
- `Memory_Ctor`'s self-pointing store is to `X32_FreeListHead` (`ES1GO 000aa24a`), the node `0007d558` holds, which `ES1GO`'s `X32_LargestFreeBlock` (`0004f738`) walks from.
- `es2_xref` finds no reference to `Memory_LargestFreeBlock` (`ES1GO 00001091`), which `ES1DBSIM`'s `Arena_Push` calls.

## Where the object file starts and ends

`memory.cpp`, named by the assert source paths at `00001560` and `ES1GO 000019a4`, is `00000d4c`–`000016e7` in `ES1DBSIM` and `00000ef4`–`00001b6b` in `ES1GO`. In `ES1DBSIM` it follows `vcr.cpp`, whose static initializer `Vcr_StaticInit` (`00000d3c`) ends with a `RET` at `00000d48` and three zero bytes ([where the tape code sits](../formats/tap-input-tape.md#where-the-code-sits)), and opens with `Memory_Ctor`, no copy of `ConfigPart`'s destructors before it; in `ES1GO` it follows `ClassItem_SkipToEnd`, whose `RET 4` ends at `00000ef3`, on a 4-byte boundary. Its static initializer `Mem_StaticInit` is its last code in both: the `RET` at `000016e6` is followed by one zero byte and the classes' file at `000016e8` ([the object classes](object-classes.md#where-the-object-files-start-and-end)), and `ES1GO`'s `RET` at `00001b6a` by one zero byte and `Raster_DrawLine` (`00001b6c`).

## Object pools

**The simulator's short-lived objects, its machines and its structures come from fixed pools, walked oldest first, as ES2's are ([walking a pool](../../retail/simulation/sim-object-layout.md#walking-a-pool--oldest-first)).** The pool functions sit in the library region, `00063784`–`00063aa3`, each removing its own arguments:

| ES1 | Address | ES2 (DBSIM) | Difference |
|---|---|---|---|
| `ObjectPool_FlushDeleteQueue` | `00063784` | `00478538` | (queue, pool): each queued object's slot `+0xc` with flags 2, then `Pool_Free`, then the count `+0x50` zeroed |
| `Pool_LinkFreeList` | `000637e4` | `00471944` | (pool, element size, count, buffer); the header and the node links as ES2's |
| `Pool_Init` | `00063878` | `004719cc` | (pool, element size, count): no `memset`; the block is `Mem_New`'s, which `Memory_Alloc` zero-fills while the heap is on ([the heap](#the-heap)), so a pool built after `Mem_InitHeap` starts zeroed |
| `Pool_Alloc`, `Pool_Free` | `000638c8`, `0006398c` | `00471a24`, `00471abc` | none |
| `Pool_AllocThunk`, `Pool_AllocZeroed` | `0006394c`, `0006395c` | | `Pool_Alloc` with a second argument not read, and `Pool_Alloc` with the element zeroed; `es2_xref.py` finds no reference to either |
| `Pool_Next`, `Pool_Prev`, `Pool_Nth` | `000639ec`, `00063a20`, `00063a78` | `00471b0c`, `00471b64`, `00471ba4` | `Pool_Next` without the diagnostic |

Destroying an object through its slot `+0xc` with 3 queues it and leaves it linked; `Pool_Free`, which `es2_xref.py` finds called from `ObjectPool_FlushDeleteQueue` alone, unlinks it at its pool's phase-5 flush. So a walk that destroys the object it stands on steps on from it ([the tick](main-loop.md#the-tick--sim_maintick-00059700)). A recycled element keeps what its last occupant left in every field its constructor does not write.

## Open

- **Open:** what `X32_AllocMemory` stores at `00090024` (the extender's `EDX`), and what joins the circular list at `0009002c` besides its head `X32_FreeListHead` (`000bc166`). `es2_xref --decode` finds no write to the head or to `0009002c` but `Memory_Ctor`'s, and no reader of `0009002c` but `X32_LargestFreeBlock` and the code after it at `00060cee`, which no Ghidra function covers and which adds up the sizes on the list; a node linked in through a register would not show. Both belong to the startup module's memory handling ([compiler.md](../formats/compiler.md#startup)).
- **Open:** what calls `Memory_DrawUsageMap` (`00001304`, `ES1GO 000016b9`), `Memory_CheckOverrun` (`00000e3c`, `ES1GO 00001028`) and `maybe_Memory_OperatorNew` (`000015e4`, `ES1GO 00001a2c`), and `ES1GO`'s `Memory_LargestFreeBlock` (`00001091`): `es2_xref` finds no branch or stored pointer to any of them but a chance dword at `ES1GO 0004a223`, the operand of a `MOV ECX, 0x16` inside `TtmScript_RunDrawCommand`. A retail check: a DOSBox-X breakpoint on `00001304` and `00000e3c` through a mission's first minute in `ES1DBSIM`.
