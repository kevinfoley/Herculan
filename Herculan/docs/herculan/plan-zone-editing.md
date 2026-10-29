# Plan — custom terrain zones in the mission editor

Letting a mission designer give a mission its own terrain: a heightmap imported from an image, saved with the mission, shared in its `.hcm` bundle, and played in HERCULAN. Sculpting terrain in the editor's 3D view comes later.

Steps 1 to 5 are implemented (`ZoneFiles`, `SavedMissions`, `MissionBundle`, `MissionFileLaunch` and `TerrainZoneLoader`, `TerrainImages` and `TerrainChanger`); sculpting is not.

## What a zone is

A zone is a 16-byte header and an 8-bit heightmap, the same Dynamix bitmap container the game uses for textures: [`../retail/formats/zone-terrain.md`](../retail/formats/zone-terrain.md). Ground textures come from the mission's theater and the per-cell material roll, so a new heightmap takes on any theater's look with no further data. HercWorks.Core reads and writes both files (`ZoneDatTransformer`, `DynamixBitmapArrayTransformer`) and holds them as one checked pair (`ZoneFiles`), and `TerrainZoneLoader` builds a `HeightGrid` from them.

## Decisions

- **A custom zone belongs to one mission.** Its files are named for the mission, as its `.ABT` about file is: `<MISSION>.DAT` for the 16-byte header and `<MISSION>.DBA` for the heightmap. A saved mission keeps them beside its `.MSN` in the install's mission folder; a bundle carries them as two more entries under the same names. Neither the editor nor an import ever writes them to the install's `DAT\` and `DBA\` folders: retail reads a zone there ahead of `ZONES.VOL` for every mission ([`../retail/formats/zone-terrain.md`](../retail/formats/zone-terrain.md#loose-zone-files)), so a file there is shared by every mission on that number.
- **A mission plays with its zone from its handoff.** A custom mission is played from the handoff its launch writes (`MissionFileLaunch.Write` and `WriteHeld`), and a bundle is never extracted (`CustomMissions`). The handoff carries the mission's zone beside its `script.dat`, under the zone's own file names (`zoneNNNN.dat`, `zoneNNNN.dba`) for the number the script plays on, and the simulator reads a zone from the handoff before the install's and the archives'. Each launch writes its own handoff, so two bundles that both use zone 9909 never meet.
- **The zone number is chosen automatically, from numbers no archive zone has, 0 to 9999.** Collisions between custom zones are harmless under the two decisions above. Keeping clear of the archive's numbers means a custom zone written into the install for a retail launch ([Open](#open)) never replaces a retail zone other missions play on. Numbers up to 9999 keep the 8.3 file names the retail zones have; DBSIM builds a longer name, but a 5-digit zone file has not been loaded in retail.
- **Grid sizes are the retail ones:** square, 128×128 or 256×256 cells, with the header's width and height shifts written to match the image, though retail reads the size from the image alone.
- **The first version imports and exports images.** Sculpting is a later stage ([below](#later-sculpting)).
- **A terrain image is a PNG or a raw 8-bit file, read without an image library.** The engine already wrote PNG (`PngEncoder`); its decoder (`PngDecoder`) sits beside it in HercWorks.Help, which takes no package for files from outside. A raw file is one byte a cell, the form terrain tools export as 8-bit raw. An image is taken as it is: a gray value is a height byte, black lowest, a 16-bit PNG by its high byte; one that is not 128 or 256 pixels square, or has a colored pixel, is refused with the reason rather than resized or converted. Exported, a heightmap is an 8-bit grayscale PNG, which reads back as the same heights.
- **Replacing a mission's terrain keeps the mission's place about the terrain's center.** Every terrain starts at the world's origin, so a terrain of another size would leave the mission kilometers from its center; every position moves by half the difference in size, in the same undo step as the terrain.
- **Replacing a mission's terrain keeps each flyer as high above the ground as it was.** Only a flyer reads its height from its position ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 5), so a flyer's position, and a flyer group's, rises or falls with the ground under it, in the same undo step as the terrain; everything else stands on the new ground as on any.

## Steps

Each step is one reviewed change, checked in the editor before the next begins.

1. **The zone pair in HercWorks.Core.** A type holding a zone's header and heightmap, built from an 8-bit height array, a cell size shift and a height scale, and checked on read: one image, square, 128 or 256 cells a side, the header 16 bytes with shifts matching the image, a cell size shift of 12 to 15 as in the retail zones, and a height scale within what the game's terrain arithmetic holds at that cell size shift ([`../retail/simulation/terrain-heightmap.md`](../retail/simulation/terrain-heightmap.md#the-height-scales-range)). Tests cover what a consumer relies on: a written pair reads back as the same heights and sizes, and a malformed pair is refused with a reason.
2. **Saved missions keep their zone.** `SavedMissions.Write` writes the pair beside the `.MSN`, with the same `.bak` copies; reading a saved mission finds it, and a pair that does not read keeps the editor from opening the mission and is why the Custom Missions window cannot play it; `CustomMissions.Files` lists it, so deleting a mission deletes its zone.
3. **Bundles carry their zone.** `MissionBundle` reads the two entries, named for the mission like its text and about file, under per-entry caps sized for a 256×256 heightmap, and refuses a pair that fails step 1's checks. File > "Export Mission Bundle..." writes them. Import keeps writing the bundle file whole, as now.
4. **Playing reads the zone from the handoff.** The handoff writers put the pair in the handoff folder, from the saved files or the bundle's bytes; `MissionScene`'s zone load takes a handoff zone first. This covers the game's Custom Missions window, the editor's own load into its scratch handoff, and the editor's Play.
5. **The editor imports and exports terrain.** The New mission window offers terrain from an image beside the zone list, with the cell size and the highest point (shown in meters, written as the height scale) to choose, previewed as the zone list's heightmaps are (`ZoneHeightmaps`). Mission Settings > Terrain opens a window for replacing a mission's terrain from an image, as one undo step that swaps the scene's terrain in place, and exporting its heightmap as an image.

## Later: sculpting

Brushes in the 3D view to raise, lower, smooth and flatten ground, editing the mission's own zone.

- The terrain mesh is rebuilt from the edited grid as the brush moves, over the cells it touched.
- An edit is one undo step in the editor's history, like any other edit.
- **Flattening under structures is off while sculpting**, so the designer sees the ground as painted. It runs again when sculpting ends, as the game runs it on load.
- Placed objects, routes and trigger areas stay on the ground as it changes.

## Open

- **Deferred:** launching retail from the editor: writing the handoff's `script.dat` and starting `DBSIM.EXE` from the command line, for a full retail install. A custom zone then goes into the install's `DAT\` and `DBA\` for the run.
- **Open:** whether a 5-digit zone file name loads in retail.
