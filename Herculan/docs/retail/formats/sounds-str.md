# The sound catalog — `str\SOUNDS.STR`

The game addresses sounds by a small integer, 0-56. The mapping lives in `SOUNDS.STR`, a `.STR` string table (container layout in [`str-strings.md`](str-strings.md)) whose single group of 57 entries pairs a filename with a 7-byte attribute blob. `SoundCatalog_Load` (`00462448`) walks the group into three parallel arrays — names (`004d2b0c`), attribute pointers (`004d2bfc`), voice handles (`004d2cfc`). How a row is opened, played, placed and muted is [`../simulation/audio.md`](../simulation/audio.md#the-sound-catalog); the sample files the names resolve to are [`sound-samples.md`](sound-samples.md). Addresses are DBSIM virtual addresses.

## Attribute bytes

The code treats the blob as **ten** bytes. The file supplies seven; the last three are runtime scratch written in place, byte 8 by the loader and bytes 7 and 9 by the suspend and the request counter.

| Byte | Meaning |
|---|---|
| 0 | loop count — `Sfx_SetLooping`. `0` = loop forever, `1` = once, `n` = n times |
| 1 | volume, 0-100, applied as `Math_Q16Multiply(v, 65000)` |
| 2 | preload — nonzero caches the sample at startup instead of on first play |
| 3 | play requests per play (see [the play-request gate](../simulation/audio.md#the-play-request-gate)) |
| 4 | rolloff start distance, in units of 1024 world units. `0xff` becomes 5 |
| 5 | cutoff distance, same units. `0xff` becomes 100 |
| 6 | variation count — playing id *i* actually plays `i + rand(count)` when count > 1 |
| 7 | *runtime*: "was playing" flag, for suspend/resume |
| 8 | *runtime*: category volume percentage, initialised to 100 |
| 9 | *runtime*: play requests counted |

Because `.STR` attribute blobs point directly into the loaded file buffer, bytes 7-9 of one entry overlap the next entry's length field and first name byte. That is inert — every pointer is collected before the first write — and the four empty entries the file carries after the last real sound give the last one its slack.

`0xff` in bytes 4 and 5 means "use the default", not "not positional".

## The catalog

`vol`, `pre`, `thr`, `min`, `max`, `var` are attribute bytes 1, 2, 3, 4, 5, 6; `loop` is byte 0.

| id | File | loop | vol | pre | thr | min | max | var |
|---|---|---|---|---|---|---|---|---|
| 0 | `battle1.wav` | forever | 100 | 1 | 0 | - | - | 1 |
| 1-9 | `battle1.wav` | forever | 100 | 0 | 0 | - | - | 1 |
| 0x0a | `laser3h.wav` | 1 | 70 | 0 | 5 | 5 | 40 | 1 |
| 0x0b | `laser1.wav` | 1 | 50 | 0 | 2 | 5 | 40 | 1 |
| 0x0c | `impacts2.wav` | 1 | 70 | 0 | 2 | 0 | 15 | **3** |
| 0x0d | `impacts3.wav` | 1 | 70 | 0 | 2 | 0 | 15 | 1 |
| 0x0e | `impacts5.wav` | 1 | 70 | 0 | 2 | 0 | 15 | 1 |
| 0x0f | `missle.wav` | 1 | 98 | 0 | 2 | - | - | 1 |
| 0x10 | `xplmlt2.wav` | 1 | 50 | 1 | 3 | - | - | 1 |
| 0x11 | `gm_69.wav` | 1 | 90 | 1 | 1 | - | - | 1 |
| 0x12 | `bacann4.wav` | 1 | 90 | 0 | 3 | 5 | 15 | 1 |
| 0x13 | `start3.wav` | 1 | 90 | 0 | 0 | - | - | 1 |
| 0x14 | `bptslct.wav` | 1 | 90 | 0 | 1 | - | - | 1 |
| 0x15 | `trgloc.wav` | 1 | 90 | 0 | 2 | - | - | 1 |
| 0x16 | `trgunloc.wav` | 1 | 90 | 0 | 2 | - | - | 1 |
| 0x17 | `warn1.wav` | 5 | 30 | 0 | 0 | - | - | 1 |
| 0x18 | `wrnwoop2.wav` | 5 | 30 | 0 | 0 | - | - | 1 |
| 0x19 | `strcfail.wav` | 5 | 30 | 0 | 0 | - | - | 1 |
| 0x1a | `gnract.wav` | 1 | 80 | 0 | 1 | - | - | 1 |
| 0x1b | `gnrdact.wav` | 1 | 80 | 0 | 1 | - | - | 1 |
| 0x1c | `whitenz.wav` | 1 | 80 | 1 | 0 | - | - | 1 |
| 0x1d | `foot2.wav` | 1 | 98 | 1 | 4 | 1 | 20 | 1 |
| 0x1e | `callpsa.wav` | 1 | 68 | 0 | 2 | - | - | 1 |
| 0x1f | `callpsb.wav` | 1 | 68 | 0 | 2 | - | - | 1 |
| 0x20 | `plasma.wav` | 1 | 60 | 0 | 4 | 5 | 75 | 1 |
| 0x21 | `explo4.wav` | 1 | 60 | 0 | 3 | - | - | 1 |
| 0x22 | `explo5.wav` | 1 | 60 | 0 | 3 | - | - | 1 |
| 0x23 | `plsmahit.wav` | 1 | 99 | 0 | 4 | 1 | 50 | 1 |
| 0x24 | `explo7.wav` | 1 | 60 | 0 | 3 | - | - | 1 |
| 0x25 | `explos1.wav` | 1 | 80 | 1 | 3 | - | - | 1 |
| 0x26 | `explos2.wav` | 1 | 80 | 0 | 3 | - | - | 1 |
| 0x27 | `xplmlt4.wav` | 1 | 50 | 1 | 3 | - | - | 1 |
| 0x28 | `explo1d.wav` | 1 | 70 | 0 | 3 | - | - | 1 |
| 0x29 | `explo2.wav` | 1 | 70 | 0 | 3 | - | - | 1 |
| 0x2a | `explo3.wav` | 1 | 70 | 0 | 3 | - | - | 1 |
| 0x2b | `lsrhit2.wav` | 1 | 8 | 0 | 4 | - | - | 1 |
| 0x2c | `throtl.wav` | 1 | 100 | 0 | 0 | - | - | 1 |
| 0x2d | `herceng1.wav` | forever | 50 | 1 | 5 | 5 | 50 | 1 |
| 0x2e | `shield1.wav` | 1 | 80 | 1 | 4 | 0 | 25 | 1 |
| 0x2f | `podin2.wav` | 1 | 70 | 0 | 1 | - | - | 1 |
| 0x30 | `podland.wav` | 1 | 70 | 0 | 1 | - | - | 1 |
| 0x31 | `flyby1.wav` | 1 | 90 | 0 | 4 | - | - | 1 |
| 0x32 | `missin.wav` | 1 | 28 | 0 | 4 | 5 | 75 | 1 |
| 0x33 | `fire1a.wav` | forever | 40 | 0 | 4 | 0 | 25 | 1 |
| 0x34 | `ricup.wav` | 1 | 40 | 0 | 2 | 0 | 15 | 1 |
| 0x35-0x38 | *(empty)* | | | | | | | |

`-` is the authored `0xff`, i.e. the 5/100 defaults. The four empty entries have no attribute bytes at all and are never opened.

`0x33` is not the flamer: it is the burning-object loop, started by the first live [`FireEffect`](../simulation/destruction-effects.md#fire) and stopped by the last, and kept positioned on whichever fire is nearest the camera — see [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#where-the-shared-sound-is-heard) for how that one is picked.

This resolves the sound ids scattered through the other docs: `0x0b` is `laser1.wav`, the beam muzzle sound of [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md); `0x16` the target-lost tone of [`../simulation/missile-lock.md`](../simulation/missile-lock.md); `0x21` the drop-in lift's rumble ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-ride--liftstart_rise-0045d840)), which the turret's servo helpers test but never start ([`../simulation/torso-aim.md`](../simulation/torso-aim.md#the-servo-sound-helpers)); `0x2f` and `0x30` the drop pod's fall and landing in [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md). The `+ 10` seen at the three data-driven call sites — `record.SoundId + 10` from `BULLETS.DAT` (`Bullet_Fire`, record `+0x8`), `ROCKETS.DAT` (`Rocket_Fire`, `+0xc`) and `EXPLOS.DAT` (`Explosion_Construct`, `+0x24`) — is exactly the music/effects split ([`../simulation/audio.md`](../simulation/audio.md#ids-0-9-are-music)): those tables index the effects half of the catalog from zero.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Attribute byte 0 selects a mixer channel or category | Its three retail values (0, 1, 5) look like a small enum, but it is passed straight to `Sfx_SetLooping` as a repeat count — 0 means forever, which is why the music entries and `herceng1`/`fire1a` carry it. |
| Attribute byte 2 is "looping" | It is the preload flag; `Sfx_Cache` is a load call, not a play call. Looping is byte 0. |
