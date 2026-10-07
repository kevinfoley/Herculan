# `dmg\<NAME>.DMG` — the per-chassis component table

Reverse-engineered from `DBSIM.EXE`; addresses are DBSIM virtual addresses. `Mech_Constructor` (`00415bb0`) and `FlyerType_LoadResources` (`00422ed0`) call `Damage_LoadMechDmgFile` (`0040d160`), which builds the filename from the machine's own name string and hands the stream to `HercPiece_LoadTable` (`0040d09c`). The file gives each of a machine's components its armour, its parent, what it throws when it goes and which internals sit behind it, and gives each internal its maximum. What the simulator does with it — the arrays it fills in, the spill, the cascade — is [`../simulation/component-damage.md`](../simulation/component-damage.md).

Retail ships 22 files, one per chassis: 21 carry 22 internals and 29 components, and `SKIMMER`'s carries 1 and 1.

## Layout

After the [VOL entry prefix](vol-archive.md#the-per-entry-prefix--fixed-9-bytes), with no padding anywhere:

```
int16 subCount,   subCount * int16 internal maximum
int16 pieceCount, pieceCount * piece
piece = 8 bytes, then dependentCount * 4 bytes of dependent list
```

`HercPiece_ReadRecord` (`0040cff8`) reads the 8 bytes into the front of an 18-byte in-memory record, reads the list into a buffer of its own and stores the pointer at `+0x08`, and fills `+0x0c` and `+0x0e` itself, so a piece is 8 + 4n bytes on disk and 18 in memory. Only internal slots 0–11 carry a nonzero maximum in retail, and no piece lists an index above 11. The ten mount pieces, 19–28, are placeholders in every mech file — armour 1 and no internals — because a fitted weapon replaces its piece at load ([below](#a-fitted-weapon-replaces-its-mounts-piece)).

## The piece record

| Offset | Field | Evidence |
|---|---|---|
| `+0x00` | `int16` `Armor`, the component's own maximum | `Component_ReadDamagePercent`'s `local_10 = *psVar5` |
| `+0x02` | `int8` — the debris group this component throws, `-1` = fall back to group 2. See [`destruction-effects.md`](../simulation/destruction-effects.md#the-two-database-index-space) | `Component_DestroyAndCascade`, on destruction |
| `+0x03` | `int8` — the `TSCellAnimPart` sequence this component drives on the machine's shape, stepped to its blank cell on destruction (`*(int*)(mechThis+0x34)+8`, `[index] = 2`), `-1` for a component with no geometry of its own. It also gates the fire — see [`mech-shape-drawing.md`](mech-shape-drawing.md) | `Component_DestroyAndCascade`, guarded by `-1 < value` |
| `+0x04` | `int8` — the **index of the parent component** this one hangs off, `-1` for none. Destroying component *n* queues every still-live piece whose parent is *n* | `Component_DestroyAndCascade`'s trailing loop |
| `+0x05` | `uint8` flags, below | `Component_ApplyDamageAndCascade`, `Component_DestroyAndCascade` |
| `+0x06` | `int16` dependent count | `HercPiece_ReadRecord`, `Component_ReadDamagePercent`'s loop bound |
| `+0x08` | pointer to the dependent list, read in place after the first 8 bytes. Four bytes an entry: `int16` **spill weight** at `+0`, `int16` internal index at `+2` | `Component_SpillIntoDependents`, `Component_ReadDamagePercent` |
| `+0x0c` | `int16` shape part id the component's position is taken on (runtime-only): the loader writes `0xffff` and the `.COL` binding below fills it in | `HercPiece_ReadRecord`, `Mech_ComponentPosition` (`0041b60c`) |
| `+0x0e` | pointer to the point that position is taken at (runtime-only): the loader writes null and the `.COL` binding fills it in | `HercPiece_ReadRecord`, `Mech_ComponentPosition` |

**The flags at `+0x05`:**

| Bit | Meaning |
|---|---|
| 0 | The piece runs its destruction at all. `Component_ApplyDamageAndCascade` calls `Component_DestroyAndCascade` only for a piece with this bit set, and that call clears the active flag, throws the debris and cascades to every live piece whose parent (`+0x04`) names it. A piece without it (the torso, in 20 of the 21 files) reads fully damaged once its armour and internals are gone and does none of that |
| 1 | Whole-machine fire: shape 0 replaces every fire already on the machine — [`destruction-effects.md`](../simulation/destruction-effects.md#who-catches-fire) |
| 3 | With bit 1 clear, the component's own fire, shape 2 — [`destruction-effects.md`](../simulation/destruction-effects.md#who-catches-fire) |
| 2 | The piece's explosion is type `0x11` rather than 10, and sets the cascade's latch — [`destruction-effects.md`](../simulation/destruction-effects.md#a-machines-component) |

The fires are [`destruction-effects.md`](../simulation/destruction-effects.md#fire)'s. Retail files carry 0, 1, 3, 5, 7 and 9: 7 is the front cockpit on 18 chassis, 9 is both weapon brackets on 13, 1 is the leg chains and the rear cockpit on most, and 5 and 3 are one component each in MAVERICK, OUTLAW, SPIDER and RAMSES.

**The position binding.** Neither runtime-only field is in the file. `Mech_ConfigureLoadout` (`004175dc`) fills them in from the `.COL`: `Collision_CollectComponentAnchors` (`0040cb84`) walks every cluster and emits `(componentIndex, nodeIndex, &cluster.boundCentre)`, and `HercPiece_BindComponentAnchors` (`0040d284`) writes each triple into the piece its component index names. The write is unguarded, so **the last cluster naming a component wins**; every retail mech `.COL` names each component exactly once, so it never bites. The point is the cluster's load-time *bounding-sphere* centre, not any one sphere. What reads it, and what a piece with no cluster falls back to, is `Mech_ComponentPosition` in [`damage-system.md`](../simulation/damage-system.md#where-a-component-stands--the-0x58-slot).

## The two index spaces

The file names nothing. The labels below are the conventional ones, and what confirms them is the structure — the parent chains (`+0x04`) and what [`Mech_ComponentDamageWrite` reads by literal index](../simulation/component-damage.md#slots-the-write-path-reads-by-index).

**Components** (the 29 pieces):

| Index | Component |
|---|---|
| 0, 1 | front and rear cockpit |
| 2, 3 | left and right shoulder |
| 4, 5 | left and right weapon bracket |
| 6 | torso |
| 7, 8 | left and right upper leg |
| 9, 10 | left and right lower leg |
| 11, 12 | left and right foot |
| 13–18 | the rear pair's three pieces on a four-legged chassis, carrying armour 1 elsewhere: left 13, 15, 17 and right 14, 16, 18 |
| 19–28 | the ten weapon mounts. A mount occupies its `.GL` record's `+0x17` plus 19 |

The leg chain runs upper → lower → foot through the parent index: ACHILLES' left leg is 7 → 9 → 11 and its right 8 → 10 → 12. Its mount components 19, 21 and 24 hang off bracket 5, 20, 23 and 25 off bracket 4, and 22 off the front cockpit; 26–28 hang off nothing. SPIDER sets `-1` throughout, so nothing on it cascades.

**Internals** (the 22 slots):

| Index | Internal |
|---|---|
| 0, 1 | left and right leg servos |
| 2 | sensor array |
| 3 | targeting computer |
| 4 | shield generator |
| 5 | reactor |
| 6 | hydraulics |
| 7 | stabilisers |
| 8 | life support |
| 9 | pilot |
| 10, 11 | left and right rear leg servos, on a four-legged chassis |
| 12–21 | the weapon mounts' internals, fit slot *n* at 12 + *n*: maximum 0 in every file, set by a fitted weapon ([below](#a-fitted-weapon-replaces-its-mounts-piece)) |

ACHILLES' maxima are 2500 for each servo pair, 800 for the sensor array and targeting computer, 500 for the next five and 50 for the pilot.

## Which internals each component holds

Each piece's dependent list, read from the 22 shipped files. Twelve chassis — ACHILLES, APOCA, CERBERUS, COLOSSUS, DIABLO, HYPERION, MIRIMAC, MONGOOSE, OGRE, OUTLAW, RAPTOR2 and SCARAB — share one map:

| Component | Internals, by index |
|---|---|
| 0, front cockpit | sensor array (2), targeting computer (3), shield generator (4), stabilisers (7), life support (8), pilot (9) |
| 1, rear cockpit | 2, 3, 4, 7, 8 — the front's list without the pilot |
| 4, 5, weapon brackets | hydraulics (6) |
| 6, torso | reactor (5), hydraulics (6) |
| 7, 9, 11, left leg | left leg servos (0) |
| 8, 10, 12, right leg | right leg servos (1) |
| everything else | nothing |

Every entry weighs 20 in the spill draw except the pilot's, which weighs 1 or 5. With five other internals at 20 behind the front cockpit, a pilot at 1 takes about one draw in a hundred and one at 5 about one in twenty-one. The pilot weighs 5 on APOCA, COLOSSUS, MAVERICK, OGRE, OUTLAW, RAPTOR2, RAZOR, SAMSON and TOMAHAWK, and 1 on the rest that carry it.

The other chassis depart from it:

- **HEADHUNT, MAVERICK, RAMSES, STINGRAY, TOMAHAWK** — the weapon brackets hold nothing, so hydraulics sit behind the torso alone.
- **SAMSON** — the torso holds the reactor only, so hydraulics sit behind the brackets alone.
- **PITBULL** — the front cockpit holds all eight systems, 2 to 9, with the reactor and hydraulics weighted 30; the rear cockpit, brackets and torso hold nothing. Its rear legs are components 13, 15, 17 (left, rear servos 10) and 14, 16, 18 (right, rear servos 11).
- **SPIDER** — the same four leg chains as PITBULL, and a front cockpit holding the standard list without the pilot; nothing else. Every internal it has is 100 points.
- **RAZOR** — the front cockpit holds the sensor array (50), targeting computer (10), life support (30) and pilot (5); the brackets hold the stabilisers (40); the torso holds the shield generator and reactor (40 each); only components 7 and 8 hold the leg servos (50). Its two servo maxima are 0.
- **SKIMMER** — one component, holding its one internal.

## A fitted weapon replaces its mount's piece

Every sim `WEAPONS.DAT` template opens with a piece record of this format, read by the same `HercPiece_ReadRecord` ([`weapons-dat-sim.md`](weapons-dat-sim.md#weaponmounttemplate-record-variable-length)), and `Mech_ConfigureLoadout` (`004175dc`) puts it in place of the mount's own. For each fitted mount it first writes two fields of the template: the chassis piece's parent byte (`+0x04`) over the template's, and 12 plus the fit slot (`.GL +0x17`) over the internal index of the template's first dependent entry. `HercPiece_ReplacePieces` (`0040d240`) then overwrites the mount component's piece with a copy, which `HercPiece_CopyRecord` (`0040d050`) gives a dependent list of its own, and `HercPiece_SetInternalMaxima` (`0040d128`) writes the template's `+0x2a` as that internal's maximum. The table is the machine's own, loaded by `Mech_Constructor` (`00415bb0`), so the replacement is per machine.

A fitted mount therefore has the template's armour (`+0`: 1500, 2000 or 2500 on the guns, 15000 on ids 19–21, 1500 on the pods), neither a debris group nor a cell sequence (both `-1`, so its loss throws group 2 and hides no part), the chassis piece's parent, destruction flag bit 0 alone, and one internal at spill weight 20 whose maximum is 500 on a gun, 15000 on ids 19–21 and 0 on a pod. Templates 0, 26 and 27 carry an empty piece with no internal. The collision side of the same loop is [`collision-spheres.md`](collision-spheres.md#a-fitted-weapon-brings-its-own-spheres).

The tail of the same function clears the occupancy flag of each of the first 29 components whose piece lists no internal ([`../simulation/component-damage.md`](../simulation/component-damage.md#the-component-damage-system)). On retail data that is every empty hardpoint and every mount fitted from templates 0, 26 or 27; the shoulders, 2 and 3, on every chassis; 13–18 on every chassis but PITBULL and SPIDER; the brackets, 4 and 5, on HEADHUNT, MAVERICK, RAMSES, STINGRAY and TOMAHAWK; 1–6 on PITBULL and SPIDER; and 1 and 9–12 on RAZOR. Of those, only SAMSON's shoulders have spheres in their `.COL`.
