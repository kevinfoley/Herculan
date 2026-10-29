# DBSIM.EXE — the Heads-Down Display's damage screen

**ES1's damage screen, the Heads-Down Display's page 1 (`F8`), is ES2's ([damage detail](../../retail/simulation/heads-down-display.md#damage-detail--page-1)): the subject machine's paper doll, each region tinted by its damage, with the machine's weapon icons over it, beside a column of rows that each name a component, an internal system or a weapon and give its integrity in percent, in three views, structural (`S`), internal (`I`) and weapons (`W`), for the player, a squadmate or the target ([subject](../../retail/simulation/heads-down-display.md#subject)).** What a player would see differently:

- twelve rows a view where ES2's has thirteen, and with the row offset at 0 a view labels its first twelve ([the rows](#the-rows); [Open](#open));
- one list of names a view, with no flyer names ([views](#views));
- the structural doll tints its regions with index 2 and 3 as one, on the mean of their two readings, as both games do 0 and 1 ([the rows](#the-rows));
- with no subject, the message is centered on the screen, where ES2's sits right of center ([building it](#building-it--hdddamagescreen_ctor-00042164)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The screen is the display's `+0xce` ([the display object](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)), and the display's presses and keys reach it as [the display's doc](heads-down-display.md#presses) lists. Colors are palette indices.

## Where the object file starts and ends

The code is `000420c8`–`0004332f`, after [the command screen's](hdd-command-screen.md#where-the-object-file-starts-and-ends), and the tables and data are `00094a32`–`00094af1`, before the next file's copy of `ConfigPart`'s table at `00094af2`. It opens with the run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) right after `HddCommandScreen_StaticInit`'s `RET` at `000420c7`: `HeapPtr_DtorHddDamageCopy` (`000420c8`), `ConfigPart_DtorHddDamageCopy` (`000420f0`), `ConfigPart_GetClassTagHddDamageCopy` (`0004212f`) and `TSPartBase_DtorHddDamageCopy` (`00042138`), with the tables `00094a32` and `00094a66`. The file has no static initializer: the static constructor table's entry after the command screen's `000adc4c` is `000adc50`, `CockpitMouse_StaticInitNoOp`. Its last function, `HddDamageScreen_BlitWeaponIcons`, ends with a `RET 0xc` at `0004332c`, followed by one zero byte and the run at `00043330` that opens the next file, the one holding `CockpitClock_Ctor` (`000433cc`) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

The functions follow ES2's in order, from `HddDamageScreen_Ctor` (ES2's `0045079c`) to `HddDamageScreen_BlitWeaponIcons` (ES2's `00451db8`), with the hide and key slots' stubs right after the constructor, where ES2's stand after `HddDamageScreen_BlitWeaponIcons`.

The data holds, after the run's tables, `HddDamageColorIds` (`00094a98`), four dwords 0x3f, 0x35, 0x3b and 0x38 in the place of ES2's color ids 19, 9, 15 and 12 ([Open](#open)); `HDDDamageVtable` (`00094aaa`); the string `100` (`00094ac6`); and the update's two jump tables, `00094acc` over a structural region's index 0–3 and `00094adc` over the damage state 0–4.

## The screen object

**The object is ES2's on its own offsets, with the page base's fields reordered as the command screen's are ([the screen object](hdd-command-screen.md#the-screen-object)), twelve rows where ES2's has thirteen and the view's name list kept in the object; it is `0xec` bytes, ES2's `0xf4`.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00`, `+0x04` | `+0x04`, `+0x08` | the page base's: the screen rect (a pointer) and the display |
| `+0x08` | `+0x0c` | the paint stage the display hands its pages ([the paint stages](heads-down-display.md#drawing-it)) |
| `+0x0c` | `+0x00` | the vptr, `HDDDamageVtable` (`00094aaa`) |
| `+0x10` | `+0x10` | twelve name labels |
| `+0x40` | `+0x44` | twelve value labels |
| `+0x70` | `+0x78` | the subject's caption label |
| `+0x74`, `+0x78` | `+0x7c`, `+0x80` | the view, 0–2, and the one the last update drew, −1 to force a full redraw |
| `+0x7c`, `+0x80` | `+0x84`, `+0x88` | the row offset and the one the last update drew |
| `+0x84` | none | the view's name list ([Open](#open)) |
| `+0x88` | `+0x90` | the row count |
| `+0x8c` | `+0x94` | the readouts the last update drew, 42 `int16` |
| `+0xe0`, `+0xe4` | `+0xe8`, `+0xec` | the subject the last update drew, and the subject |
| `+0xe8` | `+0xf0` | the no-subject label |

### Building it — `HddDamageScreen_Ctor` (`00042164`)

(screen, display, damage column, screen rect), from `HddDisplay_Ctor` with 0 for the screen:

1. allocates `0xec` bytes when given none, returning 0 when that fails; `HDDisplay_Ctor`(screen, display, screen rect), so `+0` holds the screen rect and `+4` the display ([the pages' base](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)); `HDDDamageVtable` at `+0xc`;
2. **the rows**, *c* the cell height of `cpgreen` (`ColorSchemePanels`[1], `00092d44`), its `int16` `+10`, and *w* `HudFont_MeasureString`(`cpgreen`, `100`): for row *i* from 0 to 11, from the column's *y0* + *i* × *c* to that + 4, the name label `Label_Ctor`(0, `cpgreen`) at `+0x10` + 4*i* over the column's *x0* + 12 to *x1* − *w*, and the value label at `+0x40` + 4*i* over *x1* − *w* to *x1*, each placed by `Label_SetRect` with flags 1, left-aligned, and margin (0, −1) ([labels](cockpit-widgets.md#labels)), its fill `+0x1c` = 0x3f; ES2's builds 13 rows, its names `0x1e` ≪ `VideoMode_XCoordShift` in, 4 ≪ `VideoMode_YCoordShift` tall at a pitch of 7 ≪ `VideoMode_YCoordShift`;
3. **the caption** `+0x70`: `Label_Ctor`(0, `cpgreen`) over the screen rect's *x0* + `0x14` to *x0* + `0x3c` and from its *y1* − 2 − *c* to *y1* − 2, flags 2, centered, and margin (0, −1), with no fill of its own until `HddDamageScreen_SetSubjectCaption` sets one; ES2's is `0x28` ≪ `VideoMode_XCoordShift` wide from *x0* + `0x1c` ≪ `VideoMode_XCoordShift`;
4. `HddDamageScreen_SetView`(screen, 0) and the view drawn `+0x78` = −1;
5. the subject `+0xe4` and the one drawn `+0xe0` = the display's subject `+0x4e1`, which `HddDisplay_Ctor` sets only after building the pages ([the display object](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258), step 12), so whatever the display's memory held there; the saved readouts' entries 1, 20 and 32 (the `int16` at `+0x8e`, `+0xb4` and `+0xcc`), the first of each view's, = 1;
6. **the no-subject label** `+0xe8`: `Label_Ctor`(0, `cpred`, `00092d48`) over 40 pixels either side of the screen rect's middle *x* and from its middle *y* to that + 10, flags 2 and margin (0, −1), its fill 0x3b. ES2's takes its left edge 40 pixels left of the middle unshifted and its width 80 ≪ `VideoMode_XCoordShift`, so it sits right of center.

ES2's (`0045079c`) also resolves its four color ids through its color table; ES1's has no counterpart of that loop.

`HDDDamageVtable` (`00094aaa`) holds, from `+4`, the slots the display calls through a page's `+0xc` ([the pages' base](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)): the repaint `HddDamageScreen_Repaint` and the update `HddDamageScreen_Tick`, each by its padding; the hide `HddDamageScreen_HideNoOp` (`00042399`, three `NOP`s and `RET 4`); the show `HddDamageScreen_Show`; and the key slot `HddDamageScreen_KeyDispatch` (`0004239f`, `NOP`, `XOR EAX,EAX` and `RET 8`, the code of `HDDisplay_KeyDispatchNone`), which returns 0. ES2's table, `0049dab0`, holds the base's `HDDisplay_HideNoOp` (`00451e86`) in the hide slot.

## Views

**`HddDamageScreen_SetView` (`000423d8`)**, (screen, view), sets the view `+0x74`, the row offset `+0x7c` to 0 and the offset drawn `+0x80` to −1, then by the view the name list `+0x84` and the row count `+0x88`:

| View | Key | Names | Rows | ES2's names |
|---|---|---|---|---|
| 0, structural | `S` | [`STRINGS0` group 12](../formats/str-strings.md#the-groups) (`000bd364`), 19 components | `0x13` | group 13, or 14 for a flyer |
| 1, internal | `I` | group 13 (`000bd3b0`), 12 internal systems | `0xc` | group 15, or 16 for a flyer |
| 2, weapons | `W` | `HddDamage_WeaponNames` (`000b160c`) | the `int16` count `+4` of the subject's weapon manager `+0x198` ([the manager](weapon-mounts.md)), 0 with no subject | the mounts' names |

For another view it sets neither. ES2's (`00450b60`) sets no name list, which its update picks, by the subject type's flyer flag.

**`HddDamageScreen_NextView` (`0004246c`)** and **`HddDamageScreen_PrevView` (`00042494`)**, (screen): `HddDamageScreen_SetView` with the view one on, 2 wrapping to 0, or one back, 0 wrapping to 2. ES2's (`00450bcc`, `00450bf0`). `es2_xref.py` finds them called from `HddDisplay_HandleWidgetPress` alone, for buttons 2 and 9 and for button 3 ([presses](heads-down-display.md#presses)).

**`HddDamageScreen_PageDown` (`000424bc`)** and **`HddDamageScreen_PageUp` (`000424dc`)**, (screen): the row offset `+0x7c` plus 12 while that stays below the row count `+0x88`, or less 12 while that stays at or above 0. ES2's (`00450c18`, `00450c38`) step 13. `es2_xref.py` finds no reference to either ([Open](#open)).

**`HddDamageScreen_Show` (`00043294`)**, (screen), the show slot: `HddDamageScreen_SetView`(screen, `+0x74`) and the view drawn `+0x78` = −1. ES2's (`00451d94`).

**`HddDamageScreen_Repaint` (`000423a8`)**, (screen), the repaint slot: `+0x78` = −1 and `HddDamageScreen_Update`(screen, 0). **`HddDamageScreen_Tick` (`000423c4`)**, (screen), the update slot: `HddDamageScreen_Update`(screen, 1); neither reads the stage the display stores in `+8`. ES2's (`00450b2c`, `00450b4c`).

## Drawing it — `HddDamageScreen_Update` (`000424f8`)

(screen, copy), where copy says whether each rect it redraws is copied to the other page with `Widget_DrawToCockpit`(0, rect, 1) ([drawing it](cockpit-panel.md#drawing-it)): 0 from the repaint, 1 from the update slot. It is ES2's ([damage detail](../../retail/simulation/heads-down-display.md#damage-detail--page-1)) on ES1's offsets and palette indices:

1. **Dark.** While the display's `+0x74` is set, inside `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`, it fills the screen rect with the brush {0, 0x1f}, copies it when copy is set, and returns.
2. **What changed.** The subject `+0xe4` = the display's `+0x4e1` ([subject stepping](heads-down-display.md#subject-stepping)). It tests all three first: a new subject (against `+0xe0`), a new view (against `+0x78`) and a new offset (against `+0x80`). Then a new subject runs `HddDamageScreen_SetView`(screen, `+0x74`) and sets `+0xe0`; a new view sets `+0x78`; a new offset sets `+0x80`. A change of any of the three makes this a full redraw. A new subject with the offset unchanged leaves `+0x80` at `HddDamageScreen_SetView`'s −1, so the next update finds the offset changed and redraws in full again.
3. **No subject.** With `+0xe4` null it returns unless the subject or the view changed, and puts the offset `+0x7c` to `+0x80`: −1 after a new subject with the offset unchanged, until the next `HddDamageScreen_SetView`.
4. **The view's data.** With a subject, by the view:

   | View | Names | Readouts | Compared | Doll |
   |---|---|---|---|---|
   | 0 | group 12 | entries 1–19 | 19 | `.PDG` view 0 |
   | 1 | group 13 | entries 20–31 | 12 | `.PDG` view 1 |
   | 2 | `HddDamage_WeaponNames` | entries from 32 | the row count `+0x88` | `.PDG` view 0 |

   For view 2 it first rebuilds the names: for each mount *i* below the row count `+0x88` as `HddDamageScreen_SetView` left it, the manager's count, read once before `+0x88` is zeroed, the mount entry *i* of the manager's array (`+0` of `+0x198`): a non-null mount's `WeaponMount_GetDisplayName` is copied whole, its NUL included, into the next 8-byte slot of `HddDamage_WeaponNameBuffer` (`000b1634`), the slot of its rank among the non-null mounts, and `HddDamage_WeaponNames`[*i*] pointed at it; the count `+0x88` is rebuilt as *i* + 1 for every mount, null or not. An empty hardpoint's entry keeps the pointer a previous update stored there, as in ES2. Nothing bounds the copy: a name of 8 characters or more runs into the next slot, which the next mount's name then overwrites. Nor is *i* held to the ten pointers: an eleventh mount's pointer is written over the buffer's first slot.

   It reads the readouts with `Mech_ReadDamageReadouts`(subject, buffer), 42 `int16` ([the readouts](heads-down-display.md#the-three-paints), `HddGauge_ConditionIndex`); on a frame that is not a full redraw, it returns when the compared entries equal the saved ones `+0x8c`. The doll is the view's record in the `.PDG` record at the subject's type record (`+0x188`) `+0xc2` ([the records](paper-doll.md#the-records)), its origin the view's point `+4` plus the screen rect's top-left; the icons are the subject's list `+0x194` ([the weapon icons](paper-doll.md#the-weapon-icons)). The rows shown end at the row count or the offset + 12, whichever is less, and for view 2 at the icon count when that is less.
5. **The page.** Inside the canvas push and pop, with the brush mode 0, it makes the display's (`000bdd28`) draw page its source page, as `PaperDoll_RecolorRect` does ([tinting](paper-doll.md#tinting)). On a frame where only the offset changed, it fills 0x1f from the first name label's top-left to the last value label's bottom-right. On a new subject or view it fills the screen rect 0x1f and runs `HddDamageScreen_SetSubjectCaption`(display, caption) ([subject stepping](heads-down-display.md#subject-stepping)); with no subject it sets the no-subject label's text to the display's `+0x51b`, pops, copies the screen rect when copy is set and returns. Otherwise it blits the doll's picture at the origin with `Bitmap_Blit` and flags 0, then:
   - **views 0 and 1**: `HddDamageScreen_BlitWeaponIcons`, then over each icon's rect `PaperDoll_RecolorRect`(1, 0x32, 0x38, rect), its 0x38 pixels made 0x32; ES2's makes id 12 id 6;
   - **view 2**: over the view's rect, its origin to the origin plus the view's point `+0xc`, `PaperDoll_RecolorRect`(3, 0x32, 0x36), so 0x36–0x38 become 0x32; the same over each region whose `colorId` is not 0x36, with that `colorId` as the key; `HddDamageScreen_BlitWeaponIcons`; and over each icon's rect (1, 0x38, 0x36), its 0x36 pixels made 0x38. ES2's last pass makes id 12 id 12, which changes nothing.

   Both recolor loops over the icons walk every icon's rect, a hardpoint's with no icon included, whose rect `PaperDoll_BuildWeaponIcons` leaves unwritten ([the weapon icons](paper-doll.md#the-weapon-icons)); so do view 2's rows.
6. **The rows**, below ([the rows](#the-rows)).
7. It copies the 42 readouts into `+0x8c`, pops, and on a full redraw copies the screen rect when copy is set.

### The rows

**Row *i* runs over the view's regions in their `.PDG` order (views 0 and 1) or over the icons (view 2), and is shown while the offset ≤ *i* < the rows' end, on label pair *i* − offset.**

- **Views 0 and 1.** Region *i*'s index *k* picks the name (*k* of the list) and the reading (readout *k* of the view's entries); its rect, `colorId` and `recolorMode` ([the records](paper-doll.md#the-records)) the tint. In view 0, a region with index 0 or 2 tints on the mean of its reading and that of index *k* + 1, and one with index 1 or 3 tints nothing; its row still prints its own reading, its two states read through the region before it (for the first region, the bytes before the array), which give `Damage_ToConditionState`'s states whatever that region's mode from 0 to 3 ([tinting](paper-doll.md#tinting)). ES2's pairs index 0 and 1 alone, and for a flyer none.
- **View 2.** Icon *i*'s rect, the key 0x36 and mode 0 for the tint, readout 32 + *i*, and the name `HddDamage_WeaponNames`[the icon's `int16` `+0x20`]. The region whose mode and `colorId` `Damage_PickRegionTint` reads, for the tint and for the row's states alike, is the view's first; first the function sets `HddDamage_TintGuard` (`000b1694`) to 1 if it is 0, and on view 2 stores 0 at `000b16b0` ([Open](#open)).

Then, for a row that tints, on a full redraw or when the tint reading differs from the saved one: `Damage_PickRegionTint` for both ([tinting](paper-doll.md#tinting)), and on a full redraw or a changed state, view 2's `PaperDoll_RecolorRect`(0, new color, key, rect), its key the saved state's color unless this is a full redraw, or views 0 and 1's `PaperDoll_RecolorRectFromArt`(`recolorMode`, new color, `colorId`, origin, picture, rect), which tests the doll's art rather than the screen; the rect copied when copy is set.

And for a row with a name, on a full redraw or when its own reading differs from the saved one: the states of both readings; the value label is set while the row is shown, and the name label while the row is shown and on a full redraw or a changed state. The value's text is `ltoa`((`0x100` − reading) × 100 ≫ 8), and both labels take the state's font, ES2's: 0 `cpgreen`, 1 `cpylw`, 2 `cporange`, 3 `cpred` and 4 `cpgrey` (`ColorSchemePanels` 1, 3, 9, 2 and 7), a state outside 0–4, which `Damage_PickRegionTint` gives only for a mode outside 0–3, keeping the font index a row before it set (none on the first row), each label copied when this is not a full redraw and copy is set. A row with no name sets neither label, so on a full redraw, after the fill, its pair stays blank.

**`HddDamageScreen_BlitWeaponIcons` (`000432b0`)**, (screen, origin, list): for each of the list's count `+0` entries, `0x22` bytes from the pointer at `+4`, a non-null bitmap `+0` with `Bitmap_Blit`(bitmap, the entry's flags `+0x1c`, its position `+4`/`+8` plus the origin). ES2's (`00451db8`).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorHddDamageCopy`, `ConfigPart_DtorHddDamageCopy`, `ConfigPart_GetClassTagHddDamageCopy`, `TSPartBase_DtorHddDamageCopy` | `000420c8`, `000420f0`, `0004212f`, `00042138` | the run ([module copies](../formats/compiler.md#vtables)) |
| `HddDamageScreen_Ctor`, `HddDamageScreen_HideNoOp`, `HddDamageScreen_KeyDispatch` | `00042164`, `00042399`, `0004239f` | `0045079c`, `HDDisplay_HideNoOp` (`00451e86`), `00451e8b` |
| `HddDamageScreen_Repaint`, `HddDamageScreen_Tick`, `HddDamageScreen_SetView` | `000423a8`, `000423c4`, `000423d8` | `00450b2c`, `00450b4c`, `00450b60` |
| `HddDamageScreen_NextView`, `HddDamageScreen_PrevView`, `HddDamageScreen_PageDown`, `HddDamageScreen_PageUp` | `0004246c`, `00042494`, `000424bc`, `000424dc` | `00450bcc`, `00450bf0`, `00450c18`, `00450c38` |
| `HddDamageScreen_Update`, `HddDamageScreen_Show`, `HddDamageScreen_BlitWeaponIcons` | `000424f8`, `00043294`, `000432b0` | `00450c54`, `00451d94`, `00451db8` |

`ConfigPart_GetClassTagHddDamageCopy`, `HddDamageScreen_HideNoOp` and `HddDamageScreen_KeyDispatch` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `HddDamageColorIds`, four dwords | `00094a98` |
| `HDDDamageVtable` | `00094aaa` |
| `HddDamage_WeaponNames`, ten pointers | `000b160c` |
| `HddDamage_WeaponNameBuffer`, 8-byte slots up to `000b1693` | `000b1634` |
| `HddDamage_TintGuard` (byte) | `000b1694` |

## Open

- **Open:** the file's source path: `es1_boilerplate.py bounds` finds no assert source path in `000420c8`–`0004332f`.
- **Open:** what reaches `HddDamageScreen_PageDown` (`000424bc`) and `HddDamageScreen_PageUp` (`000424dc`): `es2_xref.py` finds no branch, stored pointer or vtable slot, where it finds `HddDamageScreen_SetView`'s eight calls. Without them the offset stays 0 and a view labels its first twelve rows. Proposed retail check, by eye: on the look-down view press `F8` and then `S`, and count the structural rows against the doll's regions.
- **Open:** how many regions each view of ES1's 17 `.PDG` files holds, in what index order, and whether the regions with index 2 and 3 share a rect as 0 and 1 do in ES2's files: one pass over the files through HercWorks.Core's `.PDG` reader. A structural view with more than twelve regions has rows past the labels.
- **Open:** what reads the name list `+0x84` that `HddDamageScreen_SetView` writes: `es2_fieldscan.py --binary ES1DBSIM 84` over this file finds the three writes and no read; the update picks its names itself.
- **Open:** what reads `HddDamageColorIds` (`00094a98`): `es2_xref.py` finds no reference to any of its four dwords.
- **Open:** what reads `HddDamage_TintGuard` (`000b1694`) and the dword at `000b16b0`, which the update writes: `es2_xref.py` finds them in the update alone, the guard's test and increment and the store of 0, and no reference to `000b1698`. ES2's update has the same pair at `0049da9c` and `004d1e34`.
- **Open:** which weapons' display names run to 8 characters or more, so that the weapons view shows a name joined to the next one's ([drawing it](#drawing-it--hdddamagescreen_update-000424f8), step 4): `WeaponMount_GetDisplayName` prefixes a missile's name with its ammunition's. Proposed retail check, by eye: `F8` and then `W` in a HERC with a missile launcher, and read the launcher's row.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:000423d8` fires on `I` on the look-down view with 1 at `[ESP+8]`; and by eye, `F8` shows the doll and twelve rows, and the up arrow steps the view.
