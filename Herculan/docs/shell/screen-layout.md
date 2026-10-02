# Shell screen layout

How VSHELL puts a screen together: the canvas it authors in, where the layout numbers live, the widget tree behind every tab screen, and the shared resources the whole front end draws with. The economy behind the screens is in [`armory.md`](armory.md); the campaign state they read and write is in [`campaign-loop.md`](campaign-loop.md).

## The canvas is 640x480, and rects are inclusive

Every tab screen parents its widgets to a panel built with the rect `{0, 0, 0x27f, 0x1df}` — 0..639 by 0..479. Both corners are inclusive, which is why the far corner is one less than the dimension, and it is the convention every widget rect in the executable follows.

The shell has no second video mode. DBSIM ships each panel resource twice, `dba\`/`hba\` and `dfn\`/`hfn\`, and picks between them off a video-mode global; `SHELL0.VOL` ships one set of each, and its screens are authored at the size that set is drawn at.

## The layouts are executable literals, not data

`ServiceBay_BuildScreen` (`0043a944`, `wsrvbayi.cpp`) writes every widget rect as four immediates onto its own stack and hands the block to a widget constructor. The only files it reads are the strip's two button banks, `dba\mnu_bttn.dba` and `dba\online.dba`, through `TSBase_LoadDba`; no layout comes from a file. The same shape repeats across the other tab screens' builders.

The `gam\arm_*.dat`, `gam\rpr_*.dat` and `gam\arm_weap.dat` records are the exception, and they are narrower than they look: each positions one *content* panel against a frame of the matching `dba\` sheet ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#the-screen-layout-families)). Nothing in them describes the frame those panels sit in.

## The widget tree of a tab screen

Four levels, built in this order:

1. **The root**, textured with the backdrop bitmap the shell's global init keeps in `DAT_0046dcd4`, sized to its parent's rect rather than to a literal.
2. **A full-screen panel** at `{0, 0, 0x27f, 0x1df}`, which every other widget on the screen is parented to. Because it sits at the origin, a child's rect is also its canvas rect — parent-relative and absolute coincide for everything on the strip.
3. **The palette scope** at `{0, 0x1e, 0x27f, 0x1df}` — the canvas below the strip. It is parented to the shell's top-level window (`DAT_004810e4`, the root's own parent) rather than to the panel, and it is how the screen's palette is chosen; see [The palette](#the-palette).
4. **The strip itself**: one square button and eight tabs.

Widget fields the builders and the tab handlers write directly:

`WinButton_Ctor` (`00409788`) is the base every one of them goes through. It zeroes `+0x45`, sets `+0x49` to 1 and ORs `0x60` into the flags word at `+0x39`.

| Offset | Meaning |
|---|---|
| `+0x45` | the lit flag. The widget's own mouse handler toggles it 0/1, and the paint picks the button's face from it. A tab handler writes 1 and repaints before building its screen, which is what latches the active tab lit; `TabStrip_ClearAllLit` (`00439dcb`) clears it across all nine. On the palette scope, a different class, the same offset is a palette index instead |
| `+0x49` | 1 from the constructor, and the enable flag. The tab gate clears it on the three tabs training mode has no economy for, and the repair panel writes it alongside two greying colour fields on a test of whether the player can afford the button ([below](#the-condition-readout)) — moving with the greying, on an affordability test, is what makes it the enable flag rather than a style bit. The button's own paint reads it for one thing, whether the caption takes the pressed nudge; the base class's event handler ignores mouse events while it is clear, so a cleared widget swallows a click on it ([below](#which-widget-a-click-reaches)) |
| `+0x51` | 1 from `ESRect_Ctor`, and the gate on drawing any chrome at all: `ESRect_FillAndBorder` (0040a726) returns immediately when it is clear. Written 0 on both the root and the full-screen panel, which is how each shows its bitmap with no fill and no border. Not the button field of the same offset — different class, different layout past the base |

## What a tab click does

Every tab has its own handler, and the eight are the same function with three or four lines changed. Each opens by returning if `DAT_0046c08c` is clear, and then by returning if `DAT_0047581c` — which tab is up — already holds its own index: **clicking the tab you are already on is a no-op**, before the teardown, the palette and the sound alike. What follows is, in order:

1. `TabStrip_ClearAllLit` (`00439dcb`) — clear the lit flag on all nine strip buttons and repaint them.
2. Write `+0x45` back to 1 on this tab and repaint it. **Only tabs 2 to 7 do this**; the main menu's and the save screen's handlers skip it and hide the strip instead ([below](#tabs-0-and-1-hide-the-strip)).
3. `00439ea7` — tear down whatever tab is currently up, dispatching on `DAT_0047581c`.
4. `0043b162(tab)` — install the tab's palette.
5. `0043cfe7(tab)` — build the shared squad roster panel, on tabs 2, 3, 4 and 6 only. Neither ARMORY nor MISSION has one.
6. The screen's own builder.
7. `DAT_0047581c = tab`, then `ShellSound_PlayTabClick` (`0042ee89`) — [the tab click](#sound). Tab 6's handler stores its index before its builder instead, so the crew screen's entry already runs as tab 6 ([below](#entering-the-crew-screen)).

| Tab | Handler | Builds | Roster |
|---|---|---|---|
| square button | `00439f2d` | `004317ea` (`wmain.cpp`) | |
| 0 `MAIN MENU` | `00439fdc` | `004310a0`, after `Game_SaveSlot(10, NULL)` | |
| 1 `SAVE` | `0043a0d8` | `00439b0c`, after `DAT_0048d344 = 8` | |
| 2 `WEAPONS` | `0043a1d2` | `0043f548` | yes |
| 3 `REPAIR` | `0043a2d2` | `004332ec` | yes |
| 4 `BUILD` | `0043a3d2` | `0044690d` | yes |
| 5 `ARMORY` | `0043a4d2` | `004494f7` | |
| 6 `CREW` | `0043a5ca` | `00441a01` | yes |
| 7 `MISSION` | `0043a6ca` → `0043a857` | `004441e3` ([below](#the-mission-screen)) | |

### Tabs 0 and 1 hide the strip

Tabs 0 and 1 take a different route to their palette: instead of `0043b162` they call `00439da0(1)` directly and then `0043b23d`, which **hides** the frame's root (`0048d440`) and the full-screen panel (`0048d448`) and installs palette 1 itself. Every strip button is that panel's child, so the strip goes with it: the main menu and the save screen each stand alone over their own backdrop-textured root, and are left only through their own buttons. The save screen's way back is [its EXIT and RESTORE](#leaving-the-save-screen), which undo exactly this.

**The shell starts with the strip hidden too.** `ServiceBay_BuildScreen` hides the full-screen panel as it builds it, and the frame's root is an image panel, which is [born hidden](#showing-and-hiding-a-widget), so nothing of the frame shows until something runs `0043b162(8)` and the strip refresh: RESTORE, EXIT back to the strip, `CONTINUE GAME` or a campaign mission's load.

Returning to the main menu **autosaves**: tab 0's handler calls `Game_SaveSlot(10, NULL)` after the teardown and the strip's hide, before building the menu, and slot 10 is the campaign-or-training current-game slot ([`../formats/save-games.md`](../formats/save-games.md)).

## The tab gate

`0043b0c8` is the strip refresh, and it writes `+0x49` on five tabs from `DAT_0048260c`, the campaign/training mode flag — training being the mode the [practice missions](#the-practice-missions-screen) and `INSTANT ACTION` run in:

| Tab | Campaign | Training |
|---|---|---|
| 2 `WEAPONS` | on | on |
| 3 `REPAIR` | on | off |
| 4 `BUILD` | on | off |
| 5 `ARMORY` | on | off |
| 6 `CREW` | on | on |

The three it gates are exactly the three that spend salvage, and training mode has no salvage economy ([`armory.md`](armory.md)). It writes those five and no others, so `MAIN MENU`, `SAVE`, `MISSION` and the square button are live in both. It also shows the strip's panel and parks `DAT_0047581c` at `0xffff`, so whatever tab is clicked next cannot be mistaken for the one already up.

`ServiceBay_BuildScreen` clears `+0x49` on tab 5 as it constructs it, which the refresh then overwrites either way.

## The tab strip

All nine buttons share the top and bottom edges `4` and `0x1b`. The tabs are 75 pixels wide on a 76-pixel pitch — butted one pixel apart.

| Widget | Rect | Caption | Art |
|---|---|---|---|
| square button | `{7, 4, 0x17, 0x1b}` | `?`, the literal at `00475dcd` rather than an `estext.bin` entry | `dba\online.dba` frames 0 and 1 |
| tab 0 | `{0x19, 4, 0x63, 0x1b}` | `0x13` `MAIN MENU` | `dba\mnu_bttn.dba` frames 1 and 2 |
| tab 1 | `{0x65, 4, 0xaf, 0x1b}` | `0x14` `SAVE` | as tab 0 |
| tab 2 | `{0xb1, 4, 0xfb, 0x1b}` | `0x15` `WEAPONS` | as tab 0 |
| tab 3 | `{0xfd, 4, 0x147, 0x1b}` | `0x16` `REPAIR` | as tab 0 |
| tab 4 | `{0x149, 4, 0x193, 0x1b}` | `0x17` `BUILD` | as tab 0 |
| tab 5 | `{0x195, 4, 0x1df, 0x1b}` | `0x18` `ARMORY` | as tab 0 |
| tab 6 | `{0x1e1, 4, 0x22b, 0x1b}` | `0x19` `CREW` | as tab 0 |
| tab 7 | `{0x22d, 4, 0x277, 0x1b}` | `0x1a` `MISSION` | as tab 0 |

Those captions corroborate three of the assert-string module identifications independently: tab 2 `WEAPONS` is the screen `warmingi.cpp` builds, tab 5 `ARMORY` is `warmoryi.cpp`'s and tab 6 `CREW` is `wcrewi.cpp`'s, each reached through that tab's own handler.

`ESButtonBitmap_Ctor` (`00409d14`) stores three frame pointers, at `+0x51`, `+0x55` and `+0x59`, and **a button has only two faces**: both of the class's paints — `ESButtonBitmap_Paint` (`0040a05d`) and the subclass's `ESRadioButton_Paint` (`0040a26d`) — pick between the first two and never read the third. Every button on the strip passes `mnu_bttn` frame 3 as that unread third, including the square one whose two faces come from a different bank.

The caption is an embedded `Text` child at `+0x4d`, sized to the button's full client rect, drawn in the font at `DAT_0046dccc` and fetched with `WeaponsBin_LookupName(DAT_0046dcc0, index)` from `estext.bin`. The paint places it one pixel above its vertically-centred row when the button is idle and one below when it is lit, so a lit caption sits two pixels lower than an idle one — the pressed nudge. The centring itself is a baseline, `(Font_CellHeight(text +0xb1) + rectHeight + 1) / 2`: `+0xb1` is the `Text` widget's font handle and `Font_CellHeight` (00453fa8) returns that font's glyph cell height ([above](#text-placement-and-colour)).

The strip is rebuilt by each tab screen's own builder at these same coordinates rather than shared between them.

## Showing and hiding a widget

**The dump's two names are the wrong way round.** `Window_ShowRecursive` (0041f2e6), which the dump calls `Window_HideRecursive`, is the **show**; `Window_HideRecursive` (0041f469), which the dump calls `Window_ShowRecursive`, is the **hide**. Bit 2 of the flags word at `+0x11` is a *hidden* bit: `Window_HideRecursive` sets it and `Window_ShowRecursive` clears it. The show returns at once on a widget already visible; the hide, on one already hidden, only clears bit 4, described below.

Three things say so independently:

- **`ESMessage_Paint` (`0040b439`) draws only when `(+0x11 & 2) == 0`.** A widget that paints when the bit is clear is visible when the bit is clear, so the call that sets the bit is the hide.
- **The repair tab's entry and teardown are a matched pair.** `Repair_Enter` (004332ec), which tab 3's handler calls to bring the screen up, calls `Window_ShowRecursive` on its content panel and both list panels; `Repair_Leave` (004333eb), which the teardown dispatcher `00439ea7` calls on the way out, calls `Window_HideRecursive` on the same three. Under the dump's names an entry routine would hide its own screen and a teardown would show it.
- **The repair screen's two pictures swap with the selection.** `Repair_SwapDiagram(oldColumn, newColumn)` (0043393d) calls `Window_ShowRecursive` on the internals diagram when the selection moves into the internals list and on the exploded external picture when it moves back ([below](#the-damage-diagram)) — the right way round only if that function is the show.

**Every widget is born hidden.** `Window_SetRect`, which every constructor goes through, ends by setting bit 2. A sweep of the disassembly for stores to `+0x11` finds, besides that one, `ESWindow_Ctor` (`00409970`) zeroing the word before it calls `Window_SetRect`, the show, the hide, the child-list operations (bit 1 only) and the display root's constructor; `Display_SetCursor`'s store is to the display object's own `+0x11`, not a widget's. The constructors then differ. `WinButton_Ctor` (and with it every `Panel`, `Button`, `ButtonIcon` and `Grid`), `ESMessage_Ctor` and `ESDialog_Ctor` (0040bbf4) end with `Window_ShowRecursive`; `ESTitle_Ctor` and `ESBitmap_Ctor` end with `Window_HideRecursive`; a widget built by `ESWindow_Ctor` alone — the palette scopes, the top-level window, the dialogs' full-display windows — calls neither and stays hidden until something shows it by name. So a screen's panels are constructed dark and its entry routine is what puts them up.

Both consult the widget's parent, which `Window_Parent` (0041f283) finds by walking the `+9` chain while a node's bit 1 is clear: `+9` holds the previous sibling, and only on the first child in a list, which has bit 1 set, the parent. While that parent is itself hidden, the show sets bits 2 and 4 and returns, and the hide sets bit 4 along with bit 2; the hide of a widget already hidden clears bit 4. A show that runs clears both bits and then shows every child carrying bit 4 — so bit 2 is whether the widget is hidden and bit 4 that it comes back up with its parent, which is what lets a subtree come back up in the state it went down in.

## Which widget a click reaches

Input reaches widgets through an event queue, not directly. A mouse change becomes an event of type `0x20` with a sub-code at `+0x24`, which `Mouse_OnButtonState` (00408b03) assigns: 0 a move, 2 and 1 the left button going down and up, 4 and 3 the right. `EventQueue_Pump` (00469ba4) drains the queue.

**A move picks the target and a button event goes to it.** On a move the pump hit-tests from the display's root with `Widget_HitTestTree` (00469d1c). A widget is hit only when it is not hidden (`+0x11` bit 2, [above](#showing-and-hiding-a-widget)) and the point lies inside its absolute rect, edges included. Its children are then tried in list order, and the first that is hit answers in its place, recursively; a widget none of whose children is hit is the answer itself. When the answer changes, `Pointer_Leave` (00469d74) sends the old widget a leave event (`0x10`) and `Pointer_Enter` (00469e1c) sends the new one an enter (8). A button event carries no position test of its own: it goes to whatever the last move left under the pointer, and so does any other event posted without a target, a keystroke included.

**The pointer can be locked.** While `+0x1f` of the pointer state at `DAT_005ddbd0` is set, the pump skips the hit test, so a move changes nothing and every event goes to the current target. Only edit fields set it — `ESDialog_HandleEvent` on a press ([below](#the-widget-that-takes-a-click-decides-what-it-does)), `SaveScreen_BeginRename` (004377d2) and `0043bc0a` — and `Pointer_Unlock` (00469cdc) clears it and hit-tests again.

**Siblings are tried newest first.** `Window_AttachChild` (0041f134), which `Window_SetRect` calls from every constructor, pushes a child onto the *head* of its parent's list. The only other list operation, `Window_Detach` (0041f17a), is called by two teardowns that delete what they unlink, so no list is ever reordered. Where two siblings overlap, the one built later answers, and a child can be hit only where it lies inside its parent.

**The event then climbs to the first widget that takes mouse events.** `Event_Deliver` (00469f34) walks from the hit through `Window_Parent` to the first widget whose event mask at `+0x39` has the event type's bit, and calls that widget's vtable slot 0 with the point made relative to the hit. `Window_SetRect` starts the mask at `0x1f` and `WinButton_Ctor` adds `0x60`, which carries the mouse bit. `ESMessage_Ctor` is built on `ESWindow_Ctor` rather than `WinButton_Ctor` and clears `0x18`, leaving `0x07`. **A `Text` therefore never takes a click**: a click on a caption, a label or a value reaches its parent. `Panel` and everything built on it, `Button` among them, goes through `WinButton_Ctor`, and `ESDialog_Ctor` adds `0x360` itself, so all of those take clicks.

**The innermost widget is part of the target.** The leave event climbs the same way, so when the pointer moves from one of a row's text columns to the next, the leave sent to the first reaches the row, and puts out a press on it exactly as leaving the row would.

What that decides on the screens ported here:

| Overlap | Who answers |
|---|---|
| a [repair hotspot](#the-damage-diagram) over another | the six body areas are built first and the weapons after, each in index order, so a weapon over a body area, a higher mount over a lower one, a higher area over a lower one |
| rows 13 tall on a 12-pixel pitch — the save list, both repair lists | the lower row owns the shared border line |
| a [crew row](#the-rows)'s texts | the row; its portrait is an image panel of its own carrying the row's handler |
| a row's four [text columns](#a-row-is-four-text-columns), a button's caption | the row, the button |
| the repair screen's five [readouts](#the-repair-screen) | the readout, which is disabled and swallows it |
| the weapons screen's [picture box](#the-weapons-screen) and a picture in it | the box, which is disabled and swallows it; a picture's image panel has no handler and swallows it too |

### The widget that takes a click decides what it does

Slot 0 of a class's vtable is its event handler, and the classes on the shell's screens that take mouse events run the seven below, counting `Button`'s as the base's. Firing is `Window_DispatchCallback` (0041f5d4), which runs the handler at `+0x3d` — the constructor's handler argument — or discards the event when there is none. No handler hands an event on to the parent, so a widget with no handler, or a disabled one, swallows a click on it.

| Handler | Classes | Fires on | Needs the press on it | A leave cancels | Tests |
|---|---|---|---|---|---|
| `WinButton_HandleEvent` (004097da) | `Panel`, `FramedPanel`, `TitledPanel`, `Grid`; `Button` through `ESButtonFont_HandleEvent` (00409b0f), which adds the press sound | either button's release | yes | yes | `+0x49`, `Avi_Playing`, `MovieQueue_Running` |
| `ESArm_HandleEvent` (0040c3b5) | `HatchedDivider` | either button's release | yes | yes | `+0x49` |
| `ESButtonBitmap_HandleEvent` (00409df2) | `ButtonIcon` (RTTI `ESButtonBitmap`), the tab strip | the left press; the right release | the right button only | no | `+0x49`, `Avi_Playing`, `MovieQueue_Running` |
| the same, with `+0x61` set and `+0x5d` clear | the mission screen's arrows | the left release, and every 500 ms while held; the right release | the right button only | yes | the same |
| `ESRadioButton_HandleEvent` (0040a139) | the [checkbox](#the-preferences-screen) | the left press; the right release | the right button only | no | `+0x49`; the right button also the two movie flags |
| `ESBitmap_HandleEvent` (0040b6da) | image panel | the left release | no | — | none |
| `ESDialog_HandleEvent` (0040beaf) | edit field | the left press | — | — | none |
| `ESAlert_HandleEvent` (0040b143) | `ESAlert` | nothing: it grabs and restores the screen under it on the show and hide and repaints, and drops every other event | — | — | — |

**`WinButton_HandleEvent` pairs a press with its release.** A press, either button, lights `+0x45` and repaints; a release with `+0x45` lit fires, then clears it and repaints; the leave event clears it. So the click fires on the release, and only when the button went down on the same widget and the pointer never left it. `ESArm_HandleEvent` is the same function without the two movie flags ([below](#input-while-a-movie-plays)).

**The strip fires on the left press.** `ESButtonBitmap_HandleEvent` lights `+0x45`, plays the press sound, repaints and fires, all on the press, while its auto-repeat flag `+0x61` is clear, as the constructor leaves it. The left release zeroes `+0x45` without repainting, which is why a tab stays drawn lit after the click that latched it: what is on screen is the paint the tab handler's own write of 1 triggered. The right button falls through to `WinButton_HandleEvent`: it lights on the press and fires on the release, and then zeroes `+0x45` and repaints after the handler has run. So a tab picked with the right button has latched itself and is then repainted unlit, and right-clicking the tab already up unlights it; recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md). The leave clears `+0x45` only while `+0x5d` is 0, and `ESButtonBitmap_Ctor` sets it to 1, so a tab the pointer is dragged off stays lit, and a later right release on it fires it with no press.

**An auto-repeating `ButtonIcon` fires on the left release instead.** With `+0x61` set the left press lights the button, plays the press sound and repaints without firing, and the left release puts it out, repaints and fires, wherever the press was. Event types 1 and 2 install and remove a WinTimer alarm for the widget at 500 and 500, and each tick of it (event `0x200`) while the button is lit, enabled and not hidden adds one to `+0x65` and fires again. A builder that also clears `+0x5d` lets the leave put the button out. The right button is `WinButton_HandleEvent`'s, as on the strip.

**A checkbox fires on the left press, as the strip does**, lighting `+0x45`, playing the press sound and repainting first, but it tests neither movie flag on that button, and its left release does nothing at all. A leave leaves it lit, `+0x5d` being the constructor's 1. The right button is `WinButton_HandleEvent`'s.

**An image panel fires on any left release that reaches it**, wherever the button went down.

**An alert swallows a click on its body.** `ESAlert_Ctor` (`0040afe0`) adds `0x60` to the mask, so a click on the alert outside its buttons climbs no further, and its handler drops it.

**An edit field fires on the left press, and takes the pointer.** With its focus flag `+0xa7` clear, the press sets it, installs a WinTimer alarm for itself (`WinTimer_InstallAlarm`, 0046a0b0, with 500 and 500), [locks the pointer](#which-widget-a-click-reaches) and fires. Every later event then goes to the field, and the next press, landing there with `+0xa7` set, clears the focus, removes the alarm, releases the lock, hit-tests again and posts the press over, so it lands on whatever is under the pointer — the field itself included, which then fires again. The field ignores the right button, and while the lock holds a right click anywhere reaches the field and is dropped.

### The shell's movies

`avi.cpp` plays the shell's movies through MCI's `avivideo` device. `Movie_Enqueue` (0041e29c) adds one to a ten-entry ring at `00485668` when movies are on (`DAT_00482275`, which `-a` clears) and no entry in the ring already carries its id: the id, a rect, a palette index (`0xffff` for none), a flag that brings the location picture up after it, and a callback, which every caller passes as null. Nothing stops a write landing on an entry not yet played. `Movie_PlayQueue` (0041e368) plays the ring out. The shell's main loop (`Shell_Main`, `00401525`) calls it once a pass, after the widgets have had their events and before `ShellMap_RunIntro`; the startup (`Shell_BuildScreensAndStart`, `004012b0`), `Game_ProcessMissionResults` and `MainMenu_OnCredits` (`004315ec`) also call it straight after enqueuing, while `Mission_Show` and `maybe_Mission_UpdateLocationTab` enqueue and leave the playing to the main loop.

**The id indexes the table at `00470e74`** of 86 `avi\` paths, unchecked: `pt1`-`pt6`, `rc1`-`rc5`, `as1`-`as7`, `es1`-`es4`, `rs1`-`rs4`, `sc1`-`sc5`, `rd1`-`rd4`, `sp1`, `gd1`-`gd4`, `ex1`-`ex4`, `rf1`-`rf4`, `co1`-`co4`, `fl1`-`fl4`, `sk1`-`sk4`, `sv1`-`sv4` and `hc1`-`hc4` for ids 0 to `0x43`, then `intr_pt1`, `intr_pt2`, `c1`-`c5`, `alph_th`, `delt_th`, `omic_th`, `brav_th`, `luna`, `transm3`, `end1a`, `death`, `victory`, `credits` and `dropship` for `0x44` to `0x55`. `Path_UnderDriveCfg` (`0040d429`) puts the directory `data\drive.cfg` names in front. `ALPHA`, `BRAVO`, `DELTA`, `OMICRON`, `ESTAB2`, `ES2CREDC` and `ES2DROP3` sit in `AVI\` and are not in the table.

| Caller | Movie | Rect | Palette |
|---|---|---|---|
| the startup | `0x44`, `0x45`, the intro | full | none |
| `Mission_Show`, map view, once (`DAT_004778aa`) | `stage + 0x45`, `c1`-`c5` | Telecomm | 3, or 4 once `stage - 1 > 3` |
| | `stage + 0x4a`, the theater's thumbnail, `luna` at stage 5 | map panel | none; the location flag set |
| `Mission_Show`, briefing, once per load (`DAT_004778ab`) | `Career_BriefingMovie` | Telecomm | `stage + 4` |
| `Mission_Show`, debrief, once per load (`DAT_004778ac`) | `Career_DebriefMovie` | Telecomm | `stage + 9` |
| `maybe_Mission_UpdateLocationTab`, stage 5 | `0x55`, the lunar drop | full | none |
| `Game_ProcessMissionResults`, the campaign won | `0x53` and `0x54`, the ending and the credits | full | none |
| `MainMenu_OnCredits` | `0x54` | full | none |

The rects are left, top, width and height, which `Avi_MoveWindow` (`0041dfd6`) hands to `MoveWindow` for the movie's window: full is `{0x20, 0x3c, 0x240, 0x168}`, 576x360 centred on the canvas; Telecomm is `{0x15, 0x56, 0xef, 0xb3}`, over the Telecomm picture; the map panel is `{0x122, 0x43, 0x127, 0xe2}`. The briefing's id is the career block's last short ([`../formats/save-games.md`](../formats/save-games.md#career-block--152-bytes)), which `Career_SetBriefing` (`00412ece`) writes with the three text arrays when a mission is loaded. The debrief's, `004840ba`, is written only by `Career_SetDebriefLines` for the mission just flown, and the save does not carry it. `Game_LoadSlot` and `Game_NewCareer` clear the briefing's and the debrief's flags. Neither touches the map's, which [RESTORE](#leaving-the-save-screen) and the registration screen's [ACCEPT](#starting-a-campaign) clear.

**`Avi_Play` (0041e01c) plays one movie.** It opens the file as a child of the main window (`open %s alias mov style child parent %d`), moves the movie's window to the rect, sets its palette (`setvideo mov palette handle to %d`), drops [the hourglass](#the-pointer), captures the mouse, sets `Avi_Playing` and plays with `notify`. It then polls the keyboard until `Avi_StopRequested` is set — by Esc or Space (scan codes 1 and `0x39`), by a mouse button going down in the window procedure, or by the `MM_MCINOTIFY` with `MCI_NOTIFY_SUCCESSFUL` that the movie's end posts — and closes the movie, releases the mouse and clears `Avi_Playing`. The device is opened for each movie by `Avi_OpenDevice` (`0041def7`) and closed after it. When `Movie_PlayQueue` was called with 1, the open first builds the palette handle (`Avi_PaletteHandle`, `00485664`) with `Avi_BuildPalette` (`0041de68`) from entries 10 to 245 of the palette installed then. The main loop and `Game_ProcessMissionResults` pass 1; the startup and `MainMenu_OnCredits` pass 0, and with 0 the handle is left as it is — still 0 for the intro, and for the credits from the main menu the last movie's, or 0 when none has played ([Open](#open)).

**`Movie_PlayQueue` plays the entries in turn**, fading the music out and stopping it before the first ([What plays each sound](#what-plays-each-sound)). For each entry:

1. An intro part (`0x44`, `0x45`) sets `MovieQueue_PlayingIntro` (`00470fe4`). The entry's palette is installed unless it is `0xffff`, the entry is an intro part or it is the credits.
2. Unless the entry is the lunar drop, while the frame's panel (`ShellPanelWidget`) is up every tab is unlit but MISSION, which is lit, and the mission screen's panels are shown again.
3. The hourglass goes up and the movie plays. An intro part is skipped once a mouse button, Esc or Space has gone down during an intro movie, which sets `MovieQueue_IntroSkipped` (`00470fe0`) ([Open](#open)). When the open fails, an intro part puts up `Please insert ESII CD and restart` and ends the shell, and any other movie puts up the insert-CD panel (`DAT_0048d108`), clears `MovieQueue_Running` until its button is pressed, and tries again.
4. Full screen, the screen is blanked after an intro part or the credits.
5. After the lunar drop, with the frame's panel up, palette 1 goes in through the scope and the frame's root is repainted. After the credits palette 1 is installed.
6. With the location flag set, `Mission_Leave` takes the mission tab down, MISSION is unlit, `DAT_0047581c` is parked at `0xffff`, the music is started and faded in, and `maybe_Mission_UpdateLocationTab` (`0044409f`) runs. Below stage 5 it puts the location picture up — frame 0 of `dba\alph2`, `delt1`, `omic1` or `brav1`, indexed by `stage - 1` (`MissionLocationDbaTable`, `00477fcc`), 640x480 over the whole window — through the theater palette `stage + 0xe`. At stage 5 it queues the lunar drop in its place, installs palette 2 through the scope, and fades the music out and stops it.
7. With the location picture up, the shell waits two seconds without pumping messages (`Shell_BusyWaitSeconds(2)`, `00401d53`), takes the picture down, repaints the frame's root and installs palette 1.
8. The entry's callback runs and the entry is freed.

Once the ring is empty the music is started and faded in, unless step 6 started it and no lunar drop has played since.

### Input while a movie plays

Two flags gate input around the movies, each set and cleared by one of the two players ([Open](#open)):

| Flag | Set | Cleared |
|---|---|---|
| `Avi_Playing` (`00470d70`) | by `Avi_Play` as playback starts, with the main window capturing the mouse | as playback ends |
| `MovieQueue_Running` (`00470e70`) | by `Movie_PlayQueue` when it finds the ring holding a movie | when a later call finds the ring empty, and while the insert-CD panel waits for its button |

While `Avi_Playing` is set, the window procedure (`MainWndProc`, 00404a2c) drops both button-ups and the options hotkeys, and a button-down sets `Avi_StopRequested` (`00470d60`), which ends playback, and is dropped too: **a click skips the movie and does nothing else**, as Esc and Space do. Moves are still posted. `WinButton_HandleEvent` and `ESButtonBitmap_HandleEvent` also ignore every mouse event while either flag is set, which covers the whole run of the queue and not only the movies in it. `ESArm_HandleEvent`, `ESBitmap_HandleEvent`, `ESDialog_HandleEvent` and the checkbox's left button test neither; the checkbox's right button goes through `WinButton_HandleEvent` and so tests both.

`FUN_00444e28`, the click handler of [the mission screen's Telecomm picture](#the-mission-screen), reads `Avi_Playing` and would enqueue the briefing or debrief movie by the mission tab's view, but its first instruction after the prologue jumps to its epilogue.

### The pointer

**The shell's pointer is the Windows arrow.** VSHELL draws none of its own. `Shell_RegisterWindowClass` (`004062cb`) registers the main window's class with `LoadCursorA(NULL, IDC_ARROW)`, and `MainWndProc` passes `WM_SETCURSOR` to `DefWindowProcA`, so every move over the window puts the class's arrow up.

The widget layer carries a cursor too, and it adds nothing to that. The startup (`Shell_Main`, `00401525`) wraps what `GetCursor()` returns — the arrow — in two cursor objects, `DAT_004810e8` and `DAT_004810ec` (`ShellCursor_Ctor`, `0041f644`: vtable `00471844`, the handle at `+4`). It gives the first to the display root's `+0x35` and installs it, and `Hotspots_BuildOverlay` (`0043c1a0`) gives the second to every arming hotspot. `Pointer_Enter` installs the `+0x35` of the first widget up the parent chain that has one through the display's slot 3, `Display_SetCursor` (`0041fab3`, vtable `004717ec`), which calls `SetCursor` with it unless it is the one already installed. What reaches `SetCursor` there is the object's address rather than the handle at its `+4`.

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
| image panel (`ESBitmap`) | `0040b772` | one bitmap at an offset, under an unfilled border ([below](#the-crew-screen)) |
| `HatchedDivider` (`ESArm`) | `0040c513` | a body of horizontal lines and an optional inner border ([below](#the-crew-screen)) |
| `Grid` (`ESGrid`) | `0040b97c` | grid lines and thirty recolourable bitmap parts ([below](#the-damage-diagram)) |

`ESTitle_Paint` fills its header strip to `+0x55` for `+0x61` rows, then — when `+0x65` is set, which the constructor does and seven panels' builders undo straight after it (the repair screen's `0048d13c` and `0048d180`, the squad roster's `0048d4f8`, the weapons screen's `0048d598`, the mission screen's `0048d7d4` and `0048d7dc`, and the armory screen's `0048d97c`) — lays a **diagonal hatch** over it in colour 13: bands of fourteen 45-degree lines on a 28-pixel pitch, 26 bands from five pixels left of the widget. That is over 700 pixels of hatch for a panel a third as wide, and only the clip stops the surplus; the paint relies on clipping rather than measuring. It then punches the hatch back out to `+0x55` between `+0x6d` and `+0x71`, which is the **title plate** the caption reads against, draws the header's own side edges, and closes with a divider on row `+0x61`.

**`+0x59` chooses between a filled body and a dithered one.** Set, the body is cleared to `0x10`; clear, it takes a 50% checkerboard in `+0x5d` from the header height down — and over an unpainted surface that means the shell's single backdrop bitmap shows through at half strength. The save screen takes the second path, which is why the bay is visible through its panel.

### Text placement and colour

`Font_DrawString(font, {x, y}, text)` (0045409c) draws a run: it tests the context's `+0x231` for 1 and then for 2, so **0 is left, 1 is right and 2 is centred**, against the field width at `+0x235`; then it advances glyph by glyph. `Text` takes that mode from its own `+0x45`, so a label's alignment is a constructor argument. The save screen's detail panel builds every label right-aligned and each single value left-aligned, which is what makes a label's colon meet its value; its kill counters and column headers are centred.

The `y` is an **ink baseline**: `Font_DrawGlyph` (00453fb4) places each glyph's top row at `y - font[+0x16]`, and `+0x16` is the `.DFN` header's `inkHeight`. `Font_CellHeight` and `Font_CellHeightCopy` (`00453f9c`) both return `font[+0x0a]`, the glyph cell height. The two classes centre differently and neither is derived from the other — `Text` uses `H - (H + 1 - cellHeight) / 2 - 2` and the edit field `cellHeight / 2 + (H + 1) / 2`.

**A widget picks its text colour by remapping, not by choosing a pen.** The fonts carry one ink index each ([`../formats/dfn-hfn-dci.md`](../formats/dfn-hfn-dci.md)) and the shell's is `0x29`, so both text paints draw the string in whatever the font has and then re-blit the area through an identity table with entry `0x29` replaced — by `Text`'s `+0xb5`, or the edit field's `+0xbb`. One font therefore serves a label at `0x1a`, a value at `0x29` and a resting list row at `0x27`, and a selection highlight costs nothing but a different replacement.

`Text`'s `+0xc1` is an opaque-background flag and `+0xc5` the colour it clears to: a value field clears its own rect so a refresh overwrites cleanly, and a static label does not.

## The main menu

Tab 0, `MAIN MENU`. Built once at shell startup by `MainMenu_BuildScreen` (`0043094c`, `wmain.cpp`), put up by `MainMenu_Show` (`004310a0`) and hidden by `MainMenu_Hide` (`0043114b`), which every button that leaves the menu calls first. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | its parent's own rect | the shared backdrop; `+0x51 = 0`, so no chrome |
| content panel | `TitledPanel` | `{0x90, 0xad, 0x1e2, 0x138}` | `2` `MAIN MENU`, header 19 tall, plate `0x6e`-`0xe4`, border `0x15`, face `0x25`, dithered body in `0x10` |
| 10 buttons | `Button` | below | border `0x22` |

The panel is the save screen's with a different rect, title plate and border — `0x15` where the save screen's is `0x27` — so its body is the same checkerboard over the bay.

Every button is `{x, y, x + 0x99, y + 0xf}` on a `0x16`-pixel pitch from row `0x1c`, in two columns at `x = 0xb` and `0xad`:

| Row | Left column | Handler | Right column | Handler |
|---|---|---|---|---|
| `0x1c` | `3` `INSTANT ACTION` | `004312a6` | `7` `PRACTICE MISSIONS` | `004318ab` |
| `0x32` | `4` `START NEW GAME` | `00431379` | `0xa` `PREFERENCES` | `0043150c` |
| `0x48` | `5` `CONTINUE GAME` | `MainMenu_OnContinue` (`004313e4`) | `0xb` `VIEW DEMO` | `0043156f` |
| `0x5e` | `6` `SAVE/RESTORE` | `MainMenu_OnSaveRestore` (`00431498`) | `0xd` `CREDITS` | `004315ec` |
| `0x74` | `0xf` `ONLINE MANUAL` | `0043178c` | `0xe` `QUIT` | `00431727` |

The builder constructs them in the order `3`, `4`, `5`, `6`, `0xe`, `0xf`, `7`, `0xa`, `0xb`, `0xd`. Three captions in the `estext.bin` run are not reached by it: `8` `CONTROLS`, `9` `VEHICLE PREVIEW` and `0xc` `SERVICE BAY`.

**`CONTINUE GAME` is gated on slot 10 being in use.** `MainMenu_Show` shows the root and the panel and then writes the [greying trio](#the-condition-readout) at `CONTINUE GAME` from the byte at `00482a19`. That is not a global of its own: the slot table at `00482610` has a `0x5e`-byte stride and keeps each slot's in-use byte at `+0x5d` — the byte the save screen's `RESTORE` is gated on — and `00482610 + 10 * 0x5e + 0x5d` is `00482a19`. So the button is live only once a current game has been written to slot 10. No other button is gated.

What the handlers call, as read:

| Button | Calls |
|---|---|
| `INSTANT ACTION` | `InstantAction_Active = 1` (`0047363c`), `Shell_SetCampaignMode(0)`, `InstantAction_SelectDemo` (`0044befb`, [below](#which-mission-a-row-is)), the screen blanked full screen or the palette scope shown and hidden in a window, `Game_NewCareer("TRAINEE", option 0x27)`, `Game_ExportMissionHandoff`, `Shell_SetExitCode(2)`, `Shell_QuitFlag = 1` |
| `START NEW GAME` | `MainMenu_Hide`, `Shell_SetCampaignMode(1)`, `Registration_Show` — [the registration screen](#the-registration-screen) |
| `CONTINUE GAME` | under [the hourglass](#the-pointer): `Shell_SetCampaignMode(1)`, `Game_LoadSlot(10, 1)`, selected save slot 10; then `MainMenu_Hide` and the bare frame (`0043b162(8)`, `0043b0c8`) when `DAT_0048260e` is 2, [the END OF GAME alert](#end-of-game) otherwise |
| `SAVE/RESTORE` | `MainMenu_Hide`, `Shell_SetCampaignMode(1)`, `DAT_0048d344 = 0`, `SaveScreen_Enter` — the [save screen](#the-save-screen), with `EXIT` set to come back here |
| `ONLINE MANUAL` | `OnlineManual_Open` (`004317ea`): out of full screen, option 6 set to 0, committed and all 54 saved, `ShellSound_Stop`, then `Shell_OpenHelp` (`004073a2`), `WinHelpA(window, path, HELP_CONTENTS, 0)`, on `<language>\es2guide.hlp` ([its format](../formats/winhelp.md)), chosen by the language letter `E`, `F` or `G` |
| `PRACTICE MISSIONS` | `MainMenu_Hide`, `PracticeScreen_Show` (`0044bc92`), `Shell_SetCampaignMode(0)` — [the practice screen](#the-practice-missions-screen) |
| `PREFERENCES` | `MainMenu_Hide`, `PreferencesScreen_Enter` — [the preferences screen](#the-preferences-screen) |
| `VIEW DEMO` | the screen blanked full screen, `Shell_SetExitCode(5)`, `Shell_QuitFlag = 1` |
| `CREDITS` | shows a bare window (`DAT_0048d0c4`) and plays movie `0x54` through `Movie_Enqueue` and `Movie_PlayQueue`, then hides it |
| `QUIT` | `Shell_QuitFlag = 1`, `Shell_BlankScreen` (`0040723d`) — [below](#quit) |

**`Shell_SetCampaignMode(mode)` (`0040e69e`) is the mode write.** It stores the campaign/training flag `DAT_0048260c`, sets `prefs.cfg` option 42 to it without running its handler, and saves that option alone, so the mode survives a restart ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)). The strip is hidden while the menu is up, and the tab gate follows the new mode at the next strip refresh.

**The menu first comes up at the end of a six-frame sequence**, unless the shell was started into a debrief that goes on to the next campaign mission or to [Replay mission?](campaign-loop.md#replay-mission). The builder also puts a widget over the whole window (`StartupAnimWidget`, `DAT_0048d0c0`, built by `ESAnim2_Ctor` (`0040c85c`) from `esanim2.cpp`, vtable `0046eec4`) and adds `dbm\bay2a_80` to `bay2a_84` to it, the last twice (`ESAnim2_AddFrame`, `0040ca06`). It is shown after the screen is blanked and palette 1 installed: by the startup once its movies are done, or at once on `-X6`; and on `-X3` or `-X4` by [the debrief](campaign-loop.md#the-debrief--game_processmissionresults-0040eae7), at the end of a training mission's and after the campaign's ending movies. The class's event handler, `ESAnim2_HandleEvent` (`0040c8b3`), answers the show (event 1) by installing a 500 ms alarm and clearing `DAT_0046c098`, the flag [`WM_CLOSE`](#quit) and the display keys wait on, and paints the current frame on event 4. Each tick (event `0x200`) while the widget is not hidden advances `+0x6d`, wrapping at the frame count, paints that frame and runs the builder's handler, `StartupAnim_OnTick` (`004311b8`). That handler plays [the switch sound](#what-plays-each-sound) on its first run (`DAT_00473608`) and, once `+0x6d` reaches 5, hides the widget — whose hide (event 2) removes the alarm, frees the frames and sets `DAT_0046c098` — and calls `MainMenu_Show`, once only (`DAT_00473604`). So frame 0 goes up with the show, the switch sounds with frame 1 half a second later, and the menu comes up at 2.5 s over the same `bay2a_84` the last two frames show. The widget takes no mouse events, and no button is up until the menu is.

When `DAT_0046c088` is set, the handler also puts up a `Performance Note` message box as the menu comes up, dropping out of full screen for it: *the game will default to 320x200 mode*. The startup sets that flag when, with option 47 clear, `Sierra.ini`'s `VideoSpeed` reads below 1000, then sets option 47 and saves, so the box can appear on one run only. The GoldGames build's `DATA\PREFS.CFG` ships option 47 clear ([Open](#open)).

### END OF GAME

`CONTINUE GAME` goes on to the frame only for game state 2, the one [the debrief](campaign-loop.md#where-the-debrief-goes-next) leaves, which `Game_LoadSlot` reads into `DAT_0048260e`. For any other it calls `EndOfGame_Show(state)` (`0044cecf`), which writes the reason into the alert `EndOfGame_Build` (`0044cc2c`) builds at startup and shows it over the menu, which stays up under it. The alert is the [launch refusal](#rock--roll)'s sibling, with a different rect and plate:

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| alert | `ESAlert`, in a window the size of the display | `{0xcf, 0xcb, 0x1b4, 0x12a}` | `0x12e` `END OF GAME`, border `0x15`, face `0x25`, plate `0x31`-`0xb3` |
| first line | `Text` | `{0xc, 0x1e, 0xd6, 0x2b}` | by state: 0 `0x130` `The war is lost.`, 1 `0x131` `The cybrids were defeated.`, 3 `0x12f` `You have been killed.`; any other state leaves the line as it was, which the builder leaves on the empty entry 0 |
| second line | `Text` | `{0xc, 0x2c, 0xd6, 0x39}` | `0x132` `Restore or start a new game.` |
| `OKAY` | `Button` | `{0x41, 0x45, 0xa5, 0x54}` | `0x133`, border `0x22`; `EndOfGame_OnOkay` (`0044cf7b`) hides the alert |

Both lines are centred in `0x29`. The game `CONTINUE GAME` loaded stays loaded either way. Unlike [RESTORE](#leaving-the-save-screen) it writes no autosave and leaves the campaign map's first-show flag (`DAT_004778aa`) as it was.

### QUIT

**`QUIT` asks nothing and sets no exit code.** Its handler sets `Shell_QuitFlag` (`0046c074`), the flag that ends the shell's main loop in `Shell_Main` (`00401525`), and blanks the screen through `Shell_BlankScreen` (`0040723d`). Windowed, that zeroes the shell's bitmap and stretches a 10x10 corner of it over the window's client rect; full screen, it locks the primary surface and zeroes every row. Either way the whole window is palette index 0, strip included. `INSTANT ACTION` and `VIEW DEMO` blank the same way, but only full screen.

**Alt+F4 asks.** `MainWndProc` takes it as a key (key code `0x23e`) rather than passing it on, and while no movie plays and `DAT_0046c098` is set it releases the pointer lock and shows an `ESAlert` built at startup by `FUN_00431918` (`DAT_0048d0fc`, `{0xcf, 0xcb, 0x1b4, 0x12a}` in a window the size of the display, `0x12` `QUIT`, border `0x15`, face `0x25`, plate `0x31`-`0xb3`) over whatever screen is up. Its `CANCEL` (`0x11`, `FUN_00431b31`) hides it and its `ACCEPT` (`0x10`, `FUN_00431ba7`) sets `Shell_QuitFlag`.

The loop's exit is the same for every way out, `QUIT`, the launches, Alt+F4's `ACCEPT` and `WM_CLOSE` alike — `MainWndProc` (`00404a2c`) sets the same flag on `WM_CLOSE` once the loop is running (`DAT_0046c098`):

```
Game_SaveSlot(10, NULL)   // 0040e37b: the current-game autosave; nothing without a game in progress, slot 11 in training
...                       // the screens torn down, ShellMap_ReleaseResources
Shell_ShutdownDevicesAndSound()   // 004092dc: Devices_Shutdown, ShellSound_Shutdown
PostQuitMessage(0)
```

The startup (`Shell_WinMain`, `00406507`) pumps messages until the `WM_QUIT` arrives, releases DirectDraw (`Display_ReleaseDirectDraw`, `00407011`) and returns `0046e210` as the shell's exit code. `Shell_Main` (`00401525`) zeroed that store right after copying the `-X` code out of it, and `QUIT` leaves it alone, so the shell exits with 0 and `ES.EXE` ends ([`../command-line.md`](../command-line.md#exit-codes)).

## The registration screen

What `START NEW GAME` opens: a pilot name and a skill for a new campaign career. Built once at startup by `Registration_BuildScreen` (`0043b69e`), put up by `Registration_Show` (`0043bc0a`) and hidden by `Registration_Hide` (`0043bcb9`). Like the main menu it stands alone over a backdrop-textured root of its own, with the strip hidden, and is left through its own `CANCEL` and `ACCEPT`. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | the top-level window's own rect | the shared backdrop; `+0x51 = 0` |
| content panel | `TitledPanel` | `{0xce, 0xc6, 0x1b2, 0x144}` | `0x30` `REGISTRATION`, header 19 tall, plate `0x3f`-`0xa6`, border `0x27`, face `0x25`, dithered body in `0x10` |
| box | `FramedPanel` | `{3, 0x17, 0xe1, 0x5e}` | border `0x15`, face `0x25` |
| prompt | `Text` | `{7, 6, 0xd7, 0xf}` in the box | `0x31` `ENTER NEW PILOT NAME`, centred, `0x29` |
| name box | `Button` | `{7, 0x14, 0xd7, 0x26}` in the box | a single space (`00476167`), border `0x22`, disabled |
| name field | edit field | `{1, 1, W - 1, H - 1}` in the name box | the name, in `0x29`; handler `Registration_OnNameEvent` (`0043bdee`) |
| skill readout | `Button` | `{0x72, 0x2b, 0xd8, 0x3d}` in the box | `0x35 + skill`, border `0x13`, disabled, caption opaque in `0x17` |
| `SKILL LEVEL` | `Button` | `{7, 0x2c, 0x69, 0x3b}` in the box | `0x32`, border `0x22`; `Registration_StepSkill` (`0043c01d`) |
| `CANCEL` | `Button` | `{10, 0x66, 0x6c, 0x75}` | `0x33`, border `0x22`; `Registration_OnCancel` (`0043c098`) |
| `ACCEPT` | `Button` | `{0x76, 0x66, 0xd8, 0x75}` | `0x34`, border `0x22`, caption `0x26`, disabled; `Registration_OnAccept` (`0043c0fb`) |

`W` and `H` are the name box's own width and height, so the field lies one pixel inside it, over the inner of [a button's two borders](#how-a-widget-paints): the name box shows its outer border alone around the field's `0x10` fill.

**The field takes keys from the start.** The builder writes its permitted-character set, `00476169` — the digits, both alphabets and the space — and leaves `ESDialog_Ctor`'s `+0xbf` and `+0xb3` set and `+0xb7` at 0, where [the save rows](#the-save-screen) clear the first two and raise the third. So it types as [a save row being renamed does](#typing-into-a-row), letters upper-cased, and erases down to empty. `Registration_Show` shows the three widgets and then does what `SAVE` does to a row: posts a left press at the field, moves the pointer onto it and locks it there, so the field has the focus and its caret blinks as the screen comes up. A press elsewhere ends that as it does on the save screen, and the field takes no key until it is clicked again.

**The name and the skill carry over.** The field starts empty, and neither the show nor either button writes it, so a second `START NEW GAME` comes back to the name typed last ([Open](#open)). The skill is `RegistrationSkillChoice` (`004761ac`), 0 in the image, which the two `SKILL LEVEL` handlers step — this screen's and [the save screen's second panel](#the-second-registration-panel)'s. `Game_NewCareer` writes it to the player pilot's `+0x25` through `Pilot_Init` (`0040fcd8`), and it never changes after ([`campaign-loop.md`](campaign-loop.md#pilot-progression)), so it is also the career's [simulator difficulty](../simulation/difficulty.md).

**`ACCEPT` is live once the name has a character.** `Registration_OnNameEvent` runs on every event the field takes, and on a character or a command writes [the greying trio](#the-condition-readout) at `ACCEPT` from the field's first character: greyed while it is empty, lit once it is not. The builder greys the caption and clears the enable flag but leaves the border at `0x22`, so until the first key `ACCEPT` is a live border round a grey caption.

`SKILL LEVEL` steps the skill modulo 4 on either button's release and rewrites the readout: `ROOKIE`, `REGULAR`, `VETERAN`, `ELITE`. `CANCEL` is `Registration_Hide` then `MainMenu_Show`, and leaves the mode at the campaign's.

### Starting a campaign

`ACCEPT` is:

```
LoadHercInfDat()                        // 0041181c: gam\herc_inf.dat again, the chassis flags back to the file's
Registration_Hide()
DAT_004778aa = 0                        // the campaign map's first-show flag
Game_NewCareer(name, RegistrationSkillChoice)
MissionScreenView = (CampaignMissionInStage != 0)
```

`Game_NewCareer` in a campaign builds the roster, the player and the starting hangar ([`campaign-loop.md`](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed)), and its position step, `Career_SeedPosition` (`00412a2f`), puts the career on stage 1 mission 0 and posts the mission-name dialog's `Use Default` click as [a practice mission's](#starting-a-practice-mission) does. The last line therefore writes 0, the map view, before the click is delivered. That click runs [the campaign branch](campaign-loop.md#loading-the-careers-mission) of `Career_LoadCurrentMission`, which ends by putting the frame up (`0043b162(8)` and the strip refresh) and calling `Mission_ShowView(0, 1)`: the mission tab comes up in [the map view](#the-three-views) on stage 1, and the left press the second argument posts at `MISSION` lights it and makes the press sound, its handler finding tab 7 already current.

**`ACCEPT` writes no save.** The career's first write to slot 10 is the next autosave: the `MAIN MENU` tab's, or [the main loop's exit](#quit), which `Rock & Roll` reaches.

## The practice missions screen

What `PRACTICE MISSIONS` opens: the eight practice missions beside five mission parameters. Built once at startup by `PracticeScreen_Build` (`0044ac80`), put up by `PracticeScreen_Show` (`0044bc92`) and hidden by `PracticeScreen_Hide` (`0044bcb3`). Like the main menu it stands alone over a backdrop-textured root of its own, with the strip hidden, and is left through its own `Main Menu`. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | its parent's own rect | the shared backdrop |
| content panel | `TitledPanel` | `{0x4d, 0xa2, 0x232, 0x170}` | `0xf0` `PRACTICE MISSIONS`, header 19 tall, plate `0xaa`-`0x13a`, border `0x15`, face `0x25`, dithered body in `0x10` |
| mission list | `FramedPanel` | `{6, 0x1a, 0xcf, 0xad}` | border `0x15`, face `0x10` |
| list title | `Text` | `{0x28, 4, 0xa2, 0x10}` in the list | `0xf1` `Practice Missions:`, centred, `0x1a`, opaque |
| 8 rows | `Panel` | `{0xe, i*0xc + 0x15, 200, i*0xc + 0x21}` in the list | `0xf2 + i`, border `0x10` |
| parameter box | `FramedPanel` | `{0xd4, 0x1a, 0x1df, 0xad}` | border `0x15`, face `0xf` |
| box title | `Text` | `{0x49, 6, 0xcf, 0x12}` in the box | `0xfa` `Mission Parameters`, centred, `0x1a` |
| 5 labels | `Button` | `{6, 0x1d, 0x90, 0x2c}`, then `0x16` lower four times, in the box | `0xfb`-`0xff`, `Damage` to `Herc Type`, border `0x22` |
| 5 colons | `Text` | `{0x93, y, 0x99, y + 0xf}` beside each label | `:`, centred, `0x28` |
| 5 readouts | `Button` | `{0x9d, 0x1c, 0x104, 0x2d}`, then `0x16` lower four times, in the box | border `0x13`, disabled, caption opaque in `0x17` |
| `Main Menu` | `Button` | `{0x56, 0xb5, 0xb8, 0xc4}` | `0x100`, border `0x22` |
| `Begin Mission` | `Button` | `{0x12e, 0xb5, 400, 0xc4}` | `0x102`, border `0x22` |

The list's face of `0x10` flattens its checkerboard; the parameter box keeps a visible one in `0xf`. The readouts are built as [the repair screen's](#the-repair-screen) are: boxes with a word in them that swallow a click.

**The rows are 13 tall on a 12-pixel pitch**, so the lower row owns the shared line. Each is a [four-column row](#a-row-is-four-text-columns) cut at `0xb8` three times: the name left-aligned from `2`, then three empty columns. A row's name is `0x27` resting and `0x29` lit, and its border `0x10` either way, so only the name shows the selection.

### The parameters

Each label steps one `prefs.cfg` option, with its own modulus, and rewrites its readout from the option's `estext.bin` run. The event's sub-code decides the direction: the left release steps forward through `ShellOptions_StepOption`, the right back through `ShellOptions_StepOptionBack`.

| Label | Handler | Option | Modulus | Readout | Becomes |
|---|---|---|---|---|---|
| `Damage` | `0044bf29` | `0x26` | 2 | `0x128` `Vulnerable` / `Invulnerable` | `script.dat` header `+0x0c`, the player takes no damage |
| `Ammo` | `0044bfe6` | `0x25` | 2 | `0x12a` `Limited` / `Unlimited` | header `+0x0a`, unlimited ammunition and energy |
| `Mission Difficulty` | `0044c0a3` | `0x27` | 4 | `0x35` `ROOKIE` to `ELITE` | header `+0x0e`, the [difficulty](../simulation/difficulty.md) |
| `Time of Day` | `0044c160` | `0x29` | 2 | `0x12c` `Day` / `Night` | header `+0x12`, the theater variant |
| `Herc Type` | `0044c21d` | `0x28` | 9 | `0x6e` `Outlaw` to `Razor` | the chassis the player flies — not a header field |

The header fields are written from these options by `MsnGen_LoadMission` ([`../formats/script-dat.md`](../formats/script-dat.md#the-training-fields)), and the simulator takes them from the header ([Open](#open)).

A step changes the array in memory only. `Begin Mission` (`0044c396`) is what saves it, so the settings persist between runs, and then flies the lit row — [Starting a practice mission](#starting-a-practice-mission). `Main Menu` (`0044c2da`) is `PracticeScreen_Hide` then `MainMenu_Show`, and leaves the mode where it is.

### Selecting a mission

A click on row `i` runs `PracticeScreen_OnRow0`-`7` (`0044c413`-`0044c6ba`), eight identical handlers that `PracticeScreen_Build` stores in the table `PracticeScreen_RowHandlers` (`0048db9c`) and from there into each row's handler field `+0x3d`. Each calls `PracticeScreen_SelectRow(i)` and deletes the event. `PracticeScreen_SelectRow(row)` (`0044bd7c`) returns at once for the row already lit, `PracticeScreen_SelectedRow` (`00479bb8`), which is `-1` in the image. Otherwise it puts the old row's name back to `0x27` and lights the new one's `0x29`; writes [the greying trio](#the-condition-readout) at `Herc Type`, greyed for rows 0-3 and lit from row 4; writes the row's chassis into option `0x28` (`ShellOptions_SetOption`); rewrites the `Herc Type` readout; and stores the row. The chassis is the low byte of the row's `int16` in the table at `00479bba`:

| Row | Mission | Chassis |
|---|---|---|
| 0 | `Basic Training 1` | 0 `Outlaw` |
| 1 | `Basic Training 2` | 0 `Outlaw` |
| 2 | `Basic Training 3` | 1 `Raptor II` |
| 3 | `Flyer Training` | 8 `Razor` |
| 4 | `Strike Training Mission` | 4 `Colossus` |
| 5 | `Escort Training Mission` | 2 `Tomahawk` |
| 6 | `Recon Training Mission` | 7 `Maverick` |
| 7 | `Scramble Training Mission` | 3 `Samson` |

So a row click resets `Herc Type` to that mission's machine, whatever it was stepped to. `PracticeScreen_Show` calls `PracticeScreen_SelectRow(0)`, so **the screen always comes up on `Basic Training 1`**.

**`Herc Type` is greyed where the choice is not read.** `MsnGen_BuildPlayerHerc` (`0041c58d`), which builds the player's machine when the shell loads a mission, gives the player the mission's own machine while `PracticeScreen_SelectedRow` is below 4 or `InstantAction_Active` is set, and a machine of option `0x28`'s chassis otherwise. `INSTANT ACTION`'s handler sets `InstantAction_Active` to 1 ([Open](#open)).

### Which mission a row is

The row is the mission index. `Career_SeedPosition` (`00412a2f`), which puts a new career on its first mission, sets a training-mode career's position to stage 0, mission `PracticeScreen_SelectedRow`, and stage 0 of `gam\career.dat` is `TRAIN1`-`TRAIN8` then `DEMO`, `DEMO_01` and `DEMO_02` ([`campaign-loop.md`](campaign-loop.md#the-campaign-table--gamcareerdat)) — the eight rows in order, then three more.

**`INSTANT ACTION` plays the three past the list.** `InstantAction_SelectDemo` (`0044befb`) calls `PracticeScreen_SelectRow(8 + option 0x2e)`: row 8, 9 or 10, which puts the lit row out, lights none, leaves `Herc Type`'s greying as it was, and writes the table's next three chassis, 5 `Apocalypse`, 7 `Maverick` and 3 `Samson`, into option `0x28`. It then steps option `0x2e` modulo 3 and saves the array, so successive `INSTANT ACTION`s play `DEMO`, `DEMO_01` and `DEMO_02` in turn, each in its own machine.

### Starting a practice mission

`Begin Mission` commits the options (`ShellOptions_Commit(1)`), writes all 54 to `data\prefs.cfg` (`ShellOptions_SaveAll`) and calls `Game_NewCareer("TRAINEE", option 0x27)`: the roster, the player with the difficulty as their skill, no machines — a training career's `LoadHercsDat` reads nothing — and the career position, stage 0 at the lit row ([`campaign-loop.md`](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed)). The position's last step, `Career_StartMissionLoad` (`00412ce1`), shows a developer's mission-name dialog holding that mission's name and posts a press and a release to its `Use Default` button. The event loop delivers them once the handler has returned, and `MissionNameDialog_OnUseDefault` (`0044d55a`) hides the dialog and calls `Career_LoadCurrentMission` (`0044d4cc`) ([`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#call-chain--confirmed)).

`Career_LoadCurrentMission` loads the mission (`MsnGen_LoadMission`, `0041c73d`) and, outside a campaign ([`campaign-loop.md`](campaign-loop.md#loading-the-careers-mission) has a campaign's), goes straight on to `Game_ExportMissionHandoff`, `Shell_SetExitCode(2)` and the loop exit, the three `INSTANT ACTION` ends with. Between the write of `script.dat` and that export, the training half of `MsnGen_LoadMission` builds the squad from the mission's group 0, the one [`script.dat`](../formats/script-dat.md#placement--the-actual-rule) places the player's squad at:

1. `Squad_SetPositionsInPlay` (`004102ff`) sets the positions in play: 1, plus each member group 0 sets in an unbroken run from its second slot.
2. `MsnGen_BuildPlayerHerc` puts the player's machine in bay 0 ([Selecting a mission](#selecting-a-mission)). The mission's own machine is group 0's first member; a chosen chassis comes with its stock fit, `gam\ini_*.dat` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamini_dat--the-stock-fit-per-chassis)).
3. Squad member `i`, from 0, takes a machine built from group 0's slot `i + 1` in bay `i + 1`; `Squad_SetMemberBay` gives them the bay and runs `Squad_UpdateOnStrength`; and `Squad_SetMemberPosition` gives them position `i + 1`.

A machine built from a mission record is `Herc_SetType` on its chassis, then `Herc_FitNewUnit` (`004115c6`) with the weapon and ammunition type of each slot below the capacity whose weapon is not 0, so a `-1` there is fitted as a weapon of id `-1`.

**The last wingman never flies.** Step 3 passes `Squad_UpdateOnStrength` the member's index where a position belongs, and runs it before the member has a position. So it lands on whoever holds position `i`: nobody for member 0, and member `i - 1` after that, who goes on strength one step late. The last member to be given a bay is never updated, and `Game_ExportMissionHandoff` writes only members on strength. A squad of one wingman flies without them. Retail's training handoffs show it: each `player11.mec` below is a TRAIN5 whose group 0 gives two wingmen, and carries the player and the first.

**The loop exit saves the training career.** Its `Game_SaveSlot(10, NULL)` ([QUIT](#quit)) finds `Game_NewCareer`'s game in progress and the mode at training, so it writes slot 11: `GAME_T.SAV`, with `Career_SaveSlot` copying the handoff's `script.dat`, `mission.str` and `player.mec` beside it as `script11.dat`, `missn11.str` and `player11.mec` ([`../formats/save-games.md`](../formats/save-games.md#the-slot-handoff)). The save holds the career as the load left it: the position at stage 0 on the row, the squad the steps above built, the flag array the load seeded, and what [a training career keeps](campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed) from the game before it. `INSTANT ACTION` leaves the same way and writes the same slot.

The draws the path makes are VSHELL's generator's ([`campaign-loop.md`](campaign-loop.md#the-shells-generator)), in this order: the roster, the player's name index, the salvage, flags 4-6, then the mission load's. Three TRAIN5 launches retail wrote to slot 11 are each this path's output, with `Herc Type` on `Colossus` and `Mission Difficulty` on 2: one on `Day` with the generator seeded 82, one on `Night` seeded 19, and one on `Day` seeded 119. The last one's `GAME_T.SAV` and three working files are its output byte for byte, stale tail aside.

## The preferences screen

What `PREFERENCES` opens: six `prefs.cfg` options ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)) as eleven checkboxes in five boxes. Built once at startup by `PreferencesScreen_Build` (`00434f08`), which also loads `dba\chk_box.dba`; put up by `PreferencesScreen_Enter` (`004366b5`) and hidden by `FUN_00436717`. Like the main menu it stands alone over a backdrop-textured root of its own, with the strip hidden, and is left through its own `Cancel` and `Accept`. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | image panel | its parent's own rect | the shared backdrop |
| content panel | `TitledPanel` | `{0x72, 0x98, 0x21c, 0x183}` | `0x103` `PREFERENCES`, header 19 tall, plate `0x8c`-`0x11b`, border `0x27`, face `0x25`, dithered body in `0x10` |
| 5 boxes | `FramedPanel` | below | border `0x15`, face `0xf` |
| 5 headings | `Text` | below, in each box | `0x29`; `Audio/Speech Options:` centred, the other four left |
| 11 labels | `Text` | below, in each box | left, `0x27` |
| 11 checkboxes | checkbox | below, in each box | `chk_box.dba` frames 1 and 0, the empty caption at `00474cb1` |
| `Cancel` | `Button` | `{0xda, 0xce, 0x139, 0xdd}` | `0x10b`, border `0x22` |
| `Accept` | `Button` | `{0x13e, 0xce, 0x19e, 0xdd}` | `0x10c`, border `0x22` |

| Box | Rect | Heading | Label | Label rect | Checkbox rect | Handler | Sets |
|---|---|---|---|---|---|---|---|
| audio | `{0xc, 0x1a, 0xd0, 0x60}` | `0x106` `Audio/Speech Options:` at `{0x15, 6, 0xad, 0x12}` | `0x116` `Music` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `00436cc1` | option 0 |
| | | | `0x117` `Sound Effects` | `{5, 0x33, 0xaa, 0x41}` | `{0xaf, 0x32, 0xc1, 0x42}` | `00436d22` | option 1 |
| repair | `{0xda, 0x1a, 0x19e, 0x76}` | `0x107` `Repair Options:` at `{0x36, 6, 0xa0, 0x12}` | `0x113` `AutoRepair All Hercs` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `00436d83` | option 44 to 0 |
| | | | `0x114` `Manually Repair My Herc` | `{5, 0x33, 0xaa, 0x41}` | `{0xaf, 0x32, 0xc1, 0x42}` | `00436de4` | option 44 to 1 |
| | | | `0x115` `Manually Repair All Hercs` | `{5, 0x48, 0xaa, 0x56}` | `{0xaf, 0x48, 0xc1, 0x58}` | `00436e45` | option 44 to 2 |
| weapons | `{0xda, 0x7e, 0x19e, 0xc4}` | `0x108` `Weapons Building:` at `{0x2c, 6, 0xaa, 0x12}` | `0x111` `AutoBuild Weapons` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `00436ea6` | option 45 to 0 |
| | | | `0x112` `Manually Build Weapons` | `{5, 0x33, 0xaa, 0x41}` | `{0xaf, 0x32, 0xc1, 0x42}` | `00437006` | option 45 to 1 |
| resolution | `{0xc, 0x68, 0xd1, 0xae}` | `0x120` `Game Resolution` at `{0x2c, 6, 0xaa, 0x12}` | `0x121` `High Res (640x480)` | `{5, 0x1c, 0xaa, 0x2a}` | `{0xaf, 0x1c, 0xc1, 0x2c}` | `004370c8` | option 4 to 0 |
| | | | `0x122` `Low Res (320x240)` | `{5, 0x32, 0xaa, 0x3e}` | `{0xaf, 0x32, 0xc1, 0x42}` | `00437067` | option 4 to 1 |
| display | `{0xc, 0xb6, 0xd1, 0xe4}` | `0x123` `Display Mode` at `{0x35, 6, 0xad, 0x12}` | `0x124` `Window` | `{10, 0x1c, 0x37, 0x2a}` | `{0x39, 0x1a, 0x4b, 0x2a}` | `00436f07` | option 6 to 0 |
| | | | `0x125` `Full Screen` | `{0x5a, 0x1c, 0xa5, 0x2a}` | `{0xa9, 0x1a, 0xbb, 0x2a}` | `00436f78` | the alert, [below](#full-screen-asks-first) |

The display box lays its two out side by side, each label left of its checkbox; the other four boxes stack theirs, labels at the left and checkboxes in one column at `0xaf`. No widget overlaps another, and the panel and the boxes have no handler, so a click anywhere but a checkbox or a button is swallowed.

**The checkbox is a class of its own, `ESRadioButton` by its RTTI name.** `ESRadioButton_Ctor` (`0040a100`) is `ESButtonBitmap_Ctor` with vtable `0046e9a0` and `+0x69`, the tick, cleared. Its paint, `ESRadioButton_Paint` (`0040a26d`), is the strip's paint choosing between the two faces on `+0x69` where the strip's chooses on the lit flag `+0x45`; `+0x45` still moves the caption, which here is the empty string. The builder passes `chk_box` frame 1, the empty box, as the unticked face and frame 0, a cross, as the ticked one. Both frames are 24 wide and 17 tall, and the rect is 19 wide: the five columns the paint's clip drops are index 0. Its handler is `ESRadioButton_HandleEvent` (`0040a139`) ([The widget that takes a click decides what it does](#the-widget-that-takes-a-click-decides-what-it-does)). **Every one of the eleven is this class** — the radio groups are groups only because their setters relight them.

### What a checkbox sets

`PreferencesScreen_Enter` seeds the ticks: `Music` and `Sound Effects` take options 0 and 1 as they stand (`PreferencesScreen_SyncSoundChecks`, `00436790`), and the four groups go through their setters with their own option's value — which writes it back unchanged — before the root and the panel are shown.

A group's setter — `PreferencesScreen_SetRepairMode` (`00436a9c`) for repair, `PreferencesScreen_SetWeaponsBuildMode` (`00436b50`) for weapons, `PreferencesScreen_SetGameResolution` (`00436abc`) for resolution, `PreferencesScreen_SetDisplayMode` (`00436b70`) for display — stores its value in a word of its own (`00474cc4`, `00474cc8`, `00474cc6` and `00474cca`, [Open](#open)), writes the option through `ShellOptions_SetOption` with apply set, and relights the group: `+0x69` to 1 on the checkbox of the option's value and 0 on the others. A value no checkbox in the group names relights nothing.

The two sound checkboxes run `FUN_00436841` with 0 and 1 and then reseed both ticks. Case 1 toggles SOUNDS. Case 0 toggles MUSIC and runs a fade: turning it on, the toggle and then `ShellSound_FadeIn`; turning it off, `ShellSound_FadeOut` and then the toggle, so the fade's own MUSIC gate lets it run ([Sound](#sound)). The function also has cases 2 and 3, which cycle option 2, the simulator's PILOT MESSAGE ([Open](#open)).

`Game Resolution` is the simulator's video mode ([`../simulation/preferences.md`](../simulation/preferences.md#the-video-mode-and-full-screen-bytes)); the shell has one mode and does not read it.

### Full screen asks first

`Window` (`00436f07`) toggles the shell's window out of full screen through `Display_ToggleFullScreen` (`00407085`) when `DAT_00481e68`, the full-screen flag, is set, and then sets option 6 to 0. `Full Screen` (`00436f78`) sets nothing: while the shell is windowed it shows a window the size of the display holding an alert, and otherwise does nothing. The alert's `ACCEPT` (`FUN_00436fe8`) hides the window, toggles the shell into full screen and sets option 6 to 1. So `Full Screen` is ticked only after that `ACCEPT`.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| window | `Window` | the top-level window's absolute rect | hidden once its children are built |
| alert | `ESAlert` | `{0x68, 199, 0x226, 0x117}` | `0x126` `Alert!`, border `0x27`, header 20 tall, face `0x25`, plate `0xbe`-`0xfa`, filled body |
| line | `Text` | `{5, 0x1c, W - 4, 0x2a}` | `0x127` `If you experience difficulties, hit alt-enter and view the read-me.`, left, `0x27` |
| `ACCEPT` | `Button` | `{0xa8, 0x38, 0x107, 0x47}` | `0x10`, border `0x22` |

`W` is the alert's own width.

**Full screen is an exclusive display mode.** `Display_ToggleFullScreen` toggles it. Going in, it sets `DAT_00481e68`, creates a DirectDraw object and takes it exclusive and full screen (`Display_CreateDirectDrawExclusive` (`00406eb5`), cooperative level `0x17`), sets a 640x480 8-bit display mode and creates the primary surface (`DDraw_SetModeAndCreatePrimary` (`00406eeb`), with the canvas size from the shell's bitmap header `DAT_00481864`), and places the window topmost with its frame pushed off the screen, so its client area is the screen. It then marks the palette's entries, gives the primary surface a palette, confines the pointer to the screen (`ClipCursor`) and centres it. If DirectDraw or the mode fails the shell quits. Coming out, it clears the flag, releases every DirectDraw object (`Display_ReleaseDirectDraw`, `00407011`), which gives the desktop its mode back, and centres the window, no longer topmost.

**The startup enters it from option 6.** `Shell_WinMain` (`00406507`), the startup under `WinMain`, reads `prefs.cfg` (`ShellOptions_Init`, `0040d68c`), copies option 6 into `DAT_0046d740`, builds the window over the desktop and topmost while that is set, and then calls `Display_ToggleFullScreen`. It also looks for a `-d` or `/d` argument and clears `DAT_0046d740` for one, but that store (`0040656c`) comes before the copy from option 6 (`00406583`), which overwrites it with nothing reading it between, so `-d` has no effect. Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

**Four keys switch it**, in `MainWndProc` (`00404a2c`), each only while no movie plays and `DAT_0046c098` is set, which the startup does once the screens are built and the startup sequence's widget clears while it runs. Alt+Enter toggles full screen on the Enter key's release; Alt+Tab, Alt+Esc and Ctrl+Esc leave it (`Display_LeaveFullScreen` (`0040722e`)) on the key going down or up. Each then writes option 6 from the flag, and with the preferences screen's panel up relights its display group and repaints it; otherwise it runs `ShellOptions_Commit(0)` and `ShellOptions_SaveAll`. A modifier other than the one named stops the key matching. The shell also leaves full screen around its own message boxes and goes back after.

### Leaving the preferences screen

Both buttons end in `FUN_00436717` and `MainMenu_Show`, and differ in what they do with the options first:

| Button | Does first |
|---|---|
| `Cancel`, `00436b90` | `FUN_0040d7fe(0)`: every option that differs from the shadow is put back, running no handler. Then `Display_ToggleFullScreen` when option 6 and the full-screen flag disagree, and the fade [Sound](#sound) describes |
| `Accept`, `00436c51` | `ShellOptions_Commit(0)`, which rebaselines the shadow and runs no handler, then `ShellOptions_SaveAll` |

The shadow is the array as of the last commit, so `Cancel` puts back every option changed since, the practice screen's parameters among them, which that screen steps without committing ([The parameters](#the-parameters)).

## The save screen

Tab 1, `SAVED GAMES`. Built by `SaveScreen_BuildScreen` (004385b0), entered by `SaveScreen_Enter` (00439b0c), its selection moved by `SaveScreen_SelectSlot` (0043795f) and its detail panel refilled by `SaveScreen_RefreshDetail` (0043712c). Every rect is four immediates on the builder's stack, and they are **parent-relative**: the panel sits in the canvas, the list and the button column in the panel, and two buttons in the list.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| root | `0040b698` | its parent's own rect | the shared backdrop; `+0x51 = 0`, so no chrome |
| content panel | `TitledPanel` | `{0x8e, 0x7f, 0x1f2, 0x1d4}` | `0x1b` `SAVED GAMES`, header 19 tall, plate `0x70`-`0xf3` |
| slot list | `FramedPanel` | `{9, 0x1c, 0x15b, 0xc6}` | |
| 10 slot rows | edit field | `{10, i*12 + 0x13, W-2, i*12 + 0x1f}` | the slot's `GAMEFILE.STR` label |
| `CANCEL` | `Button` | `{0x43, 0x93, 0xa5, 0xa2}` in the list | `0x33` |
| `ACCEPT` | `Button` | `{0xb0, 0x93, 0x112, 0xa2}` in the list | `0x34` |
| `SAVE` | `Button` | `{9, 0xe5, 0x6b, 0xf4}` | `0x1d` |
| `RESTORE` | `Button` | `{9, 0xfb, 0x6b, 0x10a}` | `0x1e` |
| `EXIT` | `Button` | `{9, 0x111, 0x6b, 0x120}` | `0x1f` |
| detail panel | `FramedPanel` | `{0x74, 0xcc, 0x15b, 0x150}` | 23 `Text` children: 11 labels and 12 values |
| registration panel | `FramedPanel` | `{0x74, 0xcc, 0x15b, 0x136}` | `DAT_0048d418`, a second registration panel, [below](#the-second-registration-panel) |

The panel is centred on x=320 rather than on the canvas's own inclusive midpoint, so its left margin is 142 and its right 141. The builder overwrites three class defaults on it: `+0x59` to 0 for the dithered body, `+0x5d` to `0x10`, and `+0x55` on all three framed panels to `0x10` — which flattens their checkerboard, since it then dithers the interior colour over itself.

**The rows are 13 tall on a 12-pixel pitch**, so each overlaps its neighbour's border row, and the first and last are inset two pixels further from the left edge than the eight between them. Their right edge is computed from the list panel's absolute corners rather than written, at two pixels inside it. Each carries a permitted-character set at `+0x9f` — `^`, the digits, both alphabets and the space (`004757cd`) in place of the class's upper-case alphabet and space — so **renaming a slot is typing into its row**, and `CANCEL`/`ACCEPT` are that edit's two buttons rather than the screen's. The builder clears `+0xb3` and `+0xbf` on every row, so a row takes no keystroke and shows no caret until [a rename](#saving-is-a-rename) sets both, and writes `+0xb7 = 4`, the length [erasing](#typing-into-a-row) stops at, so the `"%2d. "` prefix cannot be deleted. Selection is `+0xbb`: `0x27` resting, `0x29` selected.

Three buttons are gated, each written as the trio [the repair panel uses](#the-condition-readout): `SAVE` on a row being selected and there being a game to write (`DAT_0048260a`), `RESTORE` on the selected slot's in-use byte, and both `CANCEL` and `ACCEPT` on the rename being live. `EXIT` has no condition of its own; a rename greys it with the other two ([below](#saving-is-a-rename)).

### Saving is a rename

`SAVE` writes nothing. Its handler (`00437bd3`) starts a rename of the selected row, and `ACCEPT` is what writes the save. `DAT_00474f40` is the rename's state: 0 idle, 2 while a rename is live, and 1 only transiently inside `SAVE`'s handler.

| Handler | What it does |
|---|---|
| `SAVE`, `00437bd3` | edit state to 1; greys `SAVE`, `RESTORE` and `EXIT`; calls `004377d2` |
| `004377d2` | returns at once if the edit state is 0. Otherwise posts a left press (event `0x20`, sub-code 2) at the selected row, moves the pointer onto the row (`Pointer_SetTarget`, 00469cbc) and [locks it there](#which-widget-a-click-reaches), so keystrokes reach the row; sets its caret flags `+0xbf` and `+0xb3`; rewrites it as `"%2d. %s"` of the slot number and the empty string at `0047526a`, so it reads ` 3. ` with the name gone; lights `CANCEL` and `ACCEPT`; edit state to 2; repaints the list. The press, delivered after the handler returns, gives the row the focus and runs its handler, whose selection move edit state 2 refuses |
| `ACCEPT`, `00437ffa` | `Game_SaveSlot(selected, text)`, where the text is the row's own string buffer at `+0x45` (`00437ba9`), so what was typed, prefix and all, becomes the slot's label ([`../formats/save-games.md`](../formats/save-games.md#writing-a-slot)); `Stats_StageCurrentGame(selected)`; refreshes the detail panel; greys `CANCEL` and `ACCEPT`, lights `SAVE`, `RESTORE` and `EXIT`; edit state to 0 |
| `CANCEL`, `00437e1a` | puts the row's `GAMEFILE.STR` label back; greys `CANCEL` and `ACCEPT`, lights `SAVE`, `RESTORE` and `EXIT`; edit state to 0; `SaveScreen_SelectSlot(10)` |

Edit state 2 is what `SaveScreen_SelectSlot` refuses, so the selection cannot move off the row being renamed. `ACCEPT` and `CANCEL` light the three buttons without their usual tests. After `CANCEL` that does not last: the closing `SelectSlot(10)` deselects the row and regates `SAVE` and `RESTORE`, both dead with no row selected. After `ACCEPT` the selection stays on the slot just written.

The click on `ACCEPT` or `CANCEL` reaches it through the locked row: the press lands on the focused field, which gives up the focus and the lock and posts the press over to what is under the pointer ([above](#the-widget-that-takes-a-click-decides-what-it-does)). A click on another row lands there instead, takes the focus and the lock, and moves nothing; keystrokes then go to that row, which takes none while its `+0xbf` is clear, until a click puts the focus back on the row being renamed.

### Typing into a row

**A key reaches the pointer's target.** `MainWndProc` turns a `WM_KEYDOWN` or `WM_KEYUP`'s virtual-key code into its position in the table at `0046d384`, a set-1 scancode, adding `0x800` for Shift, `0x400` for Ctrl, `0x200` for Alt and `0x80` for a release, and drops a key that is not in the table — every punctuation key among them. The keyboard object then sorts it (`Keyboard_PostEvents`, `00408f95`):

- A key in the filter at `0046e450` — Esc, Backspace, Tab, Enter, the editing cluster and the arrows — is a command, event `0x100`, whose code is `0046e471` at the key's index. Press and release both send one: Backspace sends 1, the left arrow 4 and Enter `0x0a` on the press, and their releases send codes of their own.
- Any other key's press is a character, event `0x40`, from `0046e571`, or `0046e5c5` with Shift down, and upper-cased. Both tables hold the letters in upper case already. Among the keys the virtual-key table admits they differ only on the digit row, where Shift gives `!@#$%^&*()`; they also differ at the punctuation keys `-=[];'\,./`, which never reach them. Ctrl+Q sends an event of its own instead.

Neither carries a target, so each goes where the last move left the pointer, or where a lock holds it.

**`ESDialog_HandleEvent` edits the string, then runs the handler, on every key.**

| Event | While | What it does |
|---|---|---|
| a character | `+0xbf` set | `ESDialog_TypeChar` (`0040bdd2`): the character goes on the end when the set at `+0x9f` holds it, the new length stays below `0x5a`, and the glyph's width, the string's cached width `+0xaf` and six more are less than the field's width |
| Backspace or the left arrow | `+0xbf` and the focus `+0xa7` set | `ESDialog_Erase` (`0040be56`): the last character comes off while the length is above `+0xb7` |
| Enter | the same | the focus cleared and the pointer released (`Pointer_Unlock`); the rename stays live |

Whatever the event did, the field then runs its handler, which on a save row is `SaveScreen_SelectSlot(row)`. So a keystroke over a row selects it, while no rename is live, and neither a key nor Enter ends a rename: only `ACCEPT` and `CANCEL` do. Letters arrive upper-cased, so of the set's two alphabets only the upper-case one is ever typed, and with punctuation dropped before the tables a row takes letters, digits, the space and Shift+6's `^`.

**The caret blinks while the field has the focus.** `ESDialog_Paint` draws a 7-by-3 block in `0x27` at the string's cached width, above the baseline, while both `+0xa7` and `+0xb3` are set. The press that focuses a field installs a 500 ms alarm for it, and each tick flips `+0xb3` while `+0xbf` is set and clears it otherwise; the press that takes the focus away, or a leave, removes it. **A row keeps `+0xbf` after its rename**: neither `ACCEPT`, `CANCEL` nor `SaveScreen_Enter` clears it ([Open](#open)), so a row once renamed keeps taking keystrokes: whenever it is the pointer's target its string can be typed over, which changes the row and nothing else until the next `SaveScreen_Enter` puts the labels back, and whenever it has the focus its caret blinks.

### Leaving the save screen

With [the strip hidden](#tabs-0-and-1-hide-the-strip), `EXIT` and `RESTORE` are the screen's only buttons that leave it. Both open with `SaveScreen_Teardown` (00439d66), which parks the selection on slot 10 — past every row, so the screen next comes up with nothing selected and `SAVE` and `RESTORE` both dead — and hides the screen's root, content panel and both detail panels.

`SaveScreen_OnExit` (00437d94), `EXIT`'s handler, then goes where `DAT_0048d344` says. 0 rebuilds the main menu; the main menu's `SAVE/RESTORE` writes it ([above](#the-main-menu)). 8 is `0043b162(8)` then `0043b0c8` — show the frame's root, show and regate the strip, park the current tab at `0xffff` — which leaves the bare frame up with no tab current and nothing lit; tab 1's handler writes it.

`RESTORE`'s handler (`00437d03`) is:

```
Game_LoadSlot(selectedSlot, 1)   // the whole save, and its career files into data\
Game_SaveSlot(10, NULL)          // straight back out as the current-game autosave
teardown
DAT_004778aa = 0
0043b162(8); 0043b0c8()          // EXIT's tab-strip path, whatever DAT_0048d344 holds
```

`Game_LoadSlot` sets `DAT_0048260a`, so `SAVE` is live from then on, and does not touch the campaign mode flag, so the slot-10 write lands in `GAME_R.SAV` or `GAME_T.SAV` by whichever mode the shell is already in ([`../formats/save-games.md`](../formats/save-games.md)). `DAT_004778aa` is the campaign map's first-show flag, 0 in the image: `Mission_Show` (004441e3) runs two `Movie_Enqueue` calls in its map arm only while it is clear and then sets it, and `TabHandler_Mission` (0043a6ca) opens the map rather than the briefing only while it is clear and the mission-within-stage counter is zero.

### The detail panel

`SaveScreen_RefreshDetail` writes 12 value fields from the selected slot's [staging record](../formats/save-games.md#the-slot-summary--statscpps-scan), or from `estext.bin` entry 0 — the empty string — when the slot holds no save, so the labels stay and the figures blank.

| Row | Label | Value |
|---|---|---|
| `0x06` | `0x24` `Name:` | the pilot's name |
| `0x12` | `0x25` `Skill:` / `0x26` `Rank:` | `0x35 + skill` and `0x39 + rank`, two runs of four words |
| `0x24` | `0x2a` `Current` / `0x2b` `Total` | column headers, centred over the grid below |
| `0x30`-`0x48` | `0x27`-`0x29` Herc / Flyer / Base `Kills:` | six counters, per-mission in the left column and career totals in the right |
| `0x5a` | `0x2c` `Salvage:` | the pool in kilograms divided by 1000, then `0x2f` `Tons` |
| `0x66` | `0x2e` `Sector:` | `0x76 + stage` |
| `0x72` | `0x2d` `Mission:` | the counter plus one |

The label indices run out of layout order: `Salvage:`, `Sector:` and `Mission:` are drawn in that order from `0x2c`, `0x2e`, `0x2d`.

**The sector run starts at stage 1.** `0x76` is `Razor`, a chassis name; the five sector words `Alpha`, `Delta`, `Omicron`, `Bravo`, `Luna` start at `0x77`. Stage 0 holds the practice missions and the demos, and the campaign's chapters are stages 1-5 ([`campaign-loop.md`](campaign-loop.md#the-campaign-table--gamcareerdat)), so the first chapter lands on the first sector word.

### The second registration panel

`SaveRegistration_BuildPanel` (`0043b260`), which the startup runs just before `Registration_BuildScreen`, fills `DAT_0048d418` with a copy of [the registration screen](#the-registration-screen)'s content in the detail panel's place: a `FramedPanel` at `{7, 6, 0xe1, 0x4b}` (`DAT_0048d470`) holding the prompt, a name box and its field, a skill readout (a `Text`, not a `Button`) and a `SKILL LEVEL` row (`SaveRegistration_StepSkill`, `0043bd15`), with `CANCEL` (`0043bd90`) and `ACCEPT` (`SaveRegistration_OnAccept`, `0043bf1b`) on `DAT_0048d418` itself. `ACCEPT` runs `Game_NewCareer` with the typed name and the shared `RegistrationSkillChoice`, then `Stats_StageCurrentGame(10)`, then `SaveRegistration_ShowDetailPanel` (`0043b679`) — which hides both panels and shows the detail panel again — and lights `SAVE`. The builder leaves its `ACCEPT` enabled ([Open](#open)), and `ACCEPT` does not reload `gam\herc_inf.dat`. `SaveScreen_Enter` and the teardown both hide `DAT_0048d418`; what shows it is [Open](#open).

## The weapons screen

Tab 2, `WEAPONS`, the arming screen. Built once by `Arming_BuildScreen` (`0043e52c`, `warmingi.cpp`), entered by `Arming_Enter` (`0043f548`) and hidden by `Arming_Leave` (`0043f5d2`), the teardown dispatcher's arming arm. Rects are parent-relative. The left of the canvas is [the squad panel](#the-squad-panel), filled with the three-quarter [bay picture](#the-bay-picture).

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x278, 0x1d9}` | `0x9f` `Weapons`, header 19 tall, plate `0x66`-`0x116`, face `0x25`, filled body |
| inventory | `TitledPanel` | `{6, 0xc5, 0x183, 400}` | `0xa0` `Weapons Inventory`, header 19 tall, `+0x65 = 0` |
| picture box | `HatchedDivider` | `{6, 0x1a, 0x183, 0xc0}` | border `0x15`, `+0x55 = 0x76`, `+0x59 = 0x25`, `+0x49 = 0` |
| 26 weapon pictures | image panel | each `gam\arm_weap.dat` record's corner in the box, sized to its frame | that frame of `dba\arm_weap.dba` |
| 4 guidance pictures | image panel | the same, from the file's second list | |
| blank picture | `Panel` | `{0x14, 0x1a, 0x14a, 0x5f}` in the box | `+0x51 = 0` |
| `ARM`, `ARH`, `SARH`, `EO` | `Button` | `{0x152, 5, 0x179, 0x16}`, then `0x17` lower three times, in the box | `0xa1`-`0xa4`, border `0x22`, hidden |
| rack button | `Button` | `{0x101, 0x61, 0x179, 0x72}` in the box | built blank, border `0x22`, hidden |
| 3 description lines | `Text` | `{1, 0x7a, W, 0x88}`, `{1, 0x88, W, 0x94}`, `{1, 0x94, W, 0xa0}` in the box | centred, `0x29`, opaque in `0x25` |
| 13 rows | `Panel` | `{9, i*0xd + 0x14, 0xaf, i*0xd + 0x20}` in the inventory | border `0x10` |
| 14 rows | `Panel` | `{0xbf, i*0xd + 0x14, 0x165, i*0xd + 0x20}` in the inventory | border `0x10` |
| label | `Text` | `{0xe, 0x195, 0x76, 0x1a1}` | `0xa5` `Hard Points`, right, `0x29` |
| `<` | `Button` | `{0x7d, 0x194, 0x8b, 0x1a3}` | border `0x22` |
| `>` | `Button` | `{0x91, 0x194, 0x9f, 0x1a3}` | border `0x22` |

`W` is the box's own width, `+0x2d - +0x25`. The `ARM` to `EO` buttons step down by `0x17`, so the four are 18 tall with five rows between them.

**The picture box is black over a grey band.** Its line fill starts on row `0x76` in `0x25`, which makes everything below that row a solid band and leaves the interior above it at the paint's own `0x10` ([below](#the-crew-screen) for the class). The pictures sit in the black, the description lines in the band. The box's enable flag is cleared, so a click anywhere on it that no button takes is swallowed.

**A picture is a bare bitmap.** Each is placed at its `arm_weap.dat` record's corner and sized `{x, y, x + width, y + height}` to its frame, and the builder writes `+0x51 = 0` on it, which `ESBitmap_Ctor` has already done: that stops [the image panel's paint](#the-crew-screen) drawing a border, and the bitmap is blitted regardless. The weapon pictures are indexed by row through `Arming_RowOfWeapon` (`0043f6f7`) into `0048d5a0`, the guidance pictures by kind into `0048d608`. `None` has no record and shows the blank picture, a `Panel` whose cleared `+0x51` leaves it drawing nothing.

### The inventory rows

The rows list the 27 weapon ids of the table at `004769b0` — `arm_weap.dat`'s 26 in the file's own order, then `None` (0) — thirteen down the left and fourteen down the right, 13 tall on a 13-pixel pitch. Each is a [four-column row](#a-row-is-four-text-columns) cut at `0x8b`, `0x8c` and `0x8d`: the name, `estext.bin` `0x7e + id`, left from `2`; two one-pixel columns holding a space; and the count right-aligned from `0x8d` to one inside the row.

`Arming_RefreshRows` (`0043fbc6`) regates all 27 on every row selection. The count is `"%d"` of the weapon's owned count, `weapons.dat` record `+0x17`, or two spaces for `None` ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#weaponsdat-catalog-record-29-bytes)). A weapon whose unlock flag `+0x16` is clear has its row disabled and its four columns set to `0x10`, the background, so the list shows a gap and keeps the builder's `"0"` where the count would be. An unlocked weapon's row takes `0x27` and is live while `Arming_RowLive` (`004149fb`) holds: always with no hardpoint selected, and with one only when the chassis's armory layout has a part for that weapon at `slot + 2`, the socket [the bay picture](#the-bay-picture) draws it in. A row that fails is disabled in `0x25`.

### Selecting a row

`Arming_SelectRow(row)` (`0043f71c`) is each row's handler, through 27 thunks from `00440300`, each of which sets `DAT_00476d58` for the length of the call. It does nothing for the row already lit (`DAT_00476d5a`) unless a guidance picture has been put up since (`DAT_00476d5c` not `-1`) or a hardpoint is selected. Otherwise it:

1. With a hardpoint selected, refuses a weapon [it will not fit](#fitting-a-weapon), and `Arming_FitSelected` (`0043dc44`) fits the one it accepts.
2. Runs `Arming_RefreshRows`, then puts the old row back to `0x27` or `0x25` by `Arming_RowLive` with its border `0x10`, and hides its picture and any guidance picture that is up.
3. Lights the new row: border and all four columns `0x29`, enabled whatever its unlock flag says. Shows its picture, or the blank one for `None`, and fills the three description lines from `wpn_desc.bin` entries `id * 3` to `id * 3 + 2`.
4. For ids `0xd` to `0x10` — the three missile racks and the Razor's launcher — captions the rack button with the weapon's name and shows the five buttons (`Arming_ShowGuidanceButtons`, `0043f5f7`, once, on `DAT_004769e6`); with a hardpoint selected it then runs `Arming_ShowGuidance` with that mount's kind and its second argument clear, which lights the kind's button and leaves the pictures alone. Any other id runs `Arming_HideGuidanceButtons` (`0043f644`), which puts the four guidance borders back to `0x22` and hides all five, and sets `DAT_00476d5c` to `-1`.
5. Stores the row in `DAT_00476d5a`.

A missile rack's row therefore leaves `DAT_00476d5c` where it was, with its picture hidden, so clicking the rack's row again runs the selection through.

### Guidance kinds

The four buttons call `Arming_ShowGuidance(kind, 1)` (`0043fd69`) with `ARM` 2, `ARH` 1, `SARH` 0 and `EO` 3 — the ids a fitted mount's record carries at `+0x08`, where the arming code reads 5 for an empty mount. It relights the borders, `0x22` on the kind in `DAT_00476d60` and `0x20` on the new one; with its second argument set it hides the lit row's picture, shows the kind's, fills the description lines from `wpn_desc.bin` `99 + kind * 3`, and stores the kind in `DAT_00476d5c`. It always stores the kind in `DAT_00476d60`, and `Arming_SetMountGuidance` (`0043dcb6`) writes it into the selected hardpoint's mount at `+0x08` when there is a hardpoint and a mount there.

The rack button's handler (`0044012a`) runs `Arming_SelectRow` on the lit row, which a guidance picture being up lets through: it puts the rack's own picture and description back.

`wpn_desc.bin` is three lines per weapon id: the 33 ids fill entries 0 to 98, and the four kinds 99 to 110 in id order.

### Entering the weapons screen

`Arming_Enter` shows the content panel, the inventory and the picture box. When the selected bay is `-1`, empty or holds a machine still being built, it calls `Squad_SelectBay` (`0043d64d`) with `Herc_FirstBuiltBay` (`00410c2a`). It then clears the hardpoint (`ArmingSelectedHardpoint`, `0xffff`) and runs `Arming_SelectRow(0)`. **So the screen opens on the first row, `AutoCannon 20mm`, with no hardpoint selected**, and a row click from there shows the weapon and fits nothing.

The tab handler stores 2 in `DAT_0047581c` only after the entry ([above](#what-a-tab-click-does)), so that `Squad_SelectBay` takes the arm of the tab being left. Every arm takes a finished machine, except that the crew arm refuses the Razor while the crew screen's selected row is not the player's, which leaves the screen with no bay. `Repair_Enter` makes the same call in the same place ([Which names a chassis shows](#which-names-a-chassis-shows)). Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

**This tab's arm of `Squad_SelectBay`** refuses an empty bay and an unfinished machine and accepts `-1`. It clears the hardpoint, clears part slot 12 of the old bay's picture — the slot `Arming_MarkHardpoint` (`004155db`) puts the selected socket's outline in — runs `Arming_SelectRow(0)`, and then swaps the bay pictures, relights the two roster rows, stores `DAT_00482ae5` and refreshes the readout.

### Fitting a weapon

A hardpoint is selected by `Arming_SelectHardpoint` ([below](#the-arming-and-repair-hotspots)), which the ten arming hotspots and the two steppers reach: `<` (`00440244`) calls `Arming_PreviousHardpoint` (`0043dd49`), which wraps from 0, or from none, to the last mount, and `>` (`004402a2`) calls `Arming_NextHardpoint` (`0043dd09`), which steps modulo the mount capacity `+0x4c`, so from none to the first. It selects the row of the mount's fitted weapon, `None`'s for an empty mount, and runs `Arming_MarkHardpoint`. With no bay selected the steppers, like everything else here, read the machine through `00482abf`, the dword before the bay array, and `>` divides by the capacity it finds there ([Open](#open)).

**`Arming_MarkHardpoint` outlines the socket.** It looks up the socket's `slot + 2` record in the group of the weapon the mount carries, `None`'s group for an empty one — every retail `arm_*.dat` has a `None` record for every socket — and puts the record's frame of the chassis's `dba\<stem>_out.dba` (the table at `0046ffc0`) in part slot 12, at the record's second position, with `0xba` remapped to `99`. It also redraws the socket's weapon part, which is what the bay picture already holds. A socket with no record, or one whose frame is `-1`, leaves slot 12 as it was, outline included. A bay change clears slot 12 ([above](#entering-the-weapons-screen)) and the tab's teardown clears all thirty parts of every bay picture (`Squad_HidePanel` through `Squad_FreeTabPictures`, `0043c95a`, and `Squad_FreeChassisPictures`, `004153f1`), so an outline lasts until the bay changes or the tab is left.

With a hardpoint selected, `Arming_SelectRow` refuses a weapon the armory holds none of unless the mount already carries it; `None` is never refused. The test reads the mount's guidance kind at `+0x08` where the weapon id at `+0x00` belongs before it reads the fitted id, so a mount whose kind number equals the weapon's id skips the refusal. `Arming_FitSelected` then calls `Herc_FitMount(herc, hardpoint, weapon)` only while `DAT_00476d58` is set, which is only inside a row's own thunk — the entry, a hardpoint, the steppers and the rack button select a row without fitting it — and in every case runs `Arming_MarkHardpoint` again.

**`Herc_FitMount` (`004114ec`) moves units, not counts.** The mount's unit goes back onto its weapon's stock list through `Armory_AddUnit` and the machine's occupied-mount count at `+0x4e` drops by one. `None` then leaves the slot empty at condition 100. Any other weapon takes the head of its list through `Armory_PopUnit` — the unit most recently returned or delivered, so refitting the fitted weapon puts the same unit back. The unit's `+0x04` becomes the hardpoint's condition through `HercStatus_Set`, `+0x4e` goes up by one, and the unit's guidance kind is written: ARH (1) for the three missile racks `0xd` to `0xf`, and 5 for every other id, the Razor's launcher `0x10` included. With the list empty the pop returns nothing, and the slot is left empty with its condition untouched. That is what the refusal's misread lets through: a weapon with none in stock whose id equals the mount's guidance kind. `AutoCannon 20mm`, `35mm` and `50mm` (ids 1-3) pass on a rack or the Razor's launcher set to ARH, ARM or EO, and `AutoCannon 100mm` (id 5) on a mount of kind 5, which `Herc_FitMount` gives every weapon but the three racks. The fitted unit returns to stock and nothing is fitted. Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

**The rows are regated only by a selection that goes through.** `Arming_RefreshRows` runs inside `Arming_SelectRow` after the fit, so the gating is whatever the last selection that passed the early return left. The entry and a bay change clear the hardpoint and then select row 0, which returns at once when row 0 is already lit and no guidance picture is up — leaving the rows gated for the hardpoint just cleared, the socket's dead rows disabled, until another row is selected. Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

## The repair screen

Tab 3, `REPAIR`. Its widgets are built once by `Repair_BuildScreen` (00432037), the screen is brought up by `Repair_Enter` and taken down by `Repair_Leave`, its rows are filled by `Repair_FillRow` (004339b2), its component names set by `Repair_SetComponentNames` (00433cdf), its selection moved by `Repair_SelectHotspot` (00433eb9) and its three readout panels refilled by `Repair_RefreshDetail` (00433445). Every rect is four immediates on the builder's stack and they are parent-relative, as everywhere else.

The content panel takes the right two thirds of the canvas. The left is the [damage diagram](#the-damage-diagram) and the [squad panel](#the-squad-panel); of those this builder owns only the eight internals pictures.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| 8 internals pictures | `Grid` | `{0x10, 0x2f, 0xe0, 0x12f}` in the canvas | one per bay, [below](#the-damage-diagram) |
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x278, 0x1d9}` | `0x3d` `REPAIR`, header 19 tall, plate `0x7f`-`0x10a`, face `0x25` |
| external list | `TitledPanel` | `{4, 0x1a, 0xe5, 0x105}` | `0x3e` `External`; `+0x65 = 0`, so no hatch and no plate |
| 6 group rows | `Panel` | `{0xc, i*0xc + 0x1c, 0xd5, i*0xc + 0x28}` | `0x4e`-`0x53` |
| 10 hardpoint rows | `Panel` | `{0xc, i*0xc + 0x72, 0xd5, i*0xc + 0x7e}` | built blank |
| internal list | `TitledPanel` | `{4, 0x10b, 0xe5, 0x1a3}` | `0x3f` `Internal`, same header |
| 9 component rows | `Panel` | `{0xc, i*0xc + 0x1c, 0xd5, i*0xc + 0x28}` | `0x54`-`0x5c` |
| mode label | `Text` | `{0xef, 0x1a, 0x179, 0x26}` | `0x40` `Mode:` |
| mode readout | `Button` | `{0xff, 0x2b, 0x168, 0x3e}` | `0x41` `Manual Repair` or `0x42` `Auto Repair` |
| salvage label | `Text` | `{0xef, 0x48, 0x179, 0x54}` | `0x43` `Salvage Available:` |
| salvage readout | `Button` | `{0xff, 0x59, 0x168, 0x6b}` | the pool, net of the build queue |
| item panel | `FramedPanel` | `{0xef, 0x77, 0x179, 0xf4}` | `0x4b` `Selected Item` |
| item cost | `Button` | `{0xf, 0x24, 0x78, 0x36}` in it | under `0x49` `Salvage Required:` |
| item condition | `Button` | `{0xf, 0x4b, 0x78, 0x5d}` in it | under `0x4c` `Condition:` |
| `REPAIR` | `Button` | `{0xf, 0x68, 0x78, 0x77}` in it | `0x4d` |
| total panel | `FramedPanel` | `{0xef, 0xfb, 0x179, 0x14f}` | `0x48` `Total` |
| total cost | `Button` | `{0xf, 0x24, 0x78, 0x36}` in it | under `0x49` again |
| `REPAIR ALL` | `Button` | `{0xf, 0x3f, 0x78, 0x4e}` in it | `0x4a` |
| scrap panel | `FramedPanel` | `{0xef, 0x156, 0x179, 0x181}` | `0x47` `Scrap Herc` |
| `SCRAP` | `Button` | `{0xf, 0x17, 0x78, 0x26}` in it | `0x46` |
| `CANCEL` | `Button` | `{0x112, 0x193, 0x156, 0x1a2}` | `0x44` |

Each row carries a click handler from the 25-thunk table at `0048d1f8`, one per `(column, row)` pair, and what that pair means and which clicks are refused are [below](#the-arming-and-repair-hotspots).

**Five of the "buttons" are readouts.** A `Button` is constructed with a border colour and an enable flag, and the mode, salvage, item cost, condition and total cost boxes are all built disabled with border `0x13` and caption `0x17` where a live button takes `0x22` and `0x29`. They are boxes with a figure in them, and a click on one stops there and does nothing ([Which widget a click reaches](#which-widget-a-click-reaches)). Four of the five also set the caption's `+0xc1`, so each clears its own rect before drawing and a refresh overwrites the last figure cleanly; the mode box, which changes only with the mode flag, does not.

The three `FramedPanel`s keep the constructor's `+0x55` of `0x25`, so their bodies carry a visible checkerboard — where the save screen flattens its own to `0x10`. The content panel keeps `+0x59` at the constructor's 1, so its body is filled rather than dithered and no backdrop shows through it.

**The condition readout's damage colour never reaches the screen.** `Repair_RefreshDetail` looks the band colour up with `Repair_DamageLevelColor` (0043da0f) and writes it into that widget's `+0xb5`, and then calls `ESMessage_SetString(widget, word, 2, 0x17, 1)` — which sets `+0xb5` from its fourth argument before painting, so the box is drawn in the same grey as every other readout. The write is dead. The *list rows* are colour-coded, because `Repair_FillRow` passes the band colour to `ESMessage_SetString` rather than writing it beside the call. Read from the decompile only; `ESMessage_SetString`'s argument order is corroborated by `ESMessage_Ctor` and by `Repair_Enter`, both of which pass a widget's own `+0x45` and `+0xb5` back in to mean "keep what is there".

### A row is four text columns

`ESQuad_AddColumns(panel, font, t0, …)` (0040a310) gives a row `Text` children at `+0x55`, `+0x59`, `+0x5d` and `+0x61`, each spanning from the previous one's right edge to its own, and the repair screen passes the same four edges for all 25 rows: `2`-`0x94` left-aligned, a zero-width second column that is never written, `0x94`-`0xab` built centred, and `0xab` to two inside the row's right edge, right-aligned. So a row reads as a name, a number and a percentage.

`Repair_FillRow(column, row)` fills one. The name column is the component's, the last column is `HercStatus_Get` (00411d06)'s reading of it formatted `"%d%%"`, and the colour that last column is drawn in is the damage band's ([below](#the-condition-readout)). A hardpoint row additionally puts the mount number, one-based, in the third column, left-aligned — the fill passes alignment 0 over the builder's centring — and takes its name from the fitted weapon — `estext.bin` `0x7e + id`, or `0x7d` `--Empty--` for an empty mount, whose percentage is replaced by the string at `0047465c` — two bytes, `20 00`, a single space, so the column reads blank rather than showing the 100 an empty slot's condition entry actually holds. **A row past the machine's mount capacity is blanked**, all three columns set to the string table's single space, rather than left showing the last machine's fitting.

### Which names a chassis shows

`Repair_SetComponentNames` holds two fifteen-entry tables of `estext.bin` indices — six group names then nine internal names — and picks the second **when the machine's chassis type is 8**, the Razor. It is a per-chassis substitution of all fifteen names at once, not a per-component list.

```
00474000   4e 4f 50 51 52 53  54 55 56 57 58 59 5a 5b 5c   walker
0047401e   4e 5d 5e 5f 60 61  62 63 56 57 58 59 5a 5b 5c   Razor
```

The Razor spends each of `0x5d`-`0x63` exactly once: the two torsos become nacelles, the chassis a fuselage, the legs wings, and the leg servos wing servos. Its cockpit and its last seven internals are the walker's. The condition arrays behind them are unchanged — a Razor's thirteen external facets group the same six ways.

It runs from one place, `Squad_SelectBay` (`0043d64d`)'s repair arm (`es2_xref.py` finds the one call), which a roster click on this tab and [the scrap dialog](#the-scrap-dialog)'s reselection reach. `Repair_Enter` calls `Squad_SelectBay` too, when the bay it opens on holds nothing it can work on, but tab 3's handler stores 3 in `DAT_0047581c` only after the entry, as tab 2's does ([Entering the weapons screen](#entering-the-weapons-screen)), so that call takes the arm of the tab being left. **Entering the tab does not set the names.** The lists keep the set the last bay change on this tab chose — until there is one, the walker set the builder constructs the rows with, `0x4e + row` and `0x54 + row` — so a machine selected on WEAPONS, BUILD or CREW is listed under the names of the chassis this tab last selected, a Razor under the walker's or a walker under the Razor's, until a roster click here selects another bay.

### The damage diagram

Both pictures are `Grid` widgets (`ESGrid_Ctor`, 0040b7e0): a filled panel with a `0x22` border, grid lines every 16 pixels in `+0x6e6` = `0x22` while `+0x6e5` is set, and thirty 56-byte part slots from `+0x55`. `ESGrid_SetPart` (0040b8cf) writes a slot: a position, a frame, blit flags at `+0x89`, and ten colour remap pairs — a source index at `+0x61` and a target at `+0x75`, the target defaulting to `0x10`. `ESGrid_Paint` (0040b97c) draws the panel and the lines, then blits the parts in slot order, and after each one fills the part's rect, `{x, y, x + width, y + height}`, through a lookup table that is the identity except for each pair whose target is not `0x10`. **A part's colour is chosen at paint time, and by rect rather than by mask**, so a recoloured part also recolours the matching pixels of any earlier part it overlaps.

`Repair_BuildDiagrams` (004140a9) fills both pictures for all eight bays:

- **`0048d4bc[bay]`, the exploded external picture.** These are the squad panel's eight pictures, built by `Squad_BuildRosterList` at `{5, 0x2b, 0xeb, 0x130}` and moved here to `{0x10, 0x2f}` and sized `0xd0` by `0x100`, with their grid lines on. The sizing writes the far corner as `origin + size - 1` ([below](#the-bay-picture)), so they end at `{0xdf, 0x12e}`, a pixel short of the internals pictures' literal `{0xe0, 0x12f}`. Each `gam\rpr_*.dat` body record becomes the part in the slot its id names, from frame `+0x12` of `dba\rpr_<chassis>.dba` with the record's flags, remapping index `0xe`. Each fitted mount then adds the record `RepairLayout_FindWeaponPart` (`00413ccc`) finds in the weapon's group with id `slot + 6`, from `dba\rpr_wpns.dba`, remapping `0xf`; a weapon with no record for that socket draws nothing.
- **`0048d118[bay]`, the internals picture.** One part in slot 0 from the chassis's single internals record, drawing its frame of `dba\<chassis>_int.dba` with flags 0. The Razor gets a second part in slot 1: frame 1 of the same bank at `(0x1d, 0xe)`. The decompiler shows that handle as a global of its own, `0046fe0c`; it is entry 8 of the bank cache at `0046fdec`, the one the Razor's first part was just loaded into.

The blit flags are 0 or 2 in every retail record, and 2 is the mirror: each left/right pair is one frame placed twice.

**The two sides disagree.** On all seven chassis with torsos — the Tomahawk's and Maverick's files have none — group 1 (`Left Torso`) sits on the viewer's left and group 4 (`Left Leg`) on the viewer's right, with 2 and 5 opposite them; the Razor's nacelles and wings split the same way. The `rpr_hots.dat` areas follow the layout records, so the colours and the clicks are consistent with the picture and with each other, and only the pairing of sides is wrong. Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

`Repair_ColorDiagram` (`0041469a`) colours whichever picture the selection's column has up, and `Repair_RefreshDetail` calls it on every refresh:

| Picture | Slot | Remap | Target |
|---|---|---|---|
| external | each body record's id | `0xe` | the band colour ([below](#the-condition-readout)) of group `id`, or `id - 0x10` for an id past 15 |
| external | `6 + slot`, each mount below the capacity | `0xf` | the band colour of that hardpoint |
| internals | 0 | the nine indices at `0046fe80`, `1d 1c 18 19 17 1e 16 1f 1b` | the band colour of internal 0-8, in list order |

Ids past 15 are the Razor's: its twelve body records are two parts per group, 0-5 and 16-21.

**Which of the two is up follows the selection.** `Repair_SwapDiagram` shows the internals diagram when the selection moves into the internals list and the exploded picture when it moves back, so the picture always matches the list being worked in. With no bay selected the squad panel shows its empty picture, `DAT_0048d4dc`, instead: the same widget at the builder's rect with its grid lines off.

`Hotspots_BuildOverlay` (0043c1a0) lays the clickable areas over each bay's exploded picture: the chassis's six `gam\rpr_hots.dat` areas as handlers 0-5, and for each fitted mount a panel over the weapon part's own rect (`Repair_WeaponPartRect`, `00414418`) as handler `6 + slot`. Every one is a `Panel` with `+0x51` cleared, so none of them draws. They are children of the external picture, so while the internals picture is up there is nothing on the diagram to click. Where two overlap — a weapon part over a body area, on several chassis — the one built later answers ([Which widget a click reaches](#which-widget-a-click-reaches)).

### Repairing and cancelling

**`REPAIR` (`Repair_OnRepair`, `00434b2d`) lifts the selection one level.** It takes `Repair_SelectionCost` off `CareerSalvage` — the whole pool, where the button is gated on the pool net of the build queue — and writes `Repair_TargetForLevel(Repair_DamageLevel(condition))` (`00413838`) into the selection through `HercStatus_Set(block, category, index, value)` (`00411cbd`), `HercStatus_Get`'s setter. The target is 100 from level 0 and the floor of the band above from any other, the same one the cost was quoted to ([`armory.md`](armory.md#what-one-repair-level-costs)). An external group is written by `HercStatus_SetGroup` (`00411c78`), which puts the value in every facet the group covers, so a group whose facets differ comes out uniform. The handler refills the row and the panels.

**`REPAIR ALL` (`Repair_OnRepairAll`, `00434c59`) rebuilds the machine.** It takes `Repair_HercCost(herc, 100)` off the pool and runs `Repair_Apply(herc, 100)` (`004113af`), which writes 100 into all thirteen facets, all nine internals and every mount slot below the capacity. A fitted mount at 0 is among them, so a destroyed weapon comes back at 100 for nothing: `Repair_HercCost` bills no mount at 0 ([`armory.md`](armory.md#repair-levels)). It then refills every row and the panels (`Repair_FillAllRows`, `00433caf`).

**`CANCEL` (`Repair_OnCancel`, `00434d73`) undoes, and does not leave the screen.** `Repair_Snapshot` (`004338f6`) copies `CareerSalvage` into `DAT_0048d25c` and the selected machine's 66-byte status block into `DAT_0048d260`, taken from the machine by `HercList_CopySelectedStatus` (`00434eb7`). It runs at the end of `Repair_Enter` and of the repair arm of `Squad_SelectBay`, so the snapshot is the pool and the machine as they stood when the tab was entered or the bay last changed. `CANCEL` writes the pool back outright and copies the status block over the selected machine, then refills every row and the panels. Each `REPAIR` and `REPAIR ALL` on the bay since then is undone. With no bay selected the snapshot copies the pool alone, and `CANCEL` still copies the old block — over the machine read through `00482abf` ([Open](#open)).

**The mode readout is the preferences' repair option**, `ShellOption_RepairMode` (`004824e4`, `prefs.cfg` option 44, [`../simulation/preferences.md`](../simulation/preferences.md)). `Repair_Enter` writes `0x42` `Auto Repair` for 0 and `0x41` `Manual Repair` for 1, and nothing for 2, so mode 2 keeps what the builder wrote, `Manual Repair`, or what an earlier entry did. None of the three buttons reads it; the debrief's repair pass, `Game_AutoRepairSquad` (`0040e804`), branches on it ([`armory.md`](armory.md#what-one-repair-level-costs)).

## The squad panel

`wsquadi.cpp`'s per-slot panel, built once by `Squad_BuildRosterList` (0043c999) and put up by `Squad_ShowPanel` (0043cfe7) on the WEAPONS, REPAIR, BUILD and CREW tabs. Every rect is a canvas literal.

| Widget | Class | Rect | Content |
|---|---|---|---|
| 8 pictures | `Grid` | `{5, 0x2b, 0xeb, 0x130}` | one per bay; filled and moved by the tab ([below](#the-bay-picture)) |
| empty picture | `Grid` | `{5, 0x2b, 0xeb, 0x130}` | `DAT_0048d4dc`, grid lines off |
| pilot label | `Text` | `{5, 0x134, 0x25, 0x141}` | `0x65` `Pilot:`, left, `0x1a` |
| pilot | `Text` | `{0x28, 0x134, 0x82, 0x141}` | left, `0x17`, opaque |
| skill label | `Text` | `{5, 0x141, 0x25, 0x14e}` | `0x66` `Skill:`, left, `0x1a` |
| skill | `Text` | `{0x28, 0x141, 0x82, 0x14e}` | left, `0x17`, opaque |
| condition label | `Text` | `{0x8d, 0x134, 0xea, 0x141}` | `0x67` `Condition:`, right, `0x1a` |
| condition | `Text` | `{0x8d, 0x141, 0xea, 0x14e}` | right, opaque |
| roster | `TitledPanel` | `{6, 0x154, 0xec, 0x1da}` | `0x64` `Squad Inventory`, header 19 tall, `+0x65 = 0` |
| 8 rows | `Panel` | `{9, i*0xe + 0x16, 0xe2, i*0xe + 0x23}` in the roster | border `0x10`, `0x29` on the selected bay |

The rows are 14 tall on a 14-pixel pitch, so unlike the repair lists they do not overlap. Each is a [four-column row](#a-row-is-four-text-columns) cut at `0x21`, `0x70` and `0x7b`: `"%d."` of the bay number centred, the machine's name left, `-` centred, and the crew column left. `Squad_RefreshRowNames` (`0043da47`) writes the name, `estext.bin` `0x6e + type`, in the band colour of the machine's overall condition (`HercStatus_OverallCondition`, 00411bd4), finished or not. `Squad_RefreshRowCrew` (`0043dad7`) writes the crew column in `0x27`: the assigned pilot's name, or `"%d%s"` of the build percentage and `0x6c` `% Complete` for a machine still being built.

`Squad_RefreshReadout(bay)` (`0043d38a`) fills the readout:

| Field | Bay with a pilot | Machine, no pilot | Empty bay |
|---|---|---|---|
| pilot | the pilot's name | `0x6d` `Unassigned` | blank |
| skill | `0x35 + skill` | blank | blank |
| condition | the band word `0x68 + level` of the overall condition, in the band colour; while the machine is being built, `"%d%s"` of the build percentage and `% Complete` in `0x20` | the same | blank |

A bay's pilot is `Squad_PilotForBay(00482a78, bay)` (`00410220`), which looks at exactly four records: the player's own, embedded at `+0x04`, and the three squad members the player structure points at from `+0x3f`. `Player_Read` (`004101b8`) sets those pointers on load to record `DAT_00483b48[k]` of squad `k`, so a pilot elsewhere in the squad block is never shown against a bay.

**A roster click is `Squad_SelectBay(bay)` (`0043d64d`)**, through eight thunks from `0043dde7`, each of which sets `DAT_004765be` for the length of the call. It returns at once for the bay already selected, and each tab takes the click its own way, picked by `DAT_0047581c`. The repair tab's arm refuses a bay that is empty or still being built; otherwise it hides whichever of the old bay's two pictures was up and shows the new bay's external one, relights the two rows, stores `DAT_00482ae5`, resets the selection with `Repair_SelectHotspot(0, 0)`, and refills the names, the rows and the panels. The weapons tab's arm is [above](#entering-the-weapons-screen), the build tab's [below](#scrapping-and-building-are-gated-on-the-bay), and the crew tab's [after it](#entering-the-crew-screen).

### The bay picture

`Squad_ShowPanel(tab)` starts with `Squad_BuildTabPictures(tab)` (`0043c915`), which fills the eight pictures for the tab: `Repair_BuildDiagrams` on the repair tab ([above](#the-damage-diagram)), and `Squad_BuildBayPictures` (`00414e5b`) on WEAPONS, BUILD and CREW. It then shows the selected bay's picture, or the empty one (`DAT_0048d4dc`) when no bay is selected.

`Squad_BuildBayPictures` moves each bay's picture to `{5, 0x2b}` at `0xe7` by `0x105` — `Window_SetWidth` (`0041ec33`) and `Window_SetHeight` (`0041ece6`) take a size and write the far corner as `origin + size - 1`, so the pictures end on row `0x12f`, one short of the rect they were built at — and switches their grid lines off. It fills them from `gam\arm_<chassis>.dat` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamarm_dat--armory-layout)) and three banks per chassis, whose stems are not the layout files': `out`, `rap`, `tom`, `sam`, `col`, `apoc`, `ogr`, `mav` and `fly`.

| Part | Slot | Frame | Position |
|---|---|---|---|
| top half | the first layout record's id | `dba\<stem>_bod.dba`, the record's own frame once the machine is built; `dba\mt_3qtr.dba` frame 0 at 0% built; body frame 2 below 51% built and 4 from it | the record's |
| bottom half | the second record's id | as above with frames 1, 1, 3 and 5 | the record's |
| each fitted weapon | the group record's id | `dba\<stem>_wep.dba`, the record's frame | the record's, one pixel in from the load |

A weapon's record is the one in its weapon-id group whose id is `slot + 2`, found by `RepairLayout_FindWeaponPart` (`00413ccc`), and a record whose frame is `-1` draws nothing. Every part takes its record's `+0x16` as its blit flags and no colour remap, so nothing on this picture is recoloured by damage. An empty bay, and the empty picture, get `mt_3qtr`'s two frames at `(1, 1)` and `(1, 0x8c)`. Retail's layouts put the body in slots 0 and 1 and the weapons from 2.

The body banks do not all carry the construction frames: `out_bod` and `mav_bod` have two frames, `rap_bod` and `tom_bod` four, and the other five six ([Open](#open)).

### What the buttons are gated on

`Repair_RefreshDetail` writes the same greying trio ([below](#the-condition-readout)) at three of the four:

| Button | Live when |
|---|---|
| `REPAIR` | the salvage pool, net of the build queue, covers lifting the selected component one level ([`armory.md`](armory.md#what-one-repair-level-costs)) |
| `REPAIR ALL` | it covers `Repair_HercCost(herc, 100)` — the whole machine to full, a different figure |
| `SCRAP` | there is a machine, the eight bays do not hold exactly one deployable machine (`Herc_HasSingleDeployable`, 00410add) — whichever bay that one is in, so a machine that cannot deploy is not scrappable beside a single one that can, and with none deployable every machine is — and its chassis is available — `(&DAT_00483b62)[type * 8]`, the `herc_inf.dat` `+0x0e` flag that `Herc_GrantUnlocks` (004118c5) sets and save block 7 carries ([`../formats/save-games.md`](../formats/save-games.md)) |
| `CANCEL` | always — no trio is written for it; what it does is [above](#repairing-and-cancelling) |

"Deployable" is `Herc_IsDeployable` (`00410a9d`): the bay is occupied, `+0x4a` is 100 so the machine is built, and `Herc_IsFlightworthy` (00411681) holds — both leg servos, the engine and life support all above 50. <!-- doc-lint: ok -->

`SCRAP`'s handler (`Repair_OnScrap`, `00434d15`) puts up [the scrap dialog](#the-scrap-dialog), the build screen's.

## The crew screen

Tab 6, `CREW`. Built once by `Crew_BuildScreen` (00440eb8, `wcrewi.cpp`), which also loads `dba\c_pilots.dba` and `dba\star.dba`; entered by `Crew_Enter` (00441a01) and hidden by `Crew_Leave` (00441afa), the teardown dispatcher's crew arm. Rects are parent-relative. The left of the canvas is [the squad panel](#the-squad-panel).

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x278, 0x1d9}` | `0xa8` `PILOT ASSIGNMENT`, header 19 tall, plate `0x7e`-`0x108`, face `0x25` |
| 3 squad portraits | image panel | `{0xbc, 0x19, 0xf7, 0x50}`, `{0xfc, …, 0x137, …}`, `{0x13c, …, 0x177, …}` | the squad member's portrait at `(1, 1)`, border `0x21` |
| `CLEAR` | `Button` | `{0x99, 0x188, 0xdf, 0x197}` | `0xa9`, border `0x22` |
| label | `Text` | `{10, 0x1d, 0xb4, 0x29}` | `0xab` `Available Pilots:`, right, `0x29` |
| 4 rows | `HatchedDivider` | `{0xd, i*0x4a + 0x58, 0x178, i*0x4a + 0x9e}` | `+0x55 = 0`, `+0x5d = 1`, border `0x21` |
| row portrait | image panel | `{0xb, 9, 0x43, 0x3d}` in the row | row 0 `star.dba` frame 0, rows 1-3 the pilot's portrait |
| 3 labels | `Text` | `{100, 0xb, 0xec, 0x18}`, then 13 lower twice | `0xac` `Name:`, `0xad` `Skill:`, `0xae` `Herc:`, right |
| 3 values | `Text` | `{0xef, 0xb, 0x164, 0x18}`, then 13 lower twice | left, opaque |

A pilot's portrait is the `c_pilots` frame its roster id (`+0x00`) names; the bank has twelve, one per roster id. The three squad portraits are the three pilots the player structure points at from `+0x3f` — the squad members of [the squad panel](#the-squad-panel).

**The image panel** is the class `ESBitmap_Ctor` (0040b698) builds. Its paint, `ESBitmap_Paint` (`0040b772`), blits the bitmap at `+0x5d` at the offset `(+0x55, +0x59)` and then draws the border over it with no fill, so a 56x52 portrait at `(0, 0)` in a row's 57x53 panel loses its top row and left column to the border. The constructor clears `+0x51`, which stops the paint drawing the border and leaves the bitmap; the builder sets it on all seven.

**`ESArm_Paint` (0040c513)** fills and borders the panel, draws horizontal lines in `+0x59` from row `+0x55` down to the bottom border, redraws row `+0x55` and the border in `+0x4d`, and with `+0x5d` set draws a second border one pixel inside the first. The constructor sets `+0x55` to the widget's height, which draws no lines at all; the crew builder writes 0, which makes the lines a solid body.

### The rows

Row 0 is the player; rows 1-3 are the three squad positions. `Crew_MatchRowPilots` (`00441b08`) fills the pointers at `004776e0` with the squad member whose position, pilot record `+0x27`, is the row — `Squad_MemberAtPosition` (`004102d6`) tests the three in pointer order — or null. `Crew_FillRows` (`00441c4f`) fills the values: the player's name, `0x35 + skill` and the chassis name `0x6e + type` of the player's bay (`00482a9e`), or `0x7e` `None` when that bay is empty; the same three from a squad member's record and bay; and for a row with no pilot, no portrait and `estext.bin` entry 0 in all three.

`Crew_ColourRows` (`00441857`) colours each row by whether it is below `DAT_00482a78`, the count of squad positions in play ([`../formats/save-games.md`](../formats/save-games.md#savgame_sav--block-order)):

| | Row in play | Row out of play |
|---|---|---|
| body, `+0x59` | `0x25` | `0x10` |
| labels | `0x19` | `0x1a` |
| value backing, `+0xc5` | `0x25` | `0x10` |

It writes the values' own colour too, `0x16` and `0x17`, but `Crew_FillRows` runs after it and its `ESMessage_SetString` calls overwrite it with `0x29`, so every value is drawn in `0x29`.

**`Crew_SelectRow(row)` (`00441b85`) selects a row**, and a row's panel and its portrait both carry it as their click handler, through four thunks from `004423b0`. The row's six texts are built with no handler and take no mouse events, so a click on one reaches the row ([Which widget a click reaches](#which-widget-a-click-reaches)). It relights the border of the row and its portrait — `0x21` on the row being left, `0x29` on the new one — calls `Squad_SelectBay` (`0043d64d`) with the row's pilot's bay, the player's for row 0 and `-1` for a row with no pilot, and only then stores the row in `DAT_004776dc`. It has no early return, so clicking the selected row runs it again.

### Entering the crew screen

`Crew_Enter` runs `Crew_ColourRows` (`00441857`) and `Crew_MatchRowPilots` (`00441b08`), sets the three squad portraits, selects rows 0, 1, 2, 3 and then 0 again, runs `Crew_FillRows` (`00441c4f`), and then calls `Squad_SelectBay(bay)` (`0043d64d`) for every bay that holds a machine, in order.

The tab handler has already stored 6 in `DAT_0047581c` ([above](#what-a-tab-click-does)), so all of those calls take `Squad_SelectBay`'s crew arm. It accepts `-1`, or a bay holding a finished machine that is not the Razor unless `DAT_004776dc` is 0 — a squad member cannot be given the Razor, and on a row click the row it tests is the one being left. It swaps the bay's picture and relights the roster rows as the repair arm does, stores `DAT_00482ae5` and refills the readout; while `DAT_004765be` is set, which a roster click and `CLEAR` do, it first gives the bay to the selected row's pilot ([below](#assigning-pilots)).

**So the screen opens on the last bay that holds a finished machine**, whatever bay was selected before and whichever is the player's: the row loop ends on row 0, which lets the closing loop accept every finished machine, the Razor included. The readout under the picture is that bay's until a row is clicked.

### Assigning pilots

Three clicks change the crew, all against the selected row.

**A squad portrait** — handlers `00442151`, `004421fc` and `004422a7` — lights its own border `0x29` and the other two `0x21`, then calls `Crew_AssignSquadMember(k)` (`00441eb8`). On the player's row that returns at once, so the portrait lights and nothing moves. On a squad row, whoever holds the row's position gives it up (`Squad_SetMemberPosition(k, -1)`, `004102be`), squad member `k` takes it, and the row pointers at `004776e0` follow; the member is then given the selected bay by `Crew_AssignSelectedBay`, exactly as a roster click gives it. The lit portrait is a widget colour that no entry resets, so it stays lit across visits.

**A roster click** reaches `Crew_AssignSelectedBay` (`00442055`) from inside `Squad_SelectBay`'s crew arm ([above](#entering-the-crew-screen)), so the bay already selected, and a bay the arm refuses, assign nothing. It runs only when the selected row is the player's or has a pilot. Whoever holds the bay loses it first: the player through `Player_SetBay(-1)` (`0040e6c8`, which writes `00482a9e`), and each squad member through `Squad_SetMemberBay(k, -1)` (`0040e6d7`), which also takes them off strength. The row's pilot then takes the bay, and for a squad member `Squad_UpdateOnStrength` (`00410366`) recomputes the on-strength byte for the row's position.

**Taking a bay leaves its old pilot with none.** Nothing hands the new pilot's previous bay on, so giving the player's bay to a squad member leaves the player's `Herc:` reading `None` until a bay is clicked on row 0. With no bay selected — where selecting an empty row leaves it — the bay handed out is `-1`, and every squad member with no bay counts as its holder.

**`CLEAR`** (handler `00442352`) calls `Crew_ClearRow` (`00441f94`), which on a squad row holding a pilot calls `Squad_SetMemberBay(k, -1)`, frees the position, nulls the row pointer, and then calls `Squad_SelectBay` with the member's bay — `-1` by then — with `DAT_004765be` set. The row is empty by that point, so the assignment inside finds no pilot, and what remains is the picture going to the empty bay. On the player's row, or an empty one, it does nothing.

Both the unassign and the recompute write the on-strength byte through `Squad_SetOnStrength` (`00410327`), which moves `00482a7a`, the count of machines on strength ([`../formats/save-games.md`](../formats/save-games.md#savgame_sav--block-order)), by one whenever the byte changes.

## The build screen

Tab 4, `BUILD`, the `Herc Construction` panel. Built once by `Build_BuildScreen` (`00445758`), entered by `Build_Enter` (`0044690d`) and hidden by `Build_Leave` (`0044694e`), the teardown dispatcher's build arm. Rects are parent-relative. The left of the canvas is [the squad panel](#the-squad-panel), filled with the three-quarter [bay picture](#the-bay-picture).

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x27c, 0x162}` | `0xba` `Herc Construction`, header 19 tall, plate `0x7f`-`0x10a`, face `0x25`, dithered body in `0x25` |
| 9 blueprints | `Grid` | `{0xb2, 0x2d, 0x180, 0x12d}` | one per chassis type, border `0x27`, grid lines `0x18` ([below](#the-blueprints)) |
| chassis list | `FramedPanel` | `{0x16, 0x4b, 0x9a, 0xd1}` | face `0x10` |
| 9 rows | `Panel` | `{10, i*0xe + 4, 0x6e, i*0xe + 0x11}` in the list | `0x6e + i`, the chassis name, border `0x10` |
| stats box | `FramedPanel` | `{0x60, 0xe3, 0xab, 0x11f}` | face `0x10` |
| 2 headings | `Text` | `{0x16, 0x28, 0x9a, 0x34}`, `{0x16, 0x34, 0x9a, 0x40}` | `0xbc` `Select Herc`, `0xbd` `Type To Build`, centred, `0x1a` |
| 4 labels | `Text` | `{8, 0xe9, 0x5e, 0xf5}`, then 12 lower three times | `0xbb` `Mass`, `0xbe` `Speed`, `0xbf` `Hardpoints`, `0xc0` `Salvage Reqd`, left, `0x1a` |
| 4 figures | `Text` | `{5, 6, 0x45, 0x12}` in the stats box, then 12 lower three times | left, `0x17`, opaque |
| lower panel | `FramedPanel` | `{0xf1, 0x166, 0x27c, 0x1d9}` | face `0x10` |
| salvage label | `Text` | `{0x7d, 9, 0x10e, 0x15}` in the lower panel | `0xc1` `Salvage Available`, centred, `0x1a` |
| salvage box | `FramedPanel` | `{0x91, 0x1a, 0xfa, 0x2c}` in the lower panel | face `0x10`; its figure a `Text` at `{1, 3, 0x68, 0xf}`, centred, `0x17`, opaque |
| scrap box | `FramedPanel` | `{0x37, 0x38, 0xc1, 100}` in the lower panel | face `0x25`; `0xc2` `Scrap Herc` at `{8, 6, 0x82, 0x12}`, centred, `0x1a` |
| `SCRAP` | `Button` | `{0xf, 0x17, 0x78, 0x26}` in the scrap box | `0xc4`, border `0x22`, built disabled |
| build box | `FramedPanel` | `{0xcc, 0x38, 0x156, 100}` in the lower panel | face `0x25`; `0xc3` `Build Herc`, placed as the scrap box's title |
| `BUILD` | `Button` | `{0xf, 0x17, 0x78, 0x26}` in the build box | `0xc5`, border `0x22`, built disabled |

The builder writes `+0x4d = 0x27` over the constructor's border on every panel and blueprint. The content panel's `+0x59 = 0` and `+0x5d = 0x25` make its body a checkerboard over [the scope's black](#the-palette), which is why the labels sit on a dithered ground while the boxes holding figures are flat. The lower panel is parented to the shell's top-level window rather than to the content panel, so the two are siblings and `Build_Enter` shows each.

**The rows are 14 tall on a 14-pixel pitch.** Each is a [four-column row](#a-row-is-four-text-columns) cut at `0x62`, `0x62` and `0x62`: the name centred from `2` to `0x62`, then three empty columns, two of them zero-wide. Nine rows means every chassis type has one, the Razor included.

The four figures are `herc_inf.dat`'s first four stats for the selected chassis, as `Herc_BuildScreenRefresh` (`00446cfa`) formats them ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md)). The salvage figure is `Build_RefreshSalvage` (`00446e71`)'s `"%ld %s"` of `(CareerSalvage - Armory_QueuedTotal()) / 1000` and `0xc6` `TONS` — the same net pool [the repair screen](#the-condition-readout) quotes in kilograms.

### Choosing a chassis

`Build_SelectChassis(chassis)` (`00446c3b`) is each row's handler, through nine thunks from `00446fbf`. It returns at once for the chassis already selected, `DAT_004786e4`; otherwise it sets the old row's name to `0x27` and its border to `0x10` and hides its blueprint, sets the new row's name and border to `0x29` and shows its blueprint, stores the chassis, and runs `Build_GateButtons` and `Herc_BuildScreenRefresh`. `DAT_004786e4` is `-1` in the image, and `es2_xref.py` finds no other store to it ([Open](#open)).

`Build_GateRows` (`00446835`) gates the rows on the chassis availability flag, `(&DAT_00483b62)[type * 8]` ([What the buttons are gated on](#what-the-buttons-are-gated-on)): a chassis without it has its row disabled and all four columns set to `0x10`, the background, so the list shows a gap where it is and a click on the gap does nothing. Every other row is enabled with all four columns at `0x27` — the selected row's name included.

`Build_Enter` runs `Build_GateRows`, `Herc_BuildScreenRefresh`, `Build_GateButtons` and `Build_FillBlueprints`, shows the content panel, the lower panel and chassis 0's blueprint, and calls `Build_SelectChassis(0)`. **So the screen always opens on chassis 0**, available or not. Coming back to the tab with chassis 0 still selected, the select returns at once, and the row keeps its lit border under the name `Build_GateRows` has just set back to `0x27`.

### Scrapping and building are gated on the bay

`Build_GateButtons` (`004469d4`) writes the [greying trio](#the-condition-readout) at both buttons from the bay the squad panel has selected, `DAT_00482ae5`:

| Bay | `SCRAP` | `BUILD` |
|---|---|---|
| empty | dead | live when the net pool is **more** than the selected chassis's price times 1000, compared unsigned |
| occupied | the repair screen's `SCRAP` test: the bays not holding exactly one deployable machine, and its chassis available | dead |

A machine is built into an empty bay, and only an occupied one can be scrapped. A pool exactly equal to the price leaves `BUILD` dead. With no bay selected the function reads the dword before the eight-pointer array at `00482ac3` as the bay's machine: `00482abf`, which is `+0x47` of the player structure at `00482a78` — the third of [the squad-member pointers](#the-squad-panel) at `+0x3f` ([Open](#open)).

**This tab's arm of `Squad_SelectBay` takes any bay.** Unlike the repair and crew arms it refuses nothing, an empty bay and an unfinished machine included: it swaps the bay pictures, relights the two roster rows, stores `DAT_00482ae5`, refreshes the readout, and runs `Build_GateButtons` for any bay but `-1`.

**`BUILD` buys the selected chassis into the selected bay and pays for it at once.** Its handler (`Build_OnBuild`, `00446f3e`) calls `Hangar_BuySelected` (`0040e91c`) with `DAT_004786e4`. That calls `HercList_OrderIntoSelected` (`00410982`), which allocates a record, stores it in the slot `DAT_00482ae5` names without looking at what the slot holds, adds one to the hangar's count at `00482ae3` and runs `Herc_Order` on it; `0040e91c` then takes the price `Herc_Order` returns off `CareerSalvage`. Nothing is queued: the machine is in the bay at 0% built and the pool is down by its price ([`armory.md`](armory.md#buying-a-chassis--herc_order-00411019)). The handler refreshes the roster's names and crew column, the readout, the salvage figure and the gate, which leaves `BUILD` dead on the bay it has just filled.

`SCRAP`'s handler (`Build_OnScrap`, `00446ee0`) puts up [the scrap dialog](#the-scrap-dialog).

### The blueprints

`Build_FillBlueprints` (`0041579d`) fills the nine grids from the same records and banks as the repair screen's [exploded external picture](#the-damage-diagram): each `gam\rpr_*.dat` body record, from `dba\rpr_<chassis>.dba`, in the slot its id names with the record's flags. No weapon is drawn. Every part's remap pair is `0xe` to `0xe`, which `ESGrid_Paint` applies because the target is not `0x10` and which changes nothing, so the parts show in their own ink. `Build_Leave` frees the nine banks (`Build_FreeBitmapsAndGrids`, `00415928`) and `Build_Enter` loads them again.

## The scrap dialog

Both `SCRAP` buttons — the build screen's (`Build_OnScrap`, `00446ee0`) and the repair screen's (`Repair_OnScrap`, `00434d15`) — call `ScrapDialog_Show` (`00447711`), which quotes the machine in the selected bay, `DAT_00482ae5`, and puts up a dialog `ScrapDialog_Build` (`00447328`) built once at startup, after every tab screen. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| window | `Window` | the display root's own rect | |
| panel | `ESAlert` (`0040afe0`, `esalert.cpp`) | `{0xcb, 199, 0x1af, 0x149}` | `0xc9` `WARNING`, border `0x27`, header 20 tall, plate `0x3f`-`0xa6`, face `0x25`, dithered body in `0x10` |
| 2 frames | `FramedPanel` | `{5, 0x18, 0xdf, 0x60}`, `{10, 0x20, 0xd8, 0x58}` in the panel | border `0x15`, filled |
| caption | `Text` | `{0xc, 0x2e, 0xd6, 0x3b}` in the panel | `0xcc` `This herc will yield`, centred, `0x29` |
| figure | `Text` | `{0xc, 0x3c, 0xd6, 0x49}` in the panel | centred, `0x29` |
| `CANCEL` | `Button` | `{10, 0x6a, 0x6c, 0x79}` in the panel | `0xca`, border `0x22` |
| `ACCEPT` | `Button` | `{0x76, 0x6a, 0xd8, 0x79}` in the panel | `0xcb`, border `0x22` |

`ESAlert_Ctor` is `ESTitle_Ctor` with its own vtable, the mouse bits ORed into the event mask and a buffer the size of its rect; its paint is `ESTitle_Paint`. Both frames keep the class's `0x25` checkerboard.

`ScrapDialog_Show` writes `"%d %s"` of `Herc_ScrapValueTons` (`0041140f`) — `Herc_ScrapValue` over 1000 ([`armory.md`](armory.md#scrapping)) — and `0xcd` `tons of salvage.` into the figure, then calls `Alert_Show` (`0040b239`), which shows the panel's parent, the window, and then the panel. `ScrapDialog_Hide` (`00447795`) is `Alert_Hide` (`0040b258`), the same two in the other order.

**While the dialog is up, nothing beneath it takes a click.** The window is built by `ESWindow_Ctor` alone, so it is hidden from construction until `Alert_Show` shows it ([Showing and hiding a widget](#showing-and-hiding-a-widget)). It is a child of the display root, as the top-level window every tab screen hangs from is, and built after it, so [the hit test](#which-widget-a-click-reaches) tries it first, and it covers the display. A click outside the panel lands on the window, whose event mask has no mouse bit, and climbs to the display root, never reaching the screen beneath; one on the panel outside its two buttons is swallowed by the disabled panel.

**`CANCEL` (`ScrapDialog_OnCancel`, `00447c38`) only takes the dialog down**: its body is `ScrapDialog_Hide` and the handler's epilogue. The builder passes it as a bare address, so auto-analysis made no function of it; `ES2DefineFunctionAt` does.

**`ACCEPT` (`ScrapDialog_OnAccept`, `00447c96`) scraps.** It takes the dialog down and runs `Hangar_ScrapSelected` (`0040e757`) on the selected bay ([`armory.md`](armory.md#scrapping)). `Squad_RefreshAfterScrap` (`0043d17c`) then puts the empty-bay picture in the bay's slot (`Squad_SetEmptyBayPicture`, `00415506`), refreshes the readout and the roster's names and crew column, and the salvage figure of whichever tab is up. The build tab then regates its buttons; the repair tab calls `Squad_SelectBay(Herc_FirstBuiltBay())` and `Repair_RefreshDetail`, so it moves to the first bay holding a finished machine. Last it repaints the repair screen's external list and condition readout and the squad panel's condition text. **On the build tab the scrapped bay stays selected**, empty now, so `SCRAP` is dead and `BUILD` gated on the pool.

**The armory's `Scrap` has a twin**, built next by `WeaponScrapDialog_Build` (`004477ae`) with the same rects and colours and `0xce` `These weapons will yield` for its caption. `WeaponScrapDialog_Show` (`00447b97`) keeps the lit weapon's id in `DAT_0048d970` and writes `"%d %s"` of `Armory_ScrapValueTons(id)` ([`armory.md`](armory.md#scrapping)) and `0xcd` into the figure; its `CANCEL` (`00447d59`) only takes it down. Its `ACCEPT` (`WeaponScrapDialog_OnAccept`, `00447db7`) takes it down, sells the stock (`Armory_ScrapWeapons`, `0040e7b2`), runs `Armory_RefreshQueue` (`00412413`) over the build queue, and then `Armory_RefreshRows` and `Armory_RefreshReadout`. The row refresh puts every live row's four columns back to `0x27`, the lit row's included, and the readout then rewrites only the lit row's count held in `0x29` — so after a scrap the lit row reads as unlit but for its count, and stays the lit row, so a click on it queues.

## The armory screen

Tab 5, `ARMORY`, the weapon build queue ([`armory.md`](armory.md#the-weapon-build-queue--armory_-armorycpp-00411efd)). Built once by `Armory_BuildScreen` (`00447e34`, `warmoryi.cpp`), entered by `Armory_Enter` (`004494f7`) and hidden by `Armory_Leave` (`004495b6`), the teardown dispatcher's armory arm. Rects are parent-relative. The tab has no squad panel, and the content panel, parented to the shell's top-level window, spans the canvas.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0, 0x2d, 0x27f, 0x1d9}` | `0xcf` `ARMORY`, header 19 tall, plate `0xf1`-`0x18c`, face `0x25`, filled body |
| list | `TitledPanel` | `{0xd, 0x1a, 0x117, 0x1a3}` | `0xd0` `Armaments Inventory`, header 19 tall, face `0x24`, `+0x65 = 0` |
| 6 headings | `Text` | [below](#the-armory-rows), in the list | `0x29` |
| 26 rows | `Panel` | `{8, i*0xc + 0x3c, 0xf8, i*0xc + 0x48}` in the list | border `0x21`, `+0x51 = 0` |
| picture box | `HatchedDivider` | `{0x121, 0x1a, 0x272, 0xf3}` | border `0x15`, `+0x55 = 0x77`, `+0x59 = 0xf`, `+0x49 = 0` |
| 26 weapon pictures | image panel | each `gam\arm_weap.dat` record's corner in the box, sized to its frame | that frame of `dba\arm_weap.dba` |
| 5 info lines | `Text` | `{0, 0x8a, W, 0x96}`, `{0, 0x96, W, 0xa2}`, `{0, 0xa2, W, 0xae}`, `{0, 0xae, W, 0xbc}`, `{0, 0xbc, W, 200}` in the box | centred, `0x29`, opaque in `0xf` |
| readout panel | `FramedPanel` | `{0x121, 0xf8, 0x272, 0x14e}` | filled, border `0x15`, face `0xf` |
| 2 workspace labels | `Text` | `{0x4a, 9, 0xdc, 0x15}`, `{0x4a, 0x15, 0xdc, 0x21}` in it | `0xd3` `Workspace Available:`, `0xd4` `Workspace In Use:`, left, `0x29` |
| 2 workspace counts | `Text` | `{0xdc, 9, 0xeb, 0x15}`, `{0xdc, 0x15, 0xeb, 0x21}` in it | built as `1` and `4`, right, `0x29`, opaque |
| 2 salvage labels | `Text` | `{0x28, 0x2b, 0xa2, 0x37}`, `{0xa2, 0x2b, 0xfe, 0x37}` in it | `0xd5` `Salvage Available:` left, `0xd6` `Allocated:` right, `0x29` |
| salvage box | `Button` | `{0x31, 0x3d, 0x93, 0x4e}` in it | built blank, border `0x13`, disabled, caption opaque |
| allocated box | `Button` | `{0xaf, 0x3d, 0x111, 0x4e}` in it | the same |
| button panel | `FramedPanel` | `{0x121, 0x153, 0x272, 0x18c}` | filled, border `0x15`, face `0xf` |
| `Clear` | `Button` | `{0x31, 0x14, 0x93, 0x23}` in it | `0xd1`, border `0x22` |
| `Scrap` | `Button` | `{0xaf, 0x14, 0x111, 0x23}` in it | `0xd2`, border `0x22` |

`W` is the parent's own width, `+0x2d - +0x25`. The picture box is [the weapons screen's](#the-weapons-screen) with a different rect and band: black above row `0x77` and solid `0xf` below it, the pictures in the black and the info lines in the band. The builder reads `arm_weap.dat`'s weapon list into `0048d984`, indexed by `Arming_RowOfWeapon`, and reads past the file's guidance list without building a picture from it.

### The armory rows

The headings are `0xd7` `Num to` at `{10, 0x19, 0x4b, 0x25}` over `0xd8` `build` at `{10, 0x25, 0x4b, 0x31}`, `0xd9` `Type` at `{0x4c, 0x25, 0xaa, 0x31}` and `0xda` `avail.` at `{0xb4, 0x25, 0xdc, 0x31}`, all left-aligned, and `0xdb` `Salv.` at `{0xdc, 0x19, W - 9, 0x25}` over `0xdc` `req.` at `{0xdc, 0x25, W - 9, 0x31}`, right-aligned.

The rows list the first 26 weapon ids of the table at `004769b0` — [the weapons screen's](#the-inventory-rows) without `None` — 13 tall on a 12-pixel pitch, so, as in the repair lists, the lower row owns the shared line. Each is a [four-column row](#a-row-is-four-text-columns) cut at `0x32`, `0xb4` and `0xc9`: the queued count and the weapon's name, both left-aligned, then the count held and the price in tons, both right-aligned. The name is `weapons.bin`'s, through `weapons.dat` record `+0x10` rather than `estext.bin`, and the price is `"%d"` of `+0x14 / 1000`, written once by the builder ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#weaponsdat-catalog-record-29-bytes)).

**A row draws no border.** The builder installs the list-row vtable at `00479bb0` on every row, as the other row builders do, and then clears `+0x51`. Its paint, `ESQuad_Paint` (`0040a600`), clears the interior to `0x10`, runs `ESRect_FillAndBorder` — which draws nothing with `+0x51` clear — and paints the four columns, so the lit row shows only in its text.

`Armory_RefreshRows` (`00449329`) runs on every entry. A weapon whose unlock flag `+0x16` is clear has its row disabled and its four columns set to `0x10`, the background, so the list shows a gap and keeps the builder's texts behind it. An unlocked weapon's row is enabled, and its first column set to `"[ %1d ]"` of `Armory_QueuedCount` (`00412642`) or to `"[   ]"` with none queued, its third to `"%d"` of the count held, and all four to `0x27`.

### Selecting and queueing

Row `i`'s handler is `Armory_OnRow`*i* (`Armory_OnRow00`, `0044a0b6`, through `Armory_OnRow25`, `0044ac0a`, `0x74` bytes apart), which the builder takes from `ArmoryRowHandlers` (`0048dab0`). Each calls `Armory_ClickRow` (`0044969f`) on the left release and `Armory_RightClickRow` (`004499de`) on the right. On any row but the lit one (`Armory_LitRow`, `00479174`), and on every row while weapons are built automatically, both select it: the old row goes back to `0x27` and its picture is hidden, and the new row is lit `0x29`, its picture shown and the five info lines filled from `wpn_info.bin` entries `Arming_RowOfWeapon(id) * 5` onward — `row * 5`, since the rows and that table share an order.

Whether weapons are built by hand is `prefs.cfg` option 45, `ShellOption_WeaponsBuildMode` ([`../simulation/preferences.md`](../simulation/preferences.md)). When they are, the lit row queues instead:

| Click on the lit row | Does |
|---|---|
| left | with a queue slot free and `Armory_QueuedTotal() + price` **less than** the pool, `Armory_Enqueue` of the weapon, the first column rewritten in `0x29`, and the readout refreshed |
| right | one unit fewer: `Armory_DequeueLit` (`0044963c`) takes every queued unit of the weapon off, and `Armory_ClickRow` then queues the count less one back |

A pool exactly covering the queue plus the price refuses the unit, as the build screen's `BUILD` refuses [a pool equal to the chassis's price](#scrapping-and-building-are-gated-on-the-bay).

`Armory_Enter` writes the [greying trio](#the-condition-readout) at `Clear` from the build mode, so `Clear` is live only while weapons are built by hand, then runs `Armory_RefreshRows` and `Armory_RefreshReadout`, shows the content panel and the list, and calls `Armory_ClickRow(0)`. `Armory_Leave` puts the lit row back to `-1`, so **the screen always opens on the first row, `Autocannon 20mm`**, and that call selects rather than queues.

`Clear` (`00449ef4`) is `Armory_DequeueLit`: every queued unit of the lit weapon comes off, the first column goes back to `"[   ]"` in `0x29`, and the readout is refreshed. `Scrap` (`Armory_OnScrap`, `00449e78`) puts up [the scrap dialog](#the-scrap-dialog)'s twin on the lit weapon, when a row is lit, and its `ACCEPT` sells the armory's whole stock of it.

### The armory readout

`Armory_RefreshReadout` (`00449cab`) writes all four figures centred in `0x29`: the salvage box `"%ld %s"` of `CareerSalvage - Armory_QueuedTotal()` and `0xc8` `kg`, the allocated box `"%d %s"` of `Armory_QueuedTotal()` and `kg`, and the two workspace counts, the queue's free slots and five less them. It then rewrites the lit row's count held in `0x29`. The two boxes are built as [the repair screen's readouts](#the-repair-screen) are — disabled, border `0x13` — but their captions are drawn in `0x29` rather than `0x17`.

## The mission screen

Tab 7, `MISSION`. Built once by `Mission_BuildScreen` (`00442534`), put up by `Mission_Show(view)` (`004441e3`) and taken down by `Mission_Leave` (`00444a05`), the teardown dispatcher's mission arm. One set of widgets serves the tab's three views, which the show routine moves, retitles and shows or hides. Rects are parent-relative; the four panels, the location picture and the button bar are parented to the shell's top-level window.

The view is `DAT_0048106c`: 0 the campaign map, 1 the briefing, 4 the debrief. `Mission_ShowView(view)` (`0043a857`) stores it. The tab handler asks for the map while both the map's first-show flag `DAT_004778aa` and the mission-within-stage counter `DAT_0046fb1a` are zero, and for the briefing otherwise. The flag is clear in the image and the map view sets it on its first show; [`RESTORE`](#leaving-the-save-screen) and a new career's [`ACCEPT`](#starting-a-campaign) clear it again, and a `CONTINUE GAME` load does not. So on a stage's first mission the map comes up on the tab's first click after the shell starts, a restore or a new career, and every later click opens the briefing. The debrief is never the tab's choice: [the campaign layer](campaign-loop.md#where-the-debrief-goes-next) writes 4 while processing a finished mission, and the load of the next mission that follows puts the tab up in it.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| Telecomm | `TitledPanel` | `{7, 0x2b, 0x113, 0x12b}` | `0xb0` `Telecomm`, header 19 tall, `+0x65 = 0` |
| Telecomm picture | image panel | `{10, 0x14, 0xf9, 0x100}` in Telecomm | `dba\terradef.dba` frame 0, no border; handler `FUN_00444e28` |
| location picture | image panel | the top-level window's own rect | `+0x51 = 0`; the theater bitmap `maybe_Mission_UpdateLocationTab` (`0044409f`) loads |
| map panel | `TitledPanel` | `{0x117, 0x2b, 0x278, 0x12b}`, top `0x2a` when full-screen | titled by the view, header 19 tall, face `0x25`, plate `0x26`-`0x13b` |
| 6 map buttons | `ButtonIcon` | `{0x137, y, 0x155, y + 0x1e}` in the map panel, `y` = `0x18`, `0x3e`, `0x64`, `0x8a`, `0xb5`, `0xdb` | `dba\miss_arw.dba`, unlit/lit frames `1`/`0`, `3`/`2`, `10`/`8`, `11`/`9`, `7`/`6`, `5`/`4`; `+0x5d = 0`, `+0x61 = 1` |
| map grid | `Grid` | `{0xb, 0x18, 0x131, 0xf9}` in the map panel | border `0x22`, grid lines off |
| map scope | palette scope | the same rect in the map panel | `DAT_0048d818`, born hidden ([below](#the-three-views)) |
| summary | `TitledPanel` | `{7, 0x133, 0x278, 0x1a7}` | `0xaf` `Mission Summary`, header 19 tall, `+0x65 = 0` |
| 5 text boxes | text box | `{10, 0x15, 0x265, 0xa8}` for the first, `{10, 0x15, 0x23f, 0x73}` for the others, in the summary | [below](#the-summary-text-box) |
| 20 report texts | `Text` | in the map panel | [the mission report](#the-mission-report)'s labels and figures |
| 2 page buttons | `ButtonIcon` | `{0x247, 0x18, 0x265, 0x36}`, `{0x247, 0x51, 0x265, 0x6f}` in the summary | `miss_arw` frames `1`/`0` and `3`/`2`; `+0x5d = 0`, `+0x61 = 1` |
| button bar | `Panel` | `{7, 0x1b1, 0x278, 0x1d9}` | border `0x22`, `+0x49 = 0` |
| 4 buttons | `Button` | `{0xe, 0x10, 0x96, 0x23}`, `{0x9d, …, 0x125, …}`, `{0x12d, …, 0x1b5, …}`, `{0x1ea, …, 0x263, …}` in the bar | `0xb4` `Mission Briefing`, `0xb6` `Mission Objectives`, `0xb7` `Intelligence Report`, `0xb8` `Rock & Roll >`; border `0x22` |

The map panel's top is `0x2b` while `DAT_00481e68`, the shell's full-screen flag, is clear, and `0x2a` while it is set; `Reference/Managment_Mission_Briefing.png` shows it level with Telecomm, so that capture was taken windowed. The builder writes the map panel's face and plate over `ESTitle_Ctor`'s `0x24` and zeros and leaves its hatch on; Telecomm and the summary keep the constructor's face and clear `+0x65`. All three keep the filled body, so nothing of the backdrop shows.

`ESTitle_Ctor` clears `+0x49` and the builder clears the button bar's, so a click on a panel, the bar or anything they hold with no handler of its own is swallowed. The Telecomm picture has a handler, and it returns at once ([above](#input-while-a-movie-plays)).

### The three views

`Mission_Show(view)` first runs `Mission_LoadPictures` (`00443f33`), which loads `terradef.dba` into the Telecomm picture and, for the map view only, `dba\th_earth.dba` below stage 5 or `dba\th_moon.dba` from it as the map grid's part 0. It then puts the summary at `{7, 0x133, 0x278, 0x1a7}` and does what the view needs:

| | Map, 0 | Briefing, 1 | Debrief, 4 |
|---|---|---|---|
| map panel title | `"%s %s"` of the sector, `0x76 + stage`, and `0x7c` `Sector` | `0xb3` `Mission Map` | `0xb9` `Mission Report` |
| summary | moved down to `{7, 0x133, 0x278, 0x1dc}`; text box 0 | text boxes 1-3 filled, box 1 up | text box 4 |
| map grid | shown | | |
| map buttons, button bar, four buttons | hidden | shown | hidden |
| page buttons | hidden | shown | shown |
| report texts | hidden by `Mission_HideReportTexts` (`00444914`) | the same | untouched: up with the map panel only on the screen's first view since the shell started |
| `DAT_004778a8` | 1 | 2 | |
| movie | the two map movies, on the view's first show (`DAT_004778aa`) | `Career_BriefingMovie` (`004135da`), once per load (`DAT_004778ab`) | `Career_DebriefMovie` (`004135e1`), once per load (`DAT_004778ac`) |

All three show Telecomm, the Telecomm picture, the map panel and the summary. In the map view nothing on the screen acts on a click: the Telecomm picture's handler returns at once and nothing else in the view has a handler, so it is left only through the strip. The map grid shows its part 0 at its origin, unremapped, over its filled body, and the stage's text is box 0's first page, with no page buttons to move it.

**What comes back with the map panel is decided at construction.** The builder hides the map panel straight after building it, so every child is built under a hidden parent, and a constructor's own show then sets bits 2 and 4: the child returns whenever the panel is shown ([Showing and hiding a widget](#showing-and-hiding-a-widget)). A later hide by name clears bit 4 again, so the child returns only when shown by name:

- **The map grid** is hidden by name as soon as it is built, and `Mission_Show` shows it by name in the map view alone. It never covers the briefing's map.
- **The map scope** is built by `ESWindow_Ctor` alone, so it is born hidden with bit 4 clear and does not come back with the panel; only a show by name would put it up, and `es2_xref.py` finds no reference to `DAT_0048d818` but the builder's store ([Open](#open)). While it stays hidden its `0x10` fill does not cover the map grid.
- **The report texts** return with the panel until `Mission_HideReportTexts` first runs. The map and briefing views run it before the panel is shown, and `Mission_Leave` runs it on the way out, so the debrief shows them only when it is the screen's first view since the shell started.

The map view's flag `DAT_004778aa` is set whether movies are on or off; with them off `Movie_Enqueue` adds nothing and the view stays up. With them on, the second map movie carries the location flag, so the tab comes down after it and the location picture goes up, or the lunar drop plays at stage 5 ([The shell's movies](#the-shells-movies)).

The debrief fills box 4 through `Mission_DebriefText` (`00444bbb`) and takes its movie through `Mission_DebriefMovie` (`00445751`), thunks to the two career accessors.

The briefing also lights `Objectives`, `Intelligence` and `Rock & Roll` — caption `0x29`, border `0x22`, enabled — then lights `Mission Briefing` through `Mission_LightViewButton(1)` and puts text box 1 up through `Mission_ShowTextBox(1)`, and writes `stage + 4`, the stage's briefing palette, into `DAT_0046c076` for the movie. The debrief greys `Rock & Roll` (`0x26`, disabled) before the same call hides it with the bar, and writes `stage + 9`.

**The map buttons** each call a method of the shell's map object, `DAT_0046f26c` — `+0xc`, `+0x10`, `+0x14` and `+0x18` for the four arrows, `+4` and `+8` for the last two — then its paint: once while `+0x65`, the count of auto-repeat ticks so far, is below 3, twice below 6, three times below 9 and four times from there. The map, its camera and what each method does are in [`mission-map.md`](mission-map.md).

### The mission report

`Debrief_WriteReport` (`0040f34c`), which `Game_ProcessMissionResults` calls just before `Career_Advance`, writes the figures of the twenty report texts `Mission_BuildScreen` puts in the map panel. Every label is right-aligned in `0x1a`; every figure is written in `0x29` through `ESMessage_SetString`, the first three left-aligned beside their labels and the rest right-aligned under the column heads.

| Row | Label | Figure |
|---|---|---|
| 1 | `0x145` `Mission Outcome:` | `0x146 + outcome`: `Failure` or `Success` |
| 2 | `0x148` `Salvage Recovered:` | `"%d %s"` of `results.dat`'s award over 1000 and `0x149` `Tons` |
| 3 | `0x14a` `Weapons Recovered:` | `"%d"` of the salvage pairs plus the weapon units the campaign granted |
| 4 | `0x14c` `Kills:` | the column heads `0x150` `Hercs:`, `0x151` `Bases:` and `0x152` `Flyers:` |
| 5 | `0x14e` `You:` | the player's mission kills in each column, pilot `+0x2d`, `+0x31` and `+0x2f` |
| 6 | `0x14f` `Squad:` | the same three summed over the player and each on-strength squad member at positions 1 up to the positions in play |
| 7 | `0x14d` `Losses:` | the pilots `Squad_ProgressAll` counted lost |

The salvage figure is the award alone: what the debrief's scrapping puts in the pool is not reported. The squad's sums are taken after `Squad_ProgressAll` has replaced its lost pilots, so a member lost on this mission counts the replacement's zeros. Only a campaign debrief the player survived writes the report, and only that one reaches the view.

### Rock & Roll

`Rock & Roll >` (`Mission_OnRockAndRoll`, `00445509`) runs four tests and puts the first that fails up as a refusal:

| Code | Test | Lines |
|---|---|---|
| 2 | the player has a bay — `00482a9e`, the player's pilot `+0x22`, is not `-1` | `0x140` `You have no herc assignment. Select`, `0x141` `a herc for this mission.` |
| 0 | its machine is built to 100 and flightworthy — `Herc_IsDeployable` (`00410a9d`) | `0x13c` `Your herc is not functional. Repair`, `0x13d` `your herc or select another herc.` |
| 1 | it is armed — `Herc_IsArmed` (`00410b11`) through `Herc_HasWeapon` (`004116ec`): one of its ten mounts holds a weapon below `0x1d`, so the four pods do not arm a machine | `0x13e` `Your herc is unarmed. Select some`, `0x13f` `weapons or another herc.` |
| 3 | every squad member on strength, in a position from 1 up to those in play, has an armed machine — `Squad_AllArmed` (`0040f6c6`) | `0x142` `One or more hercs of your squad is`, `0x143` `unarmed. Arm or reassign hercs.` |

With all four passed it writes the mission handoff (`Game_ExportMissionHandoff`, [`campaign-loop.md`](campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)), sets the exit code to 2 (`Shell_SetExitCode`, `0040876a`, which stores `0046e210`) and sets `Shell_QuitFlag`, which ends the shell's main loop; the launcher answers 2 by running the simulator ([`../command-line.md`](../command-line.md#exit-codes)). `INSTANT ACTION` ends the same way.

**The refusal** is an `ESAlert` built once by `LaunchRefusal_Build` (`0044cfdc`) in a window the size of the display, so its rect is a canvas rect. `LaunchRefusal_Show(code)` (`0044d27c`) writes the code's two lines, `estext.bin` `0x13c + 2 * code` and the next, and shows it; `OKAY`'s handler, `LaunchRefusal_OnOkay` (`0044d404`), hides it. Its window is built the way [the scrap dialog's](#the-scrap-dialog) is, so while it is up only `OKAY` takes a click.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| alert | `ESAlert` | `{0xb1, 0x67, 0x1d2, 0xc6}` | `0x13b` `WARNING!`, header 20 tall, border `0x15`, face `0x25`, plate `100`-`0xbd`, filled body |
| 2 lines | `Text` | `{5, 0x1e, 0x117, 0x2b}`, `{5, 0x2c, 0x117, 0x39}` | centred, `0x29`, no backing |
| `OKAY` | `Button` | `{0x5f, 0x45, 0xc3, 0x54}` | `0x144`, border `0x22` |

### The summary text box

`esreport.cpp`'s paginated text: a `0x36`-byte object, not a widget, that lays a string out as one `Text` child of its parent per line and shows a page of them at a time. `TextBox_Ctor` (`0040cc0c`) stores the parent, the rect and the font. `TextBox_SetText` (`0040cc81`) clears it — which also puts it on page 0 and marks it not shown — copies the string, wraps it with `TextBox_Wrap` (`0040d0d6`), and builds the line texts, all hidden: line `i` at row `i % perPage`, `{left, top + row * cell, right, top + row * cell + cell}` in the box's parent, left-aligned in `0x28`, opaque over `0x10`.

| Offset | Meaning |
|---|---|
| `+0x18` | shown |
| `+0x1e` | the page up |
| `+0x20` | the page count |
| `+0x22` | lines per page, the rect's height over the font's cell height |
| `+0x24` | the line count |

**The wrap breaks at spaces, measured.** `TextBox_Wrap` walks the string in place. A newline ends the line. Any other character below a space becomes a space, and is not tested as one on that pass. At each space the line so far is measured with `Font_MeasureString`, and when it is wider than the box the line is cut at the last break, which becomes the next line's start. The break then stays where it was rather than moving to this space; the next space that fits moves it. After the last character the final line is measured once more and cut the same way if it overflows. A line at least as wide as the box after a cut asserts.

**The page count is off by one at both ends.** `TextBox_SetText` computes it as `lines / perPage + (lines + 1 != perPage)`. A text exactly filling its pages gets a further, empty page, and a text one line short of filling its first page gets none, so neither page button moves it. Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

`TextBox_ShowPage` (`0040cee2`) shows the lines of the page up while the box is shown and hides every other. `TextBox_PageUp` (`0040d237`) and `TextBox_PageDown` (`0040d258`) move a page while the box is shown and one is there to move to, and show it.

The five boxes and what fills them:

| Box | Global | Text |
|---|---|---|
| 0 | `0048d81c` | the map view's, `Campaign_LoadStageText(stage - 1)` (`0040f775`): string `stage - 1` of the first group of `eng\campaign.str`, in the [`.STR` layout](../formats/str-strings.md) |
| 1 | `0048d820` | the briefing, `Career_BriefingText` (`004135c8`) |
| 2 | `0048d828` | the objectives, `Career_ObjectivesText` (`004135c2`) |
| 3 | `0048d82c` | the intelligence report, `Career_IntelligenceText` (`004135ce`) |
| 4 | `0048d824` | the debrief, `Career_DebriefText` (`004135d4`) |

The four career texts are the lines of `data\mission.str` that the career block's arrays name ([`../formats/save-games.md`](../formats/save-games.md#career-block--152-bytes)). The briefing, objectives and intelligence report are assembled from the loaded mission's file when it loads (`Career_BuildBriefingText`, `00412f97`); the debrief by `Career_BuildDebriefText` (`004133d2`) at the end of `Career_Advance`, which first rewrites the file and the debrief array from the mission just flown's `.msn` (`Msn_LoadDebrief` (`0041d2c3`), [`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#row-5--the-debrief)), before the next mission's load writes the file again. `Career_Advance` is the one caller of `Career_BuildDebriefText` that `es2_xref.py` finds, where it finds three of `Career_BuildBriefingText`'s, `Career_LoadSlot` among them: a restored save rebuilds the briefing texts and not the debrief.

`DAT_004780a4` is the box that is up. `Mission_ShowTextBox(n)` (`00444cb3`) marks the box in it not shown and box `n` shown, runs `TextBox_ShowPage` on both, and stores `n`; it leaves the page alone, so a box comes back on the page it was left on until the next entry refills it. `Mission_LightViewButton(n)` (`00444c49`) puts the first three buttons' borders back to `0x22` and writes `0x20` on `Mission Briefing` for 1 and 4, `Mission Objectives` for 2 and `Intelligence Report` for 3. `Mission Briefing` (`Mission_OnBriefing`, `004453c1`) runs both with the view, so in the debrief it brings back the debrief; `Mission Objectives` (`00445437`) and `Intelligence Report` (`004454a0`) run both with 2 and 3. The page buttons, `Mission_OnPageUp` (`004455e9`) and `Mission_OnPageDown` (`0044569a`), page the box that is up.

## The arming and repair hotspots

Both screens lay clickable rects over a picture of the selected machine. The geometry comes from `gam\arm_hots.dat` and `gam\rpr_hots.dat` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamarm_hotsdat-and-gamrpr_hotsdat--the-clickable-regions)), which carry position and nothing else: **an area's index within its chassis group is its identity**, because the builder passes `handlerTable[areaIndex]` as the panel's click handler. Each handler is a one-line thunk that calls a common function with its own index baked in.

`DAT_00482ae5` is the selected bay slot, 0-7 and `-1` for none, and both screens read the machine out of the eight-pointer array at `00482ac3`.

**Arming** — `Arming_SelectHardpoint(hardpoint)` (0043dbb2), ten thunks, `0043e15f`-`0043e4c8`. `Hotspots_BuildOverlay(2)` lays one chromeless `Panel` per mount below the capacity over each bay's picture, the chassis's `arm_hots.dat` area of that index, so a higher mount answers over a lower one. The handler returns immediately when the click is on the hardpoint already selected, then reads the mount pointer at `herc + 0x50 + hardpoint*4` — null for an empty slot, otherwise its first `int16` is the fitted weapon id — and repaints. What it goes on to do is [above](#fitting-a-weapon).

**Repair** — `Repair_SelectHotspot(column, row)`, twenty-five thunks in one table at `0048d1f8` (`004340b5`-`00434ac8`). Sixteen are column 0, the hotspots on the picture; nine are column 1, a list beside it. The pair is resolved into a category and an index within it:

| | `Repair_HotspotCategory` (00433410) category | `Repair_HotspotIndex` (00433431) index | Count | What it selects |
|---|---|---|---|---|
| column 0, rows 0-5 | 0 | `row` | 6 | the external component **groups** |
| column 0, rows 6-15 | 2 | `row - 6` | 10 | the per-hardpoint conditions |
| column 1, rows 0-8 | 1 | `row` | 9 | the internal components |

**That category is the status block's own accessor mode.** `HercStatus_Get(block, mode, index)` takes exactly these three: mode 0 averages an external group, mode 1 addresses the nine internals and mode 2 the ten hardpoints ([`../formats/save-games.md`](../formats/save-games.md#the-66-byte-status-block)). Three independent things agree — the counts, the mode semantics, and `rpr_hots.dat` carrying exactly six areas per chassis — so the six hotspots on the picture are the six named groups `Cockpit`, `Left Torso`, `Right Torso`, `Chassis`, `Left Leg`, `Right Leg`, in that order.

The ten weapon rows are not in `rpr_hots.dat`: the same builder loop places them from the per-chassis `rpr_*.dat` geometry, starting at handler index 6. A row is refused — the selection does not move and nothing repaints — when its slot is past the machine's mount capacity at `+0x4c`, or when the slot is empty. So an unfitted hardpoint cannot be selected on the repair screen at all.

Selection repaints the outgoing entry with `(0x10, 0x27)` and the incoming with `(0x29, 0x29)`; `0x29` is the highlight colour throughout the shell, and `0x10` the resting one.

### The condition readout

`Repair_RefreshDetail` refreshes the detail panel from the selection, writing four text fields: the selected component's repair cost, its condition, the whole machine's rebuild cost (`Repair_HercCost(herc, 100)`), and the salvage available — which is `CareerSalvage` **minus** `Armory_QueuedTotal()`, so the figure the repair screen quotes is already net of what the build queue has committed. The three figures suffix `estext.bin` `0xc8` `kg`; the condition is a word.

The condition itself goes through two functions over two in-image tables. `Repair_DamageLevel(condition)` (0043d9cf) walks `004765c0` = `{89, 79, 59, 29, 0}` and returns the first index whose entry the condition is **strictly greater** than, or 5 if it is greater than none:

| Level | Condition | `estext.bin` word | Colour (`004765ca`) |
|---|---|---|---|
| 0 | 90-100 | `0x68` `Nominal` | 14 |
| 1 | 80-89 | `0x69` `Light` | 13 |
| 2 | 60-79 | `0x6a` `Moderate` | 12 |
| 3 | 30-59 | `0x6b` `Heavy` | 11 |
| 4 | 1-29 | `0x6c` `% Complete` | 10 |
| 5 | 0 | `0x6d` `Unassigned` | 39 |

**The bands are the repair ladder's.** `Repair_LevelForCondition`'s own table is `{90, 80, 60, 30, 1, 0}` tested with `<=` where this one is `{89, 79, 59, 29, 0}` tested with `<`, which partitions identically ([`armory.md`](armory.md#repair-levels)) — the word the screen prints *is* the repair level the machine would be worked to.

**The last two rows are a retail bug.** The word run in `estext.bin` is only four long: `0x67` is the label `Condition:`, `0x68`-`0x6b` are the four words, and `0x6c`/`0x6d` are `% Complete` and `Unassigned`, which belong to the build screen and the crew screen. Nothing in the table is a fifth or sixth damage word — there is no `Critical` or `Destroyed` anywhere in the 342 entries. The index really is the level plus a fixed base, read off the instruction stream rather than the decompiler: `Repair_DamageLevel` returns its loop counter in `EAX` (`xor eax,eax` … `inc eax`, with the comparison kept in `CX`/`DX`), and the caller does `mov esi,eax` / `add si,0x68` before the lookup. Recorded in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

**Greying a button is three writes, not one.** `+0xb5` and `+0x4d` go to `0x26` when it is disabled and to `0x29`/`0x22` when it is not, and `+0x49` follows. That pairing is what makes `+0x49` the enable flag rather than a style bit: it moves with the colours, on a test of whether the player can act. `Repair_RefreshDetail` writes the trio at three of the repair screen's four buttons, and what each is tested on is [above](#what-the-buttons-are-gated-on).

## What the whole front end shares

`esglobal.cpp`'s init (`004073bc`, called with 1 on entering the shell) loads what every screen then draws with:

| Resource | Handle | Role |
|---|---|---|
| `dfn\font2.dfn` | `0046dcc4` | loaded twice, into this handle and the next |
| `dfn\font2.dfn` | `0046dcc8` | |
| `dfn\black.dfn` | `0046dccc` | every tab caption |
| `dbm\bay2a_84.dbm` | `0046dcd4` | the backdrop each screen's root is textured with |
| `bin\estext.bin` | `0046dcc0` | all UI text — 342 entries, see [`../formats/weapons-dat.md`](../formats/weapons-dat.md#the-bin-string-tables) |

`dfn\font.dfn` is in the archive and the init does not ask for it.

## Sound

The shell has a sound manager of its own: a copy of the simulator's [`SFX` manager](../formats/audio.md#the-sfx-manager) at `ShellSound_Manager` (`004731f0`), four samples out of `SHLSOUND.VOL`, and the wrappers below. The archive holds one folder, `hmi\`, and five files, all 8-bit mono PCM:

| File | Rate | Length | Is |
|---|---|---|---|
| `gm_69.wav` | 22050 | 0.06 s | the press sound |
| `bptlt2.wav` | 22050 | 0.22 s | the tab click |
| `lswitch2.wav` | 22050 | 3.8 s | the switch the startup sequence opens with |
| `shell1.wav` | 11025 | 91 s | a music track |
| `shell2.wav` | 11025 | 93 s | the other music track |

**The tracks alternate from one run to the next.** `ShellSound_Init` (`0042ec7c`), which the startup (`Shell_Main`, `00401525`) runs once its windows are built, loads the music as `shell1.wav` while `prefs.cfg` option 5 is non-zero and `shell2.wav` while it is 0, then flips option 5, commits and writes all 54 options back ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)). A music track that will not load puts up `Cannot load sound.` and the shell carries on. The music is set to loop forever at `ShellSound_MusicVolume` (`004731fc`), which is 0 in the image. Under `-s`, which clears `Shell_SoundEnabled` (`00482272`), the setup creates no manager and returns, so option 5 stays where it was and every wrapper does nothing.

| Wrapper | Does | Only while |
|---|---|---|
| `ShellSound_PlayPress` (`0042eecf`) | `gm_69.wav` at volume 100 | there is a manager, SOUNDS (option 1, `004824b9`) is on and `ShellSound_Running` (`00473200`) is set |
| `ShellSound_PlayTabClick` (`0042ee89`) | `bptlt2.wav` at volume 100 | the same |
| `ShellSound_PlaySwitch` (`0042ef15`) | `lswitch2.wav` at volume 100 | the same |
| `ShellSound_Start` (`0042ef5b`) | sets `ShellSound_Running`, plays the press sound at volume 0, and starts the music from its top at the music volume | `ShellSound_Running` is clear |
| `ShellSound_Stop` (`0042f030`) | sets the music to play once, stops every sound, gives up the driver's focus and clears `ShellSound_Running` | `ShellSound_Running` is set |
| `ShellSound_Shutdown` (`0042f14a`) | `ShellSound_Stop`, then destroys the manager | |
| `ShellSound_FadeIn` (`0042f21c`) | raises the music volume one step at a time until it is past 99 | MUSIC (option 0) is on |
| `ShellSound_FadeOut` (`0042f178`) | lowers it one step at a time until it is below 2 | MUSIC is on |

A fade takes a step whenever more than 10 ms of `GetTickCount` have passed since the last, which at that clock's 15.6 ms granularity is about a second and a half from silence to full. It is a loop that pumps window messages and returns only when it is done, so the shell does nothing else meanwhile. The volume reaches the driver as `volume * master * 0x7fff / 10000`, with the master `Sos_MasterVolume` (`00473160`) on the 100 it holds in the image — linear in the volume.

**MUSIC gates the fades, not the music.** The two fades are the writers of the music volume that `es2_xref.py` finds ([Open](#open)), and both return at once with MUSIC off; `ShellSound_Start` does not test it. So with MUSIC off from startup the music runs at volume 0 all the while. [The preferences screen](#what-a-checkbox-sets)'s `Music` checkbox fades in after turning MUSIC on and fades out before turning it off. Its `Cancel` (`00436b90`) runs the fade the reverted setting calls for, turning MUSIC on for the length of a fade out so the fade's own gate lets it run; its `Accept` runs none. A fade out stops at 1, so music turned off plays on at 1 of 100.

### What plays each sound

**The press sound goes with the class of the widget pressed** ([The widget that takes a click decides what it does](#the-widget-that-takes-a-click-decides-what-it-does)):

| Handler | Plays it on | Tests |
|---|---|---|
| `ESButtonFont_HandleEvent` (`00409b0f`), every content button | either button going down, before `WinButton_HandleEvent` | `+0x49`, `Shell_SoundEnabled`, and the event's `+0x25` being 0 |
| `ESButtonBitmap_HandleEvent` (`00409df2`), the strip and the mission screen's arrows | the left button going down | `+0x49`, `Shell_SoundEnabled`, `Avi_Playing`, `MovieQueue_Running` |
| `ESRadioButton_HandleEvent` (`0040a139`), the [checkbox](#the-preferences-screen) class `ESRadioButton_Ctor` (`0040a100`) builds (vtable `0046e9a0`) — the preferences screen's eleven, `PreferencesScreen_Build`'s only use of it | the left button going down | `+0x49`, `Shell_SoundEnabled` |

Every other class is silent: rows, panels, grids, image panels, edit fields. A content button therefore sounds on the press and fires on the release, and sounds for a press that the pointer then drags off it. A mouse event's `+0x25` is 0 on every event the mouse itself queues: `MouseEvent_Ctor` (`00468cfc`) clears it and the queue's drain (`WinMouseProducer_PostEvents`, `00408c4e`) does not write it. `Career_StartMissionLoad` sets it to 1 on the press and release it posts to the mission-name dialog's `Use Default` ([Starting a practice mission](#starting-a-practice-mission)), so that click makes no sound.

**A tab switch makes both sounds.** `ShellSound_PlayTabClick` is the last call of all eight tab handlers ([What a tab click does](#what-a-tab-click-does)), so a tab picked with the left button makes the press sound as it goes down and the click once its screen is up, and one picked with the right button, which goes through `WinButton_HandleEvent`, makes only the click. The square button makes the press sound and no click.

**The switch sound opens the startup sequence**: `004311b8`, the handler of the widget that plays it, calls `ShellSound_PlaySwitch` on its first run, once (`DAT_00473608`).

**The music starts after the startup movies.** `Shell_BuildScreensAndStart` builds every screen and then calls `ShellSound_Start`, and [the movie queue](#the-shells-movies) fades the music out and stops it before its movies and starts it and fades it in after them. On a plain startup the music therefore comes up after the two intro movies. With movies off (`DAT_00482275`) the queue does nothing and the music stays at the startup's volume 0. The startup's other arms, for `DAT_0048227e` 3, 4 and 6, skip the intro movies and call `ShellSound_FadeIn` themselves.

Elsewhere:

| Where | Does |
|---|---|
| `MainWndProc` (`00404a2c`), `WM_SETFOCUS` | `ShellSound_Start`, unless `Avi_Playing` — so the music comes back from its top |
| `MainWndProc`, `WM_KILLFOCUS` | `ShellSound_Stop` |
| `maybe_Mission_UpdateLocationTab` (`0044409f`), stage 5 | `ShellSound_FadeOut` and `ShellSound_Stop` after enqueuing the stage's movie |
| `ONLINE MANUAL` (`004317ea`) | `ShellSound_Stop` before opening the help file |
| `Shell_ShutdownDevicesAndSound` (`004092dc`), which the [main loop's exit](#quit) and the switch parser's `-v` and `-?` call, and the insert-CD failure in `Movie_PlayQueue` | `ShellSound_Shutdown` |

**There is one backdrop for the whole shell.** `es2_xref.py` finds one store to `0046dcd4`, this init's ([Open](#open)), and all eight screen builders pass that same handle as their root's image. So a screen that installs `arming.dpl` is drawing `bay2a_84` through a palette that is not its own. On the tab screens only the strip row ever shows it, and the backdrop's top 30 rows are black: everything below the strip is covered by [the palette scope's fill](#the-palette).

## The palette

**The palette is a widget, not a call.** `DAT_0048d444` is the palette scope from the widget tree above: a `Window` subclass built by `0040ca6c` (vtable `PTR_FUN_0046ef04`, allocation `0x47`) whose `+0x45` is a palette index rather than a lit flag. Its event handler `0040cab7` responds to event 2 by calling `Shell_InstallPalette(+0x45)` and committing the result. So the shell changes palette by writing `+0x45` between the two visibility calls of `00439da0(index)`, and the second of them is what fires the install.

**The scope is installed by being hidden**, which follows from [the visibility pair](#showing-and-hiding-a-widget): `00439da0` shows the scope, writes the index and hides it again, and it is the hide that posts event 2. The pair works repeatedly because each call leaves the bit where the next one needs it.

**The scope paints, and its paint is what makes the tab screens black.** Its handler sends event 4 to `ESPal_Paint` (`0040cb40`), which fills the whole scope rect, `{0, 0x1e, 0x27f, 0x1df}`, with `0x10`, and the show posts that event. Tabs 2-7 all install their palette through the scope (`0043b162` cases 2-7 call `00439da0`), and the repaint of the backdrop-textured root by name that a search of the decompile finds, `Movie_PlayQueue`'s, comes after `Mission_Leave` has taken the tab down ([The shell's movies](#the-shells-movies), [Open](#open)), so each of those tabs draws its screen over a black canvas and shows black wherever its widgets leave it bare. The main menu and the save screen go through the scope too, then put up their own backdrop-textured root over the fill, which is why they show the bay.

`Shell_InstallPalette(index)` (004075b2) reads a pointer table of `dpl\*.dpl` paths at `0046dcdc`, twenty entries long:

| # | Name | Used for |
|---|---|---|
| 0 | `intr_pt1` | the intro; nothing in the tab layer selects it |
| 1 | `palette` | the shell on entry, the service bay (`0043b23d`), `MAIN MENU`, `SAVE` and `REPAIR` |
| 2 | `arming` | `WEAPONS`, `BUILD`, `ARMORY` and `CREW` |
| 3 | `cam_er` | the campaign map, stages 1-4 |
| 4 | `cam_moon` | the campaign map, stage 5 |
| 5-9 | `br_w1`-`br_w5` | the briefing, indexed by stage |
| 10-14 | `db_w1`-`db_w5` | the debrief, indexed by stage |
| 15-19 | `alph`, `delt`, `omic`, `brav`, `luna` | the theater, indexed by stage |

`0043b162(tab)` is the switch that picks one. Tab 3 takes 1, tabs 2 and 4-6 take 2, and tab 7 takes `stage + 4` for the briefing, `stage + 9` for the debrief and 3 or 4 for the map — `4` once `stage - 1 > 3`. Case 8 is not a tab: it shows the frame's root, and is what `0043b0c8`'s callers pair with the refresh.

**The stage is the career's own, 1-5 for the campaign** — stage 0 holds the practice missions ([`campaign-loop.md`](campaign-loop.md#the-campaign-table--gamcareerdat)) — and the save holds the same number. Three tables are built for it: the briefing and debrief runs are five long and reached by `stage + 4` and `stage + 9`, the map's Earth-to-Moon switch fires at the same stage the theater run's `luna` sits at, and `maybe_Mission_UpdateLocationTab` carries four `dba\` location names — `alph2`, `delt1`, `omic1`, `brav1` — and branches away to a cutscene entirely when `stage - 1 == 4`. The arithmetic is unguarded in all three places, so a practice mission's briefing, at stage 0, is drawn through `cam_moon`. `Reference/Managment_Mission_Briefing.png`, taken on a save at stage 3, is drawn through `br_w3` ([`mission-map.md`](mission-map.md#the-camera)).

That last function also installs the theater palette directly, as `Shell_InstallPalette(stage + 0xe)`. Because stage 5 branches away before the call, `luna` is in the table and unreached by this path.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A button's three frame pointers are unlit, lit and disabled | `ESButtonBitmap_Ctor` really does take and store three, and a third face for a widget the strip refresh can gate is the natural guess. Neither paint reads `+0x59`: both branch on the lit flag between `+0x51` and `+0x55` only. A gated tab in retail looks exactly like an idle one |
| `SHELL0.VOL`'s `dba\` and `dfn\` are the 320-wide halves of a pair, as they are in the simulator archives, and shell art must be doubled to reach the canvas | There is no `hba\` or `hfn\` in `SHELL0.VOL`. The shell has one set and authors its screens at the size that set draws at — the `{0, 0, 0x27f, 0x1df}` panel is 640x480 and the button plates fill their 75x24 rects at 1:1 |
| A screen's widget rects come from its `gam\rpr_*.dat` or `gam\arm_*.dat` file, the way a cockpit widget's come from the herc's `.GAU` | Those files exist and do carry widget geometry, which makes the inference natural. They cover the per-chassis content panels only; the screen builders that place everything else write their rects as immediates and read no layout file |
| `warmingi.cpp` is a warning dialog | It is `w` + `arming` + `i`, the weapon-fitting screen, in the same naming pattern as `wsrvbayi.cpp`, `wcrewi.cpp` and `warmoryi.cpp` |
| The tab screens are drawn through `dpl\bay.dpl` | `SHELL0.VOL` carries one, and the bay screen's own name makes it the obvious candidate for the palette the bay installs. The table at `0046dcdc` does not contain it: index 1 is `dpl\palette.dpl`. `VSHELL.EXE` does not name `bay.dpl` at all: a case-blind byte search finds twenty `.dpl` strings, the table's twenty |
| Exactly one tab is latched at all times | Seven of the nine handlers latch their own plate and it is easy to assume the other two do too. `MAIN MENU` and `SAVE` clear all nine, write none back and [hide the strip](#tabs-0-and-1-hide-the-strip) |
| `0043b23d` shows the frame, as its Ghidra name `ServiceBay_Show` says | It calls `Window_HideRecursive` (0041f469) on the frame's root and panel, and the pair that undoes it — `0043b162(8)` and `0043b0c8` — calls `Window_ShowRecursive` on the same two. The name was given under the swapped reading of those two functions ([above](#showing-and-hiding-a-widget)) |
| `0041f2e6` shows a widget and `0041f469` hides it, matching their names in the raw Ghidra dump | The dump's own names support that reading — one sets a state bit and recurses into children, the other clears it, and the names line up with which is which. They are swapped: `+0x11` bit 2 is a *hidden* bit, so the setter is the hide. `known_symbols_vshell.json` carries the corrected assignment (`Window_ShowRecursive` at 0041f2e6, `Window_HideRecursive` at 0041f469); only the raw dump still has it backwards. Three witnesses agree; see [Showing and hiding a widget](#showing-and-hiding-a-widget) |
| The repair screen's detail figure and its `REPAIR ALL` figure are the same cost scaled | Both say `Salvage Required:` in kg and both come from the same unit-value tables, so a per-item share of the whole is the obvious reading. They use different functions with different targets: `Repair_HercCost` prices the machine to 100, and `Repair_LevelStepCost` (00413871) prices the selected component up to the floor of the next band only ([`armory.md`](armory.md#what-one-repair-level-costs)) |
| Every widget fires on the button's release, and only after a press on it | That is `WinButton_HandleEvent`'s rule, and it is the base class's handler, run by the panels, the grids and every content button, so it reads as the shell's. The tab strip, the image panels and the edit fields each put a handler of their own in vtable slot 0: the strip fires on the left press, an image panel on any left release, an edit field on the left press ([The widget that takes a click decides what it does](#the-widget-that-takes-a-click-decides-what-it-does)) |
| `00445758` is the mission tab's builder | It is the largest function after `wmissini.cpp`'s assert-string anchor, so the file attribution points at the mission tab. Every widget it builds is one that `Build_Enter` (`0044690d`), tab 4's entry, shows and the teardown's build arm hides, and its captions are the `Herc Construction` run `0xba`-`0xc5`. `known_symbols_vshell.json` names it `Build_BuildScreen`; the mission tab's builder is `Mission_BuildScreen` (`00442534`) |
| `00442534` builds the crew screen's detail panel | It sits between the crew screen's thunks and `wmissini.cpp`'s first assert string, inside the address range that reads as `wcrewi.cpp`'s, and it is that module's size. Every widget it builds is one `Mission_Show` (`004441e3`) shows and `Mission_Leave` (`00444a05`), the teardown's mission arm, hides, and its captions are the mission run `0xaf`-`0xb8`. It is [the mission screen's](#the-mission-screen) builder, `Mission_BuildScreen` |
| The palette scope draws nothing — it exists only to fire the palette install | It carries no bitmap, no caption and no chrome, and its handler's event 2 is the install. Its event 4 is a paint, `ESPal_Paint` (`0040cb40`), which fills its rect with `0x10`; that fill is why retail's tab screens are black ([The palette](#the-palette)) |
| The shell draws its mouse pointer from `dba\cursor.dba` | The bank sits in `SHELL0.VOL` with the shell's own art, and the Dynamix library has a `GLCursor` type to draw one with. `VSHELL.EXE` never names the bank; the pointer is the Windows arrow, with the hourglass while a save or a movie loads ([The pointer](#the-pointer)) |
| The save stores the campaign stage from zero and the shell counts it from one | Every per-stage table is reached one past the first entry a zero-based stage would need — `0x76 + stage` for the sector name lands on `Razor` at stage 0, and the briefing palettes start at `stage + 4` — which reads as a zero-based value shifted at runtime. The campaign's stages are 1-5 in `gam\career.dat` itself, stage 0 holding the practice missions, and the tables are indexed by the number the save holds: retail draws the briefing of a save at stage 3 through `br_w3` ([The palette](#the-palette)) |
| `START NEW GAME`'s `ACCEPT` is `0043bf1b` and its `SKILL LEVEL` `0043bd15` | Both handle a registration panel — one calls `Game_NewCareer` with a typed name, the other steps `RegistrationSkillChoice` — and they sit beside `Registration_Show`. They belong to `SaveRegistration_BuildPanel`'s (`0043b260`) [second panel](#the-second-registration-panel) in the save screen, whose widgets `Registration_Show` never touches; the screen it shows is `Registration_BuildScreen`'s, with `0043c01d` and `0043c0fb` ([The registration screen](#the-registration-screen)) |

## Open

- **Unported:** what [the movie queue](#the-shells-movies) does for a movie that will not open: the intro's `Please insert ESII CD and restart` and the insert-CD panel.
- **Open:** whether the `avivideo` device scales a movie to fill the window `Avi_Play` moves it to. The rects say it does: the full rect is 576x360, twice the 288x180 intro, and the map panel's is exactly the thumbnails' 295x226.
- **Open:** what the palette handle `Avi_Play` sets does to a movie's colours.
- **Open:** what else writes `Avi_PaletteHandle` (`00485664`). Besides `Avi_BuildPalette`'s store, an undisassembled routine at `0041dea8` builds a 236-entry palette (entry `i` red `10 + i`, flags `PC_NOCOLLAPSE`) and stores it there; `es2_xref.py` finds no reference to that routine, and no store clearing the handle.
- **Open:** no store clearing `MovieQueue_IntroSkipped` (`00470fe0`) found: `es2_xref.py` finds three stores, `00404b5e`, `00404bc3` and `0041e1be`, each of 1.
- **Open:** no writer of `Avi_Playing` (`00470d70`) or `MovieQueue_Running` (`00470e70`) found outside the two players: `es2_xref.py` finds `Avi_Play`'s two stores and `Movie_PlayQueue`'s four.
- **Open:** what the briefing's map panel shows while the briefing movie plays, before `ShellMap_RunIntro` has run.
- **Unported:** the pressed nudge of a content button's caption. `Button`'s paint (`00409b79`) moves the caption down while `+0x45` is lit and the button is enabled, as the strip's does.
- **Open:** what the build screen's `SCRAP` gate, `ScrapDialog_Show` and `Hangar_ScrapSelected` do with no bay selected, where each reads the third squad-member pointer at `00482abf` as [the bay's machine](#scrapping-and-building-are-gated-on-the-bay). A roster click selects one of the eight bays, but the crew tab can leave `-1` selected for the build tab to open on. The repair and weapons tabs reach `-1` when no bay holds a finished machine, or from the crew tab through [their entries' `Squad_SelectBay`](#entering-the-weapons-screen). There `Repair_RefreshDetail`'s gates and all three of [its handlers](#repairing-and-cancelling) read the same pointer, `CANCEL` copying a stale status block through it, and so do the weapons screen's [steppers](#fitting-a-weapon), its refusal test and its fit; `>` would divide by zero through a null pointer.
- **Open:** no other store to the build screen's chassis `DAT_004786e4` found: `es2_xref.py` finds `Build_SelectChassis`'s at `00446ce6` and twelve reads.
- **Open:** no reference to the mission screen's map scope `DAT_0048d818` found but `Mission_BuildScreen`'s store (`es2_xref.py`), so nothing found shows it.
- **Open:** no writer of `ShellSound_MusicVolume` (`004731fc`) found besides `ShellSound_FadeOut`'s `DEC` (`0042f1d7`) and `ShellSound_FadeIn`'s `INC` (`0042f251`): `es2_xref.py` finds those and five reads.
- **Open:** no store to the backdrop handle `0046dcd4` found but the init's (`0040754f`): `es2_xref.py` finds that, the init's own read and push, and the eight screen builders' pushes.
- **Open:** whether anything repaints the frame's root while a tab screen is up. A search of the decompile finds one repaint of `ShellRootWidget` by name, `Movie_PlayQueue`'s after the location picture; the same function also repaints, and hides and shows again, the top-level window `DAT_004810e4`, the root's parent.
- **Open:** what retail draws for a machine under construction whose body bank lacks the construction frames ([The bay picture](#the-bay-picture)). `Squad_BuildBayPictures` (`00414e5b`) indexes past them unchecked.
- **Open:** whether a command key with Shift, Ctrl or Alt down posts a command. `Keyboard_PostEvents` (`00408f95`) indexes the table at `0046e471` with the whole key code, so it reads a byte of the data section past the table's 256. None of those bytes in the image is 1, 4 or `0x0a`, so none edits a row ([Typing into a row](#typing-into-a-row)), but any that is not `0xff` posts a command, and with it runs the row's handler.
- **Unported:** the auto-repeat of the mission screen's arrows.
- **Open:** what reads the words the preferences screen's four group setters store, `00474cc4`, `00474cc6`, `00474cc8` and `00474cca` ([What a checkbox sets](#what-a-checkbox-sets)).
- **Open:** what reaches cases 2 and 3 of `FUN_00436841`, which cycle PILOT MESSAGE (option 2) — case 2 from 0 to 2 and from 1 or 2 to 0, case 3 from 0 to 1, 1 to 2 and 2 to 1. `es2_xref.py` finds two callers, `00436cc1` and `00436d22`, which pass 0 and 1, and the builder makes no widget for the others.
- **Open:** what shows [the second registration panel](#the-second-registration-panel). A search of the disassembly for `DAT_0048d418` and `DAT_0048d470` as absolute operands finds their builders and three hides — `SaveScreen_Enter`, the teardown and `SaveRegistration_ShowDetailPanel` (`0043b679`) — and no show; the dead rect `{9, 0xcf, 0x6b, 0xde}` that `SaveScreen_BuildScreen` writes just before `SAVE`'s may be where a button that showed it stood.
- **Unported:** the startup's `Performance Note` box ([The main menu](#the-main-menu)).
- **Open:** what v1.0 ships in `prefs.cfg` option 47, which decides whether the `Performance Note` check runs on a first start. The GoldGames build's file has it 0; `ES2/DATA/PREFS.CFG` (1) has been written by play.
- **Unported:** Alt+F4's `QUIT` alert ([QUIT](#quit)).
- **Open:** no other writer of the registration screen's name or `RegistrationSkillChoice` found: `es2_xref.py` finds the name field's pointer (`0048d4a8`) stored only by its builder and otherwise read, and `004761ac` stored only by the two `SKILL LEVEL` handlers (`0043bd3a`, `0043c042`) ([The registration screen](#the-registration-screen)).
- **Open:** no simulator read of options 37-41 found ([The parameters](#the-parameters)): `es2_xref.py` finds no reference to `SimOptions + 0x25` to `+0x29` (`004d1fe1`-`004d1fe5`) in DBSIM, where option 6's `004d1fc2` has one, and DBSIM has no indexed option getter.
- **Open:** no reference to `InstantAction_Active` (`0047363c`) found besides `INSTANT ACTION`'s store of 1 (`004312b6`) and `MsnGen_BuildPlayerHerc`'s read (`0041c625`), by `es2_xref.py` ([Selecting a mission](#selecting-a-mission)).
- **Open:** no store clearing a save row's `+0xbf` after a rename found: `es2_fieldscan.py bf` finds `ESDialog_Ctor`'s 1, `SaveScreen_BuildScreen`'s 0 (`00438936`) and `SaveScreen_BeginRename`'s 1 ([Typing into a row](#typing-into-a-row)).
- **Open:** no write greying [the second registration panel](#the-second-registration-panel)'s `ACCEPT` found: `es2_xref.py` finds no reference to its pointer `0048d490` but the builder's store.
- **Unported:** the developer's mission-name dialog, `Career_StartMissionLoad`'s way to the load, and its `Msn_BuildPath` button, which loads a typed name.
- **Open:** the class of [the pointer state](#which-widget-a-click-reaches) at `DAT_005ddbd0`. `Widget_HitTestTree` (`00469d1c`) and the `Pointer_*` functions around it (`Pointer_SetTarget`, `Pointer_Unlock`, `Pointer_Leave`, `Pointer_Enter`, `Pointer_MoveTo`) carry invented prefixes; their RTTI class is the vtable that object's constructor installs last.
