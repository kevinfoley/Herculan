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

`DigitalDialogBox_Ctor` (`00019279`), given the record and two words *a* and *b*, builds the `DialogBox` base (`DialogBox_Init`, `00019b25`), stores the vtable `0007ef04`, and reads the table from the record's stream, which stands after the version word. It sets the font to `000aacda` and the box with `DigitalDialogBox_SetRect` (`000193ee`), copies line number word `MessageBox_Line` (`000aad5c`) into a new buffer at `+0x15`, lays the text out with `DialogBox_Layout` ([laying out the text](../shell/message-box.md#laying-out-the-text)), starts the line's talking head with `maybe_SncList_PlayEntry` (`000453be`) on the player `Shell_LipSyncPlayer` (`0009afd4`) with *a* and *b* ([loading a line](../shell/message-box.md#loading-a-line)), and frees the table. One line of the file is shown per load.

Where the box stands by the backdrop, and how it is laid out, drawn and paged, is [the text box](../shell/message-box.md#the-text-box)'s; which file and line each kind of message loads, and the words *a* and *b*, are [the kinds](../shell/message-box.md#the-kinds)'.

## Open

- **Open:** whether every line of a file is reachable: the line numbers of [the kinds](../shell/message-box.md#the-kinds) against each file's count.
