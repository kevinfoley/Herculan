# Mission objectives (DBSIM.EXE)

What the mission wants done, what loses it, and the one number the whole layer produces. The records are `script.dat` block 12 and their text is block 13 plus `data\mission.str`; see [`../formats/script-dat.md`](../formats/script-dat.md#block-12-in-memory--76-bytes-0x4c) for the layout and [`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#row-17-field-decode--the-objective-record-dat_0047064a-58-bytesrecord) for where an author writes them.

This is a separate mechanism from the mission **actions** in [`mission-deployment.md`](mission-deployment.md). An action is a latch that makes something happen; an objective is a question that is asked over and over and never makes anything happen. They share only the mission-counter array.

## The record

An objective is **one condition asked of one subject**, plus what satisfying it does to the mission counters. `Mission_EvaluateObjectives` (`00413280`) walks the array each poll.

| field | meaning |
|---|---|
| `+0x00` | **required.** `== 1` means the objective must be satisfied; **anything else means the opposite** — the record is a failure condition and the mission is lost the moment it comes true |
| `+0x02` | which condition is asked |
| `+0x04` | subject kind: 0 group, 1 mech, 2 flyer, 3 base |
| `+0x06` | the resolved subject |
| `+0x0a` | a block-1 point, `-1` in all 62 retail missions; no reader found ([Open](#open)) |
| `+0x0e` | a block-3 waypoint group — which of the subject group's ten orders condition 0 is about |
| `+0x12` | the resolved failure text, four `char*` |
| `+0x24`/`+0x38` | ten mission-counter refs and ten operations |
| `+0x22` | runtime: the counters have been applied |

**The counter operation codes are not the action layer's.** Here 4 sets, 5 clears, 6 increments and 7 decrements; `Action_Activate` knows only 5 and 6 and reads them as clear and increment, so the two layers disagree on 6 with nothing to warn a reader. A slot is skipped unless **both** its ref and its operation are non-negative. The array is [`mission-deployment.md`](mission-deployment.md#the-mission-counters--dat_004a9ef4)'s.

The counters are applied the first time the condition holds and never again; the latch is not "the objective is met", which is re-read every poll.

## What each condition asks

`Mission_EvaluateObjectives` (`00413280`) writes the cases out twice, once for a group subject and once for an object one, each as an eleven-entry jump table on codes 0–10 (`004132d6`, `004133de`) whose entry 5 is the default. They are the same ten questions.

| code | of a group | of an object |
|---|---|---|
| 0 | the order that runs the record's waypoint group is flagged complete (`Mission_GroupOrderCompleteOnRoute`, `0041324c`) | the same, asked of the object's own group |
| 1 | written off — `Group_ConditionTier` at or past the side's threshold | destroyed or immobilised (`+0x99 \|\| +0xa4`) |
| 2 | not written off, and every living member is clear of threats | clear of threats |
| 3, 4 | the player has completed a data link (`player+0xa0`) — the subject is not looked at | as for a group |
| 6 | any member has been engaged (`+0x9e`) | engaged |
| 7 | **every** member is disarmed (`+0xa5`; `Group_AllMembersDisarmed`, `00412c58`, which answers yes for an empty group) | disarmed |
| 8 | condition 6 negated | condition 6 negated |
| 9, 10 | the player has *not* completed a data link | as for a group |

**Code 5 has no case in either switch**, and neither does a subject kind above 3. The original leaves its working register untouched, so such a record silently answers whatever the record before it answered — or, as the first record, whatever the caller left in the register ([Open](#open)). A negative code takes the same path, since the bound check is unsigned. No retail mission reaches either: across the 127 objective records in the 62 `.MSN` files the codes used are 1 (67), 2 (27), 0 (19), 3 (5), 6 (5), 4 (3) and 7 (1), and the subject is a group 92 times, a mech 22 and a structure 13 — never a flyer. 91 records are mandatory and 36 are failure conditions. **Codes 8, 9 and 10 are unused as well**, so neither negation — not engaged (8) and no data link (9, 10) — is exercised by anything that ships.

**A group's write-off threshold is not the same for both sides** (`Group_IsWrittenOff`, `00413920`): a human group is written off at condition tier 3, a Cybrid one only at 4. So "wipe out this Cybrid group" means every machine, and "this convoy did not make it" is answered a tier earlier. The tiers are [`ai-goals.md`](ai-goals.md)'s.

### Clear of threats — `Mission_IsClearOfThreats` (`004137b4`)

The escort objective's whole test, and the gate on every conclusive mission status. It walks the live object list, skipping anything undeployed, on the subject's own side, or already out of the fight, and the subject is **not** clear when a survivor either:

- knows about it (`Ai_KnowsObject`, [`ai-targeting.md`](ai-targeting.md)) and is within 80000 units — 100000 if the subject is that machine's own selected target; or
- **for a subject not on the player's group**, belongs to a group whose current order names the subject, and is either still on its way (its route has somewhere left to go) or already knows where the subject is. An assigned hunter counts at any range, which is what stops an escort being called safe while something walks towards it.

## The status — `Mission_Status` (`004135e8`)

The judgement, in the original's order: the player's own condition first, then the mission box, then the objectives.

```
if (player destroyed)                      2
else if (player immobilised)               3
else if (!quiet && outside box + 110000)   8
else if (!quiet && outside box)            7
else                                       EvaluateObjectives()
```

The box is block 1's own extent, `Mission_Box` (`004aa6c4`), which `DBSim_LoadScriptDat` (`00424308`) accumulates as it reads the coordinates; `Mission_IsOutsideBox` (`0041373c`) tests a position against it widened by a margin, 0 for answer 7 and 110000 for answer 8. The Heads-Down Display's map is framed by the same one ([`../formats/heads-down-display.md`](../formats/heads-down-display.md)).

**`quiet` is the third argument, and it means "just answer the question".** Set, the function skips the 500 ms hold, both box arms and the `SYSTEM.STR` post, and only computes. The poll clears it; the player's own [Q] clears nothing else and sets it — so **a [Q] can never answer 7 or 8**, and a player standing outside the mission box is told how the objectives stand as though they were inside it.

`EvaluateObjectives` reduces the array to three outcomes and then splits each by whether the player is clear:

| | player clear | player still in contact |
|---|---|---|
| a failure condition holds | **6** mission failed | 10 |
| every required record holds | **9** mission successful | 10 |
| neither | 5 | 4 |

**The mission does not conclude while the player is still in a fight.** All three outcomes collapse to 10 there, and 4, 5 and 10 announce nothing. As the mission ends, `Mission_WriteResults` asks `EvaluateObjectives` once more and records 9 alone as a success ([below](#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)).

The first required record that is *not* satisfied is published at `DAT_004d1f1c` as the failure text the alert panel prints — four `char*`, three from the file and an empty fourth.

## What the computer says

Four statuses carry a `SYSTEM.STR` line, posted on the **change** rather than each poll:

| status | line |
|---|---|
| 6 | `0x16` MISSION FAILED |
| 7 | `0x1e` APPROACHING MISSION ZONE BOUNDARY |
| 8 | `0x20` RULES OF ENGAGEMENT VIOLATED. MISSION ABORTED. |
| 9 | `0x17` MISSION SUCCESSFUL |

After a post the answer is held still for 500 ms so the caller's next poll cannot queue the line twice; the running baseline catches up on the first evaluation after that, which is what sequences the spoken line ahead of the alert panel. `MISSION OBJECTIVES COMPLETE`, `PRIMARY OBJECTIVE COMPLETE` and `SECONDARY OBJECTIVE COMPLETE` are recorded but have no poster found — see [`../formats/cockpit-messages.md`](../formats/cockpit-messages.md#posters).

## The poll — `Mission_PollStatus` (`004131ac`)

`Sim_MainTick`'s last act, run with the player's machine and only while it is not destroyed. Two countdowns shape it, and between them they are why a finished mission takes several seconds to say so:

Both are `CountdownTimer` records, a byte followed by the short counter at `+1` that `Math_CountdownTimerTick` steps; the poll passes each record's base, so neither counter is read by its own address.

- **The poll interval** (`MissionPollTimer`, `004a9ee6`; counter `004a9ee7`) is re-armed to 10000 counts — about 4.9 seconds, see [`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units) — every time the answer is not worth raising, so the objectives are read about once every five seconds. A player **outside the mission box** skips the interval and is read every tick, which is what makes the boundary warning prompt. The one other writer is [`Ai_ChooseWeapon`](ai-weapons.md#running-dry--mech0xa5): when a machine runs out of weapons it raises the counter to at least 1000, about half a second, so the next poll is never sooner than that.
- **The alert delay** (`MissionAlertTimer`, `004a9ee9`; counter `004a9eea`) is armed the first time an alert-worthy status appears, latched by `MissionAlertArmed` (`004a9eec`), and the status is not handed up until its 10000 counts run out. The [Q] path clears the latch after its panel closes, so the next alert-worthy status waits the full delay again.

Status 2 has an exemption from the delay here, and `Sim_MainTick` declines to raise a 2 the poll returns, but neither can happen: `Sim_MainTick`, the poll's one caller, polls only while the player's machine is not destroyed, which is the very test `Mission_Status` answers 2 on. A destroyed player's mission is ended by [the death camera](#the-status-alert--gnl_alrt-00455934) instead.

A status is worth raising when `DAT_0049935c[status]` is set — 2, 3, 6, 7, 8 and 9. The caller builds the [status alert](#the-status-alert--gnl_alrt-00455934) for it. `DAT_004a9ed0` is the status already raised, which is what stops the same one being raised twice; `Mission_StatusForAlert` (`00413180`) is the wrapper both this and [Q] go through, and **the [Q] path writes that baseline as well** — reading the status yourself is enough to stop the poll announcing it.

`Sim_MainTick` also rewrites one answer before it builds the panel. On a status 3 it first runs `Group_ApplyOutnumberedDamage` (`00423f08`) over the player's group, and if that destroys the player the status becomes **18**, the same "disabled" alert with a worse ending. Each member not [clear of threats](#clear-of-threats--mission_isclearofthreats-004137b4) weighs the strength around it, split by side: every deployed HERC within 45000 that is not out of weapons, immobilised or destroyed counts its chassis' salvage scale (type record `+0x54`, [`component-damage.md`](component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60)), every such flyer within 50000 and armed structure within 35000 counts 500. The friendly sum loses 600, and the member takes `Mech_SpreadImpactDamage(member, 0x40, 0x40)` once per 400 by which the hostile sum exceeds what is left (`Mech_ApplyOutnumberedDamage`, `0041b804`).

## The status alert — `gnl_alrt` (`00455934`)

The panel that says how the mission stands, and the only thing in the simulator that ends one. Three ways in, and they build the same panel:

- **[Q]**, scancode `0x10` in `Sim_DispatchCommand`. Asks `Mission_StatusForAlert(player, publish)` with the publish flag set — a quiet evaluation, so never 7 or 8 — and raises the panel for the answer.
- **the poll**, once the answer is worth raising and its delay has run out.
- **the player's death.** While the player's machine is destroyed, `Sim_MainTick` runs [the death camera](external-views.md#the-player-death-camera) where it would poll, and when the camera's countdown runs out it raises the panel for status 2.

The first two then compare the button the player pressed against `DAT_0049f5d8[status]`, and **that comparison is what ends a mission**; the death's call ends it whatever was pressed, which status 2's one button would have done anyway. The answer goes up as `Sim_MainTick`'s return, out of `Sim_DispatchCommand` and `Sim_PollPlayerInput` first in [Q]'s case. [Q] stays live while the death camera runs, and answers 2.

| status | panel | ends on |
|---|---|---|
| 2, 3, 8, 18 | one button, `CONTINUE` | that button — the mission is already over |
| 7 | one button, `CONTINUE` | **nothing.** Its entry is 1 and it has no button 1, so the boundary warning can only be acknowledged |
| 4, 6, 9, 10 | two, `CONTINUE` + `ABORT MISSION`/`RETURN TO BASE` | the second |
| 5 | two, `CONTINUE` + `QUIT ANYWAY` | the second |
| 17 | one button | that button, but its caller is not this panel's |

The panel's text, layout and paint are [`alert-panels.md`](alert-panels.md#the-status-alerts-text-and-layout)'s.

### Closing it

The family's keys ([`alert-panels.md`](alert-panels.md#what-the-family-shares)) and the same modal loop, with one addition: `DAT_004d25b6`, the abort flag the input poll sets, closes the panel from under it. Its `OnChildClick` (`00456160`) writes 2 for button 0 and 3 for button 1, and the loop returns that `& 1` — so the caller reads 0 for the left button and 1 for the right. [Return] presses the focused button and [Esc] the cancel widget, both of which the panel sets to button 0, so **neither key can ever be the answer that ends the mission**; only the pointer can reach button 1.

## The objectives panel

What [F11] puts up lists block 13's lines and tests nothing; block 12's conditions are never shown to it. The panel is [`alert-panels.md`](alert-panels.md#the-objectives-panel--obj_alrt-0045751c)'s.

## The player think's objective arms

`Mech_BehaviourPlayerThink` (`0041c194`) carries the waypoint arm ([`player-waypoints.md`](player-waypoints.md#the-players-think--mech_behaviourplayerthink-0041c194)) and then exactly one objective arm, chosen by the mission's own selector — `script.dat`'s header at `+0x06`. **They are the only writers of `+0x9f` and `+0xa0` in the image.** Conditions 3, 4, 9 and 10 read `+0xa0` back; the only reader of `+0x9f` is [the unreached group report](#the-group-report-and-why-nothing-shows-it), so selectors 0 and 5 have no effect on the mission beyond selector 0's message.

| selector | arm |
|---|---|
| 0 | **the mission target is picked up.** One-shot for the whole run: the first time the player's own selected target (`mech+0x1a4`) is what their group's current order names, post `0x19` MISSION TARGET DETECTED and raise `+0x9f`. It fires on the pilot selecting the thing, not on the sensors finding it |
| 5 | **the goal is reached.** Within 40000 ground units of `Mech_AiGoalPosition` raises `+0x9f`. Says nothing, and four times the waypoint arm's range |
| 3, 7 | **the data link**, below |
| other | nothing |

Selector 0 is what a mission gets when its header patch sets none. The patches set 2 (`TRAIN1`, `TRAIN3`, `TRAIN4`, which reaches no arm), 3 (`TRAIN2` and six campaign missions) and 7 (`C1_09`, behind a condition) — [`script-dat.md`](../formats/script-dat.md#header-format). No mission sets 5.

Selector **3** also reaches into the AI: `Ai_IsTargetable` refuses the current order's target to a group led by the player's machine, so the squad does not shoot the thing the player came to read. Selector 7 does not get that shield. See [`ai-targeting.md`](ai-targeting.md#is-it-a-target-at-all--ai_istargetable-00411e80).

### The data link

The player parks in front of what their order names and holds position. Holding needs four things at once: the subject alive, its group in the mission, the player within 10000 units in three dimensions, and the player's **aim** — body heading plus turret twist — within 45° of it. Nothing tests speed, so the link can be held while walking past. Breaking off posts `0x38` DATA TRANSFER ABORTED and puts the sequence back to the start.

Four lines are spoken, `0x34` to `0x37`, each after the delay the table at `0049a318` gives for the step before it: 5000, 5000, `0xffff9c40`, 0. **The link therefore takes about five seconds of holding station** — 5000 counts is about 2.4 seconds; the third entry is negative, `Timer_CountDown` clamps at zero, and `DATA TRANSFER COMPLETE` is queued the tick after `TRANSFERRING DATA`.

That is not what the player sees. The two are queued a tick apart but shown ten seconds apart, because `TRANSFERRING DATA` is the one `SYSTEM.STR` entry whose display timings are 10 s and 20 s rather than 3 s and 6 s and the port will not let a message yield before its minimum ([`../formats/cockpit-messages.md`](../formats/cockpit-messages.md#the-port)). So the transfer reads on screen as a long operation while the simulation has already finished it: `+0xa0` goes up when the last line is *queued*.

## The group report, and why nothing shows it

Eight functions sit among the ones above, read the same order records and the same per-machine flags, and produce a small integer that is plainly a line index. `Group_StatusLineIndex` (`00412f90`) is the head of the set. `es2_xref.py` finds no relative branch, stored pointer or vtable slot holding it, so nothing reaches it ([Open](#open) covers what that sweep cannot see). Only one of the other seven is called from outside the set.

```
Group_StatusLineIndex(group, verb):
    writtenOff = Group_ConditionTier(group) == 4
    tier       = min(Group_ConditionTier(group), 3)
    switch (verb) {
        0  writtenOff || !anyMember(+0x9f) ? tier+6 : anyMember(+0x9e) ? tier+2 : 1
        1  !routeExhausted ? tier+6 : tier == 0 && anyMember(+0xa6)     ? 1 : tier+2
        2  !routeExhausted ? tier+6 : tier == 0 && subjectCondition == 4 ? 1 : tier+2
        3  member[0] destroyed || !anyMember(+0xa0) ? tier+5 : tier+1
        4  !subjectArrivedAndClear || subjectCondition > 2 ? tier+5
           : subjectCondition == 0 && tier < 2 ? 1 : max(tier, 1) + 1
        5  subjectArrivedAndClear && anyMember(+0x9f) && subjectCondition != 4 ? tier+1 : tier+5
        6  subjectCondition != 4 ? tier+6 : tier == 0 && !anyMember(+0x9e) ? 1 : tier+2
    }
    return result - 1
```

`routeExhausted` is the **group's own** route cursor at `+0x04`, not the subject's. So each verb answers in one of three bands — 0 for done cleanly, `tier+1`/`tier+2` for done, `tier+5`/`tier+6` for still running — with the group's damage tier sliding the answer inside its band. It is a per-group "how is this squad doing" line, one the mission never asks for.

The six helpers:

| | Asks |
|---|---|
| `Group_AnyMemberEngaged` (`00412d90`) | any member's `+0x9e` |
| `Group_AnyMemberObjectiveSighted` (`00412ef4`) | any member's `+0x9f` |
| `Group_AnyMemberDataLinked` (`00412f28`) | any member's `+0xa0` |
| `Group_AnyMemberScoredAKill` (`00412f5c`) | any member's `+0xa6` |
| `Group_OrderSubjectEngaged` (`00412d4c`) | the current order's subject — the group form for kind 0, the object's own `+0x9e` otherwise |
| `Group_OrderSubjectArrivedAndClear` (`00413a08`) | the current order's subject is deployed and clear of threats |

`Group_AnyMemberEngaged` is the one called from outside: `Mission_EvaluateObjectives` calls it too, which is what makes condition 6 work. `Group_OrderSubjectEngaged` is the set's second head — it calls `Group_AnyMemberEngaged`, and nothing calls it. The other four helpers are called only by `Group_StatusLineIndex`.

`Group_OrderSubjectRouteExhausted` (`004139a0`) sits in the middle of the set and is a further head: `es2_xref.py` finds no reference to it of any kind, `Group_StatusLineIndex` included.

**`mech+0xa6` therefore has no live reader.** `Mech_CreditNeutralisedTarget` latches it on a machine's first cross-side kill ([`component-damage.md`](component-damage.md#what-the-attacker-is-told--mech_creditneutralisedtarget-00415710)) and the only reader `es2_fieldscan.py` finds in the simulator's objects is `Group_AnyMemberScoredAKill`. The same goes for `+0x9f` (`Group_AnyMemberObjectiveSighted`) and for `+0xa0` *in its group form* — the objective conditions read the player's own `+0xa0` directly rather than through `Group_AnyMemberDataLinked`.

## What the mission leaves the shell — `Mission_WriteResults` (`0042412c`)

Every way out of the simulator ends in `Sim_Shutdown` (`00461eec`), which `Sim_Run` (`0045f144`) calls when its loop ends: a mission-ending answer to the [status alert](#the-status-alert--gnl_alrt-00455934), [Ctrl+Q]'s `QUIT` ([pause panel](alert-panels.md#the-pause-panel--004561c0)), and the end of a demo. Closing the window is one of those: the main window's `WM_CLOSE` dispatches `0x410`, [Ctrl+Q], outside a demo, and raises `DemoAbort` (`004d25b6`) in one. A `WM_QUIT` that reaches the input poll raises `DemoAbort` as well (`0045a811`). It closes the tape files and calls `Mission_WriteResults(LocalPlayerMech)`, which writes `data\results.dat` and then the mission counters back over `data\mission.var` ([`mission-deployment.md`](mission-deployment.md#the-mission-counters--dat_004a9ef4)). The file's layout is [`campaign-loop.md`](../shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)'s; what fills it:

| Field | From |
|---|---|
| outcome | `Mission_EvaluateObjectives(player) == 9`. The objectives are walked once more, so a record first met now applies its counters before they are written; the player's own condition and the mission box play no part |
| salvage award | `Q10(2500, Mission_TotalSalvage)` plus 25,000 kg per unit of counter 20. `Mission_TotalSalvage` (`00423e88`) sums [`Mech_SalvageValue`](component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60) over every machine off the player's side that is destroyed or immobilised, walking the machine list from its end |
| salvage pairs | the list `Salvage_QueueWeapon` (`00426ac8`) built: the enemy wrecks' surviving mounts, queued by that walk, after every Cybrid mount the [destruction roll](weapon-mounts.md#the-chance-path--the-destruction-roll) knocked off during the mission |
| a block per machine | `Group_WriteStatusBlocks` (`00423d68`) over the player's group, in group order: 33 conditions, then the machine's kill tallies |

**The 33 conditions** are the [damage readouts](../formats/mfd.md) the damage screens read, entries 1-13, 20-29 and 32-41 — the first thirteen components on their own armour, the first ten dependents, and the ten weapon mounts with their paired dependent — each turned from a Q8 damage reading into a percentage condition as `((0x100 - reading) * 100) >> 8`, an arithmetic shift where the decompiler shows an unsigned one. The shell reads the 66 bytes straight over the machine's status block ([`../formats/save-games.md`](../formats/save-games.md#the-66-byte-status-block)).

**A destroyed player's block always reads 0 for the pilot**, dependent 9, which is the reading the debrief takes as the pilot killed ([`campaign-loop.md`](../shell/campaign-loop.md#where-the-debrief-goes-next)). The death gate's finish-off writes 30000 on the front cockpit ([`component-damage.md`](component-damage.md#going-out-of-the-fight)), every chassis but the SPIDER keeps its pilot there ([`dmg-damage-file.md`](../formats/dmg-damage-file.md#which-internals-each-component-holds)), and on all nine player chassis the cockpit's armour and the maxima behind it total less — at most 17050, OGRE's.

**The kill tallies** are `mech+0x2a4`, one short per target class: `Mech_CreditNeutralisedTarget` adds one at `victim+0x1a8` on a machine's first cross-side neutralisation of each victim (`0041576d`). The block carries the first three, classes 0, 1 and 2 — the Herc, Base and Flyer kills the shell adds to the pilot's record. A ground vehicle, class 3, is tallied and never reported.

The exit code follows, into `004d283c`; the codes are [`../command-line.md`](../command-line.md#exit-codes)'s.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Every objective the player is shown is an objective the simulation tests | Block 13 and block 12 are separate data and nothing reconciles them. An author writes the lines the player reads and the conditions the code tests independently |
| Reaching the last waypoint completes a "travel there" objective | Condition 0 reads the group's order-completed flag at `+0x70`, which `Group_OrderTick` sets — so the objective is about the **order** finishing, not about arrival |
| Meeting every objective ends the mission | It yields status 9 only while `Mission_IsClearOfThreats` also holds for the player. With a live hostile aware and near, the status is 10 and nothing is announced |
| `+0x00` is a priority, with higher meaning more important | The evaluator's only test is `== 1`. One is mandatory; every other value makes the record a failure condition, which is the opposite meaning rather than a weaker one |
| The data link finishes instantly because its third delay clamps to zero | The delay is written before the line it precedes, so the link still needs its two waits of holding; the clamp only removes a wait after the last line is decided |
| `DAT_0049f5d8` is a per-status button count | It is which button index ends the mission. Status 7's entry is 1 against a one-button panel, which is how its warning is made unanswerable rather than a count being wrong |

## Open

- **Open:** no reader of objective `+0x0a` found by `es2_fieldscan.py a --range 00413280-004135e5` over `Mission_EvaluateObjectives` (controls `+0x06` and `+0x0e` fire).
- **Open:** whether anything reaches the group report cluster (`Group_StatusLineIndex`, `Group_OrderSubjectEngaged`, `Group_OrderSubjectRouteExhausted`) through a static-initialiser registration. `es2_xref.py` finds no branch, pointer or vtable slot for any of the three, but a registered function can be absent from that sweep, and `RegisterSubsystemLoader` (`00401d64`) has many callers.
- **Open:** what the working register holds when `Mission_Status` and `Mission_WriteResults` call `Mission_EvaluateObjectives`. It decides how a code-5 or out-of-range-kind record answers when it is first in the array; no retail mission has such a record.
