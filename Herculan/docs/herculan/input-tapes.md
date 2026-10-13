# Input tapes: `--play` and `--record`

How HERCULAN plays and records DBSIM's `.TAP` input tapes. The format, and what retail's `-p`, `-r` and `-D` do with it, is [`../retail/formats/tap-input-tape.md`](../retail/formats/tap-input-tape.md); the flags are in [`herculan-command-line.md`](herculan-command-line.md).

`--play <tape>` is `-p`, `--demo` is `-D` and `--record <tape>` is `-r`; `--demo` with `--play` plays that tape in demo mode. `HercWorks.Core`'s `InputTapeTransformer` reads and writes the format, byte-identical on the three retail tapes.

## Playback

A tape is a path or a stem found in the install's `TAPES` folder. `Input.InputTapePlayer` decodes frames and lays out the bundle, and the host feeds each frame's keystrokes, pointer, stick and trigger through the same handlers live input takes. The tape's keystrokes reach the host's key handlers through `TapeKeys`, which maps each set-1 scancode back to a key.

Where it differs from retail, by this engine's choice:

- **Paced.** Each simulation frame is held for its own recorded `SimTickDelta`, so a tape plays in the time it was recorded in; a panel frame is held for an estimated 6 ms.
- **The install is left alone.** The bundle is unpacked over a copy of `DATA` in the temp folder rather than over `DATA` itself, with `-p`'s preference reconciliation and the install's `keyjoy.cfg`, as [above](../retail/formats/tap-input-tape.md#the-bundle).
- **One keystroke per host frame.** Every recorded key press is held down for exactly one host frame with a frame of nothing between, so each auto-repeat press is a fresh key-down edge to the handlers, as each is a fresh command to retail's dispatcher.
- **The original's per-tick steps.** `SimMath.ScalePerTickStep` is off for the replay, so the acceleration and shield steps apply once per frame, as retail's do at whatever frame rate the tape was recorded at.
- **A panel's tick runs whole.** When a frame's own input raises a panel, the engine runs that frame's entire tick after the panel comes down, where retail has already ticked the effect pools before it.
- **Divergence is logged.** The host prints each frame a panel goes up or comes down on, and `InputTapePlayer.InferredPanelSpans` lists the runs of one repeated delta that mark the recording's own panels; a mismatch is the replay leaving the recording.

After the tape, `--play` hands the controls back to the player on the engine's own timestep, and `--demo` ends the mission. `[Ctrl+E]` stops either; under `--demo` any key does.

## Recording

`--record <tape>` writes `<tape>.tap`, the extension forced as `-r` forces it. `Input.InputTapeRecorder` writes the file as it goes, as `-r` does:

- **The bundle**, at startup, before any panel writes its options back. It comes from the folder the mission was loaded from: the mission file itself, its lance file by slot (`script3.dat` takes `player3.mec`), and the other five from beside it. A file that is not there is an empty entry.
- **The capability block**, ahead of the first frame, from the stick in use at that moment. The hat flag is written as `0x10`, the value the retail tapes carry.
- **One frame per engine tick**, with `SimWorld.TickDelta` (81) as its delta, and **one per host frame a modal panel is up for**, repeating the last tick's delta.

A frame's held input is the host frame's own: the four axes before Backturn, the trigger, and the stick's eight raw buttons. Its discrete input rides the next frame written, which is the first tick of the host frame or, when the host frame ran none, a later one:

- **Key presses** are the key-down edges of the keyboard as polled at the top of the host frame, the same state the handlers poll, so a key pressed and released between two frames is not recorded, and each of a held key's auto-repeats (`KeyRepeat`), as retail records them. `[Esc]`, `[/]` and `[Alt+Enter]` record no repeats, since the host acts on those only as they go down. The first press is the command word and the rest go in the command queue; Alt and Ctrl held with a key are its `0x200` and `0x400` bits, and the modifier keys themselves, and every release, are left out.
- **Mouse events** are converted to the game's screen space, the inverse of what playback does with them.
- **The stick button that fired**, as its bit when it is one of buttons 1-4 and is not bound to FIRE, and the hat's four view bits.

Nothing is recorded while the debug UI has the keyboard or the pointer.

While recording, the live loop follows playback's order where ordinary live play does not, so that the tape replays the ticks the recording ran:

- **The mission alert** goes up straight after the tick that decided it, and the rest of that host frame's ticks do not run. Ordinary live play raises it at the top of the next host frame.
- **A panel raised by a frame's own input** — `[Q]`, `[F11]`, `[F12]` — leaves that frame on the tape as one that ticks, and its tick runs once the panel comes down, as [playback](#playback) runs it. Ordinary live play drops that tick.

## What a replay of an engine recording does not reproduce

Beyond what the [bundle](../retail/formats/tap-input-tape.md#the-bundle) leaves to the playing machine and the buttons the format [does not record](../retail/formats/tap-input-tape.md#the-stream), these are the engine's own:

- **Tick length.** The engine's tick is 40 ms and `SimTickDelta` 81 is 39.55 ms, which is what a replay advances the simulation's clock by. That clock gains 1.1% on the recording, and missile lock timing (`MechObject.Lock`) reads it.
- **Pointer precision.** Mouse positions are rounded to the game's 640x480 screen, where the live pointer is in window pixels, so a throttle-slider drag or a click on a widget's edge can land differently.
- **Held keys.** A key the host acts on for every frame it is held, rather than on its down edge, replays as held for one frame. The heads-down map's arrow scroll is one.
- **Repeated presses.** Presses from host frames that ran no tick share one frame, so a key pressed in two of them replays as one press.
- **The stick on the CONTROLS panel.** The panel reads the device directly, so presses made on it are not on the tape.
- **The single-step key.** `Alt+keypad +` ticks the simulation once while recording; playback has no single step and runs that frame as a frozen tick.
- **The staging flags.** `--heading`, `--throttle`, `--weapon`, `--link`, `--track` and `--target` set the mission's opening state, which the bundle does not carry.
- **The device map.** A bipolar throttle is `data\herculan-joystick.cfg`'s setting, which the bundle does not carry, so the playing machine's applies.
- **The stick's shape.** The capability block is written once, so a stick plugged in or out later in the recording is not on it.

## Comparing against retail

`tools/scripts/patch_dbsim_tape_pacing.py` patches a retail `DBSIM.EXE` to play tapes in real time, for side-by-side comparison with HERCULAN. It holds each simulation frame for its recorded delta and each panel frame for 6 ms. It is this project's modification, not retail behaviour.

## Open

- **Open:** whether an engine recording replays in step with itself on a real install, and whether retail DBSIM plays one. Neither has been run.
