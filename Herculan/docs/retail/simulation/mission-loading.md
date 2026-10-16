# The mission loading screen (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

While a mission loads, DBSIM shows one full-screen picture, chosen by the mission's theater and by whether the player pilots the RAZOR. `Sim_Run` (`0045f144`) calls `Sim_InitMissionSession` (`004614fc`) after its command-line parse and the subsystem phases 0 and 1, and that function puts the picture up before it loads anything of the mission and takes it down once the mission is built.

## Which picture

`Sim_ReadLoadingScreenChoice` (`004613b4`) reads two values from the mission handoff in `data\`:

| File | Read | Value |
|---|---|---|
| `data\script.dat` | the 20-byte header, keeping its first `int16` | the theater, 0-4 ([`../formats/script-dat.md`](../formats/script-dat.md#header-format)) |
| `data\player.mec` | the first five `int16`s, keeping the fifth | the player's machine type; 8 is the RAZOR in `MECHS.NAM` |

`Sim_InitMissionSession` builds the name from them: `ld_`, then `flyr` for the RAZOR and `herc` for any other machine, then the character `'1' + theater`. So a mission has one of ten pictures, `LD_HERC1` to `LD_HERC5` and `LD_FLYR1` to `LD_FLYR5`. Each file failing to open is an `INIT.CPP` assert of severity 5.

Every name ships three times in `SIMVOL0.VOL`:

| Folder | Holds |
|---|---|
| `dpl\` | the picture's own 256-colour palette |
| `dba\` | the picture at 320x240, one frame |
| `hba\` | the picture at 640x480, one frame |

## Showing it

In `Sim_InitMissionSession`, in order:

1. `Sim_CreateRenderContext`, `Win_PumpMessages`, `Screen_ClearToBlack`.
2. `Sim_ReadLoadingScreenChoice`, and the name built from it.
3. `dpl\<name>` loaded through `ClassItem_LoadFromFile` and handed to `Palette_UploadWhole`; `Win_PumpMessages`.
4. `Sim_ShowLoadingScreen` (`00461344`): loads `<name>` from `hba\` when `VideoMode_PanelMode` is 3 and from `dba\` otherwise ([`cockpit-views.md`](cockpit-views.md#video-modes)), blits frame 0 at the screen's origin, calls `Display_Present`, and frees the bitmap.
5. The load, under the picture: `Sound_Init`, `Light_ResetSystem`, subsystem phase 2, `Light_CreateMissionSun`, `Time_InitSimClock`, `DBSim_LoadScriptDat`, each object in the list at `004aaafc` handed to `Scene_SubmitObject`, `DBSim_SpawnMissionObjects`, `HddMap_BuildTerrainRaster`, `Win_PumpMessages`, the explosion and debris pools emptied, every machine stood on the terrain, and the squad cameras built.
6. `Win_PumpMessages`, `Screen_ClearToBlack`, `Win_PumpMessages`, and the rest of the cockpit's setup before the first frame.

`Sim_InitMissionSession` holds the picture up for no minimum time and draws it once: between steps 4 and 6 it makes no wait and no other present of its own, so the picture stays exactly as long as step 5 takes. What the step 5 callees draw, if anything, is [Open](#open).

## Open

- **Open:** whether any function step 5 calls presents to the screen, which would take the picture down early. Only `Sim_InitMissionSession`'s own body has been read for this.
