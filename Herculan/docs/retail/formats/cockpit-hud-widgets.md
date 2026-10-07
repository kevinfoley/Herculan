# Cockpit console widgets: HUD sprite banks, the `.GAU` tree, and the gauges

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. All addresses are DBSIM unless noted. Symbols are in `tools/ghidra_scripts/known_symbols_dbsim.json`; apply with `ES2ApplySymbolNames.java`.

Verified against retail data in `ES2/VOL/simvol0/{hba,gau,dat}/`.

The view manager and canvas these widgets are drawn onto: [`cockpit-views.md`](cockpit-views.md). Canopy art and the palette a flash swaps: [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md). The front-window gunsight complex (a separate widget tree): [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md). How a mouse click on any of these widgets reaches its own click handler: [`cockpit-input.md`](cockpit-input.md).

## HUD sprite art — `.HBA`/`.DBA`

Every bank ships twice under the same name: `dba\NAME.DBA` for the 320-wide mode and `hba\NAME.HBA` for the 640-wide one, exactly 2x on both axes, frame for frame, with identical frame counts. The two folder-name literals sit adjacent to each bank name in `.rdata` (`"NAME\0hba\0dba\0"`). `corners` is hardcoded to `dba`; `hba\CORNERS.HBA` does not exist.

Load path: `ResourcePath_BuildFolderName(name, folder)` → `Resource_Load` (`0045cdd8`) → `ClassItem_LoadResource`.

| Bank | Owning function | Role |
|---|---|---|
| `hud` | `Gau_RovingGunsightWidget` (`0043c7d8`) | gunsight / reticle — see [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md) |
| `hudhtick` | `HudHeadingTape_Ctor` (`0043b57c`) | heading tick tape — see [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#heading-tape) |
| `mfd`, `mfd_dmg`, `radar` | `MfdDisplay_Ctor` (`00445218`) | multi-function display — see [`mfd.md`](mfd.md) |
| `hdd`, `static`, `hddclip`, `pilotN` | `HddDisplay_Ctor` (`00448cc8`), `HddGauge_LoadPilotFrames` (`0044a7c0`) | heads-down display — see [`heads-down-display.md`](heads-down-display.md) |
| `pweapons`, `wpn_dmg` | `WeaponGauge_Ctor` (`0044080c`) | weapon hardpoint plates |
| `throttle` | `ThrottleGauge_Ctor` (`00447b84`) | throttle slider knob |
| `sysbuttn`, `icons`, `corners` | `SystemButtons_Ctor` (`00434368`), `HddMarker_Ctor` (`0044f130`), `CockpitFontsAndCorners_Init` (`004544a4`) | |

The class names in these symbols (`ThrottleGauge`, `WeaponGauge`, …) are the classes' own, read from their Borland class records ([`borland-rtti.md`](borland-rtti.md)). [`cockpit-input.md`](cockpit-input.md#the-cockpits-own-gadget-classes) has the clickable-widget hierarchy.

Frame-to-state mapping: `PWEAPONS` 0/1 are the selected/unselected row plate, 2/3 the unlit/lit console-button plate, 4/5/6 the hardpoint state box (green / red / amber), 7 a 640x80 strip ([Open](#open)); `WPN_DMG`'s frame 0 is the row underlay, a flat 112x14 plate in the row background `0x2e`, and frames 1-9 a weapon row's [sensor-dropout wipe](#the-wipes); `MFD_DMG`'s seven 192x118 frames are the MFD's; `THROTTLE` 0 is a 2x12 tick and 1 the 28x12 knob; `RADAR`'s 10 110x110 frames are the sweep animation; `MFD` 0-2 are 196x122 screen chrome, 3-10 five button plates in unlit/lit pairs (see [`mfd.md`](mfd.md)); `HUD` 0 is the 45x45 reticle, 11 the 182x10 rotation-indicator track and 12/13 its 62x4 yellow and green bars (sizes in the 640-wide `hba\` banks; `dba\` is exactly half).

**`static` and `pilot<n>` ship in `dba\` only**, so the 640-wide mode has no matching art for them; see [`heads-down-display.md`](heads-down-display.md).

## `.GAU` widget tree

`Gau_Load` (`00431778`, `PANEL.CPP:0x1d6`) reads a `0x6a4`-byte struct and vector-constructs six arrays of 16-byte rects inside it, of 10, 3, 4, 13, 15 and 3. The file's first two `int32`s are an origin offset added to every widget rect. `Gau_BuildCockpitWidgets` (`00431bf8`) then builds seven top-level widgets from fixed offsets and shifts every rect by `VideoMode_X/YCoordShift`. The order it builds them in is also the cockpit's click precedence — [`cockpit-input.md`](cockpit-input.md#registration-order-is-precedence) has the full sequence.

GAU coordinates are authored in the 320-wide space, half the 640-wide art's. See [`cockpit-views.md`](cockpit-views.md#cockpit-canvas) for the y-range question.

## `dat\COLORS.DAT` — logical colour ids

54-byte payload, 27 `int16` palette indices. HUD data files carry a small logical id, resolved once at load time through this table in place (`arr[i] = table[arr[i]]`). The table lives at `HudColorTable` (`004d3c00`) in `.bss`, read at 16 distinct offsets by ~60 functions. `HudColor_LoadResources` (`00467a60`), a phase-2 subsystem loader, fills it: `Ovl_ReadFile(&HudColorTable, 0x36, 1, "dat\colors")`.

Verified: the heads-down display resolves ids 19, 9, 15, 12 → palette 16, 10, 13, 14 — black, red, yellow, green, matching the retail HDD readouts.

**Not every colour number is an id.** The indirection exists for numbers that arrive in a *data file*; a colour a *constructor states as an immediate* is already a palette index and goes nowhere near this table. The weapon panel's raw 32/34/46 (`WeaponSliderGadget_Ctor`, `00442950`) are the clearest case, and the scanner screen uses both conventions at once: its contact colours are read out of the table at paint time while its screen background is the literal `0x11` its constructor writes — palette 17, matching the dish art's own corner pixels. Reading such an immediate as an id lands on a believable but wrong colour (`0x11` as an id is palette 24, a mid grey).

Consumers: the `.PDG` paper doll's regions, colour id at region offset `0x14` ([below](#pdg--paper-doll-damage-diagram)); `HddDamageScreen_Ctor` (`0045079c`, 4-entry id array at `DAT_0049d9ec`); `HudColorTable_Get` (`00434280`).

## LED gauges

`LEDBarGraphH`/`LEDBarGraphV`. `LedBarGraph_Ctor` (`004395c4`) installs vtable `PTR_FUN_0049bd30` over `LedBarGraph_CtorBase` (`004390c4`), which precomputes:

```
span    = (end - start) * 0x10000 / range     // range is the caller's value scale
current = span < 1 ? end : start              // sign of span selects fill direction
```

`LedBarGraph_PaintToValue` (`004395e8`) fills to `start + (value * span >> 16)` (16.16 fixed point), then installs field `0x24` as the draw colour and covers the remainder.

The filled span is not solid. `LedBarGraph_FillPinstripe` (`00439758`) walks the x range twice — once over even columns, once over odd — drawing a full-height line each step: field `0x2c` paints even columns, `0x30` odd. Two near-identical shades interleaved at one pixel read as a single shaded fill.

`LEDBarGraphH` fills along **x**: `LedBarGraph_CtorBase` takes start/end from the rect's `x0`/`x1` (`param_2[0]`/`param_2[2]`), and the pinstripe walk strides columns. `LEDBarGraphV`'s constructor (`00439834`) goes through `LedBarGraph_CtorV` instead, and its fill, `LedBarGraphV_FillPinstripe` (`00439a0c`), draws full-width lines on alternate rows, `0x2c` on even rows and `0x30` on odd.

`EnergyPoolGauge_Ctor` (`00444d5c`) constructs an `LEDBarGraphH` over the `.GAU` widget rect at 564 with range `0x400`. It builds the bar in a switch on its own `+0x20`, which it sets to 0 just before (`00444e41`-`00444e49`), so it always takes the `LedBarGraph_Ctor` case and skips the `LEDBarGraphV` one (`00444edc`). It writes writing colour ids 6 and 5 into `0x2c`/`0x30` and id 19 into `0x24`. Those resolve to palette indices 98/97/16 = `(0,116,204)`, `(0,40,160)`, `(0,0,0)` — the blue pinstripe bar retail draws directly under the TRACK button, i.e. the **Master Energy Pool meter**. `Player_PerFrameCockpitUpdate` feeds it the pool scaled to that range — see [../simulation/reactor-energy-pool.md](../simulation/reactor-energy-pool.md#cockpit-readouts). Its only caller is `Gau_EnergyMeterWidget`, and the binary's own class-name table pairs `EnergyPoolGauge` with `LEDBarGraphV` (file offset 280429) and `ShieldsGauge` with `ShieldsSelectGadget` (279148) — the LED bar is the energy meter, and `ShieldsGauge` is a different class entirely.

A second `LEDBarGraph` per weapon row carries the energy-weapon charge field (`WeaponSliderGadget_Ctor` (`00442950`), range `0x400`) — but with raw palette indices `0x20`/`0x22` and remainder `0x2e`, not `COLORS.DAT` ids. See [Weapon hardpoint rows](#weapon-hardpoint-rows).

## Throttle gauge

`ThrottleGauge_Ctor` (`00447b84`), called only by `Gau_ThrottleWidget` (`0043254c`).

The constructor is handed `.GAU` offset **1000**, not 1016, and treats the whole block from there as one widget record. `GauThrottle_ApplyCoordShift` (`004488cc`) shifts ints `[4..0xf]` left by the video mode's coordinate shift before it ever sees them, so the geometry below is in device pixels (`.GAU` units x2 at 640x480):

| int | file offset | Role |
|---|---|---|
| `[0]`,`[1]` | 1000, 1004 | Origin the rest is measured from. Zero in all 9 retail files, which is why it reads as an always-zero "null widget" slot until the constructor is traced |
| `[4..7]` | 1016-1028 | Slider **track** rect, `x0,y0,x1,y1` |
| `[8..11]` | 1032-1044 | **Forward fill bar** rect — `LedBarGraph_CtorV` (`00439344`) with range `+0x400` |
| `[12..15]` | 1048-1060 | **Reverse fill bar** rect — same, range `-0x400` |

| `[0x10]` | 1064 | `SLIDE_DIR`. 1 in every retail file, selecting `ThrottleSlider_CtorV` (`00447e24`); the 0 branch (`004483c0`, a fixed 12px knob spanning the track's full height) is never exercised |
| `[0x12]` | 1072 | x nudge for the centre tick, shifted by the ctor itself rather than at load |

Ints `[8..15]` are **two rects, not four points**. That explains both things the point reading found odd: "points" 1 and 2 always sit close together because they are the bottom of the upper bar and the top of the lower one, and the x alternates between two values because those are each bar's left and right edge. On OUTLAW they are two 4x20 strips inside the 14x49 track, one either side of centre.

**Neither bar is ever drawn.** `ThrottleSlider_CtorV` keeps them as private fields (`+0x7e`, `+0x82`) and never registers them with the widget tree, so nothing dispatches their paint; their class's draw slots, `BarGraphV_PaintToValue` and `BarGraphV_RepaintSpan` (`00439398`, `00439460`), have no direct caller — `es2_xref.py` finds each only in `BarGraphV`'s vtable (`0049bd48`). The slider's paint (`ThrottleSlider_PaintV`, `0044819c`) reads them only through `BarGraph_GetRect` (`004390b8`), which returns the object's rect, and unions those rects into the region it invalidates. The bars are a cut feature whose construction was left in — see the speed fraction below, which is what would have filled them.

### Slider geometry

`ThrottleSlider_CtorV` builds the knob over the track: full track width, height taken from bank frame 1 (28x12 in every retail bank), limits `+/-0x400`, and an initial position centred on the track — `knobBottom = trackBottom - (trackHeight - knobHeight)/2`. That is the manual's "Centered is stopped". `SliderWidget_RecomputeScaleV` (`00452694`) then precomputes

```
scale     = (trackHeight - knobHeight) * 0x10000 / 0x800     // Q16 device px per throttle unit
knobBottom(v) = trackBottom - ((v + 0x400) * scale >> 16)     // 00452644
```

so `+0x400` puts the knob at the top and `-0x400` at the bottom. **Up is forward** — corroborated by `Reference/Simulator1.jpg`, where the knob sits at the track's top while the HUD reads 61 K/H.

The original reaches that convention through two sign flips that cancel: `SliderWidget_GetValueV` (`00452628`) reads the knob's *top* against the track's top, so it returns the negation of what `_SetValueV` was given, and `ThrottleGauge_OnChildValue` (`00447de0`) negates again for the vertical variant (gated on the gauge's `+0xc1 == 0`). Net effect: positive is forward, linear in knob position.

### Sprites

Bank `throttle` (`.HBA`/`.DBA`), 2 frames: **0** is a 2x12 tick, **1** the 28x12 knob.

The gauge captures the tick's blit position **once**, in the constructor, at the knob's neutral height plus `[0x12]`, and nothing writes it again — so it is a static centre-detent marker beside the track. Matches `Simulator1.jpg`.

### Live values and the two-way binding

`ThrottleGauge_GetValues` (`00447dd0`) returns gauge `+0xb1`, a pair of ints:

| offset | Value |
|---|---|
| `+0xb1` | Speed as a Q10 fraction of max, `(mech+0x28e << 10) / (speed < 0 ? -maxRev : maxFwd)` |
| `+0xb5` | Throttle setting, Q10 `+/-0x400` — the same number as `mech+0x290` |

`Player_PerFrameCockpitUpdate` (`0041b130`) writes both once a frame via `ThrottleGauge_SetValues` (`00447d80`), and arbitrates the throttle against the `mech+0x93` dirty flag: flag clear, the gauge drives `mech+0x290`; flag set, the machine's throttle is handed back for the gauge to follow and the flag is cleared. Whichever moved last wins, which is what makes the slider track the keyboard and the keyboard pick up where a drag left off.

**`+0xb1` drives nothing.** `ThrottleGauge_SetValues` marks the slider child dirty when it changes, and `ThrottleSlider_PaintV` copies it into `+0x7a` and `+0x4a` and does nothing further with it — so its only observable effect is to force a repaint whenever the machine's speed changes. It is the other half of the cut feature the two fill bars are: the knob shows the throttle asked for, the bars would have shown the speed actually reached.

The slider is the **only widget a press can drag in a retail cockpit**: the energy rows' charge bars carry the same drag flag, but each lies under its row's select gadget — see [cockpit-input.md §7](cockpit-input.md#7-press-release-click-vs-drag). `ThrottleSlider_OnValue` (`00448378`) also sets `ThrottleLeverMode` (`0049a06e`) from the committed value's sign, but gated on a joystick throttle control being configured. See mech-locomotion.md for what that global actually is.

## `ShieldsGauge`

`ShieldsGauge_Ctor` (`004434fc`), called only by `Gau_ShieldDisplayWidget` (`00432454`) with `.GAU` offset 616. It loads no sprite bank and **draws no geometry**: it builds a `0x40`-byte child per facing (`ShieldsGauge_FacingCtor`, `00444aec`) whose paint slot (`ShieldFacing_Paint`, `00444b5c`) only tests visibility, plus two text labels.

**The meter is lit, not drawn.** The nested concentric rings are painted into the herc's own canopy art in palette indices 66-71 — verified on `OUTLAW.HB0`, where those six indices appear only inside the meter bezel, three per facing, the innermost ring using the fewest pixels. The gauge's paint (`00443730`/`00443748`) does two things per frame: rewrite those six palette slots (`ShieldsGauge_UpdateRingPalette`) and refill the two readouts (`ShieldsGauge_UpdateReadouts`).

### Ring ramp — `ShieldsGauge_UpdateRingPalette` (`004438f0`)

Per facing (charge at object `+0xb5` and `+0xb9`, each `(charge << 10) / baseMax` as `Shield_BalanceInputRead` writes it — [`../simulation/damage-system.md`](../simulation/damage-system.md#the-shield-system)), three rings light in turn as charge rises. The divisor is the type's base capacity, not the current maximum, so a Shield Pod's extra charge carries a ring past `0x400` into the overcharged colours instead of being renormalised:

```
ring 1: t = v
ring 2: t = v < 0x100 ? 0 : min((v - 0x100) * 2, 0x400)
ring 3: t = v < 0x180 ? 0 : min((v - 0x180) * 4, 0x400)
colour = base * t >> 10                                  -- Q10
```

Above `0x400` (an overcharged shield) the same three tracks run again over `base + (bright - base) * t`, with `v` taken as `charge - 0x400`.

Colour immediates at `ShieldRingColors` (`0049c9cb`): base RGB6 `(25,59,23)`, bright `(59,59,23)`. `Palette_InstallRange(0x42, 6, ...)` reads the six entries up the stack, so **66-68 are the first facing outermost-first and 69-71 the second**.

A facing runs 0..`0x800` with `0x400` the whole pool on one side, so an even 100/100 split parks both at `0x200`, where all six rings resolve to RGB `(48,116,44)`. The retail screenshot's meter is `(48,117,44)` — the one-channel difference is the palette scalar's own rounding.

### Readouts — `ShieldsGauge_UpdateReadouts` (`00444a68`)

`itoa(balance * 200 >> 10)` into the first label and `200 -` that into the second, from the fore/aft balance at `+0xbd`. An even split reads 100 and 100 out of a 200-point pool, which is what retail shows. **The pair always sums to 200 whatever the charge**, and an empty array still reads 100/100, so it is not a charge percentage — the natural way to misread it. Font is `ColorSchemePanels[10]` (`WHITE`); background is `COLORS.DAT` id 19 (palette 16, black).

**A forward press from centre reads 119/81, not 120/80.** A press moves the balance by `0x66` (102, [`damage-system.md`](../simulation/damage-system.md#balance-adjustment-input--players-own-mech-only)), which is 19.92 readout points rather than 20, and the `>> 10` truncates. Stepping from the power-up centre of 512, every forward stop falls just short of its multiple of 20 and every rear stop just past it, so only the front label rounds down:

| Presses | Balance | Readout |
|---|---|---|
| 5 rear | 2 | 0 / 200 |
| 4 rear | 104 | 20 / 180 |
| 3 rear | 206 | 40 / 160 |
| 2 rear | 308 | 60 / 140 |
| 1 rear | 410 | 80 / 120 |
| — | 512 | 100 / 100 |
| 1 forward | 614 | 119 / 81 |
| 2 forward | 716 | 139 / 61 |
| 3 forward | 818 | 159 / 41 |
| 4 forward | 920 | 179 / 21 |
| 5 forward | 1022 | 199 / 1 |

The readings depend on the path, because the clamp at either end moves the balance off this ladder. After a sixth forward press (1024, 200/0) the steps back are 922, 820, 718, 616 and 514, which read 180/20, 160/40, 140/60, 120/80 and 100/100; after a sixth rear press (0) the steps forward are 102, 204, 306, 408 and 510, which read 19/181, 39/161, 59/141, 79/121 and 99/101.

### `.GAU` block at 616

A 16-byte header whose first two ints are an origin offset added to the rest (all-zero in every retail file), then four ordinary `x0,y0,x1,y1` rects, all shifted by `VideoMode_X/YCoordShift` in `ShieldsGauge_ApplyCoordShift` (`00444b9c`):

| Offset | Rect |
|---|---|
| 632 | front facing's meter body |
| 648 | rear facing's meter body |
| 664 | front readout |
| 680 | rear readout |

The block ends at 696. It starts at 616, not 628 — starting it one int later rotates every slot and leaves a spurious leftover int at 692. All nine retail `.GAU` files round-trip byte-exact under this reading.

## Weapon hardpoint rows

**Which mount owns which row is the mount's business, not the panel's**: the gauge factory is called with the `gl\<HERC>.GL` record's fire-chain byte as its `.GAU` weapon-slot index, so row order and mount order are different orderings. See [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md).

Three gauge classes, one per mount class, all built on `WeaponGauge_Ctor` (`0044080c`), which lazily loads `pweapons` and `wpn_dmg` and builds a two-sequence frame table for the latter:

| Class | Factory → ctor | Value field |
|---|---|---|
| energy | `CockpitView_CreateEnergyWeaponGauge` (`00432074`) → `EnergyWeaponGauge_Ctor` (`00440a68`) | charge bar: a `WeaponSliderGadget` slider over an `LEDBarGraph` (`WeaponSliderGadget_Ctor`, `00442950`) |
| ammunition | `CockpitView_CreateAmmoWeaponGauge` (`00432124`) → `AmmoWeaponGauge_Ctor` (`00440f78`) | round count, `itoa` (`AmmoWeaponGauge_Paint`, `004411b4`) |
| pod | `CockpitView_CreatePodGauge` → one of three `PodGauge` classes | none — the name label widens over both fields — except the Turbo Pod's |

All three `strncpy` 12 bytes of the mount's name (`WeaponMount_GetDisplayName`, `0040e18c`) into the gauge at `+0xb1`. The pod class instead seeds an 11-char buffer with a space, appends the name, then appends `STRINGS0.STR` group 3 (`" POD"`) into the room left — `" SHIELD POD"`. A destroyed mount's row prints group 2 (`"OFFLINE"`) in place of the name.

**The Turbo Pod's row is the exception.** `TurboPodGauge_Ctor` (`00441a34`) overwrites that buffer with a plain 11-char `strncpy` of the name — so the row reads `TURBO`, not `" TURBO POD"` — rebuilds the name label at `x0+6 .. x0+34`, `y0+1 .. y0+5` and gives the freed right-hand end an `LedBarGraph` over `pod+0x7d`, its charge. The bar's range is 2500 where `TurboPod_ChargeTick` caps the charge at 2000, so a fully charged Turbo Pod shows four fifths of a bar. Which pod gets which gauge class, and why only two of the five have a button at all, is in [`../simulation/equipment-pods.md`](../simulation/equipment-pods.md#only-two-pods-have-a-button).

Sub-rects, all relative to the `.GAU` hardpoint rect and mirrored from its right edge when the constructor's slot-mask byte is set (that byte lands in the `.GAU`'s confirmed-zero padding in every retail file, so retail never mirrors):

| Sub-rect | Offsets from the rect, GAU | Built by |
|---|---|---|
| hardpoint state box | `x0+6 .. x0+9`, `y0 .. y0+7` | `ChainedWeaponSelectGadget_Ctor` (`00442488`) |
| weapon-name label | `x0+11 .. x0+35`, `y0 .. y0+5` | `ChainedWeaponSelectGadget_Ctor` |
| value field | `x0+36 .. x0+53`, `y0 .. y0+5` | `EnergyWeaponGauge_Ctor` / `AmmoWeaponGauge_Ctor` |
| pod name label | `x0+11 .. x0+53`, `y0 .. y0+5` | `PodGauge_Ctor` (`00441524`) |
| Turbo Pod name label | `x0+6 .. x0+34`, `y0+1 .. y0+5` | `TurboPodGauge_Ctor` (`00441a34`) |

The two pod labels are the only sub-rects that are ever painted rather than merely written in, and the paint is the only feedback a pod row gives, since it has no state box. `PodGauge_Paint` (`0044171c`) picks the label's font and background from the gauge's two state bytes: the destroyed byte at `+0xc3` wins outright and prints the offline text across the widened label, and failing that the button at `+0xc2` selects the `gray` font over background `0x2e` when it is off and the `dark` font over `COLORS.DAT` id 12 — green — when it is on. Both go into the label itself, the font at `label+0` and the background at `label+0x1d`, so an engaged pod reads as dark lettering on a green plate filling the label rect. What sets the button is in [`../simulation/equipment-pods.md`](../simulation/equipment-pods.md#only-two-pods-have-a-button). Both edges are inclusive, so the Turbo Pod's plate is 57x9 device pixels against a plain pod's 85x11. The label's *text* does not follow its rect — every row on the panel prints its name at the same `x0+11`, the Turbo Pod's included, which is why that plate has green to the left of the `T`.

`WeaponSliderGadget_Ctor` then drops the bar's own top edge one GAU unit below the value field's, and builds it over `0x400` with colour **palette indices** `0x20`/`0x22` and remainder `0x2e` written straight into the bar object — not `COLORS.DAT` ids, which is why a capacitor bar is blue where the energy meter is grey.

`WeaponSelectGadget_Paint` (`004426c0`) draws:

- `WPN_DMG` frame 0 as the row underlay, then the slot number (`WeaponSelectGadget_PaintUnderlay`, `00442394`);
- the name, in `ColorSchemePanels[10]` `WHITE` when selected and `[11]` `GRAY` otherwise;
- the slot number again, recoloured `[13]` `GREEN` when selected / `[11]` `GRAY`;
- the state box, `PWEAPONS` 6x14 frames — **only when the mount is armed or in the current fire group**, otherwise the box area is filled with the row background. Frame 4 (green, index 14) when the mount is ready, frame 5 (red) when it is not — including when the selected target is outside the weapon's range, which is also what makes the firing chain skip it ([`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#readiness--weaponmounts_mountisready-00410970)). A pod is in no fire group, so a pod row never has one;
- last, the row plate: `PWEAPONS` frame 0 selected / frame 1 not, at the rect **minus two device pixels on both axes**. The 116x18 art is not a plate but a frame — a 112x14 hole of palette index 0 is punched out of it, so all the sprite contributes is a two-pixel bezel. That offset lands the hole's top-left corner exactly on the rect, where the underlay fills it, which is what puts the 14-pixel state box and an engaged pod's plate inside it.

The three state flags come from `WeaponMounts_PerFrameUpdate` (`00410b40`), the mount manager's per-frame pass.

## Console buttons

Chain, link and auto-track are all 24x7 GAU in every retail file. `ConsoleButton_Paint` (`00442c88`) blits `PWEAPONS` frame `2 + state` at the widget's own rect — frame 2 unlit, solid palette index 34 (the retail blue, RGB `(77,77,182)`); frame 3 lit, index 14 green — then the caption in `[10]` `WHITE` unlit / `[12]` `DARK` lit. The plates are **not** canopy art.

The chain button's caption is its count in Roman numerals from `ChainCountCaptions` (`0049c71c`): `"I"`, `"II"`, `"III"` — a literal table in `.rdata`. LINK and TRACK are not fixed the same way: `ConsoleButton_Paint` reads them from `DAT_004d13d0`, the `.bss` array `SimStrings_LoadAll` fills from `STRINGS0.STR` group 4 (see [`str-strings.md`](str-strings.md)), indexed by the widget's own kind field — entry 1 for LINK, entry 2 for TRACK.

## `.PDG` — paper-doll damage diagram

`PaperDoll_Load` (`004379cc`, `pdamage.cpp`) reads 3 views, each an origin/size pair plus a vector of `0x1c`-byte regions (`{int index; int left, top; int right, bottom; int colorId; int recolorMode}`).

Coordinates are authored in the 320-wide space and shifted by `VideoMode_X/YCoordShift`, with the bottom-right corner additionally `+1` in the 640-wide mode, so a region covers the full 2x2 device footprint of each source pixel. Region art comes from `{herc}.HBA`/`.DBA`, frame `n` for view `n`.

The two nameless fields are what makes a region a damage region:

| Field | Offset | Meaning |
|---|---|---|
| `colorId` | `0x14` | The colour the art drew that body part in — a `COLORS.DAT` id, resolved to a palette index in place at load. Retail uses 9, 12, 15, 20, 24 and 25 |
| `recolorMode` | `0x18` | Recolour mode. **Every retail region states 0**; modes 1-3 are unexercised |

### Tinting

A region is not filled. `PaperDoll_RecolorRect` (`00437e94`) walks the region's rect a pixel at a time and, in mode 0, rewrites only the pixels still holding `colorId`, which is why the outlines and detail drawn over a limb survive its recolour. Modes 1 and 3 do the same without the doubled pixel step; mode 2 adds the tint to every pixel that is not the id-19 background. Modes 0 and 1 skip the walk when the two colours are equal, so an undamaged region costs nothing.

`PaperDoll_RecolorRectFromArt` (`00438230`) is the same four modes reading the source bitmap instead of the raster, for a screen that repaints a region without having repainted what is under it first — the Heads-Down Display's route, where the MFD takes the first.

`Damage_PickRegionTint` (`00438624`) chooses the colour from one Q8 damage reading, on `Damage_ToConditionState`'s own bands:

| Intact | State | Tint (`COLORS.DAT` id → palette) |
|---|---|---|
| ≥ 90% | 0 | 12 → 14 green |
| ≥ 74% | 1 | 15 → 13 yellow |
| ≥ 51% | 2 | 20 → 12 orange |
| ≥ 1% | 3 | 9 → 10 red |
| 0 | 4 | 18 → 20 grey |

Which reading a region takes is the caller's business, and the two callers disagree: see [`mfd.md`](mfd.md#viewport-and-condition-per-class) for the status screen's compact view and [`heads-down-display.md`](heads-down-display.md#damage-detail--page-1) for the damage detail's.

### Weapon icons

After the three views `PaperDoll_Load` reads one more vector, into the doll's `+0x54` (count) and `+0x58`: `0x14`-byte hardpoint entries, `x` and `y` shifted like the regions.

| Offset | Field | Meaning |
|---|---|---|
| `0x00` | `x`, `y` | Anchor point, relative to the view's origin |
| `0x08` | `frameOffset` | Added to the weapon's icon index. 1 on OUTLAW's two side hardpoints, 0 everywhere else |
| `0x0c` | `alignment` | Which point of the icon lands on the anchor: bits 0-2 horizontal (1 left, 2 right, 4 centre), the rest vertical (8 top, `0x10` bottom, `0x20` centre). `0x24`, centred both ways, on every retail entry but OUTLAW's `0x22`, `0x21` and `0x14` |
| `0x10` | `blitFlags` | Handed to the blit. 0 on every retail entry |

`PaperDoll_BuildWeaponIcons` (`00437c8c`) turns that into each machine's icon list, stored at `mech+0x1fe` by `Sim_InitMissionSession`. **Entry `n` is the hardpoint whose `.GL` slot byte (`+0x17`) is `n`**, not the `n`th `.GL` record: the builder searches the gun layout for the slot byte and takes the mount at that record's position. Every retail `.PDG` has at most as many entries as its `.GL` has records, and each entry's slot is present, so the search always lands. An empty hardpoint, or a weapon whose template `+0x50` is -1, gets no bitmap.

The frame is template `+0x50` ([`weapons-dat-sim.md`](weapons-dat-sim.md#decoded-tail-fields)) plus `frameOffset`, out of the `weapons` bank — `hba\WEAPONS.HBA` or `dba\WEAPONS.DBA`, loaded once by `PaperDoll_InitTables` (`004378d8`) together with `pdg\WEAPONS.PDG`. That file has no views: it is an `int32` count and then one `{int32 width, int32 height}` per frame in the 320-wide space, shifted at load — fourteen 9x9 frames in retail. The alignment backs the anchor off by that size, or half of it, after the shift; the rect is anchor to anchor plus size, inclusive.

`blitFlags` 2 moves the icon further left by the bitmap's own width less the `WEAPONS.PDG` width; no retail entry sets it.

## HUD fonts

`ColorSchemePanels_LoadAll` (`00431098`) lazily loads 18 `.DFN`/`.HFN` fonts into `ColorSchemePanels` (`0049b0ac`), then 7 `.DCI` cursors. Load order is the array index:

| 0-5 | 6-11 | 12-17 |
|---|---|---|
| `cpblue`, `cpgreen`, `cpred`, `cpylw`, `cpon`, `cppress` | `cpoff`, `cpgrey`, `cpblack`, `cporange`, `white`, `gray` | `dark`, `green`, `red`, `hud1`, `hud2`, `hud3` |

Each file is the same typeface stencilled in one palette index, so **a widget picks its text colour by picking a font** — no colour is ever passed to a label. Consumers reach entries by absolute address: `0049b0d4` = 10 `white`, `0049b0d8` = 11 `gray`, `0049b0dc` = 12 `dark`, `0049b0ec` = 16 `hud2`, `0049b0f0` = 17 `hud3`.

Format, glyph layout and per-file ink indices: [`dfn-hfn-dci.md`](dfn-hfn-dci.md).

## Power-up sequence

On taking a walking machine the cockpit comes up piece by piece rather than reading true from its first frame. Three fields of the shared widget base carry it — `+0x8c` armed, `+0x8d` done, `+0x90` the coarse tick it was armed on — cleared by the base constructor (`00438b20`) and set by `Widget_BeginPowerUpAnimation` (`00438ddc`); each class reads them in its own paint and update. A coarse tick is `Time_GetCoarseTicks` (`00467724`), `GetTickCount() >> 4`: 16 ms of wall time.

`Cockpit_PowerUpSound` (`004328cc`) stamps the sequence's start at `cockpit+0x241`, and `Cockpit_PowerUpTick` (`00432924`) arms, once a frame, every widget whose moment has come:

| Widget | Armed once ticks since the start | While armed and not done |
|---|---|---|
| Weapon rows, `cockpit+0x70` | `> 20 * (row + 1)` | [the row winks on](#weapon-rows-wink-on) |
| `ShieldsGauge`, `cockpit+0x1e9` | `!= 0` | [the rings fill](#shield-rings-fill) |
| Roving gunsight, `cockpit+0x1f5` | `!= 0` | the compass winds up — [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#power-up-wind-up) |
| MFD, `cockpit+0x1ed` | `!= 0` | [the scanner dish grows](#scanner-dish-grows) |

**A flyer skips all of it.** `Gau_BuildCockpitWidgets` (`00431bf8`) ends with a branch taken when the piloted machine's type record has `FlyerFlag` set — `mech+0x1f2 -> +0x50`, the RAZOR alone (see [`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md)'s type-record table). It arms *and* marks done every widget in the table, and sets `cockpit+0x245`, which stops `Cockpit_PowerUpSound` ever stamping the start. The same flag gates the engine hum, [`audio.md`](audio.md#the-cockpit-power-up). <!-- doc-lint: ok -->

Retail runs every one of these animations on the coarse clock from a stamped tick, so a widget that is off screen while its update is skipped shows on its return exactly what it would have shown.

### Weapon rows wink on

`cockpit+0x70` is ten widget slots indexed by `.GAU` weapon row. `CockpitView_RegisterWeaponGauge` (`00432018`), the registration every weapon-row gauge factory ends in, stores the gauge at its row; the number-key handler in `CockpitWidgets_HandleCommand` indexes the same array to arm a row. The delays are `DAT_0049b05a`, ten shorts reading 20, 40, … 200, so the rows arm top to bottom 320 ms apart and the last at 3.2 s.

Every weapon and pod gauge's paint (`EnergyWeaponGauge_Paint` (`00440c68`), `AmmoWeaponGauge_Paint` (`004411b4`), `PodGauge_Paint`, `TurboPodGauge_Paint` (`00441c14`)) and every child's (`WeaponSelectGadget_Paint`, `WeaponSelectGadget_PaintUnderlay` (`00442394`), both through the owner at child `+0x24`) opens on the owning gauge's armed byte. **A row that is not armed draws nothing**, so the console art shows where it will be.

Once armed, an energy row's charge bar fills rather than appearing full. `EnergyWeaponGauge_PowerUpFill` (`00440e84`) shows `min(elapsed * 0x19, value)` against the ticks since the row was armed, then the live value outright from `elapsed >= 0x33`. That reaches `0x400`, the bar's whole range, before the ramp ends. The Turbo Pod's bar runs the same ramp in `TurboPodGauge_PowerUpFill` (`00441d88`), but its value is the pod's raw charge on a 2500-unit bar, so it climbs to 1250 and then jumps to the tank's real level. An ammunition row (`AmmoWeaponGauge_Update`, `00441268`) marks itself done on its first update and has nothing to ramp.

### Shield rings fill

Until the meter is done, `ShieldsGauge_SetStateBlock` (`00443858`) copies each frame's live facings to `+0xcc`/`+0xd0` and zeroes the displayed pair at `+0xb5`/`+0xb9`, so before it is armed [the ring ramp](#ring-ramp--shieldsgauge_updateringpalette-004438f0) paints all six rings dark. Once armed, `ShieldsGauge_Update` calls `ShieldsGauge_PowerUpFill` (`004437a4`) each frame until done:

```
shown = min(ticks since armed, live)      -- per facing, on the rings' 0..0x800 scale
done when both facings show their live value
```

One ring unit a tick, so an even split's `0x200` fills in 512 ticks, about eight seconds. The readouts are not ramped: `+0xbd`, the balance they print, passes straight through.

### Scanner dish grows

The `radar` bank exists for this animation alone. `MfdDisplay_Ctor` (`00445218`) loads it, builds a one-sequence frame table over it — frames 0 to 9, each held 7 ticks — and hands both to a sprite sequencer at `+0x331` (`SpriteSequence_Init`, `00471ca0`); `MfdDisplay_Update` frees all of it the frame the display is done. The frames are 110x110, the dish's own size, and blit at the scanner screen's dish position (screen `+0x18`, [`mfd-scanner.md`](mfd-scanner.md)):

| Frames | Content |
|---|---|
| 0-4 | A small ring on the screen's palette 17 background, widening and brightening each frame |
| 5-8 | The full-size dish ring, its colour settling |
| 9 | The finished dish, with the transparent interior the scanner's own dish frame (`MFD` 14) has |

Frames 0-8 are fully opaque, so each one covers the last.

The display boots on the scanner (`Gau_MfdPanelWidget` sets mode 3). What it does there, until done:

- **Not yet armed:** `MfdDisplay_Repaint` blits frame 0 over the dish in place of the screen's paint.
- **Armed:** `MfdDisplay_Update` starts the sequence on its first pass (`SpriteSequence_Start`, `00471d04`, which stamps the coarse tick and blits frame 0) and steps it on every later one (`SpriteSequence_Step`, `00471d7c`). A step advances past every frame whose hold has run out, so frame `k` shows once more than `7k` ticks have passed. Reaching frame 9 blits it and stops the sequence, and the next pass finds it stopped and sets done: about one second in all. Each of these passes returns before the screen's update slot and the transmission branch, so neither the plot nor a squadmate's transmission is drawn while the dish grows.

On any other screen, the update sets done once the coarse tick passes `+0x345`, which the start stamps at 70 ticks (`0x46`) past its own tick — and at once if the sequence never started, since `+0x345` is then zero.

The sequencer is a 0x2a-byte object, the same kind the [sensor dropout](#sensor-dropout) plays over widget `+0x6c`:

| Offset | Contents |
|---|---|
| `+0x00` | State: 0 idle, 1 playing, 2 just ended — `SpriteSequence_Step` reports an end once and drops back to 0 |
| `+0x02`, `+0x04` | Sequence index, frame index |
| `+0x06` | Coarse tick it started on |
| `+0x0a` | Sum of the holds of the frames reached so far |
| `+0x0e` | Coarse tick the current frame's hold ends |
| `+0x12`, `+0x16` | Current sequence `{count, frames}`, sequence set `{count, sequences}`; a frame entry is `{frame, hold}` |
| `+0x1e` | Sprite bank |
| `+0x22` | Blit position |

## Sensor dropout

While the player's sensor array is damaged, the cockpit's displays drop out at random. The mechanism is on the `PanelGauge` base, whose constructor (`PanelGauge_Ctor`, `00438b20`) sets it up, and `es2_xref.py` finds six calls to its toggle, one in each display's update: the HUD gunsight complex (`Gunsight_UpdateAndPaint`), the MFD (`MfdDisplay_Update`), the Heads-Down Display (`HddDisplay_Update`, `00449bd0`), and the weapon rows' `EnergyWeaponGauge_Update` (`00440c90`), `AmmoWeaponGauge_Update` (`00441268`) and `PodGauge_Update` (`00441850`).

Each frame the caller stores the sensor array's condition at `+0x74` — `Player_DependentCondition(2)` (`004342e0`), which reads dependent 2 of the player's machine (`STRINGS0` group 15 entry 2, `SENSOR ARRAY`) as `damage << 8 / max` and buckets it through `Damage_ToConditionState`, 0 intact to 4 destroyed — and runs `PanelGauge_TickDropout` (`00438bc0`) only when it is nonzero.

| Offset | Contents |
|---|---|
| `+0x6c` | Sprite sequencer played at each change, or 0 for none |
| `+0x70`, `+0x72` | Its sequence for going dark, and for coming back |
| `+0x74` | Sensor condition, 0-4 |
| `+0x76` | 1 dark, 0 shown; `+0x77` holds the previous frame's |
| `+0x78` | Coarse tick the current state ends, 0 while none is drawn |
| `+0x7c`, `+0x80` | Range a dark spell is drawn from, in coarse ticks |
| `+0x84`, `+0x88` | Range a shown spell is drawn from |

Entering either state draws its length with `PanelGauge_RollDuration` (`00438d6c`) — `(next & 0xffff) % (hi - lo) + lo` on the [presentation generator](../simulation/random-generator.md#the-presentation-generator), no draw when the two are equal — and the state flips once it has passed. With a sequencer, the expiry instead starts sequence `+0x70` (going dark) or `+0x72` (coming back) at the display's own rect (`+4`), steps it once a frame, and flips the state on the step after the sequence ends. A condition of 0 forces the dark state, but no caller runs the toggle with one.

| Display | Dark, by condition 1 / 2 / 3 / 4 | Shown | Sequencer |
|---|---|---|---|
| Heads-Down Display (`PanelGauge_Ctor`'s defaults) | 180-360 | 180-360 | none |
| Weapon rows (`WeaponGauge_Ctor`, `0044080c`) | 120-360 | 180-1800 | one per row |
| Gunsight (tables `0049be48`-`0049be66`) | 30-120 / 60-120 / 90-160 / 120-200 | 180-900 / 180-360 / 180-360 / 120-300 | none |
| MFD (`MfdDisplay_SetDropoutRanges`, `00446db4`) | 30-120 / 60-120 / 60-120 / 60-120 | 180-900 / 180-360 / 180-360 / 180-360 | one of three at `004d1d0c`, by condition |

The Heads-Down Display keeps the defaults: every write `es2_fieldscan.py` finds to `+0x7c`-`+0x88` on a `PanelGauge` is in the base constructor, the gunsight's update, `WeaponGauge_Ctor` or the MFD's setter.

### The wipes

A weapon row and the MFD play a short animation at each change; the gunsight and the Heads-Down Display flip at once.

**Weapon rows.** `WeaponGauge_Ctor` gives every row its own sequencer over `WPN_DMG`, on a two-sequence table it builds once from the start frames at `0049c4c8` and the end frames at `0049c4cc`: frames 1 to 9 going dark and 9 to 1 coming back, one coarse tick each. The frames are 112x14, the plate's hole exactly. Frame 1 fills the row with a bright bar, which collapses to a line and then to a dot, and frame 9 is the plain plate in index 42 — a screen switching off, and on again backwards.

**MFD.** `MfdDisplay_Ctor` builds three sequencers over `MFD_DMG`, whose 192x118 frames blit at the screen inset (`+0xeb`, which the constructor also copies into the base rect). `MfdDisplay_SetDropoutRanges` picks one through `[0, 1, 2, 2, 2]` at `0049cb34` and asks it for sequences `set*2` going dark and `set*2+1` coming back. The counts at `0049cb40` and the frame lists at `0049cb4c`-`0049cb88` describe six sequences, but the constructor's loop builds three, sequence `i` for sequencer `i`, each frame held 4 ticks, and hands each sequencer a sequence set of count 1. `SpriteSequence_Select` accepts an index up to the count, so sets 1 and 2 refuse both their numbers and every wipe plays the sequence its sequencer was built with, both ways:

| Condition | Set | Frames |
|---|---|---|
| 1 | 1 | 4, 0 |
| 2-4 | 2 | 4, 5, 6 |

Frame 4 floods the screen in index 30, 5 is a thin line across it, 6 the blank screen in index 17 and 0 a band across the middle. Set 0 is the one whose numbers `SpriteSequence_Select` accepts (frames 0, 4, 0 going dark and 4, 0 coming back), and it belongs to condition 0, which never ticks. Frames 1-3 are in none of the three built sequences, and the bank has no other reader: `es2_xref.py` finds its three references, all in `MfdDisplay_Ctor`.

### While dark

A display holds back while `+0x76` is set, and one with a sequencer also while the sequencer is anything but idle — the pair every paint below tests. Nothing covers what the wipe last blitted, so its final frame stays up for the whole dark spell.

- **Gunsight.** `Gunsight_UpdateAndPaint` and `Gunsight_Paint` skip the whole paint: every child, the readouts, and the floating scanner repeater.
- **MFD.** `MfdDisplay_Update` returns before the buttons' updates, a squadmate's transmission and the screen's own update. `MfdDisplay_Repaint` paints the chrome, the buttons and the title and skips the screen.
- **Weapon rows.** `WeaponSelectGadget_Paint` draws the row plate and nothing else. The underlay and slot number (`WeaponSelectGadget_PaintUnderlay`), the name, the state box, the round count (`AmmoWeaponGauge_Paint`, `004411b4`), the charge bar (`WeaponSliderGadget_Paint`, `00442b38`) and a pod's label all hold back, so the wipe's frame is what fills the plate's hole.
- **Heads-Down Display.** `HddCommandScreen_DrawMap` floods the map viewport in id 19 and draws nothing in it; `HddCommandScreen_RefreshOrders` fonts every order `CPBLUE` with no highlight; `HddDamageScreen_Update` floods the screen rect in id 3 and draws nothing else, the subject caption included. `HddDisplay_Update` flags the current page for a full repaint whenever `+0x76` changes.

**The Heads-Down Display holds its buttons too.** `HddDisplay_HandleWidgetPress` acts on the two page buttons while dark, and for any other press returns before it clears the pending index at `+0x528`. The press stays latched, a later one replaces it, and it runs on the first update that finds the display back — which handles the press before it ticks the dropout, so one frame after the flip. The arrow keys, the magnifiers' keys and `[1]`-`[3]` reach it the same way, since `HddDisplay_KeyDispatch` (`00449fcc`) presses the widget for each; `[S]`/`[I]`/`[W]` and the command display's own keys do not.

Coming back, a weapon row repaints its children and the MFD and the ammunition and pod rows their whole widget, over the wipe's last frame.

### When each display ticks

Each display runs the toggle from its own update, so a display whose update does not run draws no spell. The deadline is an absolute coarse tick, so one that expired meanwhile flips on the first update back.

| Display | Updates |
|---|---|
| Gunsight | Outside the external view (view 4), or during a view transition |
| MFD | Once its power-up has armed it, outside view 4, with its screen on screen; not while the dish is still growing on the scanner |
| Weapon row | Once its power-up has armed it, with its rect on screen or a view transition running |
| Heads-Down Display | Outside view 4 |

## Per-frame ordering

`Sim_RenderFrame` (`0045fb9c`): `Terrain_SetupVisibleRegion`, then `CockpitView_PerFrameUpdate` (`004327ac`; `CockpitViewInstance` widget paint dispatch), then `Scene_SubmitFrameObjects` (the 3D world), then the terrain and the objects on it when the view shows any world ([`terrain-drawing.md`](terrain-drawing.md#the-frame)), then `Player_PerFrameCockpitUpdate`, then three more paint dispatches on `CockpitViewInstance` sub-objects (`+0x1f5`, `CockpitView_GetSquadMessagePort` (`00433158`)'s result, `+0x20b`).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `WPN_DMG` and `MFD_DMG` are damage art — a weapon row's damage fill levels and the MFD's damaged screen. | The names say damage and `WPN_DMG`'s frames do step from full to empty. But `es2_xref.py` finds `WPN_DMG`'s bank pointer only in `WeaponGauge_Ctor`, which builds the sensor-dropout sequencer over frames 1-9, and in `WeaponSelectGadget_PaintUnderlay`'s blit of frame 0; `MFD_DMG`'s only in `MfdDisplay_Ctor`, which builds the MFD's three sequencers over it. Both are the [sensor dropout's wipes](#the-wipes), and `WPN_DMG` frame 0 is every row's plain underlay. |

## Open

- **Open:** what consumes `PWEAPONS` frame 7, a 640x80 strip.
- **Open:** whether any cockpit shows the `PanelAmbience` clock: two two-digit labels and a colon label that `PanelAmbience_TickClock` (`00452144`) blinks, advancing the clock on every second call, the second field wrapping at 59 and the whole at 99:59. Its one construction is in `Gau_PanelAmbienceWidget` (`004326a8`), from the `.GAU` block at 1604, for which `es2_xref.py` finds no caller; that block is zero in all nine retail `.GAU` files.
- **Open:** which mech-object field picks each widget's frame or fill level per frame, for the widgets this doc does not already trace. The `.GAU` holds only geometry.
