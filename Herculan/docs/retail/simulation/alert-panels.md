# Alert panels (DBSIM.EXE)

The modal panels DBSIM puts up over a frozen cockpit, all built on one base. This doc owns the family's shared behaviour and the look of three of its members; what the mission-status alert decides is [`mission-objectives.md`](mission-objectives.md#the-status-alert--gnl_alrt-00455934)'s, and the other two members are [`preferences.md`](preferences.md)'s.

| Panel | Built by | Raised by | Owned by |
|---|---|---|---|
| status alert, `gnl_alrt` | `StatusAlertPanel_Ctor` (`00455934`) | [Q]; the status poll; the player's death sequence | [look](#the-status-alerts-text-and-layout) here, [behaviour](mission-objectives.md#the-status-alert--gnl_alrt-00455934) in the mission doc |
| pause, `EXIT EARTHSIEGE?` | `PausePanel_Ctor` (`004561c0`) | [P], [Ctrl+Q] | [here](#the-pause-panel--004561c0) |
| objectives, `obj_alrt` | `ObjectivesPanel_Ctor` (`0045751c`) | [F11] | [here](#the-objectives-panel--obj_alrt-0045751c) |
| preferences, `prf_alrt`; controls, `ctl_alrt` | | [F12] | [`preferences.md`](preferences.md) |

## What the family shares

Every panel derives from one base, `AlertPanel_CtorBase`, and runs its own near-identical copy of one modal loop. `AlertPanel_Enter` (`00454630`) pauses both message ports, saves the input state block, latches joystick buttons 0-3 (what that does to a held trigger is [`joystick-input.md`](joystick-input.md#a-latched-first-row-holds-the-axes)'s) and copies the screen under the panel into a backing store; the loop then polls input, hands it to the panel's handler in vtable slot `+0x10`, paints the widgets and presents (`AlertPanel_Present`, which also watches the global quit flag `DAT_004d2582` each pass and tears down any panel still up). **The loop never calls `Sim_MainTick`**, so the cockpit behind a panel is frozen, not merely undrawn; what that does to input is [`preferences.md`](preferences.md#preferences-and-controls-dbsimexe)'s.

**Layout.** A panel's constructor writes its rect block into `.bss` once, as `value << VideoMode_?CoordShift`, so every number is an authored 320-wide coordinate doubled. Rects are panel-local — (0, 0) is where the plate is blitted — and `AlertPanel_CenterRect` (`00454f34`) centres the panel on the 640x480 screen by its **declared** size, which is not the art's. The preferences panel is the one member that gives an origin instead.

**Widgets.** The title draws in `title` and the buttons in `active` at rest and `pushed` while held, in every member. A button's paint (`PanelButton_Paint`, `00454ff8`) overwrites its caption label's font from its own four-entry table every time, whatever the label was built in. A button's plate art is larger than its rect and is blitted at the rect's origin, so it overhangs. A click reaches the panel through `PanelButton_OnClick` (`00455080`), which forwards to the panel's vtable `+0x0c` slot.

**Text.** Each member's `.STR` path comes from `Language_StringFilePath` (`0045ef00`): `str\` in English, `stf\` and `stg\` in French and German ([`../retail-builds.md`](../retail-builds.md#how-a-language-is-chosen)). v1.0's `stf\` and `stg\` tables translate an earlier design of the panels: their preferences panel offers MUSIC, SOUNDS, RADIO, HORIZON, SKY, GROUND and SHADOWS, with no detail levels; their controls panel picks a joystick model (THRUSTMASTER FCS, FLIGHTSTICK PRO, …) and calibrates it, with no axis or button assignments; and their pause panel's title asks to return to DOS where the English asks to exit Earthsiege. Their mission alerts and objectives panel translate the shipped ones. v1.0's launcher never selects them ([`../retail-builds.md`](../retail-builds.md#how-a-language-is-chosen)). v1.10's translate the panels as shipped.

### Keys and the press flash

`AlertPanel_HandleEvent` (`00454e10`) is the slot-`+0x10` handler of every member but the controls panel, whose sticks must not press widgets ([`preferences.md`](preferences.md#what-a-joystick-button-does--controlspanel_handleevent-00458f9c)):

| | |
|---|---|
| [Return], or joystick button 1 | presses the focused widget, `+0x2f7`, which every member's loop sets to widget 0 before its first pass |
| [Esc] | presses the panel's cancel widget, `+0x2fb`, which the status alert's, the pause panel's and the objectives panel's constructors also set to widget 0 |
| [Tab], scancode `0x52`, or joystick button 2 | focus the next widget (`AlertPanel_FocusNext`, `00454d7c`), wrapping past the last. With one widget it lands back on it |
| [Shift+Tab] | the same as [Tab]: see below |

The stick buttons are fields of the block `Input_BuildPlayerDevice` (`0045a7f4`) hands the handler: button 1 is the trigger at `+0x0d`, the button on the walker's first FIRE row ([`joystick-input.md`](joystick-input.md#the-buttons)), and button 2 is the second button's byte at `+0x17`. The handler answers the trigger first, then button 2, and only then the key, and latches the button it answered — `Input_LatchButton(1, 1)` for the trigger, which latches button 0, the trigger's own button on every binding the CONTROLS panel can set.

**[Shift+Tab] walks forward.** The handler has a case for `0x80f`, [Shift+Tab], which calls `AlertPanel_FocusPrevious` (`00454da0`). But every member's loop takes its input from `Input_BuildPlayerDevice`, which ANDs the block's command word with `SimCommandMask`, `0x47ff`, clearing the `0x800` [Shift] bit ([`cockpit-input.md`](cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)), so [Shift+Tab] reaches the handler as `0x0f` ([Open](#open)).

**Focus is the pointer**: `AlertPanel_SetFocus` (`00454c7c`) warps the cursor to the centre of the widget it focuses ([`cockpit-input.md`](cockpit-input.md#9-cursor-rendering)), so the loop's first focus puts the pointer on widget 0. It does so for a greyed widget too, which the walk lands on like any other; pressing one does nothing. `AlertPanel_Leave` (`004548ac`) puts the pointer back where `AlertPanel_Enter` found it, the input block's position it saved at `+0x302`. On the preferences panel a click moves the focus too, writing `+0x2f7` directly, so the pointer stays where it clicked ([`preferences.md`](preferences.md#what-a-click-does--preferencespanel_run-00456d4c)).

**A key press is a click and a flash.** `AlertPanel_PressWidget` (`00454dcc`) leaves a widget in state 2 alone; otherwise it calls the widget's click slot with the left-button flags, then `WidgetRoot_FlashPress` on the panel's own widget root at `+0x285` ([`cockpit-input.md`](cockpit-input.md#the-press-flash)). Every pass of a member's loop services that root, so the widget is held in state 1 for 10 coarse ticks, 160 ms, and then put to 0. `PanelButton_Paint` indexes its plate frame and caption font by that state, so the button shows pressed. The 0 is written whatever the state was: an option row that rests in state 3 and lost the highlight during its flash is left looking highlighted beside the row that has it.

**A key that closes a panel holds it up one more pass.** `AlertPanel_Present` (`00454ab0`), the tail of the status alert's, the objectives panel's and the controls panel's loops, services the flashes and then, while the close flag `+0x2d1` is set and `WidgetRoot_PressFlashCount` (`00453160`) is not zero, moves the flag to `+0x331` and clears it; the next pass moves it back. The loop runs that pass, whose widget paint draws the pressed button, and then the panel comes down. `PreferencesPanel_Present` (`00457180`) has no hold, and the preferences panel's DONE sets the flag in the pass its key was read, so its flash is never drawn.

## The status alert's text and layout

### Its text

`str\GNL_ALRT.STR`, three groups read in file order and indexed by the status: 20 titles, 40 button captions (two per status) and 80 body lines (four per status). Rows **0** (`PAUSE`) and **1** (`EXIT EARTHSIEGE?`) are not this panel's — they belong to the [pause panel](#the-pause-panel--004561c0), built from the same table at a different size.

**Status 5's body is not from the table.** The constructor replaces it with `DAT_004d1f1c`, the four `char*` the first unsatisfied mandatory objective carries ([`mission-objectives.md`](mission-objectives.md#the-status--mission_status-004135e8)) — the mission author's own `mission.str` lines. So the table's `YOUR MISSION IS NOT COMPLETE.` is what a status 5 would read with no objective outstanding, and what the player actually sees is whatever that mission's author wrote.

Rows **11-16 and 19** are a canned set of the same idea, one line per objective condition (waypoints, detect, protect a base, protect a squad, data link, find and destroy, find and protect). **No writer reaches them.** `es2_xref.py` finds four call sites of the constructor and no more; two pass a constant, one patches 3 to 18, and the fourth passes `Mission_StatusForAlert`'s answer, which `Mission_Status` and `EvaluateObjectives` between them confine to {2,3,4,5,6,7,8,9,10}. Row 17 (`INSUFFICIENT MEMORY FOR MAXIMUM DETAIL`) has no writer either. The status-5 substitution is the mechanism that replaced them.

### The status alert's geometry

The [family's layout](#what-the-family-shares) applies.

| Global | Device | Is |
|---|---|---|
| — | 444 x 218 | the panel's declared size, centred on 640x480 at origin (98, 131). The plate is 444x**214** |
| `004d1f22`/`24` | y 0, height 16 | the title bar |
| `004d1f28` | 160 | the one button's x, when the status has one |
| `004d1f2a`/`2c` | 82, 240 | the two buttons' x, when it has two |
| `004d1f30`, `32`, `36` | y 160, 122 x 18 | every button's y and size. The `ALERT` plate is 124x22 and overhangs |
| `004d1f3a`/`3e` | x 100, width 244 | the body block |
| `004d1f3c`, `40`/`42` | y 60, height and pitch 20 | its four rows |
| `004d1f20`, `26`, `38`, `2e`, `34` | 0, 0, 0, 186, 18 | label margins; the last two are written and never read |

**The body is `green6x8`** (`DAT_004d1eb0`), which is where this panel's green comes from — the objectives panel's yellow is `cpylw`.

### The status alert's paint — `00456068`

Plate, title, body, then each button. The body count stops at the **first empty line** rather than skipping it, and then:

**A one-line body is drawn on row 1, not row 0.** `lineCount == 1` offsets the whole run by one row so a short message sits nearer the middle of the plate than the top of the block. Two lines or more start on row 0.

A line holding a single space is not empty and does not stop the count, which is how a mission's own third `mission.str` line — `" "` in the shipped training mission — reaches it and draws nothing.

## The pause panel — `004561c0`

The status alert's small sibling, and the same behaviour: the same base, the same modal loop, the same `GNL_ALRT.STR` read the same three ways, and **a vtable whose six entries are byte-for-byte the other's**. `StatusAlertPanel_CtorMinimal` (`00455908`) is the intermediate constructor both go through, which chains `AlertPanel_CtorBase` and installs that vtable; `PausePanel_Ctor` then overwrites the vtable pointer with its own duplicate.

What differs is size and arrangement. Two statuses reach it, both as constants from `Sim_DispatchCommand`:

| Command | Key | Status | Panel |
|---|---|---|---|
| `0x19` | `P` | 0 | `PAUSE`, one button: `CONTINUE` |
| `0x410` | `Ctrl+Q` | 1 | `EXIT EARTHSIEGE?`, two: `CONTINUE` and `QUIT` |

The manual agrees with both — "Pause the mission at any time by pressing [P]; resume by clicking Continue or pressing [Enter]", and "You can exit the game at any time by pressing [Ctrl]+[Q]" — and it is what fixes `0x400` as the `[Ctrl]` bank ([`cockpit-input.md`](cockpit-input.md#keyboard-commands-are-scancodes)).

**Neither answer ends a mission**, so neither goes through `DAT_0049f5d8`. `[P]`'s one button returns 0 and the dispatcher passes that straight out, which is simply "carry on"; `[Ctrl+Q]`'s second button sets `DAT_004d2582`, the global quit flag, which ends the simulator ([`mission-objectives.md`](mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)).

### The pause panel's geometry

| Global | Device | Is |
|---|---|---|
| — | 178 x 68 | the declared size, centred on 640x480 at origin (231, 206). The plate is `GNL_ALRT.HBA` **frame 1**, 181x70 — *larger* than the declared size, where the other two panels' plates are smaller |
| `004d1f4a`/`4c` | y 0, height 16 | the title bar |
| `004d1f50`/`52` | (28, 24) | the one button, when the status has one |
| `004d1f54`/`56`, `58`/`5a` | (28, 16), (28, 42) | the two buttons, when it has two. **They share an x and stack**, where the status alert's pair sits side by side |
| `004d1f5c`/`5e` | 122 x 18 | every button's size. The `ALERT` plate is 124x22 and overhangs, as everywhere in this family |
| `004d1f48`, `4e`, `60` | 0 | label margins |

**The three button y-origins are scaled by the horizontal shift**, not the vertical one — the constructor's own slip, and the only place in the family where an axis is crossed. It costs nothing: both of DBSIM's coordinate shifts are equal in both video modes, so the numbers come out the same.

### It has no body labels

The constructor builds a title and its buttons and stops — it never creates the four body labels its shared paint writes to. That paint runs anyway, and would index an array the constructor never filled. **It is saved by its own data**: `GNL_ALRT.STR` group 2 is empty for statuses 0 and 1, so the paint's count loop stops on the first line and the write loop never runs.

## The objectives panel — `obj_alrt` (`0045751c`)

What [F11] puts up: a plate over the frozen cockpit listing the mission's block 13 text, with one button. `[F11]` is scancode `0x57`, which `CockpitWidgets_HandleCommand` answers by constructing the panel, running its modal loop and destroying it. **Nothing in that loop answers `0x57` again**, so a second [F11] does not take the panel back down.

Its resources are SIMALERT.VOL's, alongside the status alert's and the other two panels' (`prf_alrt` and `ctl_alrt`, both in [`preferences.md`](preferences.md)):

| Resource | Holds |
|---|---|
| `hba\OBJ_ALRT.HBA` | one frame, 630x230 — the whole plate. Index 0 appears four times in it, the rounded corners |
| `hba\ALERT.HBA` | frames 0 and 1, 124x22 — the button at rest and held. They differ only in the border's palette index |
| `str\OBJ_ALRT.STR` | two groups of one: `OBJECTIVES` and `RETURN` |

### The objectives panel's geometry

| Global | Device | Is |
|---|---|---|
| `004d1f84`/`86` | 630 x 278 | the panel's declared size, which is what `AlertPanel_CenterRect` centres on the 640x480 screen: origin (5, 101) |
| `004d1f8a`/`8c` | y 0, height 16 | the title bar the title is centred in |
| `004d1f90`..`96` | 254, 186, 120 x 20 | the button. Its plate art is 2px larger both ways and is blitted at the rect's origin, so it overhangs |
| `004d1fa0`, `004d1f9c`/`a4` | y 34, pitch and height 20 | the seven objective lines |
| `004d1f88`, `8e`, `98` | 0 | title and button label margins |
| `004d1f9a`, `9e`, `a2` | 40, 40, 360 | written and never read |

**The plate is 230 rows, not the declared 278.** The panel is centred by the declared height, so the art sits 24 rows above the middle of the screen and the bottom 48 rows of the panel's rect are empty.

**The objective lines are not centred on the panel.** Their rect takes x from `panel+0x04` and `panel+0x0c` — the *absolute* screen pair — where every other rect the constructor builds uses the panel-local one at `+0x1c`/`+0x24`. The labels are centre-aligned, so the text lands `(screenWidth - 630) / 2` pixels right of the panel's centre line while the title and the button sit on it: five pixels at 640x480, and visible against the title in any retail capture.

### The objectives panel's paint — `ObjectivesPanel_Paint` (`00457b58`)

Plate at the panel origin, then the title, then the lines, then each widget's own paint. The line loop **skips an empty string rather than leaving its row blank**, and counts only the lines it filled: an eighth non-empty entry is dropped, because the constructor builds seven labels. Block 13 has ten slots, but row #4's sub-array A fills one to four of them across the 62 `.MSN` files ([`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#row-4-field-decode--the-missions-text-package-dat_00470668-144-bytesrecord)), so nothing authored reaches the cap.

Every label is centre-aligned and placed by `Label_SetRect`/`Label_SetText` ([`mfd.md`](mfd.md#label-placement)). The title draws in `title`, the lines in `cpylw`. The button's caption label is constructed in `cpylw` too and never drawn in it: the button paint overwrites the font, so the caption is `active` at rest and `pushed` while held — which is why RETURN reads grey against yellow objective text.

### The objectives panel's loop — `ObjectivesPanel_RunModal` (`00457ae4`)

[The family's loop](#what-the-family-shares) with the close flag at `+0x2d1`: poll input, hand it to the panel's key handler, repaint the widgets, present; repeat until the flag is set.

What closes it:

| | |
|---|---|
| [Return], [Esc] | the [family's](#what-the-family-shares) presses; both land on widget 0, RETURN |
| a click on RETURN | `PanelButton_OnClick` forwards to `ObjectivesPanel_OnChildClick` (`00457c30`), which sets the flag when the clicked child is child 0 |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The status alert's body text is a `GNL_ALRT.STR` row chosen by the status | For every status but 5, yes. Status 5 — the one [Q] usually answers — has its body replaced with the outstanding objective's own `mission.str` lines, so the row in the table is not what a player ever reads there |
| Widget state 2 means a panel button is not drawn | `PanelButton_Paint` has no state test at all: it indexes the frame and font tables with the state, so a state-2 button draws its third frame in `INACTIVE`. The "refused by Paint" rule is the cockpit widget classes', not this family's |
| [Return] dismisses the preferences and controls panels as [Esc] does | [Esc] presses the cancel widget, DONE on both; [Return] presses the focused widget, which both loops set to widget 0 — MUSIC and the JOYSTICK row ([`preferences.md`](preferences.md)) |
| [Shift+Tab] walks the focus backwards | `AlertPanel_HandleEvent` and `ControlsPanel_HandleEvent` both have a `0x80f` case calling `AlertPanel_FocusPrevious`, but the command word they are handed has had its [Shift] bit masked off, so [Shift+Tab] arrives as [Tab] — [above](#keys-and-the-press-flash) |

## Open

- **Open:** [Shift+Tab] walking forward rests on `SimCommandMask` keeping its initial `0x47ff`: `es2_xref.py` finds three loads of it and no store, and the disassembly shows no base pointer into its neighbourhood. A retail check settles it: on the preferences panel, [Shift+Tab] from MUSIC lands on SOUNDS, not DONE.
