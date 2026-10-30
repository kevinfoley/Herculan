# GO.EXE — the message box and its text

**A single mission starts and ends with a talking head. Before the war map, INSTANT ACTION > and SINGLE MISSION's BRIEFING > show a face over the screen's picture that speaks the mission's opening line, its text in a framed strip across the top; back from the mission, before the debriefing, another face speaks a line on how it went. The face's mouth moves with the voice, and a key, a click or the end of the line takes the player on.** The same text box shows the shell's other short texts: the mission briefing on the war map in a box near the bottom, the debriefing in a box at the upper left. The text is cut into lines at spaces to fit the box's width and the lines into pages, and the box shows one page at a time. A message's text is one line of an `eng\*_text.bin` file ([the dialog text files](../formats/bin-string-tables.md#the-dialog-text-files)); a briefing's or a debriefing's is a block of the mission's `.ENG` file ([reading a block](../formats/eng-mission-text.md#reading-a-block)). With the RADIO preference at `VOICE ONLY` a message's text is not drawn at all.

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

**`dialog.cpp`** runs from `Mission_WriteObjectives` (`00018824`), whose assert names `src\dialog.cpp` (`00018a4f`), to `0001a29b`. The file before, `TextTable_Ctor` (`0001865c`) to `Text_LoadDataFile`, ends with a `RET 8` at `00018820` and one zero byte. Its last code is its static initializer, `Dialog_StaticInit` (`00019c2a`), and the exit routine `Dialog_StaticExit` (`0001a296`), whose `RET` at `0001a29a` is followed by one zero byte and `HercStatus_Ctor` (`0001a29c`). It holds no `ConfigPart` run. Its own copies of two library functions: `Rect_CopyCtorDialogCopy` (`00019050`), byte for byte `Rect_CopyCtor` (`0004b398`), and `Point_DefaultCtorDialogCopy` (`00019a02`), byte for byte `Point_DefaultCtor` (`0004b37d`).

**The message box's file** runs from `000370ec`, after a `RET 4` at `000370e6` and three zero bytes, to `000386c7`: `MessageBox_Answer`'s `RET 8` at `000386c4` is followed by one zero byte and `Math_RandomSeed` (`000386c8`). No assert names it, it holds no `ConfigPart` run, and the boundary scan finds no static constructor or exit table entry in it. It opens with the list boxes' line builders from `000370ec`, among them `WordList_CountNonNegative` (`000373d3`), `WaText_LinesFromList` (`000373fa`) and `Menu_ListLines` (`0003750d`) ([the build](menus.md#opening-a-menu--menu_open-0003969e)). Then, before the message box's functions:

- `Shell_FreeBitmapArraySlot` (`00037924`)(array): when one of the dwords `000813d4`, `000813d8` and `000813dc`, tested in that order, holds the array, destroys it with `GLBitmapArray_Dtor` (`0005343c`) and flags 3 and clears that dword. `es2_xref.py` finds calls from `0001ec76`, a control's destructor, with the control's `+0x3e`, and from `000405ae`.
- `Menu_OpenControlTtms` (`00037996`)(id) and `Menu_CloseControlTtms` (`00037aac`)(handles): the scripts of a kind-17 control ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)). For id 1803 the first opens `w1800w00.ttm` to `w1800w13.ttm` into the bytes `000a056c` and `w1800r00.ttm` to `w1800r03.ttm` into `000a057a`, each with `Ttm_Open` on the render context `0007fcdc` and run once with `Ttm_RunScript`, and then hides each of the 18 that opened with `Ttm_SetMode`(2, handle); for any other id it sets the 18 bytes to `0xff`. It returns `000a056c`. The second closes each of the 18 handles that is not `0xff` with `Ttm_Close`; `es2_xref.py` finds its one call in `0001f164`, a control's destructor, with the control's `+0x3e`.
- `Dir_ListFilesByName` (`00037add`)(name): the names `findfirst` (`0005dc58`) and `findnext` (`0005dc70`) find for `<name>\*.<name>` (`000814cd`), each copied into 20 bytes from `Mem_New`, in a zero-ended array of pointers, or 0 when there is none ([Open](#open)).
- `MessageBox_PickT1Line` (`00037bf0`) and `Shell_PlayTtmInPlace` (`00037dce`), which serve the career's kinds ([the kinds](#the-kinds), [the answer](#the-answer)).

## The message box

**One message is up at a time. While it is up the frame loop stands still: the screen's window takes no input, the face talks and the text is drawn each frame, until a key, a click or the end of the spoken line; then the box closes and the shell opens the screen that follows that kind of message.** ES2's counterpart is [Open](#open).

### The record

`Shell_MessageBox` (`0009b010`) holds the kind, an `int16` at `+0`, and two bytes: `+2`, *to show*, and `+3`, *up*. `MessageBox_SetKind` (`00037eac`)(box, kind) stores the kind and, for −1, clears both bytes; for any other kind it sets both to 1. The static initializer `00020b68` sets kind −1.

- *To show* makes `DigitalDialog_ShowMessage` load the kind's line ([showing a message](#showing-a-message)) and `MessageBox_Run` run the loop ([the loop](#the-loop)).
- *Up* holds the loop. While it is set `Shell_DrawScreen` does not draw the window ([drawing a screen](menus.md#drawing-a-screen), step 4), `Shell_TickScreen` does not update it, and `ShellMouse_OnLeftPress` and `ShellMouse_OnLeftRelease` test it. `maybe_SncList_Stop` clears it when the face's line ends ([playing it](#playing-it)).

`MessageBox_Close` (`000382d5`)(box) sets the kind to −1 with `MessageBox_SetKind` and deletes the text box with `Dialog_DeleteBox` (`00018d93`)(`Shell_Dialog`), whose destructor stops the face ([the box](#the-box)).

### The kinds

**Each kind names a text file, a line in it, the talking head that speaks it and the screen that follows.** The text file holds one line per message ([the dialog text files](../formats/bin-string-tables.md#the-dialog-text-files)); the lip-sync list, picked by the words *a* and *b* ([loading a line](#loading-a-line)), names the face's script and each line's lip-sync script and voice ([the lip-sync lists](../formats/shell-file-lists.md#the-lip-sync-lists)). The screen is the kind's entry of `MessageBox_NextScreen` (`00081690`), 12 `int16`.

| Kind | Put up by | File | Line | *a*, *b*: list | Next screen |
|---|---|---|---|---|---|
| 0 | screen 1499's case in `Shell_Mode` 0 ([the screens](menus.md#the-screens)): INSTANT ACTION > ([its path](main-menu.md#instant-actions-path-to-the-mission)) and SINGLE MISSION's BRIEFING > ([BRIEFING >](single-mission.md#the-single-mission-menu)) | `eng\C0_text.bin` | the mission's header word 4, the word `000ab222` | 0, 1: `C0` | 1501, [the war map](war-map.md) |
| 1 | screen 1499's case in mode 1 | `eng\C<k>_text.bin` | as kind 0 | 2*k*, 1: `C<k>` | 1500, a career's war map |
| 2 | screen 1490's case, and `NewCareerAlert_OnMessage` (`0002aeec`), screen 1210's handler | `eng\G0_text.bin` | the word of the table `000814ec` at the career's campaign × `0x16` + 2 × the word `000814e8` | 0, 3: `G0` | 1700, after the last line ([the answer](#the-answer)) |
| 3 | `Shell_OpenFirstScreen` back from a mission in mode 0, the pilot not lost ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)) | `eng\D0_text.bin` | 2*g* + *r*, below | 1, 1: `D0` | 1510, [the debriefing](debriefing.md) |
| 4 | screen 1520's case, the first time in a session | `eng\D<k>_text.bin` | as kind 3 | 2*k* + 1, 1: `D<k>` | 1520 |
| 5 | `AnyDisabledAlert_OnMessage` | `eng\T1_text.bin` | `0008159c`[`000814e8`] when `000814e8` is not 0, otherwise `MessageBox_PickT1Line`(kind) | the word `000815a8`[line], 2: `T0` or `T1` | 1490 |
| 6 | `UnArmedAlert_OnMessage` in mode 1 | as kind 5 | as kind 5 | as kind 5 | 9999 |
| 7 | `Shell_OpenFirstScreen` back from a mission in mode 1, the campaign going on | as kind 5 | as kind 5, but with `000814e8` 0 the line set before | as kind 5 | 1520 ([the answer](#the-answer)) |
| 8–11 | [Open](#open) | as kind 5 | as kind 5 | as kind 5 | 1520 |

*k* is the `+0` word of a campaign of the shell's battle set (`000a02cc`, [battle sets](../formats/bst-battle-sets.md#battle-sets)), the campaign the word at `0009b241` + `0x1f7` × `0009b21f` indexes, or −1 when that index is out of range.

**Kind 0's line** is the mission's header word 4, which its header patches set ([header patches](../formats/msn-mission-file.md#header-patches)). The 14 single missions hold, by their patches, all of which set it unconditioned, 0 (`SM_RCN01` to `SM_RCN03`, which set none), 1 (`SM_ESC50`, `SM_PTL01`, `SM_PTL02`, `SM_SCT01`, `SM_SCT02`, `SM_SCT50`), 2 (`SM_RAD01`, `SM_RAD02`), 3 (`SM_SCR01`), 4 (`SM_SAR01`) or 5 (`SM_ASS01`) there (`HercWorks.Query patches`): line *n* of `C0_text.bin`, of its 9, and line *n*'s voice of `C0_FILES.DAT`.

**Kind 3's line** is 2*g* + *r*: *g* is `Mission_OutcomeGrade` (`00036837`)(the outcome `000aad58`, the mission kind `000aad5e`), 0 to 3 by [its tables](../formats/bst-battle-sets.md#equipment-availability), and *r* a draw `Math_RandomBelow`(`Shell_RandomState`, 2), taken as 0 when *g* is 1. The draw is made for every grade. `D0_FILES.DAT` gives lines 6 and 7 the same voice ([the lip-sync lists](../formats/shell-file-lists.md#the-lip-sync-lists)).

**Kinds 5 to 11** also run `Ttm_SetMode` twice before the load. `MessageBox_PickT1Line` (`00037bf0`)(kind) sets the word `0008159c` and returns it: for kind 5 line 2; for 6 line 3 plus a draw below 4, or below 6 when the current career record's word `+0xa` is not 0; for 7 a line by bands of the value `00024b6a` gives for the record's word `+0x1c` entry of the table `000808a0`, 0 with none: 96 and above 10 to 13, 71 to 95 15 to 18, 51 to 70 20 to 22, 1 to 50 24 to 27, each a draw, and 0 or less leaves the line as it was. Kind 7 may also set the word `0008159e`. Any other kind returns the line as it was ([Open](#open)).

### Showing a message

`DigitalDialog_ShowMessage` (`00037edd`)(box) returns at once while *to show* is clear. Otherwise it makes the full-screen render context `0007fcd8` (`RenderContext_GetFullScreen`, `0004c716`) the scripts' default with `Ttm_SetDefaultContext`, builds the kind's path with `sprintf`, sets the line word `MessageBox_Line` (`000aad5c`), and calls `Dialog_LoadFromFile` (`00018c36`)(`Shell_Dialog` (`0009afc8`), *a*, *b*, path) ([the load](../formats/bin-string-tables.md#the-load)). That deletes the old box, whose destructor stops a line still playing, and builds a `DigitalDialogBox` on the line ([the box](#the-box)) in the strip or box the backdrop gives it ([where the box stands](#where-the-box-stands)); its constructor loads the talking head ([loading a line](#loading-a-line)), whose script draws through the player's own render context `+0x15`, not the scripts' default. A kind outside 0 to 11 returns before the load, the default already changed. *a* goes to the load as the low word of a dword and *b* as the low byte of one.

It never clears *to show*. `Shell_DrawScreen` calls it at its step 3 each time it draws a screen ([drawing a screen](menus.md#drawing-a-screen)), so a message is loaded when its screen is drawn: for kind 0 by screen 1499's draw, before the `.ANM` that draw plays at its step 5; for kind 3 by the first draw `Shell_Main` makes after `Shell_OpenFirstScreen` ([the start-up](startup.md#the-start-up)). `MessageBox_Answer` calls it for the next line of kinds 2 and 7. `MsnGen_LoadMission` also copies header word 4 into `MessageBox_Line` ([loading a mission](../formats/msn-mission-file.md#loading-a-mission), step 6).

### The loop

**The box holds the shell in a loop of its own until the player answers or the line ends.** Each pass of the frame loop calls `MessageBox_Run` (`000382f6`)(box, `Shell_InputBlock`) at its step 5 ([the frame loop](startup.md#the-frame-loop)); it returns at once while *to show* is clear. Otherwise it hides the cursor (the raster's slot 31, `0008662c`, [the cursor](../rendering/screen-pages.md#the-cursor)) and calls `NoOp_Void` (`00012bde`), and while the input block's key word `+0` and first button byte `+0xa` are 0 and *up* is set it runs:

1. `Shell_PresentFrame`, which waits for the next frame tick, 60 a second ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame));
2. `maybe_SncList_Tick`(`Shell_LipSyncPlayer`), which sets the face's page to the line's current frame ([playing it](#playing-it));
3. `Ttm_RunAll`, which runs every open script once, the face's among them ([running a script](../formats/ttm-scripts.md#running-a-script));
4. `Dialog_Draw`(`Shell_Dialog`): the frame or the fill and the page's lines, and the voice the first time ([drawing a page](#drawing-a-page));
5. `Input_PollDeviceBlock` (`000277d8`), the input for the next test, which replaces the argument; `Shell_InputBlock` itself is not written.

Then it calls `NoOp_Void` and `MessageBox_Answer`(box, input). The loop's first test is of the block the frame loop read at its step 2, in the pass whose step 4 opened the box's screen. A key that pressed the button leaves nothing in it: the shell's keyboard takes keys through `INT 15h` and lets the BIOS have only those no subscriber claims ([the devices](startup.md#the-devices), step 8), the key word's only source besides the key queue ([the command word](../simulation/input.md#the-command-word)), and `Tab`, `Enter` and `Enter`'s release are claimed by `Shell_NavKeySubscriber`, a button's own key, press and release, by `DUIWindow_KeySubscriber` ([the keys](menu-input.md#the-keys)), none of them being among the key queue's codes. A click's message is posted at the release, so the block of that pass reads the button up. So a message opened by INSTANT ACTION > or BRIEFING >, by key or by click, runs its loop. The loop turns no page of the text: only a message's first page is shown.

### The answer

`MessageBox_Answer` (`0003837f`)(box, input), by the kind:

- **0, 1, 3, 4, and any kind not named below**: posts the next screen. It sets the code word of `Shell_PendingMessage` (`0007fcb0`) to 0 and its target `0007fcb6` to the kind's entry of `MessageBox_NextScreen`, closes the box with `MessageBox_Close`, and shows the cursor (the raster's slot 30, `00086628`). The index and value words keep the −1 and 0 that `Shell_HandleMessage` leaves in them each pass ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8), step 1), so the message is (0, −1, 0, screen), and the next pass's step 4 opens the screen: the war map, 1501, after kind 0, and the debriefing, 1510, after kind 3.
- **5, 6, 8 to 11**: first `Ttm_SetMode`(0, handle) for the handle bytes at `0007e718` + `0x12` and + 9, then as above.
- **2**: adds 1 to `000814e8`, marks the text box dirty (`Dialog_MarkDirty`, [the dialog record](#the-dialog-record)) and closes the box. With no key or button in the input and the campaign's next line in `000814ec` not −1, it presents a frame and shows that line as kind 2 again (`MessageBox_SetKind`, `DigitalDialog_ShowMessage`). Otherwise it sets `000814e8` to 0, presents a frame, fills the draw page, the visible page and page 2 through the raster's slot `0008663c`, plays a script by the career's campaign word with `Shell_PlayTtmInPlace`, `a_camni.ttm` for 0, `d_camni.ttm` for 1 and 2, `b_camni.ttm` for 3 and 4 and `g_camni.ttm` for 5 to 7, and posts entry 2, 1700.
- **7**: adds 1 to `000814e8`, marks the text box dirty and closes the box. While `0008159c`[`000814e8`] is not −1 it shows that line as kind 7 again. At −1, in `Shell_Mode` 0, it stores 1510 at `MessageBox_NextScreen` indexed by the box's kind word (`000383e0`–`000383e4`), which `MessageBox_Close` (`000383b6`) has just set to −1, so the store lands on entry −1, `0008168e`, and entry 7 keeps its 1520. It then sets the lines before it to −1 and `000814e8` to 0, posts entry 7 (`0008169e`, read as a constant), and runs `Ttm_SetMode`(0, handle) for the two handles above. Every other path reads the kind for `MessageBox_NextScreen` before it closes the box.

Only the first path shows the cursor; the next screen's draw shows it in any case ([drawing a screen](menus.md#drawing-a-screen), step 6). `Shell_PlayTtmInPlace` (`00037dce`)(name) opens the script with `Ttm_Open`(0, 0, 0, name), so on the default render context, and runs it with `Shell_PresentFrame`, `Ttm_RunScript` and `Input_PollDeviceBlock` until its mode (`Ttm_GetMode`) is not 0 or a key or the first button comes in. When the mode is still 0, the script cut short, it fills the draw page and page 2 (`0007fccc`) through `0008663c`, presents a frame and fills the new draw page. Then it closes the script and clears the palette pointer `0007fcd0`. `Shell_PlayTtm` ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47)) clears the pages before it starts and sets the default render context; this one does neither.

## The talking head

**A message's face is a `.TTM` script whose pages are the head's mouth positions, drawn over the screen's picture; the line's lip-sync script says which page to show and for how long, and the line ends when that script runs out.**

### The player

The shell's lip-sync player is `Shell_LipSyncPlayer` (`0009afd4`), `0x1b` bytes, which holds one face and one line:

| Offset | Contents |
|---|---|
| `+0x00` | `int16` timer slot, from `Timer_AddCallback` |
| `+0x02` | byte, the face's `.TTM` script handle, or −1 |
| `+0x03` | the line's sound object |
| `+0x07` | the line's lip-sync records |
| `+0x0b` | `int16` record count |
| `+0x0d` | `int16` index of the current record |
| `+0x0f` | `int16` line count of the list |
| `+0x11` | the lines' name lists, 6 bytes each |
| `+0x15` | the render context the face's script draws through |
| `+0x19` | byte, set while a line plays; the byte `0009afed` |
| `+0x1a` | byte, a request to stop; the byte `0009afee` |

The static initializer `00020b68` sets the handle `+2` (`0009afd6`) to `0xff` and zeroes `+3`, `+7`, `+0xb`, `+0xd`, `+0xf`, `+0x11`, `+0x15`, `+0x19` and `+0x1a`; it also zeroes `Shell_Dialog`'s `+0`, `+4` and `+6` (`0009afc8`, `0009afcc`, `0009afce`) and sets the message box's kind to −1.

`maybe_SncList_Init` (`00045337`)(player), which `Shell_Main` calls after `Shell_InitDevices` and `Shell_LoadData` ([the start-up](startup.md#the-start-up)), makes the `+0x15` context with `GLViewport_Ctor` (`000512a0`)(0) and sets its dwords `+0x210`, `+0x214`, `+0x218` and `+0x21c` to 0, 0, 319 and 199, `+0x208` to 1 and `+0x20c` to 0: the whole screen. It then registers `maybe_SncList_TimerTick` (`00045320`) with `Timer_AddCallback` (`00056509`) at count 2 and keeps the slot. **The line's clock** is the word `ShellSnc_Clock` (`00082658`): the tick adds 1 to it while `+0x19` is set. `Timer_AddCallback` registers on the PC timer `Shell_InitDevices` has built by then at 120.05 interrupts a second, so a count of 2 is every second interrupt and the clock counts 60.02 a second, as `Shell_FrameTick` does ([the devices](startup.md#the-devices)).

`+0x19` is set by `maybe_SncList_StartSound` and cleared by `maybe_SncList_Stop`; `+0x1a` is set by `ShellSnc_Advance` when the line's records run out and cleared by `maybe_SncList_Stop`. `es2_xref.py --decode` finds one access to each byte by its own address, the tick's read of `0009afed` and `ShellSnc_Advance`'s write of `0009afee`; the others go through the player's address.

### Loading a line

**When a message's box is built, the shell loads the face for its kind and the lip-sync script of its line.** `DigitalDialogBox_Ctor` ([the load](../formats/bin-string-tables.md#the-load)) calls `maybe_SncList_PlayEntry` (`000453be`)(`Shell_LipSyncPlayer`, *a*, *b*), which:

1. stops a line still playing, `+0x19` set, with `maybe_SncList_Stop` (`00045556`) ([playing it](#playing-it));
2. picks one of the eleven names at `0008271c`, `SNC\C0_FILES.DAT`, `D0`, `C1`, `D1`, `C2`, `D2`, `C3`, `D3`, `T0`, `T1` and `G0` (`_FILES.DAT`): name *a* when *b* is 1, *a* + 8 when *b* is 2, name 10 when *b* is 3, and name 0 for any other *b*;
3. loads that list with `maybe_SncList_Load` (`000455dc`)(player, name);
4. passes the first name of line `MessageBox_Line`'s list, `StrList_Get` (`00020254`)(list, 0), to `ShellSnc_Load` (`0004589c`)(player + 3, name).

`maybe_SncList_Load` opens the list ([the lip-sync lists](../formats/shell-file-lists.md#the-lip-sync-lists)) through `VolRStream_CtorFromPath` and reads one byte into the low byte of the line count `+0xf`. When it is not 0 it deletes the name lists `+0x11` (`StrList_Dtor`) and makes that many 6-byte ones (`00045729`), reads the face's script name with `Stream_ReadCString` (`00020154`) into a 32-byte buffer, closes the handle `+2` with `Ttm_Close` (`000469da`) when it is not −1 and opens the name with `Ttm_Open` (`0004680a`)(0, 0, `+0x15`, name) into `+2` ([opening and closing](../formats/ttm-scripts.md#opening-and-closing)), sets its page to 0, `0x19` and 1 in turn with `Ttm_SetPage` (`000468fc`), running it with `Ttm_RunScript` (`000465da`) after each page the script accepts, sets page 1 once more, and reads each line's names with `StrList_Read`. It closes and destroys the stream. The count's high byte keeps what it held, 0 from the static initializer. A count of 0 changes nothing else: the old name lists and the face's script stay, and `maybe_SncList_PlayEntry` then takes `MessageBox_Line`'s entry of the old lists.

`ShellSnc_Load` opens `SNC\<name>`, reads the `int16` length, makes `length / 4` records for `+0x0b`, reads `length` bytes into them and closes the stream ([the shell's scripts](../formats/snc-lip-sync.md#the-shells-scripts)). It then adds each record's delta to the one after it, as `int16` sums, from the second record to the last, so each holds the clock reading at which it ends, and sets the index to 0. It appends no terminator.

`maybe_SncList_StartSound` (`0004544a`), which `DialogBox_Draw` calls at each draw ([drawing a page](#drawing-a-page)), starts the line once: when the handle is not −1 and neither `+0x19` nor `+0x1a` is set, it sets `+0x19`, opens the line's second name, its `.RAW`, with `ShellSnc_OpenSound` (`00045801`), starts it through the sound's slot `+0x10`, `DigiSound_Play` ([`sound-samples.md`](../formats/sound-samples.md#playing)), when the word `Shell_RadioOption` (`0007fcbc`) is above 0, and sets the clock to 0. `ShellSnc_OpenSound` does nothing while `Shell_RadioOption` is 0, `TEXT ONLY`; otherwise it builds `snc\<name>` and, when the sound manager `00082768` exists, calls the slot `+0x30` of the manager's object slot 0 (`SoundManager_GetObject`, `00042b81`) until it returns 0 and names the file as the manager's name index 0 with `SoundManager_SetSampleName` (`00042f39`), which takes a `.RAW` as a digital sound. When that succeeds it stores `SoundManager_CreateObject` (`000429db`)(manager, 2, 0, 0), a sound on that index, and loads it with `SoundObject_LoadSample` (`00043d8a`); when it fails it stores 0 ([the shell's names](../formats/sound-samples.md#the-shells-names)). So with RADIO at `TEXT ONLY` the face still talks, with no voice, and the line still ends when its lip-sync script does.

### Playing it

**The face shows each lip-sync frame for its delta, in sixtieths of a second, and the line ends when the lip-sync script runs out, stopping the voice if it still plays.** `maybe_SncList_Tick` (`000454c4`)(player), each pass of [the loop](#the-loop):

1. stops the line with `maybe_SncList_Stop` when `+0x1a` is set;
2. returns while `+0x19` is clear;
3. advances the line with `ShellSnc_Advance` (`0004597b`)(player + 3): nothing with no records or while the clock is below the current record's total; otherwise it steps the index past every record whose total the clock has reached, and on reaching the count sets `+0x1a`, by its address `0009afee`. It compares the clock as a signed `int16` with the total read as an unsigned `uint16`, and tests the current record before its loop's count test, so a records pointer with a count of 0 reads past the records;
4. sets the face's page with `Ttm_SetPage` to the current record's frame, the last record's once the index reaches the count, or −1 with no records, and sets the face's mode to 0, playing, with `Ttm_SetMode`(0, handle) ([choosing a page](../formats/ttm-scripts.md#choosing-a-page)).

A record therefore shows for its own delta, as in ES2's scripts, and the last frame stays on until the stop. No page of a face sets a wait, so each run of its script draws the current page and steps to the next ([a run](../formats/ttm-scripts.md#a-run)): from the load until the line starts, each `Ttm_RunAll` draws the next page from page 1, and from the line's start `maybe_SncList_Tick` sets the page back to the frame before each run.

**The stop.** In the pass after `ShellSnc_Advance` sets `+0x1a`, `maybe_SncList_Tick` calls `maybe_SncList_Stop` (`00045556`)(player): when the handle is not −1 it sets the face's page to `0x1e`, or to 1 when the script has no such page, and runs it once with `Ttm_RunScript`, closes the script with `Ttm_Close` and sets the handle to −1; it releases the line with `ShellSnc_Release`; and it clears `+0x19`, `+0x1a` and the message box's *up*, `0009b013`, which ends [the loop](#the-loop). A line therefore lasts its records' deltas in sum, and one pass more.

`ShellSnc_Release` (`00045a04`), from `maybe_SncList_Stop` and `maybe_SncList_Dtor` (`00045758`), frees the records and zeroes the count, then stops and destroys the sound through its slots `+0x14` and `+0x08`(3), `DigiSound_Stop` and `DigiSound_Dtor`, and clears it. `maybe_SncList_Dtor`, which `es2_xref.py` finds called from `00020fff`, also deletes the lines' name lists, passes the timer slot to `Timer_RemoveCallback` (`000565b7`), destroys the `+0x15` context through its slot `+0x1c`(3), and frees the player when its flags' bit 0 is set.

### What the face shows

**Kind 0's face is a head of 64 × 69 pixels at (203, 37) over shoulders drawn below it at (158, 89); kind 3's is a head of 48 × 46 at (209, 59) with no shoulders of its own.** A lip-sync frame *f* is page *f* of the face's script, and each of those pages draws one bitmap of the script's array with `0xa504` ([the commands](../formats/ttm-scripts.md#the-commands)): `Bitmap_Blit` at a point, flags 0, a dirty box at delay 1, so the picture behind comes back where the next frame does not draw. `SHELL0.VOL`'s two scripts, read through HercWorks.Core:

| Page | `C0_SYNC.TTM`, list `C0` | `D0_SYNC.TTM`, list `D0` |
|---|---|---|
| 0 | selects bitmap slot 0 and loads `C0_SYNC.BMP` into it, read as `RES32\C0_SYNC.DBA`: 27 bitmaps | the same with `D0_SYNC.DBA`: 27 bitmaps |
| 1 to 22 | bitmap *page* − 1, 64 × 69, at (203, 37) | bitmap *page* − 1, 48 × 46, at (209, 59); page 23 too |
| 23, 24 | bitmaps 22 and 23, 16 × 7, at (222, 77) | 24: bitmap 23, 16 × 7, at (234, 92) |
| 25 | bitmap 24, 152 × 111, at (158, 89), and `0x4204` fixes (144, 89)–(312, 200) into the background | nothing |
| 26, 27 | bitmaps 25 and 26, 32 × 7 and 32 × 6, at (212, 65) and (212, 66) | bitmaps 25 and 26, 16 × 4, at (227, 76) |

The load runs pages 0, 25 and 1, so `C0`'s shoulders are drawn once and fixed into the background ([fixing a rect](../rendering/screen-pages.md#fixing-a-rect)) and stay for as long as the screen's picture does; the head is drawn on every run. The lip-sync scripts of `C0_FILES.DAT`'s nine lines use frames 1 to 22, and those of `D0_FILES.DAT`'s eight lines frames 1 to 23 but 16, so a line shows the head bitmaps alone; no frame of theirs selects page 0 or a page above 23. Each has 16 to 57 records, 113 to 360 counts in sum, 1.9 to 6.0 s, and none a delta of 0. The stop shows page 1, the first head. What the bitmaps of pages 23 to 27 are for is [Open](#open).

## The text box

### The dialog record

The shell keeps one text box at a time, in the dialog record `Shell_Dialog` (`0009afc8`): `+0` the stream it was loaded from, `+4` the file's version word, `+6` the box. Two loaders fill it, `Dialog_LoadFromFile` (`00018c36`) for a message ([the load](../formats/bin-string-tables.md#the-load)) and `Dialog_LoadConditional` (`00018cdb`) for an `.ENG` block ([reading a block](../formats/eng-mission-text.md#reading-a-block)); each first deletes the box it held with `Dialog_DeleteBox` (`00018d93`), which calls the box's destructor (slot `+4` of its vtable) with 3 and sets `+6` to 0. The version word picks the class: 0 a `DefaultDialogBox`, 2 a `DigitalDialogBox` (a message), 1 a `ConditionalDialogBox` (an `.ENG` block).

The record's other functions do nothing without a box:

- `Dialog_Draw` (`00018dcb`)(dialog) draws it with `DialogBox_Draw`(box, the version word) ([drawing a page](#drawing-a-page)). `es2_xref.py` finds it called from `Dialog_PrevPage`, `Dialog_NextPage`, `MessageBox_Run` (`000382f6`), `Shell_DrawScreen` (`0003c1b7`) and `Shell_Main`.
- `Dialog_PrevPage` (`00018df0`) and `Dialog_NextPage` (`00018e18`) step the page ([stepping pages](#stepping-pages)) and draw it with `Dialog_Draw`.
- `Dialog_MarkDirty` (`00018d76`)(dialog) passes the box to `DialogBox_MarkDirty` ([drawing a page](#drawing-a-page)); `es2_xref.py` finds it called from `MessageBox_Answer` (`0003837f`).

### The box

A `DialogBox` is `0x2d` bytes; its subclasses differ in their constructors and vtables: `DialogBox` (`0007ee88`), `DefaultDialogBox` (`0007eea8`), `DigitalDialogBox` (`0007ef04`), `ConditionalDialogBox` (`0007ef48`), each the class name the image stores before its vtable.

| Offset | Field |
|---|---|
| `+0x00` | a byte, 1 while the box stands in the framed strip at the top and its frame is still to be drawn ([drawing a page](#drawing-a-page)) |
| `+0x01` | the font: `000aacda`, the `nm_green` font ([the loads](../formats/styles-dat.md#the-loads)), for every class |
| `+0x05`–`+0x11` | the box: left, top, right, bottom, `int32` |
| `+0x15` | the text |
| `+0x19` | `int16`, the count of line slots |
| `+0x1b` | the line slots, one pointer each, zeroed |
| `+0x1f` | one 8-byte point per line of a page |
| `+0x23` | `int16`, the count of pages |
| `+0x25` | `int16`, the lines a page holds |
| `+0x27` | `int16`, the page shown |
| `+0x29` | the vtable |

`DialogBox_Init` (`00019b25`), which each constructor calls first, sets `+0` to 1 and zeroes the font, the text, the slots, the points and the four words; it leaves the box's rect as it was. `DefaultDialogBox_Ctor` (`00019149`) takes the top strip's rect and the text `This is a Default Dialog Box.`; `DigitalDialogBox_Ctor` (`00019279`) and `ConditionalDialogBox_Ctor` (`00019453`) take theirs from their files and their rect from their `SetRect` ([where the box stands](#where-the-box-stands)). Each sets the font and lays the text out with `DialogBox_Layout`. `DialogBox_SetFont` (`00018e40`)(box, *n*) would set the font to the *n*th of the shell's fonts, the dwords from `Shell_Fonts` (`000aacc6`) ([Open](#open)).

**Destroying a box.** Each class's destructor stores its vtable and calls `DialogBox_Dtor` (`00019b54`)(box, 0): `DefaultDialogBox_Dtor` (`00019244`), `ConditionalDialogBox_Dtor` (`00019871`), and `DigitalDialogBox_Dtor` (`000193a8`), which first stops the message's lip-sync with `maybe_SncList_Stop` (`00045556`)(`0009afd4`). `DialogBox_Dtor` stores `DialogBox`'s vtable, frees the text, the slots and the points, runs `DialogBox_Init` again, destroys the frame bitmap `DialogBox_FrameBitmaps` (`0007ee64`) when it is loaded and clears it, adds the top strip's frame rect `DialogBox_TopFrameRect` (`0009ae74`) as a dirty box at delay 0, and deletes the box when bit 0 of the flags is set. It picks that rect by `+0`, which `DialogBox_Init` has just set to 1, so it never adds the box's own rect.

### Where the box stands

**Five rects place the box: a strip across the top of the screen, inside a frame, and one box each for the war map, the debriefing and backdrop 7000.** `Dialog_StaticInit` (`00019c2a`), the static constructor table's entry `0009a24c`, builds each from two points, the smaller *x* and *y* first:

| Rect | Address | (*x0*, *y0*)–(*x1*, *y1*) |
|---|---|---|
| `DialogBox_TopRect`, the top strip's text | `0009ae64` | (8, 7)–(284, 29) |
| `DialogBox_TopFrameRect`, the top strip's frame | `0009ae74` | (3, 4)–(287, 32) |
| `DialogBox_Backdrop6000Rect` | `0009ae84` | (69, 150)–(267, 184) |
| `DialogBox_Backdrop5000Rect` | `0009ae94` | (17, 31)–(143, 85) |
| `DialogBox_Backdrop7000Rect` | `0009aea4` | (108, 160)–(299, 195) |

Its exit routine, `Dialog_StaticExit` (`0001a296`, exit table entry `0009a290`), returns at once.

`ConditionalDialogBox_SetRect` (`00018e5f`) and `DigitalDialogBox_SetRect` (`000193ee`) round the word `Shell_BackdropId` (`000817c4`) down to a thousand, the current backdrop group ([the backdrop](menus.md#the-backdrop)), and look it up in four ints, `0007ee44` and `0007ef10`, with a jump table beside them. Each copies a rect to `+5` and sets `+0`:

| Group | `ConditionalDialogBox` (`.ENG`) | `DigitalDialogBox` (a message) |
|---|---|---|
| 3000, 4000 | `DialogBox_TopRect`, `+0` 1 | `DialogBox_TopRect`, `+0` 1 |
| 5000 | `DialogBox_Backdrop5000Rect`, `+0` 0 | `DialogBox_TopRect`, `+0` 1 |
| 6000 | `DialogBox_Backdrop6000Rect`, `+0` 0 | nothing: `+0` stays the 1 `DialogBox_Init` set and the rect what `Mem_New` left ([Open](#open)) |
| 7000 | `DialogBox_TopRect`, `+0` 1 | `DialogBox_Backdrop7000Rect`, `+0` 0 |
| any other | `DialogBox_TopRect`, `+0` 1 | nothing |

The `.ENG` boxes are loaded on the war map, group 6000, and on the debriefing, screen 1510, whose group is 5000 plus 0 to 3 by the campaign ([the backdrop](menus.md#the-backdrop)), so both stand in their own box with `+0` 0. A message over group 3000 or 5000 stands in the top strip with `+0` 1, and over 7000 in `DialogBox_Backdrop7000Rect` with `+0` 0. `DefaultDialogBox_Ctor` always takes `DialogBox_TopRect`, with `+0` 1 from `DialogBox_Init`. `Dialog_LoadFromFile` calls `DigitalDialogBox_Ctor` (`00019279`) with no box, so it allocates one with `Mem_New`: over a group its `SetRect` does not name, the message's box has `+0` 1 and whatever rect the allocation held.

### Laying out the text

**The text is cut into lines at spaces, each line as wide as fits the box, and the lines go down the box at a fixed spacing.** `DialogBox_Layout` (`000198a6`)(box):

1. the line height *h* is the font's cell height, `HudFont_CellHeight2` (`00051ff0`), less 2;
2. the slot count `+0x19` is the text's width in the font, `HudFont_MeasureString` (`00051fac`), over the box's width (right − left), plus 2;
3. the lines a page holds, `+0x25`, is the box's height (bottom − top) over *h*;
4. the page count `+0x23` is the slot count over the lines a page holds, plus 1 when the two differ, as an unsigned word; the page shown `+0x27` is 0. A box lower than the line height holds 0 lines a page, and the division is by 0;
5. it allocates the slots and zeroes them, and with `__vec_new` and `Point_DefaultCtorDialogCopy` (`00019a02`) one point per line of a page: line *i* at (left, top + (*i* + 1) × *h*);
6. it cuts the text into lines with `DialogBox_Wrap` (`00019a1d`)(box, the box's width, the text) and drops the count it returns.

So the page count comes from the slot count, an estimate from the text's width, not from the lines the wrap makes.

**The wrap** is ES2's `TextBox_Wrap` ([the wrap](../../retail/shell/mission-screen.md#the-summary-text-box)): it walks the text in place, ends a line at a newline, and at each space measures the line so far and, when it is wider than the box, cuts it at the last break, which then stays where it was until a space that fits moves it; after the last character the final line is measured and cut once more. Each line's start goes into the next slot. ES1's differs:

- it turns no character into a space: a character below a space other than a newline, and a byte above 127, stays as it is;
- it does not assert after a cut;
- it takes the width as an argument, `int16`, and returns the count of cuts and newlines, one less than the lines;
- it does not test the slot count: a text that makes more lines than `+0x19` writes their starts past the slots' allocation ([Open](#open)).

Its break starts at the text's first character and is not moved by a cut, so a first word wider than the box is cut after its first character, and a second too-wide space before one that fits cuts again at the same break, a line of the same start; the final cut writes its `\0` at the break whether or not that is a space.

### Drawing a page

**A page is drawn over a cleared box: the frame bitmap for the top strip, a flat colour for any other box, and then the page's lines in the green font, each on its own line point.** `DialogBox_Draw` (`00018edd`)(box, *mode*), *mode* being the dialog's version word, 2 for a message and 1 for an `.ENG` block:

1. **The background.** With `+0` set: unless `Shell_RadioOption` (`0007fcbc`), the shell's copy of the RADIO preference ([the shared data](startup.md#the-shared-data)), is 1, `VOICE ONLY`, it loads `res32\textbox.dba` into `DialogBox_FrameBitmaps` with `Resource_LoadBitmapArray` on first use and draws the array's first bitmap at (3, 4) with `Bitmap_Blit`, flags 0. With `+0` clear it fills the box's rect with palette colour `0x1f`, `Raster_FillFrameRectColours`(`0x1f`, `0x1f`, the rect) ([drawing a control](menus.md#drawing-a-control)).
2. **The lines.** When the box has a font and slots, and unless *mode* is 2 and `Shell_RadioOption` is 1, it draws the lines from the page shown × the lines a page holds, for as many as a page holds: each one below the slot count whose slot is set, with `HudFont_DrawString`(font, the line, the next line point), the points taken in order from the first, so a skipped slot leaves no gap. `HudFont_DrawString` aligns the text by the installed render context's `+0x231` ([labels](../simulation/cockpit-widgets.md#labels)), which the box does not set ([Open](#open)).
3. **The voice.** When the box has a font and slots it starts the message's line with `maybe_SncList_StartSound` (`0004544a`)(`Shell_LipSyncPlayer`), which starts it once ([loading a line](#loading-a-line)).

It sets no page and adds no dirty box: it draws on the display's current draw page ([the pages](../rendering/screen-pages.md#the-pages)). With `+0` set and `VOICE ONLY` an `.ENG` box would draw its lines with no background; no `.ENG` box stands in the top strip ([where the box stands](#where-the-box-stands)).

`DialogBox_MarkDirty` (`0001907e`)(box) adds `DialogBox_TopFrameRect` when `+0` is set, and the box's rect otherwise, as a dirty box at delay 0 and again at delay 1, so the next two frames copy the background over it on both pages ([dirty boxes](../rendering/screen-pages.md#dirty-boxes)).

### Stepping pages

`DialogBox_NextPage` (`0001911f`) moves the page shown `+0x27` forward while it is below the page count less 1, and `DialogBox_PrevPage` (`000190fd`) back while it is above 0; each clears `+0` when it moves. So once a box in the top strip has turned a page it no longer draws its frame and fills its rect, `DialogBox_TopRect`, with colour `0x1f` instead, inside the frame drawn before. The war map's handler steps its briefing ([the war map's briefing](war-map.md#the-briefing)).

## Open

- **Open:** a caller of `DialogBox_SetFont` (`00018e40`): `es2_xref.py` finds no branch to it and no stored pointer.
- **Open:** whether a message is ever shown over group 6000, 1000, 2000 or 8000, where `DigitalDialogBox_SetRect` leaves the rect as the allocation held it and `+0` 1: the screens that put up kinds 2 and 5 to 11 are the career's ([the kinds](#the-kinds)).
- **Open:** the career's kinds, for the career module: kind 1 (screen 1499 in mode 1); kind 2 (screen 1490, `NewCareerAlert_OnMessage` (`0002aeec`), the tables `000814ec` and `000814d8`, the `camni` scripts), whose end leaves the script name unwritten for a campaign word above 7; kind 4, which screen 1520's case puts up while the byte `MessageBox_Kind4Shown` (`00081d20`) is 0 and then sets it, `es2_xref.py --decode` finding that read and write and no other access; kinds 5 to 11 (`MessageBox_PickT1Line`, the tables `0008159c` and `000815a8`, the words `000814e8` and `0008159e`, the value `00024b6a` gives, the handles at `0007e718` + 9 and + `0x12`); and the three `MessageBox_SetKind` calls in `0002d327`, screen 1700's handler, whose kinds are set in code Ghidra left undefined.
- **Open:** an ES2 counterpart of the message box's file: `es2_naming.py match` against `DBSIM` and `VSHELL` finds none, the best scores 0.28 to 0.61.
- **Open:** a caller of `Dir_ListFilesByName` (`00037add`): `es2_xref.py` finds no branch to it and no stored pointer.
- **Open:** what the bitmaps of pages 23 to 27 of `C0_SYNC.TTM` and `D0_SYNC.TTM` show, which no lip-sync frame of `C0` or `D0` selects; and the other faces' scripts (`C1`–`C3`, `D1`–`D3`, `G0`, `T1_NU.TTM`, `T1_AN.TTM`): `C1_SYNC.TTM`'s page 0 draws, fixes a rect and waits 6 ticks, and its page 1 carries a label, where `C0`'s do neither.
- **Open:** whether any retail text wraps into more lines than its slot count, the text's width over the box's width plus 2, in the `nm_green` font: one pass over the `.ENG` blocks and the `_TEXT.BIN` lines against the box each is shown in.
- **Open:** the alignment `+0x231` of the render context installed when `DialogBox_Draw` runs, from `MessageBox_Run`, `Shell_DrawScreen`, `Shell_Main` and the page steps.
- **Open:** a retail check of the boxes: INSTANT ACTION's message shows its text in the framed strip at the top of the screen and a face at about (203, 37) whose mouth moves with the voice; the war map opens on its own a moment after the voice ends, or at once on a click; the war map's briefing stands in a box across the lower middle, (69, 150)–(267, 184); with RADIO at `VOICE ONLY` the message shows neither the frame nor the text, and at `TEXT ONLY` the face still talks with no voice.
