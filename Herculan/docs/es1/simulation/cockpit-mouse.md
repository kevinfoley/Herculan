# DBSIM.EXE — the root widget and the cockpit's mouse

**ES1's cockpit takes the mouse as ES2's does: the mouse driver hands each change of the pointer or its buttons to a callback that queues it, and once a tick the input build drains the queue through the cockpit's root widget, which presses, releases and drags the widget under the pointer, the first one registered winning where two overlap** ([ES2's](../../retail/simulation/cockpit-input.md#overview)). What a player could see differently: a double-click is two releases of the same button less than a second apart, where ES2's window is 480 ms. The pointer is in the 320 × 200 screen's own pixels: the driver keeps it within x 0–319 and y 0–199 ([the mouse driver's object](input.md#the-mouse-driver)), and a press or a release is tested against the widgets after taking off the root's origin, which follows the view as ES2's does ([a strip changes edge with the view](../../retail/simulation/cockpit-input.md#a-strip-changes-edge-with-the-view)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

Two object files hold the simulator's widget root and its mouse, after [the cockpit clock's](../formats/ttm-scripts.md#the-cockpit-clock), whose `CockpitClock_FormatTime` ends with a `RET 4` at `00043780` and one zero byte ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| Code | Holds | Its end |
|---|---|---|
| `00043784`–`0004412f` | the widget base, `Widget_CtorRect` (`000438fc`), the button base table's slot functions `000439ae`–`000439cf`, [the slider base](#the-slider-base), [the root widget](#the-root-widget--ctlwindow_ctor-00043cac) and [the press and the release](#press-release-and-drag) | `Widget_Repaint`'s `RET 8` at `0004412b`, then two zero bytes |
| `00044130`–`0004487f` | [the mouse callback and its queue](#the-callback-and-the-queue), [the frame's mouse events](#the-frames-mouse-events--cockpitmouse_processqueue-000444d0) and the root's list operations | `CockpitMouse_StaticInitNoOp` (`0004487c`), a lone `RET`, entry `000adc50` of the static constructor table, then three zero bytes |

The second opens with a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), `HeapPtr_DtorCockpitMouseCopy` (`00044130`), `ConfigPart_DtorCockpitMouseCopy` (`00044158`, slot `+0x1c` of `00094bee`), `ConfigPart_GetClassTagCockpitMouseCopy` (`00044197`) and `TSPartBase_DtorCockpitMouseCopy` (`000441a0`, slot `+0x1c` of `00094c22`). The first file's functions not named here are unread ([Open](#open)).

## The root widget — `CTLWindow_Ctor` (`00043cac`)

**The root is ES2's `CTLWindow` ([one flat hit-test registry](../../retail/simulation/cockpit-input.md#5-one-flat-hit-test-registry-for-the-whole-cockpit)) with its vptr after its data, and without the press-flash list.** `CTLWindow_Ctor`, (window, rect), allocates `0x356` bytes when given none, returning 0 when that fails; runs `GLViewport_Ctor` on `+0x17`; stores `CTLWindowVtable` (`00094bde`) at `+0x352`; copies the rect to `+0` and to the viewport's rect; sets the viewport's clip mode to 1; and clears the list's count. `es2_xref.py` finds calls from `Cockpit_LoadPanel` ([building it](cockpit-panel.md#building-it)), with the `.GAU` record's first 16 bytes, and from `00045988`.

| ES1 | ES2 | Holds |
|---|---|---|
| `+0`–`+0xc` | the same | the rect *x0*, *y0*, *x1*, *y1*, `int32`; its origin is the canvas origin a press is tested from |
| `+0x10` | the same | the hit-shape byte, 0 |
| `+0x17` | `+0x1b` | the `GLViewport`, `0x239` bytes |
| `+0x21f` | `+0x223` | the viewport's clip mode `+0x208`, 1 |
| `+0x223` | `+0x227` | the viewport's translation flag `+0x20c`, which `Widget_Repaint` sets to 1 |
| `+0x227`–`+0x233` | `+0x22b`–`+0x237` | the viewport's rect `+0x210`, a copy of the root's |
| `+0x250` | `+0x254` | the clickable list's `int16` count |
| `+0x252` | `+0x256` | the clickable list, 64 widget pointers |
| none | `+0x356`, `+0x396` | the press-flash list and its count |
| `+0x352` | `+0x17` | the vptr |

`CTLWindowVtable` has one slot from `+4`, **`CTLWindow_Slot00NoOp` (`00043d96`)**, two `NOP`s and `RET 4`, ES2's (`00452aad`).

The root's operations, each ES2's on these offsets:

- **`Widget_ClearPressed` (`00044310`)**, (root): makes the root the one the frame's mouse events go to, `Widget_ActiveRoot` (`00094c54`), and sets `Widget_PressedIndex` (`00094c58`) to −1. `es2_xref.py` finds calls from `Cockpit_LoadPanel`, `00045f00` and `000460b8`.
- **`Widget_RegisterClickable` (`000443d4`)**, (widget, root): appends the widget to the list and adds 1 to the count, with no bound check.
- **`Widget_TranslateRootForView` (`00043d9c`)**, (root, delta): reads the display's attributes 0 and 1 (`GLDisplay_GetAttribute` on `000bdd28`) and drops the answers, adds the delta to the viewport's rect `+0x227`–`+0x233` and moves the root's rect by it with `Widget_OffsetRect` (`0004387c`), which adds the delta's *x* to *x0* and *x1* and its *y* to *y0* and *y1*. **`Widget_TranslateRootForView_Thunk` (`0004433c`)**, (*dx*, *dy*, root), passes the two words as the delta; `es2_xref.py` finds one call, from `CockpitWidgets_TranslateForView` ([drawing it](cockpit-panel.md#drawing-it)).
- **`Widget_Repaint` (`0004410c`)**, (root, widget): sets `+0x223` to 1 and calls the widget's invalidate slot `+8`, ES2's `+4` ([painting is deferred](../../retail/simulation/cockpit-input.md#painting-is-deferred-two-frames)).
- **`Widget_NotifySelfAndChildren` (`000440a0`)**, (root): clears `+0x223`, calls the root's slot `+4`, sets `+0x223` to 1 and calls slot `+4`, the paint, of each widget in the list. **`Widget_NotifySelfAndChildren_Thunk` (`00044328`)** calls it.
- **`Widget_Show` (`00044404`)** and **`Widget_Hide` (`00044434`)**, (index, root): put the widget at that index of the list in state 0 or 2 and repaint it.
- **`Widget_NoOp` (`00044338`)**, a `RET 4` between the two thunks, where ES2's (`00452bbc`) is.
- **`WidgetRoot_Delete` (`00044350`)**, (root): for a root that is set, stores `CTLWindowVtableCockpitMouseCopy` (`00094c6a`) at `+0x352` and the second file's copies of `GLViewport`'s two vtables, `00094c7a` and then `00094ca2`, at `+0x17`, and frees the root with `operator_delete`. `es2_xref.py` finds one call, from `000460b8`. The copies' slots: `CTLWindow_Slot00NoOpCockpitMouseCopy` (`00044383`) at `+4` of `00094c6a`, `GLViewport_DtorCockpitMouseCopy` (`00044388`) at `+0x1c` of `00094c7a` and `GLBase_DtorCockpitMouseCopy` (`000443b0`) at `+0x1c` of `00094ca2`, the code of the library's `00069bb8` and `00069b94`, the destructor slots of `GLViewport`'s tables `0009b456` and `0009b42e`.

`es2_xref.py` finds no reference to `Widget_NotifySelfAndChildren_Thunk`, `Widget_NoOp`, `Widget_Show` or `Widget_Hide`, as for ES2's ([Open](#open)).

## Hit testing

**A widget's fields up to its vptr are ES2's leaf widget's, with the state, the drag flag and the paint counter 4 bytes lower and the vptr after them:** the rect at `+0`, the hit-shape byte `+0x10` with the diamond's center `+0x11`, `+0x13` and radius `+0x15`, the `int16` state at `+0x17` (ES2's `+0x1b`), the `int16` drag flag at `+0x19` (ES2's `+0x1d`), the paint counter at `+0x1b` (ES2's `+0x1f`) and the vptr at `+0x1c` (ES2's `+0x17`). Its table's slots are ES2's [leaf-widget slots](../../retail/simulation/cockpit-input.md#the-leaf-widget-vtable) one slot further on: paint `+4`, invalidate `+8`, click `+0xc`, per-frame `+0x10`, value `+0x14`, set value `+0x18`, drag `+0x1c`, and for a slider recompute scale `+0x20`.

- **`Widget_HitTest` (`000437d0`)**, (widget, point), ES2's ([rectangle or circle](../../retail/simulation/cockpit-input.md#6-hit-test-rectangle-or-circle)): with the hit-shape byte 0, whether the point lies in the rect, edges included; otherwise whether |*x* − *cx*| + |*y* − *cy*| ≤ *r*.
- **`Widget_HitTestChildren` (`0004401c`)**, (root, point), also entered at its padding `0004401a`: the index of the first widget in the list that `Widget_HitTest` passes and whose state is not 2, else −1. So the widget registered first takes the point.

## Press, release and drag

ES2's three functions ([press, release, click vs. drag](../../retail/simulation/cockpit-input.md#7-press-release-click-vs-drag)), each taking (root, point) with the point in screen pixels:

- **`Widget_OnMouseDown` (`00043e00`)**: rebases the point by the root's rect origin (`+0`, `+4`), stores `Widget_HitTestChildren`'s answer in `Widget_PressedIndex` and, for a widget, puts it in state 1; with its drag flag 1, calls its drag slot `+0x1c` with the rebased point and sets `Widget_DragCapture` (`00094c5a`); then repaints it. ES2's adds the video-mode offsets `004d25da`/`004d25de` under a flag.
- **`Widget_OnMouseUp` (`00043eac`)**: rebases the point, hit-tests it and stores the answer in `Widget_PressedIndex`; for a widget, when it is the one that was pressed or bit 1 of `Widget_ClickButtons` (`00094b84`) is set, calls its click slot `+0xc` with what its value slot `+0x14` returns; then puts the widget in state 0, repaints it and sets `Widget_PressedIndex` to −1.
- **`Widget_TrackPressedWidget` (`00043f68`)**: while `Widget_PressedIndex` is not −1, hit-tests the point as given, not rebased; on the pressed widget in state 0 it puts state 1, anywhere else with the pressed widget in state 1 it puts state 0, repainting it either way.

A button's value slot returns `Widget_ClickButtons` ([the gadget bases](cockpit-widgets.md#the-gadget-bases)), so a click hands the button the release's mouse buttons, bit 0 for the left and bit 1 for the right.

## The callback and the queue

**`CockpitMouse_Init` (`000441cc`)**, no arguments, from `Sim_StartMission` ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)), ES2's (`00452abc`):

1. `Input_GetMouseDevice_Thunk`, whose answer it drops; `Mouse_AddCallback`(`CockpitMouse_OnEvent` by its padding `00044461`, `0x1f`), keeping the slot in `CockpitMouse_CallbackSlot` (`00094c52`) ([the mouse driver](input.md#the-mouse-driver)). The mask's bits are the driver's events: 0 the pointer moved, 1 and 2 the left button pressed and released, 3 and 4 the right's. ES2's listener mask is `0x1e`, without the move.
2. Clears `CockpitMouse_LastPoint` (`000b16c0`), the two held bytes `CockpitMouse_LeftHeld` (`000bd50e`) and `CockpitMouse_RightHeld` (`000bd510`) and `CockpitMouse_LastButtons` (`000bd538`).
3. Makes the two queues, each a count and a buffer of 100 records of `0xe` bytes from `__vec_new` with the element constructor `CockpitMouseEvent_ElementCtor` (`000442b4`, by its padding `000442b2`): `CockpitMouseQueueA` (`000bd520`, the buffer at `000bd524`), which `CockpitMouseQueue_Back` (`000bd530`) points at, and `CockpitMouseQueueB` (`000bd528`), which `CockpitMouseQueue_Front` (`000bd534`) points at.
4. `CockpitMouse_DoubleClickTicks` (`000bd518`) = 1000000 / `Math_Q14Multiply`(the timer's divisor, `0x35a1`) / 2, the timer's interrupts a second halved ([the rate](timer.md#the-rate)): 60 with the simulator's divisor. It is counted on the clock `000981c4`, which advances 60 times a second ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), so the window is one second. ES2's is `0x3c` / 2, 30 coarse ticks.
5. `Widget_ActiveRoot` = 0 and `Widget_PressedIndex` = −1.

**`CockpitMouse_Shutdown` (`000442fc`)**, no arguments and no Ghidra function: `Input_GetMouseDevice_Thunk`, then `Mouse_RemoveCallback`(`CockpitMouse_CallbackSlot`). ES2's is the code at `00452b7c`, which Ghidra makes no function of either. `es2_xref.py` finds no reference to either ([Open](#open)).

**`CockpitMouse_OnEvent` (`00044464`)**, (record), the callback, which the driver's handler runs at interrupt time with its record {*x*, *y*, buttons} ([the mouse driver](input.md#the-mouse-driver)). It does nothing while `CockpitMouseLive` (`000bd512`) is clear, which a replay keeps it ([recording and playback](input.md#recording-and-playback)). Otherwise, with *t* the clock `000981c4`: when the buttons are `CockpitMouse_LastButtons`, it passes the record to `Cursor_SyncPosition` while `Cursor_FollowsMouse` (`00094c5b`) is set ([the pages a view change moves](cockpit-views.md#cockpitview_stepviewtransition-00022904)) and queues it when *t* is past `CockpitMouse_LastEventTime` (`00094cc8`); when they differ, it stores them there and queues the record. Either way *t* becomes `CockpitMouse_LastEventTime`. So a button change always goes onto the queue, and a move at most once a count of the clock, 60 times a second. ES2's (`00452cb4`) reads the coarse ticks and stores the time whether or not the queue is live.

**`CockpitMouseQueue_Push` (`00044830`)**, (time, record), also entered at its padding `0004482d`, ES2's (`00453034`): returns 0 when the back queue holds 99 records; otherwise appends one and returns 1:

| Bytes | Field |
|---|---|
| 4, 4 | the pointer's *x* and *y* |
| 2 | the buttons: bit 0 left, 1 right, 2 middle |
| 4 | the time, in counts of the clock `000981c4` |

`es2_xref.py` finds calls from `CockpitMouse_OnEvent` and from `Input_BuildPlayerDevice`'s playback, which queues each recorded record with its recorded time.

## The frame's mouse events — `CockpitMouse_ProcessQueue` (`000444d0`)

No arguments; `Input_BuildPlayerDevice` calls it once a tick, recording or playing back or neither ([the input build](input.md#the-input-build--input_buildplayerdevice-0004cb14)). ES2's ([once per frame](../../retail/simulation/cockpit-input.md#4-once-per-frame-the-real-clickpressdrag-logic)) on `Widget_ActiveRoot`. It swaps `CockpitMouseQueue_Back` and `CockpitMouseQueue_Front`, empties the new back queue and, for each record of the front one, in order:

1. `Widget_ClickButtons` = the record's buttons.
2. **A move.** When the point differs from `CockpitMouse_LastPoint` it stores it there; during a replay (`TapePlayback`) with `Cursor_FollowsMouse` set it passes the record to `Cursor_SyncPosition`; it stores the point less the root's viewport rect origin (`+0x227`, `+0x22b`) in `CockpitMouse_CanvasPoint` (`000b16d4`). Then, with no drag captured, when `Widget_HitTest`(root, point) passes, `Widget_TrackPressedWidget`(root, point); with one captured, it calls the captured widget's drag slot `+0x1c` with the point less the root's rect origin and repaints it.
3. Clears `CockpitMouse_DoubleClick` (`00094c5c`). A left press, bit 0 set with `CockpitMouse_LeftHeld` clear, sets that byte, stores the canvas point again and calls `Widget_OnMouseDown`; a right press sets `CockpitMouse_RightHeld` alone, so the right button presses nothing.
4. A left release, bit 0 clear with `CockpitMouse_LeftHeld` set, clears that byte, sets `CockpitMouse_DoubleClick` when the record's time less `CockpitMouse_LastLeftRelease` (`00094c60`) is under `CockpitMouse_DoubleClickTicks`, stores the time there and sets `Widget_ClickButtons` to the buttons with bit 0; a right release does the same with `CockpitMouse_RightHeld`, `CockpitMouse_LastRightRelease` (`00094c64`) and bit 1, after the left's.
5. After either release, it stores the canvas point and, with no drag captured, calls `Widget_OnMouseUp`; with one captured, it puts the captured widget in state 0, repaints it, clears `Widget_DragCapture`, calls its click slot `+0xc` with what its value slot `+0x14` returns, and sets `Widget_PressedIndex` to −1.

It returns the front queue, its `int32` count and its buffer, which the recording appends to the tape ([recording and playback](input.md#recording-and-playback)). ES2's clears its button word after each record, where ES1's keeps the last record's until the next. `es2_xref.py` finds `CockpitMouse_CanvasPoint` pushed by `Gunsight_ClickGadget_OnClick` (`000337dc`), `0003b088` and `0004208c`, and `CockpitMouse_DoubleClick` read by `0003a46c`, `0003ac04` and `0003fed8` ([Open](#open)).

## The slider base

ES2's slider functions ([the symbol reference](../../retail/simulation/cockpit-input.md#symbol-reference)) on the same offsets from `+0x20`: the scale `+0x20` (Q16 pixels a unit), the range's `int16` minimum `+0x24` and maximum `+0x26`, the knob's `int16` position `+0x28`, its previous `+0x2a` and its length `+0x2c`, and the knob's rect `+0x2e`–`+0x3d`. Each pair is a horizontal one and then a vertical one:

| ES1 | ES2 | Does |
|---|---|---|
| `SliderWidget_DragToPointH` (`00043a68`), `SliderWidget_DragToPointV` (`00043b80`) | `004524f8`, `004525d8` | (slider, point): saves the knob at `+0x2a` and puts it at the point's *x*, clamped to *x0* … *x1* − length, or its *y*, clamped to *y0* + length … *y1* |
| `SliderWidget_GetValueH` (`00043ac8`), `SliderWidget_GetValueV` (`00043be8`) | `00452544`, `00452628` | (slider): ((the knob rect's *x0* − *x0*) ≪ 16) / scale + minimum, or the same from the *y0*s |
| `SliderWidget_SetValueH` (`00043ae4`), `SliderWidget_SetValueV` (`00043c08`) | `0045255c`, `00452644` | (slider, value): clamps the value to the range, puts the knob at *x0* + ((value − minimum) × scale ≫ 16), or *y1* less that, and calls the click slot `+0xc` with the value |
| `SliderWidget_RecomputeScaleH` (`00043b4c`), `SliderWidget_RecomputeScaleV` (`00043c74`) | `004525a8`, `00452694` | (slider): scale = ((*x1* − the knob rect's width − *x0*) ≪ 16) / (maximum − minimum), or the same over the heights |

`es2_xref.py` finds each horizontal one in nine stored pointers and each vertical one in three, `SliderWidget_RecomputeScaleH` called from `000386a0`, `0003ba74` and `000469ec`, and `SliderWidget_RecomputeScaleV` from `0003b2c8`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Widget_HitTest`, `Widget_OffsetRect` | `000437d0`, `0004387c` | `00452388`, `0045240c` |
| `SliderWidget_DragToPointH`, `SliderWidget_GetValueH`, `SliderWidget_SetValueH`, `SliderWidget_RecomputeScaleH` | `00043a68`, `00043ac8`, `00043ae4`, `00043b4c` | `004524f8`, `00452544`, `0045255c`, `004525a8` |
| `SliderWidget_DragToPointV`, `SliderWidget_GetValueV`, `SliderWidget_SetValueV`, `SliderWidget_RecomputeScaleV` | `00043b80`, `00043be8`, `00043c08`, `00043c74` | `004525d8`, `00452628`, `00452644`, `00452694` |
| `CTLWindow_Ctor`, `CTLWindow_Slot00NoOp`, `Widget_TranslateRootForView` | `00043cac`, `00043d96`, `00043d9c` | `004526c4`, `00452aad`, `00452734` |
| `Widget_OnMouseDown`, `Widget_OnMouseUp`, `Widget_TrackPressedWidget` | `00043e00`, `00043eac`, `00043f68` | `004527a0`, `00452870`, `00452954` |
| `Widget_HitTestChildren`, `Widget_NotifySelfAndChildren`, `Widget_Repaint` | `0004401c`, `000440a0`, `0004410c` | `00452a00`, `00452a48`, `00452a90` |
| `HeapPtr_DtorCockpitMouseCopy`, `ConfigPart_DtorCockpitMouseCopy`, `ConfigPart_GetClassTagCockpitMouseCopy`, `TSPartBase_DtorCockpitMouseCopy` | `00044130`, `00044158`, `00044197`, `000441a0` | the run ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no reference to `00044130` |
| `CockpitMouse_Init`, `CockpitMouseEvent_ElementCtor`, `CockpitMouse_Shutdown` | `000441cc`, `000442b4`, `000442fc` | `00452abc`, `00453171`, the code at `00452b7c` |
| `Widget_ClearPressed`, `Widget_NotifySelfAndChildren_Thunk`, `Widget_NoOp`, `Widget_TranslateRootForView_Thunk` | `00044310`, `00044328`, `00044338`, `0004433c` | `00452b94`, `00452bac`, `00452bbc`, `00452bc4` |
| `WidgetRoot_Delete`, `CTLWindow_Slot00NoOpCockpitMouseCopy`, `GLViewport_DtorCockpitMouseCopy`, `GLBase_DtorCockpitMouseCopy` | `00044350`, `00044383`, `00044388`, `000443b0` | `00452bdc`; the copies' slots ([the root widget](#the-root-widget--ctlwindow_ctor-00043cac)) |
| `Widget_RegisterClickable`, `Widget_Show`, `Widget_Hide` | `000443d4`, `00044404`, `00044434` | `00452c44`, `00452c64`, `00452c8c` |
| `CockpitMouse_OnEvent`, `CockpitMouse_ProcessQueue`, `CockpitMouseQueue_Push`, `CockpitMouse_StaticInitNoOp` | `00044464`, `000444d0`, `00044830`, `0004487c` | `00452cb4`, `00452d18`, `00453034`, none |

`CTLWindow_Slot00NoOp`, `ConfigPart_GetClassTagCockpitMouseCopy`, `CockpitMouse_Shutdown`, `Widget_NoOp`, `CTLWindow_Slot00NoOpCockpitMouseCopy` and `CockpitMouse_StaticInitNoOp` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `Widget_ClickButtons`, `int16`, what a button's value slot returns | `00094b84` |
| `CTLWindowVtable`, `CTLWindowVtableCockpitMouseCopy` | `00094bde`, `00094c6a` |
| `CockpitMouse_CallbackSlot`, `int16` | `00094c52` |
| `Widget_ActiveRoot` | `00094c54` |
| `Widget_PressedIndex`, `int16` | `00094c58` |
| `Widget_DragCapture`, `Cursor_FollowsMouse`, `CockpitMouse_DoubleClick`, bytes | `00094c5a`, `00094c5b`, `00094c5c` |
| `CockpitMouse_LastLeftRelease`, `CockpitMouse_LastRightRelease` | `00094c60`, `00094c64` |
| `CockpitMouse_LastEventTime` | `00094cc8` |
| `CockpitMouse_LastPoint`, `CockpitMouse_CanvasPoint`, two `int32` each | `000b16c0`, `000b16d4` |
| `CockpitMouse_LeftHeld`, `CockpitMouse_RightHeld`, bytes | `000bd50e`, `000bd510` |
| `CockpitMouseLive`, byte | `000bd512` |
| `CockpitMouse_DoubleClickTicks` | `000bd518` |
| `CockpitMouseQueueA`, `CockpitMouseQueueB`, a count and a buffer each | `000bd520`, `000bd528` |
| `CockpitMouseQueue_Back`, `CockpitMouseQueue_Front` | `000bd530`, `000bd534` |
| `CockpitMouse_LastButtons` | `000bd538` |

## Open

- **Open:** the first file's functions not read here: `00043784`, `000438a4`, the button base table's slot functions `000439ae`–`000439cf`, `000439d4` to `00043a5e`, and the slider base's constructor, ES2's `SliderWidget_CtorBase` (`004524a8`).
- **Open:** the panels that make a root of their own and hand it the mouse: `00045988`, which calls `CTLWindow_Ctor`, and `00045f00` and `000460b8`, which call `Widget_ClearPressed`, and `000460b8` also `WidgetRoot_Delete`.
- **Open:** what `CockpitMouse_CanvasPoint` gives `0003b088` and `0004208c`, and the double-click's readers `0003a46c`, `0003ac04` and `0003fed8`.
- **Open:** what reaches `CockpitMouse_Shutdown`, `Widget_NotifySelfAndChildren_Thunk`, `Widget_NoOp`, `Widget_Show`, `Widget_Hide` and `HeapPtr_DtorCockpitMouseCopy`, to none of which `es2_xref.py` finds a reference.
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed, by eye: in the forward view a left click on the screen's bottom rows pans down to the heads-down view, and there a left click on the top rows pans back up, the root's origin having moved with the view. In DOSBox-X's debugger, `BP <CS>:00044830` set during a mission fires as the mouse moves or a button changes, with the dword at `[ESP+4]` the clock `000981c4`.
