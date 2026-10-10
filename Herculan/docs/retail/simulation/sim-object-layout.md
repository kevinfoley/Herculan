# Simulation object layout

What a DBSIM simulation object *is* in memory: how the classes derive from one another, how long each one is, and where the shared fields sit.

The field-by-field and slot-by-slot inventories are **not here**. `known_structs.json` owns every field's offset, width and meaning inside an object, and `known_vtables.json` owns which vtable slot means what and which addresses hold a table of that shape; both live under `tools/ghidra_scripts/`. Their schemas, and how they are applied to the Ghidra database, are in [`tools/ghidra_scripts/README.md`](../../../../tools/ghidra_scripts/README.md#knowledge-files-and-the-apply-pipeline) and each file's `_readme`.

## Two hierarchies, one root

Each class's Borland record names the class and its base ([`../formats/borland-rtti.md`](../formats/borland-rtti.md)), so the tree below is the binary's own. `SimObjectBase_Constructor` (`00402188`) builds `TS_OBJ` (`004a0b98`), which the projectile base derives from as well. **The simulation objects and the projectiles share a root class.** That is why `SimObjectVtable`'s first six slots are `ProjectileVtable`'s, and why slots `+0x4`, `+0xc` and `+0x10` hold the same three functions in every table in the file.

```
DRAWABLE (004a14bc)
└── MAT_OBJ (00497289)
    └── TS_OBJ (004a0b98)                ── SimObjectBase_Constructor (00402188)
        ├── PROJECTILE (004987a0)        ── ROCKET, BULLET, BEAM, the cut GRENADE
        ├── EXPLOSION, DEBRIS, SMOKE_BALL, METEOR, FlatObj, FIRE
        └── ANIM_OBJ (004973ac)
            └── DET_ANIM_OBJ (004973cc)  ── SimObjectBase_ConstructAnimated / _ConstructStatic / _ConstructWithDetailTable (0040332c / 00403368 / 004033a4)
                └── ACTOR (0049a54c)
                    ├── MECH  (0049a282) ── Mech_Constructor  (00415bb0)
                    ├── FLYER (0049a5e0) ── Flyer_Constructor (004215f4)
                    └── BASE  (00497940) ── Base_Construct (00405314)
                        ├── RADAR_BASE (004979d4)
                        ├── LC_BASE    (00497784)
                        └── GUN_BASE   (004978ac)
                            └── VEHICLE (00497818)
```

`DRAWABLE` through `DET_ANIM_OBJ`, the projectiles and the effects carry `known_vtables.json`'s five-slot `DrawableVtable` or six-slot `ProjectileVtable`; `ACTOR` and below carry the 34-slot `SimObjectVtable`. The word after a table's last slot is the next block's class-record pointer or data, as [`../formats/borland-rtti.md`](../formats/borland-rtti.md#vtable-block) describes. Each destructor counts the RTL destructor counter down once per class level it inlines — two for `ACTOR`, three for `MECH`, `FLYER` and `BASE`, four for `RADAR_BASE`, `GUN_BASE` and `LC_BASE`, five for `VEHICLE` — which agrees with the records.

The structure branch is the odd one: `Base_Construct` switches on the BASES.DAT type index. **Every branch installs `StructureVtable` first and then overwrites it**, which is what establishes the four others as derived from it — and the ground vehicle branch installs three in a row, so that class is two levels down. All five are the same 34-slot shape. Which three slots differ across them, and what separates the armed classes from the unarmed ones, is [`structure-behaviour.md`](structure-behaviour.md#five-classes-one-switch)'s.

## Sizes come from the class records

Each class's Borland record states its length ([`../formats/borland-rtti.md`](../formats/borland-rtti.md)). None of these classes is allocated with a literal `operator new` size. Each is drawn from a free-list pool: `Pool_Init` (`004719cc`) takes `(pool, count, elementSize)` and allocates `count * (elementSize + 8)`; the allocator `Pool_Alloc` (`00471a24`) pops a node and returns `node + 8`. The mech and flyer pools each hold one class, and their element size is that class's length. **The structure pool holds all five structure classes**, so its element is longer than four of them:

| Class | Record length | Pool element | Pool created at | Pool global |
|---|---|---|---|---|
| `MECH` | `0x36a` (874) | `0x36a` | `00425185`, in `DBSim_LoadScriptDat` | `004a9bfe` |
| `FLYER` | `0x291` (657) | `0x291` | `00425220`, same function | `004a9e3d` |
| `BASE`, `RADAR_BASE` / `GUN_BASE` / `VEHICLE` / `LC_BASE` | `0x209` / `0x220` / `0x229` / `0x266` | `0x26d` (621) | `00405e1e`, in `Structure_InitPool` (`00405df4`) | `004a9624` |

The pool globals are zero in the image and filled in at load, so the size is not visible at the allocation site — `MOV EAX,[0x004a9bfe]` there loads the *pool pointer*. Follow the write to the global to find the size.

From `+0x209` the structure classes part ways: `GUN_BASE` keeps its turret angles and its refire and firing-window countdowns there, `VEHICLE` adds its drive speed and back-off from `+0x220`, and `LC_BASE` holds three `0x1f`-byte weapon stations, `0x209` to `0x266` ([`structure-behaviour.md`](structure-behaviour.md#five-classes-one-switch)).

**A flyer is not a shortened mech.** Two pools, two lengths, two constructors, and the flyer starts its own fields at `+0x1fa` where the mech starts at `+0x1f2`. They are siblings.

## Only the short-lived classes are recycled

`Pool_Init` (`004719cc`) `memset`s a pool once at startup and `Pool_Alloc` (`00471a24`) hands back a node without zeroing it, so a recycled slot carries its predecessor's bytes. The only route back into a pool is `Pool_Free` (`00471abc`), whose sole caller is `ObjectPool_FlushDeleteQueue`. The mech pair's flush — registered as subsystem phase 5 and so run once a frame — drains the queue at `g_MechDeleteQueue` (`004a9c02`), of which an absolute-address search of the disassembly finds one write, the one-time setup in `DBSim_LoadScriptDat`, and one read, the flush itself. A queue append goes through `ObjectPool_QueueForDelete`, which takes the queue as an argument, so a caller holding a copy of the pointer would not show in that search. Whether anything queues a machine, and so whether a machine's slot is ever reissued with its predecessor's bytes, is [Open](#open).

The short-lived classes are recycled every frame — explosions, debris, fires, drop pods and ground shapes — and those do inherit a predecessor's stale fields.

## Where the base ends — `0x1f2`

`ACTOR`'s record gives `0x1f2`. `SimObject` is never allocated on its own, and the derived classes' constructors agree with the record: each starts writing fields nothing else has at or past that line.

- `Mech_Constructor` stores the HERCS.DAT record at `+0x1f2` and allocates the component-damage header at `+0x206`.
- `Base_Construct` stores the BASES.DAT record at `+0x1f2`, the alive-flag array at `+0x201` and the state array at `+0x205`.
- `Flyer_Constructor` stores its type record at `+0x1fa` and its damage header at `+0x200`.

Below that line all three constructors write an *identical* block — `+0x1a8 = 0xffff`, `+0x98 = 1`, `+0xa7 = 1`, `ObjectList_Add`, then `+0x1b6`, `+0x1b2`, `+0x1be`, `+0x1bc`, `+0x1ba` — which is what identifies the layout as shared rather than three classes coincidentally agreeing.

## The object's frame is a transform, and its position is that transform's translation

`obj+0x12` is a complete 32-byte transform record of the kind `Transform_Concat` (`0047f914`) composes: nine Q14 `int16` matrix entries, a rank byte at `+0x12`, and an `int32` translation at `+0x14`/`+0x18`/`+0x1c`. Laid at `obj+0x12`, that translation falls at `obj+0x26`/`+0x2a`/`+0x2e` — **which is the object's world position**. The two are the same storage, not a copy: `SimObject_InstallModelTransform` (`00401fe4`) builds the matrix half from the euler angles at `obj+0x0c` whenever the frame-valid word at `obj+0x32` is 0, and sets it to 1, and every caller that wants a position passes `obj+0x26` as an `int[3]`.

## The two per-object tables are 112 rows each

`obj+0xc2` is the contact table and `obj+0x132` the line-of-sight cache, both flat byte arrays indexed by the *other* object's `listIndex` (`obj+0x4b`, assigned by `ObjectList_Add`). Neither has a length written down anywhere; both are `0x70` = 112 bytes, from the gap between them, corroborated by the identical `0x70` gap from `+0x132` to the next member at `+0x1a2`. **112 is therefore the simulation's object cap.**

## Countdowns keep their counter one byte past the record

There are two countdown records, and both are addressed by a pointer to a leading byte the tick never touches:

| Record | Size | Counter | Stepped by |
|---|---|---|---|
| `CountdownTimer` | 3 | `short` at `+0x01` | `Math_CountdownTimerTick` (`00467944`) |
| `LongCountdownTimer` | 5 | `int` at `+0x01` | `Timer_CountDown` (`004679a4`) |

Nothing in the field itself says which flavour it is — only which of the two functions is called on it does. A mech carries seven of the short kind on a regular stride of 3, `+0x258` to `+0x26c`, then four of the long kind on a stride of 5, `+0x26d` to `+0x280`, and one more short at `+0x285`; the AI behaviour block's dwell countdown is a long one at `+0x4d`+`0x04`. `known_structs.json` records each record's base and width.

| Record | Kind | What it times | Owner |
|---|---|---|---|
| `+0x258` | short | missile subtype 0's lock countdown | [`missile-lock.md`](missile-lock.md#the-mechanism) |
| `+0x25b` | short | subtype 1's | the same |
| `+0x25e` | short | subtype 2's (ARM) | the same |
| `+0x261` | short | subtype 3's slot, placed on the run's stride; the lock block never touches it | the same; [Open](#open) |
| `+0x264` | short | subtype 4's | the same |
| `+0x267` | short | the gap between ECM spoof rolls against a jamming target | [`missile-lock.md`](missile-lock.md#ecm) |
| `+0x26a` | short | how long the radar is held off after an anti-radiation hit | [`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set) |
| `+0x26d` | long | the post-collision unstick manoeuvre | [`ai-navigation.md`](ai-navigation.md#the-unstick-manoeuvre) |
| `+0x272` | long | the retarget cooldown after taking fire | [`ai-targeting.md`](ai-targeting.md#mech-fields-this-slice-owns) |
| `+0x277` | long | the friendly-fire complaint cooldown | the same |
| `+0x27c` | long | the under-fire window; its expiry clears the damage accumulator at `+0x281` (an `int`, not a timer) | the same |
| `+0x285` | short | the window `damageTaken` (`+0x288`) accumulates over; each expiry zeroes it | [`ai-combat-states.md`](ai-combat-states.md) |

**This is why almost every doc cites one of these fields one byte above its record.** `mech+0x26b` is the *counter* of the timer based at `+0x26a`, and `mech+0x52` is the counter of the block's dwell timer at `+0x51`. Both spellings name the same storage; only the second is a record you can call the tick on.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A field the scalar search cannot find is unused | Every reader of the flag bytes from `obj+0x92` up materialises that address first (`LEA ECX,[EBX + 0x92]`) and then uses a small displacement off it — `destroyed` is read as `[EDX + 0x7]`, `+0xa7` written as `[EAX + 0x15]`. `ES2FindFieldRefs` on `0x95`, `0x99`, `0xa1`, `0xa5` or `0xa7` returns **zero sites in the whole binary**, and all five are live fields. Search for the base offset the `LEA` uses, not the field's own, or use `tools/scripts/es2_fieldscan.py`, which resolves the idiom. |
| The offset a doc cites is the start of the field | For a countdown it is the counter, one byte into the record — see above. Laying a `CountdownTimer` at the cited offset puts every subsequent field three bytes out. |
| A code address after a vtable's last slot is a 35th slot | `ES2DumpVtable` resolves and disassembles any valid address. The word past the end is usually the next vtable block's class-record pointer ([`../formats/borland-rtti.md`](../formats/borland-rtti.md#vtable-block)): `0046b7c8` and `0040c3d8` both look like code and are neither functions nor slots. Check for a prologue *and* for a real call site. |
| The allocation site's argument is the object's size | It is the pool pointer. See "Sizes" above. |
| The structure pool's element size is a structure's length | One pool serves all five structure classes, whose records give `0x209` to `0x266`. The turret fields a `GUN_BASE` keeps at `+0x209` are, on an `LC_BASE`, the start of its first weapon station. |
| A mech's length can be inferred from the highest documented offset | The highest offset anyone has written down is a lower bound that moves every time someone reads another function. `0x36a` is a fact about the binary. |

## Open

- **Deferred:** whether anything queues a machine onto `g_MechDeleteQueue` (`004a9c02`). An absolute-address search of the disassembly finds only the setup write in `DBSim_LoadScriptDat` and the read in `Mech_FlushDeletes` (`0041544c`); a caller passing a copy of the pointer to `ObjectPool_QueueForDelete` is outside that search.
- **Deferred:** why the structure pool's element is `0x26d`, seven bytes past `LC_BASE`'s `0x266`, the longest structure class.
- **Deferred:** what `obj+0x92` is in the source. Whether it is a sub-object the compiler is addressing or just a base register it chose is not settled, so `known_structs.json` places those bytes at their absolute offsets rather than inside an invented struct.
- **Deferred:** what vtable `+0x0c` is for. Every `DrawableVtable`, `ProjectileVtable` and `SimObjectVtable` table holds `Stub_ReturnZero` (`004785bf`) there.
- **Deferred:** whether `mech+0x261`, the fourth lock-timer slot, is used at all. `Mech_PerTickSystemsUpdate` ticks the other four by name and no tick names this one, and `es2_fieldscan.py` over `00402000`-`00430000` finds no mech access to it (the one write it reports is `Flyer_Constructor`'s, a different class's field). A field that carries a value is never proven unread by a scan.
