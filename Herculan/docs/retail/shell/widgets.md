# Shell widgets

How VSHELL's widget classes behave on every screen: when a widget is up, which widget a click or a key reaches and what it does with it, the pointer, and how a widget paints. The frame the widgets hang in is in [`screen-layout.md`](screen-layout.md).

## Showing and hiding a widget

**The dump's two names are the wrong way round.** `Window_ShowRecursive` (0041f2e6), which the dump calls `Window_HideRecursive`, is the **show**; `Window_HideRecursive` (0041f469), which the dump calls `Window_ShowRecursive`, is the **hide**. Bit 2 of the flags word at `+0x11` is a *hidden* bit: `Window_HideRecursive` sets it and `Window_ShowRecursive` clears it. The show returns at once on a widget already visible; the hide, on one already hidden, only clears bit 4, described below.

Three things say so independently:

- **`ESMessage_Paint` (`0040b439`) draws only when `(+0x11 & 2) == 0`.** A widget that paints when the bit is clear is visible when the bit is clear, so the call that sets the bit is the hide.
- **The repair tab's entry and teardown are a matched pair.** `Repair_Enter` (004332ec), which tab 3's handler calls to bring the screen up, calls `Window_ShowRecursive` on its content panel and both list panels; `Repair_Leave` (004333eb), which the teardown dispatcher `00439ea7` calls on the way out, calls `Window_HideRecursive` on the same three. Under the dump's names an entry routine would hide its own screen and a teardown would show it.
- **The repair screen's two pictures swap with the selection.** `Repair_SwapDiagram(oldColumn, newColumn)` (0043393d) calls `Window_ShowRecursive` on the internals diagram when the selection moves into the internals list and on the exploded external picture when it moves back ([The damage diagram](weapons-and-repair.md#the-damage-diagram)) — the right way round only if that function is the show.

**Every widget is born hidden.** `Window_SetRect`, which every constructor goes through, ends by setting bit 2. A sweep of the disassembly for stores to `+0x11` finds, besides that one, `ESWindow_Ctor` (`00409970`) zeroing the word before it calls `Window_SetRect`, the show, the hide, the child-list operations (bit 1 only) and the display root's constructor; `Display_SetCursor`'s store is to the display object's own `+0x11`, not a widget's. The constructors then differ. `WinButton_Ctor` (and with it every `Panel`, `Button`, `ButtonIcon` and `Grid`), `ESMessage_Ctor` and `ESDialog_Ctor` (0040bbf4) end with `Window_ShowRecursive`; `ESTitle_Ctor` and `ESBitmap_Ctor` end with `Window_HideRecursive`; a widget built by `ESWindow_Ctor` alone — the palette scopes, the top-level window, the dialogs' full-display windows — calls neither and stays hidden until something shows it by name. So a screen's panels are constructed dark and its entry routine is what puts them up.

Both consult the widget's parent, which `Window_Parent` (0041f283) finds by walking the `+9` chain while a node's bit 1 is clear: `+9` holds the previous sibling, and only on the first child in a list, which has bit 1 set, the parent. While that parent is itself hidden, the show sets bits 2 and 4 and returns, and the hide sets bit 4 along with bit 2; the hide of a widget already hidden clears bit 4. A show that runs clears both bits and then shows every child carrying bit 4 — so bit 2 is whether the widget is hidden and bit 4 that it comes back up with its parent, which is what lets a subtree come back up in the state it went down in.

## Which widget a click reaches

Input reaches widgets through an event queue, not directly. A mouse change becomes an event of type `0x20` with a sub-code at `+0x24`, which `Mouse_OnButtonState` (00408b03) assigns: 0 a move, 2 and 1 the left button going down and up, 4 and 3 the right. `EventQueue_Pump` (00469ba4) drains the queue.

**A move picks the target and a button event goes to it.** On a move the pump hit-tests from the display's root with `Widget_HitTestTree` (00469d1c). A widget is hit only when it is not hidden (`+0x11` bit 2, [above](#showing-and-hiding-a-widget)) and the point lies inside its absolute rect, edges included. Its children are then tried in list order, and the first that is hit answers in its place, recursively; a widget none of whose children is hit is the answer itself. When the answer changes, `Pointer_Leave` (00469d74) sends the old widget a leave event (`0x10`) and `Pointer_Enter` (00469e1c) sends the new one an enter (8). A button event carries no position test of its own: it goes to whatever the last move left under the pointer, and so does any other event posted without a target, a keystroke included.

**The pointer can be locked.** While `+0x1f` of the pointer state at `g_EventQueue` (`005ddbd0`) is set, the pump skips the hit test, so a move changes nothing and every event goes to the current target. Only edit fields set it — `ESDialog_HandleEvent` on a press ([below](#the-widget-that-takes-a-click-decides-what-it-does)), `SaveScreen_BeginRename` (004377d2) and `0043bc0a` — and `Pointer_Unlock` (00469cdc) clears it and hit-tests again.

**Siblings are tried newest first.** `Window_AttachChild` (0041f134), which `Window_SetRect` calls from every constructor, pushes a child onto the *head* of its parent's list. The only other list operation, `Window_Detach` (0041f17a), is called by two teardowns that delete what they unlink, so no list is ever reordered. Where two siblings overlap, the one built later answers, and a child can be hit only where it lies inside its parent.

**The event then climbs to the first widget that takes mouse events.** `Event_Deliver` (00469f34) walks from the hit through `Window_Parent` to the first widget whose event mask at `+0x39` has the event type's bit, and calls that widget's vtable slot 0 with the point made relative to the hit. `Window_SetRect` starts the mask at `0x1f` and `WinButton_Ctor` adds `0x60`, which carries the mouse bit. `ESMessage_Ctor` is built on `ESWindow_Ctor` rather than `WinButton_Ctor` and clears `0x18`, leaving `0x07`. **A `Text` therefore never takes a click**: a click on a caption, a label or a value reaches its parent. `Panel` and everything built on it, `Button` among them, goes through `WinButton_Ctor`, and `ESDialog_Ctor` adds `0x360` itself, so all of those take clicks.

**The innermost widget is part of the target.** The leave event climbs the same way, so when the pointer moves from one of a row's text columns to the next, the leave sent to the first reaches the row, and puts out a press on it exactly as leaving the row would.

What that decides on the screens ported here:

| Overlap | Who answers |
|---|---|
| a [repair hotspot](weapons-and-repair.md#the-damage-diagram) over another | the six body areas are built first and the weapons after, each in index order, so a weapon over a body area, a higher mount over a lower one, a higher area over a lower one |
| rows 13 tall on a 12-pixel pitch — the save list, both repair lists | the lower row owns the shared border line |
| a [crew row](squad-and-crew.md#the-rows)'s texts | the row; its portrait is an image panel of its own carrying the row's handler |
| a row's four [text columns](weapons-and-repair.md#a-row-is-four-text-columns), a button's caption | the row, the button |
| the repair screen's five [readouts](weapons-and-repair.md#the-repair-screen) | the readout, which is disabled and swallows it |
| the weapons screen's [picture box](weapons-and-repair.md#the-weapons-screen) and a picture in it | the box, which is disabled and swallows it; a picture's image panel has no handler and swallows it too |

### The widget that takes a click decides what it does

Slot 0 of a class's vtable is its event handler, and the classes on the shell's screens that take mouse events run the seven below, counting `Button`'s as the base's. Firing is `Window_DispatchCallback` (0041f5d4), which runs the handler at `+0x3d` — the constructor's handler argument — or discards the event when there is none. No handler hands an event on to the parent, so a widget with no handler, or a disabled one, swallows a click on it.

| Handler | Classes | Fires on | Needs the press on it | A leave cancels | Tests |
|---|---|---|---|---|---|
| `WinButton_HandleEvent` (004097da) | `Panel`, `FramedPanel`, `TitledPanel`, `Grid`; `Button` through `ESButtonFont_HandleEvent` (00409b0f), which adds the press sound | either button's release | yes | yes | `+0x49`, `Avi_Playing`, `MovieQueue_Running` |
| `ESArm_HandleEvent` (0040c3b5) | `HatchedDivider` | either button's release | yes | yes | `+0x49` |
| `ESButtonBitmap_HandleEvent` (00409df2) | `ButtonIcon` (RTTI `ESButtonBitmap`), the tab strip | the left press; the right release | the right button only | no | `+0x49`, `Avi_Playing`, `MovieQueue_Running` |
| the same, with `+0x61` set and `+0x5d` clear | the mission screen's arrows | the left release, and each tick of its 500 ms alarm while lit; the right release | the right button only | yes | the same |
| `ESRadioButton_HandleEvent` (0040a139) | the [checkbox](main-menu.md#the-preferences-screen) | the left press; the right release | the right button only | no | `+0x49`; the right button also the two movie flags |
| `ESBitmap_HandleEvent` (0040b6da) | image panel | the left release | no | — | none |
| `ESDialog_HandleEvent` (0040beaf) | edit field | the left press | — | — | none |
| `ESAlert_HandleEvent` (0040b143) | `ESAlert` | nothing: it grabs and restores the screen under it on the show and hide and repaints, and drops every other event | — | — | — |

**`WinButton_HandleEvent` pairs a press with its release.** A press, either button, lights `+0x45` and repaints; a release with `+0x45` lit fires, then clears it and repaints; the leave event clears it. So the click fires on the release, and only when the button went down on the same widget and the pointer never left it. `ESArm_HandleEvent` is the same function without the two movie flags ([Input while a movie plays](movies-and-sound.md#input-while-a-movie-plays)).

**The strip fires on the left press.** `ESButtonBitmap_HandleEvent` lights `+0x45`, plays the press sound, repaints and fires, all on the press, while its auto-repeat flag `+0x61` is clear, as the constructor leaves it. The left release zeroes `+0x45` without repainting, which is why a tab stays drawn lit after the click that latched it: what is on screen is the paint the tab handler's own write of 1 triggered. The right button falls through to `WinButton_HandleEvent`: it lights on the press and fires on the release, and then zeroes `+0x45` and repaints after the handler has run. So a tab picked with the right button has latched itself and is then repainted unlit, and right-clicking the tab already up unlights it; recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md). The leave clears `+0x45` only while `+0x5d` is 0, and `ESButtonBitmap_Ctor` sets it to 1, so a tab the pointer is dragged off stays lit, and a later right release on it fires it with no press.

**An auto-repeating `ButtonIcon` fires on the left release instead, and again every half second while it is held down.** With `+0x61` set the left press lights the button, plays the press sound and repaints without firing, and the left release puts it out, zeroes `+0x65`, repaints and fires, wherever the press was. A held button fires again on each tick of an alarm, and `+0x65` counts those ticks, so a handler can do more the longer the button is held, as [the mission screen's map buttons](mission-screen.md#the-three-views) do.

The alarm runs from the show, not from the press. The show and the hide post events 1 and 2 to the widget ([Showing and hiding a widget](#showing-and-hiding-a-widget)), and the handler answers them by installing a WinTimer alarm for the widget with a delay and a period of 500 (`WinTimer_InstallAlarm`, `0046a0b0`) and by removing it (`WinTimer_RemoveAlarms`, `0046a138`). `Timer_Tick` (`0046a1c8`), which `Shell_PumpEvents` (`0046814c`) runs before each `EventQueue_Pump`, takes the `GetTickCount` milliseconds since its own last run off the alarm and, once it is down to 0, posts one tick (event `0x200`, target the widget) and reloads it to 500, dropping the overshoot. `ESButtonBitmap_Ctor` (`00409d14`) adds `0x200` to the event mask, so the tick reaches the button. A tick that finds the button lit, enabled and not hidden adds one to `+0x65`, repaints and fires again. The ticks keep the beat they started on at the show, so the first repeat comes anywhere up to 500 ms after the press.

A builder that also clears `+0x5d` lets the leave put the button out, and the leave zeroes `+0x65` with it. The right button is `WinButton_HandleEvent`'s, as on the strip. Its press lights the button, so a held right button repeats too, and its release fires without zeroing `+0x65`: the next press's ticks count on from where the right-button hold left off, until a left release or a leave while lit zeroes it.

**A checkbox fires on the left press, as the strip does**, lighting `+0x45`, playing the press sound and repainting first, but it tests neither movie flag on that button, and its left release does nothing at all. A leave leaves it lit, `+0x5d` being the constructor's 1. The right button is `WinButton_HandleEvent`'s.

**An image panel fires on any left release that reaches it**, wherever the button went down.

**An alert swallows a click on its body.** `ESAlert_Ctor` (`0040afe0`) adds `0x60` to the mask, so a click on the alert outside its buttons climbs no further, and its handler drops it.

**An edit field fires on the left press, and takes the pointer.** With its focus flag `+0xa7` clear, the press sets it, installs a WinTimer alarm for itself (`WinTimer_InstallAlarm`, 0046a0b0, with 500 and 500), [locks the pointer](#which-widget-a-click-reaches) and fires. Every later event then goes to the field, and the next press, landing there with `+0xa7` set, clears the focus, removes the alarm, releases the lock, hit-tests again and posts the press over, so it lands on whatever is under the pointer — the field itself included, which then fires again. The field ignores the right button, and while the lock holds a right click anywhere reaches the field and is dropped.

## The pointer

**The shell's pointer is the Windows arrow.** VSHELL draws none of its own. `Shell_RegisterWindowClass` (`004062cb`) registers the main window's class with `LoadCursorA(NULL, IDC_ARROW)`, and `MainWndProc` passes `WM_SETCURSOR` to `DefWindowProcA`, so every move over the window puts the class's arrow up.

The widget layer carries a cursor too, and it adds nothing to that. [The startup](startup.md#the-startup--shell_main-00401525) wraps what `GetCursor()` returns — the arrow — in two cursor objects, `Shell_RootCursor` (`004810e8`) and `Shell_HotspotCursor` (`004810ec`) (`ShellCursor_Ctor`, `0041f644`: vtable `00471844`, the handle at `+4`). It gives the first to the display root's `+0x35` and installs it, and `Hotspots_BuildOverlay` (`0043c1a0`) gives the second to every arming hotspot. `Pointer_Enter` installs the `+0x35` of the first widget up the parent chain that has one through the display's slot 3, `Display_SetCursor` (`0041fab3`, vtable `004717ec`), which calls `SetCursor` with it unless it is the one already installed. What reaches `SetCursor` there is the object's address rather than the handle at its `+4`.

**The hourglass is the only other pointer.** `Shell_SetBusyCursor(busy)` (`0040877f`) puts up `IDC_WAIT` for 1 and `IDC_ARROW` for 0, then `ShowCursor(1)`. `MainMenu_OnContinue` wraps its whole load in it, and `Movie_PlayQueue` raises it before each movie's setup; `Avi_Play` drops it as playback starts, and the queue drops it again when the ring is empty. Those are its only callers. It shows only while the shell is not pumping messages, since the next move puts the class's arrow back.

**`dba\cursor.dba` is not the shell's.** `SHELL0.VOL` carries it, and `VSHELL.EXE` does not name it. Every other shell bank is named by a literal of the form `dba\mnu_bttn.dba`, and the only names built at runtime are the theaters' (`shellmap.cpp`'s `dba\` + name + `.dba`) and the per-chassis banks' (`hgrid.cpp`'s name + `.dba`). A case-blind byte search of the whole executable for `curs` finds the Win32 imports, a debug format string, the RTTI names `WinCursor` and `WinWin95Cursor` of the two cursor classes above, and the Dynamix library's `GLCursor` type name — in the type-name table at `0047bda4`, beside `GLBitmap` and `GLFont` — and no resource name.

## How a widget paints

**A widget carries its rect twice.** `Window_SetRect` (`0041eb5c`) stores the constructor's rect verbatim into `+0x25`/`+0x29`/`+0x2d`/`+0x31` — left, top, right, bottom, **parent-relative** — and `Window_ResolveRect` (`0041ef45`) derives the absolute rect into `+0x15`/`+0x19`/`+0x1d`/`+0x21`. `Window_SetWidth` (`0041ec33`) shows the relation directly: it adds the parent's `+0x15` to a child's `+0x25` to get the child's `+0x1d`. A sweep of the disassembly for stores to `+0x25` finds two functions besides `Window_SetRect` that move a widget, each rewriting the relative rect and rederiving the absolute one: `Window_MoveRect` (0041ebef), called on the mission screen's `0048d7dc`, and `Window_SetPosition` (`0041ee1d`), with which `Repair_BuildDiagrams` and `Squad_BuildBayPictures` place their pictures; `Window_SetWidth` resizes those same pictures.

The paints in the table below are the visual vocabulary of the screens ported so far. All of them work in **widget-local coordinates**, where the extent they draw against is `+0x2d - +0x25`. Because that is a difference it is the same in either space — one less than the inclusive width — so a paint never reads an origin at all: `Window_BeginPaint` (0041f585) opens every one of them and binds the drawing context to the widget's absolute rect and clips to it.

**Colour is always a palette index**, taken from a widget field, and the drawing context carries a `{mode, colour}` pair: mode 0 at `+0x22c` is a solid fill, mode 6 is a blit through a 256-entry lookup table. `Gfx_FillRect` (00457364) fills a rect, `Gfx_DrawLine` (004552e4) draws a line between two inclusive endpoints, and `Gfx_PlotPixel` (0045999c) plots one pixel.

**The border is a chamfer.** `ESRect_FillAndBorder(widget, fill)` optionally clears the interior to a literal `0x10` — the shell's background colour; a `Text`'s opaque backing clears to its own `+0xc5` instead — and then draws four edges each stopping one pixel short at both ends, so the true corners stay empty, and paints the four pixels one step *inside* those corners instead. That clipped-corner box is every panel and every button in the shell. It draws nothing at all when `+0x51` is clear, which is how a screen's backdrop-textured root shows its bitmap and no chrome.

| Class | Paint | What it adds |
|---|---|---|
| `Panel` (RTTI `ESRect`) | `0040a959` | nothing — the fill and the chamfered border alone |
| `Button` (`ESButtonFont`) | `00409b79` | a second border one pixel inside the first, in the same colour, then the caption, which `ESButtonFont_Ctor` builds without a backing — a readout's opaque caption is its builder's write, and it clears over the inner border |
| `FramedPanel` (`ESRegionFill`) | `0040a9a6` | a 50% checkerboard over the interior in `+0x55` |
| `TitledPanel` (`ESTitle`) | `0040ac27` | the header strip, its hatch and title plate, a divider, and a dithered *or* filled body |
| `Text` (`ESMessage`) | `0040b439` | one string, aligned, with an optional backing fill |
| edit field (`ESDialog`) | `0040c14f` | one editable string, left-aligned, with an optional caret |
| image panel (`ESBitmap`) | `0040b772` | one bitmap at an offset, under an unfilled border ([The crew screen](squad-and-crew.md#the-crew-screen)) |
| `HatchedDivider` (`ESArm`) | `0040c513` | a body of horizontal lines and an optional inner border ([The crew screen](squad-and-crew.md#the-crew-screen)) |
| `Grid` (`ESGrid`) | `0040b97c` | grid lines and thirty recolourable bitmap parts ([The damage diagram](weapons-and-repair.md#the-damage-diagram)) |

`ESTitle_Paint` fills its header strip to `+0x55` for `+0x61` rows, then — when `+0x65` is set, which the constructor does and seven panels' builders undo straight after it (the repair screen's `RepairExternalList` (`0048d13c`) and `0048d180`, the squad roster's `Squad_InventoryPanel` (`0048d4f8`), the weapons screen's `0048d598`, the mission screen's `0048d7d4` and `0048d7dc`, and the armory screen's `0048d97c`) — lays a **diagonal hatch** over it in colour 13: bands of fourteen 45-degree lines on a 28-pixel pitch, 26 bands from five pixels left of the widget. That is over 700 pixels of hatch for a panel a third as wide, and only the clip stops the surplus; the paint relies on clipping rather than measuring. It then punches the hatch back out to `+0x55` between `+0x6d` and `+0x71`, which is the **title plate** the caption reads against, draws the header's own side edges, and closes with a divider on row `+0x61`.

**`+0x59` chooses between a filled body and a dithered one.** Set, the body is cleared to `0x10`; clear, it takes a 50% checkerboard in `+0x5d` from the header height down — and over an unpainted surface that means the shell's single backdrop bitmap shows through at half strength. The save screen takes the second path, which is why the bay is visible through its panel.

### Text placement and colour

`Font_DrawString(font, {x, y}, text)` (0045409c) draws a run: it tests the context's `+0x231` for 1 and then for 2, so **0 is left, 1 is right and 2 is centred**, against the field width at `+0x235`; then it advances glyph by glyph. v1.10's font code is another build, which reads characters unsigned ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)). `Text` takes that mode from its own `+0x45`, so a label's alignment is a constructor argument. The save screen's detail panel builds every label right-aligned and each single value left-aligned, which is what makes a label's colon meet its value; its kill counters and column headers are centred.

The `y` is an **ink baseline**: `Font_DrawGlyph` (00453fb4) places each glyph's top row at `y - font[+0x16]`, and `+0x16` is the `.DFN` header's `inkHeight`. `Font_CellHeight` and `Font_CellHeightCopy` (`00453f9c`) both return `font[+0x0a]`, the glyph cell height. The two classes centre differently and neither is derived from the other — `Text` uses `H - (H + 1 - cellHeight) / 2 - 2` and the edit field `cellHeight / 2 + (H + 1) / 2`.

**A button's caption drops two rows while the button is held down.** `ESMessage_Paint` takes a second argument, `-1` when a `Text` paints itself, and otherwise a row its owner chose, drawing the baseline one row above it. `Button`'s paint, `ESButtonFont_Paint` (`00409b79`), passes its caption (`{1, 0, W, H}` in the button, `ESButtonFont_Ctor`, `00409a54`) `(cellHeight + H + 1) / 2 - 1`, or `+ 1` while the lit flag `+0x45` and the enable flag `+0x49` are both set: a baseline of `(cellHeight + H + 1) / 2 - 2` at rest, two lower pressed. The rest baseline is not the one the caption would take as a plain `Text`; the two differ by a row whenever `H + 1 - cellHeight` is even. `WinButton_HandleEvent` lights the flag on a press and puts it out on the release or a leave, repainting each time ([The widget that takes a click decides what it does](#the-widget-that-takes-a-click-decides-what-it-does)), so the caption moves for exactly as long as a release would fire the button.

**A widget picks its text colour by remapping, not by choosing a pen.** The fonts carry one ink index each ([`../formats/dfn-hfn-dci.md`](../formats/dfn-hfn-dci.md)) and the shell's is `0x29`, so both text paints draw the string in whatever the font has and then re-blit the area through an identity table with entry `0x29` replaced — by `Text`'s `+0xb5`, or the edit field's `+0xbb`. One font therefore serves a label at `0x1a`, a value at `0x29` and a resting list row at `0x27`, and a selection highlight costs nothing but a different replacement.

`Text`'s `+0xc1` is an opaque-background flag and `+0xc5` the colour it clears to: a value field clears its own rect so a refresh overwrites cleanly, and a static label does not.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `0041f2e6` shows a widget and `0041f469` hides it, matching their names in the raw Ghidra dump | The dump's own names support that reading — one sets a state bit and recurses into children, the other clears it, and the names line up with which is which. They are swapped: `+0x11` bit 2 is a *hidden* bit, so the setter is the hide. `known_symbols_vshell.json` carries the corrected assignment (`Window_ShowRecursive` at 0041f2e6, `Window_HideRecursive` at 0041f469); only the raw dump still has it backwards. Three witnesses agree; see [Showing and hiding a widget](#showing-and-hiding-a-widget) |
| Every widget fires on the button's release, and only after a press on it | That is `WinButton_HandleEvent`'s rule, and it is the base class's handler, run by the panels, the grids and every content button, so it reads as the shell's. The tab strip, the image panels and the edit fields each put a handler of their own in vtable slot 0: the strip fires on the left press, an image panel on any left release, an edit field on the left press ([The widget that takes a click decides what it does](#the-widget-that-takes-a-click-decides-what-it-does)) |
| The shell draws its mouse pointer from `dba\cursor.dba` | The bank sits in `SHELL0.VOL` with the shell's own art, and the Dynamix library has a `GLCursor` type to draw one with. `VSHELL.EXE` never names the bank; the pointer is the Windows arrow, with the hourglass while a save or a movie loads ([The pointer](#the-pointer)) |

## Open

- **Deferred:** the class of [the pointer state](#which-widget-a-click-reaches) at `g_EventQueue`. Its constructor, `EventQueue_Ctor` (`00469b00`), writes no vtable, so RTTI does not name it; its inline methods assert in `include\WHANDLER.H`. `Widget_HitTestTree` (`00469d1c`) and the `Pointer_*` functions around it (`Pointer_SetTarget`, `Pointer_Unlock`, `Pointer_Leave`, `Pointer_Enter`, `Pointer_MoveTo`) carry invented prefixes for the same reason.
