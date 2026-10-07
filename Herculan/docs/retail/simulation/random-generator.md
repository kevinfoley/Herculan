# DBSIM.EXE pseudo-random generator — `Math_RandomNext` (`00492dd4`)

Reverse-engineered from `DBSIM.EXE` disassembly. All addresses are DBSIM.EXE virtual addresses.

A general math-library utility rather than a simulation subsystem: every randomised decision in the game comes out of here, from the load-time terrain material pass to the explosion's per-component roll. Its seeding, which makes every run draw the same stream, gets as much space below as the algorithm.

## The algorithm

An additive lagged Fibonacci generator over a 56-entry table of `short`s with two rotating byte cursors. One step is `table[i] += table[j]`, returning the new `table[i]`, then both cursors advance and wrap at 56 — the original's `== '8'` test. `i` is the state block's `+0x71` and `j` its `+0x70`.

`Math_RandomBelow` (`00492e18`) wraps it as `(next & 0x7fff) % bound` — the mask **drops the sign bit rather than taking an absolute value**, so a bounded draw is over the low fifteen bits, not the full sixteen, and is not quite uniform for a bound that does not divide `0x8000`.

Callers pass the state block's address and mask the result to the width they need: `& 0xfff` for the terrain material roll and for the explosion's per-component roll, `& 0x7f` for a beam node's jitter. The simulation's shared block is `0x4d261d`; sounds, messages and the cockpit's own effects draw on a [second generator](#the-presentation-generator) instead.

## Seeding — `Math_RandomSeed` (`00492d7c`)

**Seeding takes no input.** The state lives in BSS. `Math_RandomSeed` sets the two cursors to the literals `0x37` and `0x18`, `memmove`s 112 bytes from the static table at `004a6958`, and then calls `Math_RandomSeedNoOp` (`00492e3c`), which is `push ebp; pop ebp; ret`. It reads no clock and takes no value from its caller beyond the block's address.

`es2_xref.py` finds three calls to it. Two are in `Main_StaticInit` (`0045cbcd` and `0045cbd8`), one per block. The third is in `Math_RandomSeedAndSkip` (`00492da8`), a wrapper nothing in the image references: it seeds the block it is given, calls a second empty function (`Math_RandomSeedAndSkipNoOp`, `00492e41`), then discards `param_2` draws.

So the generator contributes no run-to-run variation: **DBSIM draws the same stream on every run**. The known wall-clock path into simulation state is `SimTickDelta`, whose measured 40 ms comes back as 41 or 42 under load and rescales every rate that tick (see [`dbsim-physics-notes.md`](dbsim-physics-notes.md)).

## The presentation generator

`0x4d268f` is the block directly after the simulation's — `0x4d261d + 0x72`, one 56-entry table and its two cursors — and a generator in its own right. The static initialiser `Main_StaticInit` (`0045cad8`) seeds the two back to back through `Math_RandomSeed` (`00492d7c`, called at `0045cbc8`–`0045cbd8`), so both start in the same state and diverge only through their own draws. Every consumer is presentation — sounds, messages, portraits, smoke, cockpit shakes and the sensor dropout — so none of them moves a simulation roll, and a consumer that never runs costs the simulation nothing either.

Its draw sites are the sixteen `PUSH 0x4d268f` in the image besides that seeding one:

| Site | Function | Draw |
|---|---|---|
| `004080ca` | `Explosion_Construct` | `Math_RandomBelow(0x32)`, discarded — the sound it then plays is the type record's `+0x24` plus 10 |
| `00409298` | `Smoke_Construct` (`0040923c`) | `next & 3`, the first of the four shape variants of smoke type `+0x28` that the emitter cycles through ([Open](#open)) |
| `0042f86f` | `TexPoly_RandomPointOnCircle` (`0042f860`), `TexPoly` vtable `+0x24` | `next`, a random angle; the point that far round a circle of the given radius ([Open](#open)) |
| `0042f9c7` | `TexPoly_SetupRings` (`0042f970`), `TexPoly` vtable `+0x20` | `next`, one random angle per ring ([Open](#open)) |
| `0043404e`, `004340cc` | `Cockpit_StartHitShake`, `Cockpit_HitShakeTick` | `(next & 0xffff) % 10`, the palette flash interval |
| `004340ea` | `Cockpit_HitShakeTick` | `(next & 0xffff) % 5`, the shake step |
| `00435cb5` | `PilotMessagePort_Post` (`00435c48`), the squad port's post | `(next & 0xffff) % variants`, drawn only for two or more |
| `00436a67` | `MessagePort_PickVariant` | the same |
| `00438d7f` | `PanelGauge_RollDuration` (`00438d6c`) | `(next & 0xffff) % (hi - lo) + lo`, not drawn when `hi == lo` — the [sensor dropout](../formats/cockpit-hud-widgets.md#sensor-dropout)'s spell lengths |
| `0044b138` | `HddGauge_PaintPilotFrame` | `next % 3`, discarded — [`../formats/heads-down-display.md`](../formats/heads-down-display.md#the-three-paints) |
| `0044b381` | `HddGauge_PaintScream` | `Math_RandomBelow(0x14)` |
| `0045db2f` | `LiftStart_Rise` (`0045d840`) | `(next & 0xffff) % 5`, the lift's closing shake step, once a frame for `0x1e` coarse ticks — [`mission-deployment.md`](mission-deployment.md#the-lift-start) |
| `0045dcfb` | `Sim_DeathFlash` (`0045dc34`) | `Math_RandomBelow(10)`, the same kind of shake step — [`../formats/cockpit-canopy-palette.md`](../formats/cockpit-canopy-palette.md#palette-module) |
| `00462753`, `004627ff` | `Sound_Play`, `Sound_PlayAt` | `Math_RandomBelow` over the sound's variation count |

## Open

- **Open:** the order in which DBSIM's draws fall within a tick. A roll's result depends on its position in the stream, so the draw order across the tick's subsystems decides every roll after the first.
- **Open:** what constructs `SMOKE`. `Sim_MainTick` ticks a list of them through `Smoke_Tick` (`004092dc`), each releasing a `SMOKE_BALL` every 200 ticks while its count lasts, but `es2_xref.py` finds no branch or pointer reaching `Smoke_Construct` or landing anywhere from `00409200` to `00409240`.
- **Open:** whether `TexPoly` is ever built. Its constructor, `TexPoly_Ctor` (`0042f700`), has no reference `es2_xref.py` finds, the class name appears only in its own RTTI record — not in the persistence name table beside `TSTexture4Poly` — and no retail `.DTS` names it.
- **Open:** whether DBSIM draws from the generator before a zone populates. The terrain scatter is the visible case, because any draw before it moves where the scatter lands.
