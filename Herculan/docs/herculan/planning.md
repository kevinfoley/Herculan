# HERCULAN Engine — Planning

Living planning document for the Earthsiege 2 engine port: architecture decisions and their rationale. This is a working document, not a spec — update it as decisions change.

Implementation history (what shipped, when, and why) is **not** kept here — it lives in git log and the per-topic docs under `docs/retail/simulation/` and `docs/retail/formats/`, which are the canonical reference for any given subsystem's reverse-engineering and porting detail.

## Context

Long-term goal: a modern, cross-platform engine capable of running Earthsiege 2 using the original game's data files. The "HercWorks" toolkit is a separate, already-underway toolkit for reading/editing those data files. See the scope assessment in project memory (`project-es2-engine-port-readiness`) for what RE work is and isn't done yet.

## Design principles

### Vanilla by default

All behavior matches the original game exactly by default. The only exceptions are purely cosmetic changes with no gameplay effect (e.g. resolution). Anything that changes behavior in the future — fixing bugs that exist in the original, raising limits, increasing mathematical precision, etc. — will be opt-in via a settings menu.


## Settled decisions

- Name: **HERCULAN Engine**

- Language: **C#**, not C++. The deciding factor is that `HercWorks.Core` already represents substantial, hard-won reverse-engineering work (file formats, DBSIM sim math) and is directly reusable from C#. Performance is a non-issue on modern systems.

- Runtime: **Modern .NET (8/9/10+), not Mono.** Mono was the historical answer for cross-platform C# (Xamarin, Unity, old MonoGame) but modern .NET has been natively cross-platform (win-x64/linux-x64/osx-arm64/etc.).

### Rendering

- Start with **OpenGL**, with an eventual goal of also supporting **Vulkan** (user-selectable backend).
- Bindings: **Silk.NET**
- Don't over-build the GL/Vulkan abstraction layer up front. An abstraction designed against a single backend tends to bake in assumptions (implicit state, no explicit sync) that don't map cleanly to Vulkan. Get OpenGL working concretely first; generalize the render interface when Vulkan support is actually being added and its real requirements are visible.

### Repo & project structure

- **Single repository** — the engine and the HercWorks toolkit share one tree.
- **Engine lives as sibling project(s) to `HercWorks.UI`**, under`Herculan/src/`, added to the existing `HercWorksMDK.sln`. Both the engine and the WinForms UI reference `HercWorks.Core` / `HercWorks.Vol`. `HercWorks.TransferApi` (UI-facing DTOs) is UI-only plumbing; the engine bypasses it and talks to `HercWorks.Core` domain types directly.

### Simulation object architecture

**Traditional OOP / virtual dispatch, matching the original — not ECS.** DBSIM.EXE's simulation objects are built on a shared base-object constructor helper (`SimObjectBase_Constructor`, `00402188`) called by every `SimObject`-derived class right after its vtable pointer is set. See [`sim-object-layout.md`](../retail/simulation/sim-object-layout.md).

Plan: a `SimObject` abstract base class in the engine with virtual overrides mirroring the discovered vtable shape (Mech, Rocket, Bullet, Flyer, ...), rather than a component/system model.

This decision is scoped to simulation objects specifically. Rendering/scene representation is a separate question and isn't required to follow the same pattern.

### Physics

**Custom, exact match to the original.** Not adopting an off-the-shelf .NET physics library (e.g. BepuPhysics) — the goal is to reproduce DBSIM's actual behavior, which has already been substantially reverse-engineered. The topic docs under [`docs/retail/simulation/`](../retail/simulation/) — locomotion, flight, hit detection, damage — are the porting target for the physics/sim subsystem, not a reference to design against; the shared fixed-point math and the tick timing they build on are in [`dbsim-physics-notes.md`](../retail/simulation/dbsim-physics-notes.md).

### Math

**Custom, exact match to the original** — port the actual fixed-point math toolkit found in DBSIM (Q8/Q10/Q14 fixed multiply, "integrate a rate over one tick," rate-limited "move toward," the sqrt-free fast 3D magnitude approximation) rather than using floating-point `System.Numerics` throughout.
- **Nice-to-have, not required for v1:** architect this behind an abstraction so the engine could later switch to modern floating-point math without a large rewrite. Apply the same caution as the rendering-backend abstraction above — don't design the swap layer in detail before there's a second implementation to validate it against.

### Audio

**OpenAL via Silk.NET.**

### Target platform

Primary development/testing target is **Windows**, but OS-specific code paths should still be abstracted from the start (consistent with the modern-.NET cross-platform decision above and Silk.NET's cross-platform windowing) so Linux/macOS support doesn't require rework later.

### Engine internal architecture

- **Library core + thin front-end host.** Engine subsystems (rendering, scene, etc.) should be built as libraries with no baked-in assumption that there's exactly one game loop consuming them. A separate, minimal host project wires those libraries into an actual real-time game loop.
- Motivation: a possible future mission editor that renders the mission environment in-engine.
- The namespace layout, the move of game rules out of the host, and the staged refactor toward both are in [`plan-architecture-refactor.md`](plan-architecture-refactor.md).

## World scale

**1000 world units are 6 metres.** `WorldScale.WorldUnitsPerMeter` is `1000/6` ≈ **166.667**. This is not an estimate — it is the original's own constant.

**Where it comes from.** The HUD prints distances to the player in metres, so DBSIM has to state its scale somewhere, and `Hud_WorldUnitsToMetres` (`00434228`) is the whole of it:

```
metres = (worldUnits / 1000) * 6
```

Three call sites in two unrelated gadgets share it — the HUD waypoint indicator's `WAYPOINT n: d M.` string (`Hud_UpdateWaypointIndicator`, `0043c3e4`) and the scanner MFD's contact-range readout (`0043ebe0`/`0043eecc`) — and both hand it a raw difference of two world positions (`Vec2_Subtract` then `Math_FastMagnitude2D`), so its input really is world units. A gameplay screenshot showing `WAYPOINT 1: 72 M.` is consistent: 72 is a multiple of 6, which every value this function can produce must be.

**What the world measures at that scale:**

| | world units | metres |
|---|---|---|
| terrain cell (`CellShift` 14) | 16384 | 98.3 |
| retail zone (128 x 128 cells) | 2097152 | 12580 (12.6 km) |
| zone 504's highest ground | 23393 | 140 |
| drop pod landing blast radius | 3000 | 18 |
| mech death explosion | 2000 | 12 |
| rocket proximity warning | 40000 | 240 |
| SAMSON model, bounding box height | 2364 | 14.2 |

**DTS model units are world units.** Two fields of `dat\<mech>.DAT`, a file the sim reads in world units, carry values that are only meaningful as model-space measurements. COLOSSUS is the one retail mech whose model dips below model-space zero, to `-400`, and it is the one retail mech with a nonzero `RideHeight`: exactly `400`. A correction expressed in the same numbers as the model's own coordinates is a 1:1 unit relationship. The second field is `HitRadius`, the machine's hit-cylinder radius (see [`mech-locomotion.md`](../retail/simulation/mech-locomotion.md#mech-type-record)); it tracks chassis size across the fleet — 1500 for OUTLAW's 1700-unit model, 2500 for everything larger (2030–2575) — so it corroborates the scale, though as a size measure rather than a height. Nothing in the load path scales a model: `MechType_InitOne` hands DTS points straight to the shape instance.

At 166.667 u/m, HERC models measure 10.2m (OUTLAW) to 15.5m (OGRE), ~1.5x the manual's quoted stature (6.1m/10.4m) — bounding box (includes raised arms/antennae) vs. quoted height; weight-class ordering matches the manual exactly.

**Independent order-of-magnitude check:** HUD speed readout (`Mech_GetDisplaySpeedKph`, `0041bb3c`) = `speed * 315/1024`; against each mech's `SpeedForward` reproduces the manual's KPH: OUTLAW 325 → 100 (exact), SAMSON 190 → 58 (quoted 60), COLOSSUS 180 → 55, MAVERICK 285 → 88 (quoted 90). Tick rate was later resolved directly (25 Hz, `Time_BeginSimTick` (`004677bc`) — see [`dbsim-physics-notes.md`](../retail/simulation/dbsim-physics-notes.md)), confirmed independently by `mech-locomotion.md`'s root-motion speed verification.

Symbols: `Hud_WorldUnitsToMetres`, `Hud_UpdateWaypointIndicator`, `Hud_UpdateSpeedReadout` (`0043dc78`), `Mech_GetDisplaySpeedKph`, `Math_Q10Multiply`, `Math_Q16Multiply`, `Math_Q16Divide`, `Math_FastMagnitude2D`, `Math_MapRange`, `Time_GetCoarseTicks`, `Vec2_Subtract`, `Vec2_Magnitude`, `Vec2_DistanceBetween`.

## Where missing and divergent behaviour is tracked

[`../../ROADMAP.md`](../../ROADMAP.md) is the single list of what the engine does not implement yet. Behavioural divergences — implemented but wrong — stay in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

How divergences get *found* rather than recorded is [`plan-differential-harness.md`](plan-differential-harness.md): a proposal to run retail and this engine over the same mission and diff their state tick by tick, which reaches the classes of divergence playtesting cannot.
