# DBSIM.EXE and GO.EXE — memory.cpp, the heap

Both ES1 programs replace the runtime's allocator with their own heap, in `memory.cpp`. Addresses are in `ES1DBSIM` unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)). The shell's copy is an older revision of the same module: [GO.EXE's heap](#goexes-heap) lists where it differs.

## The heap

`Mem_InitHeap` (`00001524`) takes one block from the extender and makes it the heap object at `000ae4ac`. The block is the size asked for, capped at the largest free block the extender reports (`X32_LargestFreeBlock`, `00060cac`) less 25,000 bytes; if the extender cannot supply it, `Assert_Report` stops the program with `Memory alloc failed` ([asserts](asserts.md)). It then sets `000901d4`, and while that is set `operator_new`, `malloc` and `calloc` allocate from the heap and `operator_delete` and `free` return blocks to it. `Mem_HeapOff` (`00001598`) clears it: the allocators then take memory straight from the extender (`X32_AllocMemory`, `000001d7`, in 4 KB multiples) and the frees do nothing.

The heap object:

| Offset | Holds |
|---|---|
| `+0` | the block's base |
| `+4` | the block's size as `Mem_InitHeap` gave it |
| `+8` | the free list: the lowest free block |
| `+0xc` | the bytes free |
| `+0x10` | the lowest value `+0xc` has reached |

Every block, free or allocated, opens with an 8-byte header: `+0` the next free block's address, and `+4` the block's size, header included, a multiple of 8. An allocated block has `ABLK` (`0x4b4c4241`) in place of the link; a free block has `FBLK` (`0x4b4c4246`) at `+8`, the first dword of what would be its payload. The free list is kept in address order.

`Heap_Alloc` (`00000ec0`) rounds the request plus the header up to a multiple of 8 and takes the first free block that is exactly that size or more than 16 bytes larger, splitting off the remainder as a free block. It zero-fills the payload. `Heap_Free` (`0000100c`) puts a block back in address order and merges it with free neighbours that touch it. `Heap_Realloc` (`000010ec`) resizes in place against a free block that follows, and moves the data otherwise. `Heap_LargestFree` (`00000e88`, `ES1GO 00001091`) returns the largest free block's size less its header; the loaders' arenas take a block of that size ([`../formats/dts-shape.md`](../formats/dts-shape.md#differences-from-es2)).

## The entry points

| | C++ convention (callee cleans) | C convention |
|---|---|---|
| allocate | `operator_new` (`000015a4`) | `malloc` (`00001618`) |
| release | `operator_delete` (`000015f8`) | `free` (`00001654`) |
| resize | — | `realloc` (`00001670`) |
| allocate zeroed | — | `calloc` (`00001688`) |

Each pair shares one body. `Mem_AllocFromHeap` (`000015e4`) allocates from a heap object it is passed rather than from `000ae4ac`. `realloc` goes to the heap whether or not `000901d4` is set. `calloc` clears its block over the size in the header before it; a block taken from the extender while the heap is off has no header, and the 4 bytes before it are read as one.

## Who makes the heap

Each program's `main` makes the heap first and turns it off last:

| | `Sim_Main` (`00059350`) | `Shell_Main` (`ES1GO 00012ec7`) |
|---|---|---|
| asks for | 6,000,000 | the dword `0007e738`, 3,700,000 |
| fatal assert when it gets 100,000 or fewer | `main.cpp` line `0xe3` | `src\main.cpp` line `0x1f8` |
| after mounting the volumes | an assert when under `0x26e8f0` (2,550,000); `g_LowMemory` set when under `0x3c45b0` (3,950,000) ([low memory](../formats/chassis-dat.md#low-memory)) | an assert when under `BND\MAIN.BND`'s +12, 2,400,000; the byte `0007fd09` set when under its +8, 3,700,000 ([`MAIN.BND`](../formats/bnd-notes.md#bndmainbnd)) |
| `Mem_HeapOff` | after the frame loop, and before the early exit when `000b5bc4` is clear | after the shell's loop, and before the early exit when `0001272d` returns non-zero |

## GO.EXE's heap

`ES1GO`'s heap is the object at `0009a308`, switched by `0007d7b4`, with the same block format, entry points and allocation policy. Where it differs:

- The heap object has no low mark: it is the four dwords `+0` to `+0xc`, and `Heap_Alloc` (`ES1GO 000010d9`) takes no argument for one.
- `Mem_InitHeap` (`ES1GO 0000195f`) leaves 15,000 bytes of the extender's largest free block unused, where `ES1DBSIM`'s leaves 25,000.
- The heap is a constructed object: `Heap_Ctor` (`ES1GO 00000ef4`) builds it, `Mem_ConstructHeap` (`ES1GO 00001b5c`) on the global one.
- It has inspection functions `ES1DBSIM` does not: `Heap_IsAllocated`, `Heap_IsValidAllocation`, a walker over every block (`HeapIter_Init`, `HeapIter_Next`), and `Heap_DrawMap` (`ES1GO 000016b9`), which draws a rows-by-columns map of the heap on the monochrome monitor (`Mono_Printf`, `ES1GO 0004f904`), one character per cell by how much of its share is allocated: 7 for none, `0xb1` below half, `0xb2` below all, `0xdb` for all.
- `Heap_IsValidAllocation` takes the heap's end as its base plus 8 times the dword at `+4`, where `Heap_Init` stores the size in bytes, so a block ending at the heap's true end does not pass that test.

## Object pools

**The simulator's short-lived objects, its machines and its structures come from fixed pools, walked oldest first, as ES2's are ([walking a pool](../../retail/simulation/sim-object-layout.md#walking-a-pool--oldest-first)).** The pool functions sit in the library region, `00063784`–`00063aa3`, each removing its own arguments:

| ES1 | Address | ES2 (DBSIM) | Difference |
|---|---|---|---|
| `ObjectPool_FlushDeleteQueue` | `00063784` | `00478538` | (queue, pool): each queued object's slot `+0xc` with flags 2, then `Pool_Free`, then the count `+0x50` zeroed |
| `Pool_LinkFreeList` | `000637e4` | `00471944` | (pool, element size, count, buffer); the header and the node links as ES2's |
| `Pool_Init` | `00063878` | `004719cc` | (pool, element size, count): no `memset`; the block is `operator_new`'s, which `Heap_Alloc` zero-fills while the heap is on ([the heap](#the-heap)), so a pool built after `Mem_InitHeap` starts zeroed |
| `Pool_Alloc`, `Pool_Free` | `000638c8`, `0006398c` | `00471a24`, `00471abc` | none |
| `Pool_AllocThunk`, `Pool_AllocZeroed` | `0006394c`, `0006395c` | | `Pool_Alloc` with a second argument not read, and `Pool_Alloc` with the element zeroed; `es2_xref.py` finds no reference to either |
| `Pool_Next`, `Pool_Prev`, `Pool_Nth` | `000639ec`, `00063a20`, `00063a78` | `00471b0c`, `00471b64`, `00471ba4` | `Pool_Next` without the diagnostic |

Destroying an object through its slot `+0xc` with 3 queues it and leaves it linked; `Pool_Free`, which `es2_xref.py` finds called from `ObjectPool_FlushDeleteQueue` alone, unlinks it at its pool's phase-5 flush. So a walk that destroys the object it stands on steps on from it ([the tick](main-loop.md#the-tick--sim_maintick-00059700)). A recycled element keeps what its last occupant left in every field its constructor does not write.

## Open

- **Open:** what `X32_AllocMemory` stores at `00090024` (the extender's `EDX`) and what the circular list at `0009002c`, which `X32_LargestFreeBlock` searches, holds. Both belong to the startup module's memory handling ([compiler.md](../formats/compiler.md#startup)).
- **Open:** what calls `Heap_DrawMap`, `Heap_IsValidAllocation` and `Mem_ConstructHeap` in `ES1GO`, and what `Heap_Ctor`'s store of `000aa24a`'s own address into it marks.
