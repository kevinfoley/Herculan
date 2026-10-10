# `dat\COLORS.DAT` — logical colour ids

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses. Verified against retail data in `ES2/VOL/simvol0/dat/`.

The table that turns the small colour ids the cockpit's data carries into palette indices. `HudColor_LoadResources` (`00467a60`) loads it, and the cockpit instruments read it: the console gauges ([`cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md)), the Heads-Down Display ([`heads-down-display.md`](../simulation/heads-down-display.md#colours)), the Multi-Function Display ([`mfd.md`](../simulation/mfd.md)) and its scanner ([`mfd-scanner.md`](../simulation/mfd-scanner.md#contact-colours)).

## Layout

54-byte payload, 27 `int16` palette indices. HUD data files carry a small logical id, resolved once at load time through this table in place (`arr[i] = table[arr[i]]`). The table lives at `HudColorTable` (`004d3c00`) in `.bss`, read at 16 distinct offsets by ~60 functions. `HudColor_LoadResources` (`00467a60`), a phase-2 subsystem loader, fills it: `Ovl_ReadFile(&HudColorTable, 0x36, 1, "dat\colors")`.

Verified: ids 19, 9, 15, 12 resolve to palette 16, 10, 13, 14 — black, red, yellow, green. The heads-down display resolves exactly these four, though no reader of the result is found ([`../simulation/heads-down-display.md`](../simulation/heads-down-display.md#rejected-readings)).

## Ids and immediates

**Not every colour number is an id.** The indirection exists for numbers that arrive in a *data file*; a colour a *constructor states as an immediate* is already a palette index and goes nowhere near this table. The weapon panel's raw 32/34/46 (`WeaponSliderGadget_Ctor`, `00442950`) are the clearest case, and the scanner screen uses both conventions at once: its contact colours are read out of the table at paint time while its screen background is the literal `0x11` its constructor writes — palette 17, matching the dish art's own corner pixels. Reading such an immediate as an id lands on a believable but wrong colour (`0x11` as an id is palette 24, a mid grey).

## Consumers

The `.PDG` paper doll's regions, colour id at region offset `0x14` ([`pdg-paper-doll.md`](pdg-paper-doll.md#views)); `HddDamageScreen_Ctor` (`0045079c`, 4-entry id array at `DAT_0049d9ec`); `HudColorTable_Get` (`00434280`).
