# Preferences and controls (DBSIM.EXE)

The two panels [F12] reaches, and the file they edit. Both are members of the modal alert-panel family whose shared base, paint conventions and button widget are in [`alert-panels.md`](alert-panels.md#what-the-family-shares); this doc owns only what is particular to them.

`PreferencesPanel_Raise` (`0045cfd4`) is what commands `0x58` ([F12]) and `0x219` ([Alt+P]) reach ([`cockpit-input.md`](cockpit-input.md#keyboard-commands-are-scancodes)). It raises `DAT_004d2576` to stop the simulation for as long as the panel is up, snapshots the view object's whole settings block beforehand and writes it back on the way out. The CONTROLS button then builds the second panel over the first, which stays on screen behind it.

**Behind the panel the camera circles the player's machine.** Before running the panel, `PreferencesPanel_Raise` copies the view camera (`ViewObjectPtr`, [`external-views.md`](external-views.md#the-camera-object--cam)) into a local `CAM` and gives the external view a 3D rect of its own through `ExternalView_SetRect` (`0042d89c`): the screen's whole width, down to the row above the panel's top edge (`panel+0x08`), with the projection centre half way across and `0x32 << VideoMode_YCoordShift` rows down — 300 rows with the centre at (320, 100) at 640x480. When the cockpit view manager's current view (`+0x14`) is 4 it installs that rect at once, attaches the camera to `LocalPlayerMech` (`Cam_AttachTo`, setting the camera's `+0x36` to 1 when its `+0x4a` is 0) and draws a frame. From any other view it first draws frames until any view transition in progress has finished, queues view command 2 (`CockpitView_QueueViewCommand`), and keeps drawing frames until that transition has started and ended, attaching the camera the same way when it starts; entering view 4 installs the rect and floods everything outside it (`CockpitView_ApplyViewState`, [`external-views.md`](external-views.md#what-the-external-view-shows)).

It then installs `PreferencesPanel_OrbitViewHook` (`0045cfac`) as the panel's draw hook (`PreferencesPanel_SetDrawHook`), which `PreferencesPanel_Run` calls once per pass of its loop: `Cam_Steer(ViewObjectPtr, -0x40, 0, 0)`, `Cam_Update`, `Sim_RenderFrame`. In the [orbit](external-views.md#steering-the-camera--cam_steer) that steer takes the heading rate from the 0 the attach left towards `Q8(0x800, -0x40)`, `-0x200` (2.8°) a pass, which it reaches on the third pass, and holds the pitch and zoom rates at 0, so the view turns steadily round the machine at the least orbit distance. Every `Cam_Update` also records the frozen machine into the chase view's ring ([`external-views.md`](external-views.md#mode-3-the-chase)). **The turn is per pass, not per unit of time.** The 40 ms wait that paces the simulator, `Time_BeginSimTick`, sits in `Sim_Run`'s loop (its one caller found by `es2_xref`), and the panel's loop runs inside a tick and never returns there, so the view turns as fast as the machine can draw it. `PreferencesPanel_Run` is the hook's one caller, so the view stands still while the CONTROLS panel is up, whose loop case 9 runs inline.

On the way out it writes the camera back, puts the default rect back (`ExternalView_ResetRect`, `0042d944`), and then either, from view 4, repaints the band outside the rect and the caption around one more frame, or puts the view manager straight back in the view it had (`CockpitView_SetView`) around one more frame, with no transition.

**A panel's loop is not the simulator's.** `AlertPanel_Enter`, then poll the device, run the panel's handler, paint, present — and never `Sim_MainTick`, which is the only caller of `Sim_PollPlayerInput`. So while any of these panels is up no key or joystick button reaches the player's machine at all, which is what lets the CONTROLS panel read the stick as a configuration device. Nor does a keyboard command reach the cockpit: each pass calls `Input_BuildPlayerDevice` and hands its event to the panel's own vtable `+0x10` handler and nothing else, so neither `Sim_DispatchCommand` nor `CockpitWidgets_HandleCommand` sees a key, and `AlertPanel_Enter` closes the command queue that `SimCommandQueue_Push` fills (`0049eacc` cleared at `0045467b`, set again by `AlertPanel_Leave`). Under a panel the only keys that act are the three `Key_WndProcHook` keeps for itself, [Alt+Tab], [Alt+Esc] and [Ctrl+Esc], which leave full screen ([`cockpit-input.md`](cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)). The pause flag is the belt to that pair of braces: it gates the world updates *inside* `Sim_MainTick` for the frames the simulator does run.

## `data\prefs.cfg` — the option array

**The file is the array.** `Prefs_LoadOptions` (`00459754`) memsets `SimOptions` (`004d1fbc`) to zero for `0x36` bytes and reads the file straight over it with no parse at all, so **an option's index is its byte offset** and a retail `prefs.cfg` is 54 bytes. It then walks all 54 options, calls the handler of each one that has one with the byte just loaded, and seeds both shadow arrays — the load-time one at `004d1ff2` and the outgoing one at `004d2028` — from the array.

Three functions write it, all through `Prefs_SetOption` (`0045993c`), which saves the outgoing byte to the shadow array at `004d2028`, stores the new one, and — when told to apply — calls that option's handler from the parallel table `Prefs_OptionHandlers` (`004d2060`):

| | |
|---|---|
| `Prefs_ToggleOption` (`00459980`) | `value ^ 1` |
| `Prefs_StepOption` (`004599b8`) | `value + 1`, wrapping to 0 at the caller's modulus |
| `Prefs_StepOptionBack` (`004599f4`) | `value - 1`, wrapping to `modulus - 1` below zero |

The caller supplies the modulus, which is why one pair drives a three-value row and a five-value one.

`Prefs_Init` (`0045a19c`) registers the handler table through `Prefs_RegisterOptionHandler` (`00459c58`) and loads the file. **Five options have a handler and the other 49 have none**, so most settings are read where they are used rather than pushed anywhere:

| Option | Handler | Does |
|---|---|---|
| 0 MUSIC | `Prefs_ApplyMusicOption` (`00459c98`) | `Sound_SetMusicEnabled`, then `Sound_UnmuteMusic` / `Sound_MuteMusic` |
| 1 SOUNDS | `Prefs_ApplySoundsOption` (`00459c6c`) | `Sound_SetEffectsEnabled`, then `Sound_UnmuteEffects` / `Sound_MuteEffects` |
| 2 PILOT MESSAGE | `Prefs_ApplyPilotMessageOption` (`00459cc4`) | `Sound_SpeechEnabled` (`0049f97e`) `= byte != 0`. That flag gates every recorded line — `Voice_Acquire` opens no clip and `Snc_Start` plays none while it is down — on both message channels ([`cockpit-messages.md`](cockpit-messages.md#the-port)) |
| 8 TERRAIN TEXTURE | `Prefs_ApplyTerrainTextureOption` (`00459d4c`) | `TerrainTexturingEnabled` (`004aab2c`) |
| `0x0e` THROTTLE | `Input_SetThrottleLeverMode` | the herc controls block's THROTTLE row, and so an independent corroboration of where that block starts |

The first two are the same function with the music and effects halves swapped, and both skip their mute call while `PrefsInitInProgress` (`004d2138`) is up — which is exactly the span of `Prefs_Init`, so reading the file cannot drive the mixer before the sound system exists.

**Three of the five are the only route their setting has.** The handler is the only writer of `Sound_SpeechEnabled`, and the music and effects handlers are the only things that mute and unmute, so without the apply step those three settings do nothing at all. The other two have a second writer: `Terrain_LoadZone` sets `TerrainTexturingEnabled` from option 8 as it loads, and `Input_SetThrottleLeverMode` is called from the input layer as well ([`mech-locomotion.md`](mech-locomotion.md)).

### Writing it back — `Prefs_SaveSelectedOptions` (`00459b78`)

**Every save is a read-modify-write of named options, never a dump of the array.** The function re-reads the current 54 bytes off disk into a local buffer, copies just the options its `(count, short *indices)` argument names out of `SimOptions` over that buffer, and writes the buffer back. An option it does not name keeps whatever is on disk rather than whatever is in memory.

`Prefs_SaveAllOptions` (`0045981c`) is the dump — open the path, write all `0x36` bytes, close — and nothing calls it. It has no references of any kind.

Four callers, and between them they are every write the simulator makes:

| Caller | Saves | When |
|---|---|---|
| `PreferencesPanel_Save` (`004574cc`) | 9: options 0-3 and 7-11 (`DAT_0049e304`) | `PreferencesPanel_Run` closing the panel |
| `ControlsPanel_Save` (`00459140`) | 13: `ControlsOptionBase - 1` through `+11` | `ControlsPanel_Run` closing the panel, at `00458c07` |
| `Joystick_InitAndSeedBindings` (`00459dd4`) | 25: options 12-36 (`DAT_0049e9d0`) — both blocks | A start that enumerates a stick while option 12 (`DAT_004d1fc8`) is 0 ([`joystick-input.md`](joystick-input.md#a-stick-that-does-not-enumerate)) |
| `Prefs_SaveOption` (`00459b64`) | 1 | `Sim_Run` (`0045f144`) at `0045f413`, on option 6, at shutdown and only when the live full-screen state differs from the option's byte |

**There is no cancel.** `PreferencesPanel_Revert` (`004574e0`) tests the same nine options with `Prefs_OptionChanged` (`00459c38`) and rolls the changed ones back out of the load-time shadow at `004d1ff2` through `Prefs_RevertSelectedOptions` (`00459b04`) — and it is unreferenced, as `Prefs_SaveAllOptions` is. Leaving the preferences panel saves, whichever button does it.

The controls panel pairs its save with `Prefs_CommitOptions` (`00459878`) one instruction later, which walks all 54 options, calls the handler of each one that differs from the load-time shadow, and re-baselines both shadows. Both panels pass apply on every write they make, so a click has already run its option's handler, and the commit runs a changed option's handler a second time.

**The controls panel's index list is latched.** `ControlsPanel_Save` builds it from `ControlsOptionBase` the first time it runs and sets `DAT_0049e7fc`, so the list keeps whatever base that was. Within one run of the simulator the player's machine is fixed by the mission load, so the latch has nothing to go stale against. It also means the `- 1` entry is option 12, the joystick-configured flag, only for a walker; flying a RAZOR it is option 24, the walker's last button binding, which is rewritten with its own unchanged value.

### What each byte is

| Option | Row | Values |
|---|---|---|
| 0 | MUSIC | off / on |
| 1 | SOUNDS | off / on |
| 2 | PILOT MESSAGE | 0 text only, 1 voice only, 2 both; what each gates is [`cockpit-messages.md`](cockpit-messages.md#the-port)'s |
| 3 | COMPUTER MESSAGE | as option 2 |
| 4 | **VSHELL's** `Game Resolution` | 0 `High Res (640x480)`, 1 `Low Res (320x240)` — and the byte `VideoMode_Configure` reads, [below](#the-video-mode-and-full-screen-bytes) |
| 5 | **VSHELL's** shell music track | non-zero `hmi\shell1.wav`, zero `hmi\shell2.wav`; the shell flips it at every startup, so the two alternate ([`../shell/movies-and-sound.md`](../shell/movies-and-sound.md#sound)) |
| 6 | `Display Mode` | 0 `Window`, 1 `Full Screen`. Both programs read it and the simulator writes it back |
| 7 | TERRAIN DISTANCE | 0-2, the draw radius ([`../rendering/terrain-texturing.md`](../rendering/terrain-texturing.md#the-terrain-detail-setting)) |
| 8 | TERRAIN TEXTURE | off / on |
| 9 | HERC DETAIL | 0-4, the LOD-root bias ([`../rendering/mech-shape-drawing.md`](../rendering/mech-shape-drawing.md#the-three-tunables)) |
| 10 | STRUCTURE DETAIL | 0-2, the `TSDetailPart` bias structures and flyers are drawn under ([`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#tsdetailpart-level-selection-and-structure-detail)) |
| 11 | EFFECTS DETAIL | 0-2, `Sound_DetailSetting`: which smoke stages a collapsing structure plays and whether a debris piece bursts ([`destruction-effects.md`](destruction-effects.md#effects-detail)), and the sound throttle's divisor ([`audio.md`](audio.md#the-play-request-gate)) |
| 12 | the joystick-configured flag | 0 lets `Joystick_InitAndSeedBindings` seed both blocks; 1 has it zero the walking block in memory when no stick enumerates |
| 13-24 | the controls panel's twelve, walking a HERC | [below](#the-bindings-are-twelve-bytes-of-the-same-file) |
| 25-36 | the same twelve, flying the RAZOR | |
| 37-41 | **VSHELL's**, not the simulator's: the practice missions screen's five parameters, difficulty among them | [`../shell/main-menu.md`](../shell/main-menu.md#the-parameters) |
| 42 | **VSHELL's** campaign-or-training flag | seeds `CampaignModeFlag`, so the mode survives a restart |
| 43 | **VSHELL's** language | 0 English, 1 French, 2 German: the startup copies it into the shell's language value before `-f` and `-g` are parsed ([`../command-line.md`](../command-line.md#vshell)). 0 in v1.10's shipped file; what the value selects is [`../retail-builds.md`](../retail-builds.md#how-a-language-is-chosen)'s |
| 44 | **VSHELL's** `Repair Options:` | 0 `AutoRepair All Hercs`, 1 `Manually Repair My Herc`, 2 `Manually Repair All Hercs` |
| 45 | **VSHELL's** `Weapons Building:` | 0 `AutoBuild Weapons`, 1 `Manually Build Weapons` |
| 46 | **VSHELL's** `INSTANT ACTION` demo | which of the three demo missions the next `INSTANT ACTION` plays, stepped modulo 3 after each; its chassis goes into option 40 ([`../shell/main-menu.md`](../shell/main-menu.md#which-mission-a-row-is)) |
| 47 | **VSHELL's** `Sierra.ini` gate | non-zero skips reading that file at startup ([`../shell/startup.md`](../shell/startup.md#sierraini)); in v1.10, 1 shows the `Performance Note` and 2 has shown it ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)) |

`ControlsOptionBase` (`004d25fb`) selects between the last two blocks: `Sim_InitMissionSession` (`004614fc`) sets it to `0x19` when `PilotingRazor` (`004d25f5`) is set and `0x0d` otherwise, and `Main_StaticInit` (`0045cad8`) starts it on `0x0d`. **The two blocks are independent** — a binding made in a walker does not disturb the RAZOR's.

No instruction in either image addresses options 48-53 by name, and they are zero in a retail file; only the loops that walk the whole array touch them.

**The file is shared with VSHELL**, which keeps the same 54-byte array (`DAT_004824b8`), the same load-time shadow (`004824ee`) and the same handler table (`00482524`), and reads and writes the same path: `ShellOptions_Load` (`0040d6a3`) and `ShellOptions_SaveAll` (`0040d752`), with the same step, step-back and commit trio (`ShellOptions_StepOption`, `ShellOptions_StepOptionBack`, `ShellOptions_Commit`). Options 4, 5 and 37-47 are its side of that sharing, and of those the simulator reads 4 and 6 ([Open](#open)), both straight from the file at startup and 6 again from the array at shutdown ([below](#the-video-mode-and-full-screen-bytes)). The shell's own screens that edit them are [`../shell/main-menu.md`](../shell/main-menu.md#the-preferences-screen) and [the practice missions screen](../shell/main-menu.md#the-parameters); options 0 and 1, the two sound bytes, are edited by both programs.

### The video-mode and full-screen bytes

`VideoMode_Configure` (`0045e4f4`) does not go through the option array at all: on its first call it `fread`s **seven bytes of `data\prefs.cfg`** into a local buffer and takes byte 4 as its mode and byte 6 as the full-screen flag. So the two settings reach the simulator before `Prefs_LoadOptions` has a say, and the file's own layout is what makes that work.

Byte 4 is reduced to two cases — **1 gives the 320x240 block and anything else the 640x480 block with hi-res banks**, which is the mode a retail file's 0 selects. What the modes are, why the file never reaches the middle one and how `-v` overrides it are [`cockpit-views.md`](cockpit-views.md#video-modes)'s.

Byte 6 non-zero makes `MainWindow_Create` (`00465054`) size the window to the desktop and place it topmost, and `WinMain` then clears the flag and calls the toggle at `004666c4`, which takes DirectDraw exclusive and sets an 8-bit display mode. `-Z1` and `-Z0` override the flag and not the option array. At shutdown `Sim_Run` compares option 6's byte with the live flag, which `Video_ToggleFullscreen` keeps at 0 or 1, and when they differ sets the option to the flag through `Prefs_SetOption` and saves it alone. So the player's own toggles during a session persist, and so does a `-Z` switch whose state the session ends in.

## The preferences panel — `prf_alrt` (`004566c4`)

Nine settings and two plain buttons, as a strip along the bottom of the screen with the [circling outside view](#preferences-and-controls-dbsimexe) above it. It is the one member of the family that **does not centre**: its constructor calls `AlertPanel_SetRect` with an origin outright where the other three go through `AlertPanel_CenterRect`.

### Its text

`str\PRF_ALRT.STR`, five groups read in file order:

| Group | Holds |
|---|---|
| 0 | `PREFERENCES` |
| 1 | the eleven button captions, in widget order |
| 2 | `OFF`, `ON` |
| 3 | `TEXT ONLY`, `VOICE ONLY`, `TEXT / VOICE` |
| 4 | `LOW`, `MED LOW`, `MED HIGH`, `HIGH`, `MAXIMUM` |

`hba\PRF_ALRT.HBA` frame 0 is the 630x170 plate, with the group boxes and a black well behind every readout painted into it. Frames 1-4 are the 133x22 button in four border colours; all eleven buttons use them.

### Geometry

The [family's layout](alert-panels.md#what-the-family-shares) applies.

| Global | Device | Is |
|---|---|---|
| `004d1f64`/`66` | 638 x 178 | the declared size. The plate is 630 wide, so eight columns at the right end are empty |
| `004d1f68`/`6a` | (0, 300) | the screen origin, given rather than derived |
| `004d1f6e`/`70` | y 0, height 16 | the title bar |
| `004d1f7a`/`7c` | (4, 0) | the readouts' label margin |
| `004d1f6c`, `72` | 0 | title margins |

Nine parallel `short[]` tables carry the rects — `0049e224` onwards for the eleven buttons' x, y, width, height and first plate frame, `0049e292` onwards for the nine readouts' x, y, width and height. Every width and height entry repeats one value: buttons are 132x18 (art 133x22, blitted at the rect origin so it overhangs) and readouts 126x14. The layout is two columns of five and four rows at x 18 and 312, the readouts three pixels past their button, and CONTROLS and DONE at (312, 142) and (450, 142).

### What a row reads — `PreferencesPanel_RefreshRow` (`004571f4`)

Nine cases, one per row, each setting one readout's label. Rows 0-3 and 5 index their string group with the option byte directly; the four detail rows index group 4 through a five-entry `short[]` map of their own:

| Row | Option | Group | Map |
|---|---|---|---|
| MUSIC, SOUNDS | 0, 1 | 2 | direct |
| PILOT MESSAGE, COMPUTER MESSAGE | 2, 3 | 3 | direct |
| TERRAIN DISTANCE | 7 | 4 | `0049e2da` = {0, 2, 4, 0, 0} |
| TERRAIN TEXTURE | 8 | 2 | direct |
| HERC DETAIL | 9 | 4 | `0049e2e4` = {0, 1, 2, 3, 4} |
| STRUCTURE DETAIL | 10 | 4 | `0049e2ee` = {0, 2, 4, 0, 0} |
| EFFECTS DETAIL | 11 | 4 | `0049e2f8` = {0, 2, 4, 0, 0} |

The three `{0, 2, 4, 0, 0}` maps are why a three-setting row reads `LOW` / `MED HIGH` / `MAXIMUM` and skips the two intermediate words. **All four maps are five entries wide** — they sit 10 bytes apart — so a map is not itself evidence of how many settings its row has; what bounds a row is the modulus its click case passes.

### What a click does — `PreferencesPanel_Run` (`00456d4c`)

The click handler (`PreferencesPanel_OnClick`, `004573a8`) records the widget index, moves the focus to the clicked widget and, for the nine option rows, moves the highlight: the row that had it goes back to widget state 3 and the clicked one to state 0. The loop then acts on the index, and **three different rules are in play**:

| Rows | Rule |
|---|---|
| MUSIC, SOUNDS, TERRAIN TEXTURE | toggle |
| TERRAIN DISTANCE, HERC DETAIL | step forward on a left click, back on a right one |
| STRUCTURE DETAIL, EFFECTS DETAIL | step **forward on either button** — see [Rejected readings](#rejected-readings) |
| PILOT MESSAGE, COMPUTER MESSAGE | step forward only, and only while `VoiceArchivePresent` is set |

`VoiceArchivePresent` (`0049e9cd`) is set once by `Voice_ArchiveExists` (`00459d6c`), which builds the localised `simvoice` name and simply tries to `fopen` it. When it is clear, `Prefs_Init` forces options 2 and 3 to 0 and neither row can be clicked off it, so the player cannot ask for voice the install does not carry.

**With no sound device the first four rows are greyed** — the loop puts widgets 0-3 into state 2 before it starts, on `SfxManager` being null. `Widget_HitTestChildren` skips a state-2 widget, so they are not merely inert but invisible to the hit test.

CONTROLS builds the controls panel and runs it inline, then repaints this panel and focuses DONE; DONE calls `PreferencesPanel_Save` (`004574cc`) and closes.

**[Return] presses the focused widget** ([`alert-panels.md`](alert-panels.md#keys-and-the-press-flash)), as a left click: MUSIC on a freshly raised panel, whose loop focuses widget 0, then whatever was clicked last, and DONE once the controls panel has been up. With no sound device MUSIC is greyed, and [Return] presses nothing until a click moves the focus. [Esc] presses DONE.

## The controls panel — `ctl_alrt` (`00457d1c`)

Four joystick axis assignments and one action per joystick button, with the action list of the selected button beside them. 370x372, centred, over whatever is already on screen.

### It is two panels in one file

`str\CTL_ALRT.STR` carries thirteen groups and the constructor reads eight — title, the fourteen captions, the twenty-one action names, the OPTIONS caption, then a three-word set per axis row. When `PilotingRazor` (`004d25f5`) is set it reads **five more** and overwrites the title and all four axis sets with them:

| Group | Walking | Flying |
|---|---|---|
| title | 0 `HERC CONTROLS` | 8 `RAZOR CONTROLS` |
| JOYSTICK | 4: `NO JOYSTICK`, `MOVEMENT`, `TURRET` | 9: `NO JOYSTICK`, `PITCH / ROLL`, `THROTTLE / YAW` |
| THROTTLE | 5: `NO THROTTLE`, `THROTTLE`, `TURRET ELEVATION` | 10: `NO THROTTLE`, `PITCH`, `THROTTLE` |
| RUDDER | 6: `NO RUDDER`, `DIRECTION`, `TURRET ROTATION` | 11: `NO RUDDER`, `ROLL`, `YAW` |
| HAT | 7: `NO HAT`, `VIEWS`, `TURRET` | 12: `NO HAT`, `VIEWS`, `THROTTLE / YAW` |

Group 1 is the fourteen captions (JOYSTICK, THROTTLE, RUDDER, HAT, BUTTON 1-8, RECOMMEND, DONE), group 2 the twenty-one action names indexed by action code, and group 3 the `OPTIONS` caption.

`DAT_004d25f5` is written in one place: `DBSim_LoadScriptDat` sets it from `playerMechType == 8`, the RAZOR.

### Geometry

Same shape as the preferences panel's block, at `004d1fa8`: 370 x 372, title bar y 0 height 16, label margin (4, 0). `AlertPanel_CenterRect` puts it at (135, 54) on the 640x480 screen. The constructor takes an optional screen origin instead, and every reachable call site passes none.

Ten parallel `short[]` tables: `0049e510` onwards for the fourteen buttons' x, y, width, height, first plate frame and caption margin, `0049e5b8` onwards for the twelve readouts' x, y, width and height. `0049e618` is the write-once flag guarding the `.bss` block.

**Three button sizes, each with its own four-frame set** in `hba\CTL_ALRT.HBA` (frame 0 is the 372x374 plate): the four axis rows are 136x18 on frames 5-8, the eight button rows 68x18 on frames 1-4, and RECOMMEND and DONE 90x18 on frames 9-12. A button draws `base + state`, and **the three sets do not order their four colours alike** — 1-4 and 5-8 run rest, held, disabled, option, while 9-12 run option, rest, held, disabled. That is a property of the art; the paint indexes all three the same way.

The OPTIONS list sits at x 226-354: its caption at y 138 height 10, then twelve rows at pitch 14 starting a **whole pitch below** the caption, the constructor advancing its running rect before each `Label_SetRect` rather than after. The caption is the one label on the panel given no background colour, which is why its rect can overlap the border the plate paints across that band without erasing it.

### The capability block

What the panel greys its rows against is `Input_QueryCapabilities`' eight bytes, whose fields the input layer owns — [`joystick-input.md`](joystick-input.md#the-capability-block--input_querycapabilities-004777f8).

The panel reaches it in two steps. `Input_GetDevice(3)` (`0045c508`) is asked first, and when it answers null or with its low bit clear the panel takes **no block at all** and greys all twelve rows at once. A stick that fails to enumerate leaves that lookup pointing at a destroyed device, so what the panel reads then is freed memory ([`joystick-input.md`](joystick-input.md#a-stick-that-does-not-enumerate)). Only past that gate does it read the fields and grey rows one at a time: `+2` bounds the button rows, `+4`, `+5` and `+6` gate THROTTLE, RUDDER and HAT. The JOYSTICK row has no field of its own: once a stick is present it is always live.

`ControlsPanel_RefreshRow` (`00458d20`) gates every one of its twelve cases on the same capability and **sets no text at all** when it fails, so a greyed row reads blank rather than showing a stale binding.

### The bindings are twelve bytes of the same file

At `ControlsOptionBase` + 0..11. An axis row's byte indexes its three-word set directly; a button row's byte is an action code into the twenty-one names, also directly. What the input layer then does with them — which game axes a row's 0, 1 and 2 select, and what each action code dispatches — is [`joystick-input.md`](joystick-input.md#applying-the-bindings--input_buildplayerdevice-0045a7f4).

Which actions a button row may be **bound to** is a separate table, read by `ControlsPanel_ActionAt` (`00457cdc`): eight rows of thirteen bytes at `0049e619` walking and `0049e681` flying. **Code 0 terminates a row rather than meaning OFF** — the constructor counts a row's length by walking until it reads a zero — so although `OFF` is action name 0 it is never an offered choice, only what a row displays when its stored byte is 0. BUTTON 1 is the extreme case: one entry, `FIRE`, so the trigger cannot be rebound. The other seven rows carry twelve actions each and **alternate between two lists**, one holding `NEXT WEAPON` and the other `PREV WEAPON`.

### What a click does — `ControlsPanel_Run` (`00458650`)

The click handler (`ControlsPanel_OnClick`, `00458ebc`) moves the highlight the same way the preferences panel's does, but not the focus, then decides between selecting and acting: a button row is *selected* only when it is not already the selected one, and otherwise its action is queued. **So a button row takes two clicks to change** — the first points the OPTIONS list at it, the second and later ones step it. An axis row is never selected, so every click on one steps it and clears the list selection. DONE's click sets the close flag itself.

**The loop acts only with a capability block** ([below](#the-capability-block)): its switch is skipped when there is none. Every row is greyed then, so of the fourteen widgets only RECOMMEND and DONE take a click, and neither does anything but DONE's close: RECOMMEND writes nothing, and DONE takes the panel down without `ControlsPanel_Save` and `Prefs_CommitOptions`.

| Widget | Action |
|---|---|
| 0-3 | step that axis's option over its three words, forward on a left click and back on a right one; clear the list selection |
| 4-11 | select, or step that button row |
| 12 | RECOMMEND |
| 13 | DONE — closes the panel |

### What a joystick button does — `ControlsPanel_HandleEvent` (`00458f9c`)

The panel installs a handler of its own in vtable slot `+0x10` where the rest of the family uses `AlertPanel_HandleEvent`, because on this panel a stick's buttons must not press widgets — they have to show the player which row they just pressed. The loop hands it the device block `Input_BuildPlayerDevice` returns, and it does three things with it.

**The trigger is put back.** Byte `+0x0d` is copied over button state 0 before anything reads them, undoing the extraction `Input_BuildPlayerDevice` performs ([`joystick-input.md`](joystick-input.md#the-buttons)). Without it BUTTON 1's row would be the one row a stick could not reach.

**Then the eight states, first pressed one wins.** A press on the row that is already selected steps that row's action, exactly as a second click does; a press on another row, one under the capability block's button count, selects it and moves the highlight to widget `row + 4`. Either way the row's widget gets the [press flash](alert-panels.md#keys-and-the-press-flash) on the panel's root, though no click reaches it. Then `Input_LatchButton` latches the button, whichever branch ran ([`joystick-input.md`](joystick-input.md#the-buttons)), so one press is one step.

**Then the keys**, which are the [family's](alert-panels.md#keys-and-the-press-flash) convention and not a departure from it: [Return] presses the widget at `+0x2f7`, or focuses widget 0 when that is unset; [Esc] presses the cancel widget at `+0x2fb`, DONE; [Tab], scancode `0x52` and [Shift+Tab] focus the next widget ([`alert-panels.md`](alert-panels.md#keys-and-the-press-flash) says why [Shift+Tab] does not go back). Only that focus walk moves `+0x2f7` on this panel, so [Return] presses the JOYSTICK row, widget 0, which the loop focuses before its first pass, and steps it.

A button row steps by **slot index within its own list** (`panel+0x462`, wrapping at that row's length in `panel+0x46a`), not by action code: `ControlsPanel_StepButton` (`00459320`) forward and `ControlsPanel_StepButtonBack` (`0045938c`) back both advance the slot and then write out whatever code that slot holds. The slot is seeded at construction by searching the row's list for the stored code, and left at 0 when it is not there — so a row showing a code its own list does not offer starts stepping from the top rather than from what it shows.

`ControlsPanel_FillOptionList` (`004593f8`) refills the twelve OPTIONS labels from the selected row's list, writing an empty string for every row when nothing is selected and past the end of the list otherwise. A freshly raised panel has none selected, so the box starts blank.

### RECOMMEND

`Input_RecommendedBindings` (`0045a258`) returns a twelve-byte block — four axis modes then eight action codes — and case 13 pushes every byte through `Prefs_SetOption`:

| | Axes | Buttons |
|---|---|---|
| Walking | 1, 0, 2, 2 | `FIRE`, `TARGET`, `CENTER TURRET`, `ATT TOGGLE`, `NEXT CHAIN`, `NEXT WEAPON`, `SHIELDS FRONT`, `SHIELDS REAR` |
| Flying | 1, 0, 2, 2 | `FIRE`, `TARGET`, `LINK WEAPON`, `MFD DISPLAYS`, `NEXT CHAIN`, `NEXT WEAPON`, `SHIELDS FRONT`, `SHIELDS REAR` |

The eight button bytes take a detour: the code searches the row's list for the recommended code, stores the slot it found — **0 when it is not there** — and writes back whatever code *that slot* holds. See [Rejected readings](#rejected-readings) for what that costs.

That function also carries an arm that zeroes the block, taken when the capability block's `+0` is 0. `Input_QueryCapabilities` writes 1 or 2 into that field every time it rebuilds the block, so the arm is not reached through a block it returns. Without a block case 13 is not reached either: RECOMMEND stays clickable, but the loop's switch needs the block ([above](#what-a-click-does--controlspanel_run-00458650)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `004d1fbc` is a four-byte array of message-channel modes | It is the base of the whole 54-byte option array. The two message modes are entries 2 and 3 of it |
| Every stepping row can be stepped both ways | STRUCTURE DETAIL and EFFECTS DETAIL cannot. Their cases test the right-button flag and then call the **forward** step down both arms, where the other two stepping rows call the backward one |
| RECOMMEND leaves every row on the recommended action | It writes the code held by the slot it *found*, and a code the row does not offer resolves to slot 0. Walking, `NEXT WEAPON` is recommended for BUTTON 6 and is not in that row's list, so retail's own RECOMMEND binds it to `LINK WEAPON` |
| A binding the readout shows is a binding its option list can reach | Rows alternate between a `NEXT WEAPON` list and a `PREV WEAPON` one, and nothing rejects a write of the other. A retail install can sit on a binding its own list cannot step to |
| `0049e9cd` is a dead constant because it is zero in the image | `Voice_ArchiveExists` writes it at startup from whether the localised `simvoice` archive opens |

## Open

- **Deferred:** no DBSIM reference to options 37-41 (`004d1fe1`-`004d1fe5`) or `004d1fe6`-`004d1fe9` found by `es2_xref.py` (control: `004d1fc2` has one), so no simulator reader of them is known beyond 4 and 6.
