# `hb<i>\` / `db<i>\` — cockpit canopy art (`.HB0`–`.HB2`, `.DB0`–`.DB2`)

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses. Verified against retail data in `ES2/VOL/simvol0/{hb0,hb1,hb2,hba}/`.

The canopy bitmap a player HERC's cockpit is drawn with, one per view. `CockpitCanopy_LoadViewBitmap` (`00429c2c`) loads it. The view manager that calls it, and the `.HD`/`.ED` viewport cutout the art is blitted under: [`../simulation/cockpit-views.md`](../simulation/cockpit-views.md). How it is blitted and which palette its indices go through: [`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md).

## Canopy art — `.HB0`/`.HB1`/`.HB2` and `.DB0`/`.DB1`/`.DB2`

`CockpitCanopy_LoadViewBitmap` (`00429c2c`, `MECHVIEW.CPP:0x12e`).

No literal `"hb0"`/`"db0"` string exists anywhere in `DBSIM.EXE`. The folder name is built at runtime: the global folder literal `"dba"` (or `"hba"` when `VideoMode_PanelMode == 3`) is copied to a stack buffer and index 2 overwritten with an ASCII digit via `_itoa`, giving `db0`/`db1`/`db2` or `hb0`/`hb1`/`hb2`. Then `ResourcePath_BuildFolderName(hercName, buf)` → `ClassItem_LoadResource`. The same trick produces `ed<i>`/`hd<i>` from `"edg"`/`"hdg"` — see [`hd-ed-clip-regions.md`](hd-ed-clip-regions.md).

Files are Dynamix bitmap arrays ([`dfn-hfn-dci.md`](dfn-hfn-dci.md)) with one frame: `.DB*` 320x240 (76834 bytes), `.HB*` 640x480 (307234). The nine player hercs each have one per view, 27 of each; the extracted copies under `ES2/VOL/simvol0/` are 10 bytes longer for the VOL entry framing ([`vol-archive.md`](vol-archive.md#loose-files-on-disk-carry-no-prefix)).

`CockpitCanopy_FreeViewBitmap` (`00429de4`) releases one view's handle, also nulling slot 3 when freeing view 2. Used only when `CockpitArt_LoadOnDemand` (`004d2704`) is set — a low-memory mode that loads and frees per view switch rather than keeping all four resident.

### Known defect in the retail code

The `maybe_CockpitLayoutMode == 1` branch increments byte 2 of the **shared global** `"dba"` literal (`MOV ECX,[0x4a0a28]; INC byte ptr [ECX+2]` at `00429d3e`) rather than its local buffer. The follow-on load still uses the unmodified local buffer, so that branch loads `db0` twice and corrupts the global folder name for every later user. `004d25bc` is byte `+0x7c` of the video-mode block, which `Main_StaticInit` zeroes, and no write of 1 to it has been found ([Open](../simulation/cockpit-views.md#open)) — see [`../simulation/cockpit-views.md`](../simulation/cockpit-views.md#video-modes).

