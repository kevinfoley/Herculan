# The shell's startup and main loop

How `VSHELL.EXE` comes up, the loop it then runs until it ends, and how it leaves. All addresses are VSHELL's. What the screens do inside the loop is [`screen-layout.md`](screen-layout.md); how a session's `-X` code joins the shell to the simulator is [`campaign-loop.md`](campaign-loop.md).

## Shell_WinMain (00406507)

The process entry splits the command line into an `argv` whose first word is `dbsim` (`Shell_SplitCommandLine`), registers the window class for a first instance, clears `0046d740` for `-d` (which [`ShellOption_DisplayMode`](../command-line.md#vshell) then overwrites), creates the main window (`Shell_CreateMainWindow`, `Unable to initialize Instance` on failure), reads the joysticks, goes full screen when `Shell_StartedFullScreen` (`0046d740`) is set, blanks the screen and calls `Shell_Main` with the split arguments. When `Shell_Main` returns it pumps messages until `WM_QUIT` arrives, releases DirectDraw (`Display_ReleaseDirectDraw`, `00407011`) and returns the exit code at `0046e210` (`Shell_GetExitCode`).

## The startup — Shell_Main (00401525)

`Shell_Main` (`vshell.cpp`) brings the shell up in this order, pumping messages (`Shell_PumpMessages`) between most steps. v1.10's mounts the archives before parsing the command line in step 3, takes its messages from `ERROR.STR` and has no step 7 ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)).

1. **The memory pool.** `Shell_PoolSize` (`0046c090`) bytes, 4,000,000 in the image, from `Mem_NewArray`, asserting at line `0x12d` when that fails, become the arena of `g_ShellPool` (`Memory_Init`, [`../runtime-library.md`](../runtime-library.md#the-memory-pool)).
2. **`data\drive.cfg`** (`DriveCfg_Read`, [`../retail-builds.md`](../retail-builds.md)), the mono debug monitor cleared (`maybe_Mono_Clear`), and the byte `004810e0` set ([Open](#open)).
3. **The switches.** `EsGlobal_Init(0)` (`004073bc`, `esglobal.cpp`) sets the switch block to its defaults — mouse switch off, keyboard, sound and movies on — copies `prefs.cfg` options 0, 1, 2 and 43 into it, prints the first three to the mono monitor (`Shell_MonoPrintAudioOptions`, `004076ff`) and zeroes the exit code. `Shell_ParseCommandLine` (`0040107c`) then parses the arguments ([`../command-line.md`](../command-line.md#vshell)).
4. **The startup code.** `Shell_StartupCode` (`0048227e`) takes the exit code `-X` left, and the exit code is zeroed ([`campaign-loop.md`](campaign-loop.md)). For 3 and 4 `Shell_PresentEnabled` is set at once ([Presenting](#presenting)).
5. **The colour depth**, for codes 0 and 1 only: when the main window's DC reports other than 8 bits per pixel, the shell leaves full screen, shows `You are not in 256 color mode` — *We recommend you change your desk top properties to 256 color mode for best performance.* — and goes back to full screen if it was there. It carries on either way.
6. **`-eggplant`.** Without it (`Shell_EggplantGiven`, `0046c084`) the shell [refuses](#the-refusals) with *You cannot run this exe directly. To play EarthSiege II, run ES.*
7. **[`Sierra.ini`](#sierraini)**, unless `prefs.cfg` option 47 is set.
8. **The archives.** `VolumeGroup_SetBufferSize(0x200)`, then the volume scan, `VolRStream_SetGroup(0x100, ".vol", DriveCfg_Directory, 0)` ([`../formats/vol-archive.md`](../formats/vol-archive.md#which-archives-are-mounted)).
9. **The disc.** `avi\pt1.avi` opened under the `drive.cfg` directory (`Path_UnderDriveCfg`); failing, the shell [refuses](#the-refusals) with *Please insert ESII CD and restart.* The stream stays open until the shell ends.
10. **The shared state.** `Devices_Init` (`devices.cpp`'s viewport), `EsGlobal_Init(1)` — palette 1, the fonts, the backdrop, `estext.bin` and the shell generator's seeding ([`screen-layout.md`](screen-layout.md#what-the-whole-front-end-shares), [`campaign-loop.md`](campaign-loop.md#the-shells-generator)) — `Shell_InitGameState`, `LoadWeaponsDat`, `LoadCareerDat`, `ShellMap_EnsureResourcesLoaded`, and `WinEvents_Init` (`00468168`): the six window-event classes registered with the class-item registry, the event queue (`g_EventQueue`, `005ddbd0`) and the twenty-alarm WinTimer (`g_WinTimer`, `005ddbd4`) built.
11. **The display.** A `WinWin95Display` (`Shell_Display`, `004810f0`) painting on the display's root window; the two cursor objects of [the pointer](widgets.md#the-pointer), the first given to the root and installed; and the root's handler set to `RootWindow_DiscardEvent` (`00401d77`), which deletes the event, with `0x60` added to its event mask. An event that [climbs](widgets.md#which-widget-a-click-reaches) to the root is dropped there.
12. **The input.** A `WinConsumer` and a `WinMouseProducer`, and a `WinKeyboardProducer` only while `Shell_KeyboardEnabled` (`00482271`) is set, which `-k` clears. All three belong to `Shell_Dispatcher` (`004810f4`), the static dispatcher the loop drains.
13. **The top-level window**, `Shell_TopWindow` (`004810e4`): an `ESWindow` over the root's rect, with no handler. The frame's root and the palette scopes are built inside it ([`screen-layout.md`](screen-layout.md#the-widget-tree-of-a-tab-screen)).
14. **The sound** (`ShellSound_Init`, [`movies-and-sound.md`](movies-and-sound.md#sound)). It fails only when the sound manager's allocation does, and the shell then [refuses](#the-refusals) with *Could not create Sound Manager.*
15. **The screens.** `Shell_BuildScreensAndStart` (`004012b0`) builds every screen and starts the intro or the debrief by the startup code ([`campaign-loop.md`](campaign-loop.md)); then the top-level window is shown, `Shell_CloseAllowed` is set and the main loop starts.

### Sierra.ini

While `prefs.cfg` option 47 (`ShellOption_SkipSierraIni`) is clear, the startup reads `VideoSpeed` from the `[Config]` section of `Sierra.ini`, a name with no path, with an empty default. A value below 1000 — a missing key reads as 0 — sets `Shell_PerformanceNotePending` (`0046c088`), which puts up [the `Performance Note`](main-menu.md#the-main-menu) as the main menu comes up. Either way the startup then sets option 47, commits and saves all 54 options, so the read happens on one run only. The installers read the same value to choose the resolution ([`../retail-builds.md`](../retail-builds.md#the-installer)). v1.10's sets option 47 to 1 when it chooses low resolution, and v1.10's shell, which does not read `Sierra.ini`, shows the note on that 1 ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)); its shipped `DATA\PREFS.CFG` has the option clear ([Open](#open)).

### The refusals

The three refusals — no `-eggplant`, no disc, no sound manager — leave full screen, show their message in a box titled `Error`, run `Shell_EmptyExitStep` and `SfxTimer_Kill`, post `WM_QUIT` and return. The main loop never runs, so nothing is saved, and the shell exits with 0.

## The main loop

Each pass of the loop:

1. pumps messages, and ends the loop on `WM_QUIT` or `Shell_QuitFlag` (`0046c074`);
2. **without the focus, does nothing else.** `Shell_HasFocus` (`0046c094`, 1 in the image) is cleared by `WM_KILLFOCUS`, which also stops the sound, and set by `WM_SETFOCUS` ([`movies-and-sound.md`](movies-and-sound.md#what-plays-each-sound)). The skipped passes are counted on the mono monitor (`hmm: %d`, `Shell_UnfocusedPassCount`);
3. drains `Shell_Dispatcher`'s queue, which carries the producers' input (`Dispatcher_RunQueue`, all masks);
4. pumps the events: the WinTimer's alarms, then the event queue (`Shell_PumpEvents`, [`widgets.md`](widgets.md#which-widget-a-click-reaches));
5. commits the palette (`Shell_CommitPalette`);
6. flushes the display's dirty list, which is where the pass's repaints reach the screen (`DirtyList_Flush`);
7. plays [the movie queue](movies-and-sound.md#the-shells-movies) (`Movie_PlayQueue(1)`) and sets `Shell_MainLoopStarted` (`0046c08c`);
8. runs [the briefing map's intro](mission-map.md#the-intro) if one is due (`ShellMap_RunIntro`);
9. reports `maybe_Assert_BreakRequested` (`0048dc2c`) as a severity-6 assert, which exits, when it is non-zero ([Open](#open));
10. stamps `Input_EventTime` with `GetTickCount`, prints it and the frame rate to the mono monitor;
11. while [Alt+F4's `QUIT` alert](main-menu.md#quit) is shown, repaints it every 51st pass (`QuitAlert_RepaintCounter`).

`Shell_QuitFlag` is tested again after each of steps 3 to 8, so a handler that sets it ends the loop before the rest of the pass.

**No tab acts before the first pass has reached step 7.** All nine tab-strip handlers return at once while `Shell_MainLoopStarted` is clear, before even the [already-on-this-tab check](screen-layout.md#what-a-tab-click-does), and its only store is step 7's.

**Without the focus the shell stands still**, timers and all, since the WinTimer's alarms are ticked only in step 4. Two other places wait on the same flag: `Shell_RunFrames` (`00401cf4`), the loop's steps 3 to 6 run up to 24 times for code that blocks, does nothing on a pass without it; and the movie queue, once each movie has closed, pumps messages until the focus is back before it goes on. A movie playing keeps playing.

### Presenting

`Shell_PresentEnabled` (`0046c080`, 0 in the image) gates the shell's own drawing on the window: while it is clear, `Display_RealizePalette` does nothing and `WM_PAINT` does not present the shell's bitmap (`Display_PresentWhole`, `004068d6`), so a movie's window and palette are left alone. `Shell_BuildScreensAndStart` sets it after the intro movies, or without them for codes 3, 4 and 6, and step 4 above sets it for 3 and 4 before anything else; `CREDITS` clears it round its movie.

## Leaving the main loop

The exit is the same for every way out — `QUIT`, the launches, Alt+F4's `ACCEPT` and `WM_CLOSE`, which sets `Shell_QuitFlag` once `Shell_CloseAllowed` is set ([`main-menu.md`](main-menu.md#quit)):

```
Game_SaveSlot(10, NULL)          // 0040e37b: the current-game autosave; nothing without a game in progress, slot 11 in training
...                              // the producers and the top-level window deleted, ShellMap_ReleaseResources
Shell_ShutdownDevicesAndSound()  // 004092dc: Devices_Shutdown, ShellSound_Shutdown
...                              // the pool's buffer freed, Shell_EmptyExitStep, SfxTimer_Kill
PostQuitMessage(0)
```

`Shell_WinMain` then returns `0046e210` as the shell's exit code. The startup zeroed that store right after copying the `-X` code out of it, and `QUIT` leaves it alone, so `QUIT` exits with 0 and `ES.EXE` ends ([`../command-line.md`](../command-line.md#exit-codes)); a launch sets 2 or 5 first.

## Open

- **Deferred:** what reads `004810e0`, the byte step 2 sets. `es2_xref.py` finds that store and no other reference to `004810d0`-`004810e3`.
- **Deferred:** what reads `00482397`, the byte `EsGlobal_Init(1)` sets after seeding the shell generator, whose state ends at `00482396`. `es2_xref.py` finds that store only.
- **Deferred:** what writes `maybe_Assert_BreakRequested`. `es2_xref.py` finds the main loop's two reads and an uncalled getter (`maybe_Assert_GetBreakRequested`, `0044df8c`); the name comes from severity 6's text, `Assert: break (Ctrl-C / Ctrl-Break)`.
- **Open:** what v1.0 ships in `prefs.cfg` option 47, which decides whether the `Sierra.ini` read runs on a first start. v1.10's file has it 0; `ES2/DATA/PREFS.CFG` (1) has been written by play.
- **Deferred:** the startup's checks and refusals: the colour depth, `-eggplant`, the disc and the sound manager.
