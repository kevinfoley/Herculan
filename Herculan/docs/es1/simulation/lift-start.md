# DBSIM.EXE — the lift start

**A mission whose HERC starts near a friendly structure opens with the HERC riding a lift up to the surface before play begins.** The window shows the world only above a line that moves down it as the HERC rises, the lift's 53-row picture under that line and one flat color below the picture; the cockpit's colors start dark, with red and green swapped, and brighten as the ride goes on; at the top the picture jolts up and down for half a second. Nothing in the mission moves meanwhile. ES2's simulator keeps the same two functions behind a gate nothing found sets ([ES2's lift start](../../retail/simulation/mission-deployment.md#the-lift-start)); ES1's runs them on a search of the structures. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

The two functions and their callee `GLPalette_CopyHeader` lie in the object file `00057680`–`00058d93`, between `TSCamera_CopyPose` (`00057be0`) and `Sim_DeathFlash` (`00058414`) ([the main loop's files](main-loop.md#where-the-object-files-start-and-end)).

## When it runs

Step 14 of `Sim_StartMission` ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)) clears `LiftStart_Pending` (`000b5bb0`) and walks `g_BasePool` (`000aed70`) oldest first with `Pool_Prev` from 0 (`0005b33f`). At the first structure whose group (`+0x3f`) has side byte `+0x14` 0, the player's side, and whose point `+0x20` lies less than 40000 from `LocalPlayerMech`'s `+0x20` (`Math_DistanceBetweenPoints`), it sets the flag to 1 and stops. With the flag set, step 14 calls `LiftStart_DarkenPalette` in place of its first frame, and step 15 calls `LiftStart_Rise` after `Sound_SetMusicEnabled` (`0004e294`)(1), before the cockpit's mouse is made live and `Sound_Play`(`0x13`).

The missions of the four retail demo tapes all set it. Each tape's own `script.dat` ([the bundle](../formats/tap-input-tape.md#the-bundle)), read through `HercWorks.Core` and spawned on its start points, puts the player's machine 16,725 and 39,563 from side-0 structures in `DEMO1` and `DEMO2`, 15,000 to 25,495 from four in `DEMO3` and 10,000 and 26,926 from two in `DEMO4`, the distances taken with every height 0 ([Open](#open)).

## The darkening — `LiftStart_DarkenPalette` (`00057cf4`)

ES2's ([the darkening](../../retail/simulation/mission-deployment.md#the-darkening--liftstart_darkenpalette-0045d52c)) with these differences:

1. It builds the two palette objects inline, each `Mem_New`(`0x14`) given the vtables `00096c7a`, `00096c9e` and `00096cc6` in turn, the base constructors' stores, and its `+4` and `+0xc` words zeroed: `LiftStart_DarkPalette` (`000b5d24`) and `LiftStart_BrightPalette` (`000b5d28`). A `Mem_New`(4) fallback sits on a branch only a null first allocation reaches, and a null second allocation skips its stores. Their colors are `LiftStart_DarkEntries` (`000b5d2c`) and `LiftStart_BrightEntries` (`000b5d30`), `0x400` bytes each from `Mem_New`.
2. `GLPalette_CopyHeader` (`00057fbc`), entered through its padding `00057fba`, (to, from), copies `ActivePaletteObject`'s fields `+4` to `+0x13` into each, after which their colors pointer `+8` is set to their own buffer.
3. `PitchGradient_ForcedStep` (`000928ee`) = 8 and `Palette_BlendRangeByPitch`(`ViewObjectPtr`) ([the pitch gradient](../rendering/ground-and-sky.md#the-pitch-gradient-palette-entries-6479)), the first two steps of the branch without the lift, so live entries 64–79 take the gradient's step-8 colors; ES2's calls an empty function there.
4. The 256 live colors, read through `ActivePaletteObject`'s colors pointer `+8`, are copied into both buffers, `0x100` entries each, so both palettes hold the scheme's colors ([the color scheme](cockpit-views.md#the-color-scheme)) and the gradient's.
5. Entries 32–47 and 64–79 of the dark buffer are rewritten from the bright one, a color being red in its low byte, green and blue above it ([installing colors](../rendering/palette.md#installing-colors)): red = green / 2, green = red / 2, **blue = red / 2**, the top byte 1. The loop reads each entry's first byte twice (`00057e74`–`00057e95`), where ES2's reads its third for blue.
6. `Palette_InstallRange`(dark + `0x80`, `0x10`, `0x20`) and `Palette_InstallRange`(dark + `0x100`, `0x10`, `0x40`), (entries, count, first), put both spans of the dark buffer live, and `Palette_BeginCrossFade`(`0x78`, `LiftStart_BrightPalette`, `LiftStart_DarkPalette`), given the palette objects rather than their colors, starts a fade from the dark palette to the bright over 120 counts of the clock `000981c4`, 2 seconds ([the palette module](../rendering/palette.md#differences-from-es2)).
7. `PitchGradient_Enabled` (`000928ec`), 1 in the image, = 0: `Palette_BlendRangeByPitch` leaves entries 64–79 alone until the ride ends, so the fade has them.

It makes no `Palette_FlushDirtyRange` and draws no frame; step 15's `Palette_FlushDirtyRange` sends the dark spans.

## The ride — `LiftStart_Rise` (`00057ffc`)

ES2's ([the ride](../../retail/simulation/mission-deployment.md#the-ride--liftstart_rise-0045d840)), the same loop with ES1's offsets, with no message pump, and with art that ships: it loads `dba\intro`, the folder `000980f8` names being `dba`, with `ClassItem_LoadFromFile`, and takes its first frame unchecked. `SIMVOL0.VOL` holds `dba\INTRO.DBA`, 16,994 bytes: one frame of 320 × 53, every pixel of it a color from 7 to 41, read through `HercWorks.Core`.

At its entry it copies the machine's position `+0x20` and `+0x24` and its angles `+6`–`+0xb`, and the render context `000bdbda`'s bottom `+0x21c` and origin `+0x220`/`+0x224` (its rect is `+0x210` left, `+0x214` top, `+0x218` right and `+0x21c` bottom, [the frame](../rendering/ground-and-sky.md#the-frame)). `Sound_Play`(`0x21`), a row of the catalog ([the catalog](../formats/sound-samples.md#the-simulators-catalog)), comes before the loop's first test, so it plays even for a ride with no passes. The height runs from 4000 below the machine's own to it in steps of `0x23`, 115 passes, each in this order:

1. The machine's `+0x20` and `+0x24` set to their entry values and `+0x28` to the pass's height (x, y, height; ES2's `+0x26` to `+0x2e`); `Cam_Update`(`ViewObjectPtr`), `Raster_InstallViewProjection`(`ViewObjectPtr`), `Raster_SetModelTransform`(0).
2. **The row.** The view point {0, 1000, −*z*}, *z* the camera's height `ViewObjectPtr`'s `+8`, goes straight to `Raster_PerspectiveDivide` and `Raster_ProjectToScreen`, with no `Raster_ModelToView`, so it does not depend on the camera's angles: row = *cy* − ((−*z* << 8) / 1000), *cy* the projection centre's y. The `Raster_SetModelTransform`(0) before it does not feed it.
3. **The bottom cut.** `+0x21c` = the top `+0x214` when the row is below 1, otherwise the lesser of the row and the entry bottom; then `Sim_RenderFrame`.
4. The origin set to {0, 0} and the bottom put back to its entry value; then the art and the fills (below), which with the origin at 0 draw at page coordinates, cut to view 0's clip regions (clip mode 2).
5. The overlay: the gunsight, slot `+0xc` of the vtable at `+0x76` of `CockpitViewInstance`'s `+0x1f1` ([the cockpit's instruments](cockpit-panel.md#building-it)), which `Sim_RenderFrame` has already drawn under the art.
6. The origin put back to its entry value; the fade's step while `LiftStart_FadeRemaining` (`00096cec`), 1 in the image, is set and the row is greater than −200, the step's answer stored back in it; `Sim_EndFrame`; the height + `0x23`.

**The art and the fills** take three cases, *r* being the art frame's rows, the short at its `+4`, not shifted:

1. row + *r* < the top `+0x214`: `Raster_FillRect` of the context's own rect, `+0x210`–`+0x21c`, and no art;
2. row + *r* < the bottom `+0x21c`: `Bitmap_Blit`(art, 0, {0, row}), then `Raster_FillRect` of {0, row + *r*, `+0x218`, `+0x21c`}, whose left edge is 0 rather than the rect's left;
3. otherwise the blit alone.

**The fills' color is `0x3f`**, as `View_FillOutside3dRect` paints the outside view's border ([the external view](cockpit-views.md#the-external-view)); ES2's takes `COLORS.DAT` id 19. Its brush is a two-word {0, `0x3f`} on the ride's own stack, whose address the ride stores at the context's `+0x22c` and never replaces with the old pointer, so after the ride the installed context's brush points into a stack frame that no longer exists until something installs another ([Open](#open)). The sky's fills, by contrast, put the previous brush back ([the horizon line and the sky](../rendering/ground-and-sky.md#the-horizon-line-and-the-sky)).

Each pass's `Sim_EndFrame` swaps the display's pages, so the ride leaves them swapped 115 times, an odd count, before the first frame of play ([the page parity](../formats/tap-input-tape.md#the-page-parity)); with no view command pending its `CockpitView_ProcessViewCommand` waits for a vertical retrace each pass ([the view command](cockpit-views.md#cockpitview_processviewcommand-000226f0)). The fade stops part-way on a short ride, as ES2's does.

After the last pass the machine's `+0x20` and `+0x24` are put back to their entry values and `+0x28` to its own height, and `Sound_Stop`(`0x21`) ends the sound. The angles copied at entry are never written back.

### The jolt at the top

**The last frame of the ride moves up and down on the screen for half a second, where ES2's never reaches the screen.** The end is the clock `000981c4` + `0x1e`, 30 counts; the timer's callback `Time_SimClockTimerTick` (`00063e3c`) advances the clock, so the loop ends whatever it draws. `CockpitView_SetShakeBand`(5) and `Sound_Play`(`0x29`), then, until the clock reaches the end, `CockpitView_StepShake`((`Math_RandomNext`(`000b5c48`) & `0xffff`) % 5) and `Sim_RenderFrame`, with no `Sim_EndFrame`. The loop tests the clock after its body, so one step and one `Sim_RenderFrame` run even when the clock already stands past the end. ES1's step moves the origin of `+0x88`, the page on the screen, as well as `+0x8c`'s ([the view shake](cockpit-views.md#the-view-shake)), so the screen shows the ride's last frame scrolled within view 0's band of 5 rows; the frames drawn meanwhile go to `+0x8c`, which is not shown.

Then `CockpitView_ClearShake`; `Mem_Delete` of both buffers; on each palette, its `+4` and `+0xc` words zeroed and slot `+0x1c` with 3, its destructor, each of the four pointers freed only when it is not null; and `PitchGradient_Enabled` = 1. `LiftStart_FadeRemaining` keeps the value the last step gave it.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `LiftStart_DarkenPalette` | `00057cf4` | `LiftStart_DarkenPalette` (`0045d52c`) |
| `GLPalette_CopyHeader`, entered at its padding `00057fba` | `00057fbc` | inline in `LiftStart_DarkenPalette` |
| `LiftStart_Rise` | `00057ffc` | `LiftStart_Rise` (`0045d840`) |

## Globals

| | Address |
|---|---|
| `LiftStart_Pending`, `int16` | `000b5bb0` |
| `LiftStart_DarkPalette`, `LiftStart_BrightPalette` | `000b5d24`, `000b5d28` |
| `LiftStart_DarkEntries`, `LiftStart_BrightEntries`, 256 colors each | `000b5d2c`, `000b5d30` |
| `LiftStart_FadeRemaining`, `int16` | `00096cec` |
| `PitchGradient_Enabled` (byte) and `PitchGradient_ForcedStep` (`int16`) | `000928ec`, `000928ee` |

## Open

- **Open:** which retail missions other than the demo tapes' open with the lift; the distances above leave out the heights, which `Math_DistanceBetweenPoints` counts, and no mission's start was checked against its structures besides the four tapes'.
- **Open:** what catalog rows `0x21` and `0x29` name.
- **Open:** whether anything fills through the render context's brush `+0x22c` after the ride and before another brush is installed there: the ride leaves it pointing into the ride's own finished stack frame ([the fills' color](#the-ride--liftstart_rise-00057ffc)).
- **Open:** a retail check. Proposed: `DBSIM -eggplant -ptapes\demo3` shows, before play, the forward window dark with a flat color, a 53-row picture coming down into it with the world above, the cockpit's colors brightening, and the picture jolting for half a second before the tape's first frame.
