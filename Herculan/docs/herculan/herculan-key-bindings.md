# Key bindings: Herculan.Engine.Host

The keys the engine adds to retail's. Every retail key works as [`../retail/key-bindings.md`](../retail/key-bindings.md) describes, alongside these:

| Key | What it does |
|---|---|
| `Esc` | In a mission's forward view, raise the menu bar with the Debug, Tweaks and Settings panels; in the front end, raise it with Tweaks and Settings, except while a movie or the briefing map's opening plays or a field is being typed into. Press again to back out. |
| Left mouse drag | With the Mouse-controlled outside view tweak, swing the outside view round the HERC. |
| `PrtScn` | While full screen, copy the frame to the clipboard in place of Windows' own capture, which can show a stale frame of a full-screen game; with `Alt`, `Ctrl`, `Shift` or `Win` held the key is Windows'. `--save-prtscn` also saves it ([`herculan-command-line.md`](herculan-command-line.md#screenshots-and-staged-state)). |

`Alt+Enter` toggles full screen in a mission while a panel such as pause is up. `Alt+Tab` leaves the game full screen behind the window switched to, where retail leaves full screen.

The tweak replaces the outside view's controls: the mouse swings the camera, the HERC stays under your control throughout, and the cockpit's keys keep working. `Esc` still returns to the cockpit.

During a tape replay, `Esc`'s menu bar and `Ctrl+E` stay with the player's own keyboard and every other key comes from the tape; under `--demo` any key ends the demo.

## Developer keys

`--developer` turns on retail's [developer keys](../retail/key-bindings.md#developer-keys), with one difference: the move and turn keys only move and turn. Under `Alt` or `Ctrl` the arrows neither steer nor move the throttle, so a sideways move looks like one rather than being hidden in a turn.
