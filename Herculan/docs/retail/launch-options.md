# Launch options (retail)

Earthsiege 2 is three programs. **ES.EXE** is the launcher. **VSHELL.EXE** is the front end: the menus, the campaign, the armory and the debriefing. **DBSIM.EXE** is the simulator: it runs one mission and exits. The launcher starts the front end, and every time the front end hands over a mission it starts the simulator, then the front end again when the mission ends.

Each program accepts options typed after its name, separated by spaces:

```
ES.EXE -s -L
```

Normally only ES.EXE is started by hand, and it passes the right options on to the other two. The front end and the simulator can be started directly with their own options, as long as `-eggplant` is among them.

Letter case matters for ES.EXE and the simulator: `-s` and `-S` are different options there. The front end ignores case and also accepts `/` in place of `-`.

This page is for players. The technical detail behind each option is in [`command-line.md`](command-line.md) and the docs it links. HERCULAN's own options are in [`../herculan/herculan-command-line.md`](../herculan/herculan-command-line.md). <!-- doc-lint: ok -->

## Launcher — ES.EXE

| Option | What it does |
|---|---|
| `-s` | Turns sound off, in both the front end and the simulator. |
| `-L` | Low-memory mode in the simulator; see `-l` [below](#memory). |
| `-r<name>` | Records every mission flown to `<name>.tap`; see [Recording and playback](#recording-and-playback). |
| `-a` | Turns the front end's movies off; see `-a` [below](#front-end--vshellexe). |
| `-SPRUNKNOWN` | Developer mode. The front end gets the mission picker (`-@`) and the simulator gets the developer keys (`-SPRUNKNOWN`), both described below. |
| `-X` | Only after `-SPRUNKNOWN`. Skips the front end entirely and flies the current mission over and over. |

Before starting anything, ES.EXE checks the computer's memory. With too little virtual memory it shows a message and quits; with under 8 MB of RAM it only warns.

The launcher also picks the music: the first mission after it starts plays CD track 2, the next track 3, and so on up to track 6, then back to 2.

The version 1.10 launcher also passes on the language chosen when the game was installed, as `-f`/`-g` to the front end and `-F`/`-G` to the simulator. Version 1.0's passes no language, so 1.0 always plays in English. See [`retail-builds.md`](retail-builds.md).

## Front end — VSHELL.EXE

| Option | What it does |
|---|---|
| `-eggplant` | Required. Without it the front end says it cannot be run directly, and stops. |
| `-f` | French in place of English. Version 1.0 carries no French menu or mission text. |
| `-g` | German in place of English. Version 1.0 carries no German menu or mission text. |
| `-s` | Turns sound off. |
| `-a` | Turns the movies off. |
| `-m` | Turns the mouse off. |
| `-k` | Turns the keyboard off. |
| `-X3`, `-X4` | Goes straight to the debriefing for the mission just flown, then on to the campaign. The launcher passes these after a mission. |
| `-@` | Before each campaign mission, leaves up a developer panel titled DEBUG that names the next mission, with a button to fly a different mission file instead. Without `-@` the front end dismisses that panel itself. |
| `-r` | Listed in the program's own help text as "Returning from sim", but does nothing: `-X` does that job. |
| `-l`, `-d` | Accepted, but do nothing. |
| `-v` | Listed in the help text as displaying the version number. It also turns sound off. |
| `-?` | Listed in the help text as displaying the list of options. It also turns sound off. |

Any `-e…` other than `-eggplant` is recognised too; see [Open](#open).

## Simulator — DBSIM.EXE

### Required

| Option | What it does |
|---|---|
| `-eggplant` | Required. Without it the simulator says it is not meant to be run on its own, and stops. |

### Display

| Option | What it does |
|---|---|
| `-v0` | Low resolution: the 3D view is drawn at 320×240 and scaled up. |
| `-v1` | 640×480, but with the low-resolution versions of the instruments and their artwork. |
| `-v2` | 640×480 with the high-resolution artwork throughout. Any higher digit is the same. |
| `-Z1` | Full screen. |
| `-Z0` or `-Z` | In a window. |
| `-b` | Switches to an older way of putting frames on screen that this version of the game never finished. Once a mission starts the picture stops updating; see [Open](#open). Do not use. |

Without `-v` or `-Z` the simulator uses the settings saved from its preferences panel. The saved setting can only choose between the `-v0` and `-v2` looks; `-v1` is reachable only from the command line. See [`simulation/preferences.md`](simulation/preferences.md#the-video-mode-and-full-screen-bytes) and [`simulation/cockpit-views.md`](simulation/cockpit-views.md#video-modes).

### Sound, music and language

| Option | What it does |
|---|---|
| `-s` | Starts without sound. |
| `-R<n>` | Chooses the CD music track for the mission. The track played is the remainder of *n* ÷ 5, plus 2: `-R0` plays track 2 (the default), `-R1` track 3, up to `-R4` for track 6. The launcher counts up through these one mission at a time. See [`simulation/audio.md`](simulation/audio.md#which-track-and-whether-there-is-one). |
| `-F` | French text and speech. Version 1.0 has French text only for its pop-up panels, and no French speech. |
| `-G` | German text and speech. Version 1.0 has German text only for its pop-up panels, and no German speech. |
| `-E` | Spanish. Both versions carry some Spanish text but no Spanish speech. |

### Cockpit

| Option | What it does |
|---|---|
| `-C<HERC>` | Shows the named HERC's cockpit whatever HERC is being piloted, e.g. `-CRAZOR`. Letter case does not matter. Recognised names: `ROADRUNNER`, `OUTLAW`, `RAPTOR2`, `TOMAHAWK`, `PATRIOT`, `PANTHER`, `SAMSON`, `COLOSSUS`, `APOCA`, `RAZOR`, `MAVERICK`, `OGRE`, `TEST3`; any other name is ignored. Only nine of those HERCs have cockpit artwork on the disc; see [Open](#open). |
| `-t<n>` | Puts pilot number *n* from the game's pilot list in the first squad message box on the heads-down display, in place of its usual pilot. See [`simulation/heads-down-display.md`](simulation/heads-down-display.md#squad-comm-boxes). |

### Memory

| Option | What it does |
|---|---|
| `-l` | Low-memory mode, for machines with little RAM. Cockpit artwork is loaded only when it is needed, sound gets half the usual memory, and the ground under bases is left unpainted. The simulator switches this on by itself on a machine with less than 12 MB. See [`simulation/audio.md`](simulation/audio.md#memory-budget-and-eviction) and [`rendering/terrain-texturing.md`](rendering/terrain-texturing.md#base-formation-pads). |

### Recording and playback

The simulator can record everything the player does during a mission to a file and play it back later. Three such recordings ship as demos, and **VIEW DEMO** on the main menu plays one of them. Playback runs as fast as the computer can draw frames, so on a modern machine a demo lasting minutes is over almost at once. `Ctrl+E` stops a playback. See [`formats/tap-input-tape.md`](formats/tap-input-tape.md#timing).

| Option | What it does |
|---|---|
| `-r<name>` | Records the mission to `<name>.tap`. |
| `-p<name>` | Plays `<name>.tap` back. |
| `-D` | Plays one of the shipped demo recordings, chosen at random. Player input stops it. This is what VIEW DEMO does. |
| `-d` | Checkpoints, placed after `-r`, `-p` or `-D`. Built to save snapshots of the game's state beside the recording and compare against them during playback, to catch a replay that has drifted from the original. In the released game it only creates an empty `<name>.dmp` when recording; see [Open](#open). |

### Developer mode: `-SPRUNKNOWN`

Turns on a set of keys the programmers used for testing: freezing the simulation, moving and turning your HERC by hand, moving the camera to other objects and damaging them. They are listed with the rest of the keyboard in [`key-bindings.md`](key-bindings.md#developer-keys).

### Other developer aids

| Option | What it does |
|---|---|
| `-B` | `Ctrl+B` stops the program for a debugger, which crashes it when no debugger is attached. Also stops `Alt+Enter` switching full screen while a recording plays back. |
| `-m` | Clears a second, monochrome debugging monitor, through a driver that does not ship with the game. Without that driver it does nothing. |

### Options with no effect

`-S`, `-P` and `-X<n>` are accepted and do nothing. `-T<n>`, `-V<n>`, `-W<n>`, `-a` and `-c` are accepted with no effect found, and neither the launcher nor the front end ever passes them; see [Open](#open).

## Open

- **Deferred:** whether the simulator's `-T<n>`, `-V<n>`, `-W<n>`, `-a` and `-c` do anything; see [`command-line.md`](command-line.md#open).
- **Deferred:** what the front end's non-`eggplant` `-e…` options do.
- **Deferred:** what `-C` does with the four names that have no cockpit artwork, and what `-E` does without Spanish speech files.
- **Deferred:** where the front end's `-v` and `-?` text appears; it is written to standard output, which a Windows program normally does not have.
- **Deferred:** whether anything in the simulator runs the `-d` checkpoints; see [`formats/tap-input-tape.md`](formats/tap-input-tape.md#open).
- **Deferred:** `-b` has not been tried against retail; the expected behaviour on each Windows family is in [`simulation/cockpit-views.md`](simulation/cockpit-views.md#open).
