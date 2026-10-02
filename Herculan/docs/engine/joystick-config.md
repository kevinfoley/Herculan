# Joystick configuration: `data\herculan-joystick.cfg`

How HERCULAN maps a modern stick onto the retail game's joystick bindings. What the retail bindings are, and how DBSIM applies them, is [`formats/joystick-input.md`](../formats/joystick-input.md). The flags named here are in [`herculan-command-line.md`](herculan-command-line.md#settings-and-input-devices).

## Why the file exists

Retail's `prefs.cfg` says which *game axis pair* each control feeds and which *action* each button fires, and nothing about hardware. It reads X, Y, Z and R in that fixed order, which worked because a 1996 gameport stick had exactly those controls in exactly that order.

A modern stick does not. The host reads it through GLFW, whose axes are **positional**: an ordered array of floats with no HID usages, in whatever order the driver enumerated. On a HOTAS the throttle and the twist grip commonly come out swapped against the order retail assumes. No API fixes this. HID has no "throttle" usage, and a vendor labels a throttle `Z`, `Rz` or `Slider` as it pleases, so `joyGetPosEx`, DirectInput and Raw Input all just relay that choice. A resting position does not identify a lever either: a throttle parked mid-travel reads 0.00, the same as a self-centring twist.

So `prefs.cfg` keeps owning what each control **does**, byte for byte as retail writes it, and `data\herculan-joystick.cfg` owns which piece of hardware each control **is**. Retail never reads it. It is an INI in `keyjoy.cfg`'s shape. When it is absent, the device's reported counts supply retail's order as the default.

## Setting up a stick

1. `--joystick-probe` prints each axis and button as it moves, which is how to find out what the driver calls each control.
2. `--write-joystick-map` writes the map in force to `data\herculan-joystick.cfg`, for editing.

Both are off by default; the map file is the player's to own. `Herculan/examples/herculan-joystick.cfg` is a worked map for a Thrustmaster T.Flight in PC mode.

**Check the mode switch on the base of a HOTAS before reading anything into its numbering.** A T.Flight's PC position numbers buttons and axes the way the retail format expects: the trigger is button 1 and the throttle is the throttle. Its PS3 position renumbers both, which looks exactly like a wrong mapping.

A rebinding made on the CONTROLS panel is written to the install's `prefs.cfg` as the panel closes, as retail does. `--no-write-prefs` prevents that.

## What the map cannot express

The limits of retail's twelve binding bytes are limits here too, because keeping `prefs.cfg` readable by the retail simulator means keeping them: no second hat, no ninth button, and four axis slots.

An axis number, though, is the map's own and not the format's, so a slot can be pointed at any axis the device has. `Rudder = 4` reads the device's fifth axis into the rudder slot, which is how a HOTAS whose rudder paddles and twist grip are separate controls gets the paddles instead of the twist. What cannot be expressed is a fifth axis *as well as* the other four.

## Options retail does not have

Both are off by default.

- **`HatDiagonals`** resolves a hat diagonal into its two cardinal directions. Retail drops a diagonal.
- **`BipolarThrottle`** reads the throttle lever centre-zero: the middle of the travel is idle, forward of it is forward and aft of it is reverse, each half covering the whole range. Retail's lever is end-to-end, idle at one stop and full at the other, with the direction set by `CHANGE DIRECTION` and the cockpit slider. `CHANGE DIRECTION` still reverses the lever in this mode, which is of no use but costs nothing. The Razor needs neither mode, since its throttle is already signed across the whole range.
