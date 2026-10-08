# HERCULAN Engine — Planning

Living planning document for the Earthsiege 2 engine port: architecture decisions and their rationale. This is a working document, not a spec — update it as decisions change.

Implementation history (what shipped, when, and why) is **not** kept here — it lives in git log and the per-topic docs under `docs/retail/simulation/`, `docs/retail/rendering/` and `docs/retail/formats/`, which are the canonical reference for any given subsystem's reverse-engineering and porting detail.

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

## Earthsiege 1

Three questions decide how much of the engine Earthsiege 1 can share with Earthsiege 2 ([the ES1 plan](plan-es1-support.md)). The answers:

- **Compiler.** Symantec C++, not Borland ([`compiler.md`](../es1/formats/compiler.md)). Its objects are byte-packed with the vptr after the subobjects' data, so no ES2 object layout carries over as a layout.
- **The modules both simulators name** (`bullet`, `collide`, `damage`, `debris`, `flyersys`, `mechsys`) share a skeleton, the same functions with the same order of operations, and differ in record and table sizes, instance layouts, features ES2 added to the skeleton, and one change of meaning, a bullet's age ([`shared-modules.md`](../es1/simulation/shared-modules.md)).
- **The fixed-point math.** The hand-written assembly library and its trigonometry tables are ES2's; the fixed-point primitives beneath the simulation round where ES2's truncate ([`fixed-point-math.md`](../es1/simulation/fixed-point-math.md)).

So ES1 does not run on ES2's simulation code with ES1's data swapped in. Common to both are the asset layer, the assembly math library and its tables. How the two games share code is [Open](#open).

## Where missing and divergent behaviour is tracked

[`../../ROADMAP.md`](../../ROADMAP.md) is the single list of what the engine does not implement yet. Behavioural divergences — implemented but wrong — stay in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

How divergences get *found* rather than recorded is [`plan-differential-harness.md`](plan-differential-harness.md): a proposal to run retail and this engine over the same mission and diff their state tick by tick, which reaches the classes of divergence playtesting cannot.

## Open

- **Open:** how the two games share code — a shared core with one assembly per game, or version branches — decided from [Earthsiege 1](#earthsiege-1) and the survey's remaining per-subsystem verdicts.
