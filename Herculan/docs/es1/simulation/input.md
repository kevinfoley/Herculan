# DBSIM.EXE — appinput.cpp, the input build

**ES1's simulator turns the keyboard, mouse and joystick into ES2's input block once a tick, from a fixed choice of six joystick configurations where ES2's reads a binding byte per control ([ES2's](../../retail/simulation/joystick-input.md#applying-the-bindings--input_buildplayerdevice-0045a7f4)).** What a player would see differently:

- the keypad's eight keys move a stick's full travel, and a quarter of it while the stick drives the HERC and the keypad aims the turret; `M`, `J`, `K` and `I` move a quarter; `Alt` held makes every key full travel; ES2's keys move half;
- the stick has two modes, movement and turret, which keypad 0 and the stick's second button swap while preferences option 8 is on; its third button is the all-stop in movement mode and centers the turret in turret mode ([the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0));
- with preferences option 10 on, `Ctrl+I` and `Ctrl+M` step the throttle and the stick's Y no longer drives it;
- `keyjoy.cfg` has no `Rudder`, and a replay reads the tape's own copy of it;
- a tape is recorded one frame at a time, appended to the file each frame.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `0004c650`–`0004db13`, `appinput.cpp` by its assert at `0004d56a`. It opens with a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) after the static initializer `0004c64c` (static constructor table entry `000adc5c`) of the file before and its three zero bytes: `HeapPtr_DtorAppInputCopy` (`0004c650`), `ConfigPart_DtorAppInputCopy` (`0004c678`, slot `+0x1c` of `000957a6`), `ConfigPart_GetClassTagAppInputCopy` (`0004c6b7`) and `TSPartBase_DtorAppInputCopy` (`0004c6c0`, slot `+0x1c` of `000957da`). It ends with the static initializer `Input_InitKeyboardAxisTable` (`0004da7c`, entry `000adc60`) and its exit routine `AppInput_ExitNoOp` (`0004db10`, exit table entry `000adcc4`), a lone `RET`, followed by three zero bytes. Its functions are ES2's from `Input_KeyjoyAxisKey` (`0045a308`) to `Input_InitKeyboardAxisTable` (`0045b888`) in order, less ES2's preferences, joystick set-up, command queue and mouse fitting ([functions](#functions)).

## The axis keys — `Input_KeyjoyAxisKey` (`0004c6ec`)

(code): the keyboard subscriber `SimInput_SetEnabled` registers against the 28 codes at `KeyjoyWantedCodes` (`0009586c`): keypad 7, 8, 9, 4, 6, 1, 2, 3, `M`, `J`, `K`, `I`, `Space` and `Enter`, then the same fourteen with the key-up bit `0x80`. It answers 1 or 2, ES2's claim and pass-on results ([ES2's](../../retail/simulation/cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)):

1. Under `DemoMode` it sets `DemoAbort` and answers 1, where ES2's answers 2.
2. With preferences option 10 (`000bd56c`) on and `Ctrl` held without `Alt`, it answers 1 after looking the scancode up in the four dwords at `00095898`: `I` subtracts `0x40` from `Input_KeyThrottleStep` (`00095896`), `M` adds `0x40`, and their key-ups and every other code do nothing. ES2 keeps its `Ctrl` digits in a separate subscriber.
3. With `SimInput_Enabled` (`00095818`) set it folds the code's index into 0–13 and updates the three 14-byte key-state blocks, held `Input_KeyHeld` (`000b1844`), release-pending `000b1854` and seen `000b1864`, as ES2's do, stores the `Alt` bit in `Input_KeyAltHeld` (`000b18a0`), and answers 1 except for `Enter`.
4. Otherwise 2.

ES2's list has fifteen keys, keypad `-` and `+` in place of `Enter`, and ES2's passes `Space` on and, with the developer flag, `Alt` and `Ctrl` codes.

## The keyboard axes — `Input_BuildKeyboardAxes` (`0004c818`)

(slow, out): ES2's ([the keyboard](../../retail/simulation/joystick-input.md#the-keyboard)) over keys 0–11 with the (dx, dy) table `KeyboardAxisDeltas` (`000b1874`) and the diagonal flags `KeyboardAxisDiagonalFlags` (`0009588c`, keypad 7, 9, 1 and 3). A key's entry is shifted left 6, a quarter of `0x100`, or left 8 while `Input_KeyAltHeld` is set and, for keys 0–7, whenever *slow* is 0. Then `out+0x10` = `Space` held and `out+0x11` = `Enter` held, where ES2's has `Space` and key 0's release-pending byte.

`Input_InitKeyboardAxisTable` (`0004da7c`), the static initializer, writes the table: ES2's first twelve entries ([the table](../../retail/simulation/joystick-input.md#the-keyboard)), keypad 7 (−1, −1) to `I` (0, +1), and a word 1 at `000b188b` for `I`'s dy, whose high byte is the first of the output block `000b188c`.

## Setting up — `Input_BuildSourceTable` (`0004c95c`)

`Sim_StartMission` calls it first ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)). In order: `0004b484`(0, `0x10000`) and `0004b1c0` on its `+4`; `0006c3b4`(`0006c6e4`, `0009581c`); `SimInput_SetEnabled`(1); `0004c1e0`(0), `0004b1c0` on its `+0x3a`, and `0006bdbc`/`0006bdf0` with the bounds (·, ·, 319, 199); `0004a724`; `Input_LiveBlockPtr` (`000b1840`) = `0004c2d0`(). It then fills the source table, as ES2's:

| Pointers | Point at |
|---|---|
| `000b17e0`–`000b17ec` | the input block's four axes, `000b17b9`, `000b17bb`, `000b17bd`, `000b17bf` (the destinations) |
| `000b17f0`–`000b17fc` | its first four buttons, `000b17c1`–`000b17c4` |
| `000b1800`–`000b180c` | the keyboard's two pairs, `000b188c`, `000b1890`, `000b1894`, `000b1898` |
| `000b1810`–`000b181c` | the live block's four stick axes, `+0xd`, `+0x11`, `+0x15`, `+0x19` |
| `000b1820`–`000b182c` | the keyboard's buttons, `000b189c`–`000b189f` (`Space`, `Enter` and two zero bytes) |
| `000b1830`–`000b183c` | the live block's buttons `+0x1d`–`+0x20` |

It clears the four press-once latches `Input_ButtonLatch` (`000bd634`–`000bd637`) and ends with `Keyjoy_LoadConfig` ([below](#keyjoycfg--keyjoy_loadconfig-0004d918)).

## The input block

`PlayerInputBlock` (`000b17ac`), `0x2b` bytes, what `Input_BuildPlayerDevice` returns:

| Offset | Holds |
|---|---|
| `+0` | the command word |
| `+2`, `+6` | the pointer's x and y, `int32` |
| `+0xa`, `+0xb`, `+0xc` | the mouse's button bytes |
| `+0xd`, `+0xf`, `+0x11`, `+0x13` | the four axes, `int16`: steering, throttle, twist, pitch |
| `+0x15`–`+0x1c` | eight buttons; button 0 is the trigger |
| `+0x1d`, `+0x21` | pointers to the two axes the camera takes |
| `+0x29` | a replay's tick delta |

ES2's block holds the trigger apart at `+0x0d` and its axes from `+0x0e` ([sources and destinations](../../retail/simulation/joystick-input.md#sources-and-destinations--input_buildsourcetable-0045a5c0)).

## The input build — `Input_BuildPlayerDevice` (`0004cb14`)

No arguments; `Sim_MainTick` calls it first each tick ([the tick](main-loop.md#the-tick--sim_maintick-00059700)). In order:

1. Zeroes the block. Reads the live block's command word, skipping words with a zero scancode byte through `0004c458`(1, word) as `Input_ReadMaskedKeyboardCommand` does, masks it with `SimCommandMask` (`000958b8`, `0x47ff`) and stores it back.
2. **Playback** ([below](#recording-and-playback)) fills the block from the tape.
3. **Live**: `Input_BuildKeyboardAxes`(slow, `000b188c`), *slow* set in stick mode 1 with the camera not driven, or while `maybe_WeaponChain_EoHold` is set; the source buttons are the live block's `+0x1d`.
4. `000444d0` gives the frame's mouse events. Button 1 of the sources pressed, with a stick mode set, runs `Input_ToggleStickMode` once a press (latch `000958bc`).
5. The missile arm, as ES2's ([a latched first row](../../retail/simulation/joystick-input.md#a-latched-first-row-holds-the-axes)): `000958bf` becomes 1 while a round is flown, 2 while button 0's latch holds its state, 0 when the latch is clear; while it is not 0 the keyboard's first pair is zeroed.
6. With the camera not driven and no round flown, in stick mode 1 the keyboard's first pair moves onto the second, its pitch negated. Otherwise the pitch half is negated while a round is flown unless `Missile` is reversed, the stick's X and Y replace the first pair where they have moved and are zeroed, and the second pair and the stick's third and fourth axes are zeroed. `Tilt` negates the keyboard's pitch.
7. **Live**: the configuration ([below](#the-joystick-configurations)) picks a source for each axis and button, and each is copied; then the latches, as ES2's: a latched button reading its latched state (`Input_ButtonLatchState`, `000bd638`) reads the opposite, one reading anything else drops the latch. The pointer, mouse bytes, buttons 4–7 (the live block's `+0x21`–`+0x24`) and the command word come from the live block.
8. **Recording** appends the frame to the tape.
9. The camera's axes: with the camera not driven and no round flown, the turret pair in stick mode 2 and the movement pair otherwise, zeroed while button 0's latch holds its state; with option 10, the throttle axis = `Input_KeyThrottleStep`, which is then cleared. Otherwise the movement pair.
10. `Backturn` negates the steering while the throttle axis is positive.

### The joystick configurations

Preferences option 7 (`000bd569`) chooses one of six. *Mode* is the stick mode; the third and fourth stick axes are the live block's `+0x15` and `+0x19`; "or kb" means the keyboard's source when the stick's reads 0:

| Option 7 | Mode 2 | Mode 0 or 1 | Buttons 0–3 |
|---|---|---|---|
| 0 | | steering, throttle: keyboard pair 1; twist: the third axis with option 9 (`000bd56b`), or kb; pitch: kb | the keyboard's |
| 1, 3 | twist, pitch: stick X, Y or kb; steering: the third axis with option 9, or kb; throttle: kb | steering: stick X or kb; throttle: stick Y (not with option 10) or kb; twist: the third axis with option 9, or kb; pitch: kb | 0, 1 from the stick or kb; 2, 3 from the stick only with 3 |
| 2 | twist, pitch: stick X, Y or kb; steering: the third axis or kb; throttle: the fourth (not with option 10) or kb | steering, throttle: stick X, Y (Y not with option 10) or kb; twist, pitch: the third and fourth axes or kb | all four from the stick or kb |
| 4, 5 | as 1 | as 1 | all four from the stick or kb |

### The stick mode

`Input_StickMode` (`00095819`), 0, 1 or 2, is preferences option 8: the option's handler `0004ad88`, which `0004afe8` registers, copies the value in. **`Input_ToggleStickMode` (`0004d8e1`)**, while option 8 (`000bd56a`) is not 0, sets the mode to 2 when it is 1 and to 1 otherwise, and returns it. Keypad 0 ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)) and source button 1 call it.

### Recording and playback

The tape starts with the seven files `-r` packs ([the command line](main-loop.md#the-command-line)), then one record a frame:

| Bytes | Field |
|---|---|
| 2 | the command word |
| 8 | the four axes |
| 2 | `SimTickDelta` |
| 2 | bit 0 button 0 (`+0x15`), bits 1–7 buttons 1–7, bits 8–11 the four source buttons |
| 4 | the mouse-event count *n* |
| 14 × *n* | the mouse events |

Recording opens `<TapeStem>.tap` in mode `ab` each frame, asserting at severity 5 (`appinput.cpp` line `0x2bd`) when it cannot, writes the record and closes the file. Playback reads the 14 bytes and the count from `TapeFile`; under `DemoMode` a live command word sets `DemoAbort`; `Sim_HandleWindowKey` gets the word. A short read, or the command `0x412` (`Ctrl+E`), sets `DemoAbort`, closes the tape, clears `TapePlayback`, sets `maybe_CockpitMouseLive` and zeroes the command word. Otherwise each mouse event goes to `00044830` and its point becomes `Input_CursorPos` (`000b17d8`), and the record fills the block's command word, axes, buttons and `+0x29`; bits 8–11 become the source buttons. The first playback frame reads the cursor's position into `Input_CursorPos` (`Cursor_GetPosition`, latch `000958be`).

Where ES2's differs ([ES2's tape](../../retail/formats/tap-input-tape.md#the-stream)): ES2's opens once, carries the capability block, a 24-byte header with the hat and the stick's own buttons, and a list of command codes; ES1's carries one command word a frame.

## `keyjoy.cfg` — `Keyjoy_LoadConfig` (`0004d918`)

Opens `Config_BuildPath`(`keyjoy.cfg`) in mode `rt`, so `tapes\keyjoy.cfg` during playback, and reads it a word at a time (`fscanf` `" %s"`), comparing each with `Str_CompareNoCaseN` against the seven words of `0009591c`–`0009593e`: `=` arms a value; `#` skips the rest of the line (`" %[^\n]"`); `TILT`, `BACKTURN` and `MISSILE` choose the key; `DEFAULT` with a value armed disarms it, and `REVERSE` with a value armed sets the key's flag, `Keyjoy_TiltReversed` (`0009580c`), `Keyjoy_BackturnReversed` (`00095810`) or `Keyjoy_MissileReversed` (`00095814`). `DEFAULT` or `REVERSE` with no value armed, or `REVERSE` with no key, stops the read. ES2's reads four keys, `Rudder` among them, through `GetPrivateProfileStringA` from `data\` ([the keyjoy switches](../../retail/simulation/joystick-input.md#the-keyjoy-switches)).

## Small functions

- **`Input_ReadMaskedKeyboardCommand` (`0004d7f8`)**, (out): ES2's, through `0004c458`(1, out) and the mask `00095918` (`0x47ff`). No reference to it found by `es2_xref.py`.
- **`SimInput_SetEnabled` (`0004d82c`)**, (enabled), ES2's with one subscriber: turning the keys on, unless `00095894` is set, registers `Input_KeyjoyAxisKey` against its 28 codes through `0006c2bb`; turning them off clears the three key-state blocks. `SimInput_PushDisable` and `SimInput_PopEnable` ([small functions](main-loop.md#small-functions)) wrap it.
- **`Input_LatchButton` (`0004d8c8`)**, (state, button 1–4): `Input_ButtonLatch`[button − 1] = 1 and `Input_ButtonLatchState`[button − 1] = state, ES2's for four buttons. `es2_xref.py` finds calls from `00007198`, `00045f00`, `00046670`, `00049bbc` and `GrenadeMount_TriggerHeld`.
- **`Input_GetCursorPos` (`0004d910`)** returns `Input_CursorPos`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorAppInputCopy`, `ConfigPart_DtorAppInputCopy`, `ConfigPart_GetClassTagAppInputCopy`, `TSPartBase_DtorAppInputCopy` | `0004c650`, `0004c678`, `0004c6b7`, `0004c6c0` | |
| `Input_KeyjoyAxisKey`, `Input_BuildKeyboardAxes`, `Input_BuildSourceTable`, `Input_BuildPlayerDevice` | `0004c6ec`, `0004c818`, `0004c95c`, `0004cb14` | `0045a308`, `0045a4b0`, `0045a5c0`, `0045a7f4` |
| `Input_ReadMaskedKeyboardCommand`, `SimInput_SetEnabled`, `Input_LatchButton`, `Input_ToggleStickMode` | `0004d7f8`, `0004d82c`, `0004d8c8`, `0004d8e1` | `0045b640`, `0045b670`, `0045b718` |
| `Input_GetCursorPos`, `Keyjoy_LoadConfig`, `Input_InitKeyboardAxisTable`, `AppInput_ExitNoOp` | `0004d910`, `0004d918`, `0004da7c`, `0004db10` | `0045b734`, `0045b78c`, `0045b888` |

Ghidra ends `Input_KeyjoyAxisKey` at `0004c7fb`, before its last two returns (to `0004c815`). The class tag, `Input_InitKeyboardAxisTable` and `AppInput_ExitNoOp` have no Ghidra function.

## Globals

| | Address |
|---|---|
| `PlayerInputBlock`, `0x2b` bytes | `000b17ac` |
| `Input_CursorPos` | `000b17d8` |
| the source table | `000b17e0`–`000b183c` |
| `Input_LiveBlockPtr` | `000b1840` |
| `Input_KeyHeld`, release-pending, seen, 14 bytes each | `000b1844`, `000b1854`, `000b1864` |
| `KeyboardAxisDeltas`, 12 (dx, dy) byte pairs | `000b1874` |
| the keyboard's output block | `000b188c` |
| `Input_KeyAltHeld`, `int16` | `000b18a0` |
| `KeyjoyWantedCodes`, `KeyboardAxisDiagonalFlags` | `0009586c`, `0009588c` |
| `Keyjoy_TiltReversed`, `Keyjoy_BackturnReversed`, `Keyjoy_MissileReversed`, dwords | `0009580c`, `00095810`, `00095814` |
| `SimInput_Enabled`, `Input_StickMode`, bytes | `00095818`, `00095819` |
| `Input_KeyThrottleStep`, `int16` | `00095896` |
| `SimCommandMask` | `000958b8` |
| `Input_ButtonLatch`, `Input_ButtonLatchState`, 4 bytes each | `000bd634`, `000bd638` |

## Open

- **Open:** the callees read for none of this file's functions: `0004b484`, `0004b1c0`, `0006c3b4`, `0006c6e4`, `0004c1e0`, `0006bdbc`, `0006bdf0`, `0004a724`, `0004c2d0` (the live block), `0004c458` (the keyboard read), `000444d0` and `00044830` (the mouse events), and the registration `0006c2bb`; so what the live block's fields come from, and whether answer 1 keeps a key out of the command word.
- **Open:** whether the gunsight drag can arm in ES1: ES2's never does, because its mouse record clears `+0x0b` before the pointer moves ([the gunsight drag](../../retail/simulation/joystick-input.md#the-gunsight-drag)); ES1's `+0xa`/`+0xb` come from the live block, unread. A retail check: hold the right mouse button over the gunsight and drag; the turret follows when it arms.
- **Open:** what preferences options 7 to 10 are called on the preferences panel and which values the panel offers.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)): with option 10 on, each `Ctrl+I` moves the throttle gauge one step forward and each `Ctrl+M` one step back (`0x40` of axis a press, `Mech_ApplyThrottleInput`'s Q8 with `0x91`).
