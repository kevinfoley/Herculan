# `dpl\<name>.DPL` — palettes

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses. Verified against retail data in `ES2/VOL/simvol0/dpl/`.

A colour table followed by a shade-ramp table. `Palette_LoadAndActivate` (`00430394`) loads one and makes it the live palette. Which palettes the simulator installs, and when: [`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#palette). The shell's: [`../shell/screen-layout.md`](../shell/screen-layout.md#the-palette).

## Layout

`COCKPIT.DPL` is a 256-entry palette (1050 bytes: 9-byte prefix, `0F 00 28 00`, size `0x408`, start index 0, count 256, 256 x 4 bytes). Entry layout is `[R][G][B][flag=1]`, 6-bit channels scaled x4 — entries 1-7 are the textbook VGA blue, green, cyan, red, magenta, brown and light grey at `0x2a`.

The prefix: [`vol-archive.md`](vol-archive.md#the-per-entry-prefix--fixed-9-bytes). The shade-ramp table follows the colour entries ([below](#the-shade-ramp-table)).

## The shade-ramp table

Immediately after the `colourCount * 4` colour entries; how the renderer uses a ramp is [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#poly-types-and-their-colour-mechanisms-dbsimexe). Read byte-complete on all 65 retail `.DPL` in `SHELL0.VOL` and `SIMVOL0.VOL`:

```
int32  rampCount              // 256 in 37 files; the other 28 store 0 and end there
rampCount x {
  int16  length               // retail: 1, 4, 7, 8, 13 or 16
  int16  paletteIndex[length] // darkest to brightest
}
```

Only the low ~19 slots carry real ramps; the rest is the degenerate `[255]`. `WORLD2`: ramp 0 is `196..203` (greys `#484848`..`#d4d4d4`), ramp 8 is `172..187` (blue-greys `#343444`..`#c4c4d4`), ramp 12 is `192..198` (near-black to `#707070`).
