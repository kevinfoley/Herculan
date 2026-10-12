# .BND — per-subsystem tuning files

ES1's volumes hold `.BND` files as ES2's do: 63 in `SIMVOL0.VOL`'s `bnd\` and 8 in `SHELL0.VOL`'s `BND\`. ES2's simulator reads none of its own ([ES2's `.BND`](../../retail/formats/bnd-notes.md)). Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

## The extension names

Both programs keep their resource folder names as strings each followed by a pointer to it, the pointers being what the code pushes. `ES1DBSIM`'s `bnd` is at `000980bc`, its pointer at `000980c0`; `es2_xref.py` finds no reference to the pointer, where the same sweep finds the two pushes of `dmg`'s pointer `000980e0` (`000117e7`, `0001c73e`). `ES1GO`'s pointer to `bnd`, `00086808`, is pushed at eight places: `0000221b`, `0000603e`, `00006f03`, `00008ceb`, `0000922a`, `00010547`, `0001271b` and `00025643`.

## `bnd\CAM.BND`

20 bytes, where ES2's is 24 ([ES2's `CAM.BND`](../../retail/formats/bnd-notes.md#cambnds-full-24-byte-record)):

| Content offset | ES1 | ES2 |
|---|---|---|
| 0–3 | `F4 C4 FF 2D` | `36 D0 34 31` |
| 4–5 | 2500 | 2500 |
| 6–7 | 30000 | 30000 |
| 8–15 | `00 08 C0 00 00 04 50 00` | `00 08 C0 00 00 04 50 00` |
| 16–19 | `00 30 64 00` | `00 30 26 02` |
| 20–23 | — | 500, 8000 |

## Open

- **Open:** whether `ES1DBSIM` reads `bnd\CAM.BND` or any other `bnd\` file: no reference to its `bnd` pointer was found by `es2_xref.py`, and a path built another way was not searched for.
- **Open:** what the shell reads through its eight `bnd` pushes, and from which of `SHELL0.VOL`'s eight `BND\` files.
- **Open:** what ES1's `CAM.BND` fields mean.
