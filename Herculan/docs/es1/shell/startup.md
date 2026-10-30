# GO.EXE — the start-up, the frame loop and the shutdown

**ES1's shell is a DOS program that starts from the number the last run left in `data\exit.cfg`: it plays the intro and opens the main menu, or comes back from a mission into its results, then draws up to 60 frames a second until the player leaves for DOS or starts a mission, and on the way out writes its own code back to `exit.cfg` and returns it as its exit status.** ES2's shell gets that number from `ES.EXE` on its command line instead ([ES2's start-up](../../retail/shell/startup.md#the-startup--shell_main-00401525)). What a player would see differently:

- the switches are `-A`, which skips the intro scripts and `endfa.ttm`, `-C`, `-M`, `-S` and `-X`, each also written with `/` and in either case ([the command line](#the-command-line)), where ES2's shell has its own set ([ES2's switches](../../retail/command-line.md#vshell));
- run without `-eggplant`, it prints `To play EarthSiege, type ES.` and exits, where ES2's shows a message box;
- `Ctrl+Esc`, `Alt+Esc`, `Ctrl+Q`, `Alt+Q`, `Ctrl+X` and `Alt+X` open the quit question, screen 999 ([the input](#the-input));
- the joystick's first button is `Enter` and its second `Tab` to the open dialog ([the input](#the-input)); a mouse click reaches the menus through the mouse's own callbacks ([the mouse](menu-input.md#the-mouse)).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are VSHELL's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). What a screen does inside the loop is [the menus](menus.md)'.

## Where the object file starts and ends

`main.cpp` runs from `Main_LoadBnd` (`00012704`) to `000131f7`, with no `ConfigPart` run at its start ([module copies](../formats/compiler.md#vtables)). The file before ends with a `RET` at `00012700` and three zero bytes. Its last code is its static initializer, `Main_RegisterSubsystem` (`000131cb`, the static constructor table's entry `0009a240`), which registers `Main_LoadBnd` at phase 1 ([the shell's files](../formats/bnd-notes.md#the-shells-files)); its `RET` at `000131f5` is followed by two zero bytes, and `VgaPlanar_SelectMaskRegisters` (`000131f8`) opens [the `.ANM` player's](../formats/anm-animation.md) file. Its asserts name `src\main.cpp`: `ExitCfg_Write`'s (line `0x88`), `ExitCfg_Read`'s (`0x90`) and `Shell_Main`'s two (`0x1f8`, `0x219`).

It holds the command line, `data\exit.cfg`'s reader and writer, the blink timer, `Shell_FrameTick` and [`Shell_PresentFrame`](../rendering/screen-pages.md#presenting-a-frame), three keyboard subscribers, two no-ops, `Shell_HandleInput` and `Shell_Main`.

## The command line

`Shell_ParseCommandLine` (`0001272d`), (argv, argc), looks at the arguments from the last down to the second; the program's path is not looked at. An argument starting with `/` or `-` is `-eggplant` when it equals `-eggplant` exactly, and is otherwise looked up by its second character, upper-cased, in the five dwords at `0007e76c` (`REPNE SCASD`), jumping to the target 20 bytes on:

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` | a local | without it the shell prints `To play EarthSiege, type ES.` and returns 1, and `Shell_Main` turns the heap off and calls `__exit`(1) |
| `-A` | `Shell_PlayIntro` (`0007fd0a`, 1 in the image) = 0 | `Shell_OpenFirstScreen` (`0003ba47`) skips the intro scripts and `endfa.ttm` ([the start-up](#the-start-up)) |
| `-C` | `Shell_AltDriveAllowed` (`0007fd0b`) = 1 | `ALT_DRIVE` is honored |
| `-M` | `Shell_MSwitchGiven` (`0007fd04`) = 1 | no reader found by `es2_xref.py` ([Open](#open)) |
| `-S` | the bytes `0007fcb8` and `0007fcb9` = 1 | `Shell_LoadData` then copies `prefs.cfg` options 0 and 1 over both ([the shared data](#the-shared-data)) |
| `-X` | `Shell_DebugMode` (`0007fd08`) = 1 | no blink timer and no `Shell_NavKeySubscriber`, the keyboard taken through `INT 9` instead of `INT 15h` ([the devices](#the-devices)), typed characters not passed to the menus ([the input](#the-input)), and `INT 3` from the assert hook and at the end of `Shell_Main` |

`-eggplant` is case-sensitive and takes only `-`; the other five take `/` or `-` and either case, and any argument that matches none is passed over. The simulator's parser takes `-c` for the drive and has none of these five ([the simulator's command line](../simulation/main-loop.md#the-command-line)).

## The start-up

`Shell_Main` (`00012ec7`), (argc, argv), called by the startup ([startup](../formats/compiler.md#startup)). In order:

1. `Mem_InitHeap`(the dword `0007e738`, 3,700,000), asserting at severity 5 (line `0x1f8`) when it gives 100,000 bytes or fewer, as the simulator's start does ([who makes the heap](../simulation/memory.md#who-makes-the-heap)).
2. `Shell_ParseCommandLine`(argv, argc); a non-zero answer turns the heap off and ends the program with `__exit`(1), with `data\exit.cfg` unread and unchanged.
3. **The drive prefix**, as the simulator's step 3 ([starting](../simulation/main-loop.md#starting--sim_main-00059350)) with `Shell_AltDriveAllowed` for `Sim_AltDriveAllowed`: `ALT_DRIVE`, taken as null unless `-C` was given, when `PREFER_ALT` is set, and the empty string otherwise, is copied to `000aae06`, the prefix `Path_ApplyAltDrive` (`00020989`) puts before every path. `Assert_SetLogDir` (`0000035e`) copies it into `Assert_LogDir` (`0009a2b0`), the directory of the assert log `Assert_LogPath` (`00000386`) builds, as ES2's `Assert_SetLogDir` does from `drive.cfg`.
4. **`data\exit.cfg`.** `ExitCfg_Read` (`0001289c`) reads its `int16` into `Shell_ExitCode` (`0007fcac`), `ExitCfg_Write` (`0001280a`) writes 0 there, and the code read is put back. Both go through `Path_ApplyAltDrive` and assert at severity 5 when the file does not open. So a run that ends before the shutdown leaves 0 in the file, as the simulator's start does ([starting](../simulation/main-loop.md#starting--sim_main-00059350), step 4). Then `Shell_StartupNoOp` (`00012be3`), an empty function.
5. **The archives.** `VolumeGroup_SetBufferSize`(`0x200`) and `VolRStream_SetGroup` with mask `0x100`, extension `.vol`, the directory `ALT_DRIVE` (when `-C` was given) and `dirFirst` set when `PREFER_ALT` is ([the volume group](../formats/streams.md#the-volume-group)); then subsystem phase 1, which reads the shell's eight `bnd\` files ([the shell's files](../formats/bnd-notes.md#the-shells-files)).
6. **The heap against `BND\MAIN.BND`**: below its `+0xc` a severity-5 assert reports the shortfall (line `0x219`), and below its `+8` `Shell_LowMemory` (`0007fd09`) is set ([`MAIN.BND`](../formats/bnd-notes.md#bndmainbnd)).
7. `FileNames_Load` (`0002094d`), `cfg\filename.bin` ([the file-name table](../formats/msn-mission-file.md)).
8. **The devices**, `Shell_InitDevices` (`00045d24`, [below](#the-devices)): the timer and the frame's pace, the sound, the display and its pages, the cursor, the mouse and the keyboard.
9. **The frame-time scale**, `ShellTime_Init` (`000119fc`, [below](#the-frame-time-scale)).
10. `Shell_PresentFrame` ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame)), once.
11. **The shared data**, `Shell_LoadData` (`00020630`, [below](#the-shared-data)).
12. `RenderContext_GetFullScreen` (`0004c716`) into `0007fcd8`: the render context `00083bc4`, made once with `GLViewport_Ctor` (`000512a0`)(0), its rect set to (0, 0)–(319, 199).
13. `maybe_SncList_Init` (`00045337`)(`0009afd4`) ([the `.SNC` list](../formats/snc-lip-sync.md)); `ShellMap_LoadResources` (`000093b3`), the briefing map's `icons` and `mapshp` bitmaps and a timer callback at a count of 2 ([Open](#open)); `ShellMouse_AddCallbacks` (`0001c460`), which registers `ShellMouse_OnMove` (`0001c605`), `ShellMouse_OnLeftPress` (`0001c7d8`) and `ShellMouse_OnLeftRelease` (`0001caaa`) with `Mouse_AddCallback` for the masks 1, 2 and 4: the pointer moved, the left button pressed and the left button released ([the mouse](menu-input.md#the-mouse)).
14. **The fonts and styles**: `Widget_LoadStylesAndFonts`, `WidgetStyles_Load`(`CFG\styles.dat`) and `res32\corners.dba` into `000ab4ee` ([the loads](../formats/styles-dat.md#the-loads)).
15. **The first screen**, `Shell_OpenFirstScreen` (`0003ba47`)(`Shell_ExitCode`): it sets up the scripts (`DirtyList_CreateContext`, `Ttm_Configure`(page 2, the context of step 12, `0x32`), `Ttm_AllocHandles`(2), `Ttm_SetPathPrefix`(`res32\`), `TtmLists_Load`; [the shell's interpreter](../formats/ttm-scripts.md#the-timer)) and opens a screen by the code:
    - 3 or 4, back from a mission: `Mission_ReadRestore`, `Shell_ProcessMissionResults` and the results screens of a single mission (`Shell_Mode` 0) or a career (1);
    - 6, back from a demo: `anm\esiege2.anm` and the main menu, `dlg\menu1000.dlg` ([the first screen](menus.md#the-first-screen--shell_openfirstscreen-0003ba47));
    - any other, among them the 0 a fresh start or a quit leaves: with `Shell_PlayIntro` set, `Ttm_SetPathPrefix`(`intro\`), `ShellMusic_Play` (`00020a70`)(`0xc`), `dynamix.ttm`, `lead_in.ttm` unless the byte `000817bc` is set, `00020a70`(`0xd`), `iintro.ttm` and `earth_w.ttm`; then `000817bc` cleared, and on into 6's code, which sets the prefix back to `res32\` before the main menu.
16. `Palette_UploadWhole` of the palette at `0007fcd0`, the one the scripts left ([the scripts' palette](../rendering/screen-pages.md#presenting-a-frame)), and the byte `0007fcd4` cleared; `Shell_DrawScreen` (`0003c1b7`), which draws the screen; and `X32_TotalFreeBytes` (`0004f77a`), the free memory, into locals `Shell_Main` reads no further.

Where ES2's `Shell_Main` differs ([ES2's start-up](../../retail/shell/startup.md#the-startup--shell_main-00401525)): it reads `drive.cfg`, checks the colour depth, refuses without `-eggplant` or the disc or the sound manager in a message box, reads `Sierra.ini`, and builds its screens and Windows input objects where ES1 polls its devices.

### The devices

`Shell_InitDevices` (`00045d24`), in order:

1. `0005152c` (`ClassItem_RegisterBucket`(`0008b4ec`, `0008b480`)) and `Light_ResetSystem`.
2. `Assert_AddHook`(`Shell_AssertCleanupHook`) ([a fatal assert](#a-fatal-assert)).
3. **The timer.** `Shell_Timer` (`00082758`) = `Kind0InputDevice_Ctor` (`00025a31`)(0, `0x26d3`), the first timer built, so the shell's PC timer runs at the simulator's 120.05 interrupts a second ([the rate](../simulation/timer.md#the-rate)). Three callbacks at a count of 2, every second interrupt, 60 times a second ([callbacks](../simulation/timer.md#callbacks)): `Shell_FrameTick`, which sets `Shell_FrameDue` (`0007fd06`) for `Shell_PresentFrame`; `Shell_WaitTick` (`0004bbdd`), which raises `Shell_WaitCount` (`000a0a90`) for `Shell_WaitTicks` (`0004bbe9`); and, unless `-X` was given, `Shell_BlinkTick` (`00012933`), which keeps `Shell_BlinkOn` (`0007fce0`) set for 20 of every 41 calls, about 0.33 s of every 0.68 s ([Open](#open)). The timer is enabled as input device kind 0 (`InputDevice_Enable`, `00025781`).
4. `ShellSound_Init` ([the shell's sound manager](../formats/sound-samples.md)).
5. **The display**, `Shell_CreateDisplay` (`00045b25`): the `VGA_8` driver's 320 × 200 screen and its four pages ([the pages](../rendering/screen-pages.md#the-pages)).
6. **The cursor**: `res32\arrow.dci` into `00082764` and `Cursor_SetImage` ([cursors](../formats/dfn-hfn-dci.md#cursors)); the raster's slot 31, `0008662c`, which hides the cursor ([the cursor](../rendering/screen-pages.md#the-cursor)); `Cursor_SyncPosition` at (1, 1).
7. **The mouse**: `Shell_MouseDevice` (`00082760`) = `MouseInputDevice_Ctor` (`000268b6`)(0), the simulator's mouse device ([the input devices](../simulation/input.md#the-input-devices)), its bounds {0, 0, display attribute 0, attribute 1} on the display `00082750`, {0, 0, 320, 200}, the width and height (`MouseDriver_SetBoundsRect`), `Mouse_SetPosition`(1, 1), enabled.
8. **The keyboard**: `Shell_KeyboardDevice` (`0008275c`) = `KeyboardInputDevice_Ctor` (`00025a82`)(0, mode), mode `0x20000`, which takes keys through `INT 15h` and lets the BIOS have those no subscriber claims, or under `-X` `0x10000`, the simulator's `INT 9` ([installing](../simulation/input.md#installing--keyboard_ctor-0006c600)); `KeyQueue_Subscriber` given the codes at `00082914`, `Del`, `Esc`, `Q`, `X`, `C`, `Insert`, `F1`, `F2` and `F3`; enabled; and, unless `-X` was given, `Shell_NavKeySubscriber` for three codes ([the keyboard subscribers](#the-keyboard-subscribers)).
9. `Prefs_ClearHandlersAndInit` (`000081a4`), `ES1DBSIM`'s shape: it zeroes the 17 `Prefs_Handlers` and runs `Prefs_Init` (`00008920`), which registers `0000868c` for option 11 through `Prefs_SetHandler` (`00008659`), runs `Prefs_Load` (`000081ca`, `data\prefs.cfg`), sets the byte `0007e09c` from `Speech_ArchivePresent` (`000087ce`) and, when that is 0, sets option 2 to 0 with `Prefs_SetOption`(1, 0, 2), and calls `000087ff`. Then, with no sound manager (`00082768` 0), options 0, 1 and 2 of `prefs.cfg` are set to 0 with `Prefs_SetOption` ([the options](../formats/msn-mission-file.md)).

**The `INT 15h` handler** (`0005cd10`, code Ghidra left undefined; the `INT 9` handler follows at `0005cda8`) acts only on `AH` = `0x4f`, the BIOS's key intercept, and jumps to the saved vector `0007f630` for any other. On its own stack (`00089f80`) it adds 2 to `Keyboard_PrefixCount` (`0007f644`) for the byte `E0` and passes any other byte to `Keyboard_Dispatch`; then it halves the count (`SHR`). With the dispatch's answer 0, or after an `E0`, it returns with `CF` set, so the BIOS processes the byte in `AL`; after a claimed byte it returns with `CF` clear, unless the bit the halving shifted out was 1, the byte having followed an `E0`, when it returns `AL` = `E0` with `CF` set.

The keyboard's reads go through `Input_ReadDeviceEvent` (`00025b30`), `ES1DBSIM`'s ([the input devices](../simulation/input.md#the-input-devices)): in mode `0x20000` it pops `KeyQueue`'s word while the device's status bit 1 (value 2) is set and asks DOS (`Dos_KeyAvailable`, `Dos_ReadKeyNoEcho`) only when it is clear, so the queue's keys come before DOS's.

### The frame-time scale

`ShellTime_Init` (`000119fc`) sets the words `000aa7a2` and `000aa7a4` to `0x100` and, once, takes the timer of GL slot 0, registers `ShellTime_Tick` (`000119e0`) at a count of 2, and computes the interrupts a second as the simulator does ([the rate](../simulation/timer.md#the-rate)): `0009ae10` = 120, `0009ae12` = 12, `0009ae14` = `0009ae16` = 120, and `000aa7a6`, `000aa7a8` and `000aa7aa` = 12. `ShellTime_Tick` takes 1 off `0009ae16` while it is not 0 and adds 1 to `0007e6fc`, which `Anm_Play` reads. At the end of each pass `ShellTime_UpdateFrameScale` (`00011b16`) takes *n* = `0009ae14` − `0009ae16`, the ticks since the last pass, sets `000aa7a2` and `000aa7a4` to `ShellTime_ScaleQ8` (`00011ac7`)(*n*, 12) = *n* × 256 / 12, held to 64–450, and puts `0009ae16` back to 120. The functions from `00011b63` to `00011d9f` read the two words ([Open](#open)).

### The shared data

`Shell_LoadData` (`00020630`):

- copies `prefs.cfg` options 12 to 16 into the mission parameters' words ([the options](../formats/msn-mission-file.md)), options 0 and 1 into the bytes `0007fcb8` and `0007fcb9`, option 2 into the word `Shell_RadioOption` (`0007fcbc`), and into the word `0007fcba` (the word `0007fd10`, `0x3f` in the image, or `0007fd18`, `0xbf`, with option 3 set, plus options 4, 5 and 6) / 256;
- loads 13 fonts ([the loads](../formats/styles-dat.md#the-loads));
- seeds `Shell_RandomState` ([the shell's generator](../formats/msn-mission-file.md)) and loads `eng\watext.bin` into `0009afa8` ([string tables](../formats/bin-string-tables.md));
- sets the rect `0009b000`–`0009b00c` to (0, 0, the display's attribute 2 − 1, attribute 3 − 1);
- loads the HERC diagrams' tables and, unless `Shell_LowMemory` is set, their art ([the load](../formats/herc-diagrams.md)), the careers log ([loading](../formats/career-log-and-player-mec.md#loading)) and the battle set ([which battle set](../formats/bst-battle-sets.md#which-battle-set)), and calls `00027c4c`.

## The frame loop

**Each pass shows the frame drawn last, reads the input, and lets the current screen act on it.** While `Shell_QuitFlag` (`0007fd05`) is clear, `Shell_Main` runs in order:

1. `Shell_PresentFrame`, which waits for the next `Shell_FrameDue`, so a pass takes at least two timer interrupts, at most 60 a second ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame)). `VGA8_ShowPage`, with the retrace wait 2 that `Shell_CreateDisplay` leaves, waits for a vertical retrace to start and end, writes the start address and waits for the next retrace to start ([the pages](../rendering/screen-pages.md#the-pages)), so each frame is shown on a retrace, a whole retrace period after the first it meets.
2. `Shell_PollInput` (`0004de3a`) into `Shell_InputBlock` (`0007e73c`), and `Shell_HandleInput` (`00012be8`) on it ([the input](#the-input)).
3. `NoOp_Void`; `Shell_QuitFlag` set when the screen word `Shell_CurrentScreen` (`000817c0`) is 9999 ([the ways out](#the-ways-out)).
4. `Shell_HandleMessage` (`0003c7d8`)(the dwords `Shell_PendingMessage` (`0007fcb0`), `0007fcb4`), the menu message pending there, which answers 1 when it has changed screens ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)). On an answer of 0, `Ttm_RunAll` runs the scripts ([running the scripts](../formats/ttm-scripts.md)).
5. `Shell_TickScreen` (`0003c115`), the current screen's tick ([drawing a screen](menus.md#drawing-a-screen)); `CockpitMouse_ProcessQueue` (`0001098e`), the mouse's event queue for a widget root, which `Shell_PollInput` also calls ([below](#the-input)); `MessageBox_Run` (`000382f6`)(`0009b010`, the input block), the message box; `Dialog_Draw` (`00018dcb`)(`0009afc8`), the text box.
6. When `Shell_QuitRequested` (`0007e808`) is set, `Shell_OpenQuitAlert` (`000408cc`), which opens screen 999, the quit question, unless the screen word `Shell_CurrentScreen` (`000817c0`) is 999 already, and the flag cleared.
7. `X32_TotalFreeBytes` into locals, and `ShellTime_UpdateFrameScale` ([the frame-time scale](#the-frame-time-scale)).

### The input

`Shell_PollInput` (`0004de3a`) calls `CockpitMouse_ProcessQueue` (`0001098e`), then `Input_PollDeviceBlock` (`000277d8`), the simulator's function with the shell's block at `000a04a0` ([the live block](../simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0)). For each of the block's four buttons `+0x1d`–`+0x20` that `Input_LatchButton` (`0004de18`)(value, button) has latched, the button is inverted while it still equals the latched value, and the latch dropped once it differs, so a button held after a latch reads as released. It clears the Num Lock and Scroll Lock bits `0x1000` and `0x2000` of the keyboard word and returns the block.

`CockpitMouse_ProcessQueue` (`0001098e`) is the shell's copy of the simulator's queue for a widget root (`ES1DBSIM`'s `CockpitMouse_ProcessQueue`, `000444d0`): it returns 0 at once unless the word `0007e5d4`, the queue's mouse callback slot, and the dword `0007e5d8`, the root, are set; otherwise it swaps the queues `000aa796` and `000aa79a`, empties the new back one, and for each 14-byte record keeps the buttons in `0007e66c`, and on a moved point keeps it at `0009adf4`/`0009adf8`, stores it less the root's `+0x227`/`+0x22b` at `0009ae08`/`0009ae0c`, and hit-tests and tracks through `00010eb0` and `000117ba` while `0007e5de` is clear, or drags the captured widget while it is set; then the presses and releases, as the simulator's. `es2_xref.py --decode` finds `0007e5d4` written by `maybe_MouseEventQueue_Start` (`00010559`), the slot, and `maybe_MouseEventQueue_Stop` (`000106af`), 0, and `0007e5d8` written by `maybe_MouseEventQueue_Start`, 0, and `0001070d`, which it finds called from `0000290b`, `00002b0a` and the mission map's constructor `000094d9` ([the buttons](main-menu.md#the-buttons)). Both are 0 in the image, so until `Shell_PrepareMission` has started the queue it returns at once. It is not how the driver's events reach the menus, whose callbacks the driver calls itself ([the mouse](menu-input.md#the-mouse)).

`Shell_HandleInput` (`00012be8`), given the block, for a keyboard word without bit `0x4000`, first clears bit `0x1000` of the word through `Shell_InputBlock` (`AND byte [EBX+1],0xef`), the block `Shell_Main` passed, and then looks the word up:

| Word | Does |
|---|---|
| `Space`, `,`, `.`, `-`, `=`, each with or without `Shift` | calls a function of the object `Shell_MissionMap` (`000809a4`), while it is set and the byte `000809a8` is not 0, and sets `000809a8` to 3 ([Open](#open)) |
| `Insert` (`0x52`) | `Tab` to the dialog (`DUIWindow_HandleNavKey` (`0001cc86`)(`0xf`)) when `prefs.cfg` option 10 is set |
| `Ctrl+Esc`, `Alt+Esc` | `Shell_QuitRequested` |
| `Ctrl+Q`, `Alt+Q`, `Ctrl+X`, `Alt+X` | `Shell_QuitRequested` |
| `Ctrl+C` (`0x42e`) | nothing: the table's last code, whose case is the same end as code 0 |
| `Q`, `X`, `C` otherwise (`Alt+C` included) | the character `q`, `x` or `c`, upper case with `Shift` or `Caps Lock` (`0x800`), or'd with `0x4000`, to `DUIWindow_SendChar` (`0001bb85`) with the current screen `Shell_CurrentMenu` (`000817c8`)'s `+6`, while that is set; the test compares the word's low byte only (`XOR AH,AH`) and tests `0x600` on the whole word |

Then the joysticks' first button (`+0x1d`, [the live block](../simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0)) pressed sends `Enter` (`0x1c`) to the dialog through `DUIWindow_HandleNavKey` (`0001cc86`) and latches the button, and on the next pass, the button reading released, `Enter`'s release (`0x9c`); the second (`+0x1e`) sends `Tab` and latches. Last, unless `-X` was given, a word with bit `0x4000`, a typed character, goes to `DUIWindow_SendChar` (`0001bb85`) with the current screen's `+6`, while `Shell_CurrentMenu` is set.

### The keyboard subscribers

`main.cpp` holds three of the keyboard library's subscribers ([a key](../simulation/input.md#a-key--keyboard_int9handler-0007dde4-and-keyboard_dispatch-0007de20)):

- `Shell_NavKeySubscriber` (`00012b4e`), registered by `Shell_InitDevices` for `Tab`, `Enter` and `Enter`'s release, the NUL-ended list at `0007e7f0` that `Keyboard_AddSubscriber` reads (the ten bytes after its NUL, `Space`, the four arrows, `Backspace`, `Home`, `End`, `Esc` and `Del`, are not registered), passes each, Num Lock's bit cleared, to the dialog's key handler `DUIWindow_HandleNavKey` (`0001cc86`) and claims it.
- `Shell_ScanKeySubscriber` (`00012b69`), which `0001f442` and `0001f66f` register, passes on `Home`, `Up`, `Left`, `Right`, `End` and `Down` while Num Lock is on, claims `Ctrl+Alt+Del` (`0x653`) and does nothing with it, and hands every other code to `Shell_ForwardKeyToDialog` (`00012bb7`), which gives it to `DUIWindow_SendKey` (`0001bbd2`) with the dialog `DUIWindow_Active` (`0007f0d0`) while that pointer is set.
- `Shell_QuitKeyCallback` (`00012b01`) sets `Shell_QuitRequested` for the six quit keys and writes `entering quitKeyCallback` to the monochrome monitor; `es2_xref.py` finds no reference to it ([Open](#open)).

## The ways out

**The loop ends when `Shell_QuitFlag` is set, and `Shell_Main` returns `Shell_ExitCode`, which the shutdown has also written to `data\exit.cfg`.** The codes are ES2's ([exit codes](../../retail/command-line.md#exit-codes)):

| Way out | `Shell_ExitCode` |
|---|---|
| `EXIT TO DOS` on the main menu (`MainMenu_OnMessage`, `0002a70a`), which sets `Shell_QuitFlag` | 0 |
| message 100 to `QuitAlert_OnMessage` (`0002a554`), which sets `Shell_QuitFlag` | 0 |
| `Shell_HandleMessage` (`0003c7d8`)'s case for message 9999, which opens screen 9999, the loading picture ([changing screens](menus.md#changing-screens--shell_handlemessage-0003c7d8)), and so ends the loop on the next pass | 5 when `Shell_DemoRequested` (`000817ce`) is set, which `OptionsMenu_OnMessage` (`0002b79d`) sets when it turns message `0x515` into 9999; 2 otherwise |

Two more end the program without the shutdown: no `-eggplant`, through `__exit`(1) before `exit.cfg` is read; and a fatal assert, below. The [simulator](../simulation/main-loop.md#the-end--sim_shutdown-0005b568) leaves 3, 4, 6 or 0, which the next start's step 15 reads. What `ES.BAT` and `ESCD.EXE` do with the shell's status and the file is not in `GO.EXE` ([Open](#open)).

### A fatal assert

`Shell_AssertCleanupHook` (`00045a68`), the hook `Shell_InitDevices` adds, runs before a severity-4, -5 or -6 assert ends the program with `__exit`(1) ([severities](../simulation/asserts.md#severities)). Under `-X` it executes `INT 3` and returns. Otherwise it calls `0005a588` on the display `00082750`; destroys the sound manager and sets the timer's divisor to `0x4da6` when there is one; removes `Shell_NavKeySubscriber` and destroys the keyboard; and sets `Shell_ExitCode` to 0 and writes it with `ExitCfg_Write`. The simulator's hook does the same for a mission ([a fatal assert in a mission](../simulation/asserts.md#a-fatal-assert-in-a-mission)). The two asserts before the hook is added, `Shell_Main`'s steps 1 and 6, leave the 0 that step 4 wrote, or the file unread at step 1.

## The shutdown

After the loop, in order:

1. `CareerLog_Write`(`0009b20c`, −1), the careers log ([the careers log](../formats/career-log-and-player-mec.md)).
2. `ExitCfg_Write`, `Shell_ExitCode` into `data\exit.cfg`.
3. `Shell_CloseScreens` (`0003c081`): `0004199c`, `Ttm_Shutdown`, the current screen destroyed, and the render contexts `0007fcd8` and `0007fcdc` destroyed.
4. `ShellMap_ReleaseResources` (`00009453`), `Shell_FreeData` (`00020ae3`), which frees the fonts, the HERC diagrams' tables, the careers log's entries and the battle set's campaigns, and `ShellMouse_RemoveCallbacks` (`0001c4de`).
5. **`Shell_ShutdownDevices` (`00046094`)**: the object `0007fd00` and the sound manager destroyed; `Shell_NavKeySubscriber` removed unless `-X` was given; the keyboard and mouse devices destroyed; the timer's divisor set to `0x4da6`, the three timer callbacks removed, `Pit_RemoveHandler` ([the interrupt](../simulation/timer.md#the-interrupt--pit_timerinterrupt-0006d391)) and the timer destroyed; `00082754` destroyed; with `Shell_ExitCode` not 0, bit 0 of the display `00082750`'s first byte cleared, as the simulator clears its display's ([starting](../simulation/main-loop.md#starting--sim_main-00059350), step 10); and the display destroyed with `0005a3e4`(display, 0).
6. `VolRStream_DeleteGroup` and `Mem_HeapOff`; under `-X`, `INT 3`.
7. `Shell_ExitCode` returned in `AX`, which the startup passes to `exit` ([startup](../formats/compiler.md#startup)).

## Functions

| ES1 | Address | ES1DBSIM | ES2 |
|---|---|---|---|
| `Shell_Main` | `00012ec7` | `Sim_Main` (`00059350`) | `Shell_Main` (VSHELL `00401525`) |
| `Shell_ParseCommandLine` | `0001272d` | | |
| `ExitCfg_Read`, `ExitCfg_Write` | `0001289c`, `0001280a` | | |
| `Assert_SetLogDir`, `Assert_LogPath` | `0000035e`, `00000386` | `00056ecc`, — | `Assert_SetLogDir` (VSHELL `0044e060`), `Assert_LogPath` (VSHELL `0044e090`) |
| `Shell_InitDevices`, `Shell_ShutdownDevices`, `Shell_AssertCleanupHook` | `00045d24`, `00046094`, `00045a68` | —, —, `Sim_AssertCleanupHook` (`0005a7b8`) | |
| `Kind0InputDevice_Ctor`, `KeyboardInputDevice_Ctor`, `MouseInputDevice_Ctor`, `InputDevice_Enable` | `00025a31`, `00025a82`, `000268b6`, `00025781` | `maybe_Kind0InputDevice_Ctor` (`0004b440`), `0004b484`, `0004c1e0`, `0004b1c0` | |
| `Input_PollDeviceBlock`, `Input_LatchButton`, `Shell_PollInput`, `Shell_HandleInput` | `000277d8`, `0004de18`, `0004de3a`, `00012be8` | `0004c2d0`, —, —, — | |
| `Shell_NavKeySubscriber`, `Shell_ScanKeySubscriber`, `Shell_ForwardKeyToDialog`, `Shell_QuitKeyCallback` | `00012b4e`, `00012b69`, `00012bb7`, `00012b01` | | |
| `Shell_BlinkTick`, `Shell_WaitTick`, `Shell_WaitTicks` | `00012933`, `0004bbdd`, `0004bbe9` | | |
| `ShellTime_Init`, `ShellTime_Tick`, `ShellTime_UpdateFrameScale`, `ShellTime_ScaleQ8` | `000119fc`, `000119e0`, `00011b16`, `00011ac7` | | |
| `Shell_LoadData`, `Shell_FreeData` | `00020630`, `00020ae3` | | |
| `ShellMap_LoadResources`, `ShellMap_ReleaseResources` | `000093b3`, `00009453` | | |
| `ShellMouse_AddCallbacks`, `ShellMouse_RemoveCallbacks` | `0001c460`, `0001c4de` | | |
| `RenderContext_GetFullScreen`, `X32_TotalFreeBytes` | `0004c716`, `0004f77a` | | |
| `Shell_StartupNoOp`, `Main_RegisterSubsystem` | `00012be3`, `000131cb` | | |

## Globals

| | Address |
|---|---|
| `Shell_ExitCode`, `int16` | `0007fcac` |
| `Shell_QuitFlag`, `Shell_QuitRequested`, `Shell_FrameDue` | `0007fd05`, `0007e808`, `0007fd06` |
| `Shell_PlayIntro`, `Shell_AltDriveAllowed`, `Shell_MSwitchGiven`, `Shell_DebugMode` | `0007fd0a`, `0007fd0b`, `0007fd04`, `0007fd08` |
| `Shell_LowMemory` | `0007fd09` |
| `Shell_DemoRequested` | `000817ce` |
| the pending menu message, four words | `0007fcb0`–`0007fcb7` |
| `Shell_InputBlock` | `0007e73c` |
| `Shell_Timer`, `Shell_KeyboardDevice`, `Shell_MouseDevice` | `00082758`, `0008275c`, `00082760` |
| `Shell_BlinkOn`, its counter | `0007fce0`, `0007e7e8` |
| `Shell_WaitCount`, `int16` | `000a0a90` |
| `Assert_LogDir`, the log path | `0009a2b0`, `0009a2b4` |

## Open

- **Open:** what the launcher does with the shell's exit status and with `data\exit.cfg`: `GO.EXE` holds no code of `ES.BAT` or `ESCD.EXE`. A retail check: after `EXIT TO DOS` under DOSBox-X, `data\exit.cfg` holds the `int16` 0, and after a mission's launch 2 until the simulator overwrites it.
- **Open:** what the object `Shell_MissionMap` (`000809a4`) is, whose functions `0000a9c8`, `0000d657`, `0000d599` and slots `+8` and `+0xc` of its `+0x4a` table `Space`, `,`, `.`, `=` and `-` call, and which `Shell_TickScreen` (`0003c115`) ticks while `000809a8` is above 1. A retail check: press `Space` on a screen that plays something and watch what stops.
- **Open:** what reads `Shell_BlinkOn`: `es2_xref.py` finds its two writes and a reference at `0001f77f`, in code Ghidra has not disassembled, inside `0001f442`'s region.
- **Open:** what reads `Shell_MSwitchGiven`: `es2_xref.py` finds the parser's store only.
- **Open:** what reaches `Shell_QuitKeyCallback`: `es2_xref.py` finds no branch to it and no stored pointer.
- **Open:** what `0007fcb8` and `0007fcb9` hold beyond the music and the click: `-S` sets both to 1 and `Shell_LoadData` then copies options 0 and 1 over them; `ShellMusic_Play` (`00020a70`) plays a backdrop's music only with `0007fcb8` set, and `MTButton_Draw` (`0001d200`) a text button's click only with `0007fcb9` set ([the menus](menus.md#the-backdrop)); `es2_xref.py --decode` finds further readers in `0002b82b`, `Menu_BuildFromDlg`, `Anm_Play`, `MTBmpButton_Draw` (`0001db87`) and `TtmScript_RunSoundCommand`. A retail check: with option 0 off, the main menu plays no music.
- **Open:** what reads `000aa7a2` and `000aa7a4`, the frame-time scale: the nine small functions from `00011b63` to `00011d9f` `es2_xref.py --decode` finds.
- **Open:** what `ShellMap_LoadResources`' timer callback `000092ae`, `00009275` and the block `0007e20c` of the word `0009a4d6`'s size are.
- **Open:** what `Prefs_Init`'s `0000868c` (option 11's handler, stored with `Prefs_SetHandler` (`00008659`)) and `000087ff` do, what the byte `0007e09c` gates, and what `Shell_InitDevices`' `0005152c` does, and what the displays `00082750` and `00082754` and the object `0007fd00` are beside `Shell_CreateDisplay`'s `000ab616`.
- **Open:** a retail check of the frame's pace under DOSBox-X: a breakpoint on `Shell_FrameTick` (`00012974`) with the main menu up stops 60 times a second, on every second interrupt.
