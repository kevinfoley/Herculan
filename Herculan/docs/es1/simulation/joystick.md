# DBSIM.EXE and GO.EXE — the game-port joystick library and the joystick devices

**ES1 reads the joystick straight from the PC's game port: it times how long each of the port's four axis lines stays high after a write to the port, and turns that count into a signed axis value through a calibration block of measured extremes and a center band.** ES2 gets its stick from Windows ([ES2's](../../retail/simulation/joystick-input.md#reading-the-hardware)) and maps the whole reported range with fixed constants ([normalising](../../retail/simulation/joystick-input.md#normalising-an-axis)). What a player would see differently:

- the stick's travel comes from a stored calibration: a minimum and maximum per axis, and a center band whose width is the dead zone, measured rather than ES2's fixed `0x19`;
- with the shift the calibration loader sets, 7, an axis spans −128 to +128 between the calibrated extremes, the span ES2's normalised axis has before its response curve;
- a reading below the calibrated minimum goes past −128, where a reading above the maximum is held at +128;
- the port's four axes and four buttons are read, two sticks' worth; with no axis answering on the port, a BIOS joystick service (`INT 15h` function `84h`) is tried instead;
- preferences option 7 picks one of five stick models, and option 9 adds rudder pedals, where ES2 has one joystick ([the classes](#the-classes)); two of the models turn a hat into buttons 4–7 ([hats and throttles](#hats-and-throttles));
- the squared response is ES2's curve, but full deflection gives +252 and −256 rather than 258, and an output under 25 reads 0 ([the record](#the-record));
- with no `data\joy1.cal`, every axis reads 0 on the high side of its travel ([the calibration file](#the-calibration-file)).

Addresses are `ES1DBSIM` image addresses unless marked `ES1GO` ([the X-32 executable](../formats/x32-executable.md#the-image)); VSHELL's are ES2's VSHELL.EXE. The class's functions remove their own arguments, so their argument lists are in stack order; the hand-written routines are C convention or take the object in `ESI` ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

The library is two pieces with the mouse driver's object file between them:

| Code | Holds | Ends |
|---|---|---|
| `0006b414`–`0006bbbd` | the joystick class, C++ with `0x90` padding, `DVJoystick_DetectOnStack` to `DVJoystick_ReadFlag` | `RET 4` at `0006bbbb`, two zero bytes |
| `0006bbc0`–`0006be73` | the mouse driver's object ([the input devices](input.md#the-input-devices)), opening with `Mouse_DetectThunk` (`0006bbc0`), a `CALL Mouse_Detect` and `RET` with no Ghidra function, then two `0x90` bytes | `RET 8` at `0006be71`, ending on a 4-byte boundary |
| `0006be74`–`0006c082` | the hand-written port and BIOS routines, `ENTER` frames or none, no `0x90` padding | `RET` at `0006c082`, one zero byte, then the keyboard library (`Keyboard_ClaimInstance`, `0006c084`) |

The code before is `GLCursor_Dtor`'s, whose `RET 8` at `0006b40e` is followed by three zero bytes. No assert in ES1's code names the file; VSHELL's `src32\dv_jy.cpp` holds the same class's functions in the same order (`DVJoystick_NoOp`, `00405739`, to `DVJoystick_ReadFlag`, `00405c77`), the Windows build, with three stubs in place of the hand-written routines (`DVJoystick_ReturnZero`, `DVJoystick_ReadRawNoOp`, `DVJoystick_PollNoOp`) and `joyGetDevCapsA` and `joyGetPos` in place of the port. ES2's simulator reads its stick through its own class ([reading the hardware](../../retail/simulation/joystick-input.md#reading-the-hardware)).

`ES1GO` links the same library: the class at `000522bc`–`00052a1d`, followed by two zero bytes and `FileRStream_CtorDefault` (`00052a20`), and the routines at `0005d2a4`–`0005d4e7`, with one routine more ([construction and detection](#construction-and-detection)).

**The joystick devices are in the input devices' object file, `0004ac60`–`0004c2cf`.** It opens with a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)) after `Prefs_SetHandler`'s `RET 8` at `0004ac5c` and one zero byte: `HeapPtr_DtorInputDevicesCopy` (`0004ac60`), `ConfigPart_DtorInputDevicesCopy` (`0004ac88`, slot `+0x1c` of `0009565a`), `ConfigPart_GetClassTagInputDevicesCopy` (`0004acc7`) and `TSPartBase_DtorInputDevicesCopy` (`0004acd0`, slot `+0x1c` of `0009568e`). Then come the preferences handlers, the calibration file's load and save, the device factory and `Prefs_Init` ([the preferences start-up](input.md#the-preferences-start-up)), the device registry ([the input devices](input.md#the-input-devices)), the keyboard's, the joysticks' and the mouse's devices, ending with a `RET` at `0004c2ce` and one zero byte before `Input_PollDeviceBlock` (`0004c2d0`). No assert names the file. ES2's counterparts are spread over its `appinput.cpp` (the preferences and the joystick set-up) and the device functions after it.

`ES1GO` holds the same keyboard and joystick device functions at `00025ce6`–`000268b5`, compiled without the `0x90` padding, with the class names in the image ([the classes](#the-classes)).

## The joystick object

0x78 bytes. `JoystickDevice_Ctor` (`0004b6c0`) builds one at `+0` of a joystick device ([the device object](#the-device-object)).

| Offset | Holds |
|---|---|
| `+0` | bit 0: the object is enabled, set by `DVJoystick_Init` when the axes asked for answer; the poll, the two normalisations, the block copies and the tracking functions do nothing while it is clear |
| `+4`, `+8`, `+0xc`, `+0x10` | the raw counts of axes 0–3, the game port's bits 0–3 |
| `+0x14` | the buttons, bits 0–3 |
| `+0x15`, `+0x19`, `+0x1d`, `+0x21` | the four axis values the last normalisation computed |
| `+0x25` | a copy of the buttons, copied out after the four values |
| `+0x26` | the calibration block, 0x40 bytes: min[4] |
| `+0x36` | max[4] |
| `+0x46` | the center band's low edge [4] |
| `+0x56` | the center band's high edge [4] |
| `+0x66` | `int16`: the normalisation's shift |
| `+0x68` | the GL slot the object claims (`000bdd0c` + slot × 4) |
| `+0x6c` | 1 when the axes and buttons are read through the BIOS |
| `+0x6d` | `int16`: the I/O port |
| `+0x6f` | dword: the axis mask, bits 0–3 |
| `+0x73` | dword: the number of axes in the mask |
| `+0x77` | the delay before each button read, in units of 256 `LOOP` iterations |

## Construction and detection

**`DVJoystick_Ctor` (`0006b42c`)**, (this, mask, port, slot): allocates 0x78 bytes for a null this, claims the GL slot (`GL_ClaimGlobal`(this, this, slot)) and, when the slot holds this, runs `DVJoystick_Init` with the same arguments; it returns this. VSHELL's asserts when the slot is taken; ES1's leaves the object as allocated. **`DVJoystick_Dtor` (`0006b470`)**, (this, flags): when the GL slot `+0x68` is set, clears bit 0 of `+0` (`DVJoystick_Shutdown`, `0006b604`) and the slot, then frees the object for flag bit 0.

**`DVJoystick_Init` (`0006b4bc`)**, (this, mask, port, slot), in order:

1. Stores the slot at `+0x68` and the port at `+0x6d`; clears `+0x14`, `+0x25`, the shift `+0x66` and the delay `+0x77`.
2. For each axis zeroes the raw count and the value, and empties the calibration block: min and the band's low edge `0x7fffffff`, max and the band's high edge `0x80000000`.
3. Runs `DVJoystick_Detect`; when it answers 0, returns 0 with `+0` as it was.
4. A mask of `0xf` takes the detected mask. `+0x6f` becomes the detected mask ANDed with the mask; when that is 0 or differs from the mask, the dword `+0` is cleared and 0 returned.
5. Sets `+0x73` to the number of bits in `+0x6f`, sets bit 0 of `+0` and returns 1.

So a mask other than `0xf` enables the object only when every axis it names answers, and the object then reads those axes alone. Init leaves `+0x6c` as the allocation left it; `Mem_New` (`000015a4`) returns `Memory_Alloc`'s block, which `Memory_Alloc` zero-fills ([memory](memory.md)). `ES1GO`'s Init (`00052364`) tests the mask differently: a mask of 0, or one whose axes all answered, enables the object with the detected mask and count unchanged.

**`DVJoystick_Detect` (`0006be74`)**, (joystick), C convention: the game port first, `DVJoystick_DetectGamePort`, answering 1 when it found an axis; then the BIOS, `DVJoystick_DetectBios`, answering 1 when that found one and 0 otherwise. Both routines take the object in `ESI` and keep `EAX` to `EDX`.

**`DVJoystick_DetectGamePort` (`0006bf4d`)**, with interrupts off from start to end:

1. Clears `+0x6f` and `+0x73`, writes the port `+0x6d` (`OUT`, any value), and reads it until bits 0–3 all read 1, giving up with nothing found after 0x10000 reads.
2. Reads it again with a fresh count of 0x10000. Each time some of the four bits have dropped to 0 since the last read, the dropped bits are added to `+0x6f` and counted into `+0x73` when it is at most 4,000 (`0xfa0`) reads since this count began, and passed over when later. It stops when all four have dropped or the count runs out.

So an axis is present when its line drops within 4,000 reads, and one whose line stays high longer, or never drops, is left out.

**`DVJoystick_DetectBios` (`0006bfc2`)** asks the BIOS: `INT 15h` with `AX` `8404h`, `DX` 1 and `EBX` loaded from `DVJoystick_BiosDriverEbx` (`0009b698`). On carry it finds nothing. Otherwise each of the four answers `AX`, `BX`, `CX` and `DX` that is non-zero and at most `0xfa0` sets mask bit 0–3 and counts into `+0x73`, except that the third's limit is tested on `BX`, not `CX`. When it counted an axis it sets `+0x6c` to 1, and every later read goes through the BIOS. `DVJoystick_BiosDriverEbx` is 0 in the image ([Open](#open)). `ES1GO`'s `DVJoystick_Detect` (`0005d2a4`) adds a step after a BIOS detection that found an axis: `DVJoystick_QueryBiosDriver` (`0005d4c2`) issues `INT 15h` with `AX` `8400h` and `DX` `0xf0`, and when `AX` answers `5347h` stores the `EBX` it returns in `DVJoystick_BiosDriverEbx` (`ES1GO 00089f9c`) and runs the BIOS detection again, so `+0x73` counts each axis twice. `ES1GO`'s BIOS axis read passes `AX` `8404h` where `ES1DBSIM`'s passes `8400h`.

**`DVJoystick_DetectOnStack` (`0006b414`)** runs `DVJoystick_Detect` on a 0x78-byte object on its stack that it does not initialise, port word included, and returns the answer ([Open](#open)).

## Reading the axes

**`DVJoystick_Poll` (`0006b614`)**, (this): while the object is enabled, reads the axes (`DVJoystick_ReadRaw`) and then the buttons (`DVJoystick_ReadButtons`). `Input_PollJoysticks` calls it once a tick ([the live block](input.md#the-live-block--input_polldeviceblock-0004c2d0)).

**`DVJoystick_ReadRaw` (`0006be9f`)**, (joystick), C convention, keeping every register. With `+0x6c` at 1 it reads the BIOS instead (`DVJoystick_ReadRawBios`, `0006c038`: `INT 15h` with `AX` `8400h` and `DX` 1, the returned `EAX`, `EBX`, `ECX` and `EDX` stored whole in the four raw counts, whatever the mask). Otherwise:

1. Interrupts off. It writes the port `+0x6d` (`OUT`, any value), starting the port's four timers, and reads the port until every axis in the mask `+0x6f` reads 1. After 0x10000 reads without that it turns interrupts on and returns, the counts unchanged.
2. It reads the port again, from a count of 0x10000 shared by all the axes. At each read where masked axes have dropped to 0, it pushes the dropped bits and the count left, and goes on waiting for the rest.
3. When every masked axis has dropped, or the count has run out, interrupts go back on. It then takes the pushed pairs in turn and stores, for each bit of a pair, the number of reads since step 2 began, less 1, at `+4` + bit × 4, until it has stored `+0x73` axes.

An axis that dropped at the first read of step 2 reads 0. The count is a number of port reads, each one pass of an `IN`, `AND`, `CMP`, `LOOPZ` loop: the routine reads neither the PC timer ([the PC timer](timer.md)) nor any other clock, so what a count is worth in time is how long one read of the port takes on the machine, and the calibration block is in the same units. The PC timer's interrupt, 120 a second, waits while interrupts are off, for as long as the longest masked axis takes to drop.

When step 2's count runs out with a masked axis still high, fewer pairs were pushed than step 3 takes: it pops the routine's saved registers as pairs, storing values made from them, and the registers it restores on return come from the wrong stack slots, `ESI` among them, which `DVJoystick_Poll` passes on to the button read. Each axis Init enabled dropped within 4,000 reads when it was detected.

## Reading the buttons

**`DVJoystick_ReadButtons` (`0006bf10`)**, (joystick), C convention, keeping every register but `ECX`: when `+0x77` is non-zero, it first counts down a `LOOP` of `+0x77` × 256. With `+0x6c` at 1 it asks the BIOS (`DVJoystick_ReadButtonsBios`, `0006c05f`: `INT 15h` with `AX` `8400h` and `DX` 0, the answer's `AL`). Otherwise, with interrupts off, it reads the port `+0x6d`. Either way the byte is inverted and shifted right by 4, and stored in `+0x14` and `+0x25`:

| Bit of `+0x14` | Port bit |
|---|---|
| 0 | 4 |
| 1 | 5 |
| 2 | 6 |
| 3 | 7 |

A button reads 0 on the port while held, so a held button is a 1. **`DVJoystick_ReadFlag` (`0006bba4`)** reads the buttons and returns `+0x14`.

## Normalising an axis

**`DVJoystick_ReadAxes` (`0006b638`)**, (this, out): returns 0 while the object is disabled. Otherwise, for each axis *i* in the mask `+0x6f`, with *raw* its count, *min*, *max*, *lo*, *hi* its four calibration values and *s* the shift `+0x66` (its low five bits, as `SHL` takes them):

```
v = raw < max ? raw : max                 // held to max; nothing holds it to min
if v < lo:  value = ((v - lo) << s) / (lo - min)
elif v > hi: value = ((v - hi) << s) / (max - hi)
else:        value = 0
```

in 32-bit two's-complement arithmetic, the division signed and truncating toward zero (`IDIV`), and a zero divisor giving 0. The function computes the value held to at least *min* first and stores it, then overwrites it with the value held to *max*, so *min* bounds nothing. Axes outside the mask keep their last value. The four values go to `+0x15`–`+0x21`; the function copies them and `+0x25`, 17 bytes, to *out* and returns `AL` = 1.

With *s* = 7 the value is −128 at *min*, 0 across the band [*lo*, *hi*] and +128 at *max*; a reading under *min* goes below −128 without bound, and every value above the band is at most +128. With the block Init empties, every value is 0: *v* is `0x80000000`, below *lo*, and *lo* − *min* is 0. Init sets *s* to 0, and the calibration file's loader sets it to 7 ([the calibration file](#the-calibration-file)).

`ES1GO`'s `DVJoystick_ReadAxes` (`00052498`) adds 1 to both differences on each side and drops the zero test: below *lo*, `-(((lo - v) + 1) << s) / ((lo - min) + 1)`; above *hi*, `(((v - hi) + 1) << s) / ((max - hi) + 1)`.

**`DVJoystick_ReadAxesDigital` (`0006b7bc`)**, (this, out): for all four axes, whatever the mask, −1 when *raw* is below *lo*, +1 when above *hi*, 0 otherwise, with no clamp or shift; then the same copy to *out* and `AL` = 1.

## Calibrating

The class keeps the block and offers the pieces of a calibration. The device classes carry their own copies of the tracking functions, with an axis mask ([calibration steps](#calibration-steps)); `es2_xref.py` finds the calibration panel calling those, and no reference to the class's own ([Open](#open)).

- **`DVJoystick_SetRanges` (`0006b868`)** and **`DVJoystick_GetRanges` (`0006b890`)**, (this, block): while enabled and with a non-null block, copy the 0x40-byte calibration block in from it or out to it.
- **`DVJoystick_TrackRange` (`0006b8b8`)**: reads the axes (`DVJoystick_ReadRaw`), then lowers each axis's min to its count when the count is below it and raises its max when the count is above it.
- **`DVJoystick_TrackMin` (`0006b9f0`)** and **`DVJoystick_TrackMax` (`0006b97c`)**: the same for min alone and max alone.
- **`DVJoystick_TrackRange2` (`0006ba68`)**: the same for the center band, widening its low and high edges to the count.
- **`DVJoystick_WaitForFlag` (`0006bb2c`)** and **`DVJoystick_WaitForFlagClear` (`0006bb68`)**: set `+0x77` to `0x80`, so each button read first waits 32,768 `LOOP` iterations, read the buttons until `+0x14` is non-zero (a button held) or zero (all released), and put `+0x77` back.

The four tracking functions work on all four axes whatever the mask, and only while the object is enabled. Each starts from what the block holds, so from Init's empty block the first reading sets each end a function tracks. Of the class's functions, Init, which empties the block, and `DVJoystick_SetRanges` write all of it.

## The joystick devices

**The simulator wraps the library's joystick object in an input device whose class is the stick model the preferences name; once a tick each enabled device turns its stick's axes and buttons into one record of four axes and eight buttons, which the live block takes ([the live block](input.md#the-live-block--input_polldeviceblock-0004c2d0)).** ES2 builds one device of the base class ([ES2's](../../retail/simulation/joystick-input.md#normalising-an-axis)).

### The device object

0x8b bytes:

| Offset | Holds |
|---|---|
| `+0` | the joystick object ([above](#the-joystick-object)) |
| `+0x78` | the input device `InputDevice_Register` records ([the input devices](input.md#the-input-devices)): the enabled byte, then at `+0x79` the kind, 3 for the first stick and 4 for the second |
| `+0x7d` | the response mode: 0 linear, 1 squared, 2 digital ([the record](#the-record)) |
| `+0x81` | the class's number, 0 to 4 ([the classes](#the-classes)) |
| `+0x85` | the buttons the device reports, bits 0–3 |
| `+0x86` | 1 for the two classes that read a hat |
| `+0x87` | the vtable, whose slot `+4` returns the record |

**`JoystickDevice_Ctor` (`0004b6c0`)**, (this, mode, mask, port, kind):

1. For a null *this*, allocates 0x8b bytes (`operator new`), returning 0 when that fails.
2. Builds the joystick object with (mask, port, kind) (`DVJoystick_Ctor`, [construction](#construction-and-detection)), so its GL slot is the kind, and registers `+0x78` as input device *kind*.
3. Stores `IMJoystickVtable` (`00095756`), the mode, 0 in `+0x81` and 0 in the object's axis count `+0x73`.
4. **Claims its axes.** For each axis in the low byte of *mask* that answered at detection (`+0x6f`, [construction](#construction-and-detection)) and that `JoystickClaimedAxes` (`0009571c`) lacks, sets it there, adds 1 to `+0x73` and adds 1 to `JoystickClaimedAxisCount` (`0009571d`).
5. Sets `+0x6f` to the low byte of *mask*, whether it answered or not, and `+0x85` and `+0x86` to 0; returns *this*.

So `+0x73`, the number of axes the port read stores ([reading the axes](#reading-the-axes)), counts only the axes no earlier device claimed. `Input_PollJoysticks` polls a pair of devices through the first, with the claimed axes and their count for the call ([the live block](input.md#the-live-block--input_polldeviceblock-0004c2d0)), so one timing of the port serves both. ES2's constructor takes the mode last and claims nothing.

**`JoystickDevice_Dtor` (`0004b7a8`)**, (this, flags): returns for a null *this*; stores `IMJoystickVtable`, clears the enabled byte of its kind (`Input_DeviceEnabled`, `000bd600`) and each of the two recorded joystick kinds (`000bd616`, `000bd61a`) that equals its kind, clears the bits of the byte `+0x6f` from `JoystickClaimedAxes` and takes the byte `+0x73` from `JoystickClaimedAxisCount`; destroys the joystick object (`DVJoystick_Dtor`, flags 0) and frees the device for flag bit 0. The vtable has no destructor slot. `es2_xref.py` finds two calls, from `ControlsPanel_ApplyDevices` (`00048e98`, [rebuilding the devices](preferences.md#rebuilding-the-joystick-devices--controlspanel_applydevices-00048e98)). ES2's clears one recorded kind and releases no axes.

### The classes

`GO.EXE`'s vtables carry their class names: each table follows the name, and the `int16` before the table holds −14, the name's offset from it. `ES1DBSIM`'s tables carry no name, and its `IMJoystick`, `IMJoystick1` and `IMJoystick2` share one table, where `GO.EXE`'s have one each. Each constructor takes (this, mode), builds through `JoystickDevice_Ctor` with port `0x201`, and sets:

| Class | Constructor | Mask | Kind | Vtable, read slot | `+0x81` | `+0x85` | `+0x86` | `ES1GO` constructor, vtable |
|---|---|---|---|---|---|---|---|---|
| `IMJoystick` | `JoystickDevice_Ctor` (`0004b6c0`) | argument | argument | `00095756`, `Joystick_ReadWithResponse` (`0004b828`) | 0 | 0 | 0 | `00025d6d`, `00080752` |
| `IMJoystick1` | `0004be30` | `3` | 3 | `00095756`, as `IMJoystick` | 0 | `3` | 0 | `00026467`, `0008076e` |
| `IMJoystick2` | `0004be94` | `0xc` | 4 | `00095756`, as `IMJoystick` | 0 | `0xc` | 0 | `000264d8`, `0008078a` |
| `IMThrustmasterFCS` | `0004bef8` | `0xb` | 3 | `00095766`, `0004bf50` | 1 | `0xf` | 1 | `00026549`, `000807ac` |
| `IMFlightstick` | `0004c054` | `0xb` | 3 | `00095786`, `0004c0a4` | 2 | `3` | 0 | `000266e2`, `000807e8` |
| `IMFlightstickPro` | `0004c114`, on `IMFlightstick`'s | `0xb` | 3 | `00095796`, `0004c164` | 3 | `0xf` | 1 | `000267d1`, `00080808` |
| `IMThrustmasterRCS` | `0004bfe0` | `4` | 4 | `00095776`, `0004c034` | 4 | 0 | 0 | `00026653`, `000807cc` |

The mask is the axes the device asks the joystick object for, so a device is enabled only when each of them answered ([construction](#construction-and-detection)): `IMJoystick1` axes 0 and 1, `IMJoystick2` axes 2 and 3 (the port's second stick), the three Thrustmaster FCS and Flightstick classes axes 0, 1 and 3, and `IMThrustmasterRCS` axis 2 alone.

### Building the devices

**`Input_CreateJoystickDevices` (`0004af0c`)**, no arguments, runs at the end of `Prefs_Init` ([the preferences start-up](input.md#the-preferences-start-up)). Every constructor it calls gets mode 1, the squared response:

| Option 7 (`000bd569`) | First device | Second device |
|---|---|---|
| 1 | `IMJoystick1` | `IMThrustmasterRCS` with option 9 |
| 2 | `IMJoystick1` | `IMJoystick2` |
| 3 | `IMThrustmasterFCS` | `IMThrustmasterRCS` with option 9 |
| 4 | `IMFlightstick` | `IMThrustmasterRCS` with option 9 |
| 5 | `IMFlightstickPro` | `IMThrustmasterRCS` with option 9 |
| 0, or above 5 | none | `IMThrustmasterRCS` with option 9 |

With option 9 (`000bd56b`) set and option 7 at 2, it clears option 9 (`Prefs_SetOption`(1, 0, 9)) instead. Each device built loads its calibration file, `data\joy1.cal` for the first and `data\joy2.cal` for the second, through `Path_ApplyAltDrive` ([the calibration file](#the-calibration-file)), and is then enabled (`InputDevice_Enable` on `+0x78`), which records it for its kind. A device whose axes did not answer is enabled too: its joystick object stays disabled, so its poll and its normalisation do nothing.

`ES1GO`'s `Input_CreateJoystickDevices` (`000087ff`), the last step of the shell's `Prefs_Init` ([the devices](../shell/startup.md#the-devices), step 9), has the same statements, with the two files named by the shell's `Shell_FileNames` lines 1 and 2.

### The record

**`Joystick_ReadWithResponse` (`0004b828`)**, slot `+4` of `IMJoystickVtable`, (device), fills `JoystickRecord` (`000b1738`, 0x1c bytes) and returns it:

1. Mode 0 or 1 normalises the axes (`DVJoystick_ReadAxes`, [normalising](#normalising-an-axis)), mode 2 reads their signs (`DVJoystick_ReadAxesDigital`), into `JoystickAxisBuffer` (`000b1754`): four axis values and the button byte. While the joystick object is disabled, neither writes, and the buffer, which every device shares, keeps its last contents.
2. With *s* the shift `+0x66`, *k* = (1 << (*s* + 8)) / ((1 << *s*) − 25), signed and truncating: 318 for *s* = 7.
3. For each axis in the mask `+0x6f`, with *v* its buffer value: when *k* is not 0, *v* becomes *v* − 25 above 25, *v* + 25 below −25 and 0 between, then (*k* × *v*) >> 8 (arithmetic shift), written back to the buffer. Mode 1 then gives *v*² >> (*s* − 1), *v* taken as `int16`, negative when *v* is, and 0 when its size is under 25; modes 0 and 2 give *v*. An axis outside the mask gives 0.
4. Each button in the mask `+0x85` gives 1 when its bit is set in the buffer's button byte, 0 otherwise; a button outside it gives 0.

| Record offset | Holds |
|---|---|
| `+0`, `+4`, `+8`, `+0xc` | axes 0–3, dwords: the stick's X and Y, the port's second X and Y |
| `+0x10`–`+0x13` | buttons 0–3, port bits 4–7 ([reading the buttons](#reading-the-buttons)) |
| `+0x14`–`+0x17` | cleared here; the hat, for two classes ([below](#hats-and-throttles)) |
| `+0x18` | the axis mask `+0x6f` |
| `+0x19` | the button mask `+0x85` |
| `+0x1a` | `+0x86` |

With *s* = 7, a calibrated axis's −128…+128 becomes −128…+127 after step 3's rescale, 0 for −25…+25, and mode 1 squares that to −256…+252, 0 for calibrated values from −56 to +57. ES2's mode 1 skips the rescale and squares the value less 25 over 41, so its full deflection is 258 and its small outputs between 0 and 25 survive ([ES2's](../../retail/simulation/joystick-input.md#normalising-an-axis)). Mode 2 reads every axis as 0, as ES2's: *k* is never 0, and the band zeroes −1 and +1. ES2's also expands eight buttons and a four-bit hat and keeps a copy to flag a change.

`ES1GO`'s copy (`00025ee5`) reads the band from the `int16` `Joystick_DeadZone` (`ES1GO 000a045c`) in each of the three places `ES1DBSIM`'s has 25, and skips step 3 while that word is 0 ([Open](#open)).

### Hats and throttles

Four classes read slot `+4` through their own function, each running `Joystick_ReadWithResponse` first. The hat and throttle readers take axis 3's normalised value *a* from the joystick object's `+0x21`, the value before step 3 ([the joystick object](#the-joystick-object)); *s* is the shift `+0x66`, and the thresholds are given for *s* = 7.

- **`IMThrustmasterFCS_ReadState` (`0004bf50`)**: clears the record's fourth axis `+0xc`, and turns *a* into one hat byte: *a* below −112 sets `+0x14`, below −80 `+0x16`, below −48 `+0x15`, below −16 `+0x17`, and from −16 up, or 0, none. In general the first threshold is −(1 << *s*) + (1 << (*s* − 3)), and each next one 1 << (*s* − 2) higher.
- **`IMFlightstick_ReadState` (`0004c0a4`)**: the record's fourth axis becomes a throttle: *t* = 2*a* + (1 << *s*) as an `int16` (from *a*'s low word), and `+0xc` = *t*² >> (*s* − 1), negative when *t* is, 0 when its size is under 64. An axis whose calibration puts the center band at one end of its travel, so that *a* runs from −128 to 0, gives −256 at one end, 0 for *a* from −95 to −33 and +256 at the other. `ES1GO`'s copy (`00026741`) reads the zero band from the `int16` `Joystick_ThrottleDeadZone` (`ES1GO 000a045e`).
- **`IMFlightstickPro_ReadState` (`0004c164`)**: `IMFlightstick_ReadState`, then, when buttons 0 and 1 (`+0x10`, `+0x11`) are both held, buttons 2 and 3 pick one hat byte, both held `+0x14`, button 2 alone `+0x15`, button 3 alone `+0x16`, neither `+0x17`, and the four button bytes are cleared. So while buttons 0 and 1 are held together the device reports the hat and no button.
- **`IMThrustmasterRCS_ReadState` (`0004c034`)**: negates the record's third axis `+8`, its one axis.

### What each configuration reads

`Input_MergeJoystickRecord` ors each enabled device's record into the live block: the four axes into `+0xd`, `+0x11`, `+0x15` and `+0x19`, the eight bytes `+0x10`–`+0x17` into `+0x1d`–`+0x24`, and `+0x18` and `+0x19` into `+0x26` and `+0x27` ([the live block](input.md#the-live-block--input_polldeviceblock-0004c2d0)). The input build reads stick X as `+0xd`, Y as `+0x11`, the third and fourth axes as `+0x15` and `+0x19`, buttons 0–3 as `+0x1d`–`+0x20` and buttons 4–7 as `+0x21`–`+0x24` ([the configurations](input.md#the-joystick-configurations)). Per option 7:

| Option 7 | `+0xd`, `+0x11` | `+0x15` | `+0x19` | `+0x1d`–`+0x20` | `+0x21`–`+0x24` | `+0x26`, `+0x27` |
|---|---|---|---|---|---|---|
| 1 | stick X, Y | 0 | 0 | buttons 0, 1; 0, 0 | 0 | `3`, `3` |
| 2 | stick X, Y | second stick's X | second stick's Y | buttons 0–3 | 0 | `0xf`, `0xf` |
| 3 | stick X, Y | 0 | 0 | buttons 0–3 | the hat from axis 3 | `0xb`, `0xf` |
| 4 | stick X, Y | 0 | the throttle from axis 3 | buttons 0, 1; 0, 0 | 0 | `0xb`, `3` |
| 5 | stick X, Y | 0 | the throttle from axis 3 | buttons 0–3, or 0 with the hat | the hat from buttons 0–3 | `0xb`, `0xf` |

Option 9's `IMThrustmasterRCS` adds its negated axis 2 at `+0x15` and its mask 4 to `+0x26`, under every option 7 but 2. Every axis is mode 1's squared value, and every value is 0 while the device is disabled or absent.

### Calibration steps

The device file has four tracking functions of its own, (device, axes): each, while the joystick object is enabled, reads the port (`DVJoystick_ReadRaw`) and widens one part of the calibration block to the raw counts for the axes in the byte *axes* alone ([calibrating](#calibrating)).

- **`JoystickDevice_TrackRange` (`0004ba80`)**: the min and the max.
- **`JoystickDevice_TrackMax` (`0004bb68`)** and **`JoystickDevice_TrackMin` (`0004bbfc`)**: the max alone and the min alone.
- **`JoystickDevice_TrackCenterBand` (`0004bc90`)**: the center band's low edge and high edge; then a low edge 1 below the min is raised by 1, and a high edge 1 above the max is raised by 1.

`es2_xref.py` finds the calibration panel's `00049a7c` calling the last three and no reference to `JoystickDevice_TrackRange` ([Open](#open)).

## The calibration file

**`data\joy1.cal` and `data\joy2.cal` hold the first and second device's calibration block as it stands in memory, so a stick keeps its calibration from one session to the next.** Each is 0x40 bytes, 16 little-endian `int32`s in port-read counts ([reading the axes](#reading-the-axes)):

| Offset | Holds |
|---|---|
| `0x00` | min, axes 0–3 |
| `0x10` | max, axes 0–3 |
| `0x20` | the center band's low edge, axes 0–3 |
| `0x30` | the center band's high edge, axes 0–3 |

**`Joystick_LoadCalibration` (`0004ae0c`)**, (path, device): opens *path* (`rb`); when it opens, reads 0x40 bytes into a block on the stack, without checking how many arrived, and closes it. When it does not open, the block is min 0, max `0x50`, low edge `0x28` and high edge `0x50` on all four axes. Either way it hands the block to the joystick object (`DVJoystick_SetRanges`, which copies nothing while the object is disabled) and sets the shift `+0x66` to 7. `Input_CreateJoystickDevices` and `ControlsPanel_ApplyDevices` (`00048e98`) call it.

With that default block an axis's high edge equals its max, and the normalisation holds every count to the max first ([normalising](#normalising-an-axis)), so no count is above the high edge: from `0x28` up the axis reads 0, and from `0x28` down to 0 it runs to −128. Which part of the stick's travel those counts are depends on how fast the machine reads the port, so without `data\joy1.cal` the stick answers on one side of each axis at most ([Open](#open)).

**`Joystick_SaveCalibration` (`0004ae90`)**, (path, device): copies the block out of the joystick object (`DVJoystick_GetRanges`) into a stack block, opens *path* (`wb`) and writes the 0x40 bytes, without testing the open; while the object is disabled the copy writes nothing and the file gets whatever the stack held. `es2_xref.py` finds one call, from the calibration panel's `00049f30`.

## Functions

| Function | Address | `ES1GO` | VSHELL |
|---|---|---|---|
| `DVJoystick_DetectOnStack` | `0006b414` | `000522bc` | `DVJoystick_NoOp` (`00405739`) |
| `DVJoystick_Ctor`, `DVJoystick_Dtor`, `DVJoystick_Init`, `DVJoystick_Shutdown` | `0006b42c`, `0006b470`, `0006b4bc`, `0006b604` | `000522d4`, `00052318`, `00052364`, `00052464` | `0040574d`, `004057b8`, `00405823`, `0040591c` |
| `DVJoystick_Poll` | `0006b614` | `00052474` | `DVJoystick_PollButtons` (`00405927`) |
| `DVJoystick_ReadAxes`, `DVJoystick_ReadAxesDigital` | `0006b638`, `0006b7bc` | `00052498`, `0005261c` | `004059fa`, `00405a63` |
| `DVJoystick_SetRanges`, `DVJoystick_GetRanges` | `0006b868`, `0006b890` | `000526c8`, `000526f0` | `00405ac7`, `00405aea` |
| `DVJoystick_TrackRange`, `DVJoystick_TrackMax`, `DVJoystick_TrackMin`, `DVJoystick_TrackRange2` | `0006b8b8`, `0006b97c`, `0006b9f0`, `0006ba68` | `00052718`, `000527dc`, `00052850`, `000528c8` | `00405b0d`, `00405b5d`, `00405b93`, `00405bc9` |
| `DVJoystick_WaitForFlag`, `DVJoystick_WaitForFlagClear`, `DVJoystick_ReadFlag` | `0006bb2c`, `0006bb68`, `0006bba4` | `0005298c`, `000529c8`, `00052a04` | `00405c19`, `00405c48`, `00405c77` |
| `DVJoystick_Detect` | `0006be74` | `0005d2a4` | `DVJoystick_ReturnZero` (`00405724`) |
| `DVJoystick_ReadRaw`, `DVJoystick_ReadButtons` | `0006be9f`, `0006bf10` | `0005d2de`, `0005d34f` | `DVJoystick_ReadRawNoOp` (`0040572b`), `DVJoystick_PollNoOp` (`00405732`) |
| `DVJoystick_DetectGamePort`, `DVJoystick_DetectBios` | `0006bf4d`, `0006bfc2` | `0005d38c`, `0005d401` | |
| `DVJoystick_ReadRawBios`, `DVJoystick_ReadButtonsBios` | `0006c038`, `0006c05f` | `0005d477`, `0005d49e` | |
| `DVJoystick_QueryBiosDriver` | | `0005d4c2` | |
| `Mouse_DetectThunk` | `0006bbc0` | | |

Ghidra's thunk `0006b603` is the `0x90` byte before `DVJoystick_Shutdown`, the entry `DVJoystick_Dtor` calls. `DVJoystick_BiosDriverEbx` is `0009b698` (`ES1GO 00089f9c`).

The input devices' file ([where it starts and ends](#where-the-object-files-start-and-end)), its functions read here; the registry and the keyboard's and mouse's devices are [`input.md`'s](input.md#functions):

| Function | Address | `ES1GO` | ES2 (DBSIM) |
|---|---|---|---|
| `HeapPtr_DtorInputDevicesCopy`, `ConfigPart_DtorInputDevicesCopy`, `ConfigPart_GetClassTagInputDevicesCopy`, `TSPartBase_DtorInputDevicesCopy` | `0004ac60`, `0004ac88`, `0004acc7`, `0004acd0` | | |
| `Joystick_LoadCalibration`, `Joystick_SaveCalibration` | `0004ae0c`, `0004ae90` | `000086d6`, — | |
| `Voice_ArchiveExists`, `Input_CreateJoystickDevices`, `Prefs_Init` | `0004aed8`, `0004af0c`, `0004afe8` | —, `000087ff`, — | `00459d6c`, —, `0045a19c` |
| `Input_IsKindEnabled` | `0004b3d0` | | `0045bf74` |
| `KeyboardInputDevice_SetMode`, `KeyboardInputDevice_GetMode`, `Input_GetKeyboardDeviceUnchecked`, `Input_GetKeyboardDevice2` | `0004b648`, `0004b66c`, `0004b67c`, `0004b698` | `00025ce6`, `00025d19`, `00025d2a`, `00025d43` | `KeyboardInputDevice_SetReadMode` (`0045c148`), `KeyboardInputDevice_GetReadMode` (`0045c16c`), `0045c178`, `0045c190` |
| `JoystickDevice_Ctor`, `JoystickDevice_Dtor`, `Joystick_ReadWithResponse` | `0004b6c0`, `0004b7a8`, `0004b828` | `00025d6d`, `00025e57`, `00025ee5` | `0045c27c`, `0045c2c4`, `0045c314` |
| `Input_GetDevice`, `Input_GetJoystickDeviceFrameless`, `Input_GetJoystick2DeviceFrameless` | `0004ba10`, `0004ba30`, `0004ba58` | `00026114`, `00026133`, `0002615d` | `0045c508` |
| `JoystickDevice_TrackRange`, `JoystickDevice_TrackMax`, `JoystickDevice_TrackMin`, `JoystickDevice_TrackCenterBand` | `0004ba80`, `0004bb68`, `0004bbfc`, `0004bc90` | `00026187`, `00026211`, `00026286`, `000262fb` | |
| `Input_PollJoysticks` | `0004bd90` | `000263c0` | |
| `IMJoystick1_Ctor`, `Input_GetJoystickDeviceUnchecked`, `IMJoystick2_Ctor`, `Input_GetJoystick2DeviceUnchecked` | `0004be30`, `0004be78`, `0004be94`, `0004bedc` | `00026467`, `000264bd`, `000264d8`, `0002652e` | |
| `IMThrustmasterFCS_Ctor`, `IMThrustmasterFCS_ReadState`, `IMThrustmasterRCS_Ctor`, `IMThrustmasterRCS_ReadState` | `0004bef8`, `0004bf50`, `0004bfe0`, `0004c034` | `00026549`, `000265af`, `00026653`, `000266b5` | |
| `IMFlightstick_Ctor`, `IMFlightstick_ReadState`, `IMFlightstickPro_Ctor`, `IMFlightstickPro_ReadState` | `0004c054`, `0004c0a4`, `0004c114`, `0004c164` | `000266e2`, `00026741`, `000267d1`, `0002682e` | |

`KeyboardInputDevice_GetMode` returns the keyboard device's `+9`, the mode `KeyboardInputDevice_SetMode` stores. The `Unchecked` getters return `Input_DeviceSlot`(1, 3 or 4) less the device's offset to its object base (4 for the keyboard, `0x78` for a joystick), or 0; `Input_GetDevice` does it for any kind; `Input_GetKeyboardDevice2` and the two `Frameless` getters take the kind recorded for the keyboard (`000bd60e`), the first joystick (`000bd616`) or the second (`000bd61a`) and return 0 while it is 0. `Input_IsKindEnabled` returns `Input_DeviceEnabled`[kind]. `es2_xref.py` finds no reference to any of these but `ES1GO`'s `000264bd`, which one stored pointer holds. The four `Unchecked` and `Frameless` getters, `Input_GetKeyboardDevice2`, `Input_GetKeyboardDeviceUnchecked` and `Input_IsKindEnabled` are frameless in `ES1DBSIM`, with no Ghidra function. `Voice_ArchiveExists` opens `vol\simvoice.vol` as given, where ES2's builds a localised name under its base directory.

Globals: `JoystickClaimedAxes` and `JoystickClaimedAxisCount`, bytes, `0009571c` and `0009571d` (`ES1GO 0008070d`, `0008070e`); `JoystickRecord` `000b1738` (`ES1GO 000a0464`); `JoystickAxisBuffer` `000b1754` (`ES1GO 000a0480`); the vtables in [the classes](#the-classes).

## Open

- **Open:** `es2_xref.py` finds no reference to `DVJoystick_DetectOnStack`, the four tracking functions, `DVJoystick_WaitForFlag`, `DVJoystick_WaitForFlagClear` or `DVJoystick_ReadFlag`, in `ES1DBSIM` or `ES1GO`, nor to `JoystickDevice_TrackRange` (`0004ba80`, `ES1GO 00026187`).
- **Open:** the calibration panel ([`preferences.md`'s Open](preferences.md#open)), `00049a7c`, `00049bbc`, `00049f30` and `0004a0e8` by `es2_xref.py`'s callers of the device tracking functions, the save and `DVJoystick_SetRanges`/`GetRanges`. `ES1GO`'s callers of its device functions, `ControlsPanel_ApplyDevices` (`00006bed`), `Input_CreateJoystickDevices` (`000087ff`) and `00007956`, are the shell's counterpart.
- **Open:** a retail check of the default calibration under DOSBox-X with a stick and option 7 at 1: with `data\joy1.cal` renamed away, the HERC turns one way at most, whichever side the stick is pushed to, where with a calibrated `joy1.cal` it turns both ways.
- **Open:** which hat direction each of the record's bytes `+0x14`–`+0x17` is, and so what buttons 4–7 do in the input build. A retail check under DOSBox-X's `joysticktype=fcs` with option 7 at 3, and `joysticktype=ch` with option 7 at 5: press each hat direction and note what the HERC or the view does.
- **Open:** under options 4 and 5, [the configurations](input.md#the-joystick-configurations) name no source for the fourth axis, where the Flightstick classes put their throttle. A retail check under `joysticktype=ch` with option 7 at 4: move the throttle wheel and watch the throttle gauge.
- **Open:** `es2_fieldscan.py` over `00048000`–`0004c300` finds the constructors' writes of the class number `+0x81` and no read (the same scan finds `Joystick_ReadWithResponse`'s read of `+0x86` at `0004b9f6`); the record's `+0x1a`, which holds `+0x86`, is past the bytes `Input_MergeJoystickRecord` merges.
- **Open:** `es2_xref.py --decode` finds seven reads of `ES1GO`'s `Joystick_DeadZone` (`000a045c`) and two of `Joystick_ThrottleDeadZone` (`000a045e`), and no write; both lie past the image's file bytes. While they are 0, `ES1GO`'s response has no band and no rescale, and its throttle no zero band.
- **Open:** whether `InputDevice_Register` (`0004b0b4`) opens a second object file: `Prefs_Init`'s last `RET` ends at `0004b0b3`, on a 4-byte boundary, so the zero-fill test cannot tell.
- **Open:** `es2_xref.py --decode` finds three reads of `DVJoystick_BiosDriverEbx` (`0009b698`) in `ES1DBSIM` and no write; the same search finds `ES1GO`'s write by `DVJoystick_QueryBiosDriver`.
- **Open:** a retail check of the normalisation under DOSBox-X with a stick attached: after calibrating, break at `0006b7b1` (`DVJoystick_ReadAxes`' `MOV AL,1`) and read `+0x15` and `+0x19` with the stick held full left, centered and full right: −128 or below, 0 and +128 for a shift of 7.
