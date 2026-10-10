# DBSIM.EXE and GO.EXE — the compiler and its runtime

Both ES1 program images were built with Symantec C++, the line Zortech C++ began and Digital Mars C++ continues, and linked with its runtime library for the X-32 DOS extender. This page sets out the evidence and the conventions the code follows, which play the part for ES1 that [Borland class descriptors](../../retail/formats/borland-rtti.md) plays for ES2. Image addresses follow [the X-32 executable](x32-executable.md#the-image). Names in backticks without an address of their own are the runtime library's, from its source.

## Identification

**The startup is Symantec's X-32 startup module.** Symantec C++ 6.1 ships its DOS-extender runtime with source, and its `CX.ASM` (Doug Huffman, "specifically designed for the Symantec compiler") is ES1's startup step for step: the trap at address 0, the command-line parser, the stack request, the coprocessor call and the calls into `__cinit`, `_main` and `_exit`, with the same four messages in the same order (`NULL code pointer called`, `Not enough memory`, `Bad stack size parameter`, `Stack Overflow`, at `ES1DBSIM 0009004e`). ES1's has two additions. Its trap opens with `push ebp; mov ebp,esp`, so the entry falls at `0xd` instead of `0xa`. Its `__x386_init` first sets the data selector's limit to `0x90023` (`INT 21h` `AX=3505h`, the extender's set-limit call) before storing `__psp`.

**Library functions match whole.** Comparing every function of 6.1's `SDX.LIB` and `X386.LIB` against the images, relocated operands masked and the whole body required to agree, finds 87 in `ES1DBSIM` and 101 in `ES1GO`. `tools/scripts/es1_libmatch.py` makes the comparison, extracting the two libraries from the CD:

| 6.1 module | Matching in `ES1DBSIM` | Also in `ES1GO` |
|---|---|---|
| `double.asm` | all 28: the software double-precision routines (`__DADD@`, `__DSUB@`, `__DMUL@`, `__DDIV@`, `__DCMP@`, the `int`/`long`/`float` conversions) and their helpers | the same |
| `float.asm`, `floattyp.asm` | — | the 12 single-precision routines |
| `inter.asm` | the interrupt API: `int_getvector`, `int_setvector`, `int_service`, `int_intercept`, `int_restore`, `int_prev` | the same |
| `strings.asm` | `memset`, `memmove`, `memcmp`, `strchr`, `strcat`, `strncat`, `strncpy`, `strncmp`, `stricmp`, `__memintset` | 8 of them |
| `io.asm`, `find.asm`, `filelen.asm`, `getenv.asm`, `getdos.asm`, `fputc.asm` | `sopen`, `creat`, `rename`, `lseek`, `filesize`, `findfirst`, `filelength`, `getenv`, four `dos_` calls, `fputc`, `fgetc` | the same, and six `dosfil.asm` calls |
| `cinit.asm`, `x386.asm`, `funcs32.asm` | `__cinit`, `__exit`, `___faterr`, `__x386_coproc_init`, `__dosx_get_abs_address`, `__x386_map_physical_address` | the same |
| C modules | `printf`, `fprintf`, `sprintf`, `vfprintf`, `fscanf`, `ecvt`, `setbuf`, `rand`, `clock`, `toupper`, `tolower`, `atol`, `labs`, `ltoa`, `ldexp`, `spawnl`, `spawnlp`, `execl`, `execlp`, `streambuf::underflow` | all but `rand` and `clock` |

Eight of these matches do not say by themselves which function they found. `fprintf`, `sprintf`, `spawnl` and `spawnlp` each match at the same two addresses (`ES1DBSIM 0005f6e0` and `0005f77c`), and `printf`, `execl` and `execlp` at one (`0005f712`): each is a short wrapper that differs from the others only in the function it calls, which the masking hides. The callees settle these seven: `0005f6e0` calls `vfprintf`, `0005f77c` calls `vsprintf` and `0005f712` calls `vprintf`, so they are `fprintf`, `sprintf` and `printf`, in `printf.c`'s own order, and `ES1GO` has the same three at `0004e140`, `0004e1dc` and `0004e172`. `streambuf::underflow` is 8 bytes and matches at two addresses.

**ES1's runtime is a later revision than 6.1's.** Most of the library's functions that ES1 uses differ in places: `open` (`00074947`) sends its error exit through a routine shared with the other `io.asm` calls (`00074922`, inside `Dos_ReadWrite`), where 6.1's sets `errno` inline. The extender kernel in ES1's executables reads `DOS extender Copyright 1991-1994 by Doug Huffman`; 6.1's `X386.LIB` carries `Copyright 1991 1992`.

## Startup

The extender enters the image at `0xd` (`__x386_init`; [Hand-off](x32-executable.md#hand-off)). The startup:

1. Stores the PSP selector in `__psp` (`0009739c`) and the environment's address in `__envptr` (`000973ac`), and zero-fills `_BSS`, from `000adcd8` to `000bdd40`, the image's memory size.
2. Copies the command tail onto the stack and splits it at spaces and tabs into an argument vector, keeping a `"` or `'` quoted run as one argument. While `__stack` (`000973e8`) is non-zero, an argument starting `=` replaces it with the decimal number that follows and is not passed on; a non-digit in it prints `Bad stack size parameter` and exits, and a zero leaves it an ordinary argument. Both executables ship `__stack` as `0x8000`.
3. Allocates the vector through `malloc` (`00001618`), stores `__argc` (`000973b0`) and `__argv` (`000973b4`), and points `argv[0]` at the program's path after the environment strings. A failed allocation prints `Not enough memory` and exits.
4. Asks the extender (`INT 21h` `AX=350Dh`) for a stack of the larger of `__stack` and 16 KB, printing `Not enough memory` on failure, and stores the DOS version in `__osmajor` (`0009739e`) and `__osminor` (`0009739f`).
5. Detects the coprocessor (`__x386_coproc_init`, `000002ba`): `000973a8` receives 0 when none answers, 2 when `+∞` and `−∞` compare equal (an 80287) and 3 otherwise.
6. Copies the environment into the heap (`init_envp`, `00000275`), runs the static constructors (`__cinit`, `0005b68c`), calls `main` (`00059350`) with `__argc` and `__argv`, and passes its result to `exit` (`0005b6f4`).

`ES1GO` follows the same steps, with `__stack` at `00083cd4`, `__cinit` at `0004defc`, `main` at `00012ec7` and `exit` at `0004df64`.

The `malloc` the startup calls is the game's own, from `memory.cpp`, among the game's objects rather than in the library region ([the entry points](../simulation/memory.md#the-entry-points)).

**Address 0 is a trap.** The image's first bytes load the address of `NULL code pointer called` and jump to the startup's message-and-exit tail (`000001cc`), which prints it and calls `__exit` (`0005b6c8`) with status 1. A call through a null code pointer lands there. A null data pointer faults instead, on the data selector's expand-down limit ([Loading the image](x32-executable.md#loading-the-image)).

## Static construction and exit

The linker gathers each object's static constructor and destructor pointers into the segments `XI` and `XC`, which meet at `000adc88`. `__cinit`'s constructor walk (`0005b692`) runs from there down to `000adbb4`, the last entry first; `__dodtors` (`0005b6ad`) runs from there up to `000adcd8`. Both skip zero entries.

`exit` (`0005b6f4`) calls the functions `atexit` has registered, newest first, through `_atexitp` (`000bdabe`). Then, if `_hookexitp` (`000bdb46`) is set, it calls that with the exit status and returns; otherwise it runs `__dodtors`, calls `_fcloseallp` (`0009eb4c`, which ships pointing at `00073ec4`) if it is set, and ends the program through `__exit` (`0005b6c8`, `INT 21h` `AH=4Ch`).

## Calling conventions

The compiler has two conventions, and the runtime library shows which declarations get which. A C++ function is mangled with calling-convention code `C` (`?setb@streambuf@@IACXPAD0H@Z`) and pushes its arguments left to right, `this` last, and removes them itself: `streambuf::setb(char *base, char *end, int own)` reads `this` at `[ebp+8]`, `own` at `+0xc`, `end` at `+0x10` and `base` at `+0x14`, and returns with `ret 0x10`. A C function pushes its arguments right to left and leaves them to the caller.

Both conventions return a result in `EAX`, give a `short` a 4-byte slot, and keep `EBX`, `ESI`, `EDI` and `EBP`.

Most of ES1's game functions remove their own arguments. `util\ovl.cpp`'s file reader (`000654bc`) takes a name at `[ebp+8]`, two `short` dimensions and a buffer at `[ebp+0x14]`, and returns with `ret 0x10`. Others leave the arguments to the caller: `00050d98`'s caller `0006d52c` follows the call with `add esp, 0xc`. Counted over Ghidra's function list:

| Region | Binary | `ret N` only | `ret` only | both | no return |
|---|---|---|---|---|---|
| game objects | `ES1DBSIM` | 1,777 | 156 | 0 | 159 |
| game objects | `ES1GO` | 1,200 | 211 | 0 | 36 |
| libraries | `ES1DBSIM` | 622 | 481 | 0 | 77 |
| libraries | `ES1GO` | 537 | 479 | 0 | 80 |

Of the 156 game functions in `ES1DBSIM` that return with a plain `ret`, 27 read an argument slot; the rest take no arguments, which the callee-cleaned convention also returns from with a plain `ret`.

## Constructors and destructors

A constructor takes `this` and returns it in `EAX`. When `this` is null it calls `operator new` (`000015a4`, which takes the size and removes it) itself, and returns null if that fails. The code of each subobject constructor it inlines repeats the test with that subobject's own size: `TSContext_Ctor` (`0005b754`), for the `0x16`-byte `TSContext`, tests and allocates at sizes `0x16`, `0xc`, 8, 6 and 4.

An array of objects goes through the runtime's `__vec_new` (`00061038`), which allocates count × size plus 4 bytes when it is given no memory, stores the count in the first dword, runs the constructor on each element and returns the address after the count; `__vec_ctor` (`000610cb`) runs the constructor over memory it is given. 6.1's library declares both `(p, size, count, ctor)` and mangles them as C functions; ES1's remove their own arguments.

A destructor takes `this` and a flags word. It returns at once when `this` is null; otherwise it stores its own class's vptr, calls its base's destructor with flags 0, and calls `operator delete` (`000015f8`) on `this` when bit 0 of the flags is set. `TSCamera_Dtor` (`0005b970`) calls its base's destructor `TSContext_Dtor` (`0005b878`) that way.

## Object layout

Members are byte-packed. `TSContext` is `0x16` bytes: a `0xc`-byte point, a 6-byte `int16` triple at `+0xc` that `TSContext_CtorWithFrame` copies as one dword and two bytes (`0005b85c`), and the vptr at `+0x12`, which is not a multiple of 4.

The vptr follows the subobjects' data rather than leading the object. Across `ES1DBSIM`, the stores of a vtable address into an object use 565 times offset 0, 140 times `+2`, 52 times `+0x1c` and 39 times `+0x12`, and one class family keeps it at `+0x87` (`0004b70f`, vtables `00095756`–`00095796`).

## Vtables

A vtable's first dword is 0 and its slots follow from `+4`. Of the 426 vtable addresses `ES1DBSIM`'s code stores into objects, 414 hold 0 at `+0`; all 218 in `ES1GO` do. Virtual calls index the slots from `+4` (`call [reg+4]`, `[reg+8]`, `[reg+0xc]`, …). Vtables sit at any byte address: `TSContext` uses `000973fe`.

A slot can hold 0. The vtables `000973fe` and `0009740e` each hold their destructor at `+4`, followed by 8 zero bytes before the next vtable.

A slot can point at the `0x90` padding before a function's prologue rather than at the prologue: `000973fe`'s destructor slot holds `0005b875`, three bytes before `TSContext_Dtor`'s `push ebp` at `0005b878`. Walking each of `ES1DBSIM`'s vtables up to its first zero slot, 1,220 slots point at a prologue and 534 at a run of 1–3 `0x90` bytes before one. Direct calls do the same: a scan for `E8` call bytes finds 449 that reach a `0x90` run before a prologue, and 78 functions are called both ways (`operator new` at `000015a4` from `00000d59` by way of `000015a3`, and at `000015a4` from `0000187e`). The padding falls through to the prologue, so both addresses are entries of the same function.

## Libraries against game code

The linker placed the game's objects first, each identified by the source path its asserts push, ending with `main.cpp` and `init.cpp` in `ES1DBSIM` and `system.cpp` in `ES1GO`. `__cinit` (`0005b68c`, `0004defc`) opens the library region that follows. It holds the C runtime, the `util\` modules (`libio.cpp` and `ovl.cpp` in `ES1DBSIM`; `mods.cpp`, `ovl.cpp` and `my_math.cpp` in `ES1GO`), and Dynamix's shared class libraries: the persistent-class table that includes `0009efd0`–`0009f05c` pairs `TSGroup`, `TSShape`, `TSBSPPart` and the other `TS*` class names with code addresses in the region, `0005d564`–`00068908` for the entries in that range. The runtime's functions are spread through the whole region rather than gathered at its end: the whole-function matches run from `0005b68c` to `0008f858`.

| Binary | Game objects | Library region |
|---|---|---|
| `ES1DBSIM` | 2,092 functions, 354,602 bytes | 1,180 functions, 180,883 bytes |
| `ES1GO` | 1,447 functions, 274,796 bytes | 1,096 functions, 172,749 bytes |

The counts are Ghidra's function list after [step 0.2](../../herculan/plan-es1-support.md)'s import, including the functions it starts on padding. The whole-function matches fall in 61 of `ES1DBSIM`'s library-region functions and 73 of `ES1GO`'s.

## Open

- **Open:** the Symantec C++ release, and the X-32 release, that ES1 was built with. Both are later than 6.1's and earlier than Symantec C++ 7, which shipped in 1995; an update to 6.1 or a 1994 FlashTek X-32VM release with its own startup and libraries are the candidates, and the startup's two additions and `open`'s shared error exit are what to compare.
- **Open:** whether a zero vtable slot is a pure virtual function, and whether the zero bytes that follow `000973fe`'s destructor slot are slots. A call through a zero slot reaches the address-0 trap.
- **Open:** whether the compiler emits run-time type information anywhere; the vtable's first dword holds none.
- **Open:** what decides whether a reference to a function targets its padding or its prologue.
- **Open:** how the library region divides between the C runtime and Dynamix's libraries, function by function. The 6.1 source identifies runtime functions whose bytes differ.
- **Open:** which of `streambuf::underflow`'s two [matches](#identification) is the function, and `double.asm`'s `exception`, whose match (`ES1DBSIM 0007e188`, `ES1GO 0005b800`) lies inside the function Ghidra starts at `0007e15a` (`ES1GO 0005b7d2`) rather than at a function of its own.
- **Open:** the runtime's `INT 21h` `AX=35xxh` calls, which are the extender's services (`3501h`, `3503h`, `3504h`, `3505h`, `350Ch`, `350Dh`).
