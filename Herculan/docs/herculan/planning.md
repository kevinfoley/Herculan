# HERCULAN Engine — Planning

Living planning document for the engine port: architecture decisions and their rationale. This is a working document, not a spec — update it as decisions change.

Implementation history (what shipped, when, and why) is **not** kept here — it lives in git log and the per-topic docs under `docs/retail/simulation/`, `docs/retail/rendering/` and `docs/retail/formats/`, which are the canonical reference for any given subsystem's reverse-engineering and porting detail.

## Context

Long-term goal: a modern, cross-platform engine capable of running Earthsiege 2 using the original game's data files. The "HercWorks" toolkit is a separate, already-underway toolkit for reading/editing those data files. See the scope assessment in project memory (`project-es2-engine-port-readiness`) for what RE work is and isn't done yet.

## Design principles

### Vanilla by default

All behavior matches the original game exactly by default. The only exceptions are purely cosmetic changes with no gameplay effect (e.g. resolution). Anything that changes behavior in the future — fixing bugs that exist in the original, raising limits, increasing mathematical precision, etc. — will be opt-in via a settings menu.


## Settled decisions

- Name: **HERCULAN Engine**, the one name for both games it runs: Earthsiege 2 and, as [Earthsiege 1](#earthsiege-1) sets out, *Metaltech: Earthsiege*. The brand promises each game played faithfully from its own data, not one simulation core under both.

- Language: **C#**, not C++. The deciding factor is that `HercWorks.Core` already represents substantial, hard-won reverse-engineering work (file formats, DBSIM sim math) and is directly reusable from C#. Performance is a non-issue on modern systems.

- Runtime: **Modern .NET (8/9/10+), not Mono.** Mono was the historical answer for cross-platform C# (Xamarin, Unity, old MonoGame) but modern .NET has been natively cross-platform (win-x64/linux-x64/osx-arm64/etc.).

### Rendering

- Start with **OpenGL**, with an eventual goal of also supporting **Vulkan** (user-selectable backend).
- Bindings: **Silk.NET**
- **The 3D view is painted in the original's order by default.** Retail has no depth buffer: it paints the terrain a cell at a time and each cell's objects whole after its ground ([`terrain-drawing.md`](../retail/rendering/terrain-drawing.md)), and a per-pixel depth test visibly differs from that (an impact effect half inside the building it struck, objects cutting through each other). `Render.WorldDrawPass` reproduces the order on the GPU; the per-pixel depth draw stays for the mission editor, whose overlays test against the scene's depth, and for the simulator as the "Per-pixel depth sorting" tweak.
- Don't over-build the GL/Vulkan abstraction layer up front. An abstraction designed against a single backend tends to bake in assumptions (implicit state, no explicit sync) that don't map cleanly to Vulkan. Get OpenGL working concretely first; generalize the render interface when Vulkan support is actually being added and its real requirements are visible.

### Repo & project structure

- **Single repository** — the engine and the HercWorks toolkit share one tree.
- **Engine lives as sibling project(s) to `HercWorks.UI`**, under`Herculan/src/`, added to the existing `HercWorksMDK.sln`. Both the engine and the WinForms UI reference `HercWorks.Core` / `HercWorks.Vol`. `HercWorks.TransferApi` (UI-facing DTOs) is UI-only plumbing; the engine bypasses it and talks to `HercWorks.Core` domain types directly.

### Simulation object architecture

**Traditional OOP / virtual dispatch, matching the original — not ECS.** DBSIM.EXE's simulation objects are built on shared base-object constructors (`SimObjectBase_Constructor`, `00402188`, and its shaped, animated and static variants), which a derived class's constructor runs first, installing its own vtable after. See [`sim-object-layout.md`](../retail/simulation/sim-object-layout.md).

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

So ES1 does not run on ES2's simulation code with ES1's data swapped in. Common to both are the asset layer, the assembly math library and its tables.

### How the two games share code

**A shared core with one assembly per game**, the shape ScummVM has: one application and one brand over many game engines that share almost no game logic, each in its own folder above a common backend.

- **The core** holds what does not depend on which game is running: the HercWorks readers, the 3Space asset classes (`TS*`, `GL*`, `AN*`), the assembly math library and its tables, and the platform, GL, render back end, audio back end, input devices, settings, install detection and launcher.
- **One assembly per game** holds that game's simulation, cockpit, shell, mission world and static `.DAT` tables. Today's `Sim`, `Cockpit`, `Shell` and `World` are ES2's; ES1's are ported fresh beside them. Host code that wires one game's screens goes with that game.
- **One application.** It finds which installs it has been given and runs the chosen game under that game's own title ("HERCULAN — Earthsiege 2", "HERCULAN — Metaltech: Earthsiege"), with the one logo.

Three rules keep the boundary:

1. **Project references enforce it.** A game assembly references the core and never the other game; the core references neither, so a dependency of asset-layer code (Content, Render, Gl, Platform, the audio back end) on game code is a build error. ES1's is `Herculan.Es1`. Until ES2's game code leaves `Herculan.Engine` ([Open](#open)), a test enforces the line instead: `Es1BoundaryTests` fails when `Herculan.Es1` references a type in ES2's `Sim`, `Cockpit`, `Shell`, `World`, `Scene` or `Terrain`.
2. **No game switches in the core.** A difference between the games lives in the game assemblies or behind a seam the core defines, never as a test of which game is running.
3. **The shared-skeleton modules are ported twice.** `bullet`, `collide`, `damage`, `debris`, `flyersys` and `mechsys` keep each game's own layouts, sizes and rounding, so each game gets its own faithful port and the two link to each other's docs. One abstraction over both would break the retail-structure constraint for both games.

Rejected alternatives:

| Alternative | Why not |
|---|---|
| A separate repository for ES1 under the same brand | The HercWorks readers, the RE tooling, the hooks and the doc rules would be duplicated or held together by submodules, and a reader fix made twice ([ES1 plan](plan-es1-support.md#decisions-to-make-before-phase-0), decision 1) |
| Version branches inside one simulation | The compiler, object layout and fixed-point rounding all differ, so the shared code would fill with version switches |
| ES1's data on ES2's simulation | Not ES1's behaviour; the answers above rule it out |

What crosses the core's seams is designed when ES1 code is ready to port, as [phase 2](plan-es1-support.md#phase-2--the-engine) of the ES1 plan sets out.

## Where missing and divergent behaviour is tracked

[`../../ROADMAP.md`](../../ROADMAP.md) is the single list of what the engine does not implement yet. Behavioural divergences — implemented but wrong — stay in [`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md).

How divergences get *found* rather than recorded is [`plan-differential-harness.md`](plan-differential-harness.md): a proposal to run retail and this engine over the same mission and diff their state tick by tick, which reaches the classes of divergence playtesting cannot.

## Open

- **Open:** five core files use ES2's game types in code, which has to end before that code can leave the core for [its own assembly](#how-the-two-games-share-code). `SceneRenderer` holds a `SimObject` as an item's light subject and runs ES2's painter's-order terrain pass (`HeightGrid.PickDrawCell`, `TerrainVisibleRegion`, and `Render`'s `TerrainPaintOrder`, `TerrainPaintRankBuffer`, `GroundShapeLayer` and `DrawEntry`, which `SpriteRenderer` also takes). `GameInstall` and `LauncherLanguage` read `MissionLoader.DataFolderName`, `LauncherLanguage` also calls `SimulatorPreferences.Load`, and `GameInstall.WriteLanguage` takes `RetailInstaller.Language`. `JoystickCapabilities` reads `ControlsPanelLayout.AxisRowCount`. `JoystickBindings` reads `MechControls`'s throttle-lever constants and `JoystickAction`, and has no ES1 user.
- **Open:** forty-five files in core folders are ES2's own code and move with it: all of `Render/Cockpit`; `Render`'s terrain mesh, paint order, paint rank and texture bank, ground shape layer, beam renderer, HDD map raster, cockpit screen layout, object draw table and world draw pass; `View`'s `ViewCamera`, `ExternalViewChain`, `ExternalCamera`, `PlayerTrail` and `FlyCameraView`; `Audio`'s `GameAudio`, `SoundDirector`, `SoundCatalog` and `ComputerVoice`; `Content`'s cockpit art, HUD color table, HUD sprite sheet, pilot roster, simulator preferences and system messages; `Input`'s cockpit mouse, gunsight drag, keyboard axes, joystick actions, press flashes and the tape player, recorder, playback and recording; and `Install`'s `RetailInstaller`. Core files that use them in code are each sorted, as ES2 code or a seam, when the move reaches them: `SoundBank` (`SoundCatalog`), `SquadVoice` (`ComputerVoice.VoiceFolder`), `SquadMessages` (`SystemMessages.IdAttribute`), `KeyState` (`InputTapePlayer`'s modifier bits), `PaletteRampTable` (`TerrainMeshBuilder.UntexturedRowShade`), `DistanceColorBands` (`ObjectTypeTag`), `IPointerDevice` (`CockpitMouseButtons`, which `CockpitInput.cs` declares), `ISimulatorInput` (`CockpitInput`) and `JoystickPilotInput` (`JoystickAction`).
- **Open:** seventeen core files name ES2's types only in doc comments, which stop resolving once that code leaves the assembly: `CdMusic`, `GameContent`, `HudFont`, `ShadeRamp`, `MeshVertex`, `JoystickReading`, `KeyAction`, `PilotAxes`, `SimMath`, `Camera`, `DtsMeshBuilder`, `ShapeDetail`, `TextureAtlas`, `CockpitHitShake`, `CockpitViewKick`, `EditorCamera` and `TweakSettingDefinitions`. The last catalogs ES2's tweaks beside ones core files read (`CdMusic`, `SceneRenderer`), so it splits rather than being reworded.
