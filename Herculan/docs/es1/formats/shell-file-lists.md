# GO.EXE — the shell's animation and lip-sync lists

**The shell finds the files its animations and talking heads play through lists of file names.** `cfg\tm_files.dat` groups the `.TTM` animation scripts, and each `snc\<k>_FILES.DAT` names, for one talking head's text file, its lip-sync script and, per line of text, the line's lip-sync file and sound. Both are built from the shell's [name lists](bst-battle-sets.md#name-lists): a count byte and that many names of up to 13 bytes with their NUL, a count of 0 followed by one byte that is skipped. Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The animation lists

```
byte     list count
list count x name list
```

`TtmLists_Load` (`00011dfc`) opens `cfg\tm_files.dat` through `VolRStream_CtorFromPath` and reads the count into the byte `0007e714`. When it is not 0 it deletes the array at `0007e710`, when there is one, through `StrList_Dtor`, makes an array of that many 6-byte lists (`00011eae`), and reads each with `StrList_Read`. It closes and destroys the stream. `es2_xref.py` finds one call, from `0003ba47`.

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

`maybe_SncList_Load` (`000455dc`) opens the list through `VolRStream_CtorFromPath` and reads one byte into the low byte of the player's count word `+0xf`. When it is not 0 it deletes the record array `+0x11` (`StrList_Dtor`) and makes one of that many 6-byte records (`00045729`), reads the name with `Stream_ReadCString` (`00020154`) into a 32-byte buffer, closes the handle `+2` with `Ttm_Close` (`000469da`) when it is not −1 and opens the name with `Ttm_Open` (`0004680a`)(0, 0, `+0x15`, name) into `+2` ([the animation scripts](ttm-scripts.md#opening-and-closing)), sets its page to 0, `0x19` and 1 in turn with `Ttm_SetPage` (`000468fc`), running it with `Ttm_RunScript` (`000465da`) after each page the script accepts, sets page 1 once more, and reads each record with `StrList_Read`. It closes and destroys the stream. Which list a message's line takes, and the line number that picks the record, are in [which file and line](bin-string-tables.md#which-file-and-line).

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

- **Open:** which animations play the lists of `cfg\tm_files.dat`: what `0003ba47` does and what reads `0007e710`.
- **Open:** what `CO_FILES.DAT` is, which nothing found names.
- **Open:** what a talking head's script draws on each page, and so what a lip-sync frame shows ([the scripts' Open items](ttm-scripts.md#open)).
