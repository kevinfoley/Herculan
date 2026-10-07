# `fm\<NAME>.FM` — flight model parameters

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

One 54-byte file per chassis that flies. Two ship, `RAZOR.FM` and `SKIMMER.FM`, and two loaders read them straight into the chassis' type record:

| Loader | Reads it for | Into |
| --- | --- | --- |
| `MechType_InitOne` (`004201a8`) | a HERC-class type whose flyer flag (`typeRec+0x50`, file offset 78) is set — the RAZOR | `typeRec+0x1dc` |
| `FlyerType_LoadResources` (`00422ed0`) | every `Flyer`-class type — the SKIMMER | `flyerTypeRec+0x3a` |

`FlightModel_Step` (`00466a54`) is the only consumer of the block. What it does with each field is [`../simulation/razor-flight.md`](../simulation/razor-flight.md#control-law-flightmodel_step); the two aircraft share the model and differ only in these numbers.

## Layout

| Offset | Type | RAZOR | SKIMMER | Role |
| --- | --- | --- | --- | --- |
| 0 | i16 | 400 | 600 | Pitch rate cap, and the Q8 gain from full elevator |
| 2 | i16 | 1500 | 1400 | Roll rate cap, and the gain from full aileron |
| 4 | i16 | 400 | 600 | Yaw rate cap, and the gain from full rudder |
| 6 | i16 | 200 | 250 | Cap on the pitch command per tick |
| 8 | i16 | 400 | 300 | Cap on the roll command **and on the yaw command** |
| 10 | i16 | 100 | 100 | How fast airspeed closes on the throttle's demand |
| 12 | i16 | 0 | 0 | Not read by `FlightModel_Step` |
| 14 | i32 | 0 | 0 | Zero on disk; the loader writes the ceiling slope here — [below](#bytes-12-17-are-not-padding) |
| 18 | i16 | 500 | 500 | Q10 of each axis' rate bled off per tick |
| 22 | i16 | 16 | 16 | Right shift, attitude → self-levelling pitch command |
| 26 | i16 | 5 | 6 | The roll counterpart |
| 30 | i16 | 4 | 4 | Right shift, bank angle → heading rate |
| 34 | i32 | 60000 | 120000 | Flight ceiling at the top airspeed (42); the far end of a ramp, not a flat maximum |
| 38 | i32 | 6000 | 6000 | Flight ceiling at the idle airspeed (46), above ground level |
| 42 | i32 | 1500 | 1000 | Airspeed at full throttle |
| 46 | i32 | 250 | 500 | Airspeed at idle — a floor, not a stall speed |
| 50 | i32 | 300 | 400 | Q10 of the sideways and vertical velocity shed per tick |

The three shift fields are *read* by the flight model as 32-bit loads at offsets 22, 26 and 30, so each occupies four bytes; only the low half is ever non-zero, which is why they are listed as `i16` with the next two bytes skipped.

### Bytes 12-17 are not padding

Offsets 14-17 are a slot the file leaves zero and the *loader* fills in. Both loaders finish with the same division — `MechType_InitOne` into `typeRec+0x1ea`, `FlyerType_LoadResources` into `flyerTypeRec+0x48`, each the middle of the block it has just read:

```c
slope = Q16Divide(fm[34] - fm[38], fm[42] - fm[46]);   // fm[N]: the field at offset N
```

That is the ceiling's slope against airspeed (43.2 world units per unit of airspeed on the RAZOR, held as a Q16 ratio), so the field block is a mixture of file content and derived state. The ceiling it produces is [the flight ceiling](../simulation/razor-flight.md#the-flight-ceiling).

## Rejected readings

| Reading | Why it is wrong |
| --- | --- |
| The fields either side of the roll rate cap (2) are all roll parameters | The field order invites it, but only three concern roll. The angular damping (18) damps every axis, and the lateral drag (50) is sideslip drag applied to velocity rather than rotation |
| Bytes 12-17 are zero padding | They are zero *on disk*. The loaders write the ceiling slope into 14-17 |
