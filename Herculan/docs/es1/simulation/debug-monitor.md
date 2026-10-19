# DBSIM.EXE and GO.EXE — the monochrome debugging screen

Both ES1 programs can write text to the screen of a monochrome display adapter while the game draws on a colour one, a developer's second monitor. They drive the adapter directly; ES2 reaches its monochrome monitor through a driver instead ([`-m`](../../retail/command-line.md#dbsim)). Addresses are `ES1DBSIM` image addresses unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)).

## Detecting the screen

The screen is used only when both adapters are present, which the program decides once, at startup. `Mono_StaticInit` (`0006102c`), a static constructor (its address is the entry at `000adc78` that `__cinit` runs, [static construction](../formats/compiler.md#static-construction-and-exit)), builds `Mono_Screen` (`000b5e70`) with `MonoScreen_Ctor` (`00060d24`): two bytes, `+1` set to 1 and `+0` to the result of `Crtc_DetectDualDisplay` (`0007634a`).

`Crtc_DetectDualDisplay` returns 1 when `Crtc_Probe` (`00076377`) answers at both CRTC index ports, `3b4` (the monochrome adapter's) and `3d4` (the colour adapter's), and 0 otherwise. `Crtc_Probe` reads the cursor-location registers `0xe` and `0xf` through the data port, writes 4 and `0xa` to them, reads both back, and puts the saved values back (`Crtc_RestoreCursorLocation`, `000763d4`); it answers when both read back as written.

Each function below does its work only while `Mono_Screen`'s two bytes share a set bit, that is while `+0` is 1.

## Writing to it

The screen is the adapter's text memory at linear `0xb0000` (`Mono_Clear` addresses it through `0009003c + 0xb0000`): 25 rows of 80 cells, each a character byte and an attribute byte.

| Function | Address | Arguments | What it does |
|---|---|---|---|
| `Mono_Clear` | `00060d78` | | fills the 2000 cells with `0x0720`, a space in attribute 7 |
| `Mono_WriteChar` | `00060db0` | `(ch, row, column)` in stack order | writes `ch` with the attribute byte `00097fa0` at `row × 80 + column`, for rows 0–24 |
| `Mono_WriteText` | `00060e08` | `(text, row, column)` in stack order | writes a NUL-terminated string from that cell, with the same attribute |
| `Mono_Printf` | `00060e78` | `(column, row, format, ...)` | formats into a 256-byte buffer and writes it there |
| `Mono_PrintfRow` | `0006016c` | `(format, row, ...)` | formats into a 100-byte buffer and writes it at column 0 |
| `Mono_ShowCursor` | `00061004` | `(show)` | writes the cursor start scan line, CRTC register `0xa` at `3b4`/`3b5`: `0xb` when `show` is non-zero (`maybe_MonoCrtc_ShowCursor`, `0007633b`), `0xf` otherwise (`maybe_MonoCrtc_HideCursor`, `0007632c`) |
| `Mono_PrintScreen` | `00060ed0` | `(printer)` | copies the screen to `LPT<printer>`, below |

`0xb` is the cursor start of the BIOS's monochrome text mode, whose cursor ends at line `0xc`; a start of `0xf`, below that end, shows no cursor on the original adapters.

`Mono_PrintScreen` opens `LPT` followed by the printer digit and sends the 2000 cells in order: before each character the PCL escape `ESC(s0B` for attribute 7 and `ESC(s7B` (bold) for any other, a NUL as a space, and a CR LF after every 80. Each cell shows `0x0fdb`, a solid block, while it is sent, and is then put back with a NUL turned into a space.

The memory, driver and slot listings at `0004e858` and the grenade distance at `0000b714` print with `Mono_Printf`, and the frame-rate meter below with `Mono_PrintfRow`.

`GO.EXE` has the same screen: `Mono_Screen` at `ES1GO 000a0bdc`, built by `Mono_StaticInit` (`ES1GO 0004fab8`), with `Mono_WriteChar`, `Mono_WriteText`, `Mono_Printf`, `Mono_PrintScreen` and `Mono_ShowCursor` at `ES1GO 0004f83c`, `0004f894`, `0004f904`, `0004f95c` and `0004fa90`, and the detection at `ES1GO 00065c42`–`00065ccc`. `Heap_DrawMap` prints its map with `Mono_Printf` ([the heap](memory.md)).

## The frame-rate meter

`FrameRate_Update` (`00060214`) measures how long each frame took and prints the rate on the monochrome screen. Called once a frame, it:

- **On its first call** registers `FrameRate_TimerTick` (`000601fc`) with `Timer_AddCallback` (`0006d289`) at count 1, sets `FrameRate_TicksPerSecond` (`000b5e2c`) to `1000000 / Math_Q14Multiply(w, 0x35a1)`, where `w` is the PC timer's divisor, so 120 ([the rate](timer.md#the-rate)), loads `FrameRate_Countdown` (`000b5e24`) with 32000, and returns.
- **On every later call** takes the ticks elapsed, `e = 32000 − FrameRate_Countdown`, reloads the countdown with 32000, and prints. `FrameRate_TimerTick` decrements the countdown each time the timer calls it, stopping at 0.

| `e` | Row 4 shows | Best and worst |
|---|---|---|
| 32000, the countdown ran out | `Current: Slower than %d fps.` with `32000 / ticksPerSecond` | unchanged |
| above `ticksPerSecond` | `Current:` and `x / 10` as the whole part and `x % 10` as the hundredths, with `x = 10e / ticksPerSecond` | unchanged |
| 0 | `Current: Faster than %d fps.` with `ticksPerSecond` | unchanged: `x` is 99999, read as 999.99 |
| otherwise | `Current:` and `x / 100` and `x % 100`, with `x = 100 × ticksPerSecond / e` | updated |

For a frame longer than a second the figure is the frame's length in seconds, to a tenth, printed as if it were frames per second: a frame of 1.5 s shows as `1.05 fps`.

`FrameRate_Best` (`00097f58`) and `FrameRate_Worst` (`00097f5c`) each hold a whole part and hundredths, starting at 0.00 and 999.99. A rate becomes the best when it is higher and neither its whole part is 999 nor its hundredths 99, so a rate ending in `.99` is never recorded as the best; it becomes the worst whenever it is lower.

`FrameRate_PrintRate` (`00060198`) prints `Best:` on row 3 and `Worst:` on row 5 as `%s%2d.%02d fps.`, or as `%s--.-- fps.` when the rate is 999.99 or 0.00, the values the two start from. After the rows the meter adds 1 to `FrameRate_FrameCount` (`000b5e28`) and prints `Frame # %d` on row 1.

`FrameRate_GetFrameCount` (`0006020c`) returns the count, and `Vcr_FlushCheckpoints` (`00000c84`, [the subsystem phases](main-loop.md#the-subsystem-phases)) puts it in its `Playback error. Frame #%ld` message.

`Timer_AddCallback` (`0006d289`) at count 1 runs `FrameRate_TimerTick` on every interrupt of the PC timer ([callbacks](timer.md#callbacks)).

## The component panel

**A developer panel lists a HERC's components on the monochrome screen with a value beside each, and lets a cursor walk the list and raise or lower the value it is on by 32.** ES2's simulator has the same five functions in the same order, `MonoComponentPanel_*` (`DBSIM 0045c630`–`0045c860`), printing through its own `Mono_Printf`; `es2_xref.py` finds no reference to any of them in either game.

`MonoComponentPanel_Init` (`000523b8`), (block), keeps the block at `MonoComponentPanel_Block` (`000bd900`), whose `int16` values start at `+2`, clears the screen with `Mono_Clear`, and walks 41 entries: for each it prints the label from `MonoComponentPanel_Labels` (`00095ffc`) at column `MonoComponentPanel_LabelColumn` (`000bd908`) and row `MonoComponentPanel_Row` (`000bd90a`), zeroes the value and prints `%d` with 0 at column `MonoComponentPanel_ValueColumn` (`000bd90c`), and moves down a row; on reaching row `0x17` it goes back to row 1 with both columns `0x28` further right. The columns start at 2 and `0x1e`, the row at 1. It then puts the cursor `MonoComponentPanel_Cursor` (`000bd904`) back on the first value and prints `>` at column 1, row 1. ES2's prints `%d` with no value passed.

ES1's label table holds 33 pointers, where ES2's holds 41: `FRONT_COCKPIT`, `REAR_COCKPIT`, `LEFT_BRACKET`, `RIGHT_BRACKET`, `LEFT_SHOULDER`, `RIGHT_SHOULDER`, `CROTCH`, `LEFT_QUAD`, `RIGHT_QUAD`, `LEFT_SHIN`, `RIGHT_SHIN`, `LEFT_FOOT`, `RIGHT_FOOT`, `LEFT_LEG_ACTUATOR`, `RIGHT_LEG_ACTUATOR`, `LEFT_ARM_ACTUATOR`, `RIGHT_ARM_ACTUATOR`, `SENSORS`, `ENGINE`, `HYDRAULICS`, `GYROS`, `LIFE_SUPPORT`, `PILOT` and `WEAPON 1` to `WEAPON 10`. The walk still runs to 41, so labels 33 to 40 are the bytes after the table, the panel's own format strings, read as pointers, and the 41st is 0.

| Function | Address | Does |
|---|---|---|
| `MonoComponentPanel_CursorDown` | `000524c0` | prints a space over the `>`; unless the cursor is on the 41st value (`+0x52`), moves it to the next value and the row down one, going to row 1 of the next column pair after row `0x16`; prints `>` |
| `MonoComponentPanel_CursorUp` | `00052540` | the same upward: unless on the first value, to the previous one, and to row `0x16` of the previous column pair from row 1 |
| `MonoComponentPanel_IncreaseValue` | `000525b8` | adds `0x20` to the value, holds it to `0xff`, and prints it with `%d     ` |
| `MonoComponentPanel_DecreaseValue` | `00052600` | subtracts `0x20`, holds it to 0, and prints it the same way |

The four are frameless, with no Ghidra function, and print through `Mono_Printf`.

## Open

- **Open:** no caller of `FrameRate_Update` or `Mono_ShowCursor` found by `es2_xref.py`, so whether the meter ever runs in a retail build, and what then calls it, is open.
- **Open:** no write to `Mono_Screen` other than `MonoScreen_Ctor`'s found by a search of the disassembly for `000b5e70` and `000b5e71`.
- **Open:** `ES1GO`'s copy of `Mono_Clear`, and whether `GO.EXE` has a frame-rate meter; the twin search found none.
