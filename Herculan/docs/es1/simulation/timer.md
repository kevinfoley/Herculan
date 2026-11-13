# DBSIM.EXE and GO.EXE — the PC timer

**ES1's simulator sets the PC's interval timer to interrupt 120 times a second, one interrupt every 8.33 ms, and every clock in the game counts those interrupts: the simulation tick and the frame's wait ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), the sound driver's 60 Hz service ([the driver](../formats/sound-samples.md#the-driver)), the cockpit's `.TTM` scripts ([the simulator's interpreter](../formats/ttm-scripts.md#the-simulator)) and the frame meter ([the frame-rate meter](debug-monitor.md#the-frame-rate-meter)).** ES2 measures time with Windows' tick count instead ([ES2's tick](../../retail/simulation/main-loop.md#the-tick)); its simulator builds the same timer object with the same divisor, and that object touches no hardware.

Addresses are `ES1DBSIM` image addresses unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The object's functions remove their own arguments, so their argument lists are in stack order; the interrupt driver's functions are C convention, their arguments removed by the caller ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

Three object files, none opened by a `ConfigPart` run; each ends in zero fill after its last return ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| Code | Holds | Ends |
|---|---|---|
| `0006c6f8`–`0006c7eb` | the timer object, `DVTimer` | `DVTimer_SetRate`'s `RET 8` at `0006c7e8` and one zero byte |
| `0006c7ec`–`0006d18b` | Symantec's `scanf.c` ([the runtime](../formats/compiler.md#identification)) | a `RET 4` at `0006d188` and one zero byte |
| `0006d18c`–`0006d4b3` | the interrupt driver: the counter's divisor, the handler and its callback slots | two `RET`s at `0006d4af` and `0006d4b0` after the handler, and three zero bytes |

The file before ends with a `RET 4` at `0006c6f3` and two zero bytes.

## The rate

`Sim_StartMission` builds the simulator's timer at its step 2 ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)): `Sim_Timer` (`000b5baa`) = `DVTimer_Ctor`(0, `0x26d3`). The PC's interval timer counts down from the divisor at 1,193,182 Hz and interrupts at each pass through zero, so the divisor 9,939 makes 120.05 interrupts a second, 8.33 ms apart.

The code that reads the rate takes the divisor *w* from the timer in GL slot 0 (`000bdd0c`, the word `+4` of the object the slot holds) and computes the interrupts a second as `1000000 / Math_Q14Multiply(w, 0x35a1)`: `0x35a1 / 0x4000` is 0.838 µs, one count of the 1.193 MHz input, so the product is the period in microseconds. For 9,939 the rounding multiply gives 8,328 µs and the quotient 120. So:

| Reader | Value |
|---|---|
| `SimClock_TicksPerSecond` (`000bba44`) and `000bba40` ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)) | 120 |
| `SimClock_TicksPer100ms` (`000bba42`), `000bdbd4`, `000bdbd6` and `000bdbd8` | 12 |
| `FrameRate_TicksPerSecond` (`000b5e2c`, [the frame-rate meter](debug-monitor.md#the-frame-rate-meter)) | 120 |
| `SoundManager_LoadDrivers`' count for the sound library's dispatcher, 120 / 120 ([the library's timer](../sound/song-player.md#the-librarys-timer)) | 1: the sound library's timed work, the songs' ticks among it, runs on every interrupt |
| `HmiTimer_Install`'s count, 120 / 60 ([the digital driver's timer](../formats/sound-samples.md#the-digital-drivers-timer)) | 2, in a path the driver load does not take |

The first timer built claims GL slot 0 and is the one installed; a later one finds the slot taken and touches nothing. `es2_xref.py` finds three calls of `DVTimer_Ctor`: `Sim_StartMission`'s; `Ttm_AllocHandles` (`00069780`), which builds one with `0x4da6` only when slot 0 is empty, and for which `es2_xref.py` finds one chain of calls, `Cockpit_LoadPanel` from `CockpitViewManager_LoadViews` from `CockpitViewManager_Ctor`, one call each, which `Sim_StartMission` runs at its step 13 ([the simulator's interpreter](../formats/ttm-scripts.md#the-simulator)), by which point the slot is taken; and `maybe_Kind0InputDevice_Ctor` (`0004b440`), to which it finds no reference ([Open](#open)). A scan of `ES1DBSIM`'s image bytes for `OUT` to ports `0x40` and `0x43` and for loads of those port numbers into `DX` finds `Pit_SetDivisor`'s three writes ([Open](#open)).

In `GO.EXE`, `Shell_InitDevices` (`ES1GO 00045d24`) builds the first timer, `Kind0InputDevice_Ctor` (`ES1GO 00025a31`, the code of `maybe_Kind0InputDevice_Ctor`) with `0x26d3`, so the shell runs at the simulator's 120.05 interrupts a second ([the shell's devices](../shell/startup.md#the-devices)). `Ttm_AllocHandles` (`ES1GO 0004670e`) builds one with `0x4da6`, 19,878, 60.02 interrupts a second, only when slot 0 is empty. The two calls of `DVTimer_SetRate` `es2_xref.py` finds, from the shell's assert hook (`ES1GO 00045a68`) and its shutdown (`ES1GO 00046094`), set `0x4da6` just before the timer is removed ([the shutdown](../shell/startup.md#the-shutdown)).

## The timer object — `DVTimer`

A 6-byte object: a dword of flags at `+0`, bit 0 set while its handler is installed, and the divisor as a word at `+4`. ES2's class of the same name and size (`DVTimer`, DBSIM's class record `004621e6`) has the same four functions, and its `DVTimer_Init` stores the divisor with no call to the hardware.

- **`DVTimer_Ctor` (`0006c6f8`)**, (this, divisor): allocates 6 bytes for a null *this*, returning 0 when that fails; `GL_ClaimGlobal` (`0007d5bc`) takes GL slot 0 for it when the slot is empty and clears `+0`; when the slot holds it, `DVTimer_Init`, and when that answers non-zero, `atexit` (`0007d628`) of `Pit_RemoveHandler` and `Assert_AddHook` (`0005730c`) of `DVTimer_AssertRemoveHook` (`0006c750`, pushed as its padding entry `0006c74d`), which calls `Pit_RemoveHandler`, so the timer is put back when the program exits or an assert aborts it. Returns *this*. ES2's is `004809e4`.
- **`DVTimer_Init` (`0006c790`)**, (this, divisor): `Pit_InstallHandler`(divisor, this) and, when that answers non-zero, the divisor to `+4`; returns bit 0 of `+0`.
- **`DVTimer_SetRate` (`0006c7bc`)**, (this, divisor): `Pit_SetDivisor`(divisor), and the divisor to `+4` with 1 returned when that answers; 0 otherwise ([Open](#open)).
- **`DVTimer_Dtor` (`0006c758`)**, (this, flags): for a non-null *this*, when GL slot 0 holds a timer, `Pit_RemoveHandler` and the slot cleared, whichever timer the slot holds; `Mem_Delete` when bit 0 of *flags* is set. `Sim_ReleaseSessionObjects` (`0005b534`) deletes `Sim_Timer` with it ([small functions](main-loop.md#small-functions)).

## The interrupt — `Pit_TimerInterrupt` (`0006d391`)

**`Pit_InstallHandler` (`0006d1c2`)**, (divisor, timer), refuses with 0 and the timer's `+0` cleared when `Pit_Timer` (`0009bd40`) already holds one. Otherwise it clears both callback masks, keeps the data selector at `0009bd54` and `0009bd58`, saves interrupt 8's protected-mode handler (`INT 21h` `AX=2502h`, at `0009bd48` and `0009bd4c`) and real-mode handler (`AX=2503h`, at `0009bd4e`), sets `Pit_Timer` to the timer, programs the divisor with `Pit_SetDivisor`, installs `Pit_TimerInterrupt` for interrupt 8 with `AX=2506h`, the extender's call for a handler that takes the interrupt in protected mode whichever mode the processor is in, and sets bit 0 of the timer's `+0`. Interrupt 8 is the interval timer's, IRQ 0.

**`Pit_SetDivisor` (`0006d18c`)**, (divisor), answers 0 while `Pit_Timer` is 0; otherwise it zeroes `Pit_ChainAccumulator` (`0009bd46`), keeps the divisor in `Pit_Divisor` (`0009bd44`), and with interrupts off writes `0x34` to port `0x43` (counter 0, low byte then high byte, mode 2, the rate generator) and the divisor's low and high bytes to port `0x40`, and answers 1. A divisor of 0 counts 65,536, the BIOS's 18.2 interrupts a second.

**`Pit_RemoveHandler` (`0006d247`)**, while `Pit_Timer` is set, puts the BIOS rate back with `Pit_SetDivisor`(0), restores both saved handlers of interrupt 8 at once (`AX=2507h`), clears bit 0 of the timer's `+0` and `Pit_Timer`, enables interrupts and answers 1.

**`Pit_TimerInterrupt` (`0006d391`)** runs at each interrupt:

1. It saves every register, loads the data selector, and sends the interrupt controller its end of interrupt (`0x20` to port `0x20`) at once.
2. While `Pit_InHandler` (`0009bd52`) is set, a previous interrupt's callbacks are still running: it restores the registers and returns, running no callback and counting nothing.
3. Otherwise it sets `Pit_InHandler`, enables interrupts, and moves to its own stack, whose top is `0009cd5c` (selector `0009bd58`). It takes 1 off `Pit_Countdown` (`0009cd5c`) while that is above 0 and adds 1 to `Pit_InterruptCount` (`0009cd60`).
4. It runs the periodic callbacks, then the delayed ones ([callbacks](#callbacks)).
5. With interrupts off it clears `Pit_InHandler` and adds `Pit_Divisor` to the 16-bit `Pit_ChainAccumulator`. When the sum carries past 65,535 it restores the registers and jumps to the saved protected-mode handler, so the handler that was there before still runs about 18.2 times a second, once every 6.59 interrupts at the simulator's divisor; otherwise it returns from the interrupt.

A callback runs with interrupts enabled, inside the interrupt, on the handler's stack. One that takes longer than an interrupt period costs every slot the interrupts that arrive meanwhile: their counters are not decremented.

`GO.EXE`'s handler, `Pit_TimerInterrupt` (`ES1GO 00056611`), runs the same slots on its own stack (top `ES1GO 000882a8`) with interrupts left off, keeps no `Pit_InHandler` or interrupt count, and sends the end of interrupt only when it does not pass the interrupt on, after the callbacks.

## Callbacks

Two sets of 16 slots, each a bit of a mask and an entry in each of the set's tables.

**Periodic**, `Timer_CallbackMask` (`0009cd64`): `Timer_AddCallback` (`0006d289`), (count, callback), takes the lowest free slot, stores the count as both the slot's period (`0009ce6c`) and its counter (`0009cdec`) and the callback at `0009cd6c`, and answers the slot + 1; 0 when no timer is installed or all 16 are taken. Each interrupt takes 1 off each taken slot's counter, lowest slot first, and at 0 calls the callback and reloads the counter from the period: a count of *n* runs the callback on every *n*th interrupt, so `Time_InitSimClock`'s and the frame meter's count of 1 runs theirs 120 times a second. **`Timer_RemoveCallback` (`0006d337`)**, (slot), frees slot − 1 for a slot of 1 to 16 and answers 1 when it was taken, 0 otherwise.

**Delayed**, `Timer_DelayedMask` (`0009cd68`): **`Timer_AddDelayedCallback` (`0006d2d7`)**, (delay, repeats, period, callback), takes the lowest free slot of the second set and stores the delay (`0009ceec`), the repeats (`0009cf2c`), the period as period (`0009ceac`) and counter (`0009ce2c`) and the callback (`0009cdac`), answering the slot + 1 or 0. Each interrupt takes 1 off a taken slot's delay while it is not 0, and otherwise off its counter; at 0 the callback runs and 1 comes off the repeats, the slot being freed when they reach 0 and the counter reloaded from the period when not. So the callback first runs after delay + period interrupts and then every period, *repeats* times in all; a repeat count of 0 does not stop it. **`Timer_RemoveDelayedCallback` (`0006d364`)** frees a slot of this set as `Timer_RemoveCallback` does ([Open](#open)).

`Pit_InstallHandler` clears both masks; `Pit_RemoveHandler` leaves them, and `Timer_AddCallback` refuses while no timer is installed.

## Functions

| ES1 | Address | `ES1GO` | ES2 |
|---|---|---|---|
| `DVTimer_Ctor`, `DVTimer_AssertRemoveHook`, `DVTimer_Dtor`, `DVTimer_Init`, `DVTimer_SetRate` | `0006c6f8`, `0006c750`, `0006c758`, `0006c790`, `0006c7bc` | `0005d6b4`, `0005d70c`, `0005d714`, `0005d74c`, `0005d778` | `004809e4`, —, `00480a2c`, `00480a6c`, `00480a84` |
| `Pit_SetDivisor`, `Pit_InstallHandler`, `Pit_RemoveHandler` | `0006d18c`, `0006d1c2`, `0006d247` | `0005640c`, `00056442`, `000564c7` | — |
| `Timer_AddCallback`, `Timer_AddDelayedCallback`, `Timer_RemoveCallback`, `Timer_RemoveDelayedCallback` | `0006d289`, `0006d2d7`, `0006d337`, `0006d364` | `00056509`, `00056557`, `000565b7`, `000565e4` | — |
| `Pit_TimerInterrupt` | `0006d391` | `00056611` | — |
| `maybe_Kind0InputDevice_Ctor` | `0004b440` | `Kind0InputDevice_Ctor` (`00025a31`) | `Kind0InputDevice_Ctor` (`0045bfcc`) |

`maybe_Kind0InputDevice_Ctor`, (this, divisor), allocates 11 bytes for a null *this*, builds a `DVTimer` at `+0` with the divisor, and passes `+6` to `0004b0b4`, where ES2's registers its `+6` as input device kind 0. `DVTimer_AssertRemoveHook` and `Pit_TimerInterrupt` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `Sim_Timer`, the simulator's timer | `000b5baa` |
| `Pit_Timer`, the installed timer | `0009bd40` |
| `Pit_Divisor`, `Pit_ChainAccumulator`, `uint16` | `0009bd44`, `0009bd46` |
| interrupt 8's saved protected-mode handler (offset, selector) and real-mode handler | `0009bd48`, `0009bd4c`, `0009bd4e` |
| `Pit_InHandler`, `int16` | `0009bd52` |
| the data and stack selectors | `0009bd54`, `0009bd58` |
| `Pit_Countdown`, the dword above the handler's stack, and `Pit_InterruptCount` | `0009cd5c`, `0009cd60` |
| `Timer_CallbackMask`: callbacks, counters, periods | `0009cd64`: `0009cd6c`, `0009cdec`, `0009ce6c` |
| `Timer_DelayedMask`: callbacks, counters, periods, delays, repeats | `0009cd68`: `0009cdac`, `0009ce2c`, `0009ceac`, `0009ceec`, `0009cf2c` |

## Open

- **Open:** what reaches `maybe_Kind0InputDevice_Ctor` (`0004b440`), to which `es2_xref.py` finds no branch or stored pointer, and what `0004b0b4` does with its `+6`. Built before `Sim_StartMission`'s step 2 with another divisor, it would take GL slot 0 and set the rate.
- **Open:** a retail check of the rate under DOSBox-X: a breakpoint on `Pit_SetDivisor` (`0006d18c`) after `Sim_StartMission` begins stops once with `9939` (`0x26d3`) on the stack, and on `Time_InitSimClock` (`00063e64`), stepping past its division, `SimClock_TicksPerSecond` (`000bba44`) holds 120. The HMI drivers loaded at run time lie outside the image's bytes the port scan covered.
- **Open:** no reader of `Pit_Countdown` or `Pit_InterruptCount` found by a scan of the image's bytes for their addresses, which finds the handler's own uses; no reference to the two `RET`s at `0006d4af` and `0006d4b0`, and no caller of `DVTimer_SetRate`, `Timer_AddDelayedCallback` or `Timer_RemoveDelayedCallback` in `ES1DBSIM`, found by `es2_xref.py`; no write to the timer's ports besides `Pit_SetDivisor`'s found by the port scan.
- **Open:** why `GO.EXE`'s two `DVTimer_SetRate` calls (`ES1GO 00045a68`, `Shell_ShutdownDevices` (`00046094`)) set `0x4da6` just before `Pit_RemoveHandler` puts the BIOS rate back.
