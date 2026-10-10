# Shell screen layout

How VSHELL puts a screen together: the canvas it authors in, where the layout numbers live, the widget tree behind every tab screen, the tab strip over it, the shared resources the whole front end draws with, and the palette. The economy behind the screens is in [`armory.md`](armory.md); the campaign state they read and write is in [`campaign-loop.md`](campaign-loop.md).

The rest of the front end:

- [`widgets.md`](widgets.md) — the widget classes: when a widget is up, which widget a click or a key reaches and what it does with it, the pointer, and how a widget paints.
- [`movies-and-sound.md`](movies-and-sound.md) — the movie queue, input around the movies, and the shell's sounds and music.
- [`main-menu.md`](main-menu.md) — the main menu and the screens that stand alone with the strip hidden: registration, practice missions, preferences and the save screen.
- [`squad-and-crew.md`](squad-and-crew.md) — the squad panel four tabs share, and the crew screen.
- [`weapons-and-repair.md`](weapons-and-repair.md) — the weapons and repair screens and the hotspots over their pictures.
- [`build-and-armory.md`](build-and-armory.md) — the build screen, the scrap dialogs and the armory screen.
- [`mission-screen.md`](mission-screen.md) — the mission tab's three views, the mission report and the launch.

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
3. **The palette scope** at `{0, 0x1e, 0x27f, 0x1df}` — the canvas below the strip. It is parented to the shell's top-level window (`Shell_TopWindow`, `004810e4`, the root's own parent) rather than to the panel, and it is how the screen's palette is chosen; see [The palette](#the-palette).
4. **The strip itself**: one square button and eight tabs.

Widget fields the builders and the tab handlers write directly:

`WinButton_Ctor` (`00409788`) is the base every one of them goes through. It zeroes `+0x45`, sets `+0x49` to 1 and ORs `0x60` into the flags word at `+0x39`.

| Offset | Meaning |
|---|---|
| `+0x45` | the lit flag. The widget's own mouse handler toggles it 0/1, and the paint picks the button's face from it. A tab handler writes 1 and repaints before building its screen, which is what latches the active tab lit; `TabStrip_ClearAllLit` (`00439dcb`) clears it across all nine. On the palette scope, a different class, the same offset is a palette index instead |
| `+0x49` | 1 from the constructor, and the enable flag. The tab gate clears it on the three tabs training mode has no economy for, and the repair panel writes it alongside two greying colour fields on a test of whether the player can afford the button ([The condition readout](weapons-and-repair.md#the-condition-readout)) — moving with the greying, on an affordability test, is what makes it the enable flag rather than a style bit. The button's own paint reads it for one thing, whether the caption takes the pressed nudge; the base class's event handler ignores mouse events while it is clear, so a cleared widget swallows a click on it ([Which widget a click reaches](widgets.md#which-widget-a-click-reaches)) |
| `+0x51` | 1 from `ESRect_Ctor`, and the gate on drawing any chrome at all: `ESRect_FillAndBorder` (0040a726) returns immediately when it is clear. Written 0 on both the root and the full-screen panel, which is how each shows its bitmap with no fill and no border. Not the button field of the same offset — different class, different layout past the base |

## What a tab click does

Every tab has its own handler, and the eight are the same function with three or four lines changed. Each opens by returning if `Shell_MainLoopStarted` (`0046c08c`) is clear, as it is until the main loop's first pass has played its movie queue ([`startup.md`](startup.md#the-main-loop)), and then by returning if `DAT_0047581c` — which tab is up — already holds its own index: **clicking the tab you are already on is a no-op**, before the teardown, the palette and the sound alike. What follows is, in order:

1. `TabStrip_ClearAllLit` (`00439dcb`) — clear the lit flag on all nine strip buttons and repaint them.
2. Write `+0x45` back to 1 on this tab and repaint it. **Only tabs 2 to 7 do this**; the main menu's and the save screen's handlers skip it and hide the strip instead ([below](#tabs-0-and-1-hide-the-strip)).
3. `00439ea7` — tear down whatever tab is currently up, dispatching on `DAT_0047581c`.
4. `0043b162(tab)` — install the tab's palette.
5. `0043cfe7(tab)` — build the shared squad roster panel, on tabs 2, 3, 4 and 6 only. Neither ARMORY nor MISSION has one.
6. The screen's own builder.
7. `DAT_0047581c = tab`, then `ShellSound_PlayTabClick` (`0042ee89`) — [the tab click](movies-and-sound.md#sound). Tab 6's handler stores its index before its builder instead, so the crew screen's entry already runs as tab 6 ([Entering the crew screen](squad-and-crew.md#entering-the-crew-screen)).

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
| 7 `MISSION` | `0043a6ca` → `0043a857` | `004441e3` ([The mission screen](mission-screen.md#the-mission-screen)) | |

### Tabs 0 and 1 hide the strip

Tabs 0 and 1 take a different route to their palette: instead of `0043b162` they call `00439da0(1)` directly and then `0043b23d`, which **hides** the frame's root (`0048d440`) and the full-screen panel (`0048d448`) and installs palette 1 itself. Every strip button is that panel's child, so the strip goes with it: the main menu and the save screen each stand alone over their own backdrop-textured root, and are left only through their own buttons. The save screen's way back is [its EXIT and RESTORE](main-menu.md#leaving-the-save-screen), which undo exactly this.

**The shell starts with the strip hidden too.** `ServiceBay_BuildScreen` hides the full-screen panel as it builds it, and the frame's root is an image panel, which is [born hidden](widgets.md#showing-and-hiding-a-widget), so nothing of the frame shows until something runs `0043b162(8)` and the strip refresh: RESTORE, EXIT back to the strip, `CONTINUE GAME` or a campaign mission's load.

Returning to the main menu **autosaves**: tab 0's handler calls `Game_SaveSlot(10, NULL)` after the teardown and the strip's hide, before building the menu, and slot 10 is the campaign-or-training current-game slot ([`../formats/save-games.md`](../formats/save-games.md)).

## The tab gate

`0043b0c8` is the strip refresh, and it writes `+0x49` on five tabs from `DAT_0048260c`, the campaign/training mode flag — training being the mode the [practice missions](main-menu.md#the-practice-missions-screen) and `INSTANT ACTION` run in:

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

The caption is an embedded `Text` child at `+0x4d`, sized to the button's full client rect, drawn in the font at `DAT_0046dccc` and fetched with `WeaponsBin_LookupName(DAT_0046dcc0, index)` from `estext.bin`. The paint places it one pixel above its vertically-centred row when the button is idle and one below when it is lit, so a lit caption sits two pixels lower than an idle one — the pressed nudge. The centring itself is a baseline, `(Font_CellHeight(text +0xb1) + rectHeight + 1) / 2`: `+0xb1` is the `Text` widget's font handle and `Font_CellHeight` (00453fa8) returns that font's glyph cell height ([Text placement and colour](widgets.md#text-placement-and-colour)).

The strip is rebuilt by each tab screen's own builder at these same coordinates rather than shared between them.

## What the whole front end shares

`EsGlobal_Init(1)` (`004073bc`, `esglobal.cpp`), which [the startup](startup.md#the-startup--shell_main-00401525) runs once, loads what every screen then draws with:

| Resource | Handle | Role |
|---|---|---|
| `dfn\font2.dfn` | `ShellFont2HandleA` (`0046dcc4`) | loaded twice, into this handle and the next |
| `dfn\font2.dfn` | `ShellFont2HandleB` (`0046dcc8`) | |
| `dfn\black.dfn` | `0046dccc` | every tab caption |
| `dbm\bay2a_84.dbm` | `0046dcd4` | the backdrop each screen's root is textured with |
| `bin\estext.bin` | `0046dcc0` | all UI text — 342 entries, see [`../formats/weapons-dat.md`](../formats/weapons-dat.md#the-bin-string-tables) |

`dfn\font.dfn` is in the archive and the init does not ask for it.

**There is one backdrop for the whole shell.** `es2_xref.py` finds one store to `0046dcd4`, this init's ([Open](#open)), and all eight screen builders pass that same handle as their root's image. So a screen that installs `arming.dpl` is drawing `bay2a_84` through a palette that is not its own. On the tab screens only the strip row ever shows it, and the backdrop's top 30 rows are black: everything below the strip is covered by [the palette scope's fill](#the-palette).

## The palette

**The palette is a widget, not a call.** `DAT_0048d444` is the palette scope from the widget tree above: a `Window` subclass built by `0040ca6c` (vtable `PTR_FUN_0046ef04`, allocation `0x47`) whose `+0x45` is a palette index rather than a lit flag. Its event handler `0040cab7` responds to event 2 by calling `Shell_InstallPalette(+0x45)` and committing the result. So the shell changes palette by writing `+0x45` between the two visibility calls of `00439da0(index)`, and the second of them is what fires the install.

**The scope is installed by being hidden**, which follows from [the visibility pair](widgets.md#showing-and-hiding-a-widget): `00439da0` shows the scope, writes the index and hides it again, and it is the hide that posts event 2. The pair works repeatedly because each call leaves the bit where the next one needs it.

**The scope paints, and its paint is what makes the tab screens black.** Its handler sends event 4 to `ESPal_Paint` (`0040cb40`), which fills the whole scope rect, `{0, 0x1e, 0x27f, 0x1df}`, with `0x10`, and the show posts that event. Tabs 2-7 all install their palette through the scope (`0043b162` cases 2-7 call `00439da0`), and the repaint of the backdrop-textured root by name that a search of the decompile finds, `Movie_PlayQueue`'s, comes after `Mission_Leave` has taken the tab down ([The shell's movies](movies-and-sound.md#the-shells-movies), [Open](#open)), so each of those tabs draws its screen over a black canvas and shows black wherever its widgets leave it bare. The main menu and the save screen go through the scope too, then put up their own backdrop-textured root over the fill, which is why they show the bay.

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
| The tab screens are drawn through `dpl\bay.dpl` | `SHELL0.VOL` carries one, and the bay screen's own name makes it the obvious candidate for the palette the bay installs. The table at `0046dcdc` does not contain it: index 1 is `dpl\palette.dpl`. `VSHELL.EXE` does not name `bay.dpl` at all: a case-blind byte search finds twenty `.dpl` strings, the table's twenty |
| Exactly one tab is latched at all times | Seven of the nine handlers latch their own plate and it is easy to assume the other two do too. `MAIN MENU` and `SAVE` clear all nine, write none back and [hide the strip](#tabs-0-and-1-hide-the-strip) |
| `0043b23d` shows the frame, as its Ghidra name `ServiceBay_Show` says | It calls `Window_HideRecursive` (0041f469) on the frame's root and panel, and the pair that undoes it — `0043b162(8)` and `0043b0c8` — calls `Window_ShowRecursive` on the same two. The name was given under the swapped reading of those two functions ([Showing and hiding a widget](widgets.md#showing-and-hiding-a-widget)) |
| The palette scope draws nothing — it exists only to fire the palette install | It carries no bitmap, no caption and no chrome, and its handler's event 2 is the install. Its event 4 is a paint, `ESPal_Paint` (`0040cb40`), which fills its rect with `0x10`; that fill is why retail's tab screens are black ([The palette](#the-palette)) |
| The save stores the campaign stage from zero and the shell counts it from one | Every per-stage table is reached one past the first entry a zero-based stage would need — `0x76 + stage` for the sector name lands on `Razor` at stage 0, and the briefing palettes start at `stage + 4` — which reads as a zero-based value shifted at runtime. The campaign's stages are 1-5 in `gam\career.dat` itself, stage 0 holding the practice missions, and the tables are indexed by the number the save holds: retail draws the briefing of a save at stage 3 through `br_w3` ([The palette](#the-palette)) |

## Open

- **Deferred:** no store to the backdrop handle `0046dcd4` found but the init's (`0040754f`): `es2_xref.py` finds that, the init's own read and push, and the eight screen builders' pushes.
- **Deferred:** whether anything repaints the frame's root while a tab screen is up. A search of the decompile finds one repaint of `ShellRootWidget` by name, `Movie_PlayQueue`'s after the location picture; the same function also repaints, and hides and shows again, the top-level window `Shell_TopWindow`, the root's parent.
