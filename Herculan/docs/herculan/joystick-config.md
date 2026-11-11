# Joystick configuration: `herculan-joystick.cfg`

How HERCULAN maps a modern stick onto the retail game's joystick bindings. What the retail bindings are, and how DBSIM applies them, is [`../retail/simulation/joystick-input.md`](../retail/simulation/joystick-input.md). The flags named here are in [`herculan-command-line.md`](herculan-command-line.md#settings-and-input-devices).

## Why the file exists

Retail's `prefs.cfg` says which *game axis pair* each control feeds and which *action* each button fires, and nothing about hardware. It reads X, Y, Z and R in that fixed order, which worked because a 1996 gameport stick had exactly those controls in exactly that order.

A modern stick does not. The host reads it through GLFW, whose axes are **positional**: an ordered array of floats with no HID usages, in whatever order the driver enumerated. On a HOTAS the throttle and the twist grip commonly come out swapped against the order retail assumes. No API fixes this. HID has no "throttle" usage, and a vendor labels a throttle `Z`, `Rz` or `Slider` as it pleases, so `joyGetPosEx`, DirectInput and Raw Input all just relay that choice. A resting position does not identify a lever either: a throttle parked mid-travel reads 0.00, the same as a self-centring twist.

So `prefs.cfg` keeps owning what each control **does**, byte for byte as retail writes it, and `herculan-joystick.cfg` owns which piece of hardware each control **is**. Retail never reads it. It is an INI in `keyjoy.cfg`'s shape, with one section per kind of device ([below](#one-file-many-devices)). For a device it has no section for, a gamepad gets its own map ([A gamepad's own controls](#a-gamepads-own-controls)) and a joystick retail's order over its reported counts.

## Gamepads

**An Xbox-compatible pad has the same numbering on every computer.** GLFW recognises such a pad, and Steam Input's virtual one, as a gamepad and reads it in its standard gamepad layout instead of the driver's order:

| Axis | Control | Button | Control |
|---|---|---|---|
| 0, 1 | left stick X, Y | 0-3 | A, B, X, Y |
| 2, 3 | right stick X, Y | 4, 5 | left and right bumper |
| 4, 5 | left and right trigger | 6-8 | Back, Start, Guide |
| | | 9, 10 | left and right stick click |
| | | 11-14 | d-pad up, right, down, left |

The d-pad is also hat 0, since a gamepad has no hat of its own. A trigger is an axis, resting at −1, and is bound to a button as an axis (see [Options retail does not have](#options-retail-does-not-have)). Any other device is a joystick, numbered in its driver's order.

### A gamepad's own controls

**A gamepad works without setting anything up, in a HERC and in a RAZOR.** A gamepad is always in Custom mode ([Setting up a stick](#setting-up-a-stick)), and its default profile gives it these controls, which leaves `prefs.cfg`'s CONTROLS settings to a joystick in Original mode and to the original game:

| Control | HERC | RAZOR |
|---|---|---|
| Left stick | steer; forward and back move the throttle up and down, and it stays where it is left | rudder and throttle |
| Right stick | turret: up and down aim it, left and right twist it | pitch and roll |
| D-pad | views: up and down for the Heads-Down Display, left and right for the side windows | the same |
| Right trigger | FIRE | FIRE |
| Left trigger | NEXT WEAPON | the same |
| A, X, Y | TARGET, LINK WEAPON, NEXT CHAIN | the same |
| B | radar active or passive, as `R` | the same |
| Left and right bumpers | SHIELDS REAR, SHIELDS FRONT | the same |

Back raises the menu bar, Start the pause menu, the left stick's click the squad orders and the right stick's the cockpit's screens ([`herculan-key-bindings.md`](herculan-key-bindings.md)); in the front end, Back and Start do so whatever the profile says. The left stick moves a HERC's throttle at a rate ([`mech-locomotion.md`](../retail/simulation/mech-locomotion.md)). The right stick's up and down work the two machines in opposite senses: a HERC's turret like a shooter's view, a RAZOR's nose like a flight stick ([Open](#open)).

## One file, many devices

**The file is the player's, not an install's.** It is kept in HERCULAN's settings folder, `%APPDATA%\Herculan` on Windows and the platform's user-config directory elsewhere, and both games read and save the same one, so a controller set up for one game is set up for the other. While that file is missing, a game that finds a copy in its install's `data` folder, where earlier builds kept it, copies it there; an install's copy is read for nothing else.

**Each kind of controller keeps its own settings, so switching between them needs no setting up again.** The file has one section per kind of device:

| Section | Serves |
|---|---|
| `[Gamepad]` | every gamepad, since all of them share the layout above |
| `[Joystick <GUID>]` | one model of joystick |
| `[Joystick]` | any joystick without a section of its own |

The GUID is the one GLFW gives a device. It names the model, not the unit: two of the same model share it. Which USB port the device is in does not change it, but how it connects can: on Linux a Bluetooth connection has a different GUID from a USB one, and on Windows a Bluetooth device that reports no vendor or product ID gets one made from its name. A section also records the device's name, `Name = …`, to keep the file readable; nothing reads it back. A file written before per-device sections has one `[Joystick]` section, which goes on serving every joystick.

**Which device a mission reads** when more than one is connected is the first, in GLFW's order, that the file has a section for, and otherwise the first connected one.

## Setting up a stick

The **Controller** window on the [Shift+Esc] menu bar edits the file, in the front end or in a mission. A column on its right shows every axis, button and hat the device reports as it moves: a bar per axis, the buttons lit while held, a gamepad's d-pad as its hat 0 rather than as four buttons, and each hat's direction. In Original mode each control's row also shows the value the game reads through the map being edited. **Detect** binds whichever control moves next: an axis has to travel half its range from where it rested when Detect was pressed, so a trigger resting at one end is not taken for the control meant. A device input that fills more than one row shows red in each, and hovering it names the other rows: the same button, or the same direction of an axis, on two rows; the same axis on two axis rows; or an axis on an axis row that a button row also uses. Two button rows on opposite directions of one axis do not count. The **Device** list chooses which connected device to set up, and the window says which section its settings go to; an edit made to one device is kept while another is chosen. **Mouse emulation**, above the device's own settings and shown only while a gamepad is connected, chooses how a gamepad works the pointer in the front end, on the Heads-Down Display and in the mission editor ([`herculan-key-bindings.md`](herculan-key-bindings.md#gamepad-in-the-front-end)): **Cursor control** is the left or the right stick, **Cursor speed** how fast it moves the pointer, from 25% to 400% of the default pace, and **Clicking** is the right trigger as the left button and the left trigger as the right, or the other way round. All three serve every gamepad whichever device is chosen, and are kept in `[Gamepad]` as `CursorStick` and `LeftClickTrigger`, each `Left` or `Right`, and `CursorSpeed`, a whole percentage; a file without them has the right stick at 100% and the right trigger. **Defaults** is the map that runs when the file has no section for the device, a gamepad's own or a joystick's from its reported counts, and puts Mouse emulation back to the right stick at 100% and the right trigger. **Save** writes the device's section, and any other device edited since the window opened, keeping the rest of the file; a mission in progress reads the stick through it at once, the front end takes Mouse emulation at once, and the editor reads it as it opens. Pressing what raises the menu bar while Detect listens, a gamepad's Back or a Custom profile's menu bar input in an Earthsiege 2 mission, stops Detect and leaves the window up, with a message that the control opens the menu bar and cannot be rebound. While the window is up a mission's stick drives nothing, and after it closes the stick stays quiet until every button is let go, so a button pressed to bind it does not also fire its action.

**Mode**, under the Device list, chooses for each joystick between **Original**, where the game's own CONTROLS panel says what each control does and the window shows the rows above, and **Custom**, where the window says it ([`plan-controller-profiles.md`](plan-controller-profiles.md)). A gamepad has no Mode: it is always Custom, and `[Gamepad]` has no `Mode` line. In Custom the window shows, for a HERC and on a tab of its own for a RAZOR:

- **Axes**: which device axis drives each function, a HERC's steering, throttle, turret twist and turret pitch, a RAZOR's roll, pitch, yaw and throttle, each with its own Invert, Deadzone and Detect, and where the axis stands for its function, inverted and zero inside the deadzone.
- **Throttle axis**: how the throttle function's axis reads, as moving the throttle up and down, as a lever idle at one end, or, a HERC's alone, as a lever idle in the middle.
- **Actions**: one row per device input, a button, an axis pushed past halfway toward one end, or one hat direction, and the action it sends, chosen from a list under the Keys window's section names, with Automatic turret tracking under piloting. **Action list** chooses how long that list is, "Common" each time the window opens or another device is chosen. "Common" leaves out what an axis usually works (the throttle, steering, the turret and the diagonals), the numbered weapon rows, the MFD's and the Heads-Down Display's screens by name, Other HERCs on STATUS, the online manual, and the FLASH COMM, command display, damage detail and outside view sections; "All" adds them, the command display's and the damage detail's sections for a joystick only. Neither lists the chain toggles, the side windows' and the Heads-Down Display's "or back" views, or picking an order by name on FLASH COMM or the command display. A RAZOR's list leaves out Center turret, Center legs and Automatic turret tracking, and a HERC's the RAZOR's throttle up and down. An action bound already shows as itself whichever list is chosen. **Add** appends a row and listens for its input, taking a hat pushed in a new direction first, then a new button press, then an axis moved half its travel; a gamepad's d-pad is offered as hat 0 alone, and its Back not at all, being the menu bar's. A gamepad's buttons and d-pad are named as printed on an Xbox 360, Xbox One or Series, PlayStation 3, 4 or 5, Switch Pro or Steam Deck pad, the family told from the name the driver gives it, its sticks' axes by side and direction, and its triggers as printed (LT and RT, L2 and R2, or ZL and ZR); any other device's buttons and axes are named by number. **Remove** deletes a row. A row with no input or no action is not saved. An action the running game lacks shows greyed and can still be chosen, for the other game.

An input on two rows of one machine, two functions on one axis, or a row on either end of an axis a function uses shows red, and hovering it names the others; of two rows on one input the first is saved. A device whose section has no Custom settings shows its default profile, and is saved without any until something is changed. In Custom, **Defaults** puts that profile back and leaves Original's map and the mode as they are.

**In an Earthsiege 2 mission a Custom profile drives the machine the mission pilots, a HERC's half or a RAZOR's.** Its axes go through fixed CONTROLS rows, chosen by how its throttle is read, so they reach the control law as a stick's do and a throttle read as a lever is a lever there ([`joystick-input.md`](../retail/simulation/joystick-input.md)). Fire, MFD next screen and Reverse the throttle lever are BUTTON 1, 3 and 4 of those rows, and the four views are the hat under VIEWS, so a recording takes them as a stick's; any other action goes in as its key, held while its input is, so it does what its key does there and a recording takes the key. The menu bar, the pause menu and the two radials answer the profile's inputs in place of a gamepad's Back, Start and stick clicks. A radial is aimed with a gamepad's stick, so one bound on a joystick opens nothing ([Open](#open)). While a modal panel is up only Fire reaches it, as its trigger, besides a gamepad's own panel controls ([`herculan-key-bindings.md`](herculan-key-bindings.md#retails-panels-on-a-gamepad)); while the view is down on the Heads-Down Display a gamepad's sticks and triggers work the display and reach none of the profile's functions or actions ([`herculan-key-bindings.md`](herculan-key-bindings.md#the-heads-down-display-on-a-gamepad)). The CONTROLS panel shows the fixed rows and takes no click but DONE.

**In *Metaltech: Earthsiege* the window is on the shell's menu bar**, since a mission has no menu bar, and the next mission reads what Save writes. It shows what the game port reads ([what each configuration reads](../es1/simulation/joystick.md#what-each-configuration-reads)): the four axis rows and BUTTON 1–4. The axis rows are named for their slots, on a gamepad too, because what each port axis does is decided by the joystick model the CONTROLS panel chooses. There is no hat row, because the port puts the hat on no line until it is known which hat direction each of its hat bytes carries ([`joystick.md`'s Open](../es1/simulation/joystick.md#open)), and no Deadzone or Hat diagonals, which the port does not apply. "Game reads" shows the position the port reads, out of 128. Radar toggle, Centre-idle throttle and Invert RAZOR pitch are Earthsiege 2's alone. In Custom it shows the HERC's axes and actions alone. The settings the window does not show stay in the file as they are, for Earthsiege 2.

From the command line:

1. `--joystick-probe` prints each axis and button as it moves, which is how to find out what the driver calls each control.
2. `--write-joystick-map` writes the map in force to the device's section of the file, for editing.

Both are off by default; the map file is the player's to own. `Herculan/examples/herculan-joystick.cfg` is a worked map for a Thrustmaster T.Flight in PC mode.

**Check the mode switch on the base of a HOTAS before reading anything into its numbering.** A T.Flight's PC position numbers buttons and axes the way the retail format expects: the trigger is button 1 and the throttle is the throttle. Its PS3 position renumbers both, which looks exactly like a wrong mapping.

A rebinding made on the CONTROLS panel is written to the install's `prefs.cfg` as the panel closes, as retail does. `--no-write-prefs` prevents that.

## The radar toggle

Retail gives a stick no radar action: none of the action codes a BUTTON row takes switches it ([`../retail/simulation/joystick-input.md`](../retail/simulation/joystick-input.md#the-buttons)). The map adds a control of its own, `RadarToggle`, beside the eight BUTTON rows rather than among them, so neither `prefs.cfg` nor the CONTROLS panel sees it. The Controller window sets it on its **Radar toggle** row, to a device button or to an axis, as a BUTTON row is set. While it is held the cockpit reads `R` as held, so it switches the radar between ACTIVE and PASSIVE as `R` does, does nothing where `R` does nothing, such as the outside view, and a recording takes it as `R`. It is quiet whenever the stick is: while the menu bar, the pause menu or a radial is up, and after them until every button is let go. It is Original mode's, and a joystick has it unmapped until the window sets it; in Custom, a gamepad among them, the radar is the `RadarMode` action, on a gamepad's B by default.

## What the map cannot express

The limits of retail's twelve binding bytes are limits here too, because keeping `prefs.cfg` readable by the retail simulator means keeping them: no second hat, no ninth button, and four axis slots.

An axis number, though, is the map's own and not the format's, so a slot can be pointed at any axis the device has. `Rudder = 4` reads the device's fifth axis into the rudder slot, which is how a HOTAS whose rudder paddles and twist grip are separate controls gets the paddles instead of the twist. What cannot be expressed is a fifth axis *as well as* the other four.

## Options retail does not have

- **An axis as a button.** Any of the eight buttons can be driven by an axis instead of a device button, written `Button1 = axis5+` (or `axis5-`). It counts as pressed while the axis is more than halfway from its centre toward that end; a trigger, which rests at its − end, does that past three-quarters of its pull. Retail reads buttons from the device's button mask alone. On a button row, the Controller window's Detect takes either a new button press or an axis moved half its travel, toward the end it moved to.

The three below are off by default.

- **`HatDiagonals`** resolves a hat diagonal into its two cardinal directions. Retail drops a diagonal.
- **`BipolarThrottle`** reads the throttle lever centre-zero: the middle of the travel is idle, forward of it is forward and aft of it is reverse, each half covering the whole range. Retail's lever is end-to-end, idle at one stop and full at the other, with the direction set by `CHANGE DIRECTION` and the cockpit slider. `CHANGE DIRECTION` still reverses the lever in this mode, which is of no use but costs nothing. The Razor needs neither mode, since its throttle is already signed across the whole range. The option is Original mode's, so a gamepad, always in Custom, has no use for it; in Custom, a HERC's throttle axis can be read as a lever idle in the middle instead ([Setting up a stick](#setting-up-a-stick)).
- **`InvertRazorPitch`** reverses the RAZOR's pitch from the device, whichever control the bindings route to it, on top of that control's own invert; the window's **Invert RAZOR pitch**. The per-axis inverts serve both machines, and with the gamepad defaults the right stick's up and down pitch a HERC's turret and a RAZOR's nose, so this is what gives the two separate senses. The keyboard's pitch keys are left alone. Retail has no invert of any kind.

## Open

- **Open:** how a joystick in Custom mode aims a radial its profile binds, a joystick having no stick of a gamepad's.
- **Open:** whether GLFW puts a Switch Pro Controller's buttons by place, its printed B as button 0, which the window's names for it assume; and which names the drivers give each family, which decide whether a pad is recognised. Neither has been checked on hardware.
- **Open:** which way the right stick's up and down move a HERC's turret and a RAZOR's nose with the gamepad defaults. The senses above follow from GLFW reporting a gamepad stick pushed up as negative, which has not been checked in play; if both come out reversed, the throttle slot's invert is the one setting to flip. The two radials and the pause menu read the sticks on the same assumption ([`herculan-key-bindings.md`](herculan-key-bindings.md#squad-orders-on-a-gamepad)).
