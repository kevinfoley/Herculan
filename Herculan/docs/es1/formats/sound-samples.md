# DBSIM.EXE and GO.EXE — digital sounds

**ES1's sound effects, voice lines and spoken lines are bare sample data: each file is the sound and nothing else.** ES2's are RIFF WAVs ([ES2's sample format](../../retail/formats/sound-samples.md#sample-format)); ES1's `.SFX` and `.RAW` files carry no header, and both programs load a file whole and hand all of it to the sound driver as one sample. [The sound manager](../sound/sound-manager.md) loads and plays them through [the drivers](../sound/sound-drivers.md#the-driver); the simulator names its sounds in [its catalog](../simulation/sound-catalog.md#the-simulators-catalog) and the shell in [its table](../shell/sound.md#the-shells-names). The music, `.HMP`, goes through the same manager and is [`hmp-music.md`](hmp-music.md).

## Which files

| Archive | Folder | Files | What names them |
|---|---|---|---|
| `SIMSOUND` | `hmi\` | 52 `.SFX` | the simulator's catalog, `str\SOUNDS.STR`, as `hmi\<name>.sfx` ([The simulator's catalog](../simulation/sound-catalog.md#the-simulators-catalog)) |
| `SIMVOICE` | `simvoice\` | 147 `.SFX`, `P1_01000.SFX` … | `SpeechLine_Load`, as `simvoice\<name>.sfx` ([Voice lines](../simulation/sound-catalog.md#voice-lines)) |
| `SHELL0` | `res32\` | 3 `.SFX`, 14 `.RAW` | the shell's name table, which holds `BUTTON.SFX`, `XXXXXXXX.SFX`, `MENUCLSA.RAW`, `MENUOPN2.RAW` and `XXXXXXXX.RAW` ([the shell's names](../shell/sound.md#the-shells-names)) |
| `SPEECH` | `snc\` | 161 `.RAW` | the talking heads' lip-sync lists, each line played with its `.SNC` script ([loading a line](../shell/message-box.md#loading-a-line)) |

`ROSEY1.SFX` and eleven `res32\` `.RAW` files are not in the shell's table; what plays them is [Open](#open). ES2's `SIMVOICE.VOL` holds the same 147 squadmate clip names ([ES2's voice clips](../../retail/formats/sound-samples.md#voice-clips)).

## Layout

There is none: the file's content is the sample, and its size is the sample's length. The manager records the size the volume stream reports for the file, `SoundObject_LoadSample` reads that many bytes, and `DigiSound_Play` passes that length to the driver. No byte of the file is read as a field.

ES2's loader plays a file that is not RIFF as raw 8-bit unsigned mono at 11,025 Hz ([ES2's sample format](../../retail/formats/sound-samples.md#sample-format)).

## The banks

`SHELL0.VOL`'s `sos\` holds five files in the HMI library's own formats, which the sound manager hands to the library without reading a field: `MELODIC.BNK`, `DRUM.BNK` and `DONS.BNK`, 5,404 bytes each with `ADLIB-` from byte 2 ([the FM driver's banks](../sound/fm-driver.md#the-banks)), and `DRUM32.DIG` and `DRUM.DIG`, which open with `HMIDIGIP`, the drum device's bank ([the bank](../sound/drum-device.md#the-bank)). `DONS.BNK` is `MELODIC.BNK` up to byte 67.

The sound manager reads three of them whole ([loading the banks](../sound/sound-drivers.md#loading-the-banks)). The retail install's `SOS\` holds those three as loose files, which `VolRStream_Open` tries before the volume group ([`VolRStream`](streams.md#volrstream)), and no `SOUND.VOL`. A search of both images finds no name for `DONS.BNK` or `DRUM.DIG` ([Open](#open)).

## Open

- **Open:** what plays `ROSEY1.SFX` and the eleven `res32\` `.RAW` files the shell's table does not name. A lead for the `.RAW` files: a script's `TOKEN_HDS_LOAD` names index `0xe` with its string put through `TtmScript_BuildPath` (`ES1GO 00047138`), which leaves a `.RAW` name as it is ([the commands](ttm-scripts.md#the-commands)), so the `0xc21f` strings of the shell's `.TTM` scripts name the files the scripts play; no census of them has been run.
- **Open:** what reads `sos\DONS.BNK` and `sos\DRUM.DIG`. A case-insensitive search of both images for `dons` and the bank names finds `sos\melodic.bnk`, `sos\drum.bnk` and `sos\drum32.dig` in each, and no `dons` or `drum.dig`.
