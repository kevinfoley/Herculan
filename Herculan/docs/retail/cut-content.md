# Cut and unreachable content

Retail content that was authored, shipped in the data files or the binary, and cannot be reached by playing the game. Two audiences: someone reading the data files who finds a weapon or a record that appears real and is not, and someone interested in what the game was going to have.

## Weapons

Three weapons appear in the `SHELL0/GAM` catalog with a code, a full name in `WEAPONS.BIN` and a price, but are not actually accessible in the campaign. Similar weapons later appeared in *Starsiege*.

- **`LAEW` — Locust Launcher** (id 26), incomplete and unusable. It has its own mount class, a hold-to-throw launcher whose shot spends a round and launches nothing, and a magazine of 0 — see [`simulation/weapon-mounts.md`](simulation/weapon-mounts.md#mount-classes).
- **`MINE` — Mine Launcher** (id 27), incomplete and unusable.
- **`MFAC` — MagnetoFusion Cannon** (id 28), a functional weapon firing the Plasma Cannon's round (`PROJ.DAT` row 22) at nearly twice the range.

**`MFAC` is fitted to three chassis in `gam\trn_herc.dat`**, a nine-record stock-fit set that is otherwise a near-copy of the `ini_*.dat` files. It is the only retail data that arms a player machine with a cut weapon. No reader for that file has been found in either binary ([Open](formats/herc-catalogs.md#open)), so it does not overturn the above — but a reader who finds `MFAC` there will reasonably think it does. See [`formats/herc-catalogs.md`](formats/herc-catalogs.md#gamtrn_hercdat--a-second-stock-fit-set).

Additionally, the game data includes an unused particle-beam weapon for the Cybrid Bull. The three Bull weapons (ids 19-21) are excluded twice over: they have no armory panel, and the thirty-entry weapon-class table at `0046f868` omits them, so a Bull weapon in a player hardpoint resolves to class `-1`. `damage.dat` also zeroes their scrap value where every other weapon's is its price divided by ten.

## Projectiles

- **An unused "Grenade" projectile type** appears in the code, with no functionality.

## Sound effects

- Computer voiceover lines "Primary objective completed", "Secondary objective completed", "Mission objectives completed"
- Computer voiceover lines "Friendly target disabled" and "friendly target destroyed". The "enemy target disabled/destroyed" lines only play when the player fired the killing shot on a currently targeted unit; friendly units cannot be targeted, so there's no logic for playing their equivalent sound effects.

## Miscellaneous features

- **Last-known-position scanner blips.** The F4 scanner's hostile branch has a complete implementation of a blinking last-known-position marker, plotted on every other coarse tick. Every object constructor sets the byte that gates it and nothing ever clears it, and nothing writes the stored position either.
- **One-step capacitor recharge.** `WeaponMount_DemandFullCharge` (`0040f4f0`) fills a weapon capacitor to the 1200 maximum in a single call and is the obvious mechanism for a full-charge pickup or cheat. Its only caller is itself unreferenced anywhere in the image.
- Unused voiceover lines "Shutdown initiated" (CVM_0037.WAV) and "Powerup initiated. Internal damage detected." (CVM_0036.WAV) suggest that Dynamix considered mid-mission shutdown - a feature that was later implemented for stealth purposes in *Starsiege*.
- **An earlier preferences panel.** v1.0's French and German alert-panel text labels options the shipped panel does not have — RADIO, HORIZON, SKY, GROUND and SHADOWS — and the game cannot show it in either language without starting the simulator by hand. See [`simulation/alert-panels.md`](simulation/alert-panels.md#what-the-family-shares).
- An unused feature in the code would animate the player's Herc from underground to surface level as if riding an elevator to the surface. This is left over from _Metaltech: Earthsiege_. See [`simulation/mission-deployment.md`](simulation/mission-deployment.md#the-lift-start).

## Files

- **`DEMO2.MSN`.** The one mission file of 62 that does not land on EOF cleanly — it undershoots by 42 bytes in the middle of a row's tail. A stale developer test file with a genuinely truncated tail, not a gap in the format. See [`formats/msn-mission-file.md`](formats/msn-mission-file.md).
