# Joystick configuration: `data\herculan-joystick.cfg`

How HERCULAN maps a modern stick onto the retail game's joystick bindings. What the retail bindings are, and how DBSIM applies them, is [`../retail/simulation/joystick-input.md`](../retail/simulation/joystick-input.md). The flags named here are in [`herculan-command-line.md`](herculan-command-line.md#settings-and-input-devices).

## Why the file exists

Retail's `prefs.cfg` says which *game axis pair* each control feeds and which *action* each button fires, and nothing about hardware. It reads X, Y, Z and R in that fixed order, which worked because a 1996 gameport stick had exactly those controls in exactly that order.

A modern stick does not. The host reads it through GLFW, whose axes are **positional**: an ordered array of floats with no HID usages, in whatever order the driver enumerated. On a HOTAS the throttle and the twist grip commonly come out swapped against the order retail assumes. No API fixes this. HID has no "throttle" usage, and a vendor labels a throttle `Z`, `Rz` or `Slider` as it pleases, so `joyGetPosEx`, DirectInput and Raw Input all just relay that choice. A resting position does not identify a lever either: a throttle parked mid-travel reads 0.00, the same as a self-centring twist.

So `prefs.cfg` keeps owning what each control **does**, byte for byte as retail writes it, and `data\herculan-joystick.cfg` owns which piece of hardware each control **is**. Retail never reads it. It is an INI in `keyjoy.cfg`'s shape, with one section per kind of device ([below](#one-file-many-devices)). For a device it has no section for, a gamepad gets its own map ([A gamepad's own controls](#a-gamepads-own-controls)) and a joystick retail's order over its reported counts.

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

**A gamepad works without setting anything up, in a HERC and in a RAZOR.** While the device being read is a gamepad, the engine uses the gamepad's own CONTROLS settings in place of the ones in `prefs.cfg`, which keeps the player's joystick settings for a joystick and for the original game:

| Control | HERC | RAZOR |
|---|---|---|
| Left stick | steer; forward and back move the throttle up and down, and it stays where it is left | rudder and throttle |
| Right stick | turret: up and down aim it, left and right twist it | pitch and roll |
| D-pad | views: up and down for the Heads-Down Display, left and right for the side windows | the same |
| Right trigger | FIRE | FIRE |
| Left trigger | NEXT WEAPON | the same |
| A, X, Y | TARGET, LINK WEAPON, NEXT CHAIN | the same |
| B | radar active or passive, as `R` ([The radar toggle](#the-radar-toggle)) | the same |
| Left and right bumpers | SHIELDS REAR, SHIELDS FRONT | the same |

Back, Start and the sticks' clicks are not among the eight buttons: Back raises the menu bar, Start the pause menu, the left stick's click the squad orders and the right stick's the cockpit's screens ([`herculan-key-bindings.md`](herculan-key-bindings.md)). The Controller window does not offer them, and a map saved naming one reads it as never pressed. B is not among them either, being the radar toggle, and BUTTON 8 has no control.

That is the map **Defaults** gives a gamepad in the Controller window, with CONTROLS rows of JOYSTICK on movement, THROTTLE and RUDDER on turret and HAT on views for a HERC, and JOYSTICK on turret, THROTTLE and RUDDER on movement and HAT on views for a RAZOR. With THROTTLE off THROTTLE neither machine reads a throttle lever, so the left stick moves the throttle at a rate ([`mech-locomotion.md`](../retail/simulation/mech-locomotion.md)). The map inverts the throttle slot, which is the right stick's up and down, and retail flips that slot in a RAZOR alone, so the stick's up and down work the two machines in opposite senses: a HERC's turret like a shooter's view, a RAZOR's nose like a flight stick ([Open](#open)). `InvertRazorPitch` turns the RAZOR's the other way without touching the HERC's ([Options retail does not have](#options-retail-does-not-have)).

On the CONTROLS panel the four axis rows show these settings and take no click. The eight button rows can be changed as usual, and DONE saves them in the `[Gamepad]` section as `HercControls` and `RazorControls`, not in `prefs.cfg`. RECOMMEND puts back the actions above. NEXT WEAPON is not on the list a RAZOR's BUTTON 3 offers, so once that row is changed in a RAZOR only RECOMMEND brings it back.

## One file, many devices

**Each kind of controller keeps its own settings, so switching between them needs no setting up again.** The file has one section per kind of device:

| Section | Serves |
|---|---|
| `[Gamepad]` | every gamepad, since all of them share the layout above |
| `[Joystick <GUID>]` | one model of joystick |
| `[Joystick]` | any joystick without a section of its own |

The GUID is the one GLFW gives a device. It names the model, not the unit: two of the same model share it. Which USB port the device is in does not change it, but how it connects can: on Linux a Bluetooth connection has a different GUID from a USB one, and on Windows a Bluetooth device that reports no vendor or product ID gets one made from its name. A section also records the device's name, `Name = …`, to keep the file readable; nothing reads it back. A file written before per-device sections has one `[Joystick]` section, which goes on serving every joystick.

**Which device a mission reads** when more than one is connected is the first, in GLFW's order, that the file has a section for, and otherwise the first connected one.

## Setting up a stick

The **Controller** window on the [Shift+Esc] menu bar edits the file, in the front end or in a mission. It shows every axis, button and hat the device reports as it moves, and beside each control the value the game reads through the map being edited. **Detect** binds whichever control moves next: an axis has to travel half its range from where it rested when Detect was pressed, so a trigger resting at one end is not taken for the control meant. A device input that fills more than one row shows red in each, and hovering it names the other rows: the same button, or the same direction of an axis, on two rows; the same axis on two axis rows; or an axis on an axis row that a button row also uses. Two button rows on opposite directions of one axis do not count. The **Device** list chooses which connected device to set up, and the window says which section its settings go to; an edit made to one device is kept while another is chosen. **Mouse emulation**, above the device's own settings and shown only while a gamepad is connected, chooses how a gamepad works the pointer in the front end and the mission editor ([`herculan-key-bindings.md`](herculan-key-bindings.md#gamepad-in-the-front-end)): **Cursor control** is the left or the right stick, **Cursor speed** how fast it moves the pointer, from 25% to 400% of the default pace, and **Clicking** is the right trigger as the left button and the left trigger as the right, or the other way round. All three serve every gamepad whichever device is chosen, and are kept in `[Gamepad]` as `CursorStick` and `LeftClickTrigger`, each `Left` or `Right`, and `CursorSpeed`, a whole percentage; a file without them has the right stick at 100% and the right trigger. **Defaults** is the map that runs when the file has no section for the device, a gamepad's own or a joystick's from its reported counts, and puts Mouse emulation back to the right stick at 100% and the right trigger. **Save** writes the device's section, and any other device edited since the window opened, keeping the rest of the file; a mission in progress reads the stick through it at once, the front end takes Mouse emulation at once, and the editor reads it as it opens. While the window is up a mission's stick drives nothing, and after it closes the stick stays quiet until every button is let go, so a button pressed to bind it does not also fire its action.

From the command line:

1. `--joystick-probe` prints each axis and button as it moves, which is how to find out what the driver calls each control.
2. `--write-joystick-map` writes the map in force to the device's section of `data\herculan-joystick.cfg`, for editing.

Both are off by default; the map file is the player's to own. `Herculan/examples/herculan-joystick.cfg` is a worked map for a Thrustmaster T.Flight in PC mode.

**Check the mode switch on the base of a HOTAS before reading anything into its numbering.** A T.Flight's PC position numbers buttons and axes the way the retail format expects: the trigger is button 1 and the throttle is the throttle. Its PS3 position renumbers both, which looks exactly like a wrong mapping.

A rebinding made on the CONTROLS panel is written to the install's `prefs.cfg` as the panel closes, as retail does. `--no-write-prefs` prevents that.

## The radar toggle

Retail gives a stick no radar action: none of the action codes a BUTTON row takes switches it ([`../retail/simulation/joystick-input.md`](../retail/simulation/joystick-input.md#the-buttons)). The map adds a control of its own, `RadarToggle`, beside the eight BUTTON rows rather than among them, so neither `prefs.cfg` nor the CONTROLS panel sees it. The Controller window sets it on its **Radar toggle** row, to a device button or to an axis, as a BUTTON row is set. While it is held the cockpit reads `R` as held, so it switches the radar between ACTIVE and PASSIVE as `R` does, does nothing where `R` does nothing, such as the outside view, and a recording takes it as `R`. It is quiet whenever the stick is: while the menu bar, the pause menu or a radial is up, and after them until every button is let go. A gamepad has it on B; a joystick has it unmapped until the window sets it.

## What the map cannot express

The limits of retail's twelve binding bytes are limits here too, because keeping `prefs.cfg` readable by the retail simulator means keeping them: no second hat, no ninth button, and four axis slots.

An axis number, though, is the map's own and not the format's, so a slot can be pointed at any axis the device has. `Rudder = 4` reads the device's fifth axis into the rudder slot, which is how a HOTAS whose rudder paddles and twist grip are separate controls gets the paddles instead of the twist. What cannot be expressed is a fifth axis *as well as* the other four.

## Options retail does not have

- **An axis as a button.** Any of the eight buttons can be driven by an axis instead of a device button, written `Button1 = axis5+` (or `axis5-`). It counts as pressed while the axis is more than halfway from its centre toward that end; a trigger, which rests at its − end, does that past three-quarters of its pull. Retail reads buttons from the device's button mask alone. On a button row, the Controller window's Detect takes either a new button press or an axis moved half its travel, toward the end it moved to.

The three below are off by default.

- **`HatDiagonals`** resolves a hat diagonal into its two cardinal directions. Retail drops a diagonal.
- **`BipolarThrottle`** reads the throttle lever centre-zero: the middle of the travel is idle, forward of it is forward and aft of it is reverse, each half covering the whole range. Retail's lever is end-to-end, idle at one stop and full at the other, with the direction set by `CHANGE DIRECTION` and the cockpit slider. `CHANGE DIRECTION` still reverses the lever in this mode, which is of no use but costs nothing. The Razor needs neither mode, since its throttle is already signed across the whole range. Nor does a gamepad, whose controls read no lever ([A gamepad's own controls](#a-gamepads-own-controls)), and the Controller window greys the option for one.
- **`InvertRazorPitch`** reverses the RAZOR's pitch from the device, whichever control the bindings route to it, on top of that control's own invert; the window's **Invert RAZOR pitch**. The per-axis inverts serve both machines, and with the gamepad defaults the right stick's up and down pitch a HERC's turret and a RAZOR's nose, so this is what gives the two separate senses. The keyboard's pitch keys are left alone. Retail has no invert of any kind.

## Open

- **Open:** which way the right stick's up and down move a HERC's turret and a RAZOR's nose with the gamepad defaults. The senses above follow from GLFW reporting a gamepad stick pushed up as negative, which has not been checked in play; if both come out reversed, the throttle slot's invert is the one setting to flip. The two radials and the pause menu read the sticks on the same assumption ([`herculan-key-bindings.md`](herculan-key-bindings.md#squad-orders-on-a-gamepad)).
