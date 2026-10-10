# The armory, the repair bay and the salvage economy

What the shell does with the numbers in [`../formats/herc-catalogs.md`](../formats/herc-catalogs.md) and [`../formats/weapons-dat.md`](../formats/weapons-dat.md): buying a chassis, queueing weapons, repairing damage and scrapping what is not worth keeping. **Every address in this doc is in `VSHELL.EXE`**, and the shell's source module is named in the assertion strings for each function cited.

## One currency, two units

There is a single resource — **salvage** — and it lives in one pool at `00482af4`, seeded at career start ([`campaign-loop.md`](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed)) and spent on everything below.

**The pool is in kilograms, and the screens print it in both units.** The crew screen and the Herc Construction screen divide it by 1000 and print tons (`estext.bin` `0x2f` `Tons`, `0xc6` `TONS`), as both scrap dialogs print their yields (`0xcd` `tons of salvage.`); the repair screen's detail panel and the armory readout print the pool net of the build queue in kilograms, against `0xc8` `kg` ([`build-and-armory.md`](build-and-armory.md#the-armory-readout)). The two catalog price fields are stored in tons and multiplied by 1000 when charged:

| Quantity | Stored | Charged / compared |
|---|---|---|
| chassis price, `herc_inf.dat` `+0x08` | tons | `x1000` by `Herc_Order` (`00411019`) |
| weapon price, `weapons.dat` `+0x14` | tons | `x1000` at load by `WeaponsDat_ReadRecord` (`00411d57`) |

So a 107,000 pool is 107 tons, an Outlaw at `+0x08 = 60` costs 60,000, and `WPN_INFO.BIN`'s prose `Salvage Required: 5,000 kg` for the `ATC20` is that weapon's stored 5 read back in the third unit the game uses for the same thing.

The Herc Construction screen prints a chassis price without converting ([Open](#open)), formatting the raw `+0x08` against `TONS` — correct, and the reason the stored figure looks like a display value.

## Buying a chassis — `Herc_Order` (`00411019`)

The Herc Construction screen's `BUILD` buys ([`build-and-armory.md`](build-and-armory.md#scrapping-and-building-are-gated-on-the-bay)), and nothing on the buying path tests anything: the screen's gates stand in front of it. The chassis's row is live only while its availability flag, `herc_inf.dat` `+0x0e`, is set, and `BUILD` only on an empty selected bay and while the pool is more than the price *after* whatever the weapon queue has already committed (`DAT_00482af4 - Armory_QueuedTotal() > price`, compared unsigned).

`Herc_Order` builds the record in place in the selected bay — type, capacity from the in-code table, `+0x4a` progress 0, no hardpoints occupied, `+0x78` set to `herc_inf.dat` `+0x0c` — and returns the price, which `Hangar_BuySelected` (`0040e91c`) takes off the pool at once. A bought chassis therefore arrives **empty, unbuilt and paid for**, and `Herc_BuildTick` (`00411086`) advances it one mission per debrief until `+0x78` reaches zero.

Retail's `gam\hercs.dat` opens a new career with this state already on the books: a Razor at 0% with three missions to run ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamhercsdat--the-starting-hangar)).

## The weapon build queue — `Armory_*` (`armory.cpp`, `00411efd`)

Weapons are not bought off a shelf; they are queued into five build slots and delivered later. The queue is two globals, reset by `Armory_ResetQueue` (`0041213d`) to five free slots and five empty ids:

```
0046f8d4   int16       free slots, 0-5
0046f8d6   5 x int16   one weapon catalog id per slot, 0 = empty
```

The armory screen prints all three views of it: `Armory_QueuedTotal` (`00412586`) sums `weapons.dat` `+0x14` over the occupied slots against `Allocated:` (`estext.bin` `0xd6`), the free count against `Workspace Available:` (`0xd3`), and `5 - free` against `Workspace In Use:` (`0xd4`) ([`build-and-armory.md`](build-and-armory.md#the-armory-readout)).

- **Enqueue** — `Armory_Enqueue` (`004125e7`) takes the first free slot from `Armory_FirstFreeSlot` (`004125bb`) and decrements the free count; with no slot free it does nothing.
- **Dequeue** — `Armory_Dequeue` (`0041260d`) clears every slot holding that id and increments the free count per slot cleared.
- **Delivery** — `Armory_DeliverQueue` (`00412428`) allocates a ten-byte weapon unit per queued id, initializes it as `{ id, 100, 100, ammo type }` — ammo type `1` for the missile racks (ids 13–16), `5` for everything else — appends it to that catalog record's owned-unit list via `Armory_AddUnit`, and returns the total price to charge. It leaves the queue as it was. With weapons built automatically the debrief's `Armory_RefreshQueue` then refills it from scratch; built by hand, it is only trimmed to the pool, so the same queue is delivered and charged again at every debrief until the player changes it.
- **Trim** — `Armory_TrimQueueToBudget` (`004123ba`) clears queued slots from the front while the pool cannot cover the committed total, refunding workspace as it goes.
- **Auto-fill** — `Armory_AutoFillQueue` (`00412341`) resets the queue and then walks the catalog in rank order through `WeaponsDat_IdAtRank` (`0041230c`), enqueueing every weapon that is unlocked, that the player owns **fewer than two of**, and that the running total still leaves affordable. The "keep two of each" rule is visible in `gam\weapons.dat`'s starting stock, which holds two units of each of sixteen of its eighteen ids.

This queue is [`../formats/save-games.md`](../formats/save-games.md#savgame_sav--block-order)'s block 2, and the `{ index, value }` pairs it writes are these five slots.

## Repairing and scrapping

Both price against the same three-mode view of a machine's 66-byte status block — 13 external component conditions, 9 internal, and one per hardpoint ([`../formats/save-games.md`](../formats/save-games.md#the-66-byte-status-block)) — and against the unit values `damage.dat` expands per chassis ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamdamagedat--the-component-and-weapon-value-table)).

### Repair levels

Two six-entry `int16` tables in the image drive the whole thing:

```
0046fd78   { 100, 89, 79, 59, 29, 0 }   repair target per level
0046fd84   {  90, 80, 60, 30,  1, 0 }   the condition band each level covers
```

`Repair_LevelForCondition` (`0041381c`) returns the first level whose band floor is at or below the condition — so 90 and up is level 0, 80–89 level 1, 60–79 level 2, 30–59 level 3, 1–29 level 4, and 0 level 5.

`Repair_ItemCost` (`0041392a`) prices one component: nothing if it is already at or above the target, otherwise `(target - current) * unitValue / 100`. `Repair_HercCost` (`004139f7`) sums that over the six external groups, the nine internals and every occupied hardpoint, taking each hardpoint's unit value from the per-weapon table at `0048431e`. A hardpoint whose condition has already reached 0 is skipped: a destroyed mount is not a repairable one.

### What one repair level costs

`Repair_LevelStepCost(unitValue, condition, mode)` (`00413871`) prices a **single step up the ladder**, and it is what the repair screen quotes for whatever the player has selected — reached through `Repair_SelectionCost(herc, category, index)` (`00411454`), which picks the unit value for the selection and, for a hardpoint, keys the weapon table by the fitted weapon's id rather than by the slot number.

With `mode` set — the only form the shell uses — the target is the floor of the level *above* the one the component is in, and only a component already at level 0 is priced all the way to 100:

| Level | Condition | Repaired to |
|---|---|---|
| 0 | 90-100 | 100 |
| 1 | 80-89 | 90 |
| 2 | 60-79 | 80 |
| 3 | 30-59 | 60 |
| 4 | 1-29 | 30 |
| 5 | 0 | 1 |

The targets are the second table read one index down — the caller indexes `0046fd82 + level*2`, which for level 1 and up lands in `0046fd84`'s band floors. So the detail panel's figure and the `REPAIR ALL` figure beside it are different quantities, not one scaled from the other: a component at 70 is quoted the 10 points that would take it to 80, while the rebuild beside it is quoted the 30 that would take it to 100.

`Repair_Auto` (`00411328`) is the `Auto Repair` mode (`estext.bin` `0x42`): start at level 0, step down a level at a time while the cost exceeds the budget, stop once the machine's own average condition already sits at the level under consideration, then apply through `Repair_Apply` (`004113af`) — which writes the chosen target across all three arrays, leaving an empty hardpoint at 100. The cost must be strictly below the budget, compared unsigned; if no level's is, nothing is repaired and nothing is charged.

The debrief charges repairs through `Game_AutoRepairSquad` (`0040e804`), which runs `Repair_Auto` by `prefs.cfg` option 44 ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)): over the player's machine in mode 0 only, and over each on-strength squad member's at positions 1 up to the positions in play in any mode but 2. Every machine gets the same budget — the pool divided by the machines on strength, taken once before the first repair — and each cost comes off the pool as it is made. A pool below zero divides to a negative budget, which the unsigned compare reads as vast, so every machine is repaired to 100.

**A full rebuild costs about 72.5% of the chassis price**, uniformly across the fleet: the fifteen `damage.dat` percentages sum to 950 and the two Q10 scale steps (`950/1024` then `800/1024`) land there for every chassis, since both factors are chassis-independent and the only per-chassis input is the price itself.

### Scrapping

`Herc_ScrapValue` (`00413b50`) values a machine by summing `condition * unitValue / 100` over the six external groups and the nine internals, plus the value of each mount too damaged to return to stock. Condition is the multiplier, so a healthy machine is worth more than a wrecked one — this is a yield, not a repair bill (`estext.bin` `0x43` `Salvage Available:`, `0xcc` `This herc will yield`).

`Herc_StripMounts` (`00411795`) decides what survives: a mount at **80 condition or better goes back into armory stock** through `Armory_AddUnit` (`00411efd`), at the condition it was fitted at rather than the hardpoint's ([`weapons-and-repair.md`](weapons-and-repair.md#fitting-a-weapon)), and anything below is destroyed. `Herc_ScrapValue`'s mount loop counts exactly the complement — the ones under 80 — so a returned weapon is credited as inventory rather than as salvage.

At debrief `Herc_SettleAfterMission` (`00410c7c`) applies the same judgement to the machine itself: below 30 average condition it is scrapped out of the hangar for its value and the hangar count drops; at 30 or above only the overall-condition slot is reset to 100.

**The player scraps from the shell** through the scrap dialog's `ACCEPT` ([`build-and-armory.md`](build-and-armory.md#the-scrap-dialog)), which runs `Hangar_ScrapSelected` (`0040e757`) on the selected bay:

1. `HercList_ScrapSelected` (`00410922`) takes the machine through `Herc_Scrap` (`00411432`) — `Herc_ScrapValue`, then `Herc_StripMounts` — frees it, empties the slot and takes one off the hangar count at `00482ae3`. An empty slot yields 0 and is left alone.
2. The value goes into the pool.
3. The pilot `Squad_PilotForBay` finds for the bay has its bay set to `-1`, and the squad member `Squad_MemberAtPosition` finds at that pilot's position, `+0x27`, is taken off strength through `Squad_SetOnStrength` — the pilot itself, for a squad member. The player's position is 0 and no squad member ever holds 0 ([`../formats/save-games.md`](../formats/save-games.md#pilot-record--59-bytes-0x3b-in-memory)), so scrapping the player's machine takes nobody off strength and leaves the player's own on-strength byte as it was.

The debrief's scrap is the same first step with one more write: it also adds one to the hangar's `+0x24`, the debrief's scrap count ([`campaign-loop.md`](campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)), which the shell's leaves alone.

**Weapons are scrapped a whole stock at a time**, from the armory's `Scrap` ([`build-and-armory.md`](build-and-armory.md#the-scrap-dialog)). `Armory_ScrapValueTons` (`0041266a`) values the stock at a tenth of its price in tons — `weapons.dat` `+0x14` truncated to tons, times the count held at `+0x17`, over 10 — and at least 1 ton for a stock that is not empty; an empty one is worth 0. `Armory_ScrapWeapons` (`0040e7b2`) adds `Armory_ScrapStock` (`00412555`) to the pool, which is that figure times 1000 after `Armory_ClearStock` (`00411f9d`) has freed every unit on the record's list, one off the count each. So scrapping gives up every unit for a tenth of their price, the condition of none of them counting.

The pool has grown, and `Armory_RefreshQueue` (`00412413`) then reconciles the build queue with it by the build mode: auto-filled from scratch while weapons are built automatically — and the stock just emptied is below two, so the weapon goes back on the queue in its rank's turn when a slot is free and the pool covers it — or trimmed to the pool while they are built by hand.

**The under-construction branch of `Herc_ScrapValue` pays nothing or everything.** For a machine whose `+0x4a` is below 100 (a signed compare) the value is `((100 - +0x4a) / 100) * price * 1000`, where the price is `herc_inf.dat` `+0x08` in tons, read from the in-memory table at `00483b5c + type * 16` by the record's `+0x00` type — the entry `Herc_Order` charged, which it reaches through `+0x02`, the identity-mapped index of the same type. The `idiv` is a signed integer division and yields 0 for every progress figure from 1 to 99, so only an untouched 0% chassis returns anything, and it returns the full price; a proportional refund would be `(100 - +0x4a) * price * 1000 / 100`. A chassis is at 0% only until the next campaign debrief, whose `HercList_BuildTickAll` (`00410a2b`) moves it to 33, 50 or 100: `herc_inf.dat`'s build times are 1 to 3 missions on all nine chassis. The scrap dialog quotes the same figure through `Herc_ScrapValueTons` (`0041140f`).

The order is confirmed in the instruction stream, not just in the decompiler's parentheses:

```
00413b85  2b c2                 sub   eax, edx                    ; eax = 100 - pct
00413b87  b9 64 00 00 00        mov   ecx, 100
00413b8c  99                    cdq
00413b8d  f7 f9                 idiv  ecx                         ; the divide, on (100 - pct) alone
00413b96  0f bf 8a 5c 3b 48 00  movsx ecx, word [edx+0x00483b5c]  ; price, after it
00413b9d  f7 e9                 imul  ecx
00413b9f  69 f8 e8 03 00 00     imul  edi, eax, 1000
```

Every instruction length chains from the function entry to `00413bac`, which is independently the `jge` target of the `+0x4a` test — so the block is bounded on both ends. The codegen is unoptimized throughout (a real `idiv` by a constant rather than a magic-number reciprocal, and a `push`/`pop` pair to compute one subtraction), which is why no reassociation could have moved the divide.

## What unlocks over the campaign

Two parallel mechanisms, both run by the campaign debrief on the flags the mission left, and both consuming the flag as they grant ([`campaign-loop.md`](campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)):

| | Weapons | Chassis |
|---|---|---|
| Flag | `weapons.dat` `+0x16` | `herc_inf.dat` `+0x0e` |
| Granter | `Armory_GrantCampaignWeapons` (`004126be`) | `Herc_GrantUnlocks` (`004118c5`) |
| Persisted as | save block 1's leading byte per catalog id | save block 7 |
| Ships locked | fourteen, eight of them unlockable | Raptor II, Ogre, Maverick, Razor |

The weapon granter also stocks the armory: a second set of flags adds whole weapon units, at condition 100, at every debrief they are set.

See [`../formats/weapons-dat.md`](../formats/weapons-dat.md#0x16-is-the-weapon-unlock-flag) and [`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5) for each.

A locked weapon's row is disabled and drawn in the background colour, a gap in the list, on the armory and on the weapon-fitting screen ([`weapons-and-repair.md`](weapons-and-repair.md#the-weapons-screen)). A locked chassis is refused by the construction screen, and a machine already in the hangar on a locked chassis cannot be scrapped: both `SCRAP` gates, `Repair_RefreshDetail`'s and `Build_GateButtons`', test the same `+0x0e` flag ([`build-and-armory.md`](build-and-armory.md#scrapping-and-building-are-gated-on-the-bay)). The starting hangar's Razor is such a machine until the Razor unlocks.

## What the armory will sell

Twenty-six of the thirty-three catalog ids have an armory panel, and the seven without one cannot be bought at all: `NONE`, the three Bull weapons, and `LAEW`, `MINE` and `MFAC`. The panel list is `gam\arm_weap.dat`'s record list, and the thirty-entry class table at `0046f868` states the Bull exclusion a second way ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamarm_weapdat)).

`gam\weapons.dat` closes with 39 units of starting stock across eighteen ids — four `ATC50`, three `ECM`, two of each of the other sixteen — every one at condition 100, and every one an id the armory also sells.

## Open

- **Deferred:** no other site that prints a chassis price found. `es2_xref.py --binary VSHELL 00483b5c` lists five absolute references — `Herc_Order`, `LoadDamageDat_Shell`, `Herc_ScrapValue`, `Build_GateButtons` and `Herc_BuildScreenRefresh` — and only the last formats it; a read through a base register would not show there.
- **Open:** no reader of the weapon unlock flag, `weapons.dat` `+0x16`, found that acts on a unit already fitted to a hangar machine. `es2_xref.py --binary VSHELL 00483bfa` lists ten absolute references, in `Armory_AutoFillQueue`, `Armory_GrantCampaignWeapons`, `PlayerMec_WriteUnlockTable`, `Arming_RowLive`, `Arming_BuildScreen`, `Arming_RefreshRows` and `Armory_RefreshRows`. Whether a disabled fitting-screen row stops the player removing a locked weapon already fitted is not established.
