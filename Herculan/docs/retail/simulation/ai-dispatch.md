# AI behaviour dispatch

The structural spine of the AI: how a machine's current behaviour is represented, how the per-tick work reaches it, and how it changes. Every other `ai-*.md` doc cites state indices and mech field offsets from here rather than restating them.

What each state actually *does* is out of scope. The walking states live in [`ai-navigation.md`](ai-navigation.md) and the fighting ones in [`ai-combat-states.md`](ai-combat-states.md); target handling is [`ai-targeting.md`](ai-targeting.md), weapon choice [`ai-weapons.md`](ai-weapons.md), and where a state's orders come from [`ai-goals.md`](ai-goals.md).

**The tables below are the `Mech` class'.** The `Flyer` class has a parallel set of its own — seven states, its own names, its own dispatchers — and everything in this doc but the specific indices applies to it unchanged. See [`ai-flyers.md`](ai-flyers.md#the-seven-states).

## Three parallel tables

DBSIM holds a machine's 22 behaviour states as three static arrays that share one index. They are contiguous, and the third begins exactly where the second ends, which is what fixes the element count at 22.

| Address | Name | Stride | Contents |
|---|---|---|---|
| `004993a4` | `BehaviourStateTable` | `0x3e` | The state descriptors. **Zero-filled in the image** — C++ statics built at startup |
| `004998f8` | `BehaviourSlotBlocks` | `0x24` | Source member-function triples the descriptors are built from |
| `00499c10` | `BehaviourStateNames` | packed | 22 NUL-terminated names, the game's own |

`004993a4 + 22 * 0x3e = 004998f8`.

Ghidra reports no xrefs on any of the three: the descriptors are reached by immediate address, the blocks are read by an unmarked static initialiser at `00413ed4`, and the names are reached as `base + offset`.

## The 22 states

Index `N` names descriptor `004993a4 + 0x3e*N` and block `004998f8 + 0x24*N`. The move slot is `Mech_MovementTick` (`0041a360`) in 18 of the 22 blocks — every state that has one bar `player fly` and `ramming`, which each carry their own — so only the exceptions are spelled out.

| # | Name | Think | Move | Reassess | `+0x04` | `+0x3c` |
|---|---|---|---|---|---|---|
| 0 | `deciding` | — | — | `0041eb34` | 0 | 3 |
| 1 | `player` | `0041c194` `Mech_BehaviourPlayerThink` | walk | — | 10 | 3 |
| 2 | `player fly` | `0041c194` | `004198f4` `Razor_MovementTick` | — | 10 | 3 |
| 3 | `attacking` | `0041c594` | walk | `0041cf18` | 50000 | 0 |
| 4 | `flanking` | `0041d4e4` | walk | `0041cf18` | 50000 | 0 |
| 5 | `facing off` | `0041d41c` | walk | `0041cf18` | 50000 | 0 |
| 6 | `attacking base` | `0041c86c` | walk | `0041cf18` | 50000 | 0 |
| 7 | `attacking flyer` | `0041c9cc` | walk | `0041cf18` | 50000 | 0 |
| 8 | `patrolling` | `0041d7d0` | walk | `0041eb34` | 10 | 3 |
| 9 | `travelling` | `0041d9cc` | walk | `0041eb34` | 10 | 3 |
| 10 | `following` | `0041daac` | walk | `0041eb34` | 10 | 3 |
| 11 | `bulldog travel` | `0041d9cc` | walk | `0041eb34` | 10 | 3 |
| 12 | `search/destroy` | `0041d60c` | walk | `0041eb34` | 10 | 3 |
| 13 | `sleeping` | `0041c418` | walk | `0041eb34` | 10 | 3 |
| 14 | `skirting` | `0041dd64` | walk | `0041eb34` | 10 | 0 |
| 15 | `guarding` | `0041e224` | walk | `0041eb34` | 10 | 3 |
| 16 | `driving off en` | `0041def0` | walk | `0041eb34` | 50000 | 0 |
| 17 | `ramming` | `0041e570` `Mech_BehaviourRamThink` | `0041e488` `Mech_BehaviourRamTick` | `0041eb34` | 10 | 0 |
| 18 | `fleeing` | `0041d2c4` | walk | `0041cf18` | 15000 | 5 |
| 19 | `in limbo` | — | — | — | 10 | 6 |
| 20 | `dead` | `0041e554` | walk | — | 0 | 6 |
| 21 | `disabled` | `0041e554` | walk | — | 0 | 7 |

Notes the table makes visible:

- **`deciding` (0) has no think and no move.** It is not a state a machine runs in; it is the state a machine is *put* in so that the reassess slot resolves it into a real one.
- **The player's two states share a think, and it is not an AI one.** `Mech_BehaviourPlayerThink` watches the player's own progress through the mission rather than steering anything — [`player-waypoints.md`](player-waypoints.md).
- **`travelling` (9) and `bulldog travel` (11) share a think function.** They differ only in which descriptor — and so which timing — is installed.
- **`dead` (20) and `disabled` (21) share a think function** and still take the normal walk move.
- **The reassess slot splits the roster in two, and five states have none.** Combat states (3–7, 18) use `Mech_AiCombatReassess` (`0041cf18`); `deciding` and the walking states (8–17) use `0041eb34`, the state-selection function itself. The player's two states and the three out-of-action states leave the slot zero, so `Behaviour_DispatchReassess` returns without calling. The combat form falls back on `0041eb34` when it finds nothing to fight — see [`ai-targeting.md`](ai-targeting.md#the-combat-reassess--mech_aicombatreassess-0041cf18).

`ramming` (17) is what anchors the indexing: its two slots are the independently-identified `Mech_BehaviourRamThink` / `Mech_BehaviourRamTick` pair (see [`damage-system.md`](damage-system.md)), and `player` (1) / `player fly` (2) match the three constructor branches in [`razor-flight.md`](razor-flight.md#how-the-flyer-paths-are-reached).

## Descriptor layout — `0x3e` bytes

| Offset | Type | Field |
|---|---|---|
| `+0x00` | ptr | The state's name, into `BehaviourStateNames` |
| `+0x04` | int | **Dwell time**, in the simulation's timer unit ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)) — how long the machine stays in this state before reassessing |
| `+0x08` | 16 B | 16 one-byte booleans, expanded from a 16-bit mask by `Behaviour_ExpandFlagBits` (`00415028`) |
| `+0x18` | triple | **Think** — `{func, thisDelta, vtableIndex}` |
| `+0x24` | triple | **Move** |
| `+0x30` | triple | **Reassess** |
| `+0x3c` | short | The string index the F7 comm box prints on a squadmate's `OBJECTIVE:` line — `STRINGS0` group 40, `ATTACK`/`TRAVEL`/`PATROL`/`FORM UP`/`GUARD`/`FLEE`/`DEAD`/`IMMOBILE`. Read only by `Mech_SquadOrderLineIndex` (`0041bac8`); see [`../formats/heads-down-display.md`](../formats/heads-down-display.md) |

### The flag bits

`Behaviour_ExpandFlagBits` writes one byte per bit (`bit & 1`) over two source bytes, so `+0x08` is a bitmask unpacked for cheap indexed testing. Each state's mask is an immediate in the initialiser, and only the low 6 bits are ever set:

| Mask | States |
|---|---|
| `0x00` | `deciding` |
| `0x01` | `player`, `player fly`, `patrolling`, `travelling`, `following`, `search/destroy` |
| `0x02` | `attacking`, `flanking`, `facing off`, `attacking base`, `attacking flyer` |
| `0x03` | `skirting` |
| `0x05` | `guarding` |
| `0x06` | `driving off en` |
| `0x09` | `bulldog travel`, `sleeping`, `ramming` |
| `0x12` | `fleeing` |
| `0x21` | `in limbo`, `dead`, `disabled` |

| Bit | Consumer |
|---|---|
| 0 | `Mech_AiTick` skips the dwell countdown — see below |
| 1 | **Committed to a fight.** Suppresses a fresh reaction to incoming fire; the combat reassess's leader sweep skips a member that has it; the acquisition score in [`ai-targeting.md`](ai-targeting.md) discounts a candidate that lacks it |
| 2 | **Holding a place.** Fire is answered by defending the post rather than chasing the shooter |
| 3 | **Ignore fire entirely** |
| 4, 5 | The target's *state tier*, read off the target's descriptor by `Ai_TargetStateTier`: fleeing versus finished. See [`ai-targeting.md`](ai-targeting.md#abandoning-a-target--ai_shouldabandontarget-0041c4a8) |

The initialiser copies block `+0x00 → +0x18`, `+0x0c → +0x24`, `+0x18 → +0x30`, so the block and descriptor slot orders differ and only the descriptor order is the one the dispatchers use.

## The mech's behaviour block — `mech+0x4d`

Not a pointer field: `0x45` bytes embedded in the mech, running `mech+0x4d` to `mech+0x91`. `Behaviour_SetState` (`00413e50`) is the only writer of the descriptor pointer, and the only thing that touches the block as a whole; it takes `&block`, not the mech. Two parts of what it lays down are then written by other code: the countdown below, and the scratch from `+0x0d` on, which each state lays its own fields over — [`ai-combat-states.md`](ai-combat-states.md#fields-this-layer-owns).

| Offset | Type | Set on state change to |
|---|---|---|
| `+0x00` | ptr | The new descriptor |
| `+0x04` | byte | Not written here; `Timer_CountDown` is handed `&block+0x04` and steps the int that follows it |
| `+0x05` | int | The dwell countdown: `descriptor+0x04` + `(DAT_004a9bf4 & 0xf)`, after `DAT_004a9bf4 += 0xd` |
| `+0x09` | int | 0. `Behaviour_CountTick` (`00413eb0`), the last call of every `Mech_AiTick`, increments it on every call except the first after the state is installed |
| `+0x0d` | 0x28 B | Zeroed |
| `+0x35` | 0x10 B | Zeroed, then `+0x36 = 1`. `Behaviour_CountTick` clears `+0x36` on its first call and sets `+0x35` on every later one |

`DAT_004a9bf4` is a global stepped by 13 per state change and masked to 4 bits, so the jitter is 0–15 counts — a few milliseconds — and deterministic in call order rather than random.

### What the dwell time buys

`Timer_CountDown` (`004679a4`) subtracts `SimTickDelta` from the countdown each AI tick and clamps it at zero, so `descriptor+0x04` is in **the simulation's timer unit, not milliseconds**: one count is about 0.49 ms ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)). It is handed `&block+0x04` and steps only the `int` that follows, never touching the byte it is given. But `Mech_AiTick` only runs the countdown when descriptor flag bit 0 is *clear*, and that is true of exactly eight states: `deciding`, the five combat states, `driving off en` and `fleeing`. **For every other state the countdown is loaded and never stepped**, so its dwell value — 10 counts, bar `dead` and `disabled` at 0 — never expires on its own. Those states end on their own terms instead, through the paths below.

The eight that do run a clock:

- **`deciding` holds for 0**, so it resolves on the tick after it is installed. It is a placeholder, not a state a machine runs in.
- **Combat states and `driving off en` hold 50000 counts, about 24 seconds.** A machine that has committed to `attacking` or `flanking` does not re-open the decision every tick, so it cannot thrash between engagement styles while a fight is in progress.
- **`fleeing` holds 15000, about 7.3 seconds.**

The 0–15 count jitter is noise against all three figures.

Three things cut a dwell short, and they are the whole of what writes `block+0x05` outside `Behaviour_SetState`:

- `Group_OrderTick` (`00423adf`) zeroes every member's countdown when the group advances to its next order.
- `Mech_AiTick` (`00411d51`) zeroes the machine's own when its think returns nonzero — the state's way of saying it is finished.
- `Mech_ReceiveSquadOrder` (`00420ad4`; the store is at `00420d53`) zeroes it when the order names the target the machine already holds, the current descriptor carries the "committed to a fight" flag bit (descriptor `+0x09`) *and* the "holding a place" bit (`+0x0a`). A committed machine that does not hold a place retargets instead — [`ai-squadmates.md`](ai-squadmates.md#receiving-one--mech_receivesquadorder-00420ad4-mech-vtable-0x28).

Because a zeroed countdown is what lets the reassess slot run, the first and third of these reach even a state whose flag bit 0 stops the clock — so "the countdown never expires" is not the same as "nothing outside the state can end it."

## The AI tick — `Mech_AiTick` (`00411cec`)

One machine's whole AI frame, and the only thing that calls the three dispatchers. **Its sole caller is `Group_OrderTick` (`00423a74`), at `00423af5`**, which runs it over every member of a group that has entered the mission.

That single call site is the most consequential fact in this doc: **the AI is driven from the mission-group layer, not from `Sim_MainTick`'s object loop.** A machine that is not a live member of a group never thinks, which is consistent with a group still awaiting deployment running `Group_DeploymentCheck` *instead of* `Group_OrderTick` — see [`mission-deployment.md`](mission-deployment.md).

```
if (mech+0x4d has a descriptor) {
    if (dwell countdown == 0)        vtable +0x1c   reassess
    if (descriptor+0x08[0] == 0)     Timer_CountDown(&block+0x04)
                                     vtable +0x14   move
    if (mech+0xaf == 0) {
        if (vtable +0x18 think != 0) dwell countdown = 0
    } else {
        mech+0xaf = 0
    }
    block+0x09++
}
```

Three things worth taking from the order:

- **Reassess runs before move and think**, so a state change takes effect on the same tick it is decided.
- **Move runs before think.** The machine is integrated on its old think's decisions, not the new ones. The think is where every steering decision is made — see [`ai-navigation.md`](ai-navigation.md).
- **`mech+0xaf` suppresses think for exactly one tick**, clearing itself as it does. A field scan of the whole image, resolving the `LEA`/`ADD` rebases and the spill-and-reload idiom, reports `Mech_AiTick`'s own clear at `00411d3d` as the only write to the byte, and the neighbours a wider store could straddle it from (`+0xae`, `+0xac`, `+0xad`) are byte fields with byte-wide accesses. So every tick found runs its think; see [Open](#open).

## How the per-tick work reaches a state

Three mech vtable slots (vtable at `0049a282`), each reading a different descriptor triple. All three have the same shape: read `mech+0x4d`, and if the triple is entirely zero return 0 without calling.

| Vtable | Function | Descriptor slot | Role |
|---|---|---|---|
| `+0x14` | `Behaviour_DispatchMove` (`00415afc`) | `+0x24` | Move |
| `+0x18` | `Behaviour_DispatchThink` (`00415b38`) | `+0x18` | Think |
| `+0x1c` | `Behaviour_DispatchReassess` (`00415b74`) | `+0x30` | Reassess |

Each calls `(*slot.func)(mech + slot.thisDelta)`. Because these are pointer-to-member calls made through the dispatchers rather than vtable entries, Ghidra reports zero xrefs on every think and move function in the table above — which is why the AI reads as unreachable code until the tables are followed by hand.

*Reassess* is this project's name for the `+0x30` slot, taken from what its two implementations do; the game's own name for it is not in the binary.

## Choosing a state — `Mech_AiSelectBehaviour` (`0041eb34`)

Three mutually exclusive paths, tested in this order.

**1. The local player** (`mech+0xa3` set). Installs `player fly` if the type record's flyer flag (`typeRec+0x50`) is set, else `player`. `Mech_Constructor` (`00415bb0`) makes the same test, so the player's state is settled at construction and re-affirmed here.

**2. A machine in the player's own group** carrying a standing squad order (`mech+0x23e` nonzero). The order verb selects:

| `mech+0x23e` | Result |
|---|---|
| 1, 2 | `patrolling` |
| 4 | `Mech_AiEngageOrderedTarget` (`0041c0f4`) against `mech+0x248` when the order target's `+0x99` is clear; otherwise clear the order and re-enter |
| 6 | `guarding` |

`mech+0x248` is the order's target object. This path is the entry point for [`ai-squadmates.md`](ai-squadmates.md); verbs 3 and 5 fall through with no state installed.

**3. Everything else** — a mission-group machine. The group record (`mech+0x45`) holds an order array at `group+0x44` and a current index at `group+0x6c`; the order's first `short` is the verb:

| Verb | State |
|---|---|
| 0 | `search/destroy` |
| 1 | `ramming` |
| 2 | `guarding` |
| 3 | `patrolling` |
| 4 | `sleeping` |
| 5 | `travelling`, or `bulldog travel` when the chassis' torso-twist limit (`typeRec+0x22`) is above `0x7d00`. Twenty chassis state 14000; the **Pitbull states 32767**, the sentinel for a turret with no stop, so it is the one machine that travels as a bulldog |
| 6 | `following` |

A null order entry substitutes verb `0x0b`, which matches no case and falls through to `Behaviour_SetState` with whatever descriptor the caller left in `EDX` — null when reached through the reassess dispatcher. [`ai-goals.md`](ai-goals.md#a-group-with-no-order-at-all) owns that case and the order data, along with [`msn-mission-file.md`](../formats/msn-mission-file.md).

Four of this function's paths — the player branch, squad verbs 1/2 and 6, and the order-verb fall-through — end the same way: if the machine holds a selected target (`mech+0x1a4`), it is released, the refcount at `target+0x1a2` decremented, and `mech+0x9d` set. The release block is skipped whole when the field is already null, so `mech+0x9d` is not set then. See [`target-selection.md`](target-selection.md).

The paths that install no state — squad verbs 3 and 5, and any verb above 6 — leave the target alone, and so does verb 4, which hands off to `Mech_AiEngageOrderedTarget` (`0041c0f4`); that function *acquires* the order's target before installing `attacking base` or `attacking flyer`. Dropping the target is this function's habit, not a property of changing state: the other 26 `Behaviour_SetState` call sites do not do it.

### Transitions

`Behaviour_SetState` (`00413e50`) has **30 call sites**, which are the state machine's whole edge list. The four in `Flyer_Constructor` (`004215f4`), `Flyer_ComponentDamageWrite` (`00421bb4`), `Flyer_EngageWithFlight` (`00422bf0`) and `Flyer_AiSelectBehaviour` (`00422d00`) install descriptors outside the mech table (`0x499cf8`, `0x499e60`, `0x499d34`) and belong to the flyer path ([`ai-flyers.md`](ai-flyers.md#the-seven-states)). The other 26:

| Installed by | States |
|---|---|
| `Mech_Constructor` (`00415bb0`) | `deciding`, `player`, `player fly` |
| `Mech_ComponentDamageWrite` (`00417de4`) | `disabled`, `dead`, `in limbo` |
| `Mech_AiEngageOrderedTarget` (`0041c0f4`) | `attacking flyer`, `attacking base` |
| `Mech_AiFleeCheck` (`0041cb94`) | `fleeing` ×2 |
| `Mech_AiCombatReassess` (`0041cf18`) | `attacking`, `flanking` or `facing off`, by combat rating |
| `Mech_BehaviourSearchDestroyThink`, `Mech_BehaviourPatrolThink` | `fleeing` |
| `Mech_BehaviourGuardThink` (`0041e224`) | `fleeing`, `driving off en` |
| `Mech_BehaviourSkirtThink` (`0041dd64`) | whatever it stashed |
| `Ai_BeginSkirtIfBlocked` (`0041de9c`) | `skirting` |
| `Mech_AiSelectBehaviour` (`0041eb34`) | `player`, `player fly`, `patrolling`, `guarding`, and the order-verb table |
| `Mech_AiOnTakingFire` (`0041f7b8`) | `driving off en` |
| `Mech_ReceiveSquadOrder` (`00420ad4`) | `patrolling` ×3, `guarding` |

Six states have no installer of their own and can only be reached through the order-verb table: `search/destroy`, `travelling`, `following`, `sleeping`, `ramming` and `bulldog travel`, the last of them only for the one chassis whose torso-twist limit clears its gate — see [Choosing a state](#choosing-a-state--mech_aiselectbehaviour-0041eb34). The states entered from a think are [`ai-combat-states.md`](ai-combat-states.md)'s and [`ai-navigation.md`](ai-navigation.md)'s.

## Mech fields the AI owns

Fields first read or written by the dispatch layer. Fields whose meaning is settled elsewhere link out rather than being restated.

| Offset | Type | Meaning |
|---|---|---|
| `+0x45` | ptr | Mission group record — [`mission-deployment.md`](mission-deployment.md) |
| `+0x4d` | 0x45 B | The behaviour block, above |
| `+0x9d` | byte | Set whenever the selected target is released |
| `+0xa3` | byte | This is the locally-piloted machine |
| `+0xaf` | byte | Suppress think for one tick. `Mech_AiTick`'s clear is the only write the field scan finds ([Open](#open)) |
| `+0x23e` | short | Standing squad order verb — [`ai-squadmates.md`](ai-squadmates.md) |
| `+0x248` | ptr | Squad order target object — [`ai-squadmates.md`](ai-squadmates.md) |
| `+0x1a4` / `+0x1a2` | ptr / short | Selected target and its refcount — [`target-selection.md`](target-selection.md) |
| `+0x1f2` | ptr | Type record — [`mech-locomotion.md`](mech-locomotion.md#mech-instance-fields) |

## Where the slices start

The AI-relevant mech vtable slots, as entry points for the topic docs. Slots whose meaning is settled elsewhere link out rather than being restated.

| Vtable | Function | Lands in |
|---|---|---|
| `+0x40` | `Mech_GetOverallDamage` (`00415504`) | The flee check's base fear — [`ai-targeting.md`](ai-targeting.md#the-flee-check--mech_aifleecheck-0041cb94) |
| `+0x28` | `Mech_ReceiveSquadOrder` (`00420ad4`) | "The player has transmitted an order to me" — [`ai-squadmates.md`](ai-squadmates.md#receiving-one--mech_receivesquadorder-00420ad4-mech-vtable-0x28) |
| `+0x48` | `Mech_AiEnemySighted` (`00412800`) | The "enemy detected" callout — [`ai-targeting.md`](ai-targeting.md#radio-callouts) |
| `+0x4c` | `Mech_CompareCombatRating` (`0041cabc`) | This machine's combat rating against a candidate's — [`ai-targeting.md`](ai-targeting.md#relative-combat-rating) |
| `+0x50` | `Mech_AiOnTakingFire` (`0041f7b8`) | "This object just took fire" — [`ai-targeting.md`](ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50). Holds one of the 30 `Behaviour_SetState` call sites |
| `+0x64` | `Mech_AiOnLineOfFireBlocked` (`0041dd2c`) | "My shot hit something that is not what I aimed at" — the trigger for `skirting`, [`ai-combat-states.md`](ai-combat-states.md#how-it-is-reached) |
| `+0x68` | `SimObject_SetRunInto` (`0042200c`) | "Something ran into me" — the latch `ramming` detonates on, [`ai-combat-states.md`](ai-combat-states.md#the-charge--mech_behaviourramtick-0041e488) |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The `0x24`-stride table starts at `00499928` and holds `Mech_MovementTick` at `+0x18`, one entry per mech type | Off by one triple. Blocks start at `004998f8` and the move slot is `+0x0c`; `00499928` is block 1's move. A raw byte search really does find `0041a360` at 18 sites of stride `0x24`, but those are the 18 **states** that share the walk move, not 18 mech types |
| `Mech_AiSelectBehaviour` is `__fastcall` and takes three arguments | Ghidra types it that way, and its own recursive call obliges by passing three. The prologue is `MOV EBX,[EBP+8]` and nothing else: it is `__cdecl(mech)`, and the second "parameter" is the `EDX` the fall-through installs |
| `Mech_MovementTick` is dispatched from mech vtable `+0x18` | `+0x18` is the **think** dispatcher. The move is vtable `+0x14` (`00415afc`), reading descriptor `+0x24`. For most states the think function drives locomotion itself, which is why the move slot looks like the tick entry |
| The think and move functions are dead code | Every one has zero xrefs because it is only ever reached as a pointer-to-member through `00415afc` / `00415b38` / `00415b74`. A whole-image sweep finds each of them as a stored dword in `BehaviourSlotBlocks` and nowhere else — `Mech_MovementTick` at 18 sites, one per state that walks |
| Changing state drops the machine's target | Four of `Mech_AiSelectBehaviour`'s paths release it, which is most of what one reads when following the reassess, and every other doc's state change goes through that function. It is still local to those four sites: `Mech_AiEngageOrderedTarget` acquires a target on its way into `attacking base`, and the damage, flee-check and taking-fire installers leave the selection standing |
| A state whose flag bit 0 stops the dwell clock can only be left on its own terms | The clock is what gates the *reassess slot*, and three functions zero the countdown directly. A group advancing its order, or a squad order arriving, resolves a machine out of any state with a reassess — see "What the dwell time buys" |

## Open

- **Open:** bits 6–15 of descriptor `+0x08` — no state sets one, so nothing can read one.
- (Deferred) **Open:** what raises `mech+0xaf`. The field scan finds no writer but `Mech_AiTick`'s own clear, and a null result is not proof; a write through a base register neither alias pass follows would go unseen.
