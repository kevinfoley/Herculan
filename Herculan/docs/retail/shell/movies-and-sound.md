# Shell movies and sound

The shell's movie queue and player, how input is gated around them, and the shell's own sound manager: its samples, the music, and what plays each sound.

## The shell's movies

`avi.cpp` plays the shell's movies through MCI's `avivideo` device. `Movie_Enqueue` (0041e29c) adds one to a ten-entry ring at `00485668` when movies are on (`Shell_MoviesEnabled`, `00482275`, which `-a` clears) and no entry in the ring already carries its id: the id, a rect, a palette index (`0xffff` for none), a flag that brings the location picture up after it, and a callback, which every caller passes as null. Nothing stops a write landing on an entry not yet played. `Movie_PlayQueue` (0041e368) plays the ring out. [The shell's main loop](startup.md#the-main-loop) calls it once a pass, after the widgets have had their events and before `ShellMap_RunIntro`; the startup (`Shell_BuildScreensAndStart`, `004012b0`), `Game_ProcessMissionResults` and `MainMenu_OnCredits` (`004315ec`) also call it straight after enqueuing, while `Mission_Show` and `maybe_Mission_UpdateLocationTab` enqueue and leave the playing to the main loop.

**The id indexes the table at `00470e74`** of 86 `avi\` paths, unchecked: `pt1`-`pt6`, `rc1`-`rc5`, `as1`-`as7`, `es1`-`es4`, `rs1`-`rs4`, `sc1`-`sc5`, `rd1`-`rd4`, `sp1`, `gd1`-`gd4`, `ex1`-`ex4`, `rf1`-`rf4`, `co1`-`co4`, `fl1`-`fl4`, `sk1`-`sk4`, `sv1`-`sv4` and `hc1`-`hc4` for ids 0 to `0x43`, then `intr_pt1`, `intr_pt2`, `c1`-`c5`, `alph_th`, `delt_th`, `omic_th`, `brav_th`, `luna`, `transm3`, `end1a`, `death`, `victory`, `credits` and `dropship` for `0x44` to `0x55`. `Path_UnderDriveCfg` (`0040d429`) puts the directory `data\drive.cfg` names in front. A French or German v1.10 shell reads the intro from `avf\` or `avg\` instead ([`../retail-builds.md`](../retail-builds.md#v110s-shell-reads-the-language-twice-more)). `ALPHA`, `BRAVO`, `DELTA`, `OMICRON`, `ESTAB2`, `ES2CREDC` and `ES2DROP3` sit in `AVI\` and are not in the table.

| Caller | Movie | Rect | Palette |
|---|---|---|---|
| the startup | `0x44`, `0x45`, the intro | full | none |
| `Mission_Show`, map view, once (`DAT_004778aa`) | `stage + 0x45`, `c1`-`c5` | Telecomm | 3, or 4 once `stage - 1 > 3` |
| | `stage + 0x4a`, the theater's thumbnail, `luna` at stage 5 | map panel | none; the location flag set |
| `Mission_Show`, briefing, once per load (`DAT_004778ab`) | `Career_BriefingMovie` | Telecomm | `stage + 4` |
| `Mission_Show`, debrief, once per load (`DAT_004778ac`) | `Career_DebriefMovie` | Telecomm | `stage + 9` |
| `maybe_Mission_UpdateLocationTab`, stage 5 | `0x55`, the lunar drop | full | none |
| `Game_ProcessMissionResults`, the campaign won | `0x53` and `0x54`, the ending and the credits | full | none |
| `MainMenu_OnCredits` | `0x54` | full | none |

The rects are left, top, width and height, which `Avi_MoveWindow` (`0041dfd6`) hands to `MoveWindow` for the movie's window: full is `{0x20, 0x3c, 0x240, 0x168}`, 576x360 centred on the canvas; Telecomm is `{0x15, 0x56, 0xef, 0xb3}`, over the Telecomm picture; the map panel is `{0x122, 0x43, 0x127, 0xe2}`. The briefing's id is the career block's last short ([`../formats/save-games.md`](../formats/save-games.md#career-block--152-bytes)), which `Career_SetBriefing` (`00412ece`) writes with the three text arrays when a mission is loaded. The debrief's, `004840ba`, is written only by `Career_SetDebriefLines` for the mission just flown, and the save does not carry it. `Game_LoadSlot` and `Game_NewCareer` clear the briefing's and the debrief's flags. Neither touches the map's, which [RESTORE](main-menu.md#leaving-the-save-screen) and the registration screen's [ACCEPT](main-menu.md#starting-a-campaign) clear.

**`Avi_Play` (0041e01c) plays one movie.** It opens the file as a child of the main window (`open %s alias mov style child parent %d`), moves the movie's window to the rect, sets its palette (`setvideo mov palette handle to %d`), drops [the hourglass](widgets.md#the-pointer), captures the mouse, sets `Avi_Playing` and plays with `notify`. It then polls the keyboard until `Avi_StopRequested` is set — by Esc or Space (scan codes 1 and `0x39`), by a mouse button going down in the window procedure, or by the `MM_MCINOTIFY` with `MCI_NOTIFY_SUCCESSFUL` that the movie's end posts — and closes the movie, releases the mouse and clears `Avi_Playing`. The device is opened for each movie by `Avi_OpenDevice` (`0041def7`) and closed after it. When `Movie_PlayQueue` was called with 1, the open first builds the palette handle (`Avi_PaletteHandle`, `00485664`) with `Avi_BuildPalette` (`0041de68`) from entries 10 to 245 of the palette installed then. The main loop and `Game_ProcessMissionResults` pass 1; the startup and `MainMenu_OnCredits` pass 0, and with 0 the handle is left as it is — still 0 for the intro, and for the credits from the main menu the last movie's, or 0 when none has played ([Open](#open)).

**`Movie_PlayQueue` plays the entries in turn**, fading the music out and stopping it before the first ([What plays each sound](#what-plays-each-sound)). For each entry:

1. An intro part (`0x44`, `0x45`) sets `MovieQueue_PlayingIntro` (`00470fe4`). The entry's palette is installed unless it is `0xffff`, the entry is an intro part or it is the credits.
2. Unless the entry is the lunar drop, while the frame's panel (`ShellPanelWidget`) is up every tab is unlit but MISSION, which is lit, and the mission screen's panels are shown again.
3. The hourglass goes up and the movie plays. An intro part is skipped once a mouse button, Esc or Space has gone down during an intro movie, which sets `MovieQueue_IntroSkipped` (`00470fe0`) ([Open](#open)). When the open fails, an intro part puts up `Please insert ESII CD and restart` and ends the shell, and any other movie puts up the insert-CD panel (`InsertCdPanel`, `0048d108`, built by `InsertCdPanel_Build`, `00431c18`) and clears `MovieQueue_Running` while it waits: `Continue` (`InsertCdPanel_OnContinue`, `00431e6a`) hides the panel and sets `InsertCdPanel_Continue`, and the movie is tried again; `Quit` (`InsertCdPanel_OnQuit`, `00431ed9`) sets `Shell_QuitFlag`, which ends the wait.
4. Full screen, the screen is blanked after an intro part or the credits.
5. Movie or none, the queue pumps messages until the shell has the focus (`Shell_HasFocus`, [`startup.md`](startup.md#the-main-loop)).
6. After the lunar drop, with the frame's panel up, palette 1 goes in through the scope and the top-level window (`Shell_TopWindow`) is repainted. After the credits palette 1 is installed.
7. With the location flag set, `Mission_Leave` takes the mission tab down, MISSION is unlit, `DAT_0047581c` is parked at `0xffff`, the music is started and faded in, and `maybe_Mission_UpdateLocationTab` (`0044409f`) runs. Below stage 5 it puts the location picture up — frame 0 of `dba\alph2`, `delt1`, `omic1` or `brav1`, indexed by `stage - 1` (`MissionLocationDbaTable`, `00477fcc`), 640x480 over the whole window — through the theater palette `stage + 0xe`. At stage 5 it queues the lunar drop in its place, installs palette 2 through the scope, and fades the music out and stops it. Below stage 5 nothing is drawn between the movie's end and the picture: `Window_HideRecursive` posts no paint, `ShellSound_FadeIn` pumps only window messages, and `WM_PAINT` re-presents the back buffer (`Display_PresentWhole`), so for the length of the fade the mission screen stays up as last drawn, in the palette the map movie installed. The `Shell_RunFrames` after the call paints the picture, commits the theater palette and flushes in one frame.
8. With the location picture up, the shell waits two seconds without pumping messages (`Shell_BusyWaitSeconds(2)`, `00401d53`), takes the picture down, repaints the frame's root and installs palette 1.
9. The entry's callback runs and the entry is freed.

Once the ring is empty the music is started and faded in, unless step 7 started it and no lunar drop has played since.

### Input while a movie plays

Two flags gate input around the movies, each set and cleared by one of the two players ([Open](#open)):

| Flag | Set | Cleared |
|---|---|---|
| `Avi_Playing` (`00470d70`) | by `Avi_Play` as playback starts, with the main window capturing the mouse | as playback ends |
| `MovieQueue_Running` (`00470e70`) | by `Movie_PlayQueue` when it finds the ring holding a movie | when a later call finds the ring empty, and while the insert-CD panel waits for one of its buttons |

While `Avi_Playing` is set, the window procedure (`MainWndProc`, 00404a2c) drops both button-ups and the options hotkeys, and a button-down sets `Avi_StopRequested` (`00470d60`), which ends playback, and is dropped too: **a click skips the movie and does nothing else**, as Esc and Space do. Moves are still posted. `WinButton_HandleEvent` and `ESButtonBitmap_HandleEvent` also ignore every mouse event while either flag is set, which covers the whole run of the queue and not only the movies in it. `ESArm_HandleEvent`, `ESBitmap_HandleEvent`, `ESDialog_HandleEvent` and the checkbox's left button test neither; the checkbox's right button goes through `WinButton_HandleEvent` and so tests both.

`Mission_OnTelecommPicture` (`00444e28`), the click handler of [the mission screen's Telecomm picture](mission-screen.md#the-mission-screen), jumps from its prologue to its epilogue (`00444e2f`), so a click does nothing. The 174 bytes it jumps over would read `Avi_Playing` and enqueue the briefing or debrief movie by the mission tab's view. It does not delete the event it is handed.

## Sound

The shell has a sound manager of its own: a copy of the simulator's [`SFX` manager](../simulation/audio.md#the-sfx-manager) at `ShellSound_Manager` (`004731f0`), four samples out of `SHLSOUND.VOL`, and the wrappers below. The archive holds one folder, `hmi\`, and five files, all 8-bit mono PCM:

| File | Rate | Length | Is |
|---|---|---|---|
| `gm_69.wav` | 22050 | 0.06 s | the press sound |
| `bptlt2.wav` | 22050 | 0.22 s | the tab click |
| `lswitch2.wav` | 22050 | 3.8 s | the switch the startup sequence opens with |
| `shell1.wav` | 11025 | 91 s | a music track |
| `shell2.wav` | 11025 | 93 s | the other music track |

**The tracks alternate from one run to the next.** `ShellSound_Init` (`0042ec7c`), which the startup (`Shell_Main`, `00401525`) runs once its windows are built, loads the music as `shell1.wav` while `prefs.cfg` option 5 is non-zero and `shell2.wav` while it is 0, then flips option 5, commits and writes all 54 options back ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)). A music track that will not load puts up `Cannot load sound.` and the shell carries on. The music is set to loop forever at `ShellSound_MusicVolume` (`004731fc`), which is 0 in the image. Under `-s`, which clears `Shell_SoundEnabled` (`00482272`), the setup creates no manager and returns, so option 5 stays where it was and every wrapper does nothing.

| Wrapper | Does | Only while |
|---|---|---|
| `ShellSound_PlayPress` (`0042eecf`) | `gm_69.wav` at volume 100 | there is a manager, SOUNDS (option 1, `004824b9`) is on and `ShellSound_Running` (`00473200`) is set |
| `ShellSound_PlayTabClick` (`0042ee89`) | `bptlt2.wav` at volume 100 | the same |
| `ShellSound_PlaySwitch` (`0042ef15`) | `lswitch2.wav` at volume 100 | the same |
| `ShellSound_Start` (`0042ef5b`) | sets `ShellSound_Running`, plays the press sound at volume 0, and starts the music from its top at the music volume | `ShellSound_Running` is clear |
| `ShellSound_Stop` (`0042f030`) | sets the music to play once, stops every sound, gives up the driver's focus and clears `ShellSound_Running` | `ShellSound_Running` is set |
| `ShellSound_Shutdown` (`0042f14a`) | `ShellSound_Stop`, then destroys the manager | |
| `ShellSound_FadeIn` (`0042f21c`) | raises the music volume one step at a time until it is past 99 | MUSIC (option 0) is on |
| `ShellSound_FadeOut` (`0042f178`) | lowers it one step at a time until it is below 2 | MUSIC is on |

A fade takes a step whenever more than 10 ms of `GetTickCount` have passed since the last, which at that clock's 15.6 ms granularity is about a second and a half from silence to full. It is a loop that pumps window messages and returns only when it is done, so the shell does nothing else meanwhile. The volume reaches the driver as `volume * master * 0x7fff / 10000`, with the master `Sos_MasterVolume` (`00473160`) on the 100 it holds in the image — linear in the volume.

**MUSIC gates the fades, not the music.** The two fades are the writers of the music volume that `es2_xref.py` finds ([Open](#open)), and both return at once with MUSIC off; `ShellSound_Start` does not test it. So with MUSIC off from startup the music runs at volume 0 all the while. [The preferences screen](main-menu.md#what-a-checkbox-sets)'s `Music` checkbox fades in after turning MUSIC on and fades out before turning it off. Its `Cancel` (`PreferencesScreen_OnCancel`, `00436b90`) runs the fade the reverted setting calls for, turning MUSIC on for the length of a fade out so the fade's own gate lets it run; its `Accept` runs none. A fade out stops at 1, so music turned off plays on at 1 of 100.

### What plays each sound

**The press sound goes with the class of the widget pressed** ([The widget that takes a click decides what it does](widgets.md#the-widget-that-takes-a-click-decides-what-it-does)):

| Handler | Plays it on | Tests |
|---|---|---|
| `ESButtonFont_HandleEvent` (`00409b0f`), every content button | either button going down, before `WinButton_HandleEvent` | `+0x49`, `Shell_SoundEnabled`, and the event's `+0x25` being 0 |
| `ESButtonBitmap_HandleEvent` (`00409df2`), the strip and the mission screen's arrows | the left button going down | `+0x49`, `Shell_SoundEnabled`, `Avi_Playing`, `MovieQueue_Running` |
| `ESRadioButton_HandleEvent` (`0040a139`), the [checkbox](main-menu.md#the-preferences-screen) class `ESRadioButton_Ctor` (`0040a100`) builds (vtable `0046e9a0`) — the preferences screen's eleven, `PreferencesScreen_Build`'s only use of it | the left button going down | `+0x49`, `Shell_SoundEnabled` |

Every other class is silent: rows, panels, grids, image panels, edit fields. A content button therefore sounds on the press and fires on the release, and sounds for a press that the pointer then drags off it. A mouse event's `+0x25` is 0 on every event the mouse itself queues: `MouseEvent_Ctor` (`00468cfc`) clears it and the queue's drain (`WinMouseProducer_PostEvents`, `00408c4e`) does not write it. `Career_StartMissionLoad` sets it to 1 on the press and release it posts to the mission-name dialog's `Use Default` ([Starting a practice mission](main-menu.md#starting-a-practice-mission)), so that click makes no sound.

**A tab switch makes both sounds.** `ShellSound_PlayTabClick` is the last call of all eight tab handlers ([What a tab click does](screen-layout.md#what-a-tab-click-does)), so a tab picked with the left button makes the press sound as it goes down and the click once its screen is up, and one picked with the right button, which goes through `WinButton_HandleEvent`, makes only the click. The square button makes the press sound and no click.

**The switch sound opens the startup sequence**: `004311b8`, the handler of the widget that plays it, calls `ShellSound_PlaySwitch` on its first run, once (`DAT_00473608`).

**The music starts after the startup movies.** `Shell_BuildScreensAndStart` builds every screen and then calls `ShellSound_Start`, and [the movie queue](#the-shells-movies) fades the music out and stops it before its movies and starts it and fades it in after them. On a plain startup the music therefore comes up after the two intro movies. With movies off (`Shell_MoviesEnabled`) the queue does nothing and the music stays at the startup's volume 0. The startup's other arms, for `Shell_StartupCode` 3, 4 and 6, skip the intro movies and call `ShellSound_FadeIn` themselves.

Elsewhere:

| Where | Does |
|---|---|
| `MainWndProc` (`00404a2c`), `WM_SETFOCUS` | `ShellSound_Start`, unless `Avi_Playing` — so the music comes back from its top. It also sets `Shell_HasFocus`, which [the main loop](startup.md#the-main-loop) waits on |
| `MainWndProc`, `WM_KILLFOCUS` | `ShellSound_Stop`, and clears `Shell_HasFocus` |
| `maybe_Mission_UpdateLocationTab` (`0044409f`), stage 5 | `ShellSound_FadeOut` and `ShellSound_Stop` after enqueuing the stage's movie |
| `ONLINE MANUAL` (`004317ea`) | `ShellSound_Stop` before opening the help file |
| `Shell_ShutdownDevicesAndSound` (`004092dc`), which the [main loop's exit](startup.md#leaving-the-main-loop) and the switch parser's `-v` and `-?` call, and the insert-CD failure in `Movie_PlayQueue` | `ShellSound_Shutdown` |

## Open

- **Deferred:** what [the movie queue](#the-shells-movies) does for a movie that will not open: the intro's `Please insert ESII CD and restart`, and the insert-CD panel, whose `Continue` retries the movie and whose `Quit` ends the shell.
- **Open:** whether the `avivideo` device scales a movie to fill the window `Avi_Play` moves it to. The rects say it does: the full rect is 576x360, twice the 288x180 intro, and the map panel's is exactly the thumbnails' 295x226.
- **Deferred:** what the palette handle `Avi_Play` sets does to a movie's colours.
- **Deferred:** what else writes `Avi_PaletteHandle` (`00485664`). Besides `Avi_BuildPalette`'s store, `maybe_Avi_BuildIndexPalette` (`0041dea8`) builds a 236-entry palette (entry `i` red `10 + i`, flags `PC_NOCOLLAPSE`) and stores it there; `es2_xref.py` finds no reference to that routine, and no store clearing the handle.
- **Deferred:** no store clearing `MovieQueue_IntroSkipped` (`00470fe0`) found: `es2_xref.py` finds three stores, `00404b5e`, `00404bc3` and `0041e1be`, each of 1.
- **Deferred:** no writer of `Avi_Playing` (`00470d70`) or `MovieQueue_Running` (`00470e70`) found outside the two players: `es2_xref.py` finds `Avi_Play`'s two stores and `Movie_PlayQueue`'s four.
- **Deferred:** no writer of `ShellSound_MusicVolume` (`004731fc`) found besides `ShellSound_FadeOut`'s `DEC` (`0042f1d7`) and `ShellSound_FadeIn`'s `INC` (`0042f251`): `es2_xref.py` finds those and five reads.
