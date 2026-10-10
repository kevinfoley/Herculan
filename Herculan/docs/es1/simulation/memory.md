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

`Heap_Alloc` (`00000ec0`) rounds the request plus the header up to a multiple of 8 and takes the first free block that is exactly that size or more than 16 bytes larger, splitting off the remainder as a free block. It zero-fills the payload. `Heap_Free` (`0000100c`) puts a block back in address order and merges it with free neighbours that touch it. `Heap_Realloc` (`000010ec`) resizes in place against a free block that follows, and moves the data otherwise.

## The entry points

| | C++ convention (callee cleans) | C convention |
|---|---|---|
| allocate | `operator_new` (`000015a4`) | `malloc` (`00001618`) |
| release | `operator_delete` (`000015f8`) | `free` (`00001654`) |
| resize | — | `realloc` (`00001670`) |
| allocate zeroed | — | `calloc` (`00001688`) |

Each pair shares one body. `Mem_AllocFromHeap` (`000015e4`) allocates from a heap object it is passed rather than from `000ae4ac`. `realloc` goes to the heap whether or not `000901d4` is set. `calloc` clears its block over the size in the header before it; a block taken from the extender while the heap is off has no header, and the 4 bytes before it are read as one.

## GO.EXE's heap

`ES1GO`'s heap is the object at `0009a308`, switched by `0007d7b4`, with the same block format, entry points and allocation policy. Where it differs:

- The heap object has no low mark: it is the four dwords `+0` to `+0xc`, and `Heap_Alloc` (`ES1GO 000010d9`) takes no argument for one.
- `Mem_InitHeap` (`ES1GO 0000195f`) leaves 15,000 bytes of the extender's largest free block unused, where `ES1DBSIM`'s leaves 25,000.
- The heap is a constructed object: `Heap_Ctor` (`ES1GO 00000ef4`) builds it, `Mem_ConstructHeap` (`ES1GO 00001b5c`) on the global one.
- It has inspection functions `ES1DBSIM` does not: `Heap_IsAllocated`, `Heap_IsValidAllocation`, `Heap_LargestFree`, a walker over every block (`HeapIter_Init`, `HeapIter_Next`), and `Heap_DrawMap` (`ES1GO 000016b9`), which draws a rows-by-columns map of the heap on the monochrome monitor (`Mono_Printf`, `ES1GO 0004f904`), one character per cell by how much of its share is allocated: 7 for none, `0xb1` below half, `0xb2` below all, `0xdb` for all.
- `Heap_IsValidAllocation` takes the heap's end as its base plus 8 times the dword at `+4`, where `Heap_Init` stores the size in bytes, so a block ending at the heap's true end does not pass that test.

## Open

- **Open:** where `Mem_InitHeap` is called from and with what size, and when the program turns the heap off.
- **Open:** what `X32_AllocMemory` stores at `00090024` (the extender's `EDX`) and what the circular list at `0009002c`, which `X32_LargestFreeBlock` searches, holds. Both belong to the startup module's memory handling ([compiler.md](../formats/compiler.md#startup)).
- **Open:** what calls `Heap_DrawMap`, `Heap_IsValidAllocation` and `Mem_ConstructHeap` in `ES1GO`, and what `Heap_Ctor`'s store of `000aa24a`'s own address into it marks.
