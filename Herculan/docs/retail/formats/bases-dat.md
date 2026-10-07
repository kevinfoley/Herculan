# `dat\BASES.DAT` — the structure type table

One record per structure type, 65 in retail. Reverse-engineered from `DBSIM.EXE` (Ghidra project `ES2Recon`); addresses are DBSIM virtual addresses. What a structure does with these fields is [`../simulation/structure-behaviour.md`](../simulation/structure-behaviour.md), how the ground vehicle types move is [`../simulation/ground-vehicles.md`](../simulation/ground-vehicles.md), how a shot strikes one is [`../simulation/hit-detection.md`](../simulation/hit-detection.md#base_directfirehittest--00405038), and how a part comes down is [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#a-structure-coming-down).

## The type record

`Base_LoadResources` (`00405fac`) reads straight into a 60-byte struct offset by offset, so **the file's field order is the runtime record's field order**. The only divergence is the component array, inline on disk and a pointer at `+0x14`.

It reaches the file through a plain-file-read helper with no container header, so the first bytes of `dat\BASES.DAT` are its own content: an `int16` record count. A copy pulled out of a VOL to disk instead keeps that container's nine-byte entry prefix, which is what makes a naive parse of one land a few bytes into the wrong field.

| Offset | Type | Meaning |
|---|---|---|
| `+0x02` | `int16` | shape index into the selected library |
| `+0x04` | `int16` | wreck type index into `dgs\BHULKS.DGS`, `-1` for none |
| `+0x06` | `int16` | how many animation threads the constructor builds — [`structure-behaviour.md`](../simulation/structure-behaviour.md#the-animation-threads). The eight types that state a non-zero count are exactly the eight drawn from `dts\BASES_AN.DTS` rather than `dgs\BASES.DGS`, but the library is picked per case in the switch and not from this field |
| `+0x08` | `int16` | whole-structure fire shape, `-1` for none |
| `+0x0a` | `int16`×3 | where that fire sits, in the structure's own frame |
| `+0x10` | `int16` | whole-structure death sequence, used in place of the last part's own |
| `+0x12` | `int16` | component count |
| `+0x14` | array | components, 30 bytes each ([below](#the-component-record-30-bytes)) |
| `+0x1e` | `int16` | non-zero = invulnerable (types 21, 22, 23) |
| `+0x20` | `int16`×2 | playback rate for each of those threads — [`structure-behaviour.md`](../simulation/structure-behaviour.md#the-animation-threads) |
| `+0x24` | `int16` | idle cell-flipbook sequence, `-1` for none — [`structure-behaviour.md`](../simulation/structure-behaviour.md#the-plain-tick--base_thinktick-00403ca8) |
| `+0x26` | `int16` | that flipbook's frame interval, in the simulation's timer unit — [`dbsim-physics-notes.md`](../simulation/dbsim-physics-notes.md#timer-units) |
| `+0x28` | `int16` | MFD silhouette frame and type-name index |
| `+0x2a` | `int16` | body radius, vtable `+0x5c` (`Base_GetBodyRadius`, `004035a4`), and `+0x7c` for an animated type; four types state 0 — [`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-three-radius-slots) |
| `+0x2c` | `int16` | how far up the structure anything aiming at it aims, vtable `+0x30` (`0040351c`) — [`structure-behaviour.md`](../simulation/structure-behaviour.md#what-a-structure-is-aimed-at) |
| `+0x2e` | `int16` | what the type shoots: 0 unarmed, 1 gun, 2 launcher — [`structure-behaviour.md`](../simulation/structure-behaviour.md#the-armed-tick--00404100). Non-zero also marks a type the AI treats as dangerous — [`ai-combat-states.md`](../simulation/ai-combat-states.md#basesdat-0x2e) |
| `+0x30` | `int16` | non-zero installs [`BASECOL.DAT`](collision-spheres.md)'s model at runtime `+0x38` |
| `+0x32` | `int16` | texture bank selector |
| `+0x38` | ptr | runtime only: the installed `BASECOL.DAT` model. Null selects the volume hit path **and** makes the type immune to blasts |

Unread: `+0x00` and `+0x18` (6 bytes).

## The component record, 30 bytes

| Offset | Meaning |
|---|---|
| `+0` | max damage (retail 1000–30000) |
| `+2` | the cell-animation sequence this component drives, stepped to its collapsed cell when the part falls; `-1` for none |
| `+4` | which of the four death sequences this part runs, `-1` for none |
| `+6` | fire shape this part burns at the end of that sequence, `-1` for none |
| `+8` | debris group it throws as it collapses |
| `+0x0a` | `int16`×3 — where that debris and that fire come from. Its X and Y repeat `+0x10`'s on most types and its Z does not: this is up on the part, where `+0x10` is at its base |
| `+0x10` | `int16`×3 — the part's position in the structure's own frame, handed out by vtable `+0x58` (`Base_ComponentPosition`, `00406808`) and measured by the blast falloff. See [`../simulation/damage-system.md`](../simulation/damage-system.md#where-a-component-stands--the-0x58-slot) |
| `+0x16` | `int16`×3 — half-extents of the box the death sequence scatters smoke inside |
| `+0x1c` | `int16` — the part this one hangs off; destroying it takes every part naming it |

The four sequence-driven fields and the collapse they run are in [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#a-structure-coming-down).
