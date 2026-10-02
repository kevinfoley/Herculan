# Command line: Herculan.Engine.Host

Every argument `Herculan.Engine.Host` accepts. The parser is the loop at the top of `src/Herculan.Engine.Host/Program.cs`; this page lists what each flag does and links the doc that explains the feature behind it. The retail executables' switches are in [`launch-options.md`](../launch-options.md) and [`command-line.md`](../command-line.md).

Flags are this engine's own. Where one stands in for a retail switch, the table says which. Flags are case-sensitive, take `--` only, and can come in any order. `--help` (or `-h`, `-?`) prints a summary and exits. An unknown flag, a missing or out-of-range value, or a third positional argument stops the host with a message naming each problem, before it looks for the install.

```
Herculan.Engine.Host [<install>] [<mission>] [flags]
```

## Positional arguments

| Position | Meaning |
|---|---|
| 1 | The Earthsiege 2 install: the folder holding the archive directory. Without it the host reads the `ES2_GAME_PATH` environment variable, then tries the install it last used, then looks for an `ES2` folder beside the executable or any folder above it, and when all of those fail opens a window asking for the folder, with the system's folder picker where there is one. A named install that is not one stops the host with a message, as does a failed search under `--screenshot`. Whichever install runs is remembered in `install-path.txt` beside `tweak-settings.json`. |
| 2 | The mission to fly: a `script.dat` ([`formats/script-dat.md`](../formats/script-dat.md)), any `SAV\script*.dat` save-slot snapshot, or a mission named by its `.MSN` — `C1_03`, `C1_03.MSN` or `MSN\C1_03.MSN` — from those `gam\career.dat` lists. Defaults to `DATA\script.dat` in the install. `--play` and `--demo` replace it with the mission their tape carries. |

A named mission is loaded as the shell loads that career position ([`shell/campaign-loop.md`](../shell/campaign-loop.md#loading-the-careers-mission)) and flown from a handoff written to a scratch folder, leaving the install's `DATA` alone except for the settings the simulator reads and writes there. A practice or demo mission takes the training load, with the practice options `DATA\prefs.cfg` holds, as `Begin Mission` or `INSTANT ACTION` on its row would. A campaign mission is loaded for a career with no history: every campaign flag 0 until the load seeds its own, and the player's lance and skill from `DATA\player.mec`, which it needs.

## What runs

Without one of these, the host flies the mission.

| Flag | Effect |
|---|---|
| `--shell` | Runs the front end instead of a mission, and the missions it launches, coming back to it after each as `ES.EXE` does ([`command-line.md`](../command-line.md#the-loop)). The other `--shell-*` flags imply it, and stage its first turn only. See [`shell/screen-layout.md`](../shell/screen-layout.md). |
| `--movie <name>` | Plays one cutscene: a path, or a name looked up in the install's `AVI` folder, with or without the extension. See [`video-playback.md`](video-playback.md#looking-at-one). |
| `--play <tape>` | Replays an input tape: a path, or a stem looked up in the install's `TAPES` folder. Hands the controls to the player when the tape runs out. Retail's `-p<name>`. See [`input-tapes.md`](input-tapes.md). |
| `--record <tape>` | Records the mission's input to `<tape>.tap`, which `--play` replays. Cannot be combined with `--play` or `--demo`. Retail's `-r<name>`. See [`input-tapes.md`](input-tapes.md#recording). |
| `--demo` | Plays a tape picked from `TAPES\demolist.str`, as VIEW DEMO does, and ends the mission when the tape runs out or a key is pressed. With `--play`, plays that tape in demo mode instead. Retail's `-D`. |

## Front end

All of these imply `--shell`.

| Flag | Effect |
|---|---|
| `--shell-tab <0-7>` | The tab the front end opens on, in place of the main menu and the startup sequence before it. |
| `--shell-bay <0-7>` | The hangar bay the repair tab works on. |
| `--shell-training` | Runs the front end in training mode, the practice missions' and `INSTANT ACTION`'s, which gates REPAIR, BUILD and ARMORY off, whatever mode `data\prefs.cfg` holds. |
| `--shell-practice` | Opens on the practice missions screen, as the main menu's PRACTICE MISSIONS does, without the startup sequence. |
| `--shell-windowed` | Keeps the front end windowed at startup, where `data\prefs.cfg` option 6 would put it in full screen. This engine's own flag; retail's `-d` has no effect ([`shell/screen-layout.md`](../shell/screen-layout.md#full-screen-asks-first)). |
| `--shell-no-movies` | Turns the front end's movies off: nothing is queued and nothing plays, and the music stays at the startup's silence until a fade raises it. Retail's `-a` ([`shell/screen-layout.md`](../shell/screen-layout.md#the-shells-movies)). |
| `--shell-palette <name>` | Pins `dpl\<name>.DPL` as the palette for the whole run, in place of each tab's own. |

See [`shell/screen-layout.md`](../shell/screen-layout.md).

## Sound and music

| Flag | Effect |
|---|---|
| `--no-sound`, `--silent` | Opens no audio device. Everything that drives sound still runs; nothing is heard. Retail's `-s`. In the shell, as with `-s`, there is no sound manager at all, so the music track is not flipped. |
| `--music <n>` | The CD track select: the mission plays track *n* % 5 + 2. Retail's `-R<n>`. Under `--shell` each mission launched counts on from it, as the launcher counts from 0; a lone mission without it plays track 2. |
| `--cd-drive <drive>` | The drive holding the music CD. |
| `--music-dir <dir>` | A folder of `Track02.wav` … `Track07.wav` to play in place of the disc. |

See [`formats/audio.md`](../formats/audio.md#cd-audio).

## Settings and input devices

| Flag | Effect |
|---|---|
| `--no-write-prefs` | Leaves `data\prefs.cfg` unwritten: when the preferences and controls panels close, and in the shell at startup, on a change of campaign or training mode, and on `Begin Mission` and `INSTANT ACTION`. See [`simulation/preferences.md`](../simulation/preferences.md) and [`shell/screen-layout.md`](../shell/screen-layout.md#sound). |
| `--joystick [0-8]` | Pretends a stick with throttle, rudder and hat is attached, so the CONTROLS panel's joystick rows are live without hardware. The number sets how many of the eight button rows are live; without it, all eight. A real stick, when one is attached, takes precedence. |
| `--joystick-probe` | Prints each axis and button of the attached stick as it moves. |
| `--write-joystick-map` | Writes the joystick map in force to `data\herculan-joystick.cfg`. |

See [`joystick-config.md`](joystick-config.md).

## Developer

| Flag | Effect |
|---|---|
| `--developer` | The developer keys. Retail's `-SPRUNKNOWN`. See [`key-bindings.md`](../key-bindings.md#herculan-engine). |

## Screenshots and staged state

`--screenshot <file>` renders 30 frames, captures the window to `<file>` and exits. It works for a mission, `--shell` and `--movie`, and hides the menu bar. A `--shell` capture opens on the main menu without its startup sequence. A screenshot run sees no keyboard or mouse input, so the flags below put the cockpit into the state to be photographed at power-up; they work in an interactive run too.

Several of them hold the capture past the 30 frames until what they stage is on screen:

| Flag | Holds the capture until |
|---|---|
| `--target` | a target is acquired |
| `--fire` | a round, beam or rocket is in flight |
| `--impact` | an impact effect carries a light |
| `--hit-shake` | the hit's palette flash is up |
| `--wait-transmission` | a squadmate's portrait is up |

### Cockpit and views

| Flag | Effect |
|---|---|
| `--mfd <0-5>` | The MFD screen at power-up, in `[F1]`–`[F6]` order. See [`formats/mfd.md`](../formats/mfd.md). |
| `--hdd [0\|1]` | Starts panned down to the Heads-Down Display: 0 the command display (`[F7]`, the default), 1 the damage detail (`[F8]`). See [`formats/heads-down-display.md`](../formats/heads-down-display.md). |
| `--hdd-damage <0-2>` | The damage screen's category: 0 structural (`[S]`), 1 internal (`[I]`), 2 weapons (`[W]`). |
| `--hdd-subject <0-4>` | The damage screen's subject, as the left and right arrows step it: 0 the player, 1-3 a squad slot, 4 the target. An empty squad slot starts on the player. |
| `--external` | Starts in the outside view, as if `[V]` were pressed at launch. See [`key-bindings.md`](../key-bindings.md#displays-and-views). |
| `--objectives` | Opens the `[F11]` objectives panel. See [`simulation/alert-panels.md`](../simulation/alert-panels.md#the-objectives-panel--obj_alrt-0045751c). |
| `--quit [0-19]` | Raises the `[Q]` mission-status alert. Without a number it shows the status the mission evaluates to; a number forces that `GNL_ALRT.STR` row, 0 and 1 being the pause panel's. |
| `--preferences` | Opens the `[F12]` preferences panel. See [`simulation/preferences.md`](../simulation/preferences.md). |
| `--controls` | Opens the preferences panel with the CONTROLS panel over it. |
| `--hit-shake` | Lands one hit on the cockpit, for the damage shake and its palette flash. See [`formats/cockpit-canopy-palette.md`](../formats/cockpit-canopy-palette.md#the-damage-shake). |

### Movement and weapons

| Flag | Effect |
|---|---|
| `--throttle <n>` | Starts with the throttle at *n*, clamped to ±1024 (full travel). See [`formats/cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#throttle-gauge). |
| `--heading <n>` | Turns the lower body to binary angle *n* (`0x4000` is a quarter turn) in place of the heading along the first leg of its route. |
| `--turret <twist> <pitch>` | Holds both turret axes for the whole run, each clamped to ±256. See [`simulation/torso-aim.md`](../simulation/torso-aim.md). |
| `--track` | Starts with Automatic Turret Tracking latched. It has nothing to hold without `--target`. |
| `--target` | Switches the scanner on and selects the nearest target after five ticks. See [`simulation/target-selection.md`](../simulation/target-selection.md). |
| `--weapon <1-10>` | Arms that weapon panel row, numbered as the row prints it. See [`formats/cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#weapon-hardpoint-rows). |
| `--link` | Links the row `--weapon` arms. |
| `--fire` | Holds the trigger down for the whole run. |
| `--impact` | Holds a `--screenshot` capture until an impact effect carries a light. Useful only with `--fire`. See [`formats/effect-lights.md`](../formats/effect-lights.md). |

### Squad orders

| Flag | Effect |
|---|---|
| `--hdd-pilot <0-2>` | The command display's selected comm box. The order list is greyed out until a pilot is selected. |
| `--hdd-order <0-7>` | The armed order, 0 Disengage, 1 Attack Enemy, 2 Defend Position, 3 Patrol Gridpoint, 4 Goto Gridpoint, 5 Join On Me, 6 Scan For Hostiles, 7 EMCON. |
| `--hdd-xmit` | Presses XMIT on the armed order, taking the map centre where the order needs a pick, and prints the squad's standing orders before and after the run. |
| `--flash-comm <0-5>` | The FLASH COMM row the cursor starts on. See [`formats/mfd.md`](../formats/mfd.md). |
| `--flash-comm-xmit` | Presses XMIT on that row once the mission is up. |
| `--wait-transmission` | Holds a `--screenshot` capture until a squadmate's portrait is up. Useful only with `--flash-comm-xmit`. |

The squadmate side of both is in [`simulation/ai-squadmates.md`](../simulation/ai-squadmates.md) and [`formats/heads-down-display.md`](../formats/heads-down-display.md).
