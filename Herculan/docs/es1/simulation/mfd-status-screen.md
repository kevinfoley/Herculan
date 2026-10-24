# DBSIM.EXE — the MFD's status screen

**ES1's status screen is ES2's ([`MFDStatus`](../../retail/simulation/mfd.md#mfdstatus--modes-0-and-4)): a picture of the subject, a HERC's damage diagram tinted part by part or a structure's or flyer's silhouette, and beside it the subject's name and condition.** What a player would see differs: the captions are centered in a narrow column, the fixed ones in red; the player's own HERC shows no name line; a hostile shows no readout under its condition, where ES2's shows its range; every flyer is named `LANDSKIMMER`; a silhouette sits at the bottom left rather than centered; and the readouts have no scrambled state, a destroyed subject is not held on screen, and the Targeting Pod's component is not marked. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00032ed8`–`0003373f` and the initialized data `0009358e`–`00093661`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `00032ed8`, `00032f00`, `00032f3f` (frameless, with no Ghidra function) and `00032f48`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `0009358e` and `000935c2`. The file has no static initializer: `MfdStatusScreen_SetCondition`'s `RET 0x10` at `0003373c` is followed by one zero byte and the next file's run at `00033740`, [the gunsight's](cockpit-gunsight-hud.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). No assert in the file names a source path, and the data holds none.

The data holds, after the run's tables, the two bank pointers, `MFDStatusVtable` (`000935fe`), the names `bases` and `flyers`, the jump table of the paint's region switch (`0009361c`, ids 0 to 14) and the condition readout's `"[ "` (`00093658`) and `"% ]"` (`0009365b`).

## The screen object

**`MfdStatusScreen_Ctor` (`00032f74`)**, (screen, rect), allocates `0x20` bytes when given none, the base `MFDisplay_Ctor` (`00039c50`) storing the rect at `+0`:

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect, a pointer | `+0x04` |
| `+0x04` | the display's block the subject is read from | `+0x08` |
| `+0x08` | the vptr, `MFDStatusVtable` | `+0x00` |
| `+0x0c`–`+0x1c` | labels 0 to 4 | `+0x10`–`+0x20` |

ES2's object (`0x42` bytes) also holds the press scramble's counter, flag and deadline, a viewport rect and the latched subject with its hold ([the subject](../../retail/simulation/mfd.md#the-subject)); ES1's holds none of them. `es2_xref.py` finds the constructor called from `0003a0df` and `0003a15c` in `MfdDisplay_Ctor` (`00039c64`), which builds the six screens in mode order and stores them at `+0x96` to `+0xaa`, as ES2's display does: this one for modes 0 and 4, [FLASH COMM](mfd-flash-comm.md), [the NAV MAP](mfd-nav-map.md), [the scanner](mfd-scanner.md) and [the missile camera](mfd-missile-camera.md). It gives each the display's `+0xb4` as its rect and points its `+4` at the display's `+0x7a` ([building the display](mfd-display.md#building-it)).

The constructor loads `bases` and `flyers` once, from the folder `000980f8` names, into `MfdStatus_BasesBank` (`000935f4`) and `MfdStatus_FlyersBank` (`000935f8`); ES2's loads `vehicles` too. It builds the five labels in `ColorSchemePanels[2]` (`cpred`) with no plate, each centered (`Label_SetRect` flags 2, zero margins) in a rect from the screen rect's *x0* + 30 to *x0* + 60 across and 10 tall, at *y0* + 0, 8, 21, 28 and 35. ES2's are left-aligned in white and red on plates.

The update, **`MfdStatusScreen_Update` (`00033690`)**, slot `+8`, runs the paint (slot `+4`) and copies the rect to the display with `Widget_DrawToCockpit`(0, rect, 1). ES2's runs the scramble's timer first. The display calls it every 30 clock counts, half a second, while the screen is up ([drawing the display](mfd-display.md#drawing-it)).

## The paint

**`MfdStatusScreen_Paint` (`000330b4`)** fills the whole rect in palette index 63 (the brush {0, `0x3f`} at the canvas context's `+0x22c`), where ES2's fills only the viewport, in `0x11`. The subject is the dword at `+8` of the block `+4` points to, the display's `+0x82`, read afresh each paint; ES2's latches it and holds a destroyed one for 300 coarse ticks. The display stores there the cockpit's machine for mode 0 (F1) and the cockpit's target selection for mode 4 (F5), when the mode is chosen and again at each refresh ([the subject](mfd-display.md#the-subject)), so F1 shows the player's own HERC and F5 the current target or `NONE`. Label 0 reads `TARGET:` (`STRINGS0.STR` group 17) whenever it is written; ES1 has no `ID:` caption ([the groups](../formats/str-strings.md#the-groups)).

| Subject | Picture | Label 1 | Condition |
|---|---|---|---|
| none | — | `NONE` (group 22) in `cpblue` | — |
| class 0, a HERC | the paper doll's view 2 ([below](#a-herc)) | the chassis name, group 19 indexed by the type record's `int16` `+0x6c`, in `cpgreen` for a friend and `cpred` for a foe; nothing for the player's own machine | scanned ([below](#a-herc)) |
| class 1, a structure | frame *n* of `bases`, *n* the type record's `int16` `+0x00`, its index in [`BASES.DAT`](../formats/bases-dat.md#the-type-record) | group 20 entry *n* | from the damage |
| class 2, a flyer | frame 0 of `flyers` | `LANDSKIMMER`, group 21 entry 0 | from the damage |
| any other class | — | `UNKNOWN` (group 23) in `cpblue` | — |

The class is the object's `int16` `+0x162` (ES2's `+0x1a8`), and the type record its `+0x188`. A silhouette's top-left is the rect's *x0* + 2 and *y1* − its height − 5, so its bottom row is 5 above the rect's; ES2's centers it in the viewport. Label 1 keeps its last font for a structure or flyer, as in ES2. With a subject, label 2 reads `STATUS:` (group 18) for a HERC, and `TARGET:` goes into label 0 for every class but the player's own HERC.

### A HERC

The paint blits the bitmap of view 2 of the chassis' [paper doll](paper-doll.md#the-records) (the record at the type record's `+0xc2`, plus `0x38`) with `Bitmap_Blit` at the doll's blit point, the view's origin plus the rect's top-left; ES2's adds `(0x11, 2)` within its viewport. The player's own machine is `CockpitViewInstance`'s `+0x1ff`; a squadmate is named by its chassis like any other HERC, where ES2's names it by its pilot. Friend or foe is the side byte, `+0x14` of the mission group at the object's `+0x3f` (ES2's `+0x12` of `+0x45`): 0 is a friend.

It reads the damage readings, `int16`s, with `Mech_ReadDamageReadouts` and tints each region of the view (count `+0x14`, `0x1c`-byte regions from `+0x18`) by the mean of the readings its id `+0x00` selects, through the jump table `0009361c`: the eight ids and readings of the walker column of ES2's table ([region tints](../../retail/simulation/mfd.md#region-tints)), unchanged, the mean a signed division that truncates. ES1's has no flyer-chassis column. Every other id, 1 to 3, 9 to 12 or above 14, divides by a count of 0, as an id ES2's switch does not name does. Each region then gets `Damage_PickRegionTint`(out, region, the mean) and `PaperDoll_RecolorRect`(the region's `recolorMode` `+0x18`, the tint, its `colorId` `+0x14`, its rect `+0x04` moved by the doll's blit point) ([ES1's tinting](paper-doll.md#tinting)).

The condition is ES2's scan on ES1's offsets: destroyed (4) when the byte `+0x93` is set (ES2's `+0x99`); else critical (3) when the least of readings 20–31 is above `0x80`, internal damage (2) when the greatest is above 0, shields down (1) when `+0xaa` is set (ES2's `+0xb0`), and OK (0) otherwise. The scan for the least starts from `0x100` and the scan for the greatest from 0, signed compares of each `int16`.

### The condition line

**`MfdStatusScreen_SetCondition` (`000336b0`)**, (screen, state, friendly, damage), is ES2's ([`MFDStatus`](../../retail/simulation/mfd.md#mfdstatus--modes-0-and-4)) without the scramble: a state of −1 becomes `Damage_ToConditionState`(damage) ([ES1's bands](paper-doll.md#tinting)), label 3 reads group 24 entry *state* (`OK`, `SHIELDS DN`, `INT DAMAGE`, `CRITICAL`, `DESTROYED`), and for a friend label 4 reads `[ `, (256 − damage) × 100 `>> 8` in base 10 (`ltoa`), and `% ]`. The shift is arithmetic, on a 32-bit product, so it rounds down: it differs from a division that truncates only where (256 − damage) × 100 is negative and not a multiple of 256, which is for a damage above 256 that is not a multiple of 64 (257 reads −1%, where truncation gives 0%). A negative damage makes the product positive, where the two agree, and reads 100% or more. Whether the damage can leave 0 to 256 is [Open](#open). The paint passes the scanned state for a HERC, −1 for a structure or flyer, the side byte's test as friendly, and the damage the object's vtable `+0x44` returns, ES2's `+0x40` one slot further on ([vtables](../formats/compiler.md#vtables)). For a foe label 4 is left as the fill painted it.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorStatusScreenCopy`, `ConfigPart_DtorStatusScreenCopy`, `ConfigPart_GetClassTagStatusScreenCopy`, `TSPartBase_DtorStatusScreenCopy` | `00032ed8`, `00032f00`, `00032f3f`, `00032f48` | the run; slots `+0x1c` and `+4` of `0009358e` and `+0x1c` of `000935c2` (`00032f48` by its padding `00032f45`) |
| `MfdStatusScreen_Ctor`, `MfdStatusScreen_Paint`, `MfdStatusScreen_Update`, `MfdStatusScreen_SetCondition` | `00032f74`, `000330b4`, `00033690`, `000336b0` | `0043a2e0`, `0043a5a0`, `0043b210`, `0043b260` |
| `MFDisplay_Ctor` | `00039c50` | `004451f0`, which also stores a vtable and clears `+0xc` |

## Globals

| | Address |
|---|---|
| `MfdStatus_BasesBank`, `MfdStatus_FlyersBank` | `000935f4`, `000935f8` |
| `MFDStatusVtable`: a zero dword, then the paint and the update | `000935fe` |

## Open

- **Open:** whether an object of ES1 carries a class other than 0 to 2 at `+0x162`, which would show `UNKNOWN`.
- **Open:** whether the damage the paint passes, the `int16` of slot `+0x44`, can leave 0 to 256, the range the condition line's percentage and `Damage_ToConditionState` expect: the HERC's slot, `Mech_GetOverallDamage` (`00010eb0`), returns what `0000a07c` makes of the damage header, unread here, and the structure's and the flyer's slots are unread.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with the status screen up and a hostile HERC as its subject, `BP <CS>:000336b0` fires on each repaint with the word at `[ESP+0xc]` (friendly) 0, and the screen shows no `[ n% ]` line under the condition.
