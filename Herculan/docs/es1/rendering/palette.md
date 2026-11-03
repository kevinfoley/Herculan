# DBSIM.EXE — the palette module

**ES1's simulator keeps one live palette as ES2's does, notes which of its entries change, sends the changes to the display each frame, cycles color ranges and fades between whole palettes on a clock.** The module is ES2's palette module ([ES2's](../../retail/rendering/cockpit-canopy-palette.md#palette-module)) with the differences below. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function removes its own arguments, so each argument list here is in stack order, ES2's reversed ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the module starts and ends

The palette's code is `00027e10`–`00028de7` and its initialized data `00092c20`–`00092c43`, between the clouds' code and `gndtex.cpp` ([where the object files start and end](ground-and-sky.md#where-the-object-files-start-and-end)). Zero bytes fill the code before `00027e10` and after its static initializer, `Palette_RegisterSubsystem` (`00028dd8`), which closes it ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)); no `ConfigPart` run opens it and its data holds no source path.

Its functions run in the order of ES2's, from `Palette_LoadResources` (`0043034c`) to `Palette_RegisterSubsystem` (`00430e90`). Where ES2's has `Palette_ShadeRampMidIndex` and `Palette_ShadeRampLookup` after `Palette_InterpolateIndexRanges`, ES1's has `Palette_UnpackColour` and `Palette_BuildTextureShadeTables` ([the texture color tables](polygon-shading.md#the-texture-colour-tables)).

## Differences from ES2

- **21 shade ramp records, against ES2's 129.** `Palette_LoadResources` (`00027e10`), frameless, allocates `g_ShadeRamps` (`00092c34`), 21 records of 8 bytes `{int16 *indices, int16 count}`, each with a buffer of 21 `int16` indices (`0x2a` bytes), and zeroes the five cycle slots (`000bc5d6`). `Palette_RegisterSubsystem` registers it at subsystem phase 2 with `RegisterSubsystemLoader`; its address is the dword at `000adc20`, among the static constructors. ES2's records hold 129 indices each. `Palette_SetRampsToRange` and `Palette_SetRampsToRangeList` (`000280e4`) pass `Raster_SetColourRamps` a fourth argument, 0; ES2's `Palette_SetRampsToRangeList` passes three.
- **The clock is the dword `000981c4`.** `PaletteCycle_Register`, `Palette_CycleAnimatedRanges` (`00028260`) and `Palette_StepCrossFade` (`000287a8`) read it where ES2's call `Time_GetCoarseTicks`; it counts 60 a second ([the tick length](../simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)). `es2_xref.py` finds one call of `PaletteCycle_Register`, from `World_LoadTheater`, which registers the records of `cyc\world<N>` ([the load](../formats/wld-world.md#the-load)); `SIMVOL0.VOL` and `ZONES.VOL` hold no `cyc\` entry ([the layout](../formats/wld-world.md#the-layout)).
- **The upload sends only the changed entries.** `Palette_FlushDirtyRange` (`00027f74`), when the active palette (`00092c20`) is not the one last uploaded (`00092c24`), passes it to `Palette_UploadWhole` and records it, as ES2's does. Otherwise it calls the display driver's palette routine through `0009df08` with (the dirty range's first entry `00092c28`, last − first + 1, the address of that entry's color) when the dirty flag `00092c30` is set, and with (0, 1, the palette's first color) when it is clear, re-sending entry 0 each time; ES2's hands all 256 entries to the screen when the flag is set and nothing when it is clear. It clears the range and the flag after. `es2_xref.py` finds calls from `Sim_EndFrame`, `CockpitViewManager_LoadViews` and twice from `Sim_StartMission`.
- **Activating a palette builds the texture color tables:** `Palette_LoadAndActivate` (`00027e5c`) calls `Palette_BuildTextureShadeTables` after making a palette active ([the texture color tables](polygon-shading.md#the-texture-colour-tables)).

The rest is ES2's, statement for statement after the compiler: `Palette_SetActive` (`00027e7c`), which the impact palette's switches call ([the pitch gradient](ground-and-sky.md#the-pitch-gradient-palette-entries-6479)); `Palette_InstallRange`, `Palette_ReadRange` (`00027f18`) and `Palette_GetEntry`; `Palette_ReadShadeRampRanges` (`00028000`); `PaletteCycle_Unregister` (`00028228`) and the cycling ([color cycling](../../retail/rendering/cockpit-canopy-palette.md#color-cycling)); `Palette_InterpolateColours` ([interpolating colors](../../retail/rendering/cockpit-canopy-palette.md#interpolating-colors)), which the pitch gradient calls; and the cross fade, `Palette_BeginCrossFade` (`00028528`), given (duration, to, from), and `Palette_StepCrossFade`, which run on the clock as ES2's do ([the palette module](../../retail/rendering/cockpit-canopy-palette.md#palette-module)) and start at the entry `PaletteFade_FirstEntry` (`000bc60c`) ([Open](#open)). `es2_xref.py` finds `Palette_BeginCrossFade` called from `LiftStart_DarkenPalette` (`00057cf4`) and twice from `Sim_DeathFlash` (`00058414`), and `Palette_StepCrossFade` from `LiftStart_Rise` (`00057ffc`) and four times from `00058414`, as ES2's are: the lift's fades from its darkened copy of the live palette to the copy left as it was ([the lift start](../simulation/lift-start.md#the-darkening--liftstart_darkenpalette-00057cf4)).

## Installing colors

**A color the simulator installs changes on the screen at the end of the frame, when the palette's changed entries are sent to the display.** `Palette_InstallRange` (`00027e98`), (source, count, base), copies *count* dwords from *source*, each a color with red in its low byte and green and blue above it, into the colors the active palette's dword `+8` points at, from entry *base*. It widens the dirty range `Palette_DirtyFirst`–`Palette_DirtyLast` over them, or starts it there and sets `Palette_DirtyValid` when that is clear. Nothing reaches the display until `Palette_FlushDirtyRange` ([above](#differences-from-es2)) runs, which `Sim_EndFrame` does once a frame, after the frame's draw and before its page flip ([the frame's end](../simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904)). The shield rings' colors, which the shield display installs during the frame's draw, are sent that way ([the ring colors](../simulation/cockpit-energy-shields.md#the-ring-colors--shieldsgauge_updateringpalette-00039340)).

**The whole screen, the world as well as the cockpit, is palette indices shown through the one live palette, so a color installed changes every pixel of that entry, wherever it was drawn.** The mission's display is `VGA_8`'s mode 0, 8 bits a pixel and 256 colors ([the video memory](screen-pages.md#the-video-memory)), and the world's faces, ground and sky are drawn into it as indices ([polygon shading](polygon-shading.md)). So the shield rings' entries 80 to 85, a cockpit script's palette upload ([the simulator's scripts](../formats/ttm-scripts.md#the-simulator)) and the death flash's palettes and fades ([the death flash](../simulation/main-loop.md#the-death-flash--sim_deathflash-00058414)) show on the world as on the cockpit, on whatever pixels hold the entries they change ([Open](#open)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `Palette_LoadResources` | `00027e10` | ES2's `0043034c` |
| `Palette_LoadAndActivate`, `Palette_SetActive`, `Palette_InstallRange` | `00027e5c`, `00027e7c`, `00027e98` | ES2's `00430394`, `004303b0`, `004303c4` |
| `Palette_ReadRange`, `Palette_GetEntry`, `Palette_FlushDirtyRange` | `00027f18`, `00027f54`, `00027f74` | ES2's `00430440`, `00430474`, `0043048c` |
| `Palette_ReadShadeRampRanges`, `Palette_SetRampsToRange`, `Palette_SetRampsToRangeList` | `00028000`, `00028044`, `000280e4` | ES2's `004304dc`, `00430518`, `00430590` |
| `PaletteCycle_Register`, `PaletteCycle_Unregister`, `Palette_CycleAnimatedRanges` | `000281cc`, `00028228`, `00028260` | ES2's `00430638`, `00430688`, `004306ac` |
| `Palette_InterpolateColours`, `Palette_BeginCrossFade`, `Palette_StepCrossFade` | `000283b0`, `00028528`, `000287a8` | ES2's `004307b0`, `004308fc`, `00430b34` |
| `Palette_InterpolateIndexRanges`, `Palette_UnpackColour`, `Palette_BuildTextureShadeTables` | `000289e0`, `00028afc`, `00028b28` | ES2's `00430d08` and none |
| `Palette_RegisterSubsystem` | `00028dd8` | ES2's `00430e90`; frameless |

## Globals

| | Address |
|---|---|
| `ActivePaletteObject`, the live palette, and `Palette_LastUploaded` | `00092c20`, `00092c24` |
| `Palette_DirtyFirst`, `Palette_DirtyLast`, `Palette_DirtyValid` | `00092c28`, `00092c2c`, `00092c30` |
| `g_ShadeRamps`, the 21 ramp records | `00092c34` |
| `PaletteFade_EndTime`, `PaletteFade_Armed` | `00092c38`, `00092c3c` |
| `PaletteCycle_Slots`, `PaletteCycle_DueTimes`, `PaletteCycle_Offsets`, five each | `000bc5d6`, `000bc5ea`, `000bc5fe` |
| `PaletteFade_From`, `PaletteFade_FirstEntry`, `PaletteFade_EntryCount`, `PaletteFade_Duration` | `000bc608`, `000bc60c`, `000bc60e`, `000bc610` |
| `PaletteFade_DeltaRed`, `PaletteFade_DeltaGreen`, `PaletteFade_DeltaBlue`, 256 `int16` each, and `PaletteFade_Mode` | `000bc614`, `000bc814`, `000bca14`, `000bcc14` |

## Open

- **Open:** what writes `PaletteFade_FirstEntry` (`000bc60c`): a grep of the disassembly by address finds its five reads in `Palette_StepCrossFade` and no store, so the fade starts at entry 0 unless something reaches it through a pointer.
- **Open:** what calls `Palette_ReadRange`, `Palette_ReadShadeRampRanges` and `PaletteCycle_Unregister`. `es2_xref.py` finds no branch to any of them; the three dwords it finds holding `Palette_ReadShadeRampRanges`'s address, at `000ad6ed`, `000ad893` and `000ad8b3`, are each a `0x280` (640) word with a zero word before it, inside tables of screen sizes (640 × 350, 640 × 480).
- **Open:** whether a world face draws any of the shield rings' entries 80 to 85, so that the rings' colors show on the world. A scan of every `.DBA` bank, every `.DTS` face's solid color and every shade table in the install finds none.
- **Open:** which display driver routine `0009df08` holds while the simulator runs, and what it does with a count of 1 at entry 0.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger during a mission, `BP <CS>:00027f74` fires on each call from `Sim_EndFrame`, and the byte at `00092c30` at the break says which call follows: the dirty range's (`00027fc6`) when it is 1, entry 0's alone (`00027fdf`) when it is 0.
