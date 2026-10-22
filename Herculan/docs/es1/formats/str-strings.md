# DBSIM.EXE — the simulator's string table

**ES1's simulator reads its cockpit captions, names and labels from `str\STRINGS0.STR` once, as ES2's does: 35 groups in file order, each into an array of string pointers that the cockpit's screens index.** ES1's file has ES2's [layout](../../retail/formats/str-strings.md#layout); it has 35 groups where ES2's has 41, each registered with exactly its own size, and its groups differ from ES2's in the places the table below marks. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's, described in ES2's [`.STR` page](../../retail/formats/str-strings.md). Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `0002fe5c`–`000301eb` and the initialized data `0009326c`–`000932e1`, between the message boxes' file ([where it ends](../simulation/cockpit-messages.md#where-the-object-file-starts-and-ends)) and the paper doll's ([where it starts](../simulation/paper-doll.md#where-the-object-file-starts-and-ends)). It carries no copy of `ConfigPart`'s tables and names no source path. The code:

- **`HeapPtr_DtorSimStringsCopy` (`0002fe5c`)**, (p, flags): byte for byte the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](compiler.md#vtables)), here with no run after it. `es2_xref.py` finds no reference to it ([Open](#open)).
- **`SimStrings_LoadAll` (`0002fe84`)**, (path): [below](#the-load).
- **`SimStrings_StaticInit` (`000301d0`)**, entry `000adc34` of the static constructor table, frameless with no Ghidra function: `StringTable_Ctor`(`SimStringTable`, `000b0f04`). ES2's (`00437898`).
- **`SimStrings_StaticDtor` (`000301dc`)**, entry `000adcb8` of the exit table, frameless with no Ghidra function: `OwnedArray_Dtor`(`SimStringTable`, 2), ES2's (`004378c8`). Its `RET` at `000301e8` is followed by three zero bytes ([where object files meet](compiler.md#where-object-files-meet-in-the-code)).

The data holds the cockpit's light names: `WPN_LED1`, `THR_LED1`, `THR_LED2` and two empty strings, listed by the five pointers at `0009328c`, and an empty string, `ENG_CLP1`–`ENG_CLP4` and another empty string, listed by the six at `000932c8`. ES2's file holds the same names before its `PBUTTON*` names, in one table at `0049bb20`. `es2_xref.py` finds `0009328c` at `0003b45d` and `0003bc09`, in `ThrottleSlider_CtorV` and `ThrottleSlider_CtorFixed`, which load `dba\<name>` as the throttle's bar pictures, the name the one the `.GAU` block's `int32` at 1064 indexes: `THR_LED1` or `THR_LED2` in the retail files ([the throttle gauge](../simulation/cockpit-throttle-gauge.md#building-it)); and `000932c8` at `000398de` ([Open](#open)).

## The load

**`SimStrings_LoadAll` (`0002fe84`)**, (path), ES2's (`00437598`): `StringTable_LoadFromFile`(`SimStringTable`, path), then one `StringTable_LoadGroup`(`SimStringTable`, attributes, count, destination) per group in the order of the table below, the attribute array non-null for group 0 alone. `ColorSchemePanels_LoadAll` passes it `str\strings0` each time it runs ([building the cockpit](../simulation/cockpit-panel.md#building-it)). Each count passed equals the group's size in the retail file, where ES2's are capacities that can exceed it; ES1's `StringTable_LoadGroup` ignores the argument and writes as many entries as the group's own count.

## The groups

The retail `STRINGS0.STR` walks to its declared content length of 2,133 bytes in these 35 groups. The last column is the ES2 group holding the same strings, where there is one.

| Group | Count | Array | Strings | ES2's | `es2_xref.py` finds the array at |
|---|---|---|---|---|---|
| 0 | 18 | `SquadOrderStrings` (`000bd228`), and in `SquadOrderAttributes` (`000bd270`) a pointer to one attribute byte each | the squadmate orders | 0 | [the FLASH COMM screen](../simulation/mfd-flash-comm.md#painting): `00035dea`, `00035e97`, `00035f0a`; `0003f75f`, `0003f7fa`, `00040a04`; the attributes beside each |
| 1 | 3 | `000bd2b8` | `RED1`–`RED3` | 1 | the load alone |
| 2 | 1 | `000bd2c4` | `OFFLINE` | 2 | `00037a2d`, `00038516` |
| 3 | 1 | `000bd2c8` | `ECM` | — | `000378ff` |
| 4 | 5 | `000bd2cc` | `I`, `II`, `III`, `LINK`, `TRACK` | — | `00038db2` |
| 5 | 13 | `000bd2e0` | `STATUS`, `FLASH COMM`, `NAV MAP`, `SCANNER`, `TARGET`, `MISSILE CAM`, `MODE`, `MODE`, `RANGE`, `TARGET`, `XMIT`, `UP`, `DN` | — | `0003af42`, `0003af92` |
| 6 | 4 | `000bd314` | `R1`, `R2`, `R3`, `ALL` | — | the load alone |
| 7 | 3 | `SquadCallsignStrings` (`000bd324`) | `RED 1`–`RED 3` | 7 | `0002b484` |
| 8 | 3 | `000bd330` | `XMIT`, `CANCEL`, `EXIT` | 9 | `0003ebc4` |
| 9 | 5 | `000bd33c` | `OK`, `INT DMG`, `SHLD DWN`, `CRITICAL`, `WASTED` | 10 | the load alone |
| 10 | 2 | `000bd350` | `MAP`, `DAMAGE` | 11 | `0003d9d6` |
| 11 | 3 | `000bd358` | `" STRUCT DAMAGE"`, `" INTERN DAMAGE"`, `" WEAPON DAMAGE"` | 12 | `0003d9ea` |
| 12 | 19 | `000bd364` | the structural components, `F COCKPIT ARMOR` to `RR FOOT ARMOR` | — | `00042410`, `00042641` |
| 13 | 12 | `000bd3b0` | the internal systems, `L LEG ACTUATOR` to `RR LEG ACTUATOR` | — | `00042426`, `0004267d` |
| 14 | 1 | `000bd3e0` | `YOU` | 17 | `0003ca1c`, `00058c72` |
| 15 | 1 | `000bd3e4` | `TARGET` | — | `0003ca6e` |
| 16 | 2 | `000bd3e8` | `NO TARGET SELECTED`, `NO INFO AVAILABLE` | 19 | `0003caa2`, `0003cd5e`, `00073eea` |
| 17 | 1 | `000bd3f0` | `TARGET:` | — | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `00033110`, `000331ff`, `0003355b`, `00033610`, `0003366b` |
| 18 | 1 | `000bd3f4` | `STATUS:` | 21 | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `0003324b` |
| 19 | 16 | `000bd3f8` | the HERC type names, `ROADRUNNER` to `ARACHNITRON` | — | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `0003323d` |
| 20 | 9 | `000bd438` | the structure type names, `SUPPLY DEPOT` to `GUN TOWER` | — | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `0003356c` |
| 21 | 2 | `000bd45c` | `LANDSKIMMER`, `HOVERTANK` | 25 | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `0003361e` |
| 22 | 1 | `000bd464` | `NONE` | 26 | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `0003311e` |
| 23 | 1 | `000bd468` | `UNKNOWN` | 27 | [`MfdStatusScreen_Paint`](../simulation/mfd-status-screen.md#the-paint): `00033679` |
| 24 | 5 | `000bd46c` | `OK`, `SHIELDS DN`, `INT DAMAGE`, `CRITICAL`, `DESTROYED` | 28 | [`MfdStatusScreen_SetCondition`](../simulation/mfd-status-screen.md#the-condition-line): `000336d3`; `0003e017` |
| 25 | 2 | `000bd480` | `ACT`, `PASS` | 29 | [the scanner's paint](../simulation/mfd-scanner.md#the-paint): `00035686` |
| 26 | 1 | `000bd488` | `RNG:` | — | [the scanner's paint](../simulation/mfd-scanner.md#the-paint): `00035b42` |
| 27 | 1 | `000bd48c` | `DST:` | — | [the scanner's paint](../simulation/mfd-scanner.md#the-paint): `00035ab9` |
| 28 | 4 | `000bd490` | `SELECT PILOT`, `SELECT COMMAND`, `DESIGNATE LOCATION`, `DESIGNATE TARGET` | — | `0004009e`, `000405f7`, `00040882` |
| 29 | 2 | `000bd4a0` | `STATUS:`, `OBJECTIVE:` | 33 | `0003dffc` |
| 30 | 1 | `000bd4ac` | `OFFLINE` | — | `0003a26d`, `0003a355` |
| 31 | 5 | `000bd4b0` | `MISS`, `READY TO`, `LAUNCH`, `NONE`, `LOCK` | — | the load alone; [the missile camera's paint](../simulation/mfd-missile-camera.md#painting) reads entries 1 to 4 at their own addresses, `000bd4b4`–`000bd4c0`: `00036818`, `00036826`, `00036836`, `000368b0` |
| 32 | 4 | `000bd4c4` | `VIEW: `, `CONTROL: `, `CAMERA`, `HERC` | 36 | `00058ca0`, `00058d1b` |
| 33 | 1 | `000bd4a8` | `ATT` | — | `00034f1f`, `00035074` |
| 34 | 8 | `000bd4d4` | `ATTACK`, `TRAVEL`, `PATROL`, `FORM UP`, `GUARD`, `FLEE`, `DEAD`, `IMMOBILE` | 40 | `0003e03d` |

The arrays lie in registration order from `000bd228` to `000bd4f3`, except group 33's, which fills the dword between group 29's and group 30's. ES1's file has no group for ES2's `HQ` (8), the flyers' component lists (14, 16), the vehicle type names (24) or the gunsight's `TIME:` and `SPEED:` (38, 39); ES2's group 4 holds an unused entry, `LINK` and `TRACK` and takes the chain button's numerals from a table in its data, where ES1's group 4 holds the numerals.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorSimStringsCopy` | `0002fe5c` | the code of `HeapPtr_DtorPanelCopy` (`0002acdc`) |
| `SimStrings_LoadAll` | `0002fe84` | `00437598` |
| `SimStrings_StaticInit`, `SimStrings_StaticDtor` | `000301d0`, `000301dc` | `00437898`, `004378c8` |

## Globals

| | Address |
|---|---|
| `SimStringTable`, the `0x10`-byte string table | `000b0f04` |
| the group arrays | [the groups](#the-groups) |

## Open

- **Open:** what reads the arrays of groups 1, 6 and 9: `es2_xref.py` finds `000bd2b8`, `000bd314` and `000bd33c` in `SimStrings_LoadAll` alone, and a grep of the disassembly for their later entries' addresses as operands finds none, where it finds group 2's two reads and group 31's four; an access through a neighboring array's base would show in neither. The other arrays' readers are the addresses in [the groups](#the-groups), unread but for the status screen's and the MFD screens', and which screen each caption belongs to follows from reading them.
- **Open:** what reads the light names at `000932c8` (`000398de`, unread), whether anything loads `WPN_LED1`, entry 0 of `0009328c`, which no retail `.GAU` selects, and what calls `HeapPtr_DtorSimStringsCopy`, to which `es2_xref.py` finds no reference.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0002fe84` fires as the cockpit is built, and after it returns the dword at `000bd2e0` points at the text `STATUS`.
