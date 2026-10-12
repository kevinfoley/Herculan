# GO.EXE — the `.BIN` string tables

ES1's shell keeps its menu text, file names and spoken-message text in `.BIN` string tables, ES2's container ([ES2's `.BIN` string tables](../../retail/formats/weapons-dat.md#the-bin-string-tables)). 44 of `SHELL0.VOL`'s 54 and `ZONES.VOL`'s one are that container as ES2 has it. The ten `ENG\*_TEXT.BIN` (`C0`–`C3`, `D0`–`D3`, `G0`, `T1`) put an `int16` version word, 2, in front of it: they hold the lines of the talking-head messages, each line read with the lip-sync record of the same number. Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are VSHELL's.

## The table

`Text_LoadDataFile` (`00018755`) reads a table from a stream as VSHELL's `Text_LoadDataFile` (`0040828a`) does: an `int32` count into `+8`, an `int32` pool size into `+0xc`, count × 2 bytes of offsets into a new array at `+0`, and the pool into a new array at `+4`. It returns 1 when the stream status is 0 and 0 otherwise; ES2's asserts on a stream error, ES1's does not. `TextTable_CtorFromStream` (`00018695`) zeroes `+0`, `+4` and `+8` of a 16-byte table, allocating it when given none, and reads it; `TextTable_Dtor` (`000186db`) frees both arrays, and the table when bit 0 of its flags is set. `Text_GetString` (`00018717`) returns the pool plus the index's offset, with no bounds test.

## The dialog text files

| Bytes | Field |
|---|---|
| 2 | `int16` version, 2 |
| 4 | `int32` *n*, the count of lines |
| 4 | `int32`, the pool's size in bytes |
| 2 × *n* | `uint16` offsets of the lines from the pool's first byte |
| … | the pool: the lines, each with its NUL, in order |

| File | Lines |
|---|---|
| `C0`–`C3_TEXT.BIN` | 9 each |
| `D0`–`D3_TEXT.BIN` | 8 each |
| `G0_TEXT.BIN` | 55 |
| `T1_TEXT.BIN` | 41 |

### The load

`Dialog_LoadFromFile` (`00018c36`) is the loader of the dialog record at `0009afc8` for these files, as `Dialog_LoadConditional` is for the `.ENG` blocks ([Reading a block](eng-mission-text.md#reading-a-block)). It deletes the record's box (`Dialog_DeleteBox`, `00018d93`) and stream, opens the path through `VolRStream_CtorFromPath`, and reads the version word into `+4`. Version 0 makes a `DefaultDialogBox` (`DefaultDialogBox_Ctor`, `00019149`), which shows `This is a Default Dialog Box.` and reads nothing more; version 2 a `DigitalDialogBox`; any other version leaves no box. It closes the stream.

`DigitalDialogBox_Ctor` (`00019279`), given the record and two words *a* and *b*, builds the `DialogBox` base (`DialogBox_Init`, `00019b25`), stores the vtable `0007ef04`, and reads the table from the record's stream, which stands after the version word. It sets the font to `000aacda` and the box with `DigitalDialogBox_SetRect` (`000193ee`), copies line number word `000aad5c` into a new buffer at `+0x15`, lays the text out with `DialogBox_Layout` ([the dialog box](eng-mission-text.md#the-dialog-box)), starts the line's lip-sync with `maybe_SncList_PlayEntry` (`000453be`) on the record at `0009afd4` with *a* and *b*, and frees the table. One line of the file is shown per load.

`DigitalDialogBox_SetRect` rounds the word `000817c4` down to a thousand and looks it up in the four ints at `0007ef10`: 3000, 5000 and 4000 copy the 16 bytes at `0009ae64` to the box (`+0x05`) and set `+0x00` to 1; 7000 copies those at `0009aea4` and sets `+0x00` to 0 (code at `00019435` Ghidra left undefined); any other value leaves the box as `DialogBox_Init` left it. `ConditionalDialogBox_SetRect` uses other boxes for the same word ([the dialog box](eng-mission-text.md#the-dialog-box)).

### Which file and line

`DigitalDialog_ShowMessage` (`00037edd`) shows a message whose record has a nonzero byte at `+2`, by the kind word at `+0`. It builds the path with `sprintf`, sets the line number `000aad5c`, and calls `Dialog_LoadFromFile` with *a* and *b* chosen to name the lip-sync list:

| Kind | File | Line | *a*, *b* | Lip-sync list |
|---|---|---|---|---|
| 0 | `eng\C0_text.bin` | the word `000ab222` | 0, 1 | `C0` |
| 1 | `eng\C<k>_text.bin` | the word `000ab222` | 2*k*, 1 | `C<k>` |
| 2 | `eng\G0_text.bin` | a word of the table at `000814ec` | 0, 3 | `G0` |
| 3 | `eng\D0_text.bin` | 2 × `Mission_OutcomeGrade` (`00036837`, [its tables](bst-battle-sets.md#equipment-availability))(`000aad58`, `000aad5e`), plus 0 or 1 at random unless that call returns 1 | 1, 1 | `D0` |
| 4 | `eng\D<k>_text.bin` | as kind 3 | 2*k* + 1, 1 | `D<k>` |
| 5–11 | `eng\T1_text.bin` | `0008159c`[`000814e8`] when `000814e8` is nonzero; else `00037bf0`(kind), except for kind 7, which keeps the line set before | the word `000815a8`[line], 2 | `T0` or `T1` |

*k* is the `+0` word of a campaign of the shell's battle set (`000a02cc`, [battle sets](bst-battle-sets.md#battle-sets)), the campaign the word at `0009b241` + `0x1f7` × `0009b21f` indexes, or −1 when that index is out of range. Kinds 5–11 also call `00046948` twice.

`maybe_SncList_PlayEntry` (`000453be`) picks one of the eleven names at `0008271c`, `SNC\C0_FILES.DAT`, `D0`, `C1`, `D1`, `C2`, `D2`, `C3`, `D3`, `T0`, `T1` and `G0` (`_FILES.DAT`): name *a* when *b* is 1, *a* + 8 when *b* is 2, name 10 when *b* is 3. When a line is playing (`+0x19` set) it stops it with `maybe_SncList_Stop` (`00045556`). It loads the list with `maybe_SncList_Load` (`000455dc`, [the lip-sync lists](shell-file-lists.md#the-lip-sync-lists)) and passes the list's 6-byte record number `000aad5c` through `StrList_Get` (`00020254`) to `0004589c`.

## Open

- **Open:** what the words `000ab222`, `000814e8` and `000815a8` hold, what `00037bf0` computes, and which screens send each message kind (`DigitalDialog_ShowMessage`'s callers `0003837f` and `0003c1b7`).
- **Open:** what `0004589c` does with a line's record, and so how a line's lip-sync plays ([the lip-sync lists' Open](shell-file-lists.md#open)).
- **Open:** whether every line of a file is reachable: the line numbers above against each file's count.
