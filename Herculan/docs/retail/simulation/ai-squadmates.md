# AI squadmates — the standing squad order

What the player's own three machines do when told something. A standing squad order sits **above** the mission group's order array: in a group whose first member is the player's machine, a nonzero `mech+0x23e` takes its own path through `Mech_AiSelectBehaviour`, which then installs a state without reading the group's verb, and `Ai_NavigationStep` drives at the order's point instead of following the route. That is how the player steers a squadmate off its group's route. Everything else about a player squadmate — formation, acquisition, combat — is the same machinery every AI machine runs, and belongs to [`ai-navigation.md`](ai-navigation.md), [`ai-targeting.md`](ai-targeting.md) and [`ai-combat-states.md`](ai-combat-states.md).

The screens that issue orders are [`heads-down-display.md`](../formats/heads-down-display.md) and [`mfd.md`](../formats/mfd.md); the order text is `STRINGS0.STR` group 0, [`str-strings.md`](../formats/str-strings.md).

## The two ways an order leaves the cockpit

Both end at the same per-machine handler. What differs is who hears it.

| Screen | Dispatcher | Reaches |
|---|---|---|
| [F7] command display, XMIT | `Squad_SendOrderToSlot` (`00431610`) | one comm-box slot out of `g_SquadmateMachines` (`004d044c`) |
| MFD FLASH COMM, XMIT | `Squad_BroadcastOrder` (`004231a4`), through `Squad_BroadcastOrderFromCockpit` (`0043166c`) | every member of the player's group (`DAT_0049b0f8`) |

`Squad_SendOrderToSlot` skips an empty slot, the player's own machine and a destroyed pilot, and withdraws message `0x22` from the pilot-and-squad port either way. It sends with no prior reply, so the one recipient always answers.

`Squad_BroadcastOrder` sends to the whole group, best-suited machine first, skipping the issuer and any destroyed member. Each pass scores every member not yet told, keeps the highest score, breaks ties on range to the issuer, and sends to that one — then carries the answer into the next send, so **only the first recipient, or the first to accept, says anything on the radio**. Verbs 0 and 2, the two that name a single target, stop at the first acceptance; the rest go round until everyone has been told. It returns whether anyone accepted, which is what makes a FLASH COMM row toggle ([`mfd.md`](../formats/mfd.md#mfdflashcomm--mode-1)).

The score is a switch over eight verbs:

| Verb | Prefers |
|---|---|
| 0, 2, 5 | a machine whose descriptor `+0x09` (committed) is **clear** — someone free to take a fight |
| 1, 8, 0xf | one where it is **set** — someone in a fight to call off |
| 4 | a machine whose radar is off |
| 7 | one whose radar is on |

`0xf` is the [F7] display's and goes to one machine, so the broadcast never scores it: `Squad_BroadcastOrder`'s one caller is `Squad_BroadcastOrderFromCockpit`, whose one caller is the FLASH COMM page's XMIT, and that page sends verbs 0-5, 7 and 8. For a verb outside the switch the score local is not assigned anywhere in the call, so every member carries the same leftover stack value. A member qualifies when its score is at least the running best, which starts at -1: a leftover of -1 or more leaves range alone to decide, and one below -1 qualifies nobody, so the order reaches no one ([Open](#open)).

## The order record — 22 bytes

One instance exists: the global at `DAT_004d0458`, which both screens write in place before transmitting. Packed and unaligned.

| Offset | Type | Meaning |
|---|---|---|
| `+0x00` | short | **The verb**, as a `STRINGS0` group 0 index |
| `+0x02` | ptr | **The issuer.** Stamped by the dispatcher with the player's machine, not by the screen that built the rest. `Squad_BroadcastOrder` ranks recipients by range to it and skips it |
| `+0x06` | int[3] | The gridpoint, for the three verbs that take one (12, 13, 14) |
| `+0x12` | ptr | The subject object, for the two verbs that take one (11, 12) |

`HddCommandScreen_FillOrderRecord` (`0044db24`) is what settles the layout: it writes the point triple or the subject pointer according to the pick kind that `HddCommandScreen_CommitPick` (`0044db74`) latched a moment earlier, and XMIT calls the pair in that order.

**The record is zeroed once, by the startup clear of the BSS in `entry` (`00401011`), and after that only overwritten field by field.** The FLASH COMM path writes the verb and the issuer. The [F7] path writes the verb, the issuer, and one half of the record: `FillOrderRecord` writes the point or the subject according to the pick kind latched in the selected pilot's pick slot (`commandScreen+0x85`, three slots of `0x2a` bytes, one per comm box), so the other half keeps whatever an earlier order left there ([Open](#open)). `CommitPick` latches a kind only for an order that wants a pick; for one that does not, the slot keeps that pilot's last pick and `FillOrderRecord` writes it again, the point for a pilot never given one, since the slot starts zeroed. Only `DEFEND POSITION` (12) and `ATTACK ENEMY` (11) take a unit pick, and only `ATTACK ENEMY` refuses a click that lands on no eligible unit; a `DEFEND POSITION` click on bare ground is a gridpoint pick (`HddCommandScreen_PickTarget`, `0044d6b8`). `DEFEND POSITION` is the one receiver that reads both halves, and it stores both, so a guard ordered onto bare ground is stored with whatever unit the record last held as its subject — the last unit pick, or one a pickless order wrote back out of its pilot's slot — and `Mech_AiGoalPosition` prefers the subject. See [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

## The verbs

All eighteen of `STRINGS0` group 0. Entries 0-8 are the MFD's FLASH COMM page, 10-17 the [F7] command display's own list; the two overlap in meaning but not in code, and `Mech_ReceiveSquadOrder` gives each pair its own case only where they differ. Each entry's one attribute byte is the index of its hotkey character ([`heads-down-display.md`](../formats/heads-down-display.md#the-order-list-and-its-state-machine)).

| # | Text | Case | Effect |
|---|---|---|---|
| 0 | `ATTACK MY TARGET` | own | Engage the player's current selection (`CockpitViewInstance+0x210`) |
| 1 | `IGNORE MY TARGET` | own | Set the `+0x9a` latch and find something else |
| 2 | `HELP ME OUT!` | own | Engage whatever is nearest to shooting at the player |
| 3 | `JOIN ON ME` | with 10, 15 | Clear the order and reinstall `patrolling` |
| 4 | `SCAN FOR HOSTILES` | with 16 | Radar ACTIVE |
| 5 | `FIRE AT WILL` | own | Set the `+0xb6` latch and start a fight |
| 6 | *(no text)* | — | No case |
| 7 | `EMCON` | with 17 | Radar PASSIVE |
| 8 | `HOLD YOUR FIRE` | own | Clear `+0xb6` |
| 9 | *(no text)* | — | No case |
| 10 | `DISENGAGE` | with 3, 15 | As verb 3 |
| 11 | `ATTACK ENEMY` | own | Engage the record's subject |
| 12 | `DEFEND POSITION` | own | Guard the record's point or subject |
| 13 | `PATROL GRIDPOINT` | own | Drive to the record's point, engaging on the way |
| 14 | `GOTO GRIDPOINT` | own | Drive to the record's point |
| 15 | `JOIN ON ME` | with 3, 10 | As verb 3 |
| 16 | `SCAN FOR HOSTILES` | with 4 | As verb 4 |
| 17 | `EMCON` | with 7 | As verb 7 |

Which of these the FLASH COMM page can send is that page's rule, in [`mfd.md`](../formats/mfd.md#mfdflashcomm--mode-1): verbs 0-5, 7 and 8. Neither screen can send 6 or 9.

## Receiving one — `Mech_ReceiveSquadOrder` (`00420ad4`, mech vtable `+0x28`)

`short __cdecl(mech, order, priorReply)`. Returns 1 when the order was taken. Twelve arms of the jump table cover sixteen verbs; each arm picks a reply id, and posts it through `Ai_PostSquadMessage` only when `priorReply` is 0, or is 1 with an order this machine accepted. The ids index the speaker's own `PILOT<n>.STR` set and the lines they read are [`cockpit-messages.md`](../formats/cockpit-messages.md#what-each-id-says)'s catalog; the reply each arm picks is in parentheses below. "Out of action" is `+0xa5` no weapons left, immobilised or destroyed.

| Verb | Refused when (reply) | Otherwise (reply) |
|---|---|---|
| 0 | out of action (`0x1b`); descriptor `+0x0c` set, already broken off (`0x0d`); the player has nothing selected, or it is neutralised (`0x12`); already engaging it (`0x0f`) | `+0x23e` = 4, `+0x248` = the selection, `+0x250` = its tier, engage (`0x11`) |
| 1 | out of action (`0x1e`); the player has nothing selected (`0x12`); `+0x9a` already set (`0x0f`) | Set `+0x9a` (`0x16`). A machine committed **to that same target** either zeroes its dwell, if its descriptor `+0x0a` (holds place) is set, or retargets. A standing engage order on that target is cleared before the `+0x9a` test, so it is cleared even when the order is then refused |
| 2 | out of action (`0x1b`); nothing within 60000 of the player is shooting at it (`0x1d`); already committed to it (`0x1a`) | Same install as verb 0, against the nearest such machine (`0x0b`) |
| 3, 10, 15 | immobilised (`0x1b`) | `Behaviour_SetState(patrolling)`, `+0x23e` = 0. `0x1e` inside 25000 of the leader, `0x14` outside |
| 4, 16 | — | `+0x96` = 1, `+0xb2` = 1. `0x26`, or `0x0f` when `+0x96` was already 1; taken either way |
| 5 | out of action (`0x1b`) | `+0xb6` = 1. A committed machine has nothing to start (`0x20`). Otherwise it acquires (`Ai_SelectTarget`). If that finds something it clears `+0x23e` and — when the group's own order is `guarding` and the machine is still within 100000 of the guarded object — re-enters `Mech_AiSelectBehaviour` rather than leaving the post; anything else engages what it found. Taken and answered `0x1c` even when nothing was found |
| 7, 17 | — | `+0x96` = 0, `+0xb2` = 0 (`0x28`) |
| 8 | — | `+0xb6` = 0 (`0x0c`). A committed machine re-enters `Mech_AiSelectBehaviour`, **then** `+0x23e` = 0 |
| 0xb | out of action (`0x1b`); already engaging the subject (`0x0f`); the subject is neutralised (`0x1d`) | Same install as verb 0, against the record's subject (`0x11`) |
| 0xc | out of action (`0x1b`); already guarding the same thing (`0x20`) | `Behaviour_SetState(guarding)`, `+0x23e` = 6 (`0x17`) |
| 0xd | immobilised (`0x1b`); `+0x23e` already 2 and the machine within 2000 of the record's point (`0x20`) | `Behaviour_SetState(patrolling)`, `+0x23e` = 2 (`0x2a`) |
| 0xe | immobilised (`0x1b`); `+0x23e` already 1 and the machine within 2000 of the record's point (`0x20`) | `Behaviour_SetState(patrolling)`, `+0x23e` = 1 (`0x2a`) |

Verbs 0xc, 0xd and 0xe write `+0x240` — and 0xc also `+0x24c` — **whether or not the order was taken**, so a refused re-order still moves the post. Verb 0xc's "same thing" is the same subject object, or — with neither the old order nor the new one naming a subject — the stored point within 1000 of the new one, on the ground plane.

`0x1e` reads `AFFIRMATIVE!` whichever arm posts it. Two arms pick it, and one of them is a refusal: verb 1 from a machine that is out of action. No arm picks the generic no, `0x1f` ([`cockpit-messages.md`](../formats/cockpit-messages.md#what-each-id-says)). A verb with no case leaves the reply id at `0xffff`, and posts that; neither screen can send one.

Verbs 0, 2, 0xb and the three `JOIN ON ME` forms clear the damage accumulator at `+0x281` ([`ai-targeting.md`](ai-targeting.md)) when they are taken, and verb 8 always does.

Verbs 0, 2 and 0xb go through `Mech_AiEngageOrderedTarget` (`0041c0f4`), which picks the state — [`ai-targeting.md`](ai-targeting.md#the-ai-writers-of-0x1a4) — as do verbs 1 and 5 when they retarget or acquire.

## The standing order

The writers of `mech+0x23e` store five values: 1 move, 2 patrol, 4 engage, 6 guard, and 0 to clear. `Mech_ReceiveSquadOrder` stores all five; `Mech_Constructor`, `Mech_AiSelectBehaviour`, `Mech_AiOnTakingFire` and `Ai_ClearSquadEngageOrder` store 0. Where each is read:

| Reader | Uses |
|---|---|
| `Mech_AiSelectBehaviour` (`0041eb34`) | In a group whose first member is `PlayerMech`: 1 and 2 install `patrolling`, 6 `guarding`, 4 engages `+0x248` — or, with that target destroyed, clears the verb and re-enters. [`ai-dispatch.md`](ai-dispatch.md#choosing-a-state--mech_aiselectbehaviour-0041eb34) |
| `Ai_NavigationStep` (`0041d598`), `Ai_DriveToPoint` (`0041fac4`) | Any nonzero verb drives at `+0x240` instead of the group's route, and engages the Turbo Pod when more than 30000 out. [`ai-navigation.md`](ai-navigation.md) |
| `Mech_AiGoalPosition` (`0041dbcc`) | 6 works to `+0x24c`, or to `+0x240` when that is null; 3 works to `+0x24c` and 5 to `+0x248` ([Open](#open)) |
| `Mech_AiCombatReassess` (`0041cf18`), `Mech_AiOnTakingFire` (`0041f7b8`) | 4 names the target to keep. In the reassess, 1 — and 0 under a travel or follow group order — ends a fight for a committed machine. [`ai-targeting.md`](ai-targeting.md) |
| `Mech_BehaviourPatrolThink` (`0041d7d0`), `Mech_BehaviourGuardThink` (`0041e224`) | A nonzero verb opens the leader gate; 2 and 4 change what is acquired. [`ai-navigation.md`](ai-navigation.md) |
| `Ai_ShouldAbandonTarget` (`0041c4a8`), `Ai_ClearSquadEngageOrder` (`0041c478`) | 4 is satisfied only past `+0x250`, and clears when its target is let go. [`ai-combat-states.md`](ai-combat-states.md) |
| `Mech_BehaviourAttackBaseThink` (`0041c86c`) | 4 with a target makes the structure being attacked count as finished only once destroyed, as one the group's order names does. [`ai-combat-states.md`](ai-combat-states.md#attacking-base-6--mech_behaviourattackbasethink-0041c86c) |
| `Mech_SquadOrderLineIndex` (`0041bac8`) | 1, 2, 3 and 6 override the `OBJECTIVE:` line. [`heads-down-display.md`](../formats/heads-down-display.md) |

## The three latches

None of them is a squad order; all three are per-machine flags this handler sets ([Open](#open)).

| Offset | Set by | Cleared by | Read by |
|---|---|---|---|
| `+0x9a` | verb 1 | verbs 0, 5 and 0xb, each before its own refusal tests; and `Mech_AiOnTakingFire` when the shooter *is* the player's selection | `Ai_IsTargetable` (`00411e80`), which refuses this machine the player's current selection while it is set |
| `+0xb6` | verb 5 | verb 8 | The leader gate in `Mech_BehaviourPatrolThink`, `Mech_BehaviourSearchDestroyThink` and `Mech_BehaviourGuardThink` — [`ai-navigation.md`](ai-navigation.md) |
| `+0xb2` | verbs 4, 16 | verbs 7, 17 | `Ai_UpdateWeaponsFree`, and the radar step of `Mech_AiCombatReassess` and of `Mech_BehaviourGuardThink`'s hand-off to a fight state — [`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set). Also switches a squadmate's **ECM pod**, which follows the radar mode — [`missile-lock.md`](missile-lock.md#ecm) |

## Mech fields this slice owns

| Offset | Type | Meaning |
|---|---|---|
| `+0x23e` | short | The standing order's verb, 0 for none |
| `+0x240` | int[2] | Where the order sends the machine. Two ints, not three: only the ground plane is stored, and `+0x248` follows immediately |
| `+0x248` | ptr | Verb 4's target |
| `+0x24c` | ptr | Verb 6's subject, or null for bare ground |
| `+0x250` | short | Verb 4's abandon threshold — [`ai-targeting.md`](ai-targeting.md) |
| `+0x9a`, `+0xb2`, `+0xb6` | byte | Above |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The FLASH COMM page lists six of the eighteen orders, or offers two orders on each of its six rows | It lists eight: rows 0-3 are fixed at verbs 0-3, and only rows 4 and 5 alternate, `SCAN FOR HOSTILES`/`EMCON` and `FIRE AT WILL`/`HOLD YOUR FIRE` |
| `mech+0x9a` stops a squadmate shooting at the player's target | It removes that one object from the *acquisition* candidate set. A machine already holding it keeps it; what drops it is verb 1's own retarget branch, which only runs for a machine committed to that exact target |
| Verb 2, `HELP ME OUT!`, clears `mech+0x9a` like the other orders that ask for a fight | Verbs 0, 5 and 0xb clear it; verb 2 has no write to it |
| `Squad_BroadcastOrder` ranks the group by fitness for the order | For eight verbs. For the others the score is not assigned, every member carries the same leftover value, and the ranking is range alone or, with a leftover below -1, nobody is told |
| The order record's point is the only thing `DEFEND POSITION` stores | It stores both: `+0x240` takes the point and `+0x24c` the subject, and `Mech_AiGoalPosition` prefers the subject, so a guard ordered onto a machine follows that machine |

## Open

- **Open:** what verbs 3 and 5 mean as values of `+0x23e`. No writer storing either is found by `es2_fieldscan.py 23e` over the whole image or by grepping the decompile for the offset, though `Mech_AiGoalPosition` (3 at `+0x24c`, 5 at `+0x248`) and `Mech_SquadOrderLineIndex` both handle them and `Mech_AiSelectBehaviour` installs nothing for either, so a machine carrying one would keep the state it had and steer at `+0x240`.
- **Open:** group 0 entries 6 and 9 have no text and no case. Entry 6 is what FLASH COMM row 3 would name if it ever toggled, and entry 9 is out of reach of that page's `+3`; neither screen can send either.
- **Open:** `Squad_BroadcastOrder`'s score local (`[EBP-0x1c]`) is not assigned for a verb outside its switch, and `JOIN ON ME` (3) is the one such verb FLASH COMM sends. Whether that broadcast reaches anyone depends on whether the leftover stack value is below -1; the value has not been traced.
- **Open:** the order record's writers above are the ones a raw scan of DBSIM finds for every pointer into `004d0400`-`004d0500` (two loads of `004d0458` itself, fourteen of the squadmate array at `004d044c`, each bounded to its three slots, and the palette fade arrays that end at `004d0428`), with the record pointer followed into each callee, plus a sweep of bulk writes with an absolute destination. A reference built from a more distant base would escape both.
- **Open:** no setter of `+0x9a`, `+0xb2` or `+0xb6` outside `Mech_ReceiveSquadOrder` is found by `es2_fieldscan.py` over the whole image or by grepping the decompile; the one other write to an `+0xb6` is `Main_StaticInit`'s, to the global block at `004d2540`.
