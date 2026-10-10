# `snc\<NAME>.SNC` — portrait lip-sync scripts

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

**`.SNC` is not an audio format.** It is the frame timeline that animates the talking pilot portrait in a comm box while the matching `.wav` plays. `Snc_Load` (`00463270`) reads one into a speech slot; the comm box that plays it, and the portrait bank its frames index, are [`heads-down-display.md`](../simulation/heads-down-display.md#squad-comm-boxes).

556 files in `snc\` (in both `SIMVOL0.VOL` and `SIMSOUND.VOL`): twelve speakers `PA`-`PL`, 47 message names, `PA`-`PH` carrying 46 of them and `PI`-`PL` all 47 (`_03001` is theirs alone). **Each speaker has its own scripts:** the twelve copies of a message hold between 6 and 12 distinct scripts, and the four of `_03001` hold 3.

## Layout

After the 9-byte [`.VOL` entry prefix](vol-archive.md#the-per-entry-prefix--fixed-9-bytes):

```
int32  length            -- bytes that follow
length/2 x {
    int8  frame          -- index into the pilot<n>.DBA portrait bank
    int8  delta          -- coarse ticks until the NEXT event
}
```

The `0xff` terminator is **not in the file** — `Snc_Load` (`00463270`) reads the declared length into the slot's 100-byte buffer and appends `0xff` itself. With no script at all the buffer is just `0xff`, and the voice plays with the portrait held.

**Verified across all 556 files**: length always even, always `fileLength - 14`, never containing a `0xff` byte, 2-28 pairs (so at most 57 bytes, terminator included, in the 100-byte buffer). Frame values are 0-23 — matching the 24 talking-head frames at the head of a `pilot<n>.DBA` bank, described in [`heads-down-display.md`](../simulation/heads-down-display.md#the-gauge) — and deltas 2-74 ticks.
