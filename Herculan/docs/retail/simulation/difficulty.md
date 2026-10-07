# Mission difficulty

One number, `0`-`3`, chosen in VSHELL and carried to DBSIM in `data\script.dat`'s header. It is the **player's own pilot skill** in a campaign and a separate practice-mission setting outside one; the simulator knows it only as `MissionDifficulty` (`004a9ee0`) and reads it in four places. The two cheat fields beside it in the same header, `UnlimitedAmmoFlag` and `PlayerInvulnerableFlag`, are [below](#the-two-sibling-cheats).

The four levels are named by `estext.bin` `0x35`-`0x38` — `ROOKIE`, `REGULAR`, `VETERAN`, `ELITE` — wherever they are shown, which is the same run the pilot roster prints a squadmate's skill from.

## Where the number comes from — VSHELL

VSHELL's `MsnGen_LoadMission` writes the header's difficulty, and the two cheat fields beside it, from the campaign/training mode just before it writes the file ([`../formats/script-dat.md`](../formats/script-dat.md#the-training-fields)); the `.msn` parser resets the header words as it starts, and a mission's own [header patch](../formats/msn-mission-file.md#the-header-patch--row-2) is overwritten, so **what a mission authors never reaches the simulator**.

- **In a campaign** it is the player pilot's skill, chosen once on [the registration screen](../shell/screen-layout.md#the-registration-screen). The player's skill never moves afterwards, because the debrief's promotion pass jumps clear of the skill ladder for the player ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#pilot-progression)). So one choice at registration fixes the difficulty of the whole career, and it rides in the save file like any other pilot field ([`../formats/save-games.md`](../formats/save-games.md#pilot-record--59-bytes-0x3b-in-memory)). A squadmate's skill is a separate, rising number and reaches the simulator not at all.
- **Outside a campaign** it is `prefs.cfg` option `0x27`, stepped by [the practice missions screen's](../shell/screen-layout.md#the-parameters) `Mission Difficulty` row. The simulator never reads that option; what reaches it is the header field.

## How it reaches DBSIM — the `script.dat` header

`DBSim_LoadScriptDat` (`00424308`) reads the 20-byte header into `004a9ed2` in one call, so a field's address is `0x4a9ed2 + offset`: `UnlimitedAmmoFlag` at `+0x0a`, `PlayerInvulnerableFlag` at `+0x0c` and `MissionDifficulty` at `+0x0e` ([`../formats/script-dat.md`](../formats/script-dat.md#header-format)).

`MissionDifficulty` has four readers, listed [below](#what-the-difficulty-changes), and the header read is its only writer: `es2_xref.py` on `004a9ee0` and on the block base `004a9ed2` finds no other store.

## What the difficulty changes

Four tables, one per consumer, all four entries wide and all indexed directly by the value:

| Consumer | Table | Entries | Effect |
|---|---|---|---|
| `Damage_ScaleByDifficulty` (`00426b04`), side 0 | `DamageScaleBySide0Difficulty` (`0049a73c`) | 3500, 2800, 2100, 1400 | Q10 — a **human** shot does 3.42x down to 1.37x |
| the same, any other side | `DamageScaleByCybridDifficulty` (`0049a744`) | 300, 600, 800, 1000 | a **Cybrid** shot does 0.29x up to 0.98x |
| `Ai_FireAtPoint` (`0041f5a0`) | `AiAimScatterByDifficulty` (`0049a30c`) | 1000, 800, 400, 200 | how far a Cybrid machine's aim is thrown off — see [`ai-weapons.md`](ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0) |
| `Mech_CollisionTest` (`00418f74`) | `SlideDamageScaleByDifficulty` (`0049a058`) | 400, 800, 1200, 1600 | the scale on the damage a long slide does on landing, player only — see [`mech-locomotion.md`](mech-locomotion.md#the-landing) |

Every table moves monotonically against the player.

### The damage scale reaches all direct fire, not just plasma

`Damage_ScaleByDifficulty` takes a damage figure by pointer and the **firing side**, read as `owner->group->side` (`owner+0x45`, `group+0x12`) at every call site. It has three call sites, in two functions:

- `Sim_RaycastObjectList` (`00426528`) calls it twice, scaling **both** of the shot record's damage figures — `+0x06` then `+0x04` ([`weapon-firing.md`](weapon-firing.md#the-shot-record)) — at the top of the sweep, whenever the record names an owner. Every beam and every bullet goes through it, and the record is rebuilt from the catalog each time, so nothing compounds.
- `Bullet_TickUpdate` (`0040b124`) scales the plasma round's blast figure separately, on a local copy of the figure ([`projectiles.md`](projectiles.md#the-plasma-branch)). The plasma branch empties the shot record before the raycast, so the raycast's own scaling finds zeros there and the round is scaled exactly once.

So a difficulty step changes what **every weapon in the game** does, on both sides, in opposite directions — not only the one round with a blast.

### The two sibling cheats

`UnlimitedAmmoFlag` (`004a9edc`) and `PlayerInvulnerableFlag` (`004a9ede`) ride in the same header and are gated by `MissionModeFlag` (`004a9ed6`), header `+0x04`, which `DBSim_LoadScriptDat` **sets to zero immediately after reading it** (`004246bb`) — every retail file carries 1 there. Its other readers are the three functions below and `Sim_Shutdown`, whose exit code 4 the zero keeps unreachable ([`../command-line.md`](../command-line.md#exit-codes)). With it held at 0:

- `Sim_DamageToPlayerDisabled` (`004240f4`) — which gates the first line of the player's own component damage — answers `PlayerInvulnerableFlag == 1`. Invulnerability is live; the `MissionDifficulty == 0` arm beside it is the one the zeroing takes out, so **`ROOKIE` is not an invulnerability setting**.
- `WeaponMounts_ArbitrateEnergy` (`004107e4`) and `WeaponMounts_FireTrigger` (`00410dbc`) build their free-shot flag as `UnlimitedAmmoFlag == 1`.

Both are `== 1`, not "nonzero", and both are the header field alone: `prefs.cfg`'s options `0x25` and `0x26` are what VSHELL steps to *write* those fields, and DBSIM never opens that half of the file.

Each reaches one mechanism, and both are the player's machine alone:

- **Invulnerability** skips the whole of `Mech_ComponentDamageWrite`, shields excepted ([`component-damage.md`](component-damage.md#the-component-damage-system)).
- **Unlimited ammunition** is one free-shot flag and one refund: the ammunition dispatch skips its round spend on the flag ([`weapon-firing.md`](weapon-firing.md#the-ammunition-dispatch)), and the energy arbitration returns the budget it was called with ([`reactor-energy-pool.md`](reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4)).

`Ai_FireAtPoint` (`0041f5a0`) pushes a literal 0 into that dispatch slot, so no AI machine can reach either half of the free shot. `WeaponMounts_FireTrigger` builds the flag from the globals without testing the owner, because `Mech_PlayerFireTick` (`00415608`) is its only caller; the arbitration, which every machine runs, tests `owner+0xa3` itself.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Difficulty runs 0-4 | Each table is four `int16` entries. `0049a73c` and `0049a744` are 8 bytes apart and admit no fifth entry, and slot 4 of the slide table is `TargetingPodComponentRotation` (`0049a060`), unrelated data. Both screens that set the number step it modulo 4, and the skill ladder the campaign feeds it from caps at 3 |
| Difficulty is a property of the mission | A mission's [header patch](../formats/msn-mission-file.md#the-header-patch--row-2) does write the field: of the 62 retail `.MSN` files, 30 patch in 2 (`C1_01`-`C3_09` and `DEMO2`), 23 patch in 3 (`C3_10`, `C4_01`-`C5_10`, `DEMO` and `TRAIN6`) and nine patch none (`TRAIN1`-`TRAIN5`, `TRAIN7`, `TRAIN8`, `DEMO_01`, `DEMO_02`). `MsnGen_LoadMission` overwrites it in both modes before the file is written — with the player's skill in a campaign and the practice screen's option outside one — so what a mission authors never reaches the simulator |
| The difficulty scale applies to plasma blast damage only | That is one of `Damage_ScaleByDifficulty`'s three call sites. The other two are in `Sim_RaycastObjectList`, on the two damage figures of every direct-fire shot |
| `MissionDifficulty == 0` makes the player invulnerable | It is one arm of `Sim_DamageToPlayerDisabled`, and the arm the loader's zeroing of `MissionModeFlag` makes unreachable. Invulnerability is its own header field |
| `prefs.cfg` options `0x25`/`0x26` are the two cheats DBSIM reads | They are VSHELL's half of a file the two programs share. The simulator reads neither: it tests the `script.dat` header fields against `== 1`. The option bytes are the shell's record of what the player asked for, not the switch |
