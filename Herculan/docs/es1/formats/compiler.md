# DBSIM.EXE and GO.EXE — the compiler and its runtime

Both ES1 program images were built by a compiler of the Zortech C++ line, the line Symantec C++ and then Digital Mars C++ continue, and linked with that line's 32-bit DOS runtime library. This page sets out the evidence and the conventions the code follows, which play the part for ES1 that [Borland class descriptors](../../retail/formats/borland-rtti.md) plays for ES2. Image addresses follow [the X-32 executable](x32-executable.md#the-image).

## Identification

**The startup's messages.** The image's data opens at `ES1DBSIM 0009004e` with four DOS strings, each `\r\n` … `\r\n$`: `NULL code pointer called`, `Not enough memory`, `Bad stack size parameter` and `Stack Overflow`. `ES1GO` carries the same four. Digital Mars's 32-bit DOS library `sdx.lib` (version 8.31) holds three of them in the same form in its startup module `..\dos32\cp.asm`, with `Max of 128 args allowed` in place of `NULL code pointer called`.

**Library code.** Comparing the first 24 bytes of each `sdx.lib` function, relocated operands masked, against the images finds 46 of them at 44 addresses in `ES1DBSIM` and 52 at 50 addresses in `ES1GO`:

| Library module | Functions found in `ES1DBSIM` |
|---|---|
| `core32\double.asm` | the software floating-point helpers `__DADD@`, `__DSUB@`, `__DMUL@`, `__DCMP@`, `__DTST0@`, `__INTDBL@`, `__LNGDBL@`, `__FLTDBL@`, `__DBLFLT@`, and five internal routines |
| `dos32\inter.asm` | the interrupt API `int_getvector`, `int_setvector`, `int_service`, `int_prev` |
| `dos32\io.asm`, `find.asm`, `filelen.asm`, `getenv.asm`, `getdos.asm` | `open`, `sopen`, `rename`, `lseek`, `filesize`, `access`, `findfirst`, `filelength`, `getenv`, `dos_getdiskfreespace` |
| `core32\` string and conversion modules | `strcat`, `strncat`, `strncpy`, `stricmp`, `atoi`, `itoa`, `ultoa`, `ldexp`, `___faterr` |
| `core\printf.c`, `_printf.c`, `scanf.c`, `ecvt.c`, `fcvt.c` | `fprintf`, `sprintf`, `vfprintf`, `printf`, `fscanf`, `sscanf`, `ecvt`, `fcvt` |

The library postdates ES1 by eight years, and most of its functions do not match byte for byte: these are the ones whose code survived unchanged.

## Startup

The extender enters the image at `0xd` ([Hand-off](x32-executable.md#hand-off)). The startup:

1. Stores the PSP selector at `0009739c` and the environment's address at `000973ac`, and zero-fills the image from the end of its file bytes to its memory size.
2. Copies the command tail onto the stack and splits it at spaces and tabs into an argument vector, keeping a `"` or `'` quoted run as one argument. The first argument starting `=` is a decimal stack size, stored at `000973e8`, and is not passed on; a non-digit in it prints `Bad stack size parameter` and exits.
3. Allocates the argument vector (`00001618`) and copies the environment into the heap (`00000275`). A failed allocation prints `Not enough memory` and exits.
4. Asks the extender (`INT 21h` `AX=350Dh`) for a stack of the larger of `0x4000` bytes and the requested size, printing `Not enough memory` on failure, and stores the DOS version at `0009739e`.
5. Detects the coprocessor (`000002ba`): `000973a8` receives 0 when none answers, 2 when `+∞` and `−∞` compare equal (an 80287) and 3 otherwise.
6. Runs the static constructors (`0005b68c`), calls `main` (`00059350`) with the argument count and vector, and passes its result to `exit` (`0005b6f4`).

`ES1GO` follows the same steps, with its static constructors at `0004defc`, `main` at `00012ec7` and `exit` at `0004df64`.

**Address 0 is a trap.** The image's first bytes are a function that loads the address of `NULL code pointer called` and joins the startup's message-and-exit tail (`000001cc`, exit code 1). A call through a null code pointer lands there. A null data pointer faults instead, on the data selector's expand-down limit ([Loading the image](x32-executable.md#loading-the-image)).

## Static construction and exit

Two tables of function pointers meet at `000adc88`. The constructor walk (`0005b692`) runs from there down to `000adbb4`, the last entry first; the destructor walk (`0005b6ad`) runs from there up to `000adcd8`. Both skip zero entries.

`exit` calls each function on a pointer stack whose top is the dword at `000bdabe`, popping until it reaches a zero entry. Then, if the dword at `000bdb46` is set, it calls that function with the exit status and returns; otherwise it runs the destructor walk, calls the function at `0009eb4c` if one is set, and ends the program with `INT 21h` `AH=4Ch` (`0005b6c8`).

## Calling conventions

Arguments are pushed right to left, and a `short` takes a 4-byte slot. The result comes back in `EAX`. Functions keep `EBX`, `ESI`, `EDI` and `EBP`.

Most functions remove their own arguments with `ret N`, and their callers make no stack adjustment. `util\ovl.cpp`'s file reader (`000654bc`) takes a name, two `short` dimensions and a buffer, and returns with `ret 0x10`. Others return with a plain `ret` and leave the arguments to the caller: `00050d98`'s caller `0006d52c` follows the call with `add esp, 0xc`. Counted over Ghidra's function list:

| Region | Binary | `ret N` only | `ret` only | both | no return |
|---|---|---|---|---|---|
| game objects | `ES1DBSIM` | 1,766 | 150 | 0 | 163 |
| game objects | `ES1GO` | 1,199 | 210 | 0 | 37 |
| libraries | `ES1DBSIM` | 617 | 476 | 0 | 75 |
| libraries | `ES1GO` | 532 | 474 | 0 | 78 |

Of the 150 game functions in `ES1DBSIM` that return with a plain `ret`, 26 read an argument slot; the rest take no arguments. A `ret` count alone does not separate a callee-cleaned function with no arguments from a caller-cleaned one.

`this` is the first stack argument (`[ebp+8]`), pushed last, and is removed with the others.

## Constructors and destructors

A constructor takes `this` and returns it in `EAX`. When `this` is null it calls `operator new` (`000015a4`, which takes the size and removes it) itself, and returns null if that fails. The code of each subobject constructor it inlines repeats the test with that subobject's own size: `0005b754`, for a `0x16`-byte class, tests and allocates at sizes `0x16`, `0xc`, 8, 6 and 4.

A destructor takes `this` and a flags word. It returns at once when `this` is null; otherwise it stores its own class's vptr, calls its base's destructor with flags 0, and calls `operator delete` (`000015f8`) on `this` when bit 0 of the flags is set. `0005b970` calls its base's destructor `0005b878` that way.

## Object layout

Members are byte-packed. The class `0005b754` builds is `0x16` bytes: a `0xc`-byte subobject, a 6-byte subobject at `+0xc` copied as one dword and two bytes (`0005b85c`), and the vptr at `+0x12`, which is not a multiple of 4.

The vptr follows the subobjects' data rather than leading the object. Across `ES1DBSIM`, the stores of a vtable address into an object use 565 times offset 0, 140 times `+2`, 52 times `+0x1c` and 39 times `+0x12`, and one class family keeps it at `+0x87` (`0004b70f`, vtables `00095756`–`00095796`).

## Vtables

A vtable's first dword is 0 and its slots follow from `+4`. Of the 426 vtable addresses `ES1DBSIM`'s code stores into objects, 414 hold 0 at `+0`; all 218 in `ES1GO` do. Virtual calls index the slots from `+4` (`call [reg+4]`, `[reg+8]`, `[reg+0xc]`, …). Vtables sit at any byte address: `0005b754`'s class uses `000973fe`.

A slot can hold 0. The vtables `000973fe` and `0009740e` each hold their destructor at `+4`, followed by 8 zero bytes before the next vtable.

A slot can point at the `0x90` padding before a function's prologue rather than at the prologue: `000973fe`'s destructor slot holds `0005b875`, three bytes before the destructor's `push ebp` at `0005b878`. Walking each of `ES1DBSIM`'s vtables up to its first zero slot, 1,220 slots point at a prologue and 534 at a run of 1–3 `0x90` bytes before one. Direct calls do the same: a scan for `E8` call bytes finds 449 that reach a `0x90` run before a prologue, and 78 functions are called both ways (`operator new` at `000015a4` from `00000d59` by way of `000015a3`, and at `000015a4` from `0000187e`). The padding falls through to the prologue, so both addresses are entries of the same function.

## Libraries against game code

The linker placed the game's objects first, each identified by the source path its asserts push, ending with `main.cpp` and `init.cpp` in `ES1DBSIM` and `system.cpp` in `ES1GO`. The static-constructor routine (`0005b68c`, `0004defc`) opens the library region that follows. It holds the C runtime, the `util\` modules (`libio.cpp` and `ovl.cpp` in `ES1DBSIM`; `mods.cpp`, `ovl.cpp` and `my_math.cpp` in `ES1GO`), and Dynamix's shared class libraries: the persistent-class table that includes `0009efd0`–`0009f05c` pairs `TSGroup`, `TSShape`, `TSBSPPart` and the other `TS*` class names with code addresses in the region, `0005d564`–`00068908` for the entries in that range.

| Binary | Game objects | Library region |
|---|---|---|
| `ES1DBSIM` | 2,079 functions, 349,600 bytes | 1,168 functions, 179,549 bytes |
| `ES1GO` | 1,446 functions, 274,510 bytes | 1,084 functions, 171,607 bytes |

The counts are Ghidra's function list after [step 0.2](../../herculan/plan-es1-support.md)'s import, including the functions it starts on padding.

## Open

- **Open:** the product and version: Zortech C++ 3.x or Symantec C++ 6.x. A runtime library of the period, matched as `sdx.lib` was, settles it.
- **Open:** what selects `ret N` or caller cleanup for a function: the linkage (`extern "C"` against C++), a compiler switch per module, or a calling-convention keyword.
- **Open:** whether a zero vtable slot is a pure virtual function, and whether the zero bytes that follow `000973fe`'s destructor slot are slots. A call through a zero slot reaches the address-0 trap.
- **Open:** whether the compiler emits run-time type information anywhere; the vtable's first dword holds none.
- **Open:** what decides whether a reference to a function targets its padding or its prologue.
- **Open:** how the library region divides between the C runtime and Dynamix's libraries, function by function.
- **Open:** the runtime's `INT 21h` `AX=35xxh` calls, which are the extender's services (`3501h`, `3503h`, `3504h`, `3505h`, `350Ch`, `350Dh`).
