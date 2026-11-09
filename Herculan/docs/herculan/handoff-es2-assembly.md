# Handoff: ES2's game code out of the core

Branch `features/es1-refactor`. Goal: `Herculan.Engine` holds only game-agnostic code and ES2's game code lives in its own assembly, `Herculan.Es2` ([planning.md, "How the two games share code"](planning.md#how-the-two-games-share-code)). Project references now enforce the line, so the stand-in checkers (`tools/BoundaryCheck`, `Es1BoundaryTests`) are gone.

## Steps

1. to 5. **Done.** The core's references to ES2 code were removed: small type moves, the install and joystick split, the settings catalogs, the `SceneRenderer` seam (ES2's `GroundLayerPass` owns its ground draw), and the last seven. The commits say what moved where.
6. **Done.** `Herculan/src/Herculan.Es2` holds the 333 files, at the same relative paths (`git mv`, so history follows), and namespaces are `Herculan.Es2.<folder>`. `Herculan.Engine.Host`, `Herculan.Engine.Editor` and `Herculan.Engine.Tests` reference it; `Herculan.Es1` does not, and Es1's one doc cref to `InputTapePlayer` became plain text.
   - Wholly moved namespaces: `Cockpit`, `Install`, `Scene`, `Shell`, `Sim`, `Sim.Ai`, `Terrain`, `World`, `Render.Cockpit`. Split between core and ES2: `Audio`, `Content`, `Input`, `Render`, `Settings`, `View`.
   - `Beam.glsl`, `BeamJagged.glsl` and `TerrainPaintRank.glsl` are embedded in `Herculan.Es2` as `Herculan.Es2.Render.Shaders.*` and loaded through the assembly overload; the core keeps its six.
   - No core `internal` had to become `public`: ES2 code uses none.
   - The usings the rewrite added and nothing needed were dropped by IDE0005, compared against an IDE0005 run of the parent commit, so the 107 unneeded usings that predate the move are untouched.

## Where it stands

Checks after step 6: both solutions build with 0 warnings; all tests pass (1,133 engine tests, one fewer with `Es1BoundaryTests` gone). With `GenerateDocumentationFile` on, the engine solution has the same 40 broken doc crefs as before the move. `doc_lint.py` is clean; `doc_links.py --code` reports only the three `difficulty.md` links.

Not checked: a run of the simulator and the editor, which is the user's. The moved shaders load on first draw only, so the beams (any energy weapon) and the painter's-order ground draw (the simulator's default 3D view) are what to look at. Delete this handoff once that run looks right.
