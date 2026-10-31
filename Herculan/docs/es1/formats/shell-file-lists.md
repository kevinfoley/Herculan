# GO.EXE — the shell's animation and lip-sync lists

**The shell finds the files its animations and talking heads play through lists of file names.** `cfg\tm_files.dat` groups the `.TTM` animation scripts, and each `snc\<k>_FILES.DAT` names, for one talking head's text file, its lip-sync script and, per line of text, the line's lip-sync file and sound. Both are built from the shell's [name lists](bst-battle-sets.md#name-lists): a count byte and that many names of up to 13 bytes with their NUL, a count of 0 followed by one byte that is skipped. Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The animation lists

```
byte     list count
list count x name list
```

`TtmLists_Load` (`00011dfc`) opens `cfg\tm_files.dat` through `VolRStream_CtorFromPath` and reads the count into the byte `0007e714`. When it is not 0 it deletes the array at `0007e710`, when there is one, through `StrList_Dtor`, makes an array of that many 6-byte lists (`00011eae`), and reads each with `StrList_Read`. It closes and destroys the stream. `es2_xref.py` finds one call, from `Shell_OpenFirstScreen` (`0003ba47`). `ShellBackdrop_OpenGroupTtms` (`00011f19`), which every backdrop change calls, reads `0007e710` and `0007e714` and opens the names of list (group / 1000 − 1) ([the group's scripts](../shell/menus.md#the-backdrop)).

The retail file holds seven lists:

| List | Names |
|---|---|
| 0, 1 | none |
| 2 | `C0_BRD.TTM`, `C0_BRJ.TTM`, and the same for `C1` to `C3`: 8 |
| 3 | `ELEV_L.TTM`, `ELEV_R.TTM`, `NUMBERS.TTM`, `SOUNDS.TTM`, … `TOMWELD1.TTM`, `COLWELD1.TTM`: 25 |
| 4 | `C0_BLNK.TTM` to `C3_BLNK.TTM` |
| 5, 6 | none |

Each empty list is its count 0 and a skipped byte 0.

## The lip-sync lists

```
byte     line count
if line count > 0:
  name of up to 32 bytes with its NUL     -- a .TTM script
  line count x name list
```

`maybe_SncList_Load` (`000455dc`) reads a list when a message's line is loaded ([loading a line](../shell/message-box.md#loading-a-line)); which list a message kind takes, and the line number that picks the line's names, are [the kinds](../shell/message-box.md#the-kinds)'.

| File | Script | Lines |
|---|---|---|
| `C0`–`C3_FILES.DAT` | `C<k>_SYNC.TTM` | 9 each |
| `D0`–`D3_FILES.DAT` | `D<k>_SYNC.TTM` | 8 each |
| `G0_FILES.DAT` | `G0_SYNC.TTM` | 55 |
| `T0_FILES.DAT` | `T1_NU.TTM` | 43 |
| `T1_FILES.DAT` | `T1_AN.TTM` | 43 |

Every line's list holds two names, a `.SNC` and then a `.RAW` (`C0_01000.SNC`, `C0_01000.RAW`): the line's [lip-sync script](snc-lip-sync.md#the-shells-scripts) and its sound. `D0_FILES.DAT`'s lines name `C0_10000.SNC` to `C0_13000.SNC`, its last two the same pair, and `D3_FILES.DAT`'s open with `C3_1…`; `T0_FILES.DAT` and `T1_FILES.DAT` differ only in their script's name. The line counts are those of the matching `ENG\<k>_TEXT.BIN` but for `T1`, whose text file holds 41 lines ([the dialog text files](bin-string-tables.md#the-dialog-text-files)); there is no `T0_TEXT.BIN`.

`SNC\CO_FILES.DAT`, with the letter O, is 221 bytes: `01 0A`, then `C0_SYNC.TTM` and nine two-name lists naming `C01000.SNC`, `C01000.RAW` to `C09000.SNC`, `C09000.RAW`. Read by `maybe_SncList_Load`'s rule it is one line under a script name that begins with the byte `0A`, with bytes left after it. A byte search of `GO.EXE` and `DBSIM.EXE` for `co_files` finds nothing, where the same search of `GO.EXE` finds `c0_files`, and the eleven names at `0008271c` do not include it.

## Open

- **Open:** what `CO_FILES.DAT` is, which nothing found names.
