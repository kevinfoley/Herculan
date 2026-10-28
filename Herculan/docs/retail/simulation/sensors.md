# Sensors and radar mode

What makes one object *known* to another — radar visibility and contacts, swept once a tick — and the radar switch that decides whether a machine emits. What the player can select from what is known is [`target-selection.md`](target-selection.md); how an AI machine picks its target is [`ai-targeting.md`](ai-targeting.md).

## The sensor model — `Sim_DetectionTick` (`004123ac`)

Runs once per tick from `Sim_MainTick`, **after** every object update and the input poll, immediately before the per-mech systems pass. Two distinct notions:

- **Radar visibility** (`obj+0x95`) is a property of one object: something with an active scanner painted it. Set by the sweep, cleared wholesale by decay.
- **A contact** (`obj+0xc2 + otherListIndex`) is a property of a *pair*, made by looking and shared sideways to the spotter's own side.

`obj+0x4b` is the object's slot in the single live-object list (`ObjectList_Add`, `00411dd4`) and is what indexes both the contact table and the line-of-sight cache at `obj+0x132`.

### Passes

1. **Timers.** `obj+0x1e2` (LOS cache) and `obj+0x1e5` (contact decay) tick; an expired decay runs `Detection_DecayContacts` (`0041251c`) on the spot, reloading at `10000 + rand(1000)`.
2. **Sweeps.** `Detection_Sweep` (`004128f8`) for each live human-side object, over Cybrid objects only — but it writes *both* objects' tables, so a Cybrid learns without sweeping. The locally-piloted machine (`obj+0xa3`) is held back and swept **last**, so squadmates' contacts have already been shared to it.
3. Clears `obj+0xa2`, the ECM gate on engagement actions ([`mission-deployment.md`](mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6)).

### `Detection_Sweep` (`004128f8`) ranges

| Range | Effect |
|---|---|
| < 200000 | Scanner paints, if either object emits and not both are already painted, with LOS |
| < 140000 | A scanner paints an object that is *not* emitting back |
| < 80000 | The looking half runs at all |
| < 50000 | For a pair that each hold the other as a contact already: sets both objects' `+0x9e` and fires each one's engagement action unless its own `+0xa2` is set. Such a pair skips the looking |

Looking: bearing plus aim twist against the ±`0x3800` sensor arc (`SimObject_BearingInSensorArc` (`00411acc`), vtable `+0x44`), then LOS. The sweep dispatches the arc through each object's own table, and all five structure classes install `Base_BearingInSensorArc_Always` (`00405308`) there, so a structure has no arc and sees all round. An AI machine's contact goes to `Detection_ShareContact` (`00412704`), which shares it to everything on its side within 100000 and engages within 50000 ([`mission-deployment.md`](mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6)); the player's machine keeps it to itself. The reciprocal bearing is tested from the other object's arc in the same pass.

Just before it paints the Cybrid, and just before it makes a contact of its own, the sweep calls the sweeping object's vtable `+0x48`, the "enemy detected" callout, which tests the state those two writes are about to change — [`ai-targeting.md`](ai-targeting.md#radio-callouts). The reciprocal test makes no call.

Decay (`Detection_DecayContacts`, `0041251c`) clears the object's own `+0x95`, then drops each contact it holds on a deployed enemy that is farther than **100000** measured **on the ground plane only** (`Math_GroundDistanceBetweenPoints`, where every other range here is the 3D approximation) or has no LOS, mutually.

### Line of sight — `Detection_LineOfSight` (`00412608`)

A terrain ray between the two objects' aim nodes (`+0x1c` of the vtable `+0x24` record — see [`aim-point.md`](aim-point.md) — or 500 when that slot returns nothing), cached per pair on the observer. **The cache gate tests the *other* object's countdown and reloads the observer's** — verified at `00412617`/`0041263f` (`CMP word ptr [ESI+0x1e3]` against `MOV word ptr [EBX+0x1e3]`), so it is not a decompiler slip. Since only human-side objects sweep, Cybrid timers stay at zero and pairs are re-walked whenever asked.

## Radar mode

`mech+0x96` is PASSIVE/ACTIVE. The player has two switches, and they are not the same code:

- **[R]** is `Mech_ToggleRadarMode` (`0041b468`), called from `CockpitWidgets_HandleCommand` ([Open](#open)). It flips the field only when `obj+0xa3` is set, and whether or not it flipped plays the tone and posts the radar-mode message for the mode the field now holds ([`cockpit-messages.md`](cockpit-messages.md), [`audio.md`](audio.md)).
- **The F4 scanner's PASS and ACTIVE buttons** are `MfdButton_OnClick` (`0044681c`), which *sets* the field to 0 or 1 on the viewed machine (`view+0x203`) directly — no `obj+0xa3` test, no message, no tone. See [`mfd-scanner.md`](mfd-scanner.md#buttons).

**The player's HERC starts each mission passive** in retail ([Open](#open)). `Base_Construct` latches it on for structure types 5, 6, `0x1d`, `0x1e` — the radar masts. An AI machine's radar is set by the writers [below](#how-an-ai-machines-radar-is-set); an anti-radiation hit and destruction clear it.

What the mode does to the player's reach is in [`target-selection.md`](target-selection.md#can-this-be-targeted--targetselect_cantarget-00433174). Radar mode is also what an AI machine's ECM pod follows — see [`missile-lock.md`](missile-lock.md#ecm).

### How an AI machine's radar is set

`Ai_UpdateWeaponsFree` (`0041c3c8`) is the AI's radar switch. Its name says weapons-free; it is not a trigger gate, and nothing in the fire path reads `+0x96`:

```
mech+0x96 = mech.Group.Leader.IsPlayer ? mech+0xb2 : mech+0x97
```

`mech+0x97` is the mission file's own per-mech flag — `.MSN` row #12 `+0x08`, `script.dat` block 7 `+0x00`, written once by `DBSim_SpawnMissionObjects` — so **the mission author decides whether a Cybrid patrol walks its route lit up or dark**. `mech+0xb2` is the player squad's radar order, written by `SCAN FOR HOSTILES` and `EMCON` ([`ai-squadmates.md`](ai-squadmates.md)). `mech+0x26b` is the counter of a radar-silence timer, 6000 after an anti-radiation hit; the writers that test it are marked below.

| Writer | Effect |
|---|---|
| `Ai_UpdateWeaponsFree`, from `Ai_NavigationStep`, `Mech_BehaviourGuardThink` and `Mech_BehaviourSleepThink` | The standing setting above, re-asserted each tick. Does not test the silence timer |
| `Mech_BehaviourTravelThink` / `Mech_BehaviourFollowThink` | On their 10000-count acquisition timer only: ACTIVE if it found something to look at or `+0x97` is set, otherwise PASSIVE. No silence test |
| `Mech_AiCombatReassess` | ACTIVE, unless the group's leader is the local player and `+0xb2` is clear, in which case PASSIVE. The ACTIVE arm waits for the silence timer — [`ai-targeting.md`](ai-targeting.md#the-combat-reassess--mech_aicombatreassess-0041cf18) |
| `Mech_BehaviourGuardThink`, on handing off to a fight state | ACTIVE, on the same player-squad condition |
| `Ai_ChooseWeapon` | ACTIVE when a SARH launcher (subtype 0) comes into range, if the silence timer is clear — that class of missile needs its own illumination ([`ai-weapons.md`](ai-weapons.md#choosing-a-weapon--ai_chooseweapon-0041f358)) |
| `Mech_DirectFireHitTest`, on a machine the player is not flying | ACTIVE when hit by a subtype 0 or 1 round, if the silence timer is clear. **PASSIVE, and held there for 6000, when hit by a subtype 2 (ARM) round** |
| `Mech_ReceiveSquadOrder` | The squad's radar verbs, [`ai-squadmates.md`](ai-squadmates.md) |

The subtype 2 row is the anti-radiation missile working: a machine that takes an ARM hit goes dark and stays dark long enough for the seeker to lose it.

## Rejected readings

| Obvious reading | Actually |
|---|---|
| `mech+0x96` is a weapons-free flag, and `Ai_UpdateWeaponsFree` is the AI's trigger gate | It is the radar mode. `Rocket_HomingSteer` homes an ARM on it, `Mech_DirectFireHitTest` clears it on an ARM hit, and the detection sweep reads it as the scanner. Nothing in the fire path consults it; the mission-file field feeding it is the mission's radar setting, not a rule of engagement. The Ghidra symbol keeps the misleading name — see [How an AI machine's radar is set](#how-an-ai-machines-radar-is-set) |

## Open

- **Deferred:** other callers of `Mech_ToggleRadarMode` (`0041b468`). `es2_xref.py` finds one rel32 call, from `CockpitWidgets_HandleCommand`, and no stored pointer or vtable slot.
- **Deferred:** a writer of `mech+0x96` at construction. `es2_fieldscan.py 96 --writes-only` finds none in a machine constructor; a wider store over `+0x93`-`+0x95`, a bulk copy or an alias the scan cannot resolve would escape it. The player's machine starting passive is the retail behaviour either way.
