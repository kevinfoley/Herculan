# The mission screen

Tab 7, `MISSION`: the campaign map, the briefing and the debrief on one set of widgets, the mission report, and the launch. The map itself is in [`mission-map.md`](mission-map.md).

## The mission screen

Tab 7, `MISSION`. Built once by `Mission_BuildScreen` (`00442534`), put up by `Mission_Show(view)` (`004441e3`) and taken down by `Mission_Leave` (`00444a05`), the teardown dispatcher's mission arm. One set of widgets serves the tab's three views, which the show routine moves, retitles and shows or hides. Rects are parent-relative; the four panels, the location picture and the button bar are parented to the shell's top-level window.

The view is `DAT_0048106c`: 0 the campaign map, 1 the briefing, 4 the debrief. `Mission_ShowView(view)` (`0043a857`) stores it. The tab handler asks for the map while both the map's first-show flag `DAT_004778aa` and the mission-within-stage counter `DAT_0046fb1a` are zero, and for the briefing otherwise. The flag is clear in the image and the map view sets it on its first show; [`RESTORE`](main-menu.md#leaving-the-save-screen) and a new career's [`ACCEPT`](main-menu.md#starting-a-campaign) clear it again, and a `CONTINUE GAME` load does not. So on a stage's first mission the map comes up on the tab's first click after the shell starts, a restore or a new career, and every later click opens the briefing. The debrief is never the tab's choice: [the campaign layer](campaign-loop.md#where-the-debrief-goes-next) writes 4 while processing a finished mission, and the load of the next mission that follows puts the tab up in it.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| Telecomm | `TitledPanel` | `{7, 0x2b, 0x113, 0x12b}` | `0xb0` `Telecomm`, header 19 tall, `+0x65 = 0` |
| Telecomm picture | image panel | `{10, 0x14, 0xf9, 0x100}` in Telecomm | `dba\terradef.dba` frame 0, no border; handler `Mission_OnTelecommPicture` (`00444e28`) |
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

The map panel's top is `0x2b` while `Display_FullScreen`, the shell's full-screen flag, is clear, and `0x2a` while it is set; `Reference/Managment_Mission_Briefing.png` shows it level with Telecomm, so that capture was taken windowed. The builder writes the map panel's face and plate over `ESTitle_Ctor`'s `0x24` and zeros and leaves its hatch on; Telecomm and the summary keep the constructor's face and clear `+0x65`. All three keep the filled body, so nothing of the backdrop shows.

`ESTitle_Ctor` clears `+0x49` and the builder clears the button bar's, so a click on a panel, the bar or anything they hold with no handler of its own is swallowed. The Telecomm picture has a handler, and it returns at once ([Input while a movie plays](movies-and-sound.md#input-while-a-movie-plays)).

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

**What comes back with the map panel is decided at construction.** The builder hides the map panel straight after building it, so every child is built under a hidden parent, and a constructor's own show then sets bits 2 and 4: the child returns whenever the panel is shown ([Showing and hiding a widget](widgets.md#showing-and-hiding-a-widget)). A later hide by name clears bit 4 again, so the child returns only when shown by name:

- **The map grid** is hidden by name as soon as it is built, and `Mission_Show` shows it by name in the map view alone. It never covers the briefing's map.
- **The map scope** is built by `ESWindow_Ctor` alone, so it is born hidden with bit 4 clear and does not come back with the panel; only a show by name would put it up, and `es2_xref.py` finds no reference to `DAT_0048d818` but the builder's store ([Open](#open)). While it stays hidden its `0x10` fill does not cover the map grid.
- **The report texts** return with the panel until `Mission_HideReportTexts` first runs. The map and briefing views run it before the panel is shown, and `Mission_Leave` runs it on the way out, so the debrief shows them only when it is the screen's first view since the shell started.

The map view's flag `DAT_004778aa` is set whether movies are on or off; with them off `Movie_Enqueue` adds nothing and the view stays up. With them on, the second map movie carries the location flag, so the tab comes down after it and the location picture goes up, or the lunar drop plays at stage 5 ([The shell's movies](movies-and-sound.md#the-shells-movies)).

The debrief fills box 4 through `Mission_DebriefText` (`00444bbb`) and takes its movie through `Mission_DebriefMovie` (`00445751`), thunks to the two career accessors.

The briefing also lights `Objectives`, `Intelligence` and `Rock & Roll` — caption `0x29`, border `0x22`, enabled — then lights `Mission Briefing` through `Mission_LightViewButton(1)` and puts text box 1 up through `Mission_ShowTextBox(1)`, and writes `stage + 4`, the stage's briefing palette, into `DAT_0046c076` for the movie. The debrief greys `Rock & Roll` (`0x26`, disabled) before the same call hides it with the bar, and writes `stage + 9`.

**Holding a map button down moves the map faster the longer it is held.** Each button calls a method of the shell's map object, `DAT_0046f26c` — `+0xc`, `+0x10`, `+0x14` and `+0x18` for the four arrows, `+4` and `+8` for the last two — then its paint and `Display_PresentStoredRect`, once while its own `+0x65` is below 3, twice below 6, three times below 9 and four times from there. `+0x65` is the button's count of [auto-repeat](widgets.md#the-widget-that-takes-a-click-decides-what-it-does) ticks: a held button fires on each tick of its 500 ms alarm, one step for the first two ticks, two for the next three, and so on, and the release fires once more at a count of 0. The page buttons repeat the same way, a page a tick. The map, its camera and what each method does are in [`mission-map.md`](mission-map.md).

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

**The refusal** is an `ESAlert` built once by `LaunchRefusal_Build` (`0044cfdc`) in a window the size of the display, so its rect is a canvas rect. `LaunchRefusal_Show(code)` (`0044d27c`) writes the code's two lines, `estext.bin` `0x13c + 2 * code` and the next, and shows it; `OKAY`'s handler, `LaunchRefusal_OnOkay` (`0044d404`), hides it. Its window is built the way [the scrap dialog's](build-and-armory.md#the-scrap-dialog) is, so while it is up only `OKAY` takes a click.

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

**The wrap breaks at spaces, measured.** `TextBox_Wrap` walks the string in place. A newline ends the line. Any other character below a space becomes a space, and is not tested as one on that pass; the test reads the character as a signed byte, so every byte above 127 becomes a space too (v1.10 keeps those, [`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)). At each space the line so far is measured with `Font_MeasureString`, and when it is wider than the box the line is cut at the last break, which becomes the next line's start. The break then stays where it was rather than moving to this space; the next space that fits moves it. After the last character the final line is measured once more and cut the same way if it overflows. A line at least as wide as the box after a cut asserts.

**The page count is off by one at both ends.** `TextBox_SetText` computes it as `lines / perPage + (lines + 1 != perPage)`. A text exactly filling its pages gets a further, empty page, and a text one line short of filling its first page gets none, so neither page button moves it. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

`TextBox_ShowPage` (`0040cee2`) shows the lines of the page up while the box is shown and hides every other. `TextBox_PageUp` (`0040d237`) and `TextBox_PageDown` (`0040d258`) move a page while the box is shown and one is there to move to, and show it.

The five boxes and what fills them:

| Box | Global | Text |
|---|---|---|
| 0 | `0048d81c` | the map view's, `Campaign_LoadStageText(stage - 1)` (`0040f775`): string `stage - 1` of the first group of `eng\campaign.str`, in the [`.STR` layout](../formats/str-strings.md). v1.10 takes the folder from the shell's language ([`../retail-builds.md`](../retail-builds.md#how-a-language-is-chosen)) |
| 1 | `0048d820` | the briefing, `Career_BriefingText` (`004135c8`) |
| 2 | `0048d828` | the objectives, `Career_ObjectivesText` (`004135c2`) |
| 3 | `0048d82c` | the intelligence report, `Career_IntelligenceText` (`004135ce`) |
| 4 | `0048d824` | the debrief, `Career_DebriefText` (`004135d4`) |

The four career texts are the lines of `data\mission.str` that the career block's arrays name ([`../formats/save-games.md`](../formats/save-games.md#career-block--152-bytes)). The briefing, objectives and intelligence report are assembled from the loaded mission's file when it loads (`Career_BuildBriefingText`, `00412f97`); the debrief by `Career_BuildDebriefText` (`004133d2`) at the end of `Career_Advance`, which first rewrites the file and the debrief array from the mission just flown's `.msn` (`Msn_LoadDebrief` (`0041d2c3`), [`../formats/msn-mission-file.md`](../formats/msn-mission-file.md#row-5--the-debrief)), before the next mission's load writes the file again. `Career_Advance` is the one caller of `Career_BuildDebriefText` that `es2_xref.py` finds, where it finds three of `Career_BuildBriefingText`'s, `Career_LoadSlot` among them: a restored save rebuilds the briefing texts and not the debrief.

`DAT_004780a4` is the box that is up. `Mission_ShowTextBox(n)` (`00444cb3`) marks the box in it not shown and box `n` shown, runs `TextBox_ShowPage` on both, and stores `n`; it leaves the page alone, so a box comes back on the page it was left on until the next entry refills it. `Mission_LightViewButton(n)` (`00444c49`) puts the first three buttons' borders back to `0x22` and writes `0x20` on `Mission Briefing` for 1 and 4, `Mission Objectives` for 2 and `Intelligence Report` for 3. `Mission Briefing` (`Mission_OnBriefing`, `004453c1`) runs both with the view, so in the debrief it brings back the debrief; `Mission Objectives` (`00445437`) and `Intelligence Report` (`004454a0`) run both with 2 and 3. The page buttons, `Mission_OnPageUp` (`004455e9`) and `Mission_OnPageDown` (`0044569a`), page the box that is up.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `00442534` builds the crew screen's detail panel | It sits between the crew screen's thunks and `wmissini.cpp`'s first assert string, inside the address range that reads as `wcrewi.cpp`'s, and it is that module's size. Every widget it builds is one `Mission_Show` (`004441e3`) shows and `Mission_Leave` (`00444a05`), the teardown's mission arm, hides, and its captions are the mission run `0xaf`-`0xb8`. It is [the mission screen's](#the-mission-screen) builder, `Mission_BuildScreen` |

## Open

- **Open:** what the briefing's map panel shows while the briefing movie plays, before `ShellMap_RunIntro` has run.
- **Deferred:** no reference to the mission screen's map scope `DAT_0048d818` found but `Mission_BuildScreen`'s store (`es2_xref.py`), so nothing found shows it.
