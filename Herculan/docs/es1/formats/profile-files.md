# DBSIM.EXE — profile files and the dongle check

**The simulator reads `resource.cfg`, the sound set-up the installer writes, with its own copy of the Windows calls that read `.ini` files.** `Sim_StartMission` takes the sound card's driver numbers, IRQ, DMA channel and port addresses from it ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc)). The same object file holds the check of a copy-protection dongle, which no reference found by `es2_xref.py` reaches. ES2 reads its configuration files through Windows itself (`GetPrivateProfileStringA`). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## Where the object file starts and ends

The code is `00050e20`–`00051f1b`, after [the digital driver's timer](sound-samples.md#the-digital-drivers-timer) and its three zero bytes. The profile functions run to `00051b2f`, the dongle's from `00051b30` to `00051c3b`, and `VGA8_CopyRectBetweenPages` (`00051c3c`, [the simulator's page routines](../rendering/screen-pages.md#the-simulators-own-page-routines)) starts right after the last `RET`, ending with three zero bytes before `00051f1c`. No zero fill, `ConfigPart` run or static initializer stands between the three ([where object files meet](compiler.md#where-object-files-meet-in-the-code)), so whether they are one object file is [Open](#open).

## The file in memory

One file is held at a time, as a block of text and a list of its lines:

| Global | Address | Holds |
|---|---|---|
| `Profile_Buffer` | `000bd8e2` | the block: the file's text and 2000 bytes more |
| `Profile_FileName` | `000bd8e6` | the file's name, 14 bytes before the next global |
| `Profile_Lines` | `000bd8f4` | the first line's node |
| `Profile_TextEnd` | `000bd8f8` | where a new line's text is written |
| `Profile_BufferEnd` | `000bd8fc` | the block's last byte |
| `Profile_Dirty` | `00095d10` | a byte, set once a value has been written |

`Profile_Load` (`00050e20`), (name): clears `Profile_Dirty`; opens the file as a `FileRStream`; copies the name; mallocs the file's size plus 2000 bytes, reads the file into it and sets `Profile_TextEnd` to one byte past the text. It then splits the text into lines, each a 12-byte node {line, next, previous} from `operator new`: bytes below `0x20` or above `0x7e` before a line are skipped, a line runs while its bytes are `0x20`–`0x7e`, and the byte that ends it is overwritten with 0. So a tab ends a line, and a CR LF ends one line and is skipped before the next. When the file does not open, it mallocs 2000 bytes, sets `Profile_TextEnd` to their start and leaves the list empty, keeping the name.

`Profile_Save` (`00051478`), (name): opens name as a `FileWStream` and returns 0 when its status is not 0; otherwise writes each line followed by CR LF, frees each node, empties the list and the name, frees the block, and returns 1.

A call naming another file than the one held loads it first. The readers save the one held before that only while `Profile_Dirty` is set; the writer saves it in any case. A call finding `Profile_FileName` empty loads its file.

## Reading a value

The readers take Windows' arguments in reverse, the functions removing their own ([calling conventions](compiler.md#calling-conventions)). A section is the first line beginning `[section]` and runs to the next line beginning `[`; a key is the first line in it beginning `key=`. Both comparisons ignore case and test only the pattern's length (`Str_CompareNoCaseN`), so `[Music]x` is the section `Music` and spaces around `=` are not allowed.

- **`Profile_GetInt` (`00050ffc`)**, (file, default, key, section): `atol` of what follows the line's first `=`, or default when the section or the key is missing, or when section, key or file is null.
- **`Profile_GetString` (`000511a4`)**, (file, size, buffer, default, key, section): copies what follows the line's first `=` into the buffer and returns its length. With a null key it copies the section's lines whole, each ended by a 0, and returns their length less 1; when they reach size − 1 bytes it ends the buffer with two 0 bytes and returns size − 1. When the key or the section is missing, or section or file is null, it copies default and returns its length. Only the section listing is held to size.

## Writing a value

**`Profile_WriteString` (`00051730`)**, (file, value, key, section): returns 0 for a null file. Once the file is held:

1. With section, key and value all null, it returns `Profile_Save`(file).
2. With a null section or a null value it returns 0.
3. It sets `Profile_Dirty`. With a null key it returns `Profile_DeleteSection` (`0005155c`) of `[section]`, which unlinks the section's header and lines from the list without freeing them, and returns 1.
4. It finds the section's header with `Profile_FindOrAddSection` (`0005165c`), which appends one at the end of the list, its text at `Profile_TextEnd`, when there is none, and looks for `key=` among the lines after it.
5. With no such line it writes `key=` and the value at `Profile_TextEnd` and links the new line after the last line it looked at. With one whose value is at least as long as the new, it clears the old value's bytes (`00072ecc`) and copies the new over them; with a shorter one it writes the whole line anew at `Profile_TextEnd` and puts its node in the old one's place.
6. It returns 1 while more than `0x95` bytes are left between `Profile_TextEnd` and the block's end, and otherwise `Profile_Save`(file), so the file goes to disk and the next call loads it again with 2000 bytes to spare.

Step 2 returns before the branch of step 5 that would remove a key for a null value can run.

## The dongle

The messages name a `Hasp`. `Dongle_IsOk` (`00051bf0`) builds a dongle object on its stack (`Dongle_Ctor`, `0007303c`, a 1-byte object), returns 1 when `Dongle_Check` (`00073078`) answers 0, and destroys it (`Dongle_Dtor`, `00073058`). `Dongle_Check` calls `00082698` twice, C convention, with a service number, the seed `0x13d` (`0009dec0`), 0 (`0009dec4`), two passwords and four answer pointers:

1. service 1 with passwords 0 and 0; it answers 1 when the first answer is 0;
2. service 2 with the passwords `0x6fc3` and `0x7b7f` (`0009decc`, `0009ded0`); it answers 0 when the four answers are `0xcb57`, `0x753c`, `0x648e` and `0xc042`, and 2 otherwise.

`Dongle_PrintStatus` (`00051b30`), (status), prints the message for a status with `printf`, from a `REPNE SCASD` table at `00095e40`:

| Status | Message |
|---|---|
| −999 | `Invalid Dongle Service requested` |
| −5 | `Write to dongle failed` |
| −4 | `Attempting Memo operations on a non-memo Hasp` |
| −3 | `Hasp does not match given password` |
| −2 | `Invalid Address given to dongle` |
| −1 | `Dongle Timed-Out` |
| 0 | `Dongle is okay` |
| 1 | `Dongle is Missing` |
| 2 | `Dongle has the wrong Password` |
| any other | `BOGUS STATUS CODE %d` |

`Dongle_VersionString` (`00051c28`), frameless, decodes a string into `000b1eec` with `00072faa` and returns it. The key is the dword `0009de3a`, 1234567, as its seven decimal digits *d*₁…*d*₇; byte *i* of each seven-byte run of `0009de3e` is that run's byte *dᵢ* − 1 less the character code of *dᵢ* and less 9, which with this key gives `Dongle Version!`.

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `Profile_Load`, `Profile_GetInt`, `Profile_GetString` | `00050e20`, `00050ffc`, `000511a4` | |
| `Profile_Save`, `Profile_DeleteSection`, `Profile_FindOrAddSection`, `Profile_WriteString` | `00051478`, `0005155c`, `0005165c`, `00051730` | `Profile_Save` is entered at its padding `00051476` by the readers |
| `Dongle_PrintStatus`, `Dongle_IsOk`, `Dongle_VersionString` | `00051b30`, `00051bf0`, `00051c28` | `Dongle_PrintStatus`'s case blocks and `Dongle_VersionString` are undefined bytes in Ghidra |
| `Dongle_Ctor`, `Dongle_Dtor`, `Dongle_Check` | `0007303c`, `00073058`, `00073078` | in the library region |

## Open

- **Open:** whether `00050e20`–`00051f1b` is one object file or three; no assert names a source file in it.
- **Open:** no reference to `Profile_WriteString`, `Dongle_IsOk`, `Dongle_PrintStatus` or `Dongle_VersionString` found by `es2_xref.py`; so whether the shipped simulator writes a profile or asks for the dongle at all.
- **Open:** `00082698`, the call `Dongle_Check` makes, and `00072faa`, the decoder, which lies inside the extent Ghidra gives `strchr`.
- **Open:** what a file name of 14 characters or more does to `Profile_FileName`, which `Profile_Load` copies whole into the 14 bytes before `Profile_Lines`; `Path_ApplyAltDrive` builds the name `Sim_StartMission` passes.
- **Open:** which functions `GO.EXE`'s `ShellSound_Init` reads `resource.cfg` through; `es2_naming.py match ES1DBSIM ES1GO` pairs none of these.
