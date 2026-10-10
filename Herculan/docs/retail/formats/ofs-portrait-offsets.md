# `ofs\PILOT<n>.OFS` — portrait frame offsets

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

One file per pilot portrait bank, twelve in retail, each placing the frames of the matching `dba\PILOT<n>.DBA` inside a squad comm box. `HddGauge_LoadPilotFrames` (`0044a7c0`) loads it with the bank; how the comm box and the MFD's copy of it draw the frames is [`heads-down-display.md`](../simulation/heads-down-display.md#the-gauge).

## Layout

No header and no count: a flat array of three-`int32` entries — `{ frameIndex, x, y }` — of which the loader reads a fixed 27, copying each pair to `gauge + frameIndex * 8 + 0x3d`. The pair is signed and in the bank's own 320-wide space: it is the frame's position inside the box.

## Retail files

Every retail file holds entries 0-26 in order, and `PILOT9.OFS` alone a 28th, for frame 27, that the loader does not read. The first 24 entries are the talking-head frames and share one offset per pilot in all twelve files but `PILOT2`, whose entry 23 differs. Entries 24-26 place the three frames after them — wide strips in ten banks, 1x8 placeholders in `PILOT9` and `PILOT10`.
