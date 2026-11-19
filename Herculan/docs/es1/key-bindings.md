# Key bindings

The simulator's keyboard, by what each key does. Keys the manual's keyboard commands (pages 64 and 65) do not list are marked **(not in the manual)**. How a keystroke becomes a command word, and which handler answers it, is [`simulation/input.md`](simulation/input.md#the-keyboard) and [`simulation/main-loop.md`](simulation/main-loop.md#commands--sim_dispatchcommand-00059df8); the joystick's configurations are [`simulation/input.md`](simulation/input.md#the-joystick-configurations).

A key is the one in its place on a US keyboard, whatever the layout prints on it: the simulator reads the keyboard's scancodes from the hardware ([the keyboard](simulation/input.md#the-keyboard)), so on a French AZERTY keyboard the command display's ATTACK ENEMY, `A`, is the key marked Q. The grey arrows, `Home`, `Page Up`, `End`, `Page Down`, `Insert` and `Delete` are the keypad's keys in their places, grey `Enter` is `Enter`, and the right `Ctrl` and `Alt` are the left ones ([extended keys](simulation/input.md#the-command-word)). `Shift` changes no key: the command word drops it.

A key held down acts again at the keyboard's repeat rate, as if pressed over and over ([repeat](simulation/input.md#the-command-word)). The driving and turret keys, `Space` and `Enter` are read as held for as long as they are down instead ([the axis keys](simulation/input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec)).

## Driving

| Key | What it does |
|---|---|
| `Up`, `Down` | Throttle forward and back. Holding `Down` takes the throttle through zero into reverse ([the throttle](simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)). |
| `Left`, `Right` | Steer. |
| Keypad `5` | All stop **(not in the manual)** ([the cockpit](simulation/mech.md#the-cockpit)). |
| `J`, `K` | Twist the turret left and right. |
| `I`, `M` | Pitch the turret up and down. |
| `Alt` with `J`, `K`, `I`, `M` | Twist or pitch the turret at full speed. The turret keys alone move it at a quarter of that **(not in the manual)** ([the keyboard axes](simulation/input.md#the-keyboard-axes--input_buildkeyboardaxes-0004c818)). |
| `Backspace` | Center the turret on the body, unless Automatic Turret Tracking is holding it on a target ([the player's controls](simulation/main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)). |
| Keypad `7`, `9`, `1`, `3` | Steer and work the throttle together, diagonally, overriding the arrows and the rest of the keypad **(not in the manual)**. |
| `Insert` | With a joystick, switch it between `MOVEMENT CONTROL`, which drives the HERC, and `TURRET CONTROL`, which aims the turret ([the stick mode](simulation/input.md#the-stick-mode)). |
| `Up`, `Down`, `Left`, `Right` | With the joystick on `MOVEMENT CONTROL`, aim the turret instead, at a quarter speed, or at full speed with `Alt` held **(not in the manual)** ([reading the player's input](simulation/input.md#reading-the-players-input--input_readplayerinput-0004cb14), steps 3 and 6). |
| `Ctrl+1` | Make the throttle forward only, then switch it between forward only and reverse only. In reverse only, `Up` backs the HERC up and `Down` brings it to a stop **(not in the manual)** ([the throttle](simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)). |
| `Ctrl+I`, `Ctrl+M` | With `WCS` on in the `CONTROLS` panel, step the throttle forward and back **(not in the manual)** ([the axis keys](simulation/input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec)). |

## Weapons

| Key | What it does |
|---|---|
| `Space` | Fire, for as long as it is held. |
| `1` … `9` | Select weapon row 1 to 9. On the ECM's row, switch the ECM on or off instead ([the cockpit's keys](simulation/cockpit-panel.md#keys-and-the-view-device), [the pod row](simulation/cockpit-weapon-gauges.md#the-pod-row)). |
| `Alt+1` … `Alt+9` | Add that row to the current firing chain, or take it out ([arming, chaining and linking](simulation/weapon-mounts.md#arming-chaining-and-linking)). |
| `W`, `Alt+W` | Next and previous weapon. |
| `L` | Link the selected weapon with the identical one it pairs with, or unlink them ([the console buttons](simulation/cockpit-weapon-gauges.md#the-console-buttons)). |
| `` ` ``, `` Alt+` `` (the manual's `~`) | Next and previous firing chain. |
| `B` | Next firing chain, leaving the selected weapon selected even when it is not in that chain **(not in the manual)** ([arming, chaining and linking](simulation/weapon-mounts.md#arming-chaining-and-linking)). |
| `=`, `-`, keypad `+`, `-` (the manual's `+` and `-`) | Raise or lower the selected energy weapon's power level, one step a press. |

## Targeting and shields

| Key | What it does |
|---|---|
| `Tab`, `T` | Select the next target. `T` only while the MFD is on screen ([the MFD's keys](simulation/mfd-display.md#clicks-and-keys)). |
| `Alt+T` | Automatic Turret Tracking on or off ([the console buttons](simulation/cockpit-weapon-gauges.md#what-the-buttons-do)). |
| `R` | Switch the radar between active and passive. |
| `D` | On the SCANNER screen, switch the radar between active and passive **(not in the manual)**. |
| `Alt+R` | Change the radar range. |
| `[`, `]` | Move shield power toward the rear or the front ([the cockpit](simulation/mech.md#the-cockpit)). |

## Displays and views

Outside the outside view, with the cockpit's keys ([the cockpit's keys](simulation/cockpit-panel.md#keys-and-the-view-device)):

| Key | What it does |
|---|---|
| `F1` … `F6` | Show STATUS, FLASH COMM, NAV MAP, SCANNER, TARGET or MISSILE CAM on the MFD; from the Heads-Down Display or a side window, the view goes back to the forward view. |
| `F7`, `F8` | Show the command display or the damage detail on the Heads-Down Display, and pan down to it. |
| `F9`, `F10` | Look out of the left and right windows. |
| `Esc` | From the forward view, down to the Heads-Down Display. Back to the forward view from a side window or the Heads-Down Display, and to the cockpit from the outside view. |
| `V` | Switch to the outside view, and back to the cockpit ([commands](simulation/main-loop.md#commands--sim_dispatchcommand-00059df8)). |

On the Heads-Down Display the cockpit hands every key it does not take itself to the display, so the number keys and `Tab` work it and do not select weapons or targets there.

### On FLASH COMM

| Key | What it does |
|---|---|
| `A`, `G`, `H`, `O`, `C`, `E`, `F` | Pick an order: ATTACK MY TARGET, IGNORE MY TARGET, HELP ME OUT!, JOIN ON ME, SCAN FOR HOSTILES, EMCON, FIRE AT WILL / HOLD YOUR FIRE ([the MFD's keys](simulation/mfd-display.md#clicks-and-keys)). |
| `,`, `.` | Step through the orders ([picking a row](simulation/mfd-flash-comm.md#picking-a-row)). |
| `X` | Transmit the order. |
| `Alt` with `A`, `G`, `H`, `O`, `C`, `E`, `F` | Pick and transmit the same orders from any screen ([the order helpers](simulation/mfd-flash-comm.md#the-order-helpers)). |

### On the command display

| Key | What it does |
|---|---|
| `1`, `2`, `3` | Pick squadmate 1, 2 or 3 to give an order to, also from the damage detail ([presses](simulation/heads-down-display.md#presses)). |
| `D`, `A`, `F`, `T`, `G`, `O`, `C`, `E` | Pick an order: DISENGAGE, ATTACK ENEMY, DEFEND POSITION, PATROL GRIDPOINT, GOTO GRIDPOINT, JOIN ON ME, SCAN FOR HOSTILES, EMCON ([the keys](simulation/hdd-command-screen.md#clicks-and-keys)). |
| `,`, `.` | Step through the orders. |
| `Tab` | For ATTACK ENEMY or DEFEND POSITION, pick the next unit on the map it can take: a hostile you know of, or a friendly ([Tab's step](simulation/hdd-command-screen.md#tabs-step)). |
| `Enter` | Make the order's pick as a click would: the unit last picked for ATTACK ENEMY or DEFEND POSITION, otherwise whatever is under the pointer **(not in the manual)**. |
| `=`, `-`, keypad `+`, `-` | Zoom the map in and out ([the display's keys](simulation/heads-down-display.md#keys)). |
| `Up`, `Down`, `Left`, `Right` | Scroll the map. |
| Keypad `5` | Put the map back on your HERC **(not in the manual)**. |
| `X`, `Backspace` | Transmit the order, or cancel it. |

### On the damage detail

| Key | What it does |
|---|---|
| `S`, `I`, `W` | Show structural, internal and weapon systems, also from the command display ([the display's keys](simulation/heads-down-display.md#keys)). |
| `Up`, `Down` | Step through structural, internal and weapon systems **(not in the manual)** ([presses](simulation/heads-down-display.md#presses)). |
| `Left`, `Right` | Step through the HERCs: yours, each squadmate's, then your target's. |

### In the outside view

| Key | What it does |
|---|---|
| `Up`, `Down`, `Left`, `Right` | Swing the camera around the HERC and over it ([the camera](simulation/external-views.md#the-camera)). |
| `Space` with `Up`, `Down` | Zoom in and out. |
| `Tab`, `Enter` | Hand the controls to the HERC, which you then drive and fire from outside, and back to the camera ([commands](simulation/main-loop.md#commands--sim_dispatchcommand-00059df8); `Enter` is not in the manual). |
| `N` | Look at the next HERC in your squad. |
| `V`, `Esc` | Back to the cockpit. |

## Panels and the game

| Key | What it does |
|---|---|
| `Q` | Show how the mission stands, with a way out of it ([commands](simulation/main-loop.md#commands--sim_dispatchcommand-00059df8)). |
| `P` | Pause. |
| `Ctrl+Q`, `Ctrl+Esc` | Leave the game, after `QUIT TO DOS?`. |
| `F11` | Show the mission objectives ([the cockpit's keys](simulation/cockpit-panel.md#keys-and-the-view-device)). |
| `Alt+P`, `F12` | Open the preferences ([raising the panel](simulation/preferences.md#raising-the-panel--preferencespanel_raise-00057820)). |
| `Enter` | On a panel, press the selected button, which is the first until another is selected ([the panels' keys](simulation/alert-panels.md#keys--alertpanel_handleevent-00046670)). |
| `Esc` | On a panel, press its cancel button. |
| `Tab`, `Alt+Tab` | On a panel, select the next or the previous button. |
| `Insert` | On a panel, with `WCS` on in the `CONTROLS` panel, select the next button **(not in the manual)**. |
| `Ctrl+E` | Stop an input tape that is playing back **(not in the manual)** ([playback](simulation/input.md#recording-and-playback)). |
| `Alt+S` | While an input tape plays back or records, freeze the game, and press again to go on **(not in the manual)** ([commands](simulation/main-loop.md#commands--sim_dispatchcommand-00059df8), [the tick](simulation/main-loop.md#the-tick--sim_maintick-00059700)). |

## Developer keys

`Ctrl+Alt` with keypad `4`, keypad `6` and `\` have the first squadmate say its pilot file's lines one at a time ([the display's keys](simulation/heads-down-display.md#keys)). `Ctrl+Alt+P` deals the HERC its outnumbered damage, `Ctrl+Alt` with `1` to `9` sets the step of the next four keys, and, while the axis keys are off, `Alt` with keypad `8`, `2`, `4` and `6` moves the HERC along its own axes and `Ctrl` with keypad `4` and `6` turns it ([the cockpit](simulation/mech.md#the-cockpit), [the simulator's two subscribers](simulation/input.md#the-simulators-two-subscribers)). Whether a key reaches each of them is [Open](#open).

## Open

- **Open:** what `Z` does for the player. It reaches the weapon manager's code `0x2c`, which wakes every mount's capacitor ([arming, chaining and linking](simulation/weapon-mounts.md#arming-chaining-and-linking)); in ES2 the same code shows nothing, since the charge bar reads its slider back over what it writes ([ES2's power level](../retail/simulation/weapon-firing.md#power-level--weaponmount_adjustpowerlevel-0040f48c)).
- **Open:** whether the developer keys reach their handlers: [the display's](simulation/heads-down-display.md#open) and [the HERC's](simulation/mech.md#open) Open items.
