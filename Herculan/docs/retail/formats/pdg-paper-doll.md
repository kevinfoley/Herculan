# `pdg\<NAME>.PDG` — paper-doll damage diagram

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

One file per chassis, 21 in retail, plus `pdg\WEAPONS.PDG`: a chassis' damage diagram — its three views, the regions in each that take a damage tint, and where the weapon icons go. `PaperDoll_Load` (`004379cc`, `pdamage.cpp`) reads a chassis file. What the cockpit draws from it is [`cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md#paper-doll); the two screens that show it are the MFD's status screen ([`mfd.md`](../simulation/mfd.md#viewport-and-condition-per-class)) and the Heads-Down Display's damage detail ([`heads-down-display.md`](../simulation/heads-down-display.md#damage-detail--page-1)).

## Views

`PaperDoll_Load` reads 3 views, each an origin/size pair plus a vector of `0x1c`-byte regions (`{int index; int left, top; int right, bottom; int colorId; int recolorMode}`).

Coordinates are authored in the 320-wide space and shifted by `VideoMode_X/YCoordShift`, with the bottom-right corner additionally `+1` in the 640-wide mode, so a region covers the full 2x2 device footprint of each source pixel. Region art comes from `{herc}.HBA`/`.DBA`, frame `n` for view `n`.

The two nameless fields are what makes a region a damage region:

| Field | Offset | Meaning |
|---|---|---|
| `colorId` | `0x14` | The colour the art drew that body part in — a [`COLORS.DAT`](colors-dat.md) id, resolved to a palette index in place at load. Retail uses 9, 12, 15, 20, 24 and 25 |
| `recolorMode` | `0x18` | [Recolour mode](../simulation/cockpit-hud-widgets.md#tinting). **Every retail region states 0**; modes 1-3 are unexercised |

## Hardpoint list

After the three views `PaperDoll_Load` reads one more vector, into the doll's `+0x54` (count) and `+0x58`: `0x14`-byte hardpoint entries, `x` and `y` shifted like the regions. What the cockpit builds from them is [Weapon icons](../simulation/cockpit-hud-widgets.md#weapon-icons).

| Offset | Field | Meaning |
|---|---|---|
| `0x00` | `x`, `y` | Anchor point, relative to the view's origin |
| `0x08` | `frameOffset` | Added to the weapon's icon index. 1 on OUTLAW's two side hardpoints, 0 everywhere else |
| `0x0c` | `alignment` | Which point of the icon lands on the anchor: bits 0-2 horizontal (1 left, 2 right, 4 centre), the rest vertical (8 top, `0x10` bottom, `0x20` centre). `0x24`, centred both ways, on every retail entry but OUTLAW's `0x22`, `0x21` and `0x14` |
| `0x10` | `blitFlags` | Handed to the blit. 0 on every retail entry |

## `pdg\WEAPONS.PDG`

`PaperDoll_InitTables` (`004378d8`) loads it once, together with the `weapons` sprite bank whose frames it sizes. That file has no views: it is an `int32` count and then one `{int32 width, int32 height}` per frame in the 320-wide space, shifted at load — fourteen 9x9 frames in retail.
