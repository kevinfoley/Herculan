# DBSIM.EXE — `mechsys.cpp`, the chassis records

**`mechsys.cpp` loads the formation table and each chassis' record at mission start, as ES2's does, and its functions stand in ES2's order.** What each does is written where the data it handles is: the chassis record in [`chassis-dat.md`](../formats/chassis-dat.md), the formations in [`formations-dat.md`](../formats/formations-dat.md), the name list in [`nam-name-list.md`](../formats/nam-name-list.md), the gun layout in [the paper doll](paper-doll.md), and the module against ES2's in [the modules ES2 shares](shared-modules.md). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00019854`–`0001a12b`. It opens, after the three zero bytes that end [the HERC's AI](mech-ai.md#where-the-object-file-starts-and-ends), with the run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)): `HeapPtr_DtorMechSysCopy` (`00019854`), to which `es2_xref.py` finds no branch or stored pointer; `ConfigPart_DtorMechSysCopy` (`0001987c`), slot `+0x1c` of `00091d46`, the table `ConfigPart_Ctor` stores; `ConfigPart_GetClassTagMechSysCopy` (`000198bb`); and `TSPartBase_DtorMechSysCopy` (`000198c4`), slot `+0x1c` of `00091d7a`. `Mech_LoadResources` (`000198f0`) asserts `mechsys.cpp`. The static initializer closes it:

- **`Mech_RegisterLoader` (`0001a11c`)**, frameless, the static constructor table's entry `000adc04`: `RegisterSubsystemLoader` (`00000ac4`)(2, `Mech_LoadResources`). ES2's (`00420654`) also builds the texture group slots, which ES1 keeps as loaded ([the sample](shared-modules.md#the-sample)).

Its `RET` at `0001a128` is followed by three zero bytes and a lone copy of the 40-byte destructor at `0001a12c`, which opens the next object file.

## The functions this page names

- **`Shape_RemapTransformId` (`00019aa0`)**, (map, id): ES2's (`0041ff68`): when the `int16` at *id* is not negative it becomes the signed byte *map*[*id*]. `Shape_RemapTransformIds` (`00019ac4`, [the type record](../formats/chassis-dat.md)) calls it for each node field.
- **`MECH_TYPE_DATA_Dtor` (`0001a020`)**, (record, flags): for a record that is not null, `__vec_dtor` (`000610fd`) over the ten `0x18`-byte parts at `+0xd4` with `ConfigPart_DtorThunk`, then `operator_delete` on flags bit 0. ES2's (`00420867`) also counts the live destructors.
- **`ConfigPart_DtorThunk` (`0001a08c`)**, frameless: jumps to slot `+0x1c` of the part its first argument points at, the part's destructor, with the arguments `__vec_dtor` passed.
- **`Mechs_FreeAllTypes` (`0001a054`)**, frameless: `__vec_delete` (`00061088`)(`MECH_TYPE_DATA_Dtor`, `0x1c4`, 7, `g_MechTypeTable` (`000bc444`)), then `g_MechTypeSlotIndex` (`000bc430`) set to `0xff` over its 16 bytes and `g_MechTypeCount` (`000bc42e`) to 0. ES2's (`00420560`) does the same over its `0x21e`-byte records and `0x55`-byte map. `es2_xref.py` finds no branch or stored pointer to it ([Open](#open)).

`0001a020`, `0001a054` and `0001a08c` stand between `ConfigPart_Ctor` (`00019f78`) and `Formation_GetSlotOffset` (`0001a098`), where ES2's `Mechs_FreeAllTypes` stands before `Formation_GetSlotOffset` and its `MECH_TYPE_DATA_Dtor` after `ConfigPart_Ctor`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMechSysCopy`, `ConfigPart_DtorMechSysCopy`, `ConfigPart_GetClassTagMechSysCopy`, `TSPartBase_DtorMechSysCopy` | `00019854`, `0001987c`, `000198bb`, `000198c4` | the run |
| `Mech_LoadResources`, `MechFormation_ElementCtor`, `MechFormation_SlotCtor` | `000198f0`, `000199e4`, `00019a14` | `0041fdb0`, `004207bb`, — |
| `MechTypeSlot_GetIndex`, `MechTypeSlot_Allocate`, `Shape_RemapTransformId`, `Shape_RemapTransformIds` | `00019a30`, `00019a40`, `00019aa0`, `00019ac4` | `0041ff00`, `0041ff10`, `0041ff68`, `0041ff88` |
| `MechType_RemapDetailRootTransforms`, `MechType_InitOne`, `CONNECT_INFO_Ctor`, `Mechs_InitAllTypes` | `00019ba0`, `00019cf0`, `00019ea4`, `00019ee0` | `00420090`, `004201a8`, `004207db`, `0042049c` |
| `MECH_TYPE_DATA_Ctor`, `ConfigPart_Ctor`, `MECH_TYPE_DATA_Dtor`, `Mechs_FreeAllTypes`, `ConfigPart_DtorThunk` | `00019f40`, `00019f78`, `0001a020`, `0001a054`, `0001a08c` | `00420817`, `0042067c`, `00420867`, `00420560`, — |
| `Formation_GetSlotOffset`, `Mech_GetGunLayout`, `Mech_RegisterLoader` | `0001a098`, `0001a0f8`, `0001a11c` | `004205cc`, `00420634`, `00420654` |

## Open

- **Open:** what calls `Mechs_FreeAllTypes` and `HeapPtr_DtorMechSysCopy`, to which `es2_xref.py` finds no branch or stored pointer; ES2's `Mechs_FreeAllTypes` has none found either.
