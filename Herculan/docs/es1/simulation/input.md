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

## The keyboard

**ES1 reads the keyboard from the hardware: its own `INT 9` handler takes each byte from port `0x60`, adds the modifier bits, and offers the code to a list of subscribers, newest first. The oldest subscriber is a queue of 128 words, and the command word is one word from it a tick.** ES2 gets its keys from Windows messages and keeps its unclaimed codes in a ring the same way ([ES2's](../../retail/simulation/cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)). The library's code runs from `0006c084` to `0006c6f3`, with four Symantec runtime functions ([identification](../formats/compiler.md#identification)) and two drive functions among it, and the two interrupt handlers are at `0007dd4c`–`0007de1c`, before `Keyboard_Dispatch` (`0007de20`). The library's functions from `0006c084` to `0006c538` take their arguments in the C convention; `Keyboard_Ctor` and the functions after it remove theirs.

### Installing — `Keyboard_Ctor` (`0006c600`)

`KeyboardInputDevice_Ctor` (`0004b484`)(0, `0x10000`), from `Input_BuildSourceTable`, allocates 14 bytes, builds the keyboard at `+0` with `Keyboard_Ctor`(this, mode), registers `+4` as input device kind 1 (`InputDevice_Register`, `0004b0b4`), and keeps the mode at `+9` and a drain flag, 0, at `+0xd`. `Keyboard_Ctor` claims GL slot 1 (`GL_ClaimGlobal`(this, this, 1)). When the slot holds it, `Keyboard_Install` (`0006c6ac`) makes it `Keyboard_Instance` (`0009b6aa`) with the status dword 1, unless another one is (`Keyboard_ClaimInstance`, `0006c084`), and calls `Keyboard_SetMode`(mode). With the instance installed, `Keyboard_Ctor` registers `Keyboard_Uninstall` (`0006c0a8`) as an exit routine (`Keyboard_UninstallAtExit`, `0006c5f0`) and an assert hook (`Keyboard_UninstallOnAssert`, `0006c5f8`), and registers `KeyQueue_Subscriber` (`0006c6e4`) as the first subscriber with an empty list of codes.

**`Keyboard_SetMode` (`0006c0e8`)**, (mode), with interrupts off: unhooks the current mode's vector; seeds `Keyboard_Modifiers` (`0009b6ae`) from the BIOS's shift flags (`INT 16h` function 2), the flag byte as its low byte plus `0x800` for Caps Lock, `0x1000` for Num Lock and `0x2000` for Scroll Lock; hooks the new mode's vector; and stores the mode in the status bits `0x70000`, returning 1, or 0 for no known mode. Mode `0x10000`, the simulator's, hooks `INT 9`: `Keyboard_HookInt9` (`0006c45e`) saves the protected-mode and real-mode vectors (`INT 21h` `AX=2502h` and `2503h`) and takes the interrupt in protected mode with `Keyboard_Int9Handler` (`0007dde4`, `2506h`). Mode `0x20000` hooks `INT 15h` the same way (`Keyboard_HookInt15`, `0006c4c0`) with `Keyboard_Int15Handler` (`0007dd4c`), and mode `0x40000` hooks nothing. `Keyboard_UnhookInt9` (`0006c4a3`) and `Keyboard_UnhookInt15` (`0006c50e`) put the saved vector back (`2507h`). `Keyboard_Uninstall` unhooks, clears the status dword and releases the instance.

### A key — `Keyboard_Int9Handler` (`0007dde4`) and `Keyboard_Dispatch` (`0007de20`)

The handler switches to its own stack (`000a11e0`), reads one byte from port `0x60`, passes it to `Keyboard_Dispatch` in `EAX`, sends the interrupt controller its end of interrupt (`0x20` to port `0x20`) and returns from the interrupt. It does not jump on to the saved vector, so the BIOS's key buffer and shift flags stand still while the simulator runs. `Keyboard_Dispatch`, the byte in `EAX`:

1. With `Keyboard_TranslateTable` (`0009b7ba`) set, the byte's low seven bits are replaced by the table's entry, the key-up bit `0x80` kept.
2. Bit `0x8000` of `Keyboard_Modifiers` is set while the `INT 15h` handler's prefix count `Keyboard_PrefixCount` (`0009b6b0`) is not 0, and cleared otherwise.
3. The code is the byte, bit `0x80` set for a key-up, or'd with the high byte of `Keyboard_Modifiers` as it stood before this byte.
4. A modifier key then updates `Keyboard_Modifiers`, by the byte's low seven bits: `0x2a` (left `Shift`) and `0x36` (right `Shift`) set bits `2` and `1` on a press and clear them on a release, and `0x3a` (`Caps Lock`) flips `0x40` on a press; after any of the three, `0x800` is set when exactly one of "a `Shift` held" and "`Caps Lock` on" holds, and cleared otherwise. `0x38` (`Alt`) sets `0x200` on a press and clears it on a release, `0x1d` (`Ctrl`) the same with `0x400`; `0x45` (`Num Lock`) flips `0x1000` and `0x46` (`Scroll Lock`) `0x2000` on a press.
5. **The subscribers.** `Keyboard_CodeMasks` (`0009b7be`), 256 dwords indexed by the code's low byte, holds bit *i* for each subscriber *i* that wants the code. From the highest set bit down, it calls that subscriber's callback (`Keyboard_Subscribers`, `0009bbc2`, 32 slots, `Keyboard_SubscriberCount` `0009bbbe`) with the code as its one argument, which the callback removes. An answer of 2 passes the code to the next lower bit; any other answer ends the walk and the dispatch returns 1. A code with no bit left returns 0.

The `INT 15h` handler (function `0x4f`) dispatches the same way, but counts an `E0` prefix in `Keyboard_PrefixCount` instead of dispatching it, so that `0x8000` marks the key after it, and lets the BIOS process a key the dispatch returns 0 for. The `INT 9` handler drops the answer.

| Function | Address | Arguments | Does |
|---|---|---|---|
| `Keyboard_AddSubscriber` | `0006c2bb` | (callback, codes) | when a keyboard is installed and fewer than 32 are registered, appends the callback and sets its bit for each byte of the NUL-terminated codes, returning 1; otherwise 0 |
| `Keyboard_AddSubscriberCodes`, `Keyboard_RemoveSubscriberCodes` | `0006c3b4`, `0006c408` | (callback, codes) | set or clear the bit of the newest slot holding the callback for each of the codes, returning 1; 0 when no slot holds it |
| `Keyboard_RemoveSubscriber` | `0006c30a` | (callback) | removes the newest slot holding the callback, moving the later slots and their bits down one |
| `Keyboard_SetTranslateTable` | `0006c289` | (table) | sets `Keyboard_TranslateTable` |
| `Keyboard_PostCode` | `0006c185` | (code) | dispatches the code's low word as if typed |
| `KeyQueue_Push` | `0006c1a0` | (code) | appends the word to `KeyQueue` (`0009b6b2`, 128 words, write index `KeyQueue_Head` `0009b7b2`, read index `KeyQueue_Tail` `0009b7b6`) and sets status bit `2`; when the ring holds 127, drops it and sets bits `2` and `0x80000`; returns 1 |
| `KeyQueue_Pop` | `0006c1f8` | | the next word, or 0 when empty; clears status bit `2` when the ring empties |
| `KeyQueue_Subscriber` | `0006c6e4` | (code), removing it | `KeyQueue_Push`, answering 1 |

`es2_xref.py` finds calls to `Keyboard_AddSubscriber` from `Keyboard_Ctor` and `SimInput_SetEnabled`, and to `Keyboard_AddSubscriberCodes` from `Input_BuildSourceTable`. `KeyboardInputDevice_SetMode` (`0004b648`), (device, mode), calls `Keyboard_SetMode` and keeps the mode at `+9`.

### The simulator's two subscribers

`Input_BuildSourceTable` gives `KeyQueue_Subscriber` the 78 make codes of `KeyQueueWantedCodes` (`0009581c`): `Esc` to `]` (`0x01`–`0x1b`), `Enter` (`0x1c`), `A` to `` ` `` (`0x1e`–`0x29`), `\` to `/` (`0x2b`–`0x35`), keypad `*` (`0x37`), `Caps Lock` (`0x3a`), `F1` to `F10` (`0x3b`–`0x44`), the keypad's thirteen keys `0x47`–`0x53` and `F11` and `F12` (`0x57`, `0x58`). The list has no key-up, `Ctrl`, `Shift`, `Alt`, `Space`, `Num Lock` or `Scroll Lock`. It then registers `Input_KeyjoyAxisKey` through `SimInput_SetEnabled`(1), so a code on both lists is offered to the axis keys first:

- **A code of the axis keys' 28** ([below](#the-axis-keys--input_keyjoyaxiskey-0004c6ec)): answer 1 keeps it out of the queue; answer 2 passes it on, and the queue takes it when it is a make code on its list. So while `SimInput_Enabled` is set, `Enter` reaches both the axis keys (the held button) and the queue (the command `0x1c`), and keypad 7, 8, 9, 4, 6, 1, 2 and 3 and `M`, `J`, `K` and `I` reach the axis keys only. While it is clear, in the cockpit's downward view ([the slide](cockpit-views.md#cockpitview_stepviewtransition-00022904)) and between `SimInput_PushDisable` and `SimInput_PopEnable`, they all reach the queue as commands, and `Space` and the key-ups reach neither.
- **Any other code on the queue's list** reaches the queue.
- **The rest** reach neither.

So `Alt`+keypad 8 is full throttle travel while the keys are on, and the command `0x248`, `Mech_HandleCommand`'s step along +Y ([the cockpit](mech.md#the-cockpit)), while they are off; the same holds for the other move and turn codes on the keypad, except that with preferences option 10 on the axis keys claim `Ctrl` without `Alt` whether the keys are on or off, so `Ctrl`+keypad 4 and 6 (`0x44b`, `0x44d`) never reach the queue, and under `DemoMode` they claim all 28. Keypad 0 and 5 are not among the 28 and are always commands.

Every `SimInput_SetEnabled`(1) after a (0) registers `Input_KeyjoyAxisKey` again, in a new slot, since `Keyboard_AddSubscriber` appends without looking for the callback; the copies answer alike, so the walk is unchanged. With all 32 slots taken, `Keyboard_AddSubscriber` returns 0, which `SimInput_SetEnabled` returns.

### The command word

`Input_PollDeviceBlock` ([the live block](#the-live-block--input_polldeviceblock-0004c2d0)) reads one word a tick: `Input_ReadKeyboardCommand` (`0004c458`)(1, out) answers 0 while the keyboard device is disabled and otherwise stores `Input_ReadDeviceEvent` (`0004b51c`)'s word. In mode `0x10000` that is `KeyQueue_Pop` while status bit `2` is set and 0 otherwise; with the device's drain flag set, which `KeyboardInputDevice_Ctor` clears, it then empties the queue. Modes `0x20000` and `0x40000` read DOS as well: `Dos_KeyAvailable` (`0006c52c`, `INT 21h` function `0x0b`), then `Dos_ReadKeyNoEcho` (`0006c538`, function 8), read again with `0x8000` set when it gives 0, and `0x4000` set. The live block clears `0x1000` and `0x2000` from the word, and `Input_BuildPlayerDevice` masks it with `0x47ff`, dropping `0x800`, so a shifted key is the plain key, as in ES2. Keys typed faster than the tick wait their turn, 127 at most.

**Modifiers.** The command word carries `0x200` for either `Alt` and `0x400` for either `Ctrl`, as they stood when the key's byte arrived; `Input_KeyjoyAxisKey` sees the same bits.

**Repeat.** Neither the handler nor the dispatch compares a byte with the one before, so each make code of a held key's typematic repeat is dispatched again: the axis keys set the key held again, which changes nothing, and a queued key queues its command again, one a tick. The delay and rate are the keyboard's ([Open](#open)).

**Extended keys.** The `E0` prefix reaches `Keyboard_Dispatch` as the code `0xe0`, which no subscriber wants and which no modifier case matches, and the byte after it is the plain key. So the grey arrows are keypad 8, 2, 4 and 6, grey `Home`, `Page Up`, `End` and `Page Down` keypad 7, 9, 1 and 3, grey `Insert` keypad 0 (the stick mode) and grey `Delete` keypad `.`, grey `Enter` is `Enter` and grey `/` is `/`, and the right `Ctrl` and `Alt` are the left ones. The `Shift` bytes a keyboard sends around a grey key move only `0x800`.

## The axis keys — `Input_KeyjoyAxisKey` (`0004c6ec`)

(code): the keyboard subscriber `SimInput_SetEnabled` registers against the 28 codes at `KeyjoyWantedCodes` (`0009586c`): keypad 7, 8, 9, 4, 6, 1, 2, 3, `M`, `J`, `K`, `I`, `Space` and `Enter`, then the same fourteen with the key-up bit `0x80`. It answers 1 or 2, ES2's claim and pass-on results ([ES2's](../../retail/simulation/cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)):

1. Under `DemoMode` it sets `DemoAbort` and answers 1, where ES2's answers 2.
2. With preferences option 10 (`000bd56c`) on and `Ctrl` held without `Alt`, it answers 1 after looking the scancode up in the four dwords at `00095898`: `I` subtracts `0x40` from `Input_KeyThrottleStep` (`00095896`), `M` adds `0x40`, and their key-ups and every other code do nothing. ES2 keeps its `Ctrl` digits in a separate subscriber. Option 10's handler also narrows the throttle to forward only ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)).
3. With `SimInput_Enabled` (`00095818`) set it folds the code's index into 0–13 and updates the three 14-byte key-state blocks, held `Input_KeyHeld` (`000b1844`), release-pending `000b1854` and seen `000b1864`, as ES2's do, stores the `Alt` bit in `Input_KeyAltHeld` (`000b18a0`), and answers 1 except for `Enter`.
4. Otherwise 2.

ES2's list has fifteen keys, keypad `-` and `+` in place of `Enter`, and ES2's passes `Space` on and, with the developer flag, `Alt` and `Ctrl` codes.

## The keyboard axes — `Input_BuildKeyboardAxes` (`0004c818`)

(slow, out): ES2's ([the keyboard](../../retail/simulation/joystick-input.md#the-keyboard)) over keys 0–11 with the (dx, dy) table `KeyboardAxisDeltas` (`000b1874`) and the diagonal flags `KeyboardAxisDiagonalFlags` (`0009588c`, keypad 7, 9, 1 and 3). A key's entry is shifted left 6, a quarter of `0x100`, or left 8 while `Input_KeyAltHeld` is set and, for keys 0–7, whenever *slow* is 0. Then `out+0x10` = `Space` held and `out+0x11` = `Enter` held, where ES2's has `Space` and key 0's release-pending byte.

`Input_InitKeyboardAxisTable` (`0004da7c`), the static initializer, writes the table: ES2's first twelve entries ([the table](../../retail/simulation/joystick-input.md#the-keyboard)), keypad 7 (−1, −1) to `I` (0, +1), and a word 1 at `000b188b` for `I`'s dy, whose high byte is the first of the output block `000b188c`.

## Setting up — `Input_BuildSourceTable` (`0004c95c`)

`Sim_StartMission` calls it first ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)). In order: the keyboard, `KeyboardInputDevice_Ctor`(0, `0x10000`), enabled (`InputDevice_Enable`, `0004b1c0`, on its `+4`), and `Keyboard_AddSubscriberCodes`(`KeyQueue_Subscriber`, `KeyQueueWantedCodes`) ([the keyboard](#the-keyboard)); `SimInput_SetEnabled`(1); the mouse, `MouseInputDevice_Ctor` (`0004c1e0`)(0), which builds the mouse driver's object at `+0` (`DVMouse_Ctor`, [the mouse driver](#the-mouse-driver)) and registers `+0x3a` as input device kind 2, enabled on its `+0x3a`, and `MouseDriver_GetBounds`/`MouseDriver_SetBounds` with the bounds (·, ·, 319, 199); `Prefs_ClearHandlersAndInit` (`0004a724`), which reloads the preferences and builds the joystick devices ([below](#the-preferences-start-up)); `Input_LiveBlockPtr` (`000b1840`) = `Input_PollDeviceBlock`(). It then fills the source table, as ES2's:

| Pointers | Point at |
|---|---|
| `000b17e0`–`000b17ec` | the input block's four axes, `000b17b9`, `000b17bb`, `000b17bd`, `000b17bf` (the destinations) |
| `000b17f0`–`000b17fc` | its first four buttons, `000b17c1`–`000b17c4` |
| `000b1800`–`000b180c` | the keyboard's two pairs, `000b188c`, `000b1890`, `000b1894`, `000b1898` |
| `000b1810`–`000b181c` | the live block's four stick axes, `+0xd`, `+0x11`, `+0x15`, `+0x19` |
| `000b1820`–`000b182c` | the keyboard's buttons, `000b189c`–`000b189f` (`Space`, `Enter` and two zero bytes) |
| `000b1830`–`000b183c` | the live block's buttons `+0x1d`–`+0x20` |

It clears the four press-once latches `Input_ButtonLatch` (`000bd634`–`000bd637`) and ends with `Keyjoy_LoadConfig` ([below](#keyjoycfg--keyjoy_loadconfig-0004d918)).

### The input devices

ES2's registry ([ES2's](../../retail/simulation/joystick-input.md#sources-and-destinations--input_buildsourcetable-0045a5c0)): `InputDevice_Register` (`0004b0b4`)(device, owner, kind) keeps the owner in `Input_DeviceTable` (`000bd5dc`)[kind], clears its byte in `Input_DeviceEnabled` (`000bd600`)[kind] and records the kind, kind 0 at `000bd60a`, 1 (the keyboard) at `000bd60e`, 2 (the mouse) at `000bd612`, 3 or 5 (the first joystick) at `000bd616` and 4 or 6 (the second) at `000bd61a`; for kinds 2 to 6 it first disables the device recorded there. `InputDevice_Enable` (`0004b1c0`), for kinds 2 to 6, disables the device recorded for its kind and records this one, then sets its byte `+0` and the enabled byte; `InputDevice_Disable` (`0004b2a4`) clears both and `InputDevice_IsEnabled` (`0004b2c0`) returns `+0`. `Input_DeviceSlot` (`0004b2d0`) returns `Input_DeviceTable`[kind]. `Input_GetKeyboardDevice` (`0004b310`), `Input_GetJoystickDevice` (`0004b370`) and `Input_GetJoystick2Device` (`0004b3a0`) return the recorded keyboard, less 4, and joysticks, less `0x78`, or 0, as `Input_GetMouseDevice` does the mouse; `Input_GetKeyboardDevice_Thunk` (`0004c62c`) calls the first. The keyboard's and the mouse's destructors, `IMKeyboard_Dtor` (`0004b4d4`) and `MouseInputDevice_Dtor` (`0004c220`), clear the enabled byte and the recorded kind, run `Keyboard_Dtor` (`0006c670`, which uninstalls the instance through `Keyboard_Release` (`0006c6d9`) and frees GL slot 1) or `DVMouse_Dtor` (`0006bcd4`), and free the object for flag bit 0. The joystick devices, kinds 3 and 4, their classes and the record they return are [the joystick page's](joystick.md#the-joystick-devices), with the file's boundaries and its small getters ([functions](joystick.md#functions)).

### The preferences start-up

**`Prefs_ClearHandlersAndInit` (`0004a724`)** zeroes the 17 handlers of `Prefs_HandlerTable` (`000bd598`) and runs **`Prefs_Init` (`0004afe8`)**, ES2's ([ES2's](../../retail/simulation/preferences.md#dataprefscfg--the-option-array)) with ES1's options. In order: `PrefsInitInProgress` (`000b1734`) = 1; the handlers, through `Prefs_SetHandler`, `0004ad0c` for option 0, `0004acfc` for 1, `0004ad30` for 2, `0004ad54` for `0xb`, `Input_SetStickMode` for 8, `Input_SetThrottleLeverMode` for 10 and `Prefs_LimitDetailInLowMemory` for 6; `Prefs_Load`(1), or under `DemoMode` `Prefs_Load`(1) with `DemoMode` cleared for it, then `Prefs_Load`(0) and `Prefs_RevertSelectedOptions` (`0004aaf0`)(`0009570c`, 7), which puts the seven options the list names back to their values as loaded ([the option array](preferences.md#the-option-array)); `VoiceArchivePresent` (`000956bf`) = `Voice_ArchiveExists` (`0004aed8`), and option 2 set to 0 (`Prefs_SetOption`(1, 0, 2)) when that is 0; the joystick devices (`Input_CreateJoystickDevices`, [building the devices](joystick.md#building-the-devices)); `000956be` = 240 ([the ground](../rendering/ground-and-sky.md)); `PrefsInitInProgress` = 0. Since `Input_BuildSourceTable` calls it, each mission start reloads `data\prefs.cfg` and builds the joystick devices again.

## The live block — `Input_PollDeviceBlock` (`0004c2d0`)

No arguments; `Input_BuildPlayerDevice` calls it first each tick and `Input_BuildSourceTable` once. It zeroes `Input_LiveBlock` (`000b1774`, `0x29` bytes), fills it and returns it:

| Offset | Holds |
|---|---|
| `+0` | one word from the keyboard ([the command word](#the-command-word)), `0x1000` and `0x2000` cleared |
| `+2`, `+6` | the mouse's x and y, `int32` |
| `+0xa`, `+0xb`, `+0xc` | 1 for the mouse's buttons 0, 1 and 2, bits 0, 1 and 2 of its button word |
| `+0xd`, `+0x11`, `+0x15`, `+0x19` | the joysticks' four axis dwords |
| `+0x1d`–`+0x24` | the joysticks' eight button bytes, their record's `+0x10`–`+0x17` |
| `+0x25` | `Input_ReadKeyboardCommand`'s answer, 1 while the keyboard device is enabled |
| `+0x26`, `+0x27` | the joysticks' record bytes `+0x18` and `+0x19` |
| `+0x28` | `Input_ReadMouseState`'s answer, 1 while the mouse device is enabled |

In order: the keyboard; `Input_PollJoysticks` (`0004bd90`); for each of the two joysticks, `Input_ReadJoystickState` (`0004c4ac`)(kind 3 or 4, record), which copies the `0x1c`-byte record that slot `+4` of the enabled device's vtable (at its `+0x87`) returns ([the record](joystick.md#the-record)) and answers 0 for a disabled device or no record, and `Input_MergeJoystickRecord` (`0004c3cc`)(record), which ors it into `+0xd`–`+0x27`; the mouse through `Input_ReadMouseState` (`0004c51c`)(2, record), whose record is `Mouse_ReadState`'s when `MouseInputDevice_ReadNewState` (`0004c26c`) finds bit 1 of the mouse's status set (an event since the last read) and otherwise the last one, kept at `Input_LastMouseRecord` (`000b17a0`). So the pointer and the buttons hold their last state between mouse events. ES2's ([ES2's](../../retail/simulation/joystick-input.md#sources-and-destinations--input_buildsourcetable-0045a5c0)) reads one joystick.

`Input_PollJoysticks` polls the first joystick device through `DVJoystick_Poll` (`0006b614`), which times the game port's axes and reads its buttons ([the joystick library](joystick.md#reading-the-axes)); with two, it polls the first with its `+0x6f` and `+0x73` set from the bytes `0009571c` and `0009571d` for the call and put back after, and copies its four axis dwords `+4`–`+0x10` and its button byte `+0x14` into the second's, the byte also into the second's `+0x25`.

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

1. Zeroes the block and refills the live block (`Input_PollDeviceBlock`). Reads its command word, skipping words with a zero scancode byte through `Input_ReadKeyboardCommand`(1, word) as `Input_ReadMaskedKeyboardCommand` does, masks it with `SimCommandMask` (`000958b8`, `0x47ff`) and stores it back.
2. **Playback** ([below](#recording-and-playback)) fills the block from the tape.
3. **Live**: `Input_BuildKeyboardAxes`(slow, `000b188c`), *slow* set in stick mode 1 with the camera not driven, or while `maybe_WeaponChain_EoHold` is set; the source buttons are the live block's `+0x1d`.
4. `CockpitMouse_ProcessQueue` (`000444d0`) presses, releases and drags the cockpit's widgets with the frame's mouse events and returns them ([the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0)). Button 1 of the sources pressed, with a stick mode set, runs `Input_ToggleStickMode` once a press (latch `000958bc`).
5. The missile arm, as ES2's ([a latched first row](../../retail/simulation/joystick-input.md#a-latched-first-row-holds-the-axes)): `000958bf` becomes 1 while a round is flown, 2 while button 0's latch holds its state, 0 when the latch is clear; while it is not 0 the keyboard's first pair is zeroed.
6. With the camera not driven and no round flown, in stick mode 1 the keyboard's first pair moves onto the second, its pitch negated. Otherwise the pitch half is negated while a round is flown unless `Missile` is reversed, the stick's X and Y replace the first pair where they have moved and are zeroed, and the second pair and the stick's third and fourth axes are zeroed. `Tilt` negates the keyboard's pitch.
7. **Live**: the configuration ([below](#the-joystick-configurations)) picks a source for each axis and button, and each is copied; then the latches, as ES2's: a latched button reading its latched state (`Input_ButtonLatchState`, `000bd638`) reads the opposite, one reading anything else drops the latch. The pointer, mouse bytes, buttons 4–7 (the live block's `+0x21`–`+0x24`) and the command word come from the live block.
8. **Recording** appends the frame to the tape.
9. The camera's axes: with the camera not driven and no round flown, the turret pair in stick mode 2 and the movement pair otherwise, zeroed while button 0's latch holds its state; with option 10, the throttle axis = `Input_KeyThrottleStep`, which is then cleared. Otherwise the movement pair.
10. `Backturn` negates the steering while the throttle axis is positive.

### The joystick configurations

Preferences option 7 (`000bd569`) chooses one of six; it also picks the joystick device's class, and which live-block fields each class fills is [the joystick page's](joystick.md#what-each-configuration-reads). *Mode* is the stick mode; the third and fourth stick axes are the live block's `+0x15` and `+0x19`; "or kb" means the keyboard's source when the stick's reads 0:

| Option 7 | Mode 2 | Mode 0 or 1 | Buttons 0–3 |
|---|---|---|---|
| 0 | | steering, throttle: keyboard pair 1; twist: the third axis with option 9 (`000bd56b`), or kb; pitch: kb | the keyboard's |
| 1, 3 | twist, pitch: stick X, Y or kb; steering: the third axis with option 9, or kb; throttle: kb | steering: stick X or kb; throttle: stick Y (not with option 10) or kb; twist: the third axis with option 9, or kb; pitch: kb | 0, 1 from the stick or kb; 2, 3 from the stick only with 3 |
| 2 | twist, pitch: stick X, Y or kb; steering: the third axis or kb; throttle: the fourth (not with option 10) or kb | steering, throttle: stick X, Y (Y not with option 10) or kb; twist, pitch: the third and fourth axes or kb | all four from the stick or kb |
| 4, 5 | as 1 | as 1 | all four from the stick or kb |

### The stick mode

`Input_StickMode` (`00095819`), 0, 1 or 2, is preferences option 8: the option's handler `Input_SetStickMode` (`0004ad88`), which `Prefs_Init` (`0004afe8`) registers with `Prefs_SetHandler` (`0004ac4c`), copies the value in. **`Input_ToggleStickMode` (`0004d8e1`)**, while option 8 (`000bd56a`) is not 0, sets the mode to 2 when it is 1 and to 1 otherwise, and returns it. Keypad 0 ([commands](main-loop.md#commands--sim_dispatchcommand-00059df8)) and source button 1 call it.

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

Recording opens `<TapeStem>.tap` in mode `ab` each frame, asserting at severity 5 (`appinput.cpp` line `0x2bd`) when it cannot, writes the record and closes the file. Playback reads the 14 bytes and the count from `TapeFile`; under `DemoMode` a live command word sets `DemoAbort`; `Sim_HandleWindowKey` gets the word. A short read, or the command `0x412` (`Ctrl+E`), sets `DemoAbort`, closes the tape, clears `TapePlayback`, sets `CockpitMouseLive` and zeroes the command word. Otherwise each event goes onto the cockpit's mouse queue with its recorded time (`CockpitMouseQueue_Push`, `00044830`, [the callback and the queue](cockpit-mouse.md#the-callback-and-the-queue)), for the input build's step 4 to press and release with, and its point becomes `Input_CursorPos` (`000b17d8`); and the record fills the block's command word, axes, buttons and `+0x29`; bits 8–11 become the source buttons. The first playback frame reads the cursor's position into `Input_CursorPos` (`Cursor_GetPosition`, latch `000958be`).

Where ES2's differs ([ES2's tape](../../retail/formats/tap-input-tape.md#the-stream)): ES2's opens once, carries the capability block, a 24-byte header with the hat and the stick's own buttons, and a list of command codes; ES1's carries one command word a frame.

## `keyjoy.cfg` — `Keyjoy_LoadConfig` (`0004d918`)

Opens `Config_BuildPath`(`keyjoy.cfg`) in mode `rt`, so `tapes\keyjoy.cfg` during playback, and reads it a word at a time (`fscanf` `" %s"`), comparing each with `Str_CompareNoCaseN` against the seven words of `0009591c`–`0009593e`: `=` arms a value; `#` skips the rest of the line (`" %[^\n]"`); `TILT`, `BACKTURN` and `MISSILE` choose the key; `DEFAULT` with a value armed disarms it, and `REVERSE` with a value armed sets the key's flag, `Keyjoy_TiltReversed` (`0009580c`), `Keyjoy_BackturnReversed` (`00095810`) or `Keyjoy_MissileReversed` (`00095814`). `DEFAULT` or `REVERSE` with no value armed, or `REVERSE` with no key, stops the read. ES2's reads four keys, `Rudder` among them, through `GetPrivateProfileStringA` from `data\` ([the keyjoy switches](../../retail/simulation/joystick-input.md#the-keyjoy-switches)).

## The mouse driver

**ES1 talks to the DOS mouse driver itself, through `INT 33h`, and keeps the pointer's last position and buttons where the input code reads them.** ES2 gets the mouse from Windows. The functions are at `00055978`–`00055d1f`, hand-written code with `ENTER` frames or none and no `0x90` padding, after the class-item code and before `Volume_Ctor` ([where the object files start and end](../formats/dts-shape.md#where-the-object-files-start-and-end)). Every one but `Mouse_Detect` does nothing while `Mouse_StatusPtr` (`000962e0`) is 0, before `Mouse_Install`.

| Function | Address | Arguments, C convention | Does |
|---|---|---|---|
| `Mouse_Detect` | `00055978` | | asks the extender for the real-mode vector `0x33` (`INT 21h` `AX=2503h`, `CL=0x33`) and returns 0 when it is 0 or points at an `IRET` (`0xcf`), read through the selector `0009003a`; otherwise `INT 33h` functions `0x20` and 0, returning 1 when the reset answers non-zero |
| `Mouse_Install` | `000559c9` | (status, state) | unless installed, and when function 0 answers non-zero: keeps both pointers (`Mouse_StatusPtr`, `Mouse_StatePtr` `000962e4`), sets bit 0 of the status dword, stores `DS` where `Mouse_EventHandler` reloads it (`00096398`, `000967e7`), and makes the extender calls `INT 21h` `AX=2509h`, `250Dh` and `250Eh`, the last with `EDX` `0x7f` |
| `Mouse_SetEventsEnabled` | `00055a2c` | (enabled) | the same three calls, `EDX` `0x7f` when enabled and 0 when not |
| `Mouse_Reset`, `Mouse_DisableDriver`, `Mouse_EnableDriver` | `00055a67`, `00055a76`, `00055a90` | | `INT 33h` functions 0, `0x1f` (its `AX` + 1 returned) and `0x20` |
| `Mouse_SetDoubleX`, `Mouse_GetDoubleX` | `00055aa4`, `00055ab2` | (double) | `Mouse_DoubleX` (`000962dc`): while set, x goes to the driver doubled and comes back halved |
| `Mouse_SetPosition` | `00055ab8` | (*x*, *y*) | stores the point as the last position, x doubled while `Mouse_DoubleX` is set, and moves the driver's pointer there, function 4 |
| `Mouse_SetLimitsAndSensitivity`, `Mouse_SetLimits` | `00055aee`, `00055b53` | (record) | functions 7 and 8 with the record's {`+0`, `+8`} and {`+4`, `+0xc`} as the x and y limits, the x limits doubled while `Mouse_DoubleX` is set, and the first also function `0x1a` with its words `+0x10`, `+0x12` and `+0x14` |
| `Mouse_GetSensitivity` | `00055b34` | (record) | function `0x1b` into the record's `+0x10`, `+0x12` and `+0x14` |
| `Mouse_ReadState` | `00055b86` | | with interrupts off, copies the last x, y and buttons to the state's `+0`, `+4` and `+8` and clears bit 1 of the status dword |
| `Mouse_AddCallback` | `00055bec` | (callback, mask) | takes the lowest free of 32 slots in `Mouse_CallbackMask` (`000962f4`), keeps the callback in `Mouse_Callbacks` (`000962f8`) and the mask's low byte in `Mouse_CallbackEvents` (`00096378`), and returns the slot + 1 |
| `Mouse_RemoveCallback` | `00055c35` | (slot) | frees slot − 1 |
| `Mouse_EnableButtonMap`, `Mouse_DisableButtonMap` | `00055c4d`, `00055c5e` | | set and clear `Mouse_ButtonMapOn` (`000962f2`), returning 1 |
| `Mouse_PostEvent` | `00055bc2` | (event, mask) | calls `Mouse_EventHandler` as the driver would, with the event's x, y and buttons (`+0`, `+4`, the word `+8`) and the mask |

**`Mouse_EventHandler` (`00055c6f`)** is a far procedure taking an event in the driver's registers: `AX` the event mask, `BX` the buttons, `CX` x and `DX` y. It reloads `DS` and `ES`, and unless the byte `0009639a` is set switches to its own stack (`000967e3`) and saves every register. With `Mouse_ButtonMapOn` set the buttons become the entry of the eight-dword table `000967eb` their low three bits pick. It stores the buttons in `Mouse_Buttons` (`000962f0`), x, halved while `Mouse_DoubleX` is set, in `Mouse_X` (`000962e8`) and y in `Mouse_Y` (`000962ec`), sets bit 1 of the status dword, and calls each registered callback whose event byte shares a bit with the mask, passing `000962e8`, the record {x, y, buttons}. With all 32 slots taken, `Mouse_AddCallback` stores no callback but still writes the mask byte and sets a mask bit, at an index whose upper half is what `ECX` held.

**The driver's object** is VSHELL's `DVMouse` of `src32\dv_mse.cpp`, `0x3a` bytes: the status dword at `+0`, the state record {*x*, *y*, buttons} at `+4`, the settings at `+0xe` (the bounds *x0*, *y0*, *x1*, *y1* as `int32` and the three sensitivity words at `+0x1e`), and the settings it found at `+0x24`, which it puts back on shutdown:

| Function | Address | Arguments | Does |
|---|---|---|---|
| `DVMouse_Ctor` | `0006bbc8` | (mouse) | allocates `0x3a` bytes when given none, returning 0 when that fails; claims GL slot 2 (`GL_ClaimGlobal`, `000bdd14`); and when the slot holds it and `DVMouse_Init` answers non-zero, registers `Mouse_ResetThunk` with `atexit` and `DVMouse_AssertHook` with `Assert_AddHook`; returns the mouse |
| `DVMouse_AssertHook` | `0006bccc` | (·) | `Mouse_Reset`, `RET 4` |
| `DVMouse_Dtor` | `0006bcd4` | (mouse, flags) | with GL slot 2 set, `DVMouse_Shutdown` and clears the slot; frees the mouse for flag bit 0 |
| `DVMouse_Init` | `0006bd10` | (mouse) | `Mouse_Install`(`+0`, `+4`); when that sets bit 0 of the status, `Mouse_SetDoubleX`(1), saves the settings to `+0x24` with `MouseDriver_GetBounds`, sets the bounds to (0, 0, 320, 200), reads the sensitivity in with `MouseDriver_GetBounds`(mouse, `+0xe`) and hands the settings to the driver with `MouseDriver_SetBounds`; returns bit 0 |
| `DVMouse_Shutdown` | `0006bd84` | (mouse) | with bit 0 of the status set, clears the status and runs `Mouse_Reset`; when that answers non-zero, `MouseDriver_SetBounds`(mouse, `+0x24`) and returns 1; otherwise 0 |
| `MouseDriver_GetBounds` | `0006bdbc` | (mouse, record) | copies the four bounds into the record and fills its `+0x10`–`+0x14` with `Mouse_GetSensitivity` |
| `MouseDriver_SetBounds` | `0006bdf0` | (mouse, record) | copies the record's four bounds, the dword `+0x10` and the bytes `+0x14` and `+0x15` to `+0xe`–`+0x23` and calls `Mouse_SetLimitsAndSensitivity` with it |
| `MouseDriver_GetBoundsRect`, `MouseDriver_SetBoundsRect` | `0006be18`, `0006be40` | (mouse, rect) | copy the four bounds out, and in, the second also calling `Mouse_SetLimits` with the rect |

`GO.EXE` links the same object, `DVMouse_Ctor` at `0005cff8` to `MouseDriver_SetBoundsRect` at `0005d270`. `Input_BuildSourceTable` then sets the bounds to (0, 0, 319, 199) ([setting up](#setting-up--input_buildsourcetable-0004c95c)), so the pointer's *x* runs over 0–319 and its *y* over 0–199, the driver's *x* doubled and halved back. VSHELL's `DVMouse_Init` (`004053bf`) installs no driver, calls an empty function where ES1's sets `Mouse_DoubleX` and sets the bounds to (0, 0, 480, 640); its `DVMouse_Ctor` (`004052fb`) asserts when the slot is taken and registers no exit routine.

`es2_xref.py` finds the calls: `Mouse_Detect` from `0006bbc0`; `Mouse_Install` and `Mouse_SetDoubleX` from `DVMouse_Init`; `Mouse_Reset` from `DVMouse_AssertHook`, `DVMouse_Shutdown` and `Mouse_ResetThunk` (`00055c47`, whose address is stored at `0006bcab`); `Mouse_SetLimitsAndSensitivity` from `MouseDriver_SetBounds`, `Mouse_GetSensitivity` from `MouseDriver_GetBounds` and `Mouse_SetLimits` from `MouseDriver_SetBoundsRect`; `Mouse_ReadState` from `0004c26c`; `Mouse_DisableDriver` and `Mouse_EnableDriver` from `0004ad54`; `Mouse_AddCallback` from `CockpitMouse_Init` (`000441cc`) and `Mouse_RemoveCallback` from `CockpitMouse_Shutdown` (`000442fc`) ([the callback and the queue](cockpit-mouse.md#the-callback-and-the-queue)); `DVMouse_Ctor` from `MouseInputDevice_Ctor` and `DVMouse_Dtor` from `MouseInputDevice_Dtor`; `Mouse_PostEvent` from `Gau_RovingGunsightWidget` ([the gunsight](cockpit-gunsight-hud.md)) and `Mouse_EventHandler` from `Mouse_PostEvent`. It finds none to `Mouse_SetEventsEnabled`, `Mouse_GetDoubleX` or the button map's two.

## Small functions

- **`Input_ReadMaskedKeyboardCommand` (`0004d7f8`)**, (out): ES2's, through `Input_ReadKeyboardCommand`(1, out) and the mask `00095918` (`0x47ff`). No reference to it found by `es2_xref.py`.
- **`SimInput_SetEnabled` (`0004d82c`)**, (enabled), ES2's with one subscriber: turning the keys on, unless `00095894` is set, registers `Input_KeyjoyAxisKey` against its 28 codes through `Keyboard_AddSubscriber` ([the simulator's two subscribers](#the-simulators-two-subscribers)) and returns its answer; turning them off clears the three key-state blocks. `SimInput_PushDisable` and `SimInput_PopEnable` ([small functions](main-loop.md#small-functions)) wrap it.
- **`Input_LatchButton` (`0004d8c8`)**, (state, button 1–4): `Input_ButtonLatch`[button − 1] = 1 and `Input_ButtonLatchState`[button − 1] = state, ES2's for four buttons. `es2_xref.py` finds calls from `00007198`, `AlertPanel_Enter` (`00045f00`) and `AlertPanel_HandleEvent` (`00046670`) ([the in-mission panels](alert-panels.md#putting-a-panel-up-and-taking-it-down)), `00049bbc` and `GrenadeMount_TriggerHeld`.
- **`Input_GetCursorPos` (`0004d910`)** returns `Input_CursorPos`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorAppInputCopy`, `ConfigPart_DtorAppInputCopy`, `ConfigPart_GetClassTagAppInputCopy`, `TSPartBase_DtorAppInputCopy` | `0004c650`, `0004c678`, `0004c6b7`, `0004c6c0` | |
| `Input_KeyjoyAxisKey`, `Input_BuildKeyboardAxes`, `Input_BuildSourceTable`, `Input_BuildPlayerDevice` | `0004c6ec`, `0004c818`, `0004c95c`, `0004cb14` | `0045a308`, `0045a4b0`, `0045a5c0`, `0045a7f4` |
| `Input_ReadMaskedKeyboardCommand`, `SimInput_SetEnabled`, `Input_LatchButton`, `Input_ToggleStickMode` | `0004d7f8`, `0004d82c`, `0004d8c8`, `0004d8e1` | `0045b640`, `0045b670`, `0045b718` |
| `Input_GetCursorPos`, `Keyjoy_LoadConfig`, `Input_InitKeyboardAxisTable`, `AppInput_ExitNoOp` | `0004d910`, `0004d918`, `0004da7c`, `0004db10` | `0045b734`, `0045b78c`, `0045b888` |

Ghidra ends `Input_KeyjoyAxisKey` at `0004c7fb`, before its last two returns (to `0004c815`). The class tag, `Input_InitKeyboardAxisTable` and `AppInput_ExitNoOp` have no Ghidra function.

The two files before, the input devices' (`0004ac60`–`0004c2cf`, [where it starts and ends](joystick.md#where-the-object-files-start-and-end), the rest of its functions on [the joystick page](joystick.md#functions)) and the live block's (`0004c2d0`–`0004c64f`, after the zero byte at `0004c2cf`); their functions not listed here or there are unread:

| ES1 | Address | ES2 |
|---|---|---|
| `InputDevice_Register`, `InputDevice_Enable`, `InputDevice_Disable`, `InputDevice_IsEnabled`, `Input_DeviceSlot` | `0004b0b4`, `0004b1c0`, `0004b2a4`, `0004b2c0`, `0004b2d0` | `0045bd88`, `0045be34`, `0045bec4`, `0045bedc`, `0045bee8` |
| `Input_GetKeyboardDevice`, `KeyboardInputDevice_Ctor`, `IMKeyboard_Dtor`, `Input_ReadDeviceEvent` | `0004b310`, `0004b484`, `0004b4d4`, `0004b51c` | `0045bf24`, `0045c000`, `0045c03c`, `0045c084` |
| `MouseInputDevice_Ctor`, `MouseInputDevice_Dtor`, `MouseInputDevice_ReadNewState` | `0004c1e0`, `0004c220`, `0004c26c` | `0045c1b8`, `0045c1e4`, `0045c22c` |
| `Input_PollDeviceBlock`, `Input_ReadKeyboardCommand`, `Input_ReadJoystickState`, `Input_ReadMouseState`, `Input_GetKeyboardDevice_Thunk` | `0004c2d0`, `0004c458`, `0004c4ac`, `0004c51c`, `0004c62c` | `0045ba8c`, `0045bba8`, `0045bbf4`, `0045bc58`, `0045bd68` |
| `Input_GetJoystickDevice`, `Input_GetJoystick2Device`, `KeyboardInputDevice_SetMode`, `Input_PollJoysticks`, `Input_MergeJoystickRecord` | `0004b370`, `0004b3a0`, `0004b648`, `0004bd90`, `0004c3cc` | |

The keyboard library, which ES2 does not have:

| ES1 | Address |
|---|---|
| `Keyboard_ClaimInstance`, `Keyboard_Uninstall`, `Keyboard_SetMode`, `Keyboard_PostCode` | `0006c084`, `0006c0a8`, `0006c0e8`, `0006c185` |
| `KeyQueue_Push`, `KeyQueue_Pop`, `Keyboard_SetTranslateTable`, `Keyboard_AddSubscriber` | `0006c1a0`, `0006c1f8`, `0006c289`, `0006c2bb` |
| `Keyboard_RemoveSubscriber`, `Keyboard_AddSubscriberCodes`, `Keyboard_RemoveSubscriberCodes` | `0006c30a`, `0006c3b4`, `0006c408` |
| `Keyboard_HookInt9`, `Keyboard_UnhookInt9`, `Keyboard_HookInt15`, `Keyboard_UnhookInt15` | `0006c45e`, `0006c4a3`, `0006c4c0`, `0006c50e` |
| `Dos_KeyAvailable`, `Dos_ReadKeyNoEcho`, `Keyboard_UninstallAtExit`, `Keyboard_UninstallOnAssert` | `0006c52c`, `0006c538`, `0006c5f0`, `0006c5f8` |
| `Keyboard_Ctor`, `Keyboard_Dtor`, `Keyboard_Install`, `Keyboard_Release`, `KeyQueue_Subscriber` | `0006c600`, `0006c670`, `0006c6ac`, `0006c6d9`, `0006c6e4` |
| `Keyboard_Int15Handler`, `Keyboard_Int9Handler`, `Keyboard_Dispatch` | `0007dd4c`, `0007dde4`, `0007de20` |

Ghidra's thunks `0004b2a2`, `0004c3cb`, `0004c455`, `0004c4a9` and `0006c6aa` are the `0x90` byte before `InputDevice_Disable`, `Input_MergeJoystickRecord`, `Input_ReadKeyboardCommand`, `Input_ReadJoystickState` and `Keyboard_Install`, an entry some of their callers use. The two handlers and the two uninstall stubs have no Ghidra function. `GO.EXE` links the same keyboard library: `es1_naming.py twin` pairs 21 of its functions with `GO.EXE`'s, `Keyboard_Dispatch` with `0005cde4`.

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
| `Input_LiveBlock`, `0x29` bytes; `Input_LastMouseRecord`, 10 bytes | `000b1774`, `000b17a0` |
| `Input_DeviceTable`, dwords by kind; `Input_DeviceEnabled`, bytes by kind | `000bd5dc`, `000bd600` |
| `KeyQueueWantedCodes`, 78 bytes and a 0 | `0009581c` |
| `Keyboard_Instance`, the status dword's pointer | `0009b6aa` |
| `Keyboard_Modifiers`, `Keyboard_PrefixCount`, words | `0009b6ae`, `0009b6b0` |
| `KeyQueue`, 128 words; `KeyQueue_Head`, `KeyQueue_Tail` | `0009b6b2`, `0009b7b2`, `0009b7b6` |
| `Keyboard_TranslateTable` | `0009b7ba` |
| `Keyboard_CodeMasks`, 256 dwords | `0009b7be` |
| `Keyboard_SubscriberCount`, `Keyboard_Subscribers`, 32 dwords | `0009bbbe`, `0009bbc2` |

## Open

- **Open:** `es2_xref.py` finds no call to `Keyboard_PostCode`, `Keyboard_RemoveSubscriber`, `Keyboard_RemoveSubscriberCodes`, `Keyboard_SetTranslateTable` or `KeyboardInputDevice_SetMode`, and one read of `00095894` (`SimInput_SetEnabled`'s) with no write.
- **Open:** the keyboard's repeat delay and rate in ES1: the disassembly's one `INT 16h` is function 2, and a grep of it finds no `OUT` to port `0x60`, so the simulator is not seen setting them. A retail check: hold `]` and time the shield-balance gauge's steps, one a repeat ([the cockpit](mech.md#the-cockpit)).
- **Open:** a retail check of the gunsight drag, which ES2's never arms because its mouse record clears `+0x0b` before the pointer moves ([the gunsight drag](../../retail/simulation/joystick-input.md#the-gunsight-drag)). ES1's `+0xb` is the right mouse button, held from one mouse event to the next ([the live block](#the-live-block--input_polldeviceblock-0004c2d0)), so the drag arms while it is held over the gunsight: hold the right button over the gunsight and drag; the turret follows.
- **Open:** what preferences options 7 to 10 are called on the preferences panel and which values the panel offers.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)): with option 10 on, each `Ctrl+I` moves the throttle gauge one step forward and each `Ctrl+M` one step back (`0x40` of axis a press, `Mech_ApplyThrottleInput`'s Q8 with `0x91`).
