# `.STR` — simulator string tables

`simvol0\str\*.STR`. DBSIM keeps its UI text out of its code; every caption, readout label and alert line comes from one of these.

## Load

`SimStrings_LoadAll` (`00437598`) opens one file and issues a run of registration calls, each naming a destination pointer array in `.bss` and a count. Groups are consumed in strict file order, so a group's index is its position in that sequence. `STRINGS0.STR` registers 41 groups, the sixth (13 entries, `DAT_004d13e0`) being the MFD's captions.

The count in a registration is the **destination array's capacity, not the group's size** — group 23 is registered with 40 slots for 31 strings. Only the order is load-bearing.

## Layout

After the 9-byte VOL entry prefix; all integers little-endian:

```
int32 contentLength                  -- bytes that follow
repeat until contentLength consumed:
    int16 count                      -- strings in this group
    count x {
        int16 length                 -- includes the NUL terminator
        byte[length] text            -- NUL-terminated ASCII
        uint8 attributeCount         -- 0, 1, 7 or 8 in retail data
        byte[attributeCount] attributes
    }
```

`attributeCount` is self-describing, which makes the file walkable without the group table. It matches `SimStrings_LoadAll` passing a second destination array only for groups whose entries carry attributes and NULL for the rest.

**Verified byte-exact**: this shape consumes `STRINGS0.STR`, `SYSTEM.STR`, `COMMAND1.STR`, `PILOTS.STR` and `SOUNDS.STR` to their declared content lengths with zero slack, and each file's group counts reproduce the registration sequence in order.

Attribute use: `STRINGS0.STR` group 0 carries one byte per order; `SYSTEM.STR`'s computer messages carry eight — the message id, a queue priority, four display timings and the `CVM` voice clip that reads the line, decoded in [`cockpit-messages.md`](cockpit-messages.md#the-computers-messages); `SOUNDS.STR` carries seven — loop count, volume, preload, throttle divisor and the two rolloff distances, then a variation count. The sound module reads a tenth byte past them and treats bytes 7-9 as runtime scratch; see [`audio.md`](audio.md#the-sound-catalog--strsoundsstr).

## `STRINGS0.STR` groups

Groups referenced by decoded code:

| Group | Count | Contents |
|---|---|---|
| 0 | 18 | Squadmate orders, and the verb `Mech_ReceiveSquadOrder` switches on. Entries 0-8 are the MFD's FLASH COMM page and 10-17 the [F7] command display's; 6 and 9 are empty. The full table is in [`../simulation/ai-squadmates.md`](../simulation/ai-squadmates.md). |
| 1 | 3 | `RED1`-`RED3` |
| 2 | 1 | `OFFLINE` — what a destroyed mount's cockpit weapon row prints in place of its name |
| 3 | 1 | `" POD"` — appended to a pod row's name, giving `" SHIELD POD"` |
| 4 | 4 | Console button captions: `I`, `LINK`, `TRACK`, `` (entry 0 unused — the chain button's numerals come from a separate `.rdata` table, `ChainCountCaptions` at `0049c71c`) |
| 5 | 13 | MFD captions: `STATUS`, `FLASH COMM`, `NAV MAP`, `SCANNER`, `TARGET`, `MISSILE CAM`, `MODE`, `SELECT`, `RANGE`, `TARGET`, `XMIT`, `PASS`, `ACTIVE`. Entries 0-5 are the screen titles, 6-12 the aux button captions. |
| 8 | 1 | `HQ` — the name the pilot and squad channel signs a speakerless line with ([`cockpit-messages.md`](cockpit-messages.md#its-speakerless-set)) |
| 9 | 3 | `XMIT`, `CANCEL`, `EXIT` — the Heads-Down Display's transmit buttons |
| 10 | 5 | `OK`, `INT DMG`, `SHLD DWN`, `CRITICAL`, `WASTED` — not the condition table, which is group 28; no reader of `DAT_004d1440` other than `SimStrings_LoadAll` has been found ([`mfd.md`](mfd.md#open)). |
| 11 | 2 | `MAP`, `DAMAGE` — the Heads-Down Display's page-0 title |
| 12 | 3 | `" STRUCT DAMAGE"`, `" INTERN DAMAGE"`, `" WEAPON DAMAGE"` — its page-1 title, indexed by damage category rather than by page |
| 13 | 19 | Structural component names, walker variant |
| 14 | 19 | The same list for a flyer, selected by the subject type's `+0x50`. Only 6 slots are filled (`COCKPIT ARMOR`, `L`/`R NACELLE ARMOR`, `FUSELAGE ARMOR`, `L`/`R WING ARMOR`); the other 13 are empty strings, so the group stays index-compatible with group 13 |
| 15 | 12 | Internal system names, walker variant |
| 16 | 12 | The same list for a flyer — group 15 with the two leg servos replaced by `L`/`R WING SERVO` and the two trailing rear-leg slots blanked |
| 17 | 1 | `YOU` — the name on the external view's caption when the player's machine is the one viewed ([`../simulation/external-views.md`](../simulation/external-views.md#what-the-external-view-shows)) |
| 19 | 2 | `NO TARGET SELECTED`, `NO INFO AVAILABLE` — the damage screen with no subject |
| 20 | 3 | `ID:`, `TARGET:`, `DIST:  ` |
| 21 | 1 | `STATUS:` |
| 22 | 16 | Herc type names — the player-side roster only, and **not** what the status screen prints; that takes the machine's own type-record name |
| 23 | 31 | Structure type names, indexed by `BASES.DAT +0x28` |
| 24 | 4 | Vehicle type names, same index for a type whose `BASES.DAT +0x32` is set |
| 25 | 2 | `LANDSKIMMER`, `HOVERTANK` |
| 26 | 1 | `NONE` — the MFD target screen with nothing selected |
| 27 | 1 | `UNKNOWN` — a subject whose target class the status screen does not recognise |
| 28 | 5 | Condition, the MFD status screen's fourth label: `OK`, `SHIELDS DN`, `INT DAMAGE`, `CRITICAL`, `DESTROYED` |
| 29 | 2 | `ACT`, `PASS` — consumer not located; the scanner's own toggles caption from group 5 |
| 30, 31 | 1,1 | `TRG:` and `RNG:`, the scanner's two corner captions (`DAT_004d16b4`/`b8`) |
| 33 | 2 | `STATUS:` and `OBJECTIVE:`, the squad comm box's two fixed captions |
| 36 | 4 | `VIEW: `, `CONTROL: `, `CAMERA`, `HERC` — the external view's caption ([`../simulation/external-views.md`](../simulation/external-views.md#what-the-external-view-shows)) |
| 37 | 2 | `ATT` and `"WAYPOINT "` (trailing space) — the Automatic Turret Tracking legend and the waypoint indicator's caption prefix, both of them the gunsight complex's ([`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex)) |
| 38, 39 | 1,1 | `TIME:`, `SPEED:` — the gunsight readouts |
| 40 | 8 | Squad comm box's current-order line: `ATTACK`, `TRAVEL`, `PATROL`, `FORM UP`, `GUARD`, `FLEE`, `DEAD`, `IMMOBILE` |

Other files: `SYSTEM.STR` the cockpit computer's 63 messages ([`cockpit-messages.md`](cockpit-messages.md#the-computers-messages)), `PILOT0/1/2/4.STR` what a squadmate can say, keyed the same way but with seven attribute bytes and live variants ([`cockpit-messages.md`](cockpit-messages.md#the-pilot-and-squad-channel)), `COMMAND0.STR` the three lines a mission action can post and `COMMAND1`-`4.STR` the training missions' instructor scripts, both on the pilot and squad channel ([`cockpit-messages.md`](cockpit-messages.md#its-speakerless-set)), `PILOTS.STR` 36 pilot surnames indexed by a machine's own pilot index ([`heads-down-display.md`](heads-down-display.md#squad-comm-boxes)), `SOUNDS.STR` a 57-entry sample catalog ([`audio.md`](audio.md)).
