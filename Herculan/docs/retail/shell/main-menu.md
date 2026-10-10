# The main menu and its screens

Tab 0's main menu and the screens it opens, each standing alone with [the strip hidden](screen-layout.md#tabs-0-and-1-hide-the-strip): the registration screen, the practice missions screen, the preferences screen, and the save screen, which is also tab 1. The widget classes they are built from are in [`widgets.md`](widgets.md).

## The main menu

Tab 0, `MAIN MENU`. Built once at shell startup by `MainMenu_BuildScreen` (`0043094c`, `wmain.cpp`), put up by `MainMenu_Show` (`004310a0`) and hidden by `MainMenu_Hide` (`0043114b`), which every button that leaves the menu calls first. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | its parent's own rect | the shared backdrop; `+0x51 = 0`, so no chrome |
| content panel | `TitledPanel` | `{0x90, 0xad, 0x1e2, 0x138}` | `2` `MAIN MENU`, header 19 tall, plate `0x6e`-`0xe4`, border `0x15`, face `0x25`, dithered body in `0x10` |
| 10 buttons | `Button` | below | border `0x22` |

The panel is the save screen's with a different rect, title plate and border — `0x15` where the save screen's is `0x27` — so its body is the same checkerboard over the bay.

Every button is `{x, y, x + 0x99, y + 0xf}` on a `0x16`-pixel pitch from row `0x1c`, in two columns at `x = 0xb` and `0xad`:

| Row | Left column | Handler | Right column | Handler |
|---|---|---|---|---|
| `0x1c` | `3` `INSTANT ACTION` | `004312a6` | `7` `PRACTICE MISSIONS` | `004318ab` |
| `0x32` | `4` `START NEW GAME` | `00431379` | `0xa` `PREFERENCES` | `0043150c` |
| `0x48` | `5` `CONTINUE GAME` | `MainMenu_OnContinue` (`004313e4`) | `0xb` `VIEW DEMO` | `0043156f` |
| `0x5e` | `6` `SAVE/RESTORE` | `MainMenu_OnSaveRestore` (`00431498`) | `0xd` `CREDITS` | `004315ec` |
| `0x74` | `0xf` `ONLINE MANUAL` | `0043178c` | `0xe` `QUIT` | `00431727` |

The builder constructs them in the order `3`, `4`, `5`, `6`, `0xe`, `0xf`, `7`, `0xa`, `0xb`, `0xd`. Three captions in the `estext.bin` run are not reached by it: `8` `CONTROLS`, `9` `VEHICLE PREVIEW` and `0xc` `SERVICE BAY`.

**`CONTINUE GAME` is gated on slot 10 being in use.** `MainMenu_Show` shows the root and the panel and then writes the [greying trio](weapons-and-repair.md#the-condition-readout) at `CONTINUE GAME` from the byte at `00482a19`. That is not a global of its own: the slot table at `00482610` has a `0x5e`-byte stride and keeps each slot's in-use byte at `+0x5d` — the byte the save screen's `RESTORE` is gated on — and `00482610 + 10 * 0x5e + 0x5d` is `00482a19`. So the button is live only once a current game has been written to slot 10. No other button is gated.

What the handlers call, as read:

| Button | Calls |
|---|---|
| `INSTANT ACTION` | `InstantAction_Active = 1` (`0047363c`), `Shell_SetCampaignMode(0)`, `InstantAction_SelectDemo` (`0044befb`, [below](#which-mission-a-row-is)), the screen blanked full screen or the palette scope shown and hidden in a window, `Game_NewCareer("TRAINEE", option 0x27)`, `Game_ExportMissionHandoff`, `Shell_SetExitCode(2)`, `Shell_QuitFlag = 1` |
| `START NEW GAME` | `MainMenu_Hide`, `Shell_SetCampaignMode(1)`, `Registration_Show` — [the registration screen](#the-registration-screen) |
| `CONTINUE GAME` | under [the hourglass](widgets.md#the-pointer): `Shell_SetCampaignMode(1)`, `Game_LoadSlot(10, 1)`, selected save slot 10; then `MainMenu_Hide` and the bare frame (`0043b162(8)`, `0043b0c8`) when `DAT_0048260e` is 2, [the END OF GAME alert](#end-of-game) otherwise |
| `SAVE/RESTORE` | `MainMenu_Hide`, `Shell_SetCampaignMode(1)`, `DAT_0048d344 = 0`, `SaveScreen_Enter` — the [save screen](#the-save-screen), with `EXIT` set to come back here |
| `ONLINE MANUAL` | `OnlineManual_Open` (`004317ea`): out of full screen, option 6 set to 0, committed and all 54 saved, `ShellSound_Stop`, then `Shell_OpenHelp` (`004073a2`), `WinHelpA(window, path, HELP_CONTENTS, 0)`, on `<language>\es2guide.hlp` ([its format](../formats/winhelp.md)), chosen by `data\language.cfg`'s first byte, `E`, `F` or `G` (`Shell_ReadLanguageCfg`, `004087b9`). Any other byte, or no file, asserts |
| `PRACTICE MISSIONS` | `MainMenu_Hide`, `PracticeScreen_Show` (`0044bc92`), `Shell_SetCampaignMode(0)` — [the practice screen](#the-practice-missions-screen) |
| `PREFERENCES` | `MainMenu_Hide`, `PreferencesScreen_Enter` — [the preferences screen](#the-preferences-screen) |
| `VIEW DEMO` | the screen blanked full screen, `Shell_SetExitCode(5)`, `Shell_QuitFlag = 1` |
| `CREDITS` | shows a bare window (`DAT_0048d0c4`) and plays movie `0x54` through `Movie_Enqueue` and `Movie_PlayQueue`, then hides it |
| `QUIT` | `Shell_QuitFlag = 1`, `Shell_BlankScreen` (`0040723d`) — [below](#quit) |

**`Shell_SetCampaignMode(mode)` (`0040e69e`) is the mode write.** It stores the campaign/training flag `DAT_0048260c`, sets `prefs.cfg` option 42 to it without running its handler, and saves that option alone, so the mode survives a restart ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)). The strip is hidden while the menu is up, and the tab gate follows the new mode at the next strip refresh.

**The menu first comes up at the end of a six-frame sequence**, unless the shell was started into a debrief that goes on to the next campaign mission or to [Replay mission?](campaign-loop.md#replay-mission). The builder also puts a widget over the whole window (`StartupAnimWidget`, `DAT_0048d0c0`, built by `ESAnim2_Ctor` (`0040c85c`) from `esanim2.cpp`, vtable `0046eec4`) and adds `dbm\bay2a_80` to `bay2a_84` to it, the last twice (`ESAnim2_AddFrame`, `0040ca06`). It is shown after the screen is blanked and palette 1 installed: by the startup once its movies are done, or at once on `-X6`; and on `-X3` or `-X4` by [the debrief](campaign-loop.md#the-debrief--game_processmissionresults-0040eae7), at the end of a training mission's and after the campaign's ending movies. The class's event handler, `ESAnim2_HandleEvent` (`0040c8b3`), answers the show (event 1) by installing a 500 ms alarm and clearing `DAT_0046c098`, the flag [`WM_CLOSE`](#quit) and the display keys wait on, and paints the current frame on event 4. Each tick (event `0x200`) while the widget is not hidden advances `+0x6d`, wrapping at the frame count, paints that frame and runs the builder's handler, `StartupAnim_OnTick` (`004311b8`). That handler plays [the switch sound](movies-and-sound.md#what-plays-each-sound) on its first run (`DAT_00473608`) and, once `+0x6d` reaches 5, hides the widget — whose hide (event 2) removes the alarm, frees the frames and sets `DAT_0046c098` — and calls `MainMenu_Show`, once only (`DAT_00473604`). So frame 0 goes up with the show, the switch sounds with frame 1 half a second later, and the menu comes up at 2.5 s over the same `bay2a_84` the last two frames show. The widget takes no mouse events, and no button is up until the menu is.

When `Shell_PerformanceNotePending` (`0046c088`) is set by [the startup's `Sierra.ini` read](startup.md#sierraini), the handler also puts up a `Performance Note` message box as the menu comes up, dropping out of full screen for it: *the game will default to 320x200 mode*. v1.10's handler shows it on `prefs.cfg` option 47 instead ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)).

### END OF GAME

`CONTINUE GAME` goes on to the frame only for game state 2, the one [the debrief](campaign-loop.md#where-the-debrief-goes-next) leaves, which `Game_LoadSlot` reads into `DAT_0048260e`. For any other it calls `EndOfGame_Show(state)` (`0044cecf`), which writes the reason into the alert `EndOfGame_Build` (`0044cc2c`) builds at startup and shows it over the menu, which stays up under it. The alert is the [launch refusal](mission-screen.md#rock--roll)'s sibling, with a different rect and plate:

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| alert | `ESAlert`, in a window the size of the display | `{0xcf, 0xcb, 0x1b4, 0x12a}` | `0x12e` `END OF GAME`, border `0x15`, face `0x25`, plate `0x31`-`0xb3` |
| first line | `Text` | `{0xc, 0x1e, 0xd6, 0x2b}` | by state: 0 `0x130` `The war is lost.`, 1 `0x131` `The cybrids were defeated.`, 3 `0x12f` `You have been killed.`; any other state leaves the line as it was, which the builder leaves on the empty entry 0 |
| second line | `Text` | `{0xc, 0x2c, 0xd6, 0x39}` | `0x132` `Restore or start a new game.` |
| `OKAY` | `Button` | `{0x41, 0x45, 0xa5, 0x54}` | `0x133`, border `0x22`; `EndOfGame_OnOkay` (`0044cf7b`) hides the alert |

Both lines are centred in `0x29`. The game `CONTINUE GAME` loaded stays loaded either way. Unlike [RESTORE](#leaving-the-save-screen) it writes no autosave and leaves the campaign map's first-show flag (`DAT_004778aa`) as it was.

### QUIT

**`QUIT` asks nothing and sets no exit code.** Its handler sets `Shell_QuitFlag` (`0046c074`), the flag that ends [the shell's main loop](startup.md#the-main-loop), and blanks the screen through `Shell_BlankScreen` (`0040723d`). Windowed, that zeroes the shell's bitmap and stretches a 10x10 corner of it over the window's client rect; full screen, it locks the primary surface and zeroes every row. Either way the whole window is palette index 0, strip included. `INSTANT ACTION` and `VIEW DEMO` blank the same way, but only full screen.

**Alt+F4 asks.** `MainWndProc` takes it as a key (key code `0x23e`) rather than passing it on, and while no movie plays and `Shell_CloseAllowed` (`0046c098`) is set it releases the pointer lock and shows an `ESAlert` built at startup by `QuitAlert_Build` (`00431918`) (`QuitAlert`, `0048d0fc`, `{0xcf, 0xcb, 0x1b4, 0x12a}` in a window the size of the display, `0x12` `QUIT`, border `0x15`, face `0x25`, plate `0x31`-`0xb3`) over whatever screen is up. Its `CANCEL` (`0x11`, `QuitAlert_OnCancel`, `00431b31`) hides it and its `ACCEPT` (`0x10`, `QuitAlert_OnAccept`, `00431ba7`) sets `Shell_QuitFlag`. While it is up the main loop repaints it every 51st pass.

Every way out — `QUIT`, the launches, Alt+F4's `ACCEPT` and `WM_CLOSE`, which `MainWndProc` (`00404a2c`) turns into the same flag once `Shell_CloseAllowed` is set — leaves through [the main loop's exit](startup.md#leaving-the-main-loop), which autosaves; `QUIT` leaves the exit code at 0, so `ES.EXE` ends.

## The registration screen

What `START NEW GAME` opens: a pilot name and a skill for a new campaign career. Built once at startup by `Registration_BuildScreen` (`0043b69e`), put up by `Registration_Show` (`0043bc0a`) and hidden by `Registration_Hide` (`0043bcb9`). Like the main menu it stands alone over a backdrop-textured root of its own, with the strip hidden, and is left through its own `CANCEL` and `ACCEPT`. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | the top-level window's own rect | the shared backdrop; `+0x51 = 0` |
| content panel | `TitledPanel` | `{0xce, 0xc6, 0x1b2, 0x144}` | `0x30` `REGISTRATION`, header 19 tall, plate `0x3f`-`0xa6`, border `0x27`, face `0x25`, dithered body in `0x10` |
| box | `FramedPanel` | `{3, 0x17, 0xe1, 0x5e}` | border `0x15`, face `0x25` |
| prompt | `Text` | `{7, 6, 0xd7, 0xf}` in the box | `0x31` `ENTER NEW PILOT NAME`, centred, `0x29` |
| name box | `Button` | `{7, 0x14, 0xd7, 0x26}` in the box | a single space (`00476167`), border `0x22`, disabled |
| name field | edit field | `{1, 1, W - 1, H - 1}` in the name box | the name, in `0x29`; handler `Registration_OnNameEvent` (`0043bdee`) |
| skill readout | `Button` | `{0x72, 0x2b, 0xd8, 0x3d}` in the box | `0x35 + skill`, border `0x13`, disabled, caption opaque in `0x17` |
| `SKILL LEVEL` | `Button` | `{7, 0x2c, 0x69, 0x3b}` in the box | `0x32`, border `0x22`; `Registration_StepSkill` (`0043c01d`) |
| `CANCEL` | `Button` | `{10, 0x66, 0x6c, 0x75}` | `0x33`, border `0x22`; `Registration_OnCancel` (`0043c098`) |
| `ACCEPT` | `Button` | `{0x76, 0x66, 0xd8, 0x75}` | `0x34`, border `0x22`, caption `0x26`, disabled; `Registration_OnAccept` (`0043c0fb`) |

`W` and `H` are the name box's own width and height, so the field lies one pixel inside it, over the inner of [a button's two borders](widgets.md#how-a-widget-paints): the name box shows its outer border alone around the field's `0x10` fill.

**The field takes keys from the start.** The builder writes its permitted-character set, `00476169` — the digits, both alphabets and the space — and leaves `ESDialog_Ctor`'s `+0xbf` and `+0xb3` set and `+0xb7` at 0, where [the save rows](#the-save-screen) clear the first two and raise the third. So it types as [a save row being renamed does](#typing-into-a-row), letters upper-cased, and erases down to empty. `Registration_Show` shows the three widgets and then does what `SAVE` does to a row: posts a left press at the field, moves the pointer onto it and locks it there, so the field has the focus and its caret blinks as the screen comes up. A press elsewhere ends that as it does on the save screen, and the field takes no key until it is clicked again.

**The name and the skill carry over.** The field starts empty, and neither the show nor either button writes it, so a second `START NEW GAME` comes back to the name typed last ([Open](#open)). The skill is `RegistrationSkillChoice` (`004761ac`), 0 in the image, which the two `SKILL LEVEL` handlers step — this screen's and [the save screen's second panel](#the-second-registration-panel)'s. `Game_NewCareer` writes it to the player pilot's `+0x25` through `Pilot_Init` (`0040fcd8`), and it never changes after ([`campaign-loop.md`](campaign-loop.md#pilot-progression)), so it is also the career's [simulator difficulty](../simulation/difficulty.md).

**`ACCEPT` is live once the name has a character.** `Registration_OnNameEvent` runs on every event the field takes, and on a character or a command writes [the greying trio](weapons-and-repair.md#the-condition-readout) at `ACCEPT` from the field's first character: greyed while it is empty, lit once it is not. The builder greys the caption and clears the enable flag but leaves the border at `0x22`, so until the first key `ACCEPT` is a live border round a grey caption.

`SKILL LEVEL` steps the skill modulo 4 on either button's release and rewrites the readout: `ROOKIE`, `REGULAR`, `VETERAN`, `ELITE`. `CANCEL` is `Registration_Hide` then `MainMenu_Show`, and leaves the mode at the campaign's.

### Starting a campaign

`ACCEPT` is:

```
LoadHercInfDat()                        // 0041181c: gam\herc_inf.dat again, the chassis flags back to the file's
Registration_Hide()
DAT_004778aa = 0                        // the campaign map's first-show flag
Game_NewCareer(name, RegistrationSkillChoice)
MissionScreenView = (CampaignMissionInStage != 0)
```

`Game_NewCareer` in a campaign builds the roster, the player and the starting hangar ([`campaign-loop.md`](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed)), and its position step, `Career_SeedPosition` (`00412a2f`), puts the career on stage 1 mission 0 and posts [the mission-name dialog](#the-mission-name-dialog)'s `Use Default` click as [a practice mission's](#starting-a-practice-mission) does. The last line therefore writes 0, the map view, before the click is delivered. That click runs [the campaign branch](campaign-loop.md#loading-the-careers-mission) of `Career_LoadCurrentMission`, which ends by putting the frame up (`0043b162(8)` and the strip refresh) and calling `Mission_ShowView(0, 1)`: the mission tab comes up in [the map view](mission-screen.md#the-three-views) on stage 1, and the left press the second argument posts at `MISSION` lights it and makes the press sound, its handler finding tab 7 already current.

**`ACCEPT` writes no save.** The career's first write to slot 10 is the next autosave: the `MAIN MENU` tab's, or [the main loop's exit](startup.md#leaving-the-main-loop), which `Rock & Roll` reaches.

## The practice missions screen

What `PRACTICE MISSIONS` opens: the eight practice missions beside five mission parameters. Built once at startup by `PracticeScreen_Build` (`0044ac80`), put up by `PracticeScreen_Show` (`0044bc92`) and hidden by `PracticeScreen_Hide` (`0044bcb3`). Like the main menu it stands alone over a backdrop-textured root of its own, with the strip hidden, and is left through its own `Main Menu`. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | its parent's own rect | the shared backdrop |
| content panel | `TitledPanel` | `{0x4d, 0xa2, 0x232, 0x170}` | `0xf0` `PRACTICE MISSIONS`, header 19 tall, plate `0xaa`-`0x13a`, border `0x15`, face `0x25`, dithered body in `0x10` |
| mission list | `FramedPanel` | `{6, 0x1a, 0xcf, 0xad}` | border `0x15`, face `0x10` |
| list title | `Text` | `{0x28, 4, 0xa2, 0x10}` in the list | `0xf1` `Practice Missions:`, centred, `0x1a`, opaque |
| 8 rows | `Panel` | `{0xe, i*0xc + 0x15, 200, i*0xc + 0x21}` in the list | `0xf2 + i`, border `0x10` |
| parameter box | `FramedPanel` | `{0xd4, 0x1a, 0x1df, 0xad}` | border `0x15`, face `0xf` |
| box title | `Text` | `{0x49, 6, 0xcf, 0x12}` in the box | `0xfa` `Mission Parameters`, centred, `0x1a` |
| 5 labels | `Button` | `{6, 0x1d, 0x90, 0x2c}`, then `0x16` lower four times, in the box | `0xfb`-`0xff`, `Damage` to `Herc Type`, border `0x22` |
| 5 colons | `Text` | `{0x93, y, 0x99, y + 0xf}` beside each label | `:`, centred, `0x28` |
| 5 readouts | `Button` | `{0x9d, 0x1c, 0x104, 0x2d}`, then `0x16` lower four times, in the box | border `0x13`, disabled, caption opaque in `0x17` |
| `Main Menu` | `Button` | `{0x56, 0xb5, 0xb8, 0xc4}` | `0x100`, border `0x22` |
| `Begin Mission` | `Button` | `{0x12e, 0xb5, 400, 0xc4}` | `0x102`, border `0x22` |

The list's face of `0x10` flattens its checkerboard; the parameter box keeps a visible one in `0xf`. The readouts are built as [the repair screen's](weapons-and-repair.md#the-repair-screen) are: boxes with a word in them that swallow a click.

**The rows are 13 tall on a 12-pixel pitch**, so the lower row owns the shared line. Each is a [four-column row](weapons-and-repair.md#a-row-is-four-text-columns) cut at `0xb8` three times: the name left-aligned from `2`, then three empty columns. A row's name is `0x27` resting and `0x29` lit, and its border `0x10` either way, so only the name shows the selection.

### The parameters

Each label steps one `prefs.cfg` option, with its own modulus, and rewrites its readout from the option's `estext.bin` run. The event's sub-code decides the direction: the left release steps forward through `ShellOptions_StepOption`, the right back through `ShellOptions_StepOptionBack`.

| Label | Handler | Option | Modulus | Readout | Becomes |
|---|---|---|---|---|---|
| `Damage` | `PracticeScreen_OnDamage` (`0044bf29`) | `0x26` | 2 | `0x128` `Vulnerable` / `Invulnerable` | `script.dat` header `+0x0c`, the player takes no damage |
| `Ammo` | `PracticeScreen_OnAmmo` (`0044bfe6`) | `0x25` | 2 | `0x12a` `Limited` / `Unlimited` | header `+0x0a`, unlimited ammunition and energy |
| `Mission Difficulty` | `PracticeScreen_OnDifficulty` (`0044c0a3`) | `0x27` | 4 | `0x35` `ROOKIE` to `ELITE` | header `+0x0e`, the [difficulty](../simulation/difficulty.md) |
| `Time of Day` | `PracticeScreen_OnTimeOfDay` (`0044c160`) | `0x29` | 2 | `0x12c` `Day` / `Night` | header `+0x12`, the theater variant |
| `Herc Type` | `PracticeScreen_OnHercType` (`0044c21d`) | `0x28` | 9 | `0x6e` `Outlaw` to `Razor` | the chassis the player flies — not a header field |

The header fields are written from these options by `MsnGen_LoadMission` ([`../formats/script-dat.md`](../formats/script-dat.md#the-training-fields)), and the simulator takes them from the header ([Open](#open)).

A step changes the array in memory only. `Begin Mission` (`0044c396`) is what saves it, so the settings persist between runs, and then flies the lit row — [Starting a practice mission](#starting-a-practice-mission). `Main Menu` (`0044c2da`) is `PracticeScreen_Hide` then `MainMenu_Show`, and leaves the mode where it is.

### Selecting a mission

A click on row `i` runs `PracticeScreen_OnRow0`-`7` (`0044c413`-`0044c6ba`), eight identical handlers that `PracticeScreen_Build` stores in the table `PracticeScreen_RowHandlers` (`0048db9c`) and from there into each row's handler field `+0x3d`. Each calls `PracticeScreen_SelectRow(i)` and deletes the event. `PracticeScreen_SelectRow(row)` (`0044bd7c`) returns at once for the row already lit, `PracticeScreen_SelectedRow` (`00479bb8`), which is `-1` in the image. Otherwise it puts the old row's name back to `0x27` and lights the new one's `0x29`; writes [the greying trio](weapons-and-repair.md#the-condition-readout) at `Herc Type`, greyed for rows 0-3 and lit from row 4; writes the row's chassis into option `0x28` (`ShellOptions_SetOption`); rewrites the `Herc Type` readout; and stores the row. The chassis is the low byte of the row's `int16` in the table at `00479bba`:

| Row | Mission | Chassis |
|---|---|---|
| 0 | `Basic Training 1` | 0 `Outlaw` |
| 1 | `Basic Training 2` | 0 `Outlaw` |
| 2 | `Basic Training 3` | 1 `Raptor II` |
| 3 | `Flyer Training` | 8 `Razor` |
| 4 | `Strike Training Mission` | 4 `Colossus` |
| 5 | `Escort Training Mission` | 2 `Tomahawk` |
| 6 | `Recon Training Mission` | 7 `Maverick` |
| 7 | `Scramble Training Mission` | 3 `Samson` |

So a row click resets `Herc Type` to that mission's machine, whatever it was stepped to. `PracticeScreen_Show` calls `PracticeScreen_SelectRow(0)`, so **the screen always comes up on `Basic Training 1`**.

**`Herc Type` is greyed where the choice is not read.** `MsnGen_BuildPlayerHerc` (`0041c58d`), which builds the player's machine when the shell loads a mission, gives the player the mission's own machine while `PracticeScreen_SelectedRow` is below 4 or `InstantAction_Active` is set, and a machine of option `0x28`'s chassis otherwise. `INSTANT ACTION`'s handler sets `InstantAction_Active` to 1 ([Open](#open)).

### Which mission a row is

The row is the mission index. `Career_SeedPosition` (`00412a2f`), which puts a new career on its first mission, sets a training-mode career's position to stage 0, mission `PracticeScreen_SelectedRow`, and stage 0 of `gam\career.dat` is `TRAIN1`-`TRAIN8` then `DEMO`, `DEMO_01` and `DEMO_02` ([`campaign-loop.md`](campaign-loop.md#the-campaign-table--gamcareerdat)) — the eight rows in order, then three more.

**`INSTANT ACTION` plays the three past the list.** `InstantAction_SelectDemo` (`0044befb`) calls `PracticeScreen_SelectRow(8 + option 0x2e)`: row 8, 9 or 10, which puts the lit row out, lights none, leaves `Herc Type`'s greying as it was, and writes the table's next three chassis, 5 `Apocalypse`, 7 `Maverick` and 3 `Samson`, into option `0x28`. It then steps option `0x2e` modulo 3 and saves the array, so successive `INSTANT ACTION`s play `DEMO`, `DEMO_01` and `DEMO_02` in turn, each in its own machine.

### Starting a practice mission

`Begin Mission` commits the options (`ShellOptions_Commit(1)`), writes all 54 to `data\prefs.cfg` (`ShellOptions_SaveAll`) and calls `Game_NewCareer("TRAINEE", option 0x27)`: the roster, the player with the difficulty as their skill, no machines — a training career's `LoadHercsDat` reads nothing — and the career position, stage 0 at the lit row ([`campaign-loop.md`](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed)). The position's last step goes through [the mission-name dialog](#the-mission-name-dialog), whose `Use Default` the shell clicks itself, to `Career_LoadCurrentMission` (`0044d4cc`).

`Career_LoadCurrentMission` loads the mission (`MsnGen_LoadMission`, `0041c73d`) and, outside a campaign ([`campaign-loop.md`](campaign-loop.md#loading-the-careers-mission) has a campaign's), goes straight on to `Game_ExportMissionHandoff`, `Shell_SetExitCode(2)` and the loop exit, the three `INSTANT ACTION` ends with. Between the write of `script.dat` and that export, the training half of `MsnGen_LoadMission` builds the squad from the mission's group 0, the one [`script.dat`](../formats/script-dat.md#placement--the-actual-rule) places the player's squad at:

1. `Squad_SetPositionsInPlay` (`004102ff`) sets the positions in play: 1, plus each member group 0 sets in an unbroken run from its second slot.
2. `MsnGen_BuildPlayerHerc` puts the player's machine in bay 0 ([Selecting a mission](#selecting-a-mission)). The mission's own machine is group 0's first member; a chosen chassis comes with its stock fit, `gam\ini_*.dat` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamini_dat--the-stock-fit-per-chassis)).
3. Squad member `i`, from 0, takes a machine built from group 0's slot `i + 1` in bay `i + 1`; `Squad_SetMemberBay` gives them the bay and runs `Squad_UpdateOnStrength`; and `Squad_SetMemberPosition` gives them position `i + 1`.

A machine built from a mission record is `Herc_SetType` on its chassis, then `Herc_FitNewUnit` (`004115c6`) with the weapon and ammunition type of each slot below the capacity whose weapon is not 0, so a `-1` there is fitted as a weapon of id `-1`.

**The last wingman never flies.** Step 3 passes `Squad_UpdateOnStrength` the member's index where a position belongs, and runs it before the member has a position. So it lands on whoever holds position `i`: nobody for member 0, and member `i - 1` after that, who goes on strength one step late. The last member to be given a bay is never updated, and `Game_ExportMissionHandoff` writes only members on strength. A squad of one wingman flies without them. Retail's training handoffs show it: each `player11.mec` below is a TRAIN5 whose group 0 gives two wingmen, and carries the player and the first.

**The loop exit saves the training career.** Its `Game_SaveSlot(10, NULL)` ([`startup.md`](startup.md#leaving-the-main-loop)) finds `Game_NewCareer`'s game in progress and the mode at training, so it writes slot 11: `GAME_T.SAV`, with `Career_SaveSlot` copying the handoff's `script.dat`, `mission.str` and `player.mec` beside it as `script11.dat`, `missn11.str` and `player11.mec` ([`../formats/save-games.md`](../formats/save-games.md#the-slot-handoff)). The save holds the career as the load left it: the position at stage 0 on the row, the squad the steps above built, the flag array the load seeded, and what [a training career keeps](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed) from the game before it. `INSTANT ACTION` leaves the same way and writes the same slot.

The draws the path makes are VSHELL's generator's ([`campaign-loop.md`](campaign-loop.md#the-shells-generator)), in this order: the roster, the player's name index, the salvage, flags 4-6, then the mission load's. Three TRAIN5 launches retail wrote to slot 11 are each this path's output, with `Herc Type` on `Colossus` and `Mission Difficulty` on 2: one on `Day` with the generator seeded 82, one on `Night` seeded 19, and one on `Day` seeded 119. The last one's `GAME_T.SAV` and three working files are its output byte for byte, stale tail aside.

## The preferences screen

What `PREFERENCES` opens: six `prefs.cfg` options ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)) as eleven checkboxes in five boxes. Built once at startup by `PreferencesScreen_Build` (`00434f08`), which also loads `dba\chk_box.dba`; put up by `PreferencesScreen_Enter` (`004366b5`) and hidden by `PreferencesScreen_Hide` (`00436717`). Like the main menu it stands alone over a backdrop-textured root of its own, with the strip hidden, and is left through its own `Cancel` and `Accept`. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | its parent's own rect | the shared backdrop |
| content panel | `TitledPanel` | `{0x72, 0x98, 0x21c, 0x183}` | `0x103` `PREFERENCES`, header 19 tall, plate `0x8c`-`0x11b`, border `0x27`, face `0x25`, dithered body in `0x10` |
| 5 boxes | `FramedPanel` | below | border `0x15`, face `0xf` |
| 5 headings | `Text` | below, in each box | `0x29`; `Audio/Speech Options:` centred, the other four left |
| 11 labels | `Text` | below, in each box | left, `0x27` |
| 11 checkboxes | checkbox | below, in each box | `chk_box.dba` frames 1 and 0, the empty caption at `00474cb1` |
| `Cancel` | `Button` | `{0xda, 0xce, 0x139, 0xdd}` | `0x10b`, border `0x22` |
| `Accept` | `Button` | `{0x13e, 0xce, 0x19e, 0xdd}` | `0x10c`, border `0x22` |

| Box | Rect | Heading | Label | Label rect | Checkbox rect | Handler | Sets |
|---|---|---|---|---|---|---|---|
| audio | `{0xc, 0x1a, 0xd0, 0x60}` | `0x106` `Audio/Speech Options:` at `{0x15, 6, 0xad, 0x12}` | `0x116` `Music` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `PreferencesScreen_OnMusic` (`00436cc1`) | option 0 |
| | | | `0x117` `Sound Effects` | `{5, 0x33, 0xaa, 0x41}` | `{0xaf, 0x32, 0xc1, 0x42}` | `PreferencesScreen_OnSoundEffects` (`00436d22`) | option 1 |
| repair | `{0xda, 0x1a, 0x19e, 0x76}` | `0x107` `Repair Options:` at `{0x36, 6, 0xa0, 0x12}` | `0x113` `AutoRepair All Hercs` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `PreferencesScreen_OnAutoRepairAll` (`00436d83`) | option 44 to 0 |
| | | | `0x114` `Manually Repair My Herc` | `{5, 0x33, 0xaa, 0x41}` | `{0xaf, 0x32, 0xc1, 0x42}` | `PreferencesScreen_OnManualRepairMine` (`00436de4`) | option 44 to 1 |
| | | | `0x115` `Manually Repair All Hercs` | `{5, 0x48, 0xaa, 0x56}` | `{0xaf, 0x48, 0xc1, 0x58}` | `PreferencesScreen_OnManualRepairAll` (`00436e45`) | option 44 to 2 |
| weapons | `{0xda, 0x7e, 0x19e, 0xc4}` | `0x108` `Weapons Building:` at `{0x2c, 6, 0xaa, 0x12}` | `0x111` `AutoBuild Weapons` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `PreferencesScreen_OnAutoBuildWeapons` (`00436ea6`) | option 45 to 0 |
| | | | `0x112` `Manually Build Weapons` | `{5, 0x33, 0xaa, 0x41}` | `{0xaf, 0x32, 0xc1, 0x42}` | `PreferencesScreen_OnManualBuild` (`00437006`) | option 45 to 1 |
| resolution | `{0xc, 0x68, 0xd1, 0xae}` | `0x120` `Game Resolution` at `{0x2c, 6, 0xaa, 0x12}` | `0x121` `High Res (640x480)` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `PreferencesScreen_OnHighRes` (`004370c8`) | option 4 to 0 |
| | | | `0x122` `Low Res (320x240)` | `{5, 0x32, 0xaa, 0x3e}` | `{0xaf, 0x32, 0xc1, 0x42}` | `PreferencesScreen_OnLowRes` (`00437067`) | option 4 to 1 |
| display | `{0xc, 0xb6, 0xd1, 0xe4}` | `0x123` `Display Mode` at `{0x35, 6, 0xad, 0x12}` | `0x124` `Window` | `{10, 0x1c, 0x37, 0x2a}` | `{0x39, 0x1a, 0x4b, 0x2a}` | `PreferencesScreen_OnWindow` (`00436f07`) | option 6 to 0 |
| | | | `0x125` `Full Screen` | `{0x5a, 0x1c, 0xa5, 0x2a}` | `{0xa9, 0x1a, 0xbb, 0x2a}` | `PreferencesScreen_OnFullScreen` (`00436f78`) | the alert, [below](#full-screen-asks-first) |

The display box lays its two out side by side, each label left of its checkbox; the other four boxes stack theirs, labels at the left and checkboxes in one column at `0xaf`. No widget overlaps another, and the panel and the boxes have no handler, so a click anywhere but a checkbox or a button is swallowed.

**The checkbox is a class of its own, `ESRadioButton` by its RTTI name.** `ESRadioButton_Ctor` (`0040a100`) is `ESButtonBitmap_Ctor` with vtable `0046e9a0` and `+0x69`, the tick, cleared. Its paint, `ESRadioButton_Paint` (`0040a26d`), is the strip's paint choosing between the two faces on `+0x69` where the strip's chooses on the lit flag `+0x45`; `+0x45` still moves the caption, which here is the empty string. The builder passes `chk_box` frame 1, the empty box, as the unticked face and frame 0, a cross, as the ticked one. Both frames are 24 wide and 17 tall, and the rect is 19 wide: the five columns the paint's clip drops are index 0. Its handler is `ESRadioButton_HandleEvent` (`0040a139`) ([The widget that takes a click decides what it does](widgets.md#the-widget-that-takes-a-click-decides-what-it-does)). **Every one of the eleven is this class** — the radio groups are groups only because their setters relight them.

### What a checkbox sets

`PreferencesScreen_Enter` seeds the ticks: `Music` and `Sound Effects` take options 0 and 1 as they stand (`PreferencesScreen_SyncSoundChecks`, `00436790`), and the four groups go through their setters with their own option's value — which writes it back unchanged — before the root and the panel are shown.

A group's setter — `PreferencesScreen_SetRepairMode` (`00436a9c`) for repair, `PreferencesScreen_SetWeaponsBuildMode` (`00436b50`) for weapons, `PreferencesScreen_SetGameResolution` (`00436abc`) for resolution, `PreferencesScreen_SetDisplayMode` (`00436b70`) for display — stores its value in a word of its own (`00474cc4`, `00474cc8`, `00474cc6` and `00474cca`, [Open](#open)), writes the option through `ShellOptions_SetOption` with apply set, and relights the group: `+0x69` to 1 on the checkbox of the option's value and 0 on the others. A value no checkbox in the group names relights nothing.

The two sound checkboxes run `PreferencesScreen_ToggleAudioOption` (`00436841`) with 0 and 1 and then reseed both ticks. Case 1 toggles SOUNDS. Case 0 toggles MUSIC and runs a fade: turning it on, the toggle and then `ShellSound_FadeIn`; turning it off, `ShellSound_FadeOut` and then the toggle, so the fade's own MUSIC gate lets it run ([Sound](movies-and-sound.md#sound)). The function also has cases 2 and 3, which cycle option 2, the simulator's PILOT MESSAGE ([Open](#open)).

`Game Resolution` is the simulator's video mode ([`../simulation/preferences.md`](../simulation/preferences.md#the-video-mode-and-full-screen-bytes)); the shell has one mode and does not read it.

### Full screen asks first

`Window` (`PreferencesScreen_OnWindow`, `00436f07`) toggles the shell's window out of full screen through `Display_ToggleFullScreen` (`00407085`) when `Display_FullScreen`, the full-screen flag, is set, and then sets option 6 to 0. `Full Screen` (`PreferencesScreen_OnFullScreen`, `00436f78`) sets nothing: while the shell is windowed it shows a window the size of the display holding an alert, and otherwise does nothing. The alert's `ACCEPT` (`FullScreenAlert_OnAccept`, `00436fe8`) hides the window, toggles the shell into full screen and sets option 6 to 1. So `Full Screen` is ticked only after that `ACCEPT`.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| window | `Window` | the top-level window's absolute rect | hidden once its children are built |
| alert | `ESAlert` | `{0x68, 199, 0x226, 0x117}` | `0x126` `Alert!`, border `0x27`, header 20 tall, face `0x25`, plate `0xbe`-`0xfa`, filled body |
| line | `Text` | `{5, 0x1c, W - 4, 0x2a}` | `0x127` `If you experience difficulties, hit alt-enter and view the read-me.`, left, `0x27` |
| `ACCEPT` | `Button` | `{0xa8, 0x38, 0x107, 0x47}` | `0x10`, border `0x22` |

`W` is the alert's own width.

**Full screen is an exclusive display mode.** `Display_ToggleFullScreen` toggles it. Going in, it sets `Display_FullScreen`, creates a DirectDraw object and takes it exclusive and full screen (`Display_CreateDirectDrawExclusive` (`00406eb5`), cooperative level `0x17`), sets a 640x480 8-bit display mode and creates the primary surface (`DDraw_SetModeAndCreatePrimary` (`00406eeb`), with the canvas size from the shell's bitmap header `DAT_00481864`), and places the window topmost with its frame pushed off the screen, so its client area is the screen. It then marks the palette's entries, gives the primary surface a palette, confines the pointer to the screen (`ClipCursor`) and centres it. If DirectDraw or the mode fails the shell quits. Coming out, it clears the flag, releases every DirectDraw object (`Display_ReleaseDirectDraw`, `00407011`), which gives the desktop its mode back, and centres the window, no longer topmost.

**The startup enters it from option 6.** `Shell_WinMain` (`00406507`), the startup under `WinMain`, reads `prefs.cfg` (`ShellOptions_Init`, `0040d68c`), copies option 6 into `Shell_StartedFullScreen` (`0046d740`), builds the window over the desktop and topmost while that is set, and then calls `Display_ToggleFullScreen`. It also looks for a `-d` or `/d` argument and clears `Shell_StartedFullScreen` for one, but that store (`0040656c`) comes before the copy from option 6 (`00406583`), which overwrites it with nothing reading it between, so `-d` has no effect. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**Four keys switch it**, in `MainWndProc` (`00404a2c`), each only while no movie plays and `DAT_0046c098` is set, which the startup does once the screens are built and the startup sequence's widget clears while it runs. Alt+Enter toggles full screen on the Enter key's release; Alt+Tab, Alt+Esc and Ctrl+Esc leave it (`Display_LeaveFullScreen` (`0040722e`)) on the key going down or up. Each then writes option 6 from the flag, and with the preferences screen's panel up relights its display group and repaints it; otherwise it runs `ShellOptions_Commit(0)` and `ShellOptions_SaveAll`. A modifier other than the one named stops the key matching. The shell also leaves full screen around its own message boxes and goes back after.

### Leaving the preferences screen

Both buttons end in `PreferencesScreen_Hide` (`00436717`) and `MainMenu_Show`, and differ in what they do with the options first:

| Button | Does first |
|---|---|
| `Cancel`, `PreferencesScreen_OnCancel` (`00436b90`) | `ShellOptions_RevertAll(0)`: every option that differs from the shadow is put back, running no handler. Then `Display_ToggleFullScreen` when option 6 and the full-screen flag disagree, and the fade [Sound](movies-and-sound.md#sound) describes |
| `Accept`, `PreferencesScreen_OnAccept` (`00436c51`) | `ShellOptions_Commit(0)`, which rebaselines the shadow and runs no handler, then `ShellOptions_SaveAll` |

The shadow is the array as of the last commit, so `Cancel` puts back every option changed since, the practice screen's parameters among them, which that screen steps without committing ([The parameters](#the-parameters)).

## The save screen

Tab 1, `SAVED GAMES`. Built by `SaveScreen_BuildScreen` (004385b0), entered by `SaveScreen_Enter` (00439b0c), its selection moved by `SaveScreen_SelectSlot` (0043795f) and its detail panel refilled by `SaveScreen_RefreshDetail` (0043712c). Every rect is four immediates on the builder's stack, and they are **parent-relative**: the panel sits in the canvas, the list and the button column in the panel, and two buttons in the list.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | `0040b698` | its parent's own rect | the shared backdrop; `+0x51 = 0`, so no chrome |
| content panel | `TitledPanel` | `{0x8e, 0x7f, 0x1f2, 0x1d4}` | `0x1b` `SAVED GAMES`, header 19 tall, plate `0x70`-`0xf3` |
| slot list | `FramedPanel` | `{9, 0x1c, 0x15b, 0xc6}` | |
| 10 slot rows | edit field | `{10, i*12 + 0x13, W-2, i*12 + 0x1f}` | the slot's `GAMEFILE.STR` label |
| `CANCEL` | `Button` | `{0x43, 0x93, 0xa5, 0xa2}` in the list | `0x33` |
| `ACCEPT` | `Button` | `{0xb0, 0x93, 0x112, 0xa2}` in the list | `0x34` |
| `SAVE` | `Button` | `{9, 0xe5, 0x6b, 0xf4}` | `0x1d` |
| `RESTORE` | `Button` | `{9, 0xfb, 0x6b, 0x10a}` | `0x1e` |
| `EXIT` | `Button` | `{9, 0x111, 0x6b, 0x120}` | `0x1f` |
| detail panel | `FramedPanel` | `{0x74, 0xcc, 0x15b, 0x150}` | 23 `Text` children: 11 labels and 12 values |
| registration panel | `FramedPanel` | `{0x74, 0xcc, 0x15b, 0x136}` | `DAT_0048d418`, a second registration panel, [below](#the-second-registration-panel) |

The panel is centred on x=320 rather than on the canvas's own inclusive midpoint, so its left margin is 142 and its right 141. The builder overwrites three class defaults on it: `+0x59` to 0 for the dithered body, `+0x5d` to `0x10`, and `+0x55` on all three framed panels to `0x10` — which flattens their checkerboard, since it then dithers the interior colour over itself.

**The rows are 13 tall on a 12-pixel pitch**, so each overlaps its neighbour's border row, and the first and last are inset two pixels further from the left edge than the eight between them. Their right edge is computed from the list panel's absolute corners rather than written, at two pixels inside it. Each carries a permitted-character set at `+0x9f` — `^`, the digits, both alphabets and the space (`004757cd`) in place of the class's upper-case alphabet and space — so **renaming a slot is typing into its row**, and `CANCEL`/`ACCEPT` are that edit's two buttons rather than the screen's. The builder clears `+0xb3` and `+0xbf` on every row, so a row takes no keystroke and shows no caret until [a rename](#saving-is-a-rename) sets both, and writes `+0xb7 = 4`, the length [erasing](#typing-into-a-row) stops at, so the `"%2d. "` prefix cannot be deleted. Selection is `+0xbb`: `0x27` resting, `0x29` selected.

Three buttons are gated, each written as the trio [the repair panel uses](weapons-and-repair.md#the-condition-readout): `SAVE` on a row being selected and there being a game to write (`DAT_0048260a`), `RESTORE` on the selected slot's in-use byte, and both `CANCEL` and `ACCEPT` on the rename being live. `EXIT` has no condition of its own; a rename greys it with the other two ([below](#saving-is-a-rename)).

### Saving is a rename

`SAVE` writes nothing. Its handler (`00437bd3`) starts a rename of the selected row, and `ACCEPT` is what writes the save. `DAT_00474f40` is the rename's state: 0 idle, 2 while a rename is live, and 1 only transiently inside `SAVE`'s handler.

| Handler | What it does |
|---|---|
| `SAVE`, `00437bd3` | edit state to 1; greys `SAVE`, `RESTORE` and `EXIT`; calls `004377d2` |
| `004377d2` | returns at once if the edit state is 0. Otherwise posts a left press (event `0x20`, sub-code 2) at the selected row, moves the pointer onto the row (`Pointer_SetTarget`, 00469cbc) and [locks it there](widgets.md#which-widget-a-click-reaches), so keystrokes reach the row; sets its caret flags `+0xbf` and `+0xb3`; rewrites it as `"%2d. %s"` of the slot number and the empty string at `0047526a`, so it reads ` 3. ` with the name gone; lights `CANCEL` and `ACCEPT`; edit state to 2; repaints the list. The press, delivered after the handler returns, gives the row the focus and runs its handler, whose selection move edit state 2 refuses |
| `ACCEPT`, `00437ffa` | `Game_SaveSlot(selected, text)`, where the text is the row's own string buffer at `+0x45` (`00437ba9`), so what was typed, prefix and all, becomes the slot's label ([`../formats/save-games.md`](../formats/save-games.md#writing-a-slot)); `Stats_StageCurrentGame(selected)`; refreshes the detail panel; greys `CANCEL` and `ACCEPT`, lights `SAVE`, `RESTORE` and `EXIT`; edit state to 0 |
| `CANCEL`, `00437e1a` | puts the row's `GAMEFILE.STR` label back; greys `CANCEL` and `ACCEPT`, lights `SAVE`, `RESTORE` and `EXIT`; edit state to 0; `SaveScreen_SelectSlot(10)` |

Edit state 2 is what `SaveScreen_SelectSlot` refuses, so the selection cannot move off the row being renamed. `ACCEPT` and `CANCEL` light the three buttons without their usual tests. After `CANCEL` that does not last: the closing `SelectSlot(10)` deselects the row and regates `SAVE` and `RESTORE`, both dead with no row selected. After `ACCEPT` the selection stays on the slot just written.

The click on `ACCEPT` or `CANCEL` reaches it through the locked row: the press lands on the focused field, which gives up the focus and the lock and posts the press over to what is under the pointer ([The widget that takes a click decides what it does](widgets.md#the-widget-that-takes-a-click-decides-what-it-does)). A click on another row lands there instead, takes the focus and the lock, and moves nothing; keystrokes then go to that row, which takes none while its `+0xbf` is clear, until a click puts the focus back on the row being renamed.

### Typing into a row

**A key reaches the pointer's target.** `MainWndProc` turns a `WM_KEYDOWN` or `WM_KEYUP`'s virtual-key code into its position in the table at `0046d384`, a set-1 scancode, adding `0x800` for Shift, `0x400` for Ctrl, `0x200` for Alt and `0x80` for a release, and drops a key that is not in the table — every punctuation key among them. The keyboard object then sorts it (`Keyboard_PostEvents`, `00408f95`):

- A key in the filter at `0046e450` — Esc, Backspace, Tab, Enter, the editing cluster and the arrows — is a command, event `0x100`, whose code is `0046e471` at the key's index. Press and release both send one: Backspace sends 1, the left arrow 4 and Enter `0x0a` on the press, and their releases send codes of their own.
- Any other key's press is a character, event `0x40`, from `0046e571`, or `0046e5c5` with Shift down, and upper-cased. Both tables hold the letters in upper case already. Among the keys the virtual-key table admits they differ only on the digit row, where Shift gives `!@#$%^&*()`; they also differ at the punctuation keys `-=[];'\,./`, which never reach them. Ctrl+Q sends an event of its own instead.

Neither carries a target, so each goes where the last move left the pointer, or where a lock holds it.

**Only an edit field takes a command**, so Esc does nothing elsewhere. Every write to a widget's event mask `+0x39` (`es2_fieldscan.py --binary VSHELL 39`) sets bits from `0x1f`, `0x60` and `0x200`, except `ESDialog_Ctor`'s `0x360`, which alone carries the command bit `0x100`; `Event_Deliver` (`00469f34`) discards an event no widget up the pointer's chain accepts. The key ring has two other readers, each of which takes Esc (scancode 1) or Space as a skip: `Avi_Play` ([The shell's movies](movies-and-sound.md#the-shells-movies)) and `ShellMap_RunIntro` (`0040146a`), the [briefing map's](mission-map.md#the-intro) intro. With Alt or Ctrl held, Esc leaves [full screen](#full-screen-asks-first) instead.

**`ESDialog_HandleEvent` edits the string, then runs the handler, on every key.**

| Event | While | What it does |
|---|---|---|
| a character | `+0xbf` set | `ESDialog_TypeChar` (`0040bdd2`): the character goes on the end when the set at `+0x9f` holds it, the new length stays below `0x5a`, and the glyph's width, the string's cached width `+0xaf` and six more are less than the field's width |
| Backspace or the left arrow | `+0xbf` and the focus `+0xa7` set | `ESDialog_Erase` (`0040be56`): the last character comes off while the length is above `+0xb7` |
| Enter | the same | the focus cleared and the pointer released (`Pointer_Unlock`); the rename stays live |

Whatever the event did, the field then runs its handler, which on a save row is `SaveScreen_SelectSlot(row)`. So a keystroke over a row selects it, while no rename is live, and neither a key nor Enter ends a rename: only `ACCEPT` and `CANCEL` do. Letters arrive upper-cased, so of the set's two alphabets only the upper-case one is ever typed, and with punctuation dropped before the tables a row takes letters, digits, the space and Shift+6's `^`.

**The caret blinks while the field has the focus.** `ESDialog_Paint` draws a 7-by-3 block in `0x27` at the string's cached width, above the baseline, while both `+0xa7` and `+0xb3` are set. The press that focuses a field installs a 500 ms alarm for it, and each tick flips `+0xb3` while `+0xbf` is set and clears it otherwise; the press that takes the focus away, or a leave, removes it. **A row keeps `+0xbf` after its rename**: neither `ACCEPT`, `CANCEL` nor `SaveScreen_Enter` clears it ([Open](#open)), so a row once renamed keeps taking keystrokes: whenever it is the pointer's target its string can be typed over, which changes the row and nothing else until the next `SaveScreen_Enter` puts the labels back, and whenever it has the focus its caret blinks.

### Leaving the save screen

With [the strip hidden](screen-layout.md#tabs-0-and-1-hide-the-strip), `EXIT` and `RESTORE` are the screen's only buttons that leave it. Both open with `SaveScreen_Teardown` (00439d66), which parks the selection on slot 10 — past every row, so the screen next comes up with nothing selected and `SAVE` and `RESTORE` both dead — and hides the screen's root, content panel and both detail panels.

`SaveScreen_OnExit` (00437d94), `EXIT`'s handler, then goes where `DAT_0048d344` says. 0 rebuilds the main menu; the main menu's `SAVE/RESTORE` writes it ([above](#the-main-menu)). 8 is `0043b162(8)` then `0043b0c8` — show the frame's root, show and regate the strip, park the current tab at `0xffff` — which leaves the bare frame up with no tab current and nothing lit; tab 1's handler writes it.

`RESTORE`'s handler (`00437d03`) is:

```
Game_LoadSlot(selectedSlot, 1)   // the whole save, and its career files into data\
Game_SaveSlot(10, NULL)          // straight back out as the current-game autosave
teardown
DAT_004778aa = 0
0043b162(8); 0043b0c8()          // EXIT's tab-strip path, whatever DAT_0048d344 holds
```

`Game_LoadSlot` sets `DAT_0048260a`, so `SAVE` is live from then on, and does not touch the campaign mode flag, so the slot-10 write lands in `GAME_R.SAV` or `GAME_T.SAV` by whichever mode the shell is already in ([`../formats/save-games.md`](../formats/save-games.md)). `DAT_004778aa` is the campaign map's first-show flag, 0 in the image: `Mission_Show` (004441e3) runs two `Movie_Enqueue` calls in its map arm only while it is clear and then sets it, and `TabHandler_Mission` (0043a6ca) opens the map rather than the briefing only while it is clear and the mission-within-stage counter is zero.

### The detail panel

`SaveScreen_RefreshDetail` writes 12 value fields from the selected slot's [staging record](../formats/save-games.md#the-slot-summary--statscpps-scan), or from `estext.bin` entry 0 — the empty string — when the slot holds no save, so the labels stay and the figures blank.

| Row | Label | Value |
|---|---|---|
| `0x06` | `0x24` `Name:` | the pilot's name |
| `0x12` | `0x25` `Skill:` / `0x26` `Rank:` | `0x35 + skill` and `0x39 + rank`, two runs of four words |
| `0x24` | `0x2a` `Current` / `0x2b` `Total` | column headers, centred over the grid below |
| `0x30`-`0x48` | `0x27`-`0x29` Herc / Flyer / Base `Kills:` | six counters, per-mission in the left column and career totals in the right |
| `0x5a` | `0x2c` `Salvage:` | the pool in kilograms divided by 1000, then `0x2f` `Tons` |
| `0x66` | `0x2e` `Sector:` | `0x76 + stage` |
| `0x72` | `0x2d` `Mission:` | the counter plus one |

The label indices run out of layout order: `Salvage:`, `Sector:` and `Mission:` are drawn in that order from `0x2c`, `0x2e`, `0x2d`.

**The sector run starts at stage 1.** `0x76` is `Razor`, a chassis name; the five sector words `Alpha`, `Delta`, `Omicron`, `Bravo`, `Luna` start at `0x77`. Stage 0 holds the practice missions and the demos, and the campaign's chapters are stages 1-5 ([`campaign-loop.md`](campaign-loop.md#the-campaign-table--gamcareerdat)), so the first chapter lands on the first sector word.

### The second registration panel

`SaveRegistration_BuildPanel` (`0043b260`), which the startup runs just before `Registration_BuildScreen`, fills `DAT_0048d418` with a copy of [the registration screen](#the-registration-screen)'s content in the detail panel's place: a `FramedPanel` at `{7, 6, 0xe1, 0x4b}` (`DAT_0048d470`) holding the prompt, a name box and its field, a skill readout (a `Text`, not a `Button`) and a `SKILL LEVEL` row (`SaveRegistration_StepSkill`, `0043bd15`), with `CANCEL` (`SaveRegistration_OnCancel`, `0043bd90`) and `ACCEPT` (`SaveRegistration_OnAccept`, `0043bf1b`) on `DAT_0048d418` itself. `ACCEPT` runs `Game_NewCareer` with the typed name and the shared `RegistrationSkillChoice`, then `Stats_StageCurrentGame(10)`, then `SaveRegistration_ShowDetailPanel` (`0043b679`) — which hides both panels and shows the detail panel again — and lights `SAVE`. The builder leaves its `ACCEPT` enabled ([Open](#open)), and `ACCEPT` does not reload `gam\herc_inf.dat`. `SaveScreen_Enter` and the teardown both hide `DAT_0048d418`; what shows it is [Open](#open).

## The mission-name dialog

**A developer's dialog titled `DEBUG` stands between every career's position and its mission load, and a player never sees it.** `Career_StartMissionLoad` (`00412ce1`), the last step of `Career_SeedPosition` (`00412a2f`) for a new career and of [the debrief](campaign-loop.md#where-the-debrief-goes-next) for the next mission, shows it and posts a press and a release at its `Use Default`, which [makes no sound](movies-and-sound.md#what-plays-each-sound). The event loop delivers them once the handler that got there has returned, so the dialog goes down before anything repaints. With `-@` (`Shell_MissionPickerEnabled`, `00482284`, [`../command-line.md`](../command-line.md#--the-mission-picker)) it posts nothing, and the dialog waits for a click.

Built once at startup by `MissionNameDialog_Build` (`0044d6a8`), shown by `MissionNameDialog_Show` (`0044db25`) and hidden by `MissionNameDialog_Hide` (`0044db87`). Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | the top-level window's own rect | the shared backdrop; `+0x51 = 0`, and no handler |
| panel | `TitledPanel` | `{0xce, 199, 0x1b2, 0x145}` | `DEBUG`, header 19 tall, plate `0x3f`-`0xa6`, border `0x27`, face `0x25`, dithered body in `0x10` |
| prompt | `Text` | `{0x26, 0x1d, 0xb9, 0x26}` | `Enter Mission File Name`, centred, `0x29` |
| name box | `Button` | `{10, 0x2b, 0xda, 0x3d}` | a single space, border `0x22`, disabled |
| name field | edit field | `{1, 1, W - 1, H - 1}` in the name box | the typed name; permitted set `0047a445`, `^` then the digits, both alphabets and the space; no handler |
| label | `Text` | `{10, 0x46, 0x42, 0x58}` | `Default:`, centred, `0x29` |
| default | `Text` | `{0x46, 0x46, 0xda, 0x58}` | the position's mission, centred, `0x29` |
| `Use Default` | `Button` | `{0x76, 0x66, 0xd8, 0x75}` | border `0x22`; `MissionNameDialog_OnUseDefault` (`0044d55a`) |
| `ACCEPT` | `Button` | `{10, 0x66, 0x6c, 0x75}` | `0x34`, border `0x22`; `MissionNameDialog_OnLoad` (`0044d5bd`) |

Every caption but `ACCEPT`'s is a string of the builder's own. The root covers the whole display, so nothing beneath shows and a click anywhere but the field and the two buttons is swallowed.

`MissionNameDialog_Show` writes the position's mission from `gam\career.dat`, a path such as `MSN\C1_01.MSN`, into the default, or the empty string past the end of the stage, and shows the root and the panel. It does not take the pointer: the field types as [the registration screen's](#the-registration-screen) does once it is clicked, and since nothing clears it a later show comes back to the name typed last.

Both buttons hide the dialog first. `Use Default` then runs `Career_LoadCurrentMission` (`0044d4cc`). `ACCEPT` instead loads `msn\<name>.msn` (`"msn\\%s.msn"` at `0047a2a6`, the name's first `^` turned to `_`, which no key types) through `MsnGen_LoadMission`, and goes on as `Career_LoadCurrentMission` does after its load: in a campaign the map, the briefing text, slot 10's summary, the mission tab's palette, the strip and `Mission_ShowView(MissionScreenView, 1)`; in training the handoff, exit code 2 and the loop's end. The career position stays on its own mission.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `START NEW GAME`'s `ACCEPT` is `0043bf1b` and its `SKILL LEVEL` `0043bd15` | Both handle a registration panel — one calls `Game_NewCareer` with a typed name, the other steps `RegistrationSkillChoice` — and they sit beside `Registration_Show`. They belong to `SaveRegistration_BuildPanel`'s (`0043b260`) [second panel](#the-second-registration-panel) in the save screen, whose widgets `Registration_Show` never touches; the screen it shows is `Registration_BuildScreen`'s, with `0043c01d` and `0043c0fb` ([The registration screen](#the-registration-screen)) |

## Open

- **Open:** whether a command key with Shift, Ctrl or Alt down posts a command. `Keyboard_PostEvents` (`00408f95`) indexes the table at `0046e471` with the whole key code, so it reads a byte of the data section past the table's 256. None of those bytes in the image is 1, 4 or `0x0a`, so none edits a row ([Typing into a row](#typing-into-a-row)), but any that is not `0xff` posts a command, and with it runs the row's handler.
- **Deferred:** what reads the words the preferences screen's four group setters store, `00474cc4`, `00474cc6`, `00474cc8` and `00474cca` ([What a checkbox sets](#what-a-checkbox-sets)).
- **Deferred:** what reaches cases 2 and 3 of `PreferencesScreen_ToggleAudioOption` (`00436841`), which cycle PILOT MESSAGE (option 2) — case 2 from 0 to 2 and from 1 or 2 to 0, case 3 from 0 to 1, 1 to 2 and 2 to 1. `es2_xref.py` finds two callers, `PreferencesScreen_OnMusic` (`00436cc1`) and `PreferencesScreen_OnSoundEffects` (`00436d22`), which pass 0 and 1, and the builder makes no widget for the others.
- **Deferred:** what shows [the second registration panel](#the-second-registration-panel). A search of the disassembly for `DAT_0048d418` and `DAT_0048d470` as absolute operands finds their builders and three hides — `SaveScreen_Enter`, the teardown and `SaveRegistration_ShowDetailPanel` (`0043b679`) — and no show; the dead rect `{9, 0xcf, 0x6b, 0xde}` that `SaveScreen_BuildScreen` writes just before `SAVE`'s may be where a button that showed it stood.
- **Unported:** the startup's `Performance Note` box ([The main menu](#the-main-menu)).
- **Unported:** Alt+F4's `QUIT` alert ([QUIT](#quit)).
- **Deferred:** no other writer of the registration screen's name or `RegistrationSkillChoice` found: `es2_xref.py` finds the name field's pointer (`0048d4a8`) stored only by its builder and otherwise read, and `004761ac` stored only by the two `SKILL LEVEL` handlers (`0043bd3a`, `0043c042`) ([The registration screen](#the-registration-screen)).
- **Deferred:** no simulator read of options 37-41 found ([The parameters](#the-parameters)): `es2_xref.py` finds no reference to `SimOptions + 0x25` to `+0x29` (`004d1fe1`-`004d1fe5`) in DBSIM, where option 6's `004d1fc2` has one, and DBSIM has no indexed option getter.
- **Deferred:** no reference to `InstantAction_Active` (`0047363c`) found besides `INSTANT ACTION`'s store of 1 (`004312b6`) and `MsnGen_BuildPlayerHerc`'s read (`0041c625`), by `es2_xref.py` ([Selecting a mission](#selecting-a-mission)).
- **Open:** no store clearing a save row's `+0xbf` after a rename found: `es2_fieldscan.py bf` finds `ESDialog_Ctor`'s 1, `SaveScreen_BuildScreen`'s 0 (`00438936`) and `SaveScreen_BeginRename`'s 1 ([Typing into a row](#typing-into-a-row)).
- **Deferred:** no write greying [the second registration panel](#the-second-registration-panel)'s `ACCEPT` found: `es2_xref.py` finds no reference to its pointer `0048d490` but the builder's store.
