# DBSIM.EXE — the weapon rows and the console buttons

**ES1's weapon rows and the console buttons under them are ES2's ([weapon hardpoint rows](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows), [console buttons](../../retail/simulation/cockpit-hud-widgets.md#console-buttons)): each row's number arms its weapon on a left click and adds it to the fire chain on a right click, with a charge bar beside an energy weapon's name and the round count beside an ammunition weapon's; the ECM's row is a switch; and the console's buttons pick the fire chain, link a pair of weapons and switch the turret's automatic tracking.** What a player would see differs: the rows are there from the first frame, with no power-up and no sensor dropout; the round count is always yellow on a plate of palette index `0x3f`, where ES2's turns white or gray; where each part of a row sits comes from the layout file, where ES2's places them at fixed offsets; the clickable part of a row is its number alone, and the charge bar carries a drawn knob at the weapon's power level; the console has a button for each of the three chains, the current one lit, where ES2's has one that steps through them, and its TRACK button shows only its press, the tracking showing as `ATT` on the gunsight alone; and the keys `` ` `` and `Alt`+`` ` `` step the chain, `Alt+T` switches the tracking and `L` links. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

ES1's fonts named here are `ColorSchemePanels`' (`00092d40`), whose index is their load order ([building it](cockpit-panel.md#building-it)): `cpblue` 0, `cpgreen` 1, `cpred` 2, `cpylw` 3, `cpon` 4, `cppress` 5 and `cpoff` 6. A frame *n* of `dba\pbuttons` is dword *n* of the frame array that `CockpitPButtonsBank`'s (`00092d68`) `+8` points at.

## Where the object file starts and ends

The code is `00036eb4`–`00038f0b` and the initialized data starts at `00093a06`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `00036eb4`, `00036edc`, `00036f1b` (frameless, with no Ghidra function) and `00036f24`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `00093a06` and `00093a3a`. The file has no static initializer: the static constructor table's entry after the NAV MAP's `000adc44` is `000adc48`, which holds `0003edac`. A `RET 8` at `00038f06` is followed by three zero bytes and, at `00038f0c`, a lone copy of the 40-byte destructor, which opens the next file, the one holding the constructor `Gau_ShieldDisplayWidget` calls (`00038f34`) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). `es1_boilerplate.py bounds` also reports a `RET 0xce9` at `00038998` followed by a zero byte; those bytes are inside the this-adjusting thunk at `00038994` ([the charge bar](#the-charge-bar--weaponslidergadget)), not a file's end.

The file holds, in ES2's order, the row gauge base, the energy and ammunition rows, the pod row from `00037810`, the console buttons from `00037bd0`, and the rows' and the console's children from `000380fc`, the classes ES2's names `WeaponSelectGadget`, `ChainedWeaponSelectGadget`, `WeaponSliderGadget` and `WeaponRangeSelectGadget`.

## The row gauge base

**`WeaponGauge_Ctor` (`00036f50`)**, (gauge, mount, cockpit, rect), runs `PanelGauge_Ctor`(gauge, cockpit, rect) ([the gauge base](cockpit-widgets.md#the-gauge-base)), stores `WeaponGaugeVtable` (`00093a6e`) at `+0x76` and the mount at `+0x86`, and clears the bytes `+0x7a` and `+0x8a`. ES2's (`0044080c`) also loads the `pweapons` and `wpn_dmg` banks and gives the row its sensor-dropout sequencer and ranges ([the wipes](../../retail/simulation/cockpit-hud-widgets.md#the-wipes)), which ES1's has none of.

`WeaponGaugeVtable` is ES2's `ChainedWeaponGaugeVtable` (`0049c93c`) one slot further on, with 0 at `+8` and `+0xc` where ES2's holds `Rtl_PureVirtualCalled`. Its three functions stand after the constructor, frameless and with no Ghidra function:

| Slot | Function | Code |
|---|---|---|
| `+4` | `PanelGauge_OnChildClickNoOpWeaponGaugeCopy` (`00036f85`) | three `NOP`s and `RET 0xc`, the code of `PanelGauge_OnChildClickNoOp` (`00031395`) |
| `+0x10` | `PanelGauge_KeyDispatchNoneWeaponGaugeCopy` (`00036f8b`) | `NOP`, `XOR EAX,EAX`, `RET 8`, the code of `PanelGauge_KeyDispatchNone` (`0003139b`); the energy and ammunition tables enter it at `00036f8c` |
| `+0x14` | `WeaponGauge_SetStateNoOp` (`00036f91`) | three `NOP`s and `RET 8`, ES2's `00443417`; the pod row's table enters it at `00036f94` |

**`WeaponGauge_MountProjectileType` (`00036f98`)** and **`WeaponGauge_MountProjectileSubtype` (`00036fd0`)**, (gauge), are ES2's (`00440a14`, `00440a3c`) on ES1's offsets: the `int16` `+0` or `+2` of the record at the mount's `+0x20`, or −1 with no record. With no mount they return −1 in `AX` alone. `es2_xref.py` finds one call of each, from `MfdDisplay_SyncMissileCamMode` (`0003ad08`), which switches the MFD to the missile camera while the armed mount's record has type 0 and subtype 3 ([the missile camera switch](mfd-display.md#the-missile-camera-switch)).

## The rows' fields

The energy row's object is `0xa4` bytes and the ammunition row's `0xb8`; ES2's are `0xdf` and `0xef`.

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00` | `+0x04` | the row's rect, the block's first 16 bytes |
| `+0x10` | `+0x14` | the cockpit |
| `+0x14`, `+0x18` | `+0x18`, `+0x1c` | child 0, the select button, and the energy row's child 1, the charge bar |
| `+0x64` | `+0x68` | the child count |
| `+0x75` | `+0xb0` | the ammunition row's repaint counter, which is the [gauge base's](cockpit-widgets.md#sensor-dropout) byte |
| `+0x76` | `+0x00` | the vptr |
| `+0x7a` | `+0xb1` | the mount's name, 12 bytes |
| `+0x86` | `+0xbd` | the mount |
| `+0x8a` | `+0xc1` | the energy row's held-back destroyed change |
| `+0x8b` | `+0xc2` | the state block, `0x17` bytes: `+0x8b` the value, `+0x8f` the charge bar's position, `+0x9c` destroyed, and the three state flags ([the block](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows)), which the number button's paint reads as armed `+0x9f`, in the current chain `+0xa0` and ready `+0xa1` ([the number button](#the-number-button--weaponselectgadget)) |
| `+0xa2` | `+0xd9` | the row, `int16` |
| `+0xa4` | `+0xdb` | the ammunition row's round-count label |
| `+0xa8`–`+0xb4` | `+0xdf`–`+0xeb` | the ammunition row's value rect |

ES2's energy row also keeps the value its power-up fill ramps toward at `+0xdb`.

## The energy row

**`EnergyWeaponGauge_Ctor` (`0003700c`)**, (gauge, mount, name, row, cockpit, block), allocates `0xa4` bytes when given none, runs `WeaponGauge_Ctor`(gauge, mount, cockpit, block), stores `WeaponGaugeVtable` again, the row at `+0xa2` and then `EnergyWeaponGaugeVtable` (`00093a8e`). With (*x*, *y*) the block's top-left and `CockpitViewInstance`'s root widget as the list:

1. child 0 is `ChainedWeaponSelectGadget_Ctor` (`00038338`)(0, row, gauge, the block's rect at `+0x10` moved by (*x*, *y*)) ([the number button](#the-number-button--weaponselectgadget)), stored at `+0x14` and registered with `Widget_RegisterClickable`;
2. the name's first 12 bytes go to `+0x7a` (`strncpy`);
3. child 1 is `WeaponSliderGadget_Ctor` (`000386a0`)(0, the block's dword `+0x3c`, gauge, the block's rect at `+0x20` moved by (*x*, *y*)) ([the charge bar](#the-charge-bar--weaponslidergadget)), stored at `+0x18` and registered;
4. the child count is 2, `+0x8f` is set to 800, and `memset` clears the `0x17` bytes from `+0x8b`, the 800 with them.

ES2's (`00440a68`) stores `0x400` at `+0xc6` and clears it the same way. It hands its select button the block whole with the row's mirror flag, and its charge bar a rect from fixed offsets; ES1's children take their rects from the block ([the layout](../formats/gau-cockpit-layout.md#differences-from-es2)) and no mirror flag. It reads the name with `WeaponMount_GetDisplayName`, where ES1's factory passes it in ([the weapon rows](cockpit-panel.md#the-weapon-rows)).

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `EnergyWeaponGauge_OnChildClick` (`000372e4`) | `+0`, `00440ef0` |
| `+8` | `EnergyWeaponGauge_Paint` (`00037144`) | `+4`, `00440c68` |
| `+0xc` | `EnergyWeaponGauge_Update` (`0003716c`) | `+8`, `00440c90` |
| `+0x10` | `PanelGauge_KeyDispatchNoneWeaponGaugeCopy` | `+0xc`, `PanelGauge_KeyDispatchNone` |
| `+0x14` | `EnergyWeaponGauge_SetState` (`00037194`) | `+0x10`, `00440d4c` |

- **`EnergyWeaponGauge_Paint` (`00037144`)**, (gauge), calls slot `+4`, the paint, of each child's table (at the child's `+0x1c`). ES2's does so only while the row is armed.
- **`EnergyWeaponGauge_Update` (`0003716c`)**, (gauge), calls slot `+0x10` of each child's table. ES2's first tests that the row is on screen and armed, runs the power-up fill, the first paint and the sensor dropout, and repaints the children when the row comes back from dark ([ES2's update](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows)).
- **`EnergyWeaponGauge_SetState` (`00037194`)**, (gauge, block), is ES2's without the power-up's value: a new block `+4` goes to `+0x8f` and to the charge bar's slot `+0x18`; a new block `+0` to `+0x8b` and the bar's `+0x7a`; a new destroyed byte `+0x11` to `+0x9c`, and both children's `int16` state `+0x17` to 2 or 0, held back through `+0x8a` while `CockpitView_GetShakeOffset`'s `+4` is nonzero; the bytes `+0x14`–`+0x16` to `+0x9f`–`+0xa1`. It repaints, slot `+8`, child 0 when a byte changed and the bar when a value did. `EnergyWeaponGauge_GetState` (`000372d8`), (gauge), frameless, returns the gauge plus `0x8b`.
- **`EnergyWeaponGauge_OnChildClick` (`000372e4`)**, (gauge, value, child), is ES2's: for child 0, with bit 1 of the value set it calls `WeaponMounts_ToggleChainMember` (`0000d5e4`)(the cockpit machine's weapon manager `+0x198`, the dword at `+0xa2`), which reads the row from its low half ([chaining](weapon-mounts.md#arming-chaining-and-linking)), and otherwise `CockpitView_ArmWeaponByGauge`(cockpit, gauge); for child 1 it stores the value, clamped to 0..`0x400`, at `+0x8f`. The dword at `+0xa2` holds the row in its low half and runs two bytes past the object.

## The ammunition row

**`AmmoWeaponGauge_Ctor` (`000373a4`)**, (gauge, mount, name, row, cockpit, block), allocates `0xb8` bytes when given none and builds child 0 and the name as the energy row does, with `ProjectileWeaponGaugeVtable` (`00093aae`) as its table; the inlined constructor of the value rect `+0xa8` writes nothing. The value rect is the block's rect at `+0x20` moved by (*x*, *y*), and the round-count label at `+0xa4` is `Label_Ctor`(0, `ColorSchemePanels`' `cpylw`) placed by `Label_SetRect` left-aligned over it with zero margins, with the fill color `0x3f`. The child count is 1, and the state block is cleared. ES2's (`00440f78`) builds its label in `gray` over fixed offsets on a plate of `0x2e`, right-aligned when mirrored.

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `AmmoWeaponGauge_OnChildClick` (`00037784`) | `+0`, `004414b4` |
| `+8` | `AmmoWeaponGauge_Paint` (`00037544`) | `+4`, `004411b4` |
| `+0xc` | `AmmoWeaponGauge_Update` (`000375e4`) | `+8`, `00441268` |
| `+0x10` | `PanelGauge_KeyDispatchNoneWeaponGaugeCopy` | `+0xc` |
| `+0x14` | `AmmoWeaponGauge_SetState` (`000376b4`) | `+0x10`, `004413f4` |

- **`AmmoWeaponGauge_Paint` (`00037544`)**, (gauge), calls child 0's paint, then, when the label's rect has a corner on screen (`CockpitView_RectCornerVisible`), child 0's state `+0x17` is not 2 and `+0x9c` is clear, fills the value rect with the brush {0, `0x3f`} (`Raster_FillRect`) and writes `ltoa`(`+0x8b`, 10) into the label with `Label_SetText`, which fills the same rect again. ES2's runs only while the row is armed and neither dark nor mid-wipe, and fonts the label `white` while the first state flag is set and `gray` otherwise.
- **`AmmoWeaponGauge_Update` (`000375e4`)**, (gauge), calls child 0's slot `+0x10`; then, with the repaint counter `+0x75` at 2 and the label's rect on screen, it writes the count as the paint does, unless destroyed, and lowers the counter, which stays at 2 while the label's rect is off screen; at 1 it copies the label's rect to the display with `Widget_DrawToCockpit`(0, rect, 0), unless destroyed, and lowers the counter. ES2's runs the same steps behind its update's gates, power-up and sensor dropout, and also raises the counter when child 0's byte `+0x1f` is set.
- **`AmmoWeaponGauge_SetState` (`000376b4`)**, (gauge, block): a new round count goes to `+0x8b` and sets the counter to 2; a new destroyed byte to `+0x9c` and child 0's state to 2 or 0; the three flags to `+0x9f`–`+0xa1`; a byte change repaints child 0. ES2's also sets the counter on a byte change. `AmmoWeaponGauge_GetState` (`00037778`), (gauge), frameless, returns the gauge plus `0x8b`.
- **`AmmoWeaponGauge_OnChildClick` (`00037784`)**, (gauge, value, child), is the energy row's for child 0 alone. ES2's also sets its repaint counter.

## The pod row

**ES1's one pod row is the ECM's: a number button and the word `ECM`, which a click on the number or the row's number key switches on and off, the word's plate changing color as it does.** It is ES2's switchable pod row (`TogglePodGauge`, [equipment pods](../../retail/simulation/equipment-pods.md#only-two-pods-have-a-button)) as a single class, with ES1's placement from the layout file. `EcmMount_CreateGauge` (`0000abec`) builds it through `CockpitView_CreatePodGauge` ([the weapon rows](cockpit-panel.md#the-weapon-rows)), and `EcmMount_PushGaugeState` (`0000b588`) copies the switch into the mount each frame ([the ECM](weapon-mount-classes.md#the-ecm)).

The object is `0xa1` bytes, the row gauge base's fields with these:

| Offset | Holds |
|---|---|
| `+0x14` | child 0, the number button ([below](#the-number-button--weaponselectgadget)); the child count `+0x64` is 1 |
| `+0x75` | the repaint counter, the [gauge base's](cockpit-widgets.md#sensor-dropout) byte |
| `+0x7a` | the name, 12 bytes |
| `+0x8b`, `+0x8c` | the state block: the switch, and the destroyed byte |
| `+0x8d` | the name label |
| `+0x91`–`+0x9d` | the label's rect |

**`PodGauge_Ctor` (`00037810`)**, (gauge, mount, row, cockpit, block), allocates `0xa1` bytes when given none, returning 0 when that fails, runs `WeaponGauge_Ctor`(gauge, mount, cockpit, block) and stores `PodGaugeVtable` (`00093ace`) at `+0x76`. With (*x*, *y*) the block's top-left and `CockpitViewInstance`'s root widget as the list:

1. child 0 is `WeaponSelectGadget_Ctor` (`000380fc`)(0, row, gauge, the block's rect at `+0x10` moved by (*x*, *y*)), stored at `+0x14` and registered with `Widget_RegisterClickable`;
2. the first 12 bytes of `STRINGS0.STR` group 3's one string, `ECM` (`000bd2c8`, [the groups](../formats/str-strings.md#the-groups)), go to `+0x7a` (`strncpy`), whatever the mount;
3. the label's rect `+0x91` is the block's rect at `+0x20` moved by (*x*, *y*), and the label at `+0x8d` is `Label_Ctor`(0, `ColorSchemePanels`' `cpred`) placed by `Label_SetRect`(label, 2, the rect, {0, 0}), centered, with the fill color `0x3f`;
4. the child count is 1, and `memset` clears the two bytes from `+0x8b`.

It returns the gauge. ES2's (`00441524`, with `TogglePodGauge_Ctor` `00441998` over it) builds the name `" "` + the mount's name + `" POD"` and its label at fixed offsets from the row's rect. The block is the layout's from 340 ([the layout](../formats/gau-cockpit-layout.md#differences-from-es2)).

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `PodGauge_OnChildClick` (`00037b5c`) | `TogglePodGauge_OnClick` (`004419fc`) |
| `+8` | `PodGauge_Paint` (`0003799c`) | `PodGauge_Paint` (`0044171c`) |
| `+0xc` | `PodGauge_Update` (`00037abc`) | `PodGauge_Update` (`00441850`) |
| `+0x10` | `PanelGauge_KeyDispatchNoneWeaponGaugeCopy`, by `00036f8c` | the same |
| `+0x14` | `WeaponGauge_SetStateNoOp`, by `00036f94` | `TogglePodGauge_SetStateBlock` (`004419c4`) |

- **`PodGauge_Paint` (`0003799c`)**, (gauge), calls child 0's paint (slot `+4` of its table at `+0x1c`), then, when the label's rect has a corner on screen (`CockpitView_RectCornerVisible`) and child 0's `int16` state `+0x17` is not 2, installs a brush on the stack at the render context's `+0x22c` and writes the label with `Label_SetText`, its font `cpylw`: with the destroyed byte clear, the name, on a plate of `0x38` while the switch `+0x8b` is set and `0x3f` while it is clear; with it set, `STRINGS0.STR` group 2's `OFFLINE` (`000bd2c4`) on `0x3f`, the label placed again by `Label_SetRect`(label, 2, {child 0's *x0*, *y0*, the label's *x1*, *y1*}, {0, 0}), and child 0's state set to 2 after it has painted. ES2's runs only while the row is armed and neither dark nor mid-wipe, and picks a gray font on `0x2e` off and a dark one on green on.
- **`PodGauge_Update` (`00037abc`)**, (gauge), steps the repaint counter `+0x75`: at 2 the paint (slot `+8`), at 1 `Widget_DrawToCockpit`(0, the label's rect, 0), each lowering it by 1; at 0 it calls child 0's per-frame slot `+0x10`. ES2's runs the same two steps behind its update's gates, power-up and sensor dropout.
- **`PodGauge_SetStateBlock` (`00037b14`)**, (gauge, block): when either of the block's two bytes differs from the gauge's, copies the two and sets the counter to 2. ES2's also copies the next dword. It is not in the gauge's table: `es2_xref.py` finds one call, from `CockpitWidgets_HandleCommand` ([keys](cockpit-panel.md#keys-and-the-view-device)), which hands it a copy of the gauge's block with bit 0 flipped.
- **`PodGauge_GetStateBlock` (`00037b50`)**, (gauge), frameless, returns the gauge plus `0x8b`, as ES2's (`00441978`) returns its `+0xc2`. `es2_xref.py` finds calls from `CockpitWidgets_HandleCommand` and `EcmMount_PushGaugeState`.
- **`PodGauge_OnChildClick` (`00037b5c`)**, (gauge, value, child): for child 0 it flips the switch and sets the counter to 2, whatever the value, as ES2's `TogglePodGauge_OnClick`.

So the switch changes on a click on the row's number, by any mouse button the number button passes on ([the number button](#the-number-button--weaponselectgadget)), and on the row's number key, and nothing else of the row's writes it. The destroyed byte is written by the constructor's `memset` and by `PodGauge_SetStateBlock`, from the copy `CockpitWidgets_HandleCommand` makes of the gauge's own block, so those writers keep it 0 ([Open](#open)).

## The console buttons

**The console under the rows has five buttons: `I`, `II` and `III`, one for each fire chain, the one for the current chain lit; `LINK`, which links the armed weapon with its pair; and `TRACK`, which switches the turret's automatic tracking, shown as `ATT` on the gunsight.** ES2's console ([console buttons](../../retail/simulation/weapon-mounts.md#console-buttons)) has one chain button that steps through the three, LINK, TRACK and an unused fourth. `Gau_WeaponButtonsWidget` builds it from the layout's block at 452 ([building it](cockpit-panel.md#building-it)) and the weapon manager exchanges a state block with it each frame ([the per-frame pass](weapon-mounts.md#the-per-frame-pass--weaponmounts_perframeupdate-0000cf78)).

The object, ES2's `0xb5`-byte one on the gauge base, is `0x7e` bytes:

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00` | `+0x04` | the block's first 16 bytes, the gauge base's rect |
| `+0x14`–`+0x24` | `+0x18`–`+0x24` | the buttons: `I`, `II`, `III`, `LINK`, `TRACK`; ES2's chain, LINK, TRACK and the fourth |
| `+0x64` | `+0x68` | the button count, 5 |
| `+0x76` | `+0x00` | the vptr, `WeaponRangeGaugeVtable` (`00093aee`) |
| `+0x7a` | `+0xb1` | the current chain, `int16`, 0–2 |
| `+0x7c` | `+0xb3` | the tracking latch |
| `+0x7d` | `+0xb4` | the link request |

**`ConsoleButtons_Ctor` (`00037bd0`)**, (console, cockpit, block), allocates `0x7e` bytes when given none, returning 0 when that fails, runs `PanelGauge_Ctor`(console, cockpit, block) and stores `WeaponRangeGaugeVtable` at `+0x76`. With (*x*, *y*) the block's first two `int32`, for each button *i* from 0 to 4 it takes the block's rect at `+0x10` + 16*i* moved by (*x*, *y*), builds the button with `WeaponRangeSelectGadget_Ctor` (`00038ce4`)(0, console, rect) ([the console button](#the-console-button--weaponrangeselectgadget)), registers it in `CockpitViewInstance`'s root widget's list with `Widget_RegisterClickable`, stores it at `+0x14` + 4*i*, and gives it a label, `Label_Ctor`(0, `ColorSchemePanels`' `cpgreen`) placed by `Label_SetRect`(label, 2, the rect, {0, 0}), centered, at the button's `+0x2c`, and the index *i* at its `+0x28`. It sets the count to 5 and clears `+0x7c`; it writes neither `+0x7a` nor `+0x7d`. `Gau_WeaponButtonsWidget` then calls `ConsoleButtons_SetChainNumber`(console, 2). ES2's (`00441dd0`) builds four buttons from 484 the same way and puts TRACK in state 2 for a flyer. The block's header is the layout's at 452 ([the console block](../formats/gau-cockpit-layout.md#differences-from-es2)).

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `ConsoleButtons_OnChildClick` (`00037f00`) | `+0`, `0044212c` |
| `+8` | `ConsoleButtons_Paint` (`00037e20`) | `+4`, `00442058` |
| `+0xc` | `ConsoleButtons_Update` (`00037e4c`) | `+8`, `0044207c` |
| `+0x10` | `ConsoleButtons_HandleCommand` (`00038034`) | `+0xc`, `004421a0` |

- **`ConsoleButtons_Paint` (`00037e20`)** and **`ConsoleButtons_Update` (`00037e4c`)**, (console), call each of the five buttons' paint (slot `+4`) or per-frame slot (`+0x10`), as ES2's do over four.
- **`ConsoleButtons_SetChainNumber` (`00037ce4`)**, (console, chain), stores the chain at `+0x7a`; for 0, 1 or 2 it lights that chain's button (its lamp byte `+0x3c` set to 1) and puts out the other two, repainting (slot `+8`) only a button whose lamp changes. ES2's (`00441f68`) stores the number alone, its chain button showing it as a caption. `es2_xref.py` finds calls from `Gau_WeaponButtonsWidget`, `WeaponMounts_SetChain` ([chaining](weapon-mounts.md#arming-chaining-and-linking)), `ConsoleButtons_SetStateBlock` and, twice, `ConsoleButtons_HandleCommand`.
- **`ConsoleButtons_OnChildClick` (`00037f00`)**, (console, flags, button), finds the button among the five: for the chain buttons 0–2 it puts out the other two chains' lamps, repainting each that was lit, and stores the button's index at `+0x7a`; for `LINK`, `ConsoleButtons_SetLinkLatch`(console, 1); for `TRACK`, `ConsoleButtons_ToggleAutoTrack`. The button lights its own lamp before it calls this ([the console button](#the-console-button--weaponrangeselectgadget)). ES2's steps the chain, wrapping after 2, sets the link latch or toggles tracking.
- **`ConsoleButtons_ToggleAutoTrack` (`00037df8`)**, (console), flips `+0x7c` and does nothing else. ES2's (`00441f7c`) refuses a TRACK button in state 2, copies the latch into the button's lamp, repaints it and posts `AUTO TRACKING ENGAGED` or `DISABLED`.
- **`ConsoleButtons_SetLinkLatch` (`00037e0c`)**, (console, value), stores the value at `+0x7d`. ES2's (`0044202c`) also runs `WeaponMounts_ToggleLink`.
- **`ConsoleButtons_GetStateBlock` (`00037ed4`)**, (console), copies the chain (`int16`), the tracking latch and the link request into the four bytes of `ConsoleButtons_StateBlock` (`000b13e8`) and returns it, as ES2's (`004420f8`) does into `004d1ae0`.
- **`ConsoleButtons_SetStateBlock` (`00037e78`)**, (console, block), is ES2's (`004420a0`): a chain other than `+0x7a` is stored and handed to `ConsoleButtons_SetChainNumber`; a byte 2 other than `+0x7c` runs `ConsoleButtons_ToggleAutoTrack`; a byte 3 other than `+0x7d` goes to `ConsoleButtons_SetLinkLatch`.
- **`ConsoleButtons_HandleCommand` (`00038034`)**, (console, code), looks the code up among the four dwords of `ConsoleButtons_CommandCodes` (`00093b1c`) with `REPNE SCASD` and jumps through the four after them, returning 1 for these and 0 for any other:

  | Code | Key | What it does |
  |---|---|---|
  | `0x29` | `` ` `` | the next chain, 2 wrapping to 0, through `ConsoleButtons_SetChainNumber` |
  | `0x229` | `Alt`+`` ` `` | the previous chain, 0 wrapping to 2, the same way |
  | `0x214` | `Alt+T` | presses `TRACK` (`+0x24`): unless its `int16` state `+0x17` is 2 or 3, its click slot `+0xc` with (button, 1) |
  | `0x26` | `L` | `WeaponMounts_ToggleLink` (`0000d41c`) on the weapon manager `+0x198` of `CockpitViewInstance`'s machine `+0x1ff`, then `Sound_Play`(`0x11`) |

  `CockpitWidgets_HandleCommand` reaches the slot for a key no earlier handler takes ([keys](cockpit-panel.md#keys-and-the-view-device)). ES2's (`004421a0`) presses LINK on `L` and the chain button on `` ` ``, and toggles tracking on `T` elsewhere ([automatic turret tracking](../../retail/simulation/torso-aim.md#automatic-turret-tracking--t)).

### What the buttons do

The weapon manager's per-frame pass reads `ConsoleButtons_GetStateBlock`'s block and writes it back through `ConsoleButtons_SetStateBlock` ([the per-frame pass](weapon-mounts.md#the-per-frame-pass--weaponmounts_perframeupdate-0000cf78), step 3), so each button acts through the manager on the next frame:

- **A chain button** stores its chain at `+0x7a`, and the next pass hands it to `WeaponMounts_SetChain`. `` ` `` and `Alt`+`` ` `` do the same through `ConsoleButtons_SetChainNumber`. A chain the manager sets itself (its constructor, the chain key `0x30` of `WeaponMounts_HandleCommand`) sets the manager's `+0x16`, and that pass puts the manager's chain into the block instead, which `ConsoleButtons_SetStateBlock` lights.
- **`LINK`** sets `+0x7d`; the next pass runs `WeaponMounts_ToggleLink` once, clears byte 3 and writes the block back, and `ConsoleButtons_SetStateBlock` stores the 0 without a second toggle, so a press is one toggle, where ES2's runs three. `L` toggles the link at once, without the request.
- **`TRACK`**, clicked with the left button or pressed by `Alt+T`, flips the tracking latch `+0x7c`. Each pass then sets the manager's `+0x14` with `WeaponMounts_SetTrackLatch` (`0000d408`) while byte 2 is set and clears it while it is clear; `Sim_PollPlayerInput` turns the turret toward the target while `+0x14` is set, the turret has no input and the target lives ([the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0), step 6); and `Gunsight_Paint` writes `ATT` while `+0x7c` is set ([painting](cockpit-gunsight-hud.md#painting-and-the-per-frame-update)). None of the console's functions posts on the computer's channel, where ES2's toggle posts `AUTO TRACKING ENGAGED` or `DISABLED` ([Open](#open)).

## The number button — `WeaponSelectGadget`

**A row's clickable part is its number: a button the width of the digit, drawn from `dba\pbuttons` in its press state, which passes every click to the row's gauge.** The energy and ammunition rows use the chained class, which adds the name and the state box; the pod row the plain one.

| Offset | Holds |
|---|---|
| `+0x00` | the rect, set by the constructor ([below](#the-plain-button)) |
| `+0x17` | the state, `int16`: 0 up, 1 pressed, 2 destroyed |
| `+0x1b` | the paint counter |
| `+0x1c` | the vptr |
| `+0x20`, `+0x24` | the `PanelGadget` mixin: the owning gauge and its vptr |
| `+0x28` | the row |
| `+0x2c` | the chained class's name label |
| `+0x3c` | the digit's label |
| `+0x40` | the digit, then a 0 byte |
| `+0x42`, `+0x46` | the chained class's state box, its top-left |
| `+0x4a`–`+0x56` | the chained class's second rect, from the box to the name's end |

### The plain button

**`WeaponSelectGadget_Ctor` (`000380fc`)**, (gadget, row, gauge, rect), allocates `0x42` bytes when given none, returning 0 when that fails; runs `Widget_CtorRect` on the rect and stores the module's copy of the button base table (`00093b3e`) and 0 at the drag flag `+0x19`; then `PanelSelectGadget`'s tables (`00093b66` at `+0x24`, `00093b7e` at `+0x1c`), the gauge at `+0x20`, the row at `+0x28` and the state 0; then `WeaponSelectGadgetVtable` (`00093bbe`) and its mixin table `00093ba6`. It builds the digit's label at `+0x3c`, `Label_Ctor`(0, `cpon`) placed by `Label_SetRect`(label, 2, {*x0*, *y0*, *x0* + 9, *y1*}, {0, 0}), centered; stores the digit, the row + `'1'`, at `+0x40` and 0 at `+0x42`, one byte past the `0x42` it allocates; and last sets its own rect's *x1* to *x0* + 8 and *y1* to *y0* + the rows (`int16` `+4`) of `pbuttons`' frame 0. So the rows in the retail files give it 9 columns from the row's left, where ES2's covers the row. ES2's (`004421dc`) prints (row + 1) mod 10 and places the digit 2 to 5 units in.

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `WeaponSelectGadget_PaintUnderlay` (`0003825c`) | `+0`, `00442394` |
| `+8` | `Widget_InvalidateDeferredWeaponGaugeCopy` (`00038234`), by `00038233`: `+0x1b` = 2 | `+4` |
| `+0xc` | `WeaponSelectGadget_OnClick` (`00038304`) | `+8`, `00442458` |
| `+0x10` | `WeaponSelectGadget_FlushDeferredPaint` (`000382cc`) | `+0xc`, `00442424` |
| `+0x14`–`+0x1c` | the button base's value, set-value and drag slots, the module's copies | the same |

- **`WeaponSelectGadget_PaintUnderlay` (`0003825c`)**, (gadget), when its rect has a corner on screen, blits `pbuttons`' frame *state* (`+0x17`) at its top-left with `Bitmap_Blit`, and writes the digit in `ColorSchemePanels`[4 + *state*]: `cpon` up, `cppress` pressed, `cpoff` destroyed. ES2's blits `WPN_DMG`'s frame 0 and colors the digit by state.
- **`WeaponSelectGadget_FlushDeferredPaint` (`000382cc`)**, (gadget), is ES2's: at a paint counter of 2 the paint, at 1 `Widget_DrawToCockpit`(0, rect, 0), each lowering it by 1.
- **`WeaponSelectGadget_OnClick` (`00038304`)**, (gadget, flags), is ES2's: whatever the flags, the gauge's slot `+4` with (gauge, flags, gadget), then its own repaint (`+8`) and the mixin's click sound (`Widget_ClickSound`).

The mixin table `00093ba6` holds `WeaponSelectGadget_PaintUnderlayThunk` (`00038244`) and `WeaponSelectGadget_FlushDeferredPaintThunk` (`00038250`), each `ADD dword ptr [ESP+4], -0x20` and a jump into the padding before the paint or the flush, and `Widget_ClickSound`, as ES2's (`0044340d`, `00443403`).

### The chained button

**`ChainedWeaponSelectGadget_Ctor` (`00038338`)**, (gadget, row, gauge, rect), allocates `0x5a` bytes when given none, returning 0 when that fails, runs `WeaponSelectGadget_Ctor` with its arguments and stores `ChainedWeaponSelectGadgetVtable` (`00093bfe`) and its mixin table `00093be6`. With {*x0*, *y0*, *x1*, *y1*} the rect it was given:

1. the state box's top-left `+0x42`, `+0x46` is (the digit label's *x1* + 2, *y0*), so (*x0* + 11, *y0*);
2. the name's label at `+0x2c` is `Label_Ctor`(0, `cpylw`) placed by `Label_SetRect`(label, 1, {*x0* + 15, *y0*, *x1*, *y1*}, {1, 0}), left-aligned one column in;
3. the second rect `+0x4a` is {*x0* + 11, *y0*, *x1*, *y1*}.

ES2's (`00442488`) places the box and the name at fixed offsets, mirrored when its flag is set, and fills the name's label with `0x2e`.

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `WeaponSelectGadget_Paint` (`00038464`) | `+0`, `004426c0` |
| `+8` | `Widget_InvalidateDeferredWeaponGaugeCopy` | `+4` |
| `+0xc` | `ChainedWeaponSelectGadget_OnClick` (`0003866c`), `WeaponSelectGadget_OnClick`'s code | `+8`, `00442920` |
| `+0x10` | `ChainedWeaponSelectGadget_FlushDeferredPaint` (`0003861c`) | `+0xc`, `004428d4` |

- **`WeaponSelectGadget_Paint` (`00038464`)**, (gadget), when its rect has a corner on screen: `WeaponSelectGadget_PaintUnderlay`; then, with the gauge's destroyed byte `+0x9c` clear, the name `+0x7a` in `cpylw` on a plate of `0x35` while the row is armed (`+0x9f`) and in `cpgreen` on `0x3f` while it is not, and the state box: while the row is armed or in the current chain (`+0xa0`), `pbuttons`' frame 55 when it is ready (`+0xa1`) and 56 when it is not (the frame array's `+0xdc` and `+0xe0`), blitted at the box's top-left; otherwise a `Raster_FillRect` of `0x3f` over {*bx*, *by*, *bx* + 2, *by* + 7}. With the destroyed byte set, its own rect's *x1* and *y1* become the gauge's (`+8`, `+0xc`, the row's), the label is placed again by `Label_SetRect`(label, 2, that rect, {0, 0}), centered, and shows `OFFLINE` in `cpylw` on `0x3f`, and the box is frame 57 (`+0xe4`). The rect stays widened, so a destroyed weapon's number button spans its whole row. ES2's draws the row plate's bezel and a `PWEAPONS` box, and the offline text over a widened label.
- **`ChainedWeaponSelectGadget_FlushDeferredPaint` (`0003861c`)**, (gadget): at a paint counter of 2, `WeaponSelectGadget_PaintUnderlay` and the paint; at 1, `Widget_DrawToCockpit`(0, …, 0) on the rect and on the second rect; each lowering it by 1, as ES2's.

The mixin table `00093be6` holds `ChainedWeaponSelectGadget_PaintThunk` (`0003844c`) and `ChainedWeaponSelectGadget_FlushDeferredPaintThunk` (`00038458`), the same thunks to the paint and the flush, and `Widget_ClickSound`, as ES2's (`004433ef`, `004433e5`).

## The charge bar — `WeaponSliderGadget`

**An energy row's charge bar is an LED bar graph that fills with the weapon's charge, with a knob over it, a one-column picture from `dba\pbuttons`, at the weapon's power level; the knob is a slider's, which the pointer can drag ([the slider base](cockpit-mouse.md#the-slider-base)).** ES2's ([the charge bar](../../retail/simulation/weapon-firing.md#the-charge-bar)) draws no knob and lies under the row's select gadget.

| Offset | Holds |
|---|---|
| `+0x00`–`+0x3d` | the slider base: the bar's rect `+0`, the state `+0x17`, the drag flag `+0x19`, the paint counter `+0x1b`, the vptr `+0x1c`, the range 0 to `0x400` at `+0x24` and `+0x26`, the `int16` position `+0x28` and previous position `+0x2a`, the knob's length `+0x2c` and its rect `+0x2e`–`+0x3a` |
| `+0x3e`, `+0x42` | the `PanelSliderGadget` mixin: the owning gauge and its vptr |
| `+0x46` | the `LEDBarGraphH` |
| `+0x4a` | the value the bar was last painted to |
| `+0x4e` | the knob's bitmap |
| `+0x52` | the save buffer for the screen under the knob |
| `+0x56` | a `GLRectangleRegion`, its rect at `+0x5e`–`+0x6a` |
| `+0x6e`, `+0x72` | the top-left the saved screen came from |
| `+0x7a` | the value the bar shows, the capacitor's level |

**`WeaponSliderGadget_Ctor` (`000386a0`)**, (bar, colors, gauge, rect):

1. allocates `0x7e` bytes when given none; runs `SliderWidget_CtorBase`(bar, 1, 0, `0x100`, 0, rect), stores the module's copy of the slider base table (`00093c26`) and runs `SliderWidget_RecomputeScaleH`;
2. stores the gauge at `+0x3e`, and builds the region at `+0x56` in place with the module's copies of its tables (`00093c76`, `00093c9e`, `00093cc6` stored in turn) and 0 at its `+4`;
3. stores the tables of `PanelHSliderGadget` (`00093cee` at `+0x42`, `00093d06` at `+0x1c`), the state 0, and then `WeaponSliderGadgetVtable` (`00093d4a`) and its mixin table `00093d32`;
4. takes `pbuttons`' frame 26 as the knob's bitmap; the knob's length is 1 and its rect {*x0*, *y0* − 1, *x0* + 1, *y1* + 1}, which the region's rect and the saved top-left start as; allocates the save buffer, the bitmap's columns × rows + `0x20` bytes;
5. builds the bar with `LedBarGraph_Ctor`(0, `0x400`, rect) ([LED bar graphs](cockpit-widgets.md#led-bar-graphs)) at `+0x46`, and with *colors* 0 gives it the shades `0x32` at its `+0x2c` and `0x31` at `+0x30` and the unlit color `0x3f` at `+0x20`;
6. clears `+0x4a` and `+0x7a`, puts the position and the previous position at the rect's *x0*, the maximum at `0x400`, and runs `SliderWidget_RecomputeScaleH`.

*colors* is the energy block's dword at 304, 0 in the eight retail files. ES2's (`00442950`) drops the bar's top one unit below the rect's, colors it `0x20`, `0x22` and `0x2e`, and has no knob bitmap.

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `WeaponSliderGadget_Paint` (`000389ac`) | `+0`, `00442b38` |
| `+8` | `HSliderGadget_InvalidateDeferredWeaponGaugeCopy` (`00038984`), the code of `HSliderGadget_InvalidateDeferred` (`00046c78`): `+0x1b` = 2 | `+4` |
| `+0xc` | `HSliderGadget_OnClick` | `+8` |
| `+0x10` | `WeaponSliderGadget_FlushDeferredPaint` (`00038b30`) | `+0xc`, `00442be8` |
| `+0x14`–`+0x20` | `SliderWidget_GetValueH`, `SliderWidget_SetValueH`, `SliderWidget_DragToPointH`, `SliderWidget_RecomputeScaleH` | the same |

- **`WeaponSliderGadget_Paint` (`000389ac`)**, (bar), when its rect has a corner on screen and its state is not 2: when the knob has moved (its *x0* other than the position), puts the saved screen back (`Region_Restore`(region, `+0x6e`, buffer)); resets the bar (slot `+0xc` of its table at `+0x28`) and paints it to `+0x7a` (slot `+4`), storing `+0x7a` at `+0x4a`; puts the knob's *x0* at the position and its *x1* at the position + the length; makes the display's (`000bdd28`) draw page `+0x3c` its source page `+0x38`, through `00098dd6` and the raster slot at `0009df14` when it is the current display; sets the region's rect to the knob's moved by the canvas context's `+0x220`, `+0x224`; saves the screen under it (`Region_Save`(region, buffer)); blits the knob at its top-left with `Bitmap_Blit`; and keeps that top-left at `+0x6e`. ES2's paints the bar alone, behind the gauge's gates.
- **`WeaponSliderGadget_FlushDeferredPaint` (`00038b30`)**, (bar), acts at a paint counter of 2, when the rect has a corner on screen and the state is not 2: it puts the saved screen back, paints the bar to `+0x7a` only when that differs from `+0x4a`, moves, saves and blits the knob as the paint does, copies {the bar's start `+0x14`, the knob's *y0*, the bar's end `+0x18`, the knob's *y1*} with `Widget_DrawToCockpit`(0, rect, 1), and clears the counter. ES2's runs the paint at a counter of 2.

The mixin table `00093d32` holds `WeaponSliderGadget_PaintThunk` (`00038994`) and `WeaponSliderGadget_FlushDeferredPaintThunk` (`000389a0`), each `ADD dword ptr [ESP+4], -0x3e` and a jump, and `Widget_NoClickSound` by its padding `00031668`, as ES2's (`004433d1`, `004433c7`). The region's three tables hold the module's destructor copies, (object, flags), each storing its table and its bases' in turn and freeing the object when bit 0 of flags is set: `GLRectangleRegion_DtorWeaponGaugeCopy` (`00038954`), `GLRegion_DtorWeaponGaugeCopy` (`0003892c`) and `GLBase_DtorWeaponGaugeCopy` (`00038908`), the code of the gadget file's ([the gadget bases](cockpit-widgets.md#the-gadget-bases)).

**What sets the bar and the knob.** `EnergyWeaponGauge_SetState` ([the energy row](#the-energy-row)) puts the block's `+0` at `+0x7a`, which the bar shows, and hands the block's `+4` to slot `+0x18`, `SliderWidget_SetValueH`, which moves the knob and passes the value through the click slot to `EnergyWeaponGauge_OnChildClick`, which stores it at the gauge's `+0x8f`. A press on the bar starts a drag ([press, release and drag](cockpit-mouse.md#press-release-and-drag)): `SliderWidget_DragToPointH` puts the knob under the pointer, and the release calls the click slot with `SliderWidget_GetValueH`'s value, which `EnergyWeaponGauge_OnChildClick` stores at `+0x8f`, the block's `+4` that the mount's push reads back ([the charge bar's exchange](../../retail/simulation/weapon-firing.md#the-charge-bar)). In the eight retail files the bar's rect starts 40 columns into the row and the number button's ends 8 columns in, so a press on the bar reaches the bar ([Open](#open)).

## The console button — `WeaponRangeSelectGadget`

**Each console button is a captioned box: the chain buttons are their caption, lit when theirs is the current chain; LINK and TRACK are a `pbuttons` picture that shows the press.** The object is `0x3d` bytes: the rect `+0`, the state `+0x17`, the paint counter `+0x1b`, the vptr `+0x1c`, the console at `+0x20` and the mixin's vptr at `+0x24`, the index at `+0x28`, the caption's label at `+0x2c` and the lamp byte at `+0x3c`; ES2's lamp is its `+0x40`.

**`WeaponRangeSelectGadget_Ctor` (`00038ce4`)**, (button, console, rect), allocates `0x3d` bytes when given none, returning 0 when that fails; runs `Widget_CtorRect` on the rect with the button base table's copy `00093b3e` and 0 at `+0x19`; then `PanelSelectGadget`'s tables (`00093b66`, `00093b7e`), the console at `+0x20`, the index and the state 0; then `PanelStateGadget`'s table `00093d76` and the lamp 0; then `WeaponRangeSelectGadgetVtable` (`00093db6`) and its mixin table `00093d9e`. ES2's (`00442c00`) runs the same chain of tables.

| Slot | ES1 | ES2's |
|---|---|---|
| `+4` | `ConsoleButton_Paint` (`00038d90`) | `+0`, `00442c88` |
| `+8` | `Widget_InvalidateDeferredWeaponGaugeCopy` | `+4` |
| `+0xc` | `WeaponRangeSelectGadget_OnClick` (`00038ec0`) | `+8`, `00442dc8` |
| `+0x10` | `WeaponRangeSelectGadget_FlushDeferredPaint` (`00038e88`), ES2's | `+0xc`, `00442d94` |

- **`ConsoleButton_Paint` (`00038d90`)**, (button), when its rect has a corner on screen, writes `STRINGS0.STR` group 4's entry *index* (`000bd2cc`: `I`, `II`, `III`, `LINK`, `TRACK`) into its label with `Label_SetText`: for the chain buttons, with a brush on the stack installed at the render context's `+0x22c` and the label's fill color `0x3f`, in `cpylw` while the lamp is lit and `cpblue` while it is not; for LINK and TRACK, after blitting `pbuttons`' frame 6 + *state* at the button's top-left, so frame 6 up and 7 pressed, in `cppress` while the lamp is lit and `cpon` while it is not. ES2's blits `PWEAPONS`' frame 2 or 3 behind every caption, takes TRACK's from its lamp and skips a button in state 2.
- **`WeaponRangeSelectGadget_OnClick` (`00038ec0`)**, (button, flags), acts only with bit 0 of the flags set, so a right click on a console button does nothing: a chain button's lamp set to 1, then the mixin's click sound (`Widget_ClickSound`) and the console's slot `+4`, `ConsoleButtons_OnChildClick`(console, flags, button). It does not repaint itself; a click's release repaints the button ([press, release and drag](cockpit-mouse.md#press-release-and-drag)). ES2's lights nothing.

The lamps of LINK and TRACK are written by the constructor alone among the functions this batch read, so those two draw `cpon`, and the tracking latch shows as the gunsight's `ATT` rather than on its button. The mixin table `00093d9e` holds `WeaponRangeSelectGadget_PaintThunk` (`00038d78`) and `WeaponRangeSelectGadget_FlushDeferredPaintThunk` (`00038d84`), the same thunks with `-0x20`, and `Widget_ClickSound`, as ES2's (`004433b3`, `004433a9`).

## The module's tables

`00093b3e`, which the three button constructors store first, is the module's copy of the button base table ([`panel.cpp`'s copy](cockpit-panel.md#the-screen-edges-and-the-cursors)); `00093c26`, which the charge bar's stores first, of the slider base's ([the slider base](cockpit-mouse.md#the-slider-base)). Their slot functions are frameless, after `WeaponSelectGadget_Ctor`'s `RET 0x10` and after `WeaponSliderGadget_Ctor`'s, with no Ghidra function:

| Slot | `00093b3e` | `00093c26` | Code | The code of |
|---|---|---|---|---|
| `+4` | `CTLButtonControl_PaintNoOpWeaponGaugeCopy` (`0003820f`) | `CTLSliderControl_PaintNoOpWeaponGaugeCopy` (`000388fd`) | a `NOP` and `RET 4`; three `NOP`s and `RET 4` | `CTLButtonControl_PaintNoOpGadgetsCopy` (`00031523`); `CTLSliderControl_PaintNoOp` (`00043a5e`) |
| `+8` | `CTLControl_InvalidateNoOpWeaponGaugeCopy` (`00038213`) | `CTLSliderControl_InvalidateNoOpWeaponGaugeCopy` (`00038903`) | a `NOP` and `RET 4` | `CTLControl_InvalidateNoOpPanelCopy` (`0002dae3`); `CTLSliderControl_InvalidateNoOp` (`00043a63`) |
| `+0xc` | `CTLControl_OnClickNoOpWeaponGaugeCopy` (`00038217`) | the same, by `00038218` | a `NOP` and `RET 8` | `CTLControl_OnClickNoOpPanelCopy` (`0002dae7`) |
| `+0x10` | `CTLControl_FlushDeferredPaintNoOpWeaponGaugeCopy` (`0003821b`) | the same, by `0003821c` | a `NOP` and `RET 4` | `CTLControl_FlushDeferredPaintNoOpPanelCopy` (`0002daeb`) |
| `+0x14` | `Widget_GetButtonValueWeaponGaugeCopy` (`0003821f`) | `SliderWidget_GetValueH` | a `NOP`, `Widget_ClickButtons` (`00094b84`) in `AX`, `RET 4` | `Widget_GetButtonValuePanelCopy` (`0002daef`) |
| `+0x18` | `CTLControl_SetValueNoOpWeaponGaugeCopy` (`00038229`) | `SliderWidget_SetValueH` | three `NOP`s and `RET 8` | `CTLControl_SetValueNoOpPanelCopy` (`0002daf9`) |
| `+0x1c` | `CTLControl_DragMoveNoOpWeaponGaugeCopy` (`0003822f`) | `SliderWidget_DragToPointH` | a `NOP` and `RET 8` | `CTLControl_DragMoveNoOpPanelCopy` (`0002daff`) |

The other tables reuse these: the derived ones enter the slots at their first byte past the `NOP`. `00093c26` also holds `SliderWidget_RecomputeScaleH` at `+0x20`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorWeaponGaugeCopy`, `ConfigPart_DtorWeaponGaugeCopy`, `ConfigPart_GetClassTagWeaponGaugeCopy`, `TSPartBase_DtorWeaponGaugeCopy` | `00036eb4`, `00036edc`, `00036f1b`, `00036f24` | the run; slots `+0x1c` and `+4` of `00093a06` and `+0x1c` of `00093a3a` |
| `WeaponGauge_Ctor`, `PanelGauge_OnChildClickNoOpWeaponGaugeCopy`, `PanelGauge_KeyDispatchNoneWeaponGaugeCopy`, `WeaponGauge_SetStateNoOp` | `00036f50`, `00036f85`, `00036f8b`, `00036f91` | `0044080c`, `0045233f`, `00452344`, `00443417` |
| `WeaponGauge_MountProjectileType`, `WeaponGauge_MountProjectileSubtype` | `00036f98`, `00036fd0` | `00440a14`, `00440a3c` |
| `EnergyWeaponGauge_Ctor`, `EnergyWeaponGauge_Paint`, `EnergyWeaponGauge_Update`, `EnergyWeaponGauge_SetState`, `EnergyWeaponGauge_GetState`, `EnergyWeaponGauge_OnChildClick` | `0003700c`, `00037144`, `0003716c`, `00037194`, `000372d8`, `000372e4` | `00440a68`, `00440c68`, `00440c90`, `00440d4c`, `00440e74`, `00440ef0` |
| `AmmoWeaponGauge_Ctor`, `AmmoWeaponGauge_Paint`, `AmmoWeaponGauge_Update`, `AmmoWeaponGauge_SetState`, `AmmoWeaponGauge_GetState`, `AmmoWeaponGauge_OnChildClick` | `000373a4`, `00037544`, `000375e4`, `000376b4`, `00037778`, `00037784` | `00440f78`, `004411b4`, `00441268`, `004413f4`, `004414a4`, `004414b4` |
| `PodGauge_Ctor`, `PodGauge_Paint`, `PodGauge_Update`, `PodGauge_SetStateBlock`, `PodGauge_GetStateBlock`, `PodGauge_OnChildClick` | `00037810`, `0003799c`, `00037abc`, `00037b14`, `00037b50`, `00037b5c` | `00441524` and `TogglePodGauge_Ctor` (`00441998`), `0044171c`, `00441850`, `TogglePodGauge_SetStateBlock` (`004419c4`), `00441978`, `TogglePodGauge_OnClick` (`004419fc`) |
| `ConsoleButtons_Ctor`, `ConsoleButtons_SetChainNumber`, `ConsoleButtons_ToggleAutoTrack`, `ConsoleButtons_SetLinkLatch` | `00037bd0`, `00037ce4`, `00037df8`, `00037e0c` | `00441dd0`, `00441f68`, `00441f7c`, `0044202c` |
| `ConsoleButtons_Paint`, `ConsoleButtons_Update`, `ConsoleButtons_SetStateBlock`, `ConsoleButtons_GetStateBlock`, `ConsoleButtons_OnChildClick`, `ConsoleButtons_HandleCommand` | `00037e20`, `00037e4c`, `00037e78`, `00037ed4`, `00037f00`, `00038034` | `00442058`, `0044207c`, `004420a0`, `004420f8`, `0044212c`, `004421a0` |
| `WeaponSelectGadget_Ctor`, the button base table's copies | `000380fc`, `0003820f`–`0003822f` | `004421dc`, [the module's tables](#the-modules-tables) |
| `Widget_InvalidateDeferredWeaponGaugeCopy`, `WeaponSelectGadget_PaintUnderlayThunk`, `WeaponSelectGadget_FlushDeferredPaintThunk` | `00038234`, `00038244`, `00038250` | `Widget_InvalidateDeferred` (`00455524`), `0044340d`, `00443403` |
| `WeaponSelectGadget_PaintUnderlay`, `WeaponSelectGadget_FlushDeferredPaint`, `WeaponSelectGadget_OnClick` | `0003825c`, `000382cc`, `00038304` | `00442394`, `00442424`, `00442458` |
| `ChainedWeaponSelectGadget_Ctor`, `ChainedWeaponSelectGadget_PaintThunk`, `ChainedWeaponSelectGadget_FlushDeferredPaintThunk` | `00038338`, `0003844c`, `00038458` | `00442488`, `004433ef`, `004433e5` |
| `WeaponSelectGadget_Paint`, `ChainedWeaponSelectGadget_FlushDeferredPaint`, `ChainedWeaponSelectGadget_OnClick` | `00038464`, `0003861c`, `0003866c` | `004426c0`, `004428d4`, `00442920` |
| `WeaponSliderGadget_Ctor`, `CTLSliderControl_PaintNoOpWeaponGaugeCopy`, `CTLSliderControl_InvalidateNoOpWeaponGaugeCopy` | `000386a0`, `000388fd`, `00038903` | `00442950`, `CTLSliderControl_PaintNoOp` (`0045571c`), `CTLSliderControl_InvalidateNoOp` (`00455721`) |
| `GLBase_DtorWeaponGaugeCopy`, `GLRegion_DtorWeaponGaugeCopy`, `GLRectangleRegion_DtorWeaponGaugeCopy` | `00038908`, `0003892c`, `00038954` | slot `+0x1c` of `00093c76`, `00093c9e` and `00093cc6` (the first by its padding `00038907`) |
| `HSliderGadget_InvalidateDeferredWeaponGaugeCopy`, `WeaponSliderGadget_PaintThunk`, `WeaponSliderGadget_FlushDeferredPaintThunk` | `00038984`, `00038994`, `000389a0` | —, `004433d1`, `004433c7` |
| `WeaponSliderGadget_Paint`, `WeaponSliderGadget_FlushDeferredPaint` | `000389ac`, `00038b30` | `00442b38`, `00442be8` |
| `WeaponRangeSelectGadget_Ctor`, `WeaponRangeSelectGadget_PaintThunk`, `WeaponRangeSelectGadget_FlushDeferredPaintThunk` | `00038ce4`, `00038d78`, `00038d84` | `00442c00`, `004433b3`, `004433a9` |
| `ConsoleButton_Paint`, `WeaponRangeSelectGadget_FlushDeferredPaint`, `WeaponRangeSelectGadget_OnClick` | `00038d90`, `00038e88`, `00038ec0` | `00442c88`, `00442d94`, `00442dc8` |

## Globals

| | Address |
|---|---|
| `WeaponGaugeVtable` | `00093a6e` |
| `EnergyWeaponGaugeVtable` and `ProjectileWeaponGaugeVtable` | `00093a8e`, `00093aae` |
| `PodGaugeVtable` and `WeaponRangeGaugeVtable`, the console's | `00093ace`, `00093aee` |
| `ConsoleButtons_CommandCodes`, four codes and their four handlers | `00093b1c` |
| `CTLButtonControlVtableWeaponGaugeCopy` and `CTLSliderControlVtableWeaponGaugeCopy` | `00093b3e`, `00093c26` |
| `WeaponSelectGadgetVtable`, `ChainedWeaponSelectGadgetVtable`, `WeaponSliderGadgetVtable`, `WeaponRangeSelectGadgetVtable`, and their mixin tables | `00093bbe`, `00093bfe`, `00093d4a`, `00093db6`; `00093ba6`, `00093be6`, `00093d32`, `00093d9e` |
| `ConsoleButtons_StateBlock`, four bytes | `000b13e8` |

## Open

- **Open:** a write of the pod row's destroyed byte `+0x8c` other than the two above: `es2_xref.py` finds `PodGauge_GetStateBlock` called from `CockpitWidgets_HandleCommand` and `EcmMount_PushGaugeState`, which write nothing through the block it returns, and a store through the gauge's base would not show in that search. Retail check: with the ECM's hardpoint destroyed, the ECM's row still reads `ECM` and still switches.
- **Open:** whether a press on an energy row's charge bar reaches it in retail. The number button's rect ends 8 columns into the row and the bar starts 40 in, in the eight retail files, and the instruments `Cockpit_LoadPanel` registers before the rows, whose constructors are unread, could cover a row. Retail check: dragging the knob along an energy weapon's charge bar moves it and stays where it is let go, and the weapon then charges to a different level.
- **Open:** the sizes of `dba\pbuttons`' frames 0 (the number button's rows), 6 and 7 (LINK and TRACK), 26 (the knob) and 55–57 (the state box), for the port.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with an ammunition weapon on the player's HERC, `BP <CS>:000375e4` fires each frame, and the row's count is drawn in yellow on a plate and changes as the weapon fires. For the console: pressing `` ` `` moves the lit chain button from `I` to `II`, and `Alt+T` puts `ATT` on the gunsight; whether a message comes up on the computer's channel as it does is the check on whether code outside the console posts one.
