# Key bindings

The simulator's keyboard, by what each key does. Keys the manual does not list are marked **(not in the manual)**. The technical side — how a keystroke becomes a command code, and which handler answers it — is [`simulation/cockpit-input.md`](simulation/cockpit-input.md#keyboard-commands-are-scancodes); the joystick's bindings are [`simulation/joystick-input.md`](simulation/joystick-input.md).

"Keypad" means the numeric keypad with Num Lock off. The arrow keys and the keypad's arrows are the same keys to the game.

## Driving

| Key | What it does |
|---|---|
| `Up`, `Down` | Throttle forward and back. Holding `Down` takes the throttle through zero into reverse. |
| `Left`, `Right` | Steer. |
| Keypad `5` | All stop. |
| `J`, `K` | Twist the turret left and right. |
| `I`, `M` | Pitch the turret up and down. |
| `Backspace` | Centre the turret on the body and turn Automatic Turret Tracking off. |
| `\` | Centre the legs under the turret. |
| Keypad `+`, `-` | Throttle, in the RAZOR. |
| Keypad `7`, `9`, `1`, `3` | Steer and work the throttle together, diagonally. One of them overrides the other keys of the keypad and arrows. |

## Weapons

| Key                           | What it does                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Space`                       | Fire, for as long as it is held.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `1` … `0`                     | Select weapon row 1 to 10.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `Alt+1` … `Alt+0`             | Add that row to the current firing chain, or take it out.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `W`, `Alt+W`                  | Next and previous weapon.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `L`                           | Link the selected weapon to the identical one on the opposite hardpoint.                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `` ` `` (the manual's `[~]`)  | Next firing chain.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `=`, `-`, and keypad `+`, `-` | **(not in the manual)** Raise or lower the selected energy weapon's power level, one step a press. A weapon starts at 960 of 1200 and a step is 80, so three presses reach the top. For a weapon that charges up before it fires, such as the particle beam, a higher level is a stronger shot that takes longer to charge; its damage over time stays about the same. For a laser it changes nothing but the charge bar. In the RAZOR the keypad pair is the throttle instead. See [`simulation/weapon-firing.md`](simulation/weapon-firing.md#power-level--weaponmount_adjustpowerlevel-0040f48c). |

## Targeting and shields

| Key | What it does |
|---|---|
| `Enter` | Select the next target. |
| `'` | Select the nearest target. |
| `;` | Clear the target. |
| `Tab` | Step through the target's components, with a Targeting Pod. |
| `R` | Radar active or passive. |
| `Alt+R` | Radar range, in active mode. |
| `Alt+T` | **(not in the manual)** On the SCANNER screen, select the next target, as `Enter` does. |
| `T` | Automatic Turret Tracking on or off. Turning it off also centres the turret. |
| `[`, `]` | Move shield power toward the rear or the front. |

## Displays and views

| Key         | What it does                                                                                                                                                          |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `F1` … `F6` | MFD screen: STATUS, FLASH COMM, NAV MAP, SCANNER, TARGET, MISSILE CAM. Also returns from the Heads-Down Display.                                                      |
| `F7`, `F8`  | Heads-Down Display: command display, damage detail.                                                                                                                   |
| `F9`, `F10` | Look out of the left and right windows.                                                                                                                               |
| `Esc`       | Back to the forward view from a side window or the Heads-Down Display, and to the cockpit from the outside view.                                                      |
| `V`         | The outside view, and back to the cockpit.                                                                                                                            |
| `D`         | Status of the other HERCs: on the STATUS screen, cycles between your Herc and squadmates' Hercs. **(not in the manual)** On TARGET STATUS it selects the next target. |
| `Alt+D`     | Drop a nav marker where you stand.                                                                                                                                    |

On FLASH COMM:

| Key | What it does |
|---|---|
| `A`, `G`, `H`, `O`, `C`, `E`, `F` | Pick an order: attack my target, ignore my target, help me out, join on me, scan for hostiles, EMCON, fire at will / hold your fire. |
| `.`, `,` | Step through the orders. |
| `X` | Transmit. |
| `Alt` + an order's key | Pick and transmit that order from any screen. |

On the Heads-Down Display's command display:

| Key | What it does |
|---|---|
| `1`, `2`, `3` | Pick a squadmate, left to right. |
| `D`, `A`, `F`, `T`, `G`, `O`, `C`, `E` | Pick an order. |
| `,`, `.` | Step through the orders. |
| `Tab` | For ATTACK ENEMY or DEFEND POSITION, pick the next unit on the map it can take: a hostile you know of, or a friendly. |
| `Enter` | **(not in the manual)** Make the order's pick as a click would: on the unit last picked for ATTACK ENEMY or DEFEND POSITION, otherwise under the pointer. |
| `+`, `-` | Zoom the map. |
| Arrows | Scroll the map. |
| Keypad `5` | Put the map back on your HERC. |
| `X`, `Backspace` | Transmit, cancel. |

On the damage detail:

| Key | What it does |
|---|---|
| `S`, `I`, `W` | Show structural, internal and weapon systems. |
| `Up`, `Down` | Step through those three. |
| `Left`, `Right` | Step through the HERCs: yours, each squadmate's, then your target's. |

While either screen is down the arrows, the keypad's among them, are the display's, and no key steers, works the throttle or moves the turret ([`simulation/joystick-input.md`](simulation/joystick-input.md#the-keyboard)).

In the outside view:

| Key | What it does |
|---|---|
| Arrows | Swing the camera round the HERC and over it. |
| `Space` + `Up`, `Down` | Zoom in and out. |
| `Enter` | Hand the controls to the HERC, which you then drive and fire from outside, and back to the camera. |
| `N` | Look at the next HERC in your squad. The controls stay on the camera while it is not your own. |
| `V`, `Esc` | Back to the cockpit. |

A joystick does the same with its stick and trigger, and its CHASE VIEW button follows your HERC from behind instead ([`simulation/joystick-input.md`](simulation/joystick-input.md#the-buttons)). With the cockpit gone, so are the keys that work its displays: the weapon rows, `F1` to `F11`, `;`, `Tab`, `R` and the `Alt` order keys do nothing until you are back inside. `Tab` swaps the controls like `Enter`. The full behaviour is [`simulation/external-views.md`](simulation/external-views.md).

## Panels and the game

| Key | What it does |
|---|---|
| `Q` | How the mission stands, with a way out of it. |
| `P` | Pause. |
| `Ctrl+Q` | Leave the game. |
| `F11` | Mission objectives. |
| `F12`, `Alt+P` | Preferences. |
| `/` (the manual's `[?]`) | The on-line manual. |
| `Enter`, `Esc` | Close a panel with its first button. |
| `Alt+Enter` | Switch between full screen and a window. |

While an input tape plays back, `Ctrl+E` stops it; see [`formats/tap-input-tape.md`](formats/tap-input-tape.md).

## Developer keys

**(not in the manual)** Keys the programmers used for testing, which DBSIM answers only when it is started with [`-SPRUNKNOWN`](launch-options.md#developer-mode--sprunknown). `Alt+S` is the exception: it also works while a recording is being made or played back. The retail code behind each key is [`command-line.md`](command-line.md#-sprunknown-the-developer-keys).

The `Ctrl+Alt+number` keys choose how far the move and turn keys go; each mission starts on the `Ctrl+Alt+4` size. The move and turn keys act on the HERC the camera is on, which is your own until `Ctrl+N` or `Ctrl+P` moves the camera, and they take it straight through anything in the way. The arrows keep their ordinary job under `Alt` and `Ctrl`, so `Alt+Left`/`Right` and `Ctrl+Left`/`Right` also steer your HERC, and `Alt+Up`/`Down` also move its throttle. Holding any of these keys repeats it at the keyboard's repeat rate, so a held move key keeps moving.

| Key | What it does |
|---|---|
| `Alt+S` | Freezes and unfreezes the simulation. Everything stops but your own controls: the throttle still moves and your HERC still turns, fires and moves its turret, but it covers no ground, and what it fires waits in the air. Whether it can turn depends on where the throttle is. |
| `Alt+keypad +` | Runs the simulation for one frame, then freezes it. |
| `Alt+Up`, `Alt+Down` | Moves the HERC forward or back. |
| `Alt+Left`, `Alt+Right` | Moves the HERC sideways. |
| `Ctrl+Left`, `Ctrl+Right` | Turns the HERC on the spot. |
| `Ctrl+Alt+1` … `Ctrl+Alt+9` | Sets the size of each move and turn, from smallest to largest. |
| `Ctrl+N`, `Ctrl+P` | Moves the camera to the next or previous object in the mission, going out to the outside view first if you are in the cockpit. |
| `Ctrl+F` | Steps from the cockpit to the outside view, from there to a free camera, and from that back to the cockpit — of the object chosen with `Ctrl+N`/`Ctrl+P`, seen from its own eye. The free camera turns with the arrows and flies with `Space` + `Up`/`Down`. |
| `Ctrl+T` | Takes your HERC off the controls: it stops firing and ignores the steering, throttle and turret keys, and those keys drive the camera instead. `Ctrl+N` and `Ctrl+P` do this too. Press again to take the controls back. |
| `Ctrl+Alt+.`, `Ctrl+Alt+,` | Chooses which part of a machine `Ctrl+Alt+D` hits (30 to choose from). |
| `Ctrl+Alt+D` | Damages the chosen part of whatever the camera is on — your own HERC until the camera has been moved. |
| `Ctrl+Alt+N` | Hits a nearby Cybrid machine with a massive amount of damage. |
