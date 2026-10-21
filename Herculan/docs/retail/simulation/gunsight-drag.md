# The gunsight drag (DBSIM.EXE)

The one mouse input that reaches the machine rather than a widget: hold the right button over the front window and move the pointer, and the turret turns toward it, faster the further the pointer is from where the button went down. Retail carries the whole mechanism, but its mouse layer loses the held right button before the pointer can move, so in play it never acts ([below](#the-term-stays-zero-in-play)). How a click reaches a widget is [`cockpit-input.md`](cockpit-input.md)'s; how the stick and keyboard reach the same axes is [`joystick-input.md`](joystick-input.md)'s.

## The term

`Sim_PollPlayerInput` (`00460764`) adds the drag to the twist and pitch it hands the machine after its camera branch and its ordinary branch alike ([`joystick-input.md`](joystick-input.md#while-the-camera-has-the-controls)), so it reaches the two turret ticks, and in a RAZOR `Razor_ApplyFlightInput` as rudder and throttle ([`razor-flight.md`](razor-flight.md#axis-remapping)). It is added to `Sim_PollPlayerInput`'s own locals and never written back to the device block, whose axes are what a tape records ([`../formats/tap-input-tape.md`](../formats/tap-input-tape.md)).

It reads the frame's device block from `Input_PollDeviceBlock` (`0045ba8c`): the pointer at `+0x02`/`+0x06` and the right button at `+0x0b`.

| `+0x0b` | Armed (`0049f702`) | Each tick |
|---|---|---|
| clear | either | disarms and zeroes both terms |
| set | no | arms when the pointer lies inside the complex's rect, inclusive, and records it at `004d2afa`/`004d2afe`; the terms stay zero |
| set | yes | twist term = `dx²/64`, pitch term = `dy²/32`, `dx` and `dy` being the pointer's distance from the recorded point, positive to the right and up |

The terms are 32-bit squares added to the 16-bit axes, and nothing clamps them on the way to the ticks. The twist term reaches a stick's full deflection, `0x100`, 128 pixels from the press, and the pitch term about 91 pixels from it.

## The rect

`+0x1f5` of `CockpitViewInstance` is the gunsight complex, and the rect is its own `+4`..`+0x10`: `.GAU` 1088-1103 as `PanelGauge_Ctor` (`00438b20`) copies it after `GauRovingGunsight_ApplyCoordShift` (`0043e118`) has doubled it at 640x480 ([block at 1088](../formats/gau-cockpit-layout.md#gau-block-at-1088)). That is `0,0 - 640,234` or `0,0 - 640,314` by herc, the whole window above the console.

On a view change `CockpitWidgets_TranslateForView` (`0043271c`) moves the root, the edge strips and the widgets listed at the view's `+0x246`, and not the complex, so the rect is compared with the pointer's screen position whatever the view: the same screen rows arm it in a glance, on the Heads-Down Display and in the external view. A right press held outside the rect arms where the pointer first enters it.

## The term stays zero in play

`+0x0b` is bit 1 of the button word `Input_ReadMouseState` (`0045bc58`) copies from `Mouse_State` (`006c5fd4`), the record `MouseDriver_GetState` (`00480728`) returns. `Mouse_DispatchEvent` (`0048083c`) rebuilds that word on every mouse message from bits 0 and 1 of what `Mouse_WndProcHook` (`004808ec`) passes it. The hook passes the right button as 2 only for `WM_RBUTTONDOWN`; for every other message it forwards wParam's `MK_RBUTTON` doubled, as 4, which the dispatcher drops:

| Message | Passed | Right bit |
|---|---|---|
| `WM_MOUSEMOVE` | `(wParam & 1) + (wParam & 2) * 2` | clear |
| `WM_LBUTTONDOWN` | `(wParam & 2) * 2 + 1` | clear |
| `WM_LBUTTONUP` | `(wParam & 2) * 2` | clear |
| `WM_RBUTTONDOWN` | `(wParam & 1) + 2` | set |
| `WM_RBUTTONUP` | `wParam & 1` | clear |

The bit therefore stands from a right press until the next mouse message, a move included, so the drag arms on a press held still and disarms as soon as the pointer moves. A term other than zero needs a right release and a press at another point inside one tick.

During a replay the device block's pointer and buttons are zero ([`../formats/tap-input-tape.md`](../formats/tap-input-tape.md)), so the drag never arms.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The drag arms over the gunsight's click surface, the area at `.GAU` 1148 | `CockpitViewInstance+0x1f5` is the whole complex, and its `+4`..`+0x10` is the complex's own extent from `.GAU` 1088. The click surface is the complex's child 0, which holds the 1148 rect ([`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex)) |
