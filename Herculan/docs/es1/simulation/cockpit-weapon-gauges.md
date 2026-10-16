# DBSIM.EXE — the weapon rows' gauges

**ES1's energy and ammunition rows are ES2's ([weapon hardpoint rows](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows)): each row is a button that arms its weapon on a left click and adds it to the fire chain on a right click, with a charge bar beside an energy weapon's name and the round count beside an ammunition weapon's.** What a player would see differs: the rows are there from the first frame, with no power-up and no sensor dropout; the round count is always yellow on a plate of palette index `0x3f`, where ES2's turns white or gray; and where each part of a row sits comes from the layout file, where ES2's places them at fixed offsets. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00036eb4`–`00038f0b` and the initialized data starts at `00093a06`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `00036eb4`, `00036edc`, `00036f1b` (frameless, with no Ghidra function) and `00036f24`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `00093a06` and `00093a3a`. The file has no static initializer: the static constructor table's entry after the NAV MAP's `000adc44` is `000adc48`, which holds `0003edac`. A `RET 8` at `00038f06` is followed by three zero bytes and, at `00038f0c`, a lone copy of the 40-byte destructor, which opens the next file, the one holding the constructor `Gau_ShieldDisplayWidget` calls (`00038f34`) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

The file holds, in ES2's order, the row gauge base, the energy and ammunition rows (below), the pod row from `00037810`, the console buttons from `00037bd0`, and the rows' children from `000380fc`; this page covers the code to `0003780d` ([Open](#open)).

## The row gauge base

**`WeaponGauge_Ctor` (`00036f50`)**, (gauge, mount, cockpit, rect), runs `PanelGauge_Ctor`(gauge, cockpit, rect) ([the gauge base](cockpit-widgets.md#the-gauge-base)), stores `WeaponGaugeVtable` (`00093a6e`) at `+0x76` and the mount at `+0x86`, and clears the bytes `+0x7a` and `+0x8a`. ES2's (`0044080c`) also loads the `pweapons` and `wpn_dmg` banks and gives the row its sensor-dropout sequencer and ranges ([the wipes](../../retail/simulation/cockpit-hud-widgets.md#the-wipes)), which ES1's has none of.

`WeaponGaugeVtable` is ES2's `ChainedWeaponGaugeVtable` (`0049c93c`) one slot further on, with 0 at `+8` and `+0xc` where ES2's holds `Rtl_PureVirtualCalled`. Its three functions stand after the constructor, frameless and with no Ghidra function:

| Slot | Function | Code |
|---|---|---|
| `+4` | `PanelGauge_OnChildClickNoOpWeaponGaugeCopy` (`00036f85`) | three `NOP`s and `RET 0xc`, the code of `PanelGauge_OnChildClickNoOp` (`00031395`) |
| `+0x10` | `PanelGauge_KeyDispatchNoneWeaponGaugeCopy` (`00036f8b`) | `NOP`, `XOR EAX,EAX`, `RET 8`, the code of `PanelGauge_KeyDispatchNone` (`0003139b`); the energy and ammunition tables enter it at `00036f8c` |
| `+0x14` | `WeaponGauge_SetStateNoOp` (`00036f91`) | three `NOP`s and `RET 8`, ES2's `00443417`; the pod row's table enters it at `00036f94` |

**`WeaponGauge_MountProjectileType` (`00036f98`)** and **`WeaponGauge_MountProjectileSubtype` (`00036fd0`)**, (gauge), are ES2's (`00440a14`, `00440a3c`) on ES1's offsets: the `int16` `+0` or `+2` of the record at the mount's `+0x20`, or −1 with no record. With no mount they return −1 in `AX` alone. `es2_xref.py` finds one call of each, from `0003ad08` ([Open](#open)).

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
| `+0x8b` | `+0xc2` | the state block, `0x17` bytes: `+0x8b` the value, `+0x8f` the charge bar's position, `+0x9c` destroyed, `+0x9f`–`+0xa1` the three state flags ([the block](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows)) |
| `+0xa2` | `+0xd9` | the row, `int16` |
| `+0xa4` | `+0xdb` | the ammunition row's round-count label |
| `+0xa8`–`+0xb4` | `+0xdf`–`+0xeb` | the ammunition row's value rect |

ES2's energy row also keeps the value its power-up fill ramps toward at `+0xdb`.

## The energy row

**`EnergyWeaponGauge_Ctor` (`0003700c`)**, (gauge, mount, name, row, cockpit, block), allocates `0xa4` bytes when given none, runs `WeaponGauge_Ctor`(gauge, mount, cockpit, block), stores `WeaponGaugeVtable` again, the row at `+0xa2` and then `EnergyWeaponGaugeVtable` (`00093a8e`). With (*x*, *y*) the block's top-left and `CockpitViewInstance`'s root widget as the list:

1. child 0 is `00038338`(0, row, gauge, the block's rect at `+0x10` moved by (*x*, *y*)), stored at `+0x14` and registered with `Widget_RegisterClickable`;
2. the name's first 12 bytes go to `+0x7a` (`strncpy`);
3. child 1 is `000386a0`(0, the block's dword `+0x3c`, gauge, the block's rect at `+0x20` moved by (*x*, *y*)), stored at `+0x18` and registered;
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
- **`EnergyWeaponGauge_OnChildClick` (`000372e4`)**, (gauge, value, child), is ES2's: for child 0, with bit 1 of the value set it calls `0000d5e4`(the cockpit machine's weapon manager `+0x198`, the dword at `+0xa2`), where ES2's calls `WeaponMounts_ToggleChainMember` with the row, and otherwise `CockpitView_ArmWeaponByGauge`(cockpit, gauge); for child 1 it stores the value, clamped to 0..`0x400`, at `+0x8f`. The dword at `+0xa2` holds the row in its low half and runs two bytes past the object.

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
- **`AmmoWeaponGauge_Update` (`000375e4`)**, (gauge), calls child 0's slot `+0x10`; then, with the repaint counter `+0x75` at 2 and the label's rect on screen, it writes the count as the paint does, unless destroyed, and lowers the counter; at 1 it copies the label's rect to the display with `Widget_DrawToCockpit`(0, rect, 0), unless destroyed, and lowers the counter. ES2's runs the same steps behind its update's gates, power-up and sensor dropout, and also raises the counter when child 0's byte `+0x1f` is set.
- **`AmmoWeaponGauge_SetState` (`000376b4`)**, (gauge, block): a new round count goes to `+0x8b` and sets the counter to 2; a new destroyed byte to `+0x9c` and child 0's state to 2 or 0; the three flags to `+0x9f`–`+0xa1`; a byte change repaints child 0. ES2's also sets the counter on a byte change. `AmmoWeaponGauge_GetState` (`00037778`), (gauge), frameless, returns the gauge plus `0x8b`.
- **`AmmoWeaponGauge_OnChildClick` (`00037784`)**, (gauge, value, child), is the energy row's for child 0 alone. ES2's also sets its repaint counter.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorWeaponGaugeCopy`, `ConfigPart_DtorWeaponGaugeCopy`, `ConfigPart_GetClassTagWeaponGaugeCopy`, `TSPartBase_DtorWeaponGaugeCopy` | `00036eb4`, `00036edc`, `00036f1b`, `00036f24` | the run; slots `+0x1c` and `+4` of `00093a06` and `+0x1c` of `00093a3a` |
| `WeaponGauge_Ctor`, `PanelGauge_OnChildClickNoOpWeaponGaugeCopy`, `PanelGauge_KeyDispatchNoneWeaponGaugeCopy`, `WeaponGauge_SetStateNoOp` | `00036f50`, `00036f85`, `00036f8b`, `00036f91` | `0044080c`, `0045233f`, `00452344`, `00443417` |
| `WeaponGauge_MountProjectileType`, `WeaponGauge_MountProjectileSubtype` | `00036f98`, `00036fd0` | `00440a14`, `00440a3c` |
| `EnergyWeaponGauge_Ctor`, `EnergyWeaponGauge_Paint`, `EnergyWeaponGauge_Update`, `EnergyWeaponGauge_SetState`, `EnergyWeaponGauge_GetState`, `EnergyWeaponGauge_OnChildClick` | `0003700c`, `00037144`, `0003716c`, `00037194`, `000372d8`, `000372e4` | `00440a68`, `00440c68`, `00440c90`, `00440d4c`, `00440e74`, `00440ef0` |
| `AmmoWeaponGauge_Ctor`, `AmmoWeaponGauge_Paint`, `AmmoWeaponGauge_Update`, `AmmoWeaponGauge_SetState`, `AmmoWeaponGauge_GetState`, `AmmoWeaponGauge_OnChildClick` | `000373a4`, `00037544`, `000375e4`, `000376b4`, `00037778`, `00037784` | `00440f78`, `004411b4`, `00441268`, `004413f4`, `004414a4`, `004414b4` |

## Globals

| | Address |
|---|---|
| `WeaponGaugeVtable` | `00093a6e` |
| `EnergyWeaponGaugeVtable` and `ProjectileWeaponGaugeVtable` | `00093a8e`, `00093aae` |

## Open

- **Open:** the rest of the file, from `00037810` to `00038f0b`: the pod row (`00037810`, its table `00093ace`, and `00037b14` and `00037b50`, which `CockpitWidgets_HandleCommand` calls), the console buttons (`00037bd0`, `00037ce4`, and `00037e20`–`00038034`, which `es2_naming.py match` pairs with ES2's `ConsoleButtons_*`), and the children from `000380fc`, among them `00038338` and `000386a0`, which the rows build their select button and charge bar with.
- **Open:** `0000d5e4`, which a right click on a row calls in the place of ES2's `WeaponMounts_ToggleChainMember`, and whether it reads only the low half of the dword it is passed.
- **Open:** `0003ad08`, which `es2_xref.py` finds calling the two projectile getters, in the place of ES2's `MfdDisplay_SyncMissileCamMode`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with an ammunition weapon on the player's HERC, `BP <CS>:000375e4` fires each frame, and the row's count is drawn in yellow on a plate and changes as the weapon fires.
