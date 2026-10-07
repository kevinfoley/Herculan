# Canopy art, blitting, and the cockpit palette

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. All addresses are DBSIM unless noted. Symbols are in `tools/ghidra_scripts/known_symbols_dbsim.json`; apply with `ES2ApplySymbolNames.java`.

Verified against retail data in `ES2/VOL/simvol0/{hb0,hb1,hb2,hba,dpl}/`.

The view manager that loads this art per view, and the `.HD`/`.ED` viewport cutout it is blitted under: [`cockpit-views.md`](cockpit-views.md). The console and HUD widgets painted over it: [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md).

## Canopy art — `.HB0`/`.HB1`/`.HB2` and `.DB0`/`.DB1`/`.DB2`

`CockpitCanopy_LoadViewBitmap` (`00429c2c`, `MECHVIEW.CPP:0x12e`).

No literal `"hb0"`/`"db0"` string exists anywhere in `DBSIM.EXE`. The folder name is built at runtime: the global folder literal `"dba"` (or `"hba"` when `VideoMode_PanelMode == 3`) is copied to a stack buffer and index 2 overwritten with an ASCII digit via `_itoa`, giving `db0`/`db1`/`db2` or `hb0`/`hb1`/`hb2`. Then `ResourcePath_BuildFolderName(hercName, buf)` → `ClassItem_LoadResource`. The same trick produces `ed<i>`/`hd<i>` from `"edg"`/`"hdg"` — see [`cockpit-views.md`](cockpit-views.md#hd0-hd3--ed0-ed3--3d-viewport-clip-regions).

Files are Dynamix bitmap arrays ([`dfn-hfn-dci.md`](dfn-hfn-dci.md)) with one frame: `.DB*` 320x240 (76834 bytes), `.HB*` 640x480 (307234). The nine player hercs each have one per view, 27 of each; the extracted copies under `ES2/VOL/simvol0/` are 10 bytes longer for the VOL entry framing.

`CockpitCanopy_FreeViewBitmap` (`00429de4`) releases one view's handle, also nulling slot 3 when freeing view 2. Used only when `CockpitArt_LoadOnDemand` (`004d2704`) is set — a low-memory mode that loads and frees per view switch rather than keeping all four resident.

### Known defect in the retail code

The `maybe_CockpitLayoutMode == 1` branch increments byte 2 of the **shared global** `"dba"` literal (`MOV ECX,[0x4a0a28]; INC byte ptr [ECX+2]` at `00429d3e`) rather than its local buffer. The follow-on load still uses the unmodified local buffer, so that branch loads `db0` twice and corrupts the global folder name for every later user. `004d25bc` is byte `+0x7c` of the video-mode block, which `Main_StaticInit` zeroes, and no write of 1 to it has been found ([Open](#open)) — see [`cockpit-views.md`](cockpit-views.md#video-modes).

## Blitting

`Bitmap_Blit` (`0048159c`) — `Bitmap_Blit(bitmap, {int x, int y}, flags)`.

| Flags | Effect |
|---|---|
| 0 | none |
| 1 | flip vertically |
| 2 | mirror horizontally |
| 3 | both |

Confirmed at the reticle corner-bracket draw (`0044401d`–`0044403a`), which blits one corner sprite four times with flags 0/2/1/3, offsetting x by the bitmap's width field (`+6`) for flag 2 and y by its height field (`+4`) for flag 1. `Bitmap_BlitScaled` (`004816bc`) is the same drawn at an explicit `{w, h}` destination size; `CockpitView_SetView` uses it only in `maybe_CockpitLayoutMode == 2`, and the video-mode blit helpers use it to double 320-wide art in the 640-wide modes.

## Palette

**The live 256-slot palette is the theater palette, in full.** `World_LoadTheater` (`0042e010`) calls `Palette_LoadAndActivate` (`00430394`) with `dpl\world<N>`, and that object becomes `ActivePaletteObject` (`0049b020`); field `+8` is its 256 x 4-byte entry array.

**`COCKPIT.DPL` contributes exactly one 24-entry window.** `CockpitViewManager_LoadViews` issues a single call:

```
Palette_InstallRange(0x2a, 0x18, COCKPIT.DPL.entries + (schemeIndex*0x18 + 0x20)*4)
```

Live slots **42-65** ← `COCKPIT.DPL` entries `[32 + 24*schemeIndex, +24)`. No other site installs `COCKPIT.DPL`; its remaining 232 entries are never read.

`schemeIndex` is the mech type record's `+0x52`, i.e. **offset 80 of `dat\<MECH>.DAT`** ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#mech-type-record)). Retail values are a 0-8 permutation over the nine player hercs, so the nine schemes tile `COCKPIT.DPL` entries 32-247 exactly:

| Herc | scheme | COCKPIT.DPL entries |
|---|---|---|
| APOCA | 0 | 32-55 |
| COLOSSUS | 1 | 56-79 |
| SAMSON | 2 | 80-103 |
| MAVERICK | 3 | 104-127 |
| OGRE | 4 | 128-151 |
| OUTLAW | 5 | 152-175 |
| RAPTOR2 | 6 | 176-199 |
| RAZOR | 7 | 200-223 |
| TOMAHAWK | 8 | 224-247 |

`COCKPIT.DPL` is a 256-entry palette (1050 bytes: 9-byte prefix, `0F 00 28 00`, size `0x408`, start index 0, count 256, 256 x 4 bytes). Entry layout is `[R][G][B][flag=1]`, 6-bit channels scaled x4 — entries 1-7 are the textbook VGA blue, green, cyan, red, magenta, brown and light grey at `0x2a`.

Canopy art indices are used **as authored**; there is no shift, and the live palette is not assembled from two `.DPL` files.

### Corroboration

- The measured retail values resolve to it exactly: APOCA renders canopy index `i` as `COCKPIT.DPL[i-10]` (slot 42 → entry 32 = scheme 0); COLOSSUS as `COCKPIT.DPL[i+14]` (slot 42 → entry 56 = scheme 1).
- All ten `WORLD<n>.DPL` park precisely slots 42-65 at pure green (R = B = 0, 6-bit G of 62 or 63), with slots 41 and 66 not green — the exact window the cockpit scheme overwrites.

Consequences: the heading tape's index 74 is a theater colour; the shield meter's green is a theater colour absent from `COCKPIT.DPL`; the canopy hazard stripes at index 13 render as the theater's yellow (measured 92% agreement at `(192,192,44)`).

### Palette module

| Symbol | Address | Role |
|---|---|---|
| `ActivePaletteObject` | `0049b020` | Live palette; `+8` = entry array. `0049b024` last uploaded, `0049b028`/`0x2c`/`0x30` dirty min/max/valid. |
| `Palette_LoadAndActivate` | `00430394` | Load a `.DPL` and make it active. |
| `Palette_SetActive` | `004303b0` | Swap active object, return previous. |
| `Palette_InstallRange` | `004303c4` | Copy `count` entries to `baseIndex`, extend dirty range. |
| `Palette_ReadRange` | `00430440` | Inverse of the above. |
| `Palette_GetEntry` | `00430474` | Single entry. |
| `Palette_FlushDirtyRange` | `0043048c` | Upload dirty range; whole palette if the active object changed. |
| `Palette_CycleAnimatedRanges` | `004306ac` | 5 slots of per-frame sub-range rotation, keyed off `Time_GetCoarseTicks`. |
| `Palette_InterpolateColours` | `004307b0` | Interpolate colour pairs across N steps. |
| `Palette_BeginCrossFade` | `004308fc` | Precompute 8.8 per-channel deltas between two palette objects over a duration in coarse ticks; either may be null (fade to/from black). |
| `Palette_StepCrossFade` | `00430b34` | Write the fade's colours for the time elapsed since its first call; returns the coarse ticks remaining. |
| `Palette_InterpolateIndexRanges` | `00430d08` | Interpolates index *ranges*, not colours — terrain shading only, driven from `WORLD<n>.WLD`. |

`Palette_BeginCrossFade` has three call sites in two functions, neither part of steady-state cockpit rendering and neither touching the secondary palette:

- **The death flash, `Sim_DeathFlash` (`0045dc34`).** It loads the `death1`, `death2` and `world0` palettes, mutes the effects, plays sound `0x26` (`explos2.wav`), installs `death1` whole and fades it to `death2` over `0x78` ticks, then `death2` to black. For the first `0x1e` coarse ticks of the first fade it also shakes the view in a band of 10, one `Math_RandomBelow(10)` step a frame on the [presentation generator](../simulation/random-generator.md#the-presentation-generator). It ends by installing `world0` and running a `StatusAlertPanel` of kind 2. `es2_xref.py` finds nothing that reaches it ([Open](#open)), and **its two palettes do not ship**: no `.VOL` and no file in the install is named `death1` or `death2`. What the player's death does play is [the death camera](../simulation/external-views.md#the-player-death-camera).
- **The lift start's darkening, `LiftStart_DarkenPalette` (`0045d52c`).** It rewrites entries 32-47 and 64-79 as (G/2, R/2, B/2), installs them, then fades from that palette back to the live one. The rest of the sequence is in [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-lift-start).

**The fade runs on the clock, not per call.** The first `Palette_StepCrossFade` after a `Palette_BeginCrossFade` arms an end time of now plus the duration, and every call writes `from + delta * elapsed >> 8` for the coarse ticks elapsed since then, over the whole entry count from slot 0; entries whose endpoints agree have a zero delta and do not move. Once past the end it returns 0 **without writing**, so the fade's last colours are those of the last call before the end, never the destination itself. A caller that stops calling early leaves the palette part-way.

### The damage shake

Taking a hit shakes the view and flashes the palette, for `0x3c` coarse ticks — 0.96 s. Two functions in `MECHVIEW.CPP` own it, and it is the sibling of the footfall kick below: the cockpit's per-frame pass (`CockpitView_PerFrameUpdate`) ticks the two one after the other.

`Cockpit_StartHitShake` (`00434010`) arms it, and returns at once in view mode 4:

```
if (view.mode == 4) return
if (endTick != 0) { Palette_RestoreFromImpact(); CockpitView_ClearShake() }
endTick = now + 0x3c
if (nextToggleTick == 0) {
    nextToggleTick = now + rand() % 10
    Palette_ActivateImpact()
    CockpitView_SetShakeBand(5 << VideoMode_YCoordShift)
}
```

**A second trigger inside the window stops the shake rather than compounding it.** The restart restores the palette and clears the view band, and then finds `nextToggleTick` still non-zero — the tick keeps it armed for as long as the shake runs and zeroes it the frame after the window closes — so the arm block is skipped and the band is not put back. `endTick` is extended all the same. So a hit 0.3 s into a shake buys another 0.96 s of `CockpitView_StepShake` calls against a disarmed band, which move nothing: the view goes still for the rest of the window. See [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**What the flash does after a second trigger depends on its phase.** `Palette_ToggleImpact` swaps the active palette with the one it saved (`004cfd8c`), and `Palette_RestoreFromImpact` reactivates the original (`004cfd90`) without touching that saved slot. A trigger that lands while the theater palette is showing leaves the impact palette saved, and the flash carries on. One that lands while the impact palette is showing leaves the theater palette saved, so every later toggle swaps the theater palette with itself and the flash stops on it for the rest of the window.

**A finished view slide can put the band back inside that window.** `CockpitView_StepViewTransition` ends a slide with `CockpitView_SetShakeBand(10)` when the byte at `0049ac45` is set. `CockpitView_ApplyViewState` sets it whenever a view change clears an armed band and clears it on a change to view 4, so once a view has changed during a running shake, any later slide that finishes inside a disarmed window restarts the walk.

`Cockpit_HitShakeTick` (`0043408c`) runs it: while `endTick` is in the future it calls `CockpitView_StepShake(rand() % 5)` every frame and flips the palette each time `nextToggleTick` expires, rearming that at `now + rand() % 10`. On expiry it restores both. Mode 4 clears `endTick` outright, so leaving the cockpit ends a shake in progress.

**The shake is the projection centre again, not a camera move** — the same mechanism as the step kick. `CockpitView_SetShakeBand` (`0042d2f8`) copies the resting view offset to `004cfae4`/`004cfae8` and sets two limits either side of it, `+amplitude` or `-amplitude` depending on the view mode; `CockpitView_StepShake` (`0042d4a8`) then moves the y offset toward **whichever limit is farther** by up to its step argument.

**Which limit that is flips at the band's middle**, so the walk reverses every time it crosses the centre and can never settle — and, after the first step off the limit it starts on, **it never reaches either limit again**. Moving outward requires being on the near side of the middle, so the furthest reachable offset is the largest sub-middle offset plus the largest step. With the band of `5 << VideoMode_YCoordShift` the arm passes — ten device pixels in the 640-wide modes — and steps of 0-4, the offset starts at the limit and thereafter ranges over 1 to 8. **The band is wider than the excursion it produces**, which is the trap in reading the amplitude as the travel.

The flash is a whole-palette swap rather than a fade: `Palette_ActivateImpact` (`0042ea44`) makes the secondary palette `ImpactPaletteObject` (`0049aef8`) active and saves the previous one, `Palette_ToggleImpact` (`0042ea70`) alternates the two, and `Palette_RestoreFromImpact` (`0042ea94`) puts the original back. That object is loaded twice over: `World_LoadTheater` (`0042e010`) builds it from the theater's own `IMPACT<n>.DPL` — `wld\WORLD<n>.WLD`'s third trailing string — and step 6 of the cockpit load sequence ([`cockpit-views.md`](cockpit-views.md#cockpitviewmanager_loadviews-sequence)) then overwrites its 24 cockpit indices from `IMPACTCP.DPL`. So a flash recolours the world and the cockpit together, each from its own source.

Its two triggers are both damage: a direct-fire hit on either of the player's own **cockpit** components while that component still reads under `0x64` damaged ([`../simulation/damage-system.md`](../simulation/damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated)), and the landing at the bottom of a long slide ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#the-landing)). The first is gated and sits inside that function's band-change branch, so a shot that only scuffs the cockpit's armour is not felt; the second is ungated.

Because the swap is of the whole palette, everything drawn through it flashes: the world, the canopy and the HUD. How much of the HUD moves depends on the theater. Comparing each `WORLD<n>.DPL` with its `IMPACT<n>.DPL` at the palette indices `COLORS.DAT`'s twenty-seven entries name, twenty move under `IMPACT1`, `3` and `5`, eighteen under `IMPACT0`, `2`, `4`, `8` and `9`, and five under `IMPACT6` and `7`. Where they move they move a long way: in all but `IMPACT6` and `7`, HUD green `(64,212,40)` becomes orange `(208,92,0)` and white `(228,228,228)` becomes `(252,0,0)`; those two leave both unchanged.

The shield meter's rings are computed rather than taken from either file. `ShieldsGauge_UpdateRingPalette` derives their six colours from the six channel bytes at `ShieldRingColors` (`0049c9cb`) and installs them at slots 66-71 of whichever palette is active. It runs when the gauge is built or repainted, and from `ShieldsGauge_Update` on the frame after the shield readings change, not on every frame. Slots 66-71 of eight of the ten `IMPACT<n>.DPL` are black, and of `IMPACT6` and `7` the theater's own filler, so through a flash the rings show what the gauge last wrote into the impact palette while it was active, or the file's colours if it never has ([Open](#open)).

### The step kick

Each footfall of the player's own machine bobs the view through the projection centre, as the shake does. `Mech_PlaceLegsOnGround` calls `Cockpit_StartStepKick` (`00434144`) for a locally piloted machine; outside view mode 4 it takes out any offset in progress (`CockpitView_ClearKickOffset`, `0042d854`) and restarts the curve, `StepKickStartTick` (`0049b634`) at now and `StepKickEndTick` (`0049b638`) at now + `0x3c` coarse ticks, 0.96 s. A machine stepping faster than that re-triggers it.

`Cockpit_StepKickTick` (`00434194`), which `CockpitView_PerFrameUpdate` runs straight after `Cockpit_HitShakeTick`, plays `StepKickCurve` (`0049b046`), ten `int16`s `1 2 3 4 5 5 4 3 2 1`, indexed `(now - start) * 10 / 0x3c`. It halves the value when `VideoMode_YCoordShift` is 0 and shifts it left by `YCoordShift - 1` otherwise, so the peak is five device pixels in the 640-wide modes. `CockpitView_SetKickOffset` (`0042d82c`) adds the value to the render context's `+0x224` after taking out the one it added last (`CockpitView_KickOffset`, `004cfd3c`), and `Raster_InstallViewProjection` subtracts that field, so the whole image slides behind a cockpit that stays put. View mode 4 or the end tick passing ends it, through `CockpitView_ClearKickOffset`. The camera node's world orientation does not move over a stride.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `Palette_BeginCrossFade` is the damage flash | Its call sites are the death flash and the lift start. The damage flash is a hard alternation between two whole palettes — `Palette_ActivateImpact`/`Palette_ToggleImpact` — with no fade of any kind between them |
| `Palette_StepCrossFade` advances the fade one step per call | It interpolates by coarse ticks elapsed since its first call, so a slow frame rate jumps further per frame rather than stretching the fade — see "Palette module" |
| The shake's amplitude is how far the view travels | The band is the limit pair the walk is bounded by, not its excursion. A step is only ever taken toward the farther limit, which reverses at the middle, so a ten-pixel band produces a one-to-eight-pixel wander — see "The damage shake" |

## Open

- **Open:** what reaches the death flash, `Sim_DeathFlash`. `es2_xref.py` finds no branch, stored pointer or vtable slot holding it, and the `death1` and `death2` palettes it loads are not in the shipped data.
- **Open:** whether `maybe_CockpitLayoutMode` (`004d25bc`) is ever non-zero. `es2_xref.py` finds 23 absolute occurrences, all `MOVSX` reads, and `es2_fieldscan.py 7c` over the holders of the block base `004d2540` finds `Main_StaticInit`'s store of 0 and one read; no other write found.
- **Open:** no writer of `HitShakeEndTick` (`0049b0fc`) or `HitShakeNextToggleTick` (`0049b100`) found outside `Cockpit_StartHitShake` and `Cockpit_HitShakeTick`. `es2_xref.py` finds 7 and 6 absolute references, all in those two functions, and none of the `0049b0xx`-`0049b12x` addresses the image loads as a base reaches either.
- **Open:** what the shield meter's rings show through a retail flash — whether a reading change lands while the impact palette is active often enough to keep them steady, or they drop to the impact palette's slots 66-71.
