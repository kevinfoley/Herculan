# Borland class descriptors

`DBSIM.EXE` and `VSHELL.EXE` were built with Borland C++, which emits a type-descriptor record for each class into the image. Every record names its class, so these are the **authentic class names**, not invented ones, and the same record gives the object size, the base classes with their subobject offsets, where the vtable pointer sits in the object, and the destructor. DBSIM carries 238 class records and VSHELL 117, plus a few plain-struct records. Template instances have records too, under their full names: `ObjPool<MECH>`, `LoosePool<PROJECTILE>`, `ARRAY<GUN_STATE *>`.

`tools/scripts/es2_classes.py` reads them straight from the shipped executable, so it does not need a Ghidra project. `--vtables` resolves each record to its vtable and `--tree` prints the hierarchy. Per-class findings stay in the docs for those classes. The cockpit widget family is in [`cockpit-input.md`](cockpit-input.md#the-cockpits-own-gadget-classes), and the cut `GRENADE` projectile is in [`../simulation/weapon-damage-types.md`](../simulation/weapon-damage-types.md#type--a-firing-mechanism-selector).

The field order matches the `tpid` type descriptor in Borland's RTL headers. The flags and offsets below were each checked against all 355 class records in both binaries. Where a field has only the header's name, it says so.

## Class record

| Offset | Type | Contents |
|---|---|---|
| `+0x00` | `uint32` | Object size in bytes |
| `+0x04` | `uint16` | Type mask: `3` (struct \| class) for every class record |
| `+0x06` | `uint16` | Offset of the name within the record: `0x30` when the destructor fields are present (flag `0x02`), otherwise `0x20` |
| `+0x08` | `int32` | Offset of the primary vtable pointer within the **object**. `-1` when the class has none |
| `+0x0c` | `uint32` | Class flags, below |
| `+0x10` | `uint16` | Offset of the base-class list within the record |
| `+0x12` | `uint16` | Offset of the virtual-base list within the record |
| `+0x14` | code ptr | Set in 8 of DBSIM's 238 class records and none of VSHELL's: `ACTOR` and the seven classes derived from it, all holding `SimObject_OperatorDelete` (`004270c1`), which takes the object off the global live-object list. The header calls this field the class's `operator delete` |
| `+0x18` | `uint16` | `1` where `+0x14` is set, otherwise `0` |
| `+0x1a`, `+0x1c` | `uint16`, ptr | Zero in every record. The header calls these the array `operator delete[]` counterparts |
| `+0x20`, `+0x24` | `uint32` | Two counts, always equal (1 to 41). The header names them the destructor count and the non-virtual destructor count |
| `+0x28` | code ptr | Destructor |
| `+0x2c` | `uint16` | `1` in every record |
| `+0x2e` | `uint16` | Offset of a third list within the record, laid out like the other two. The header calls it the list of members that need destroying. It is non-empty in 15 DBSIM records and 7 VSHELL records |
| name offset | `char[]` | NUL-terminated class name |

The fields from `+0x20` up exist only in the `0x30` layout. There are 166 such records in DBSIM and 86 in VSHELL.

Each list is a run of 12-byte entries ending in a zero dword:

| Entry offset | Contents |
|---|---|
| `+0x00` | The base's class record |
| `+0x04` | Offset of that base's subobject within the object |
| `+0x08` | `3` in all but five entries, which have `1`. `DVDisplay`'s entry for its struct base `DVDisplayCap` is one of them |

A singly inherited class has one entry, at offset 0. A class with a second base names it and gives its offset directly. Examples: `PanelSelectGadget` has `PanelGadget` at `+0x20`, `PanelHSliderGadget` has `PanelSliderGadget` at `+0x3e`, and `REG_OBJ` has `TSContext` at `+0x0c`. The virtual-base list is empty in both binaries.

The records are byte-packed, not dword-aligned, so a record address is usually odd. **A record's address is where it starts, not where its name is.** `GRENADE`'s record is at `0040acdc`, and its name string is at `0040ad0c`.

### Class flags (`+0x0c`)

| Bit | Meaning | How it was checked |
|---|---|---|
| `0x02` | Has the destructor fields; the name is at `0x30` | Matches the name offset in every record |
| `0x04` | Has bases | Set exactly when the base list is non-empty |
| `0x08` | — | Never set; no class in either binary has a virtual base |
| `0x10` | Has a vtable pointer | Set exactly when `+0x08` is not `-1` |
| `0x20` | Virtual destructor | Set exactly when the `+0x28` destructor appears in the class's own vtable |
| `0x01`, `0x40` | [Open](#open) | `0x40` is set on every record that has a vtable pointer |

The common values are `0x77` (the sim objects, with a virtual destructor), `0x55`/`0x57` (the cockpit and shell UI classes, whose destructor is not virtual) and `0x01`/`0x03` (plain structs with no vtable).

### Struct records

A plain struct has a shorter record, with type mask `1` and the name at `+0x10`: size, mask, name offset, `-1` at `+0x08`, and zero flags at `+0x0c`. It has no lists. DBSIM has four (`DVDisplayCap`, `GLDevBitmap`, `GLDevRegion`, `GLDevEdgeTableRegionData`), and all four appear as the second base of a `DV`/`GL` class. VSHELL has the same four plus `WinBase`.

### Pointer-type records

Borland also emits a record for a class's pointer type. `"ROCKET *"` at `0040ab28` is one: size 4, mask `0x90`, the name at `+0x0c`, and `+0x08` pointing at `ROCKET`'s class record (`0040ab3d`), which follows it directly. `es2_classes.py` lists class and struct records, not pointer-type ones.

## Vtable block

A class's record is linked to its vtable, not to its constructor. The primary vtable sits in a block that starts with a pointer to the record:

| Block offset | Contents |
|---|---|
| `-0x0c` | The class record |
| `-0x08`, `-0x04` | Zero |
| `+0x00` | The primary vtable |
| after it, when the class has a second base with a vtable | Two constants, then that base's vtable |

To turn a vtable found in the disassembly into a class name, read the dword at vtable `-0x0c`. The two zero dwords are what separate a block from a record whose base-list entry happens to hold the same pointer.

**When a class has two bases, the block states its own primary length.** The two constants are the second base's subobject offset, which is the same value as its base-list entry, and the byte offset from the primary table to the second one. The primary slot count is therefore `(second constant - 8) / 4`. The first class to mix in the second base declares the pair, and its subclasses inherit it unchanged, so the pair varies by branch rather than by leaf. DBSIM has 22 blocks with a pair: the cockpit widgets, plus `REG_OBJ` and `TS_REG_OBJ`, whose mixin is at `+0x0c` with the constant `0x1c` over a five-slot table. VSHELL has none. A class with no second base has no pair, so nothing in its block states the primary length.

Blocks are packed end to end. **The word after a table's last slot is usually the next block's record pointer.** It is a valid address that disassembles like code. Establish a table's length from its pair, where it has one, or from its call sites.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A class's record sits immediately before its destructor | That is true of the projectile family (`ROCKET`, `BULLET`, `GRENADE`, `PROJECTILE`). It holds for only 60 of DBSIM's 166 records with a destructor. The `+0x28` field always holds the destructor. |
| After the base pointer comes a fixed tail `0, 3, 0` | That tail is the rest of a single-base list: subobject offset 0, flags 3, and the terminator. A class with a second base has a second 12-byte entry there, holding its offset. |
| The base class's record is found after the name, padded to a dword | This lands on the right place in every record, but it lands on the first base-list entry. The `+0x10` field is what locates the list, and the list can hold more than one base. |

## Open

- **Open:** what the two equal counts at `+0x20`/`+0x24` hold, beyond the header's names for them.
- **Open:** the third list at `+0x2e`, beyond the header's name for it.
- **Open:** what the base-list entry flag `1` means, against the usual `3`.
- **Open:** class flags `0x01` and `0x40`.
