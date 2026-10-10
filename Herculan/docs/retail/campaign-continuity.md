# Campaign continuity

What one campaign mission's result changes in the missions after it, read from v1.0's 50 campaign `.MSN` files. The order of play never changes: there is no alternate route, and a lost mission is followed by the same next mission as a won one. What a result changes is the content of later missions, the squad's size, the chassis and weapons the armory can build, and whether the war goes on at all.

## How a result is carried

The campaign has one store for all of this, the 1,000-word flag array ([`shell/campaign-loop.md`](shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store)). A mission's conditions read it twice: when the mission loads, which decides what is in it, and again at its own debrief, which picks its debrief text ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#row-5--the-debrief)). Four things carry a result into that array:

- **The outcome, flag 0.** The debrief stores 1 for a won mission and 0 for a lost one. The next mission's load tests it and its header patch then clears it ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#the-header-patch--row-2)), so only the mission immediately after sees it.
- **Mission counters.** An object, or a whole group, adds to a flag when it goes out of the fight ([`simulation/mission-deployment.md`](simulation/mission-deployment.md#the-out-of-action-report)), and an objective adds or subtracts the first time its condition holds ([`simulation/mission-objectives.md`](simulation/mission-objectives.md#the-record)). Apart from the slots the debrief grants from and the few the simulator resets at each load ([`simulation/mission-deployment.md`](simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4)), they keep their value for the rest of the campaign, so any later mission can test them.
- **Debrief grants.** The debrief reads fixed flags to unlock chassis and weapons and to stock weapon units ([below](#unlocks-and-weapon-units)), whether the mission was won or lost. It skips them when the player's machine was destroyed, since the campaign then ends.
- **Hard-coded events.** The advance to `C1_04` moves the player into bay 4, and the advance to `C1_07` withdraws the Razor ([`shell/campaign-loop.md`](shell/campaign-loop.md#where-the-debrief-goes-next)). Both happen whatever the outcome.

The hangar, the pilots, the salvage pool and the armory stock also carry over from mission to mission, by the debrief's accounting ([`shell/campaign-loop.md`](shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)). This doc covers what the missions themselves do with the flags.

A test changes a mission through what its condition gates: a roster record, a group, an order, an objective, the header patch, or a line of text. A record that repeats an earlier record's GUID overlays it, copying every field it sets ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#repeated-guids)). This is how most consequences are written: a conditioned group record with the GUID of an existing group adds members to it, and a conditioned structure record sets a structure's starting condition to 0.

## Losing a mission

`Career_Advance` (`00412dc7`) moves the career on one mission after every debrief, won or lost. A loss ends the war in several places ([`shell/campaign-loop.md`](shell/campaign-loop.md#where-the-debrief-goes-next)):

- the last mission of chapters 1 to 4, `C1_10`, `C2_10`, `C3_10` and `C4_10`, since losing it moves the position onto the next chapter's first mission;
- any mission of chapter 5.

A destroyed player's machine ends the campaign in any mission. Both endings offer [`REPLAY MISSION?`](shell/campaign-loop.md#replay-mission).

Every other loss continues to the next mission, which may then differ in the ways below.

## Chapter 1 — Alpha sector

| Mission | Briefing | What it carries forward |
|---|---|---|
| `C1_01` | Perimeter patrol | Flag 250 counts the five scout Hercs (mechs 213-217) put out of the fight. In `C1_02` it sizes a Cybrid group of Mirimacs at 50% condition that joins the attack on the base (group 191, deploying on action 153): four machines with no scout killed, three with one, two with two, one with three or more. In `C1_07` it picks the three machines of groups 85 and 86: three Ramses with none killed, then one Ramses fewer and one Stingray more per kill, three Stingrays from three kills. |
| `C1_02` | Defend the laser lab | The L400 unlock ([below](#unlocks-and-weapon-units)). |
| `C1_03` | Recover the downed Buzzard | Flag 270 is set when the Buzzard (flyer 70) is destroyed. In `C1_04` the squad then patrols route 40, four waypoints, in place of route 37, five. |
| `C1_04` | Razor test flight | A loss adds three Pitbulls to `C1_05` (groups 217-219, deploying on actions 167-169), hunting the stranded pilot's group. Its three Pitbull records are set to write flag 282, which `C1_05`'s intelligence line tests, with an operation that writes nothing ([below](#unreachable-branches)). |
| `C1_05` | Protect the ATC 75 prototype | The ATC75 unlock. |
| `C1_06` | Escort the Maverick | The Maverick unlock. |
| `C1_07` | Destroy the Cybrid lander | Flag 310 is set when the lander (structure 72) is destroyed. If it is not, `C1_10` adds a group of two Ramses and a Stingray (group 187) attacking the base, and a briefing line saying so. |
| `C1_08` | Raid the listening post | Flag 321 is set when the data link completes, flag 320 when the listening post is destroyed. A win makes `C1_09` the virus upload: a header patch sets the mission objective type to 7 ([`formats/script-dat.md`](formats/script-dat.md#header-format)), and its objectives are to complete a data link at the listening post without destroying it. A loss makes `C1_09` a base defence: its one objective is the base clear of threats, the squad's first order becomes guarding the base, and Cybrid groups 204 and 205 attack the base instead of the squad. In `C1_10`, flag 321 adds four Ramses on the human side (groups 192-195, deploying on action 155) guarding the base, the Cybrids whose IFF the virus scrambled. They depend on `C1_08`'s download, not on `C1_09`'s upload. |
| `C1_09` | Defend the base, or upload the virus | Its outcome picks `C1_10`'s briefing (text lines 202, 201 and 200, in that order) and intelligence (203 and 204), with `C1_07` and `C1_08` refining each. After a loss, the briefing calls the attack all-out and, if the `C1_07` lander survived, says its Hercs support it; the intelligence reports medium and light Hercs on several vectors. After a win, the briefing adds a line about the lander's Hercs if it survived; if `C1_08`'s listening post still stands, the intelligence says a third squad, from the south, appears infected with the virus; and if `C1_08` also downloaded the data, the briefing says the Hercs deployed before the attack are compromised and that IFF-scrambled Cybrids show as friendly. Two cases contradict the mission. A base defence won after a `C1_08` that left the post standing without downloading gets the infected-squad line, though no virus was uploaded and no friendly Cybrids appear. And the four friendly Ramses, which follow `C1_08`'s download alone, go unmentioned whenever `C1_09` was lost or the post was destroyed after the download. |
| `C1_10` | Defend the command base | Must be won. |

The Alpha sector command base appears in `C1_01`, `C1_02`, `C1_05`, `C1_06`, `C1_09` and `C1_10` — see [Persistent bases](#persistent-bases).

## Chapter 2 — Delta sector

| Mission | Briefing | What it carries forward |
|---|---|---|
| `C2_01` | Hunt the Pitbulls | Flag 350 counts the three tracked Pitbulls (mechs 141-143) put out of the fight. In `C2_02` it picks the four machines of the Cybrid group that deploys on action 80 (group 113): three Pitbulls and a Stingray with none killed; two Pitbulls, a Ramses and a Stingray with one; a Pitbull, two Ramses and a Stingray with two; four Stingrays with three. Two or fewer also add a briefing warning. Each of the three also stocks one `EMP2` unit. |
| `C2_02` | Find and destroy the Cybrid base | A loss adds three Stingrays to `C2_03` (group 129) attacking the munitions plants. The base's structures persist into `C2_08`. |
| `C2_03` | Defend the munitions plants | The L500 and ATC100 unlocks. |
| `C2_04` | Raid the listening post for targeting data | The TARG unlock. |
| `C2_05` | Escort the Raptor II | The Raptor II unlock. The factories' fate (group 126, flag 390) changes only the debrief text, which can call the programme cancelled while the unlock's own count is met. |
| `C2_06` | Intercept the Cybrid patrols | Flag 401 is set when Cybrid group 104 is out of the fight. If it is not, `C2_07` adds three machines (group 131, Diablos and Achilles, deploying on action 102) to the assault. The same group is set to write flag 402, which `C2_07` tests to add two Raptor IIs on the human side (group 123), with an operation that writes nothing ([below](#unreachable-branches)). |
| `C2_07` | Defend the base | Nothing forward. |
| `C2_08` | Rescue the turbo pod | The TURB unlock. |
| `C2_09` | Raid the refinery | The Razor unlock. A loss adds three machines to `C2_10` (group 121, Diablos and Achilles) and a briefing line naming the refinery as their staging area. |
| `C2_10` | Hold Delta sector | Must be won. |

The Delta sector base appears in `C2_01`, `C2_03`, `C2_04`, `C2_05`, `C2_07`, `C2_08` and `C2_10`, and the Cybrid base in `C2_02` and `C2_08`.

## Chapter 3 — Omicron sector

| Mission | Briefing | What it carries forward |
|---|---|---|
| `C3_01` | Cut off the Ogre's pursuers | The Ogre unlock. |
| `C3_02` | Find the Cybrid landers | Flag 204 is set when the first lander (structure 89) is destroyed. If it is not, `C3_03` adds three Mongooses (group 143) hunting the squad. |
| `C3_03` | Raid the listening post for shield data | The SHLD unlock. |
| `C3_04` | Pre-emptive patrol | Flag 480 counts the eight tracked Cybrids (mechs 65-72) put out of the fight. Fewer than five changes `C3_05`'s intelligence line to say they reinforce the attack; no machine is added. |
| `C3_05` | Defend the energy pod base | The ENRG unlock, and one `ENRG` unit. |
| `C3_06` | Escort the technician | Its outcome picks `C3_07`'s briefing text only. |
| `C3_07` | Destroy our own radar outposts | Nothing reachable ([below](#unreachable-branches)). |
| `C3_08` | Locate the command elements | Nothing forward. |
| `C3_09` | Destroy the command elements | A loss adds three machines to `C3_10` (group 112, drawn from Diablo, Achilles, Hyperion and Scarab, deploying on action 86). |
| `C3_10` | Defend the command post | Must be won. |

The Omicron sector base appears in `C3_01`, `C3_02`, `C3_03`, `C3_05`, `C3_06`, `C3_07`, `C3_08` and `C3_10`.

## Chapter 4 — Bravo sector

Two missions here set how many squad positions the next one has. The positions in play are 1 plus the unbroken run of members group 0 names from its second slot ([`shell/main-menu.md`](shell/main-menu.md#starting-a-practice-mission)), and a won mission's overlay on group 0 adds a slot.

| Mission | Briefing | What it carries forward |
|---|---|---|
| `C4_01` | Defend the base | A win gives `C4_02` two squad positions; a loss, one. |
| `C4_02` | Rescue two trapped pilots | A win gives `C4_03` three squad positions; a loss, two. |
| `C4_03` | Defend the command post and factories | Nothing forward. |
| `C4_04` | Head off the attack | A loss adds one machine to `C4_05` (group 72, drawn from Diablo, Achilles, Hyperion and Headhunter, deploying on action 31) hunting the player's squad. |
| `C4_05` | Escort the recon team home | A loss changes only `C4_06`'s recommended strike package ([below](#unreachable-branches)). |
| `C4_06` | Four bases under attack | Nothing forward. |
| `C4_07` | Save what you can | Nothing forward. |
| `C4_08` | Scout the landing zone | Flag 620 is set when the three landers of group 100 are all out of the fight, and adds a briefing line to `C4_09`. Flag 621 counts the six landers destroyed; four or more change the formation `C4_09`'s three landers (group 97) stand in, from 3 to 0. The landing zone's structures persist into `C4_09` and `C4_10`. |
| `C4_09` | Clear the lander guard | Flag 631 counts the six landers destroyed; four or more change the formation of `C4_10`'s three target landers (group 79) from 3 to 2. |
| `C4_10` | Capture the landers | Must be won. |

## Chapter 5 — the Moon

Every mission must be won. One result is carried: the lunar outpost captured in `C5_01` is the base defended in `C5_05`, so its structures persist ([below](#persistent-bases)).

`C5_05`'s briefing asks the player to choose between the base and the landers. Its only objectives are the base's group (group 111) clear of threats and not lost; nothing records what happened to the landers.

## Persistent bases

A base that several missions place carries its damage between them, one flag per structure. Each structure's out-of-action report adds 1 to its flag, and every mission placing the base repeats the structure's record conditioned on that flag being above 0, with a starting condition of 0, which places it collapsed ([`simulation/structure-behaviour.md`](simulation/structure-behaviour.md#starting-condition)). Damage short of destruction does not carry: a structure comes back either whole or collapsed.

| Base | Placed in | Flags | Structures with a collapsed record |
|---|---|---|---|
| Alpha sector command base | `C1_01`, `C1_02`, `C1_05`, `C1_06`, `C1_09`, `C1_10` | 100-133 | all 30 |
| Delta sector base | `C2_01`, `C2_03`, `C2_04`, `C2_05`, `C2_07`, `C2_08`, `C2_10` | 144-156, 255-257 | 10 of 16 |
| Delta sector Cybrid base | `C2_02`, `C2_08` | 157-171 | all 15 |
| Omicron sector base | `C3_01`, `C3_02`, `C3_03`, `C3_05`, `C3_06`, `C3_07`, `C3_08`, `C3_10` | 217-233, 263-269, 475 | 18 of 25; `C3_08` places 11 of them |
| Bravo sector landing zone | `C4_08`, `C4_09`, `C4_10` | 622-629, 642-649, 775-782 | 20 of 24, including three landers in `C4_10` that are not its targets |
| Lunar outpost | `C5_01`, `C5_05` | 800-828 | all 28 |

Destroying a structure that has a collapsed record, in any mission of its set, leaves it collapsed in every later one; the others come back whole. `C2_09` and `C3_09` track their refineries on the same flags, 134-143, and only `C2_09` reads them.

A comparison of every condition and what it gates between v1.0 and v1.10 finds two differences: v1.10's `C5_05` drops the collapsed records of four of the lunar outpost's structures (flags 821, 823, 825 and 827), and its `C2_03` adds a second set of debrief loss-line conditions.

## Unlocks and weapon units

The debrief unlocks a chassis or a weapon when its flag holds an exact value, and clears the flag either way ([`formats/herc-catalogs.md`](formats/herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5), [`formats/weapons-dat.md`](formats/weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be)). Each flag is written by one campaign mission's objectives, each adding or subtracting 1 the first time its condition holds. The mission's outcome plays no part: what counts is which objectives were met at some point and which failure conditions came true.

| Unlock | Mission | Flag | Needs | How the flag moves |
|---|---|---|---|---|
| L400 | `C1_02` | 50 | 1 | +1 the base clear of threats; −1 the base lost |
| ATC75 | `C1_05` | 52 | 2 | +1 the stranded pilot's Herc clear of threats; +1 the rescue group finishing its route; −1 the pilot's Herc lost |
| Maverick | `C1_06` | 61 | 2 | +1 the Maverick clear of threats; +1 the squad finishing its route; −1 the Maverick lost |
| L500 and ATC100 | `C2_03` | 51 and 53 | 1 each | +1 the munitions plants clear of threats; −1 the plants lost |
| TARG | `C2_04` | 57 | 1 | +1 the data link |
| Raptor II | `C2_05` | 60 | 2 | +1 the prototype clear of threats; +1 the squad clear of threats; −1 the prototype lost |
| TURB | `C2_08` | 58 | 2 | +1 the technician's Raptor II clear of threats; +1 Cybrid mech 169 put out of the fight; −1 the technician's Raptor II lost |
| Razor | `C2_09` | 63 | 1 | +1 the refinery group written off |
| Ogre | `C3_01` | 62 | 2 | +1 the Ogre clear of threats; +1 the base clear of threats; −1 the base lost. Losing the Ogre subtracts nothing |
| SHLD | `C3_03` | 56 | 1 | +1 the data link; −1 the listening post lost |
| ENRG | `C3_05` | 59 | 1 | +1 the base clear of threats; −1 the base lost |

`C3_03`'s briefing says the listening post may be destroyed once the data is downloaded. Destroying it is a failure condition, and its −1 cancels the SHLD unlock.

**Weapon units** come from kills. Specific Cybrid machines in chapters 1 to 3 store 1 in a unit-grant flag when put out of the fight, and the debrief stocks one unit of that weapon for each flag set ([`simulation/mission-deployment.md`](simulation/mission-deployment.md#the-out-of-action-report)). A store means one unit per weapon per mission however many such machines fall. Nearly every mission of chapters 1 to 3 stocks some of `ATC50`, `MSL10`, `EMPC`, `ELFW`, `L300`, `EMP2` and `ELF2` this way. The scarcer grants:

| Weapon | Where |
|---|---|
| `TARG` | `C2_04`'s Ramses and Stingrays |
| `TURB` | `C2_06`, `C2_07` and `C2_08` |
| `SHLD` | `C3_02` and `C3_03`; and in each of `C4_08`, `C4_09` and `C4_10`, one when a particular Cybrid structure group is wiped out |
| `EMP2` | one per tracked Pitbull in `C2_01`, up to three |
| `ENRG` | `C3_05`'s base first clear of threats |

A granted unit does not unlock its weapon; the armory row stays disabled until the unlock above.

## Replaying a mission

`REPLAY MISSION?` replays from the autosave the shell made at launch, but the simulator then reads the flags the failed attempt left in `data\mission.var` ([`shell/campaign-loop.md`](shell/campaign-loop.md#replay-mission)). Every counter above carries over from the failed attempt into the replay:

- kill counts (flags 250, 350, 480 and the landers' 621 and 631) add both attempts together;
- a structure destroyed in the failed attempt counts as destroyed for the missions after, even if the replay saves it, though it stands in the replay itself, whose mission file is not rebuilt;
- an unlock count gathers both attempts' objectives: one that needs 1 reaches 2 when both attempts earn it, and one that needs 2 can reach 3 or 4. An unlock earned before the player was killed is lost if the replay earns it again.

## Unreachable branches

`C3_08` and `C4_06` are written as though a later record replaced an earlier one, which no row of the format does, and `C3_05` uses another layer's operation code. `C1_05` and `C2_07` depend on operation `0x17`, which no layer of the game implements: the out-of-action report acts on 1, 2 and `0x0d`-`0x10` ([`simulation/mission-deployment.md`](simulation/mission-deployment.md#the-out-of-action-report)), objectives on 4-7 and actions on 5 and 6.

| Mission | Branch | Why it is never taken |
|---|---|---|
| `C1_05` | The intelligence line for a successful `C1_04` scouting run | It tests flag 282, which only `C1_04`'s three Pitbulls write, with operation `0x17`, beside a working write of the salvage bonus. The line is always the variant reporting no intelligence on the Cybrid force. |
| `C2_07` | Two Raptor IIs on the human side (group 123), and the two briefing variants crediting the intelligence `C2_06` gathered | Flag 402 is written by `C2_06`'s group 104 with operation `0x17`, beside the working write of flag 401 that keeps that group out of `C2_07`. |
| `C3_08` | Diablos in the Cybrid groups 119-121 after a `C3_07` loss | The Diablos are a second variant table under key 105, conditioned on the loss. `Msn_PickVariant` (`00415fc3`) reaches a second table only with draws the first leaves uncovered ([`formats/msn-mission-file.md`](formats/msn-mission-file.md#variants)), and the first draws below 150 and covers 0-149. `C1_04` and `C1_05` have the same shape under keys 46 and 172 for the squad's own slot, which the hangar fills anyway. |
| `C3_05` | An `ENRG` unit for each of the two Cybrid machines 103 and 104 put out of the fight | Their reports write flag 42 with operation 6, the objective layer's increment, which the out-of-action report has no case for. The objective's own `ENRG` unit is still granted. |
| `C4_06` | Two squad positions after a `C4_05` loss | The loss's overlay on group 0 sets members 0 and 1. An overlay copies only member refs it sets (`Msn_MergeRow16`, `00417286`), so the base record's third member stays and the squad keeps three positions. |
| `C5_10` | Its debrief text, placeholder lines | A win ends the campaign before the debrief text is built, and a loss ends the war ([`shell/campaign-loop.md`](shell/campaign-loop.md#where-the-debrief-goes-next)). |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Losing a mission can send the campaign down another route — another sector, or other missions. | `gam\career.dat` is a flat list per chapter ([`shell/campaign-loop.md`](shell/campaign-loop.md#the-campaign-table--gamcareerdat)) and `Career_Advance` steps through it whatever the outcome. A loss changes what the next missions contain, or ends the war. |
| A structure's health carries from one mission to the next. | Only whether it was destroyed carries: one flag per structure, tested for being above 0, choosing a starting condition of 0. |

## Open

- **Deferred:** what operation `0x17` was meant to do. Both its uses, `C1_04`'s scouting run and `C2_06`'s intelligence gathering ([Unreachable branches](#unreachable-branches)), fit a flag set when the player scans a particular target.