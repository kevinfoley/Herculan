# Roadmap — work not yet done

Everything the HERCULAN Engine does not implement yet, in one place. This is the counterpart of [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md), which records things that *are* implemented but behave differently from retail. If a feature is missing entirely it belongs here; if it is present and wrong it belongs there.

Each entry names the doc that owns the subject. **That doc is authoritative** for how much is reverse-engineered and how much is still unknown — this file only tracks that the porting work is outstanding.

## Reverse-engineered, not ported

The mechanism is understood; what is left is engine work.

- **The triple turret does not shoot.** Four of the five structure classes now fill their `+0x18` tick slot; type `0x22`'s three-turrets-from-one-object tick (`004045c8`) is the one left, and it needs the 11-`short` weapon descriptor table at `DAT_004a9640` dumped out of the data section. → [`docs/simulation/structure-behaviour.md`](docs/simulation/structure-behaviour.md)
- **A machine's crudest LOD roots are never drawn.** Root selection is ported, but the roots that compact their node numbering — the crudest one to three of each chassis — are excluded, because drawing them puts APOCA's upper body on a knee. The original composes every root through root 0's pose array too, so by the binary it should do the same; retail does not visibly do so, and what reconciles that is not yet found. **Settle that before changing anything here**: it decides whether the truncation is a divergence to lift or retail behaviour to match. → [`docs/formats/mech-shape-drawing.md`](docs/formats/mech-shape-drawing.md#the-pose-array-is-root-0s)
- **Terrain raycast, swept-volume mode.** Only thin-ray mode is ported; the swept-volume mode (movement collision) is not, because nothing in the engine needs it yet. → [`docs/formats/terrain-heightmap.md`](docs/formats/terrain-heightmap.md)
- **An engine recording does not replay exactly.** `--record` writes tapes `--play` replays, but the tick length, pointer rounding, held keys and a handful of inputs the tape does not carry let a replay drift from its recording. `-d`'s checkpoint file, which would catch that drift, is not ported. → [`docs/engine/input-tapes.md`](docs/engine/input-tapes.md#what-a-replay-of-an-engine-recording-does-not-reproduce)
- **A structure's kill credit and starting damage.** A destroyed structure credits no kill to whoever destroyed it, and the mission's starting condition is applied to machines but not to structures. → [`docs/simulation/structure-behaviour.md`](docs/simulation/structure-behaviour.md#open)
- **Three parts of a homing round's steer:** the node it aims at, the ECM wobble, and the gate on a round whose owner is piloted locally. → [`docs/simulation/rockets.md`](docs/simulation/rockets.md#open)
- **Weapon readiness and charging.** A launcher with no missile lock should read red and be skipped, and the charge bar's round trip leaves an energy weapon's charge target a unit or two low; neither is ported. → [`docs/simulation/weapon-mounts.md`](docs/simulation/weapon-mounts.md#open), [`docs/simulation/weapon-firing.md`](docs/simulation/weapon-firing.md#open)
- **The RAZOR's gun convergence.** Its guns are never converged on the range to its target. → [`docs/simulation/razor-flight.md`](docs/simulation/razor-flight.md#open)
- **Two hit-detection paths:** testing a structure's node-placed clusters in the node's frame, and `Sim_RaycastShapeList`, the bulk line-of-sight query over the structure list. → [`docs/simulation/hit-detection.md`](docs/simulation/hit-detection.md#open)
- **The impact-effect owner rule and the 40-effect pool limit.** → [`docs/simulation/impact-effects.md`](docs/simulation/impact-effects.md#open)
- **The "enemy detected" callout.** → [`docs/simulation/target-selection.md`](docs/simulation/target-selection.md#open)
- **A machine standing inside a building is drawn by its own radius.** Retail files it with the building for drawing. → [`docs/simulation/mech-locomotion.md`](docs/simulation/mech-locomotion.md#open)
- **Four shape-drawing details:** the back fill and line colours, the face-skip flag 5120, the `TSBSPPart` tree walk, and one-vertex polys painted as a pixel. → [`docs/formats/dts-texture-binding.md`](docs/formats/dts-texture-binding.md#open)
- **The terrain walk's object culling.** An object filed under a cell the walk does not visit is still drawn, the per-class object draw distances are missing, and a ground shape does not paint over an object drawn earlier where they overlap. → [`docs/formats/terrain-drawing.md`](docs/formats/terrain-drawing.md#open), [`docs/simulation/ground-shapes.md`](docs/simulation/ground-shapes.md#open)
- **The RAZOR's heads-down 3D view and altitude scale.** → [`docs/formats/cockpit-views.md`](docs/formats/cockpit-views.md#open), [`docs/formats/cockpit-gunsight-hud.md`](docs/formats/cockpit-gunsight-hud.md#open)
- **The cockpit's two system buttons and the fullscreen toggle.** The right-hand button is also retail's third way into the online manual. → [`docs/formats/cockpit-input.md`](docs/formats/cockpit-input.md#open), [`docs/engine/online-manual.md`](docs/engine/online-manual.md#open)
- **The MFD's squad-roster step**, mode 0's arm of SELECT/TARGET. → [`docs/formats/mfd.md`](docs/formats/mfd.md#open)
- **A throttle lever bound to the turret pair pitching the turret while the camera has the controls.** → [`docs/formats/joystick-input.md`](docs/formats/joystick-input.md#open)
- **The `.hmp` MIDI path and reading `SOUND.CFG`.** → [`docs/formats/audio.md`](docs/formats/audio.md#open)

## Reverse-engineering still open

The engine cannot be faithful here until the original is understood.

- **How many times DBSIM has advanced its generator before any given roll.** The algorithm, the 56-entry seed table and both cursor starts are ported, so the two generators produce identical streams from the same starting point — but a roll's result depends on its position in that stream, and this engine does not yet make the same draws in the same order. Replay parity needs the call history matched, which is really a question about tick order, not about the generator. → [`docs/simulation/random-generator.md`](docs/simulation/random-generator.md)

## The shell front end
`--shell` draws the frame every tab screen shares — the tiled backdrop, the square button and the eight captioned tabs, hit-tested, latching on the six tabs that latch, gated by campaign mode and switching palette per tab — plus the startup sequence and the main menu it brings up, whose `INSTANT ACTION` flies the next demo mission, `START NEW GAME` starts a campaign from its registration screen, `CONTINUE GAME` loads the current game and `VIEW DEMO` plays a demo tape, its practice screen, whose `Begin Mission` flies the lit training mission, and the save, weapons, repair, build, armory and crew screens behind tabs 0 to 6, weapon fitting included, with the squad panel down the left of four of them, and the mission tab's campaign map and its briefing with its map, whose `Rock & Roll >` launches the mission, and the way back from it — the debrief's accounting and its report, then the next mission's load or the offer to replay the one that ended the campaign — all with the shell's music and click sounds, and its movies: the intro, the campaign map's with its location picture, the briefing's and `CREDITS`. The widget paints are ported onto an indexed software canvas, so a further screen is layout, text and hit-testing rather than new drawing code. What is missing:
- **The startup's `Performance Note` box.** Retail shows it once; the shell does not. → [`docs/shell/screen-layout.md`](docs/shell/screen-layout.md#the-main-menu)
- **Indeo 3's 8-bit pixels and half-pel motion vectors.** No shipped frame uses them. → [`docs/formats/indeo3.md`](docs/formats/indeo3.md#open)
- **Three shell controls:** a content button's caption nudging down while pressed, the mission screen arrows' auto-repeat, and the developer's mission-name dialog. → [`docs/shell/screen-layout.md`](docs/shell/screen-layout.md#open)
- **A movie that will not open.** The shell skips it, where retail asks for the CD. → [`docs/shell/screen-layout.md`](docs/shell/screen-layout.md#the-shells-movies)

## Debugging features
- **A launch option that disables the AI.** Every unit but the player's stays stationary, though it can still be damaged and destroyed. → [`docs/engine/herculan-command-line.md`](docs/engine/herculan-command-line.md#developer)
- **Editing the current `script.dat` in the Editor**, chiefly moving Cybrid and player spawn points, to set up test scenarios quickly. The scope, and the RE question that gates adding records, are in [`docs/engine/handoff-editor-mission-editing.md`](docs/engine/handoff-editor-mission-editing.md). → [`docs/formats/script-dat.md`](docs/formats/script-dat.md)

## HercWorks editors
- **Edit the configuration files and zone headers in HercWorks UI.** Core reads and writes `data\keyjoy.cfg` (`KeyjoyTransformer`), `data\prefs.cfg` (`PrefsTransformer`), `data\drive.cfg` (`DriveTransformer`) and `dat\zoneNNNN.dat` (`ZoneDatTransformer`), but the UI has no form for any of them. Two things to settle first: `keyjoy.cfg`'s writer drops the comments a retail file ships with, and `prefs.cfg` should be written back as the simulator does — a read-modify-write of the bytes the form changed, never a fresh 54-byte dump. → [`docs/formats/joystick-input.md`](docs/formats/joystick-input.md), [`docs/simulation/preferences.md`](docs/simulation/preferences.md), [`docs/formats/terrain-heightmap.md`](docs/formats/terrain-heightmap.md)
