# Destruction effects — wreckage, fire, and a structure coming down

What a destroyed thing puts on screen: the shapes it throws (`debris.cpp`, tables in [`../formats/debris-dat.md`](../formats/debris-dat.md)), the flames left burning on it (`fire.cpp`, resources in [`../formats/fire-dat.md`](../formats/fire-dat.md)), and the multi-stage collapse a building runs before it becomes a wreck (`base.cpp`, record fields in [`../formats/bases-dat.md`](../formats/bases-dat.md#the-type-record)). All three are driven by the damage model but are separate from it — the arithmetic of losing a part is in [`component-damage.md`](component-damage.md) for a machine and [`structure-behaviour.md`](structure-behaviour.md#taking-damage--base_applydamage-00404d70) for a structure.

Impact effects — the puff where a shot lands — are a fourth, different subsystem: [`impact-effects.md`](impact-effects.md). Everything here spawns them, and nothing here is one.

## Debris

### The two-database index space

Every spawn site names a debris group by a single index, and `Debris_Resolve` (`004089bc`) splits it against the **default** database's group count:

```c
if (index >= g_DebrisDefault.groupCount) { index -= g_DebrisDefault.groupCount; return g_DebrisAlternate; }
return &g_DebrisDefault;
```

`g_DebrisDefault` (`004a96d4`) is `DEF_DEB`, loaded once at startup. `g_DebrisAlternate` (`004a96d0`) is a *currently installed* pointer each spawn site writes immediately before it spawns:

| Site | Installs |
|---|---|
| `Mech_ComponentDamageWrite` (`00417de4`) | the machine's own chassis table, `typeRecord+0x212` |
| `Base_ThrowDebris` (`0040379c`) | `BASE_DEB` (`004a9644`) |
| `Debris_TickUpdate` (`00408bd8`), for a bursting piece | whatever table that piece was thrown out of, captured at `obj+0x5d` |

So a low index always means the same thing — group 2 is `DEF_DEB`'s third group whoever throws it — and a high index means whatever was installed a heartbeat earlier. Nothing bounds the alternate half; a high index with nothing installed reads through a null pointer.

Retail has 6 groups in `DEF_DEB`, so indices 0-5 are shared and 6 upward reach the installed table. The databases themselves — `DEF_DEB`, `BASE_DEB` and one per chassis — and their file layout are [`../formats/debris-dat.md`](../formats/debris-dat.md).

### Throwing a group

`Debris_ThrowGroup` (`004086bc`) reads a group two ways:

- **`throwCount == 0`** — throw every piece it holds, once each.
- **otherwise** — throw exactly `throwCount` pieces drawn from it at random, weighted: a draw under the group's total, then each piece's weight subtracted off it in file order until one covers what is left. The same piece can therefore be thrown twice and another skipped.

`Debris_ThrowGroupAt` (`00408530`) is the same call from a *point* rather than a frame — it builds an identity rotation with the point in the translation. Every site but a machine's own component destruction uses it.

`Debris_Throw` (`00408588`) places one piece. A piece stating either angle has its own yaw composed onto the spawn frame and the composed attitude read back out as Euler angles; only a piece stating an orientation yaw keeps that attitude, so one stating only a throw yaw does the matrix work and discards it. **The position is the spawn frame's own either way** — the composed transform is a temporary nothing is placed at.

`Debris_Launch` (`004089e4`) does the launch:

```c
speed      = (g_DebrisSpeedScale << 10) / mass
horizontal = Q14Multiply(speed, Cos(pitch))
vx = Q14Multiply(horizontal, Cos(yaw + 0x4000))
vy = Q14Multiply(horizontal, Cos(yaw))
vz = Q14Multiply(speed,      Cos(pitch - 0x4000))
spin = Q10Multiply(-1700, rand & 0x3ff) - 800      // always negative: 800-2500 BAM/s
```

The pitch is drawn between two globals and the speed scaled by a third. All three are rewritten around a burst and restored after it, which is the whole of what makes second-generation debris scatter closer:

| | `004a96e8` pitch min | `004a96ea` pitch max | `004a96ec` speed scale |
|---|---|---|---|
| First throw | 6000 (≈33°) | 16000 (≈88°) | 420 |
| A piece bursting | 3000 | 8000 | 320 |

`Debris_ThrowAtBearing` (`00408b74`) draws the pitch and forwards; `Debris_ThrowRandomBearing` (`00408bb4`) draws the bearing too. A carrier velocity at `004a96e4` is added to the result, and only `Flyer_ComponentDamageWrite` (`00421bb4`) ever sets it — a shot-down aircraft's wreckage keeps the aircraft's motion, and nothing else's does.

### The piece — `Debris_TickUpdate` (`00408bd8`)

Object fields, 0x61 bytes from a 150-entry pool at `004a96c6`:

| Offset | Meaning |
|---|---|
| `+0x0c` | euler triple; only X moves |
| `+0x26` | position, three `int32` |
| `+0x41` | `signed char` child group, `-1` for none — **the gate on the whole death branch** |
| `+0x4a` | velocity, three `int16` |
| `+0x55` | burst countdown, `RandomBelow(30000) + 2000` |
| `+0x59` | `int16` `EXPLOS.DAT` type on death, `-1` for none |
| `+0x5b` | spin rate |
| `+0x5d` | the database this piece was thrown out of |

Per tick, in order:

1. Spin integrated into euler X; gravity into vertical speed.
2. Horizontal drag on X and Y only, `Q10Multiply(30, v)` integrated. Nothing slows the fall.
3. The move, by the **average of the speed before and after** this tick's changes. Verified against the raw disassembly at `00408ca1`: it is `ADD dword [pos], movsx word [avg]` — the average is taken and shifted in 16 bits (`SAR word`), and it is added **un-integrated**, so a debris velocity is per-tick where a RAZOR's is per-second. A Cybrid flyer's is per-tick too — see [`ai-flyers.md`](ai-flyers.md#the-move--flyer_movementtick-004218c4).
4. Ground: below `terrainHeight + Q10Multiply(500, shapeRadius)`, `shapeRadius` being vtable `+0x10` (`SimObject_GetShapeRadius`, the piece's root's own `shape+8`), the piece is snapped up to it and bounces at `-Q10Multiply(450, vz)`, its countdown cleared. A rebound under `0x2d` has stopped.
5. For a piece with a child group: the countdown is ticked only while it is still flying, so ground contact bursts it on that tick either way. The burst spawns its effect, re-installs `+0x5d`, and throws the child group at the tightened window above — unless EFFECTS DETAIL is at its lowest ([below](#effects-detail)).

Gravity is `-0x20` everywhere but theater 4, the Moon — the test is `CMP word [ScriptDatHeader], 4` at `00408bf3`, the `script.dat` header's theater index ([`../formats/script-dat.md`](../formats/script-dat.md#header-format)) — where it is `-10` and wreckage hangs noticeably longer. A piece with no child group has no death branch at all and simply lives until it settles.

### Spawn sites

| Site | Group | When |
|---|---|---|
| `Component_DestroyAndCascade` (`0040d434`) | the `.DMG` record's `+0x02`, or 2 when it states `-1` | a machine's component comes apart; thrown from the component's own composed frame |
| `Mech_ApplyDirectFireDamage` (`004188c8`) | 2 | a hit moved a component into a new damage band without finishing it |
| `WeaponMount_Destroy` (`0040f57c`) | see below | a visibly-mounted hardpoint is lost |
| `Base_ThrowDebris` (`0040379c`) | the component record's `+8`, plus 10 and 12 when that is above 5 | a structure's part collapses |
| `Base_DirectFireHitTest` (`00405038`) | 1, on a `rand & 0xfff < 0x401` roll | any hit on a structure |
| `Flyer_DirectFireHitTest` (`00421c8c`) | 1, or 3 for the hit that brings it down | any hit on an aircraft |
| `Razor_MovementTick` (`004198f4`) | 3 | a cockpit or fuselage contact that destroys its component |

`WeaponMount_Destroy` is the one site that throws a piece it built itself rather than anything a table names: the gun's own model, the same shape index out of `dts\MECHWPN2.DTS` rather than the `MECHWPNS.DTS` it was drawn from, off the mount point, on a `Math_EulerToward` bearing away from the machine's aim point, at the hardpoint's `.GL +0x18` pitch and a flat mass of `0x4b0`. It keeps the muzzle frame's attitude. **The third argument picks the pair the piece is built with**: 0 builds a piece that bursts (child group 2, effect `0x14`), 1 a plain piece that just falls. Which way of losing the mount passes which is [`weapon-mounts.md`](weapon-mounts.md#losing-a-mount).

## A machine's component

`Component_DestroyAndCascade` (`0040d434`) is the whole of what a lost machine component puts on screen. The component's `.DMG` record ([`../formats/dmg-damage-file.md`](../formats/dmg-damage-file.md#the-piece-record)) says what it does; what brings it there is [`component-damage.md`](component-damage.md). In order:

1. **Fire**, picked by the record's flags — [Who catches fire](#who-catches-fire).
2. **The shape**: a `+0x03` that is not `-1` steps that sequence of the machine's shape to cell 2, its blank cell ([`../rendering/mech-shape-drawing.md`](../rendering/mech-shape-drawing.md#a-destroyed-component-hides-its-own-geometry)).
3. **An explosion** of `EXPLOS.DAT` type 10, or `0x11` when the flags' bit 2 is set, at the component's anchor: its cluster's bounding-sphere centre, through that cluster's node frame composed with the machine's, or the machine's own frame when no cluster names the component.
4. **Debris**, the `+0x02` group or 2, thrown from that same frame ([Spawn sites](#spawn-sites)).

The fire and the explosion share a one-per-cascade latch, `DAT_004a98b0`, cleared at the start of each cascade. Choosing type `0x11` sets it, and while it is set the rest of that cascade lights neither a fire nor an explosion. The shape step and the debris are not gated by it.

## Fire

`fire.cpp`'s burning-object effect — **not** the muzzle flash, which is the weapon model's own flipbook ([`weapon-mounts.md`](weapon-mounts.md#the-muzzle-flash)).

`FireEffect_LoadResources` (`0046b0a4`) loads the shapes, banks and bank table described in [`../formats/fire-dat.md`](../formats/fire-dat.md) and allocates a **ten**-entry pool at `006b4fb4`.

`FireEffect_AcquireSlot` (`0046b32c`) takes a free entry, or when none is free returns the live one with the **fewest loops left** (`+0x59`). A full pool never refuses a new fire; the one nearest going out pays for it.

`FireEffect_Ctor` (`0046b388`) fields:

| Offset | Meaning |
|---|---|
| `+0x41` | set when no attach point was given: follow the owner's origin |
| `+0x4a` | owner |
| `+0x4e` | attach cluster id; `< 0` uses the raw local point, `0xffff` is the structures' "no cluster" |
| `+0x50` | attach point, three `int16` |
| `+0x57` | frame timer, reloaded to `0x40` |
| `+0x59` | loops remaining: 30, or 5 on the Moon |

On the Moon — the same theater test, at `0046b3b0` — only shapes 1 and 3 are built at all; the others go straight back on the free list. The test runs after the caller has taken the slot, so with the pool full a filtered fire still evicts the live one with the fewest loops left and lights nothing in its place. The machine sites below light only shapes 0 and 2, so on the Moon a machine never burns — though the whole-machine branch still releases the fires already on it. The first live instance starts sound `0x33` and `FireEffect_Dtor` stops it on the last — one loop for every fire in the mission at once, kept positioned on whichever is nearest the camera.

`FireEffect_TickUpdate` is the tick: count the frame timer down, step the shape's cell animation, decrement the loop count each time the frame wraps to zero, then re-place the effect from wherever the owner has carried it to. It ends when the loop count reaches zero. So a fire **loops** where an impact effect plays once.

### Where the shared sound is heard

`Sound_Play(0x33)` is not positional, so on its own the loop would sit centred at the row's own volume. What places it is the tail of `FireEffect_TickUpdate`, which every live fire runs:

```
d = isqrt(dx*dx + dy*dy + dz*dz)      // to ViewObjectPtr, the camera
if ((int)d < (int)DAT_006b4fc0) { Sound_UpdatePosition(0x33, firePosition); DAT_006b4fc0 = d; }
```

`DAT_006b4fc0` is a running minimum reset to `0x7fffffff` once a frame by the pool's own phase-5 subsystem hook (`FireEffect_PerFrameReset`, `0046b084`, registered by `FireEffect_RegisterSubsystem` and driven from `Sim_RenderFrame`). So each frame the last fire to beat the minimum keeps the sound, which is the nearest one. The placement sits **above** the loops-remaining test, so a fire counts on the tick it goes out. `FireEffect_Ctor` calls the tick itself, which is why a fire's sound is placed from the moment it is lit rather than a frame later.

The three squares are 32-bit `IMUL`s and the sum wraps. Distance is in world units (166.667/unit metre), so the sum passes `2^31` at about 278 m and `2^32` at about 393 m — and a fire near a wrap boundary reads as *close*, captures the loop, and is then placed past the row's own 25×1024 cutoff, i.e. at volume zero. See [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

`FireEffect_ReleaseForOwner` (`0046b528`) returns every entry whose owner matches — called from one place, the whole-machine branch of [Who catches fire](#who-catches-fire).

### Who catches fire

Two sites, and they light different shapes:

- **`Component_DestroyAndCascade`**, for a component whose `.DMG` `+0x03` byte is not `-1` (the same byte that drives its shape sequence — see [`../rendering/mech-shape-drawing.md`](../rendering/mech-shape-drawing.md)), and only while the cascade's latch is clear ([above](#a-machines-component)). The `.DMG` flags byte's (`+0x05`, [bits](../formats/dmg-damage-file.md#the-piece-record)) bit 1 releases every fire already on the machine and lights **shape 0** in their place — the machine going up as a whole. With bit 1 clear, bit 3 lights **shape 2** on that component's own cluster and leaves what is already alight.
- **`Base_DeathSequenceTick`**, at the last stage of a collapsing part — see below.

## A structure coming down

A structure's part is not deleted when its health runs out. `Base_ApplyDamage` (`00404d70`, [structure-behaviour.md](structure-behaviour.md#taking-damage--base_applydamage-00404d70)) gives it a **stage countdown**, and `Base_DeathSequenceTick` (`00403914`) steps every falling part through it one stage at a time. That is why a building takes several seconds to collapse and its parts collapse in the order they were shot.

The sequence is picked by the component record's `+4`, or by the type's own `+0x10` for the last part to fall (stage 1 below), indexing five parallel four-entry tables in the executable's statics:

| Sequence | `004973fc` collapse explosion | `00497404` hold after it | `0049740c` explode at object origin | `00497414` smoke explosion | `0049741c` stage count |
|---|---|---|---|---|---|
| 0 | 16 | 2000 | yes | 15 | 8 |
| 1 | 21 | 0 | no | 15 | 6 |
| 2 | 15 | 0 | no | 15 | 6 |
| 3 | 15 | 0 | no | 15 | 2 |

`Base_ApplyDamage` sets `state[+5] = stageCount` and `state[+3] = 300`; each expiry of that timer takes one off the stage, and:

- **Stage > 1** — one smoke explosion at a random point inside the part's own spread box (component record `+0x16`, half-extents) around its position, and the timer reloaded to 300, both only on the stages EFFECTS DETAIL allows ([below](#effects-detail)). **Stage exactly 4** also runs `Base_FinishDependents`, whatever the setting.
- **Stage 1** — the collapse. `Base_CollapseExplosion` (`004036c0`) sets off a sequence's explosion, at the structure's origin or at a point put through the structure's frame on `0049740c`, and reloads the timer with the sequence's hold. When the part has a fire of its own, the type states a whole-structure fire and `Base_EveryPartGone` agrees, the sequence is the type's (`typeRec+0x10`, at `typeRec+0x0a`); otherwise it is the part's `+4` at its own emission point, and a part stating none sets off no explosion and reloads no hold. Then `Base_ThrowDebris`, then the shape change. When `Base_EveryPartGone` agrees and the type leaves a wreck, the model instance's shape pointer becomes `hulkShapes[typeRec+0x04]` — the **hulk swap**, which does not need the whole-structure fire. Otherwise the sequence the component record's `+2` names steps to cell 1 (`shapeInstance[+8][rec+2] = 1`, the structure-scale counterpart of a machine's `= 2`, skipped when `+2` is `-1`) and the part finishes its dependents. A structure's parts are two-cell `TSCellAnimPart`s whose **second cell is that part's own rubble**, so a collapsing part is replaced by its wreckage rather than removed — unlike a machine's, whose third cell is a bare `TSPoly` and draws nothing.
- **Stage 0** — the fire, or the drop. A structure with no wreck (`typeRec+0x04` of `-1`), one part, and no cell sequence of its own (`+2` of `-1`) is **dropped through the floor** — `-100000` written straight onto its Z, which is how a small object disappears. Otherwise, when the type or the part states no fire, the part lights its own at its emission point if it states one. When both state one, the type's whole-structure fire (`typeRec+0x08`, at `typeRec+0x0a`) lights only once `Base_EveryPartGone` agrees, and the parts light nothing of their own.

`Base_FinishDependents` (`00403890`) finishes every part naming this one at component record `+0x1c`, recursively and through the ordinary damage path, so each starts its own sequence. That is the structure equivalent of a machine's bone group.

`Base_EveryPartGone` (`00403668`) is the "the structure, not just this part, has gone" test: it walks the parts and passes when every one has a fire shape of `-1` **or** damage at its maximum.

`dgs\BHULKS.DGS` is the wreck library, loaded by `Base_LoadResources` (`00405fac`) into `004a9608`, sized by `max(typeRec+0x04) + 1` over the whole type table, and bound to `BASETEX` whatever bank the standing building used. Retail ships 16 wrecks.

**The swap changes the shape and nothing else.** Both swap sites, stage 1 above and `Base_Construct` (`00405314`) for starting condition 0, write only `shapeInstance+4`. The per-sequence cell array at `+8` stays the one `TSShapeInstance_Ctor` (`00490a58`) built for the standing shape (`shape+0x24` entries, copied from its zeroed `+0x1c`), and `TSShapeInstance_Render` (`00490b10`) binds that array as `g_CellAnimFrames` before drawing whatever shape the instance now holds. A wreck's `TSCellAnimPart`s therefore read the cells the building left. Two retail wrecks have any, and each is named by a single type with one component whose `+2` is `-1`, so neither inherits a collapsed part:

- **Wreck 13** (type `0x1a`): three parts on sequence 0, two cells each. That type's idle flipbook toggles sequence 0 every 125 ms while it stands and stops the moment `Base_ApplyDamage` sets `+0x99` ([structure-behaviour.md](structure-behaviour.md#the-plain-tick--base_thinktick-00403ca8)), so the wreck stands on whichever cell was up at the kill.
- **Wreck 15** (type `0x1f`): parts on sequences 0 and 2, two cells each. Nothing steps that type's cells, so the wreck stands on cell 0. Its standing shape has three sequences, so sequence 2 is inside the array.

## EFFECTS DETAIL

The preferences row of that name is `prefs.cfg` byte 11, `Sound_DetailSetting` (`004d1fc7`), 0 to 2 ([`preferences.md`](preferences.md#what-each-byte-is)). Four instructions read it by its absolute address: the preferences panel's readout, the sound throttle ([`audio.md`](audio.md#the-play-request-gate)), and these two.

- **`Base_DeathSequenceTick`** reads it once on entry. A smoke stage scatters its explosion at 2 on every stage, at 1 on the odd-numbered stages only, and at 0 never. **The 300 reload is inside the same test**, so a stage that scatters nothing leaves its timer at zero and the next tick takes the stage after it: at 0 a part goes from its first hit to its collapse in as many ticks as it has stages, and at 1 each even stage passes in one. The collapse, the debris, the fire and the stage-4 cascade are not gated.
- **`Debris_TickUpdate`** throws a bursting piece's child group only when the setting is non-zero. The piece's own `EXPLOS.DAT` effect goes off either way, above the test; at 0 there is simply no second generation.

A theater-4 test sits beside these in both debris and fire and is a different thing — see [Debris](#the-piece--debris_tickupdate-00408bd8) and [Fire](#fire).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `typeRec+0x04` indexes the base shape table, so a wreck is another building's model | It indexes `dgs\BHULKS.DGS`, a separate library `Base_LoadResources` sizes from the largest value any type states |
| A wreck is a fresh shape, so its cell parts stand on cell 0 | The swap writes only the shape pointer; the wreck draws through the building's cell array ([above](#a-structure-coming-down)) |
| `WeaponMount_Destroy`'s third argument selects a debris *lifetime*, shorter for the local player | It selects a `(childGroup, deathEffect)` pair, and the path that loses the mount picks it ([`weapon-mounts.md`](weapon-mounts.md#losing-a-mount)). Neither call site tests who is flying |
| `Sound_DetailSetting` is an audio setting | The name comes from the sound throttle's read. The byte is the EFFECTS DETAIL row, and it also decides a collapsing structure's smoke and pace and a debris piece's burst ([EFFECTS DETAIL](#effects-detail)) |
| A debris piece's `+0x59` is a lifetime or an eviction priority, as it is on a fire | Different classes at the same offset. On a piece it is the `EXPLOS.DAT` type that goes off where the piece ends |
