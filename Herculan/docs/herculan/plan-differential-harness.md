# Plan — differential testing against retail DBSIM

A harness that runs retail `DBSIM.EXE` and `Herculan.Engine` over the same mission and compares simulation state tick by tick, so a divergence is found by diffing rather than by noticing it on screen.

This is a plan, not a record of something built. Nothing here is implemented.

## Why

Playtesting cannot reach a large class of divergence. Mission-objective conditions, campaign and save state, salvage arithmetic, AI latches and anything gated on a rare condition all fail quietly or fail as a symptom several layers from the cause. The audit that produced [`../retail/simulation/damage-system.md`](../retail/simulation/damage-system.md)'s corrections found, among others, an engagement flag whose absence makes a mission objective unsatisfiable, and an alert latch that never re-armed — the first would have looked like a broken objective system, the second like normal behaviour. Both would have been caught in one tick-diff run.

Reading the disassembly harder is not the alternative. Every error that audit found was a **negative or a count** — "nothing calls", "never cleared", "exactly 5", "both callers" — because those are the claims a human verifies by searching rather than by reading, and searching is where corners get cut. A harness does not care what anyone believed.

## What makes this tractable

Four properties of the retail binary, all verified:

- **No ASLR.** `DYNAMICBASE` is clear in the PE header and relocations are not stripped, so the image loads at `0x00400000` every run. Every address in these docs is a literal runtime address; no rebasing layer is needed. (System-wide Mandatory ASLR in Exploit Protection would break this. It is off by default.)
- **The simulation is deterministic.** The generator has no entropy input at all — see [`../retail/simulation/random-generator.md`](../retail/simulation/random-generator.md). Same mission plus same input gives the same run, every time.
- **One wall-clock path, and it is patchable.** `Time_BeginSimTick` (`004677bc`) spins on `GetTickCount` until 40 ms have passed, then sets `SimTickDelta = clamp((elapsed << 8) / 125, 0x40, 0x1c2)` — 81 at exactly 40 ms, but 83 at 41 ms and 86 at 42 ms when the OS overshoots, which rescales every rate that tick. Forcing `SimTickDelta` to 81 after each call removes the last source of run-to-run variation.
- **One clean hook point.** `Sim_MainTick` (`0045f464`) is `short __cdecl(void)` — no arguments, one entry per frame, walking every global object list. A five-byte detour at its prologue is the whole instrumentation surface.

The engine side already matches on the two that matter: `SimWorld.TickDelta` is pinned to 81, and `SimRandom`'s default constructor is DBSIM's own seeded state.

## Shape

Retail side: an injected DLL that pins `SimTickDelta`, detours `Sim_MainTick`, and after each tick walks the live-object list and emits one record per tick.

Engine side: the same record, from the equivalent point in `SimWorld.Tick`.

Then a diff that reports the first tick at which the two disagree, and on which object and field.

**Emit a hash per tick, not a full dump.** One line of `tick, objectCount, hash` keeps a whole mission cheap to run and cheap to store. Binary-search to the first divergent tick, then re-run both sides dumping full fields for a window around it. Full dumps from the start are the obvious design and the wrong one — the I/O dominates and almost all of it is thrown away.

Globals the retail walk needs, all already identified:

| What | Where |
|---|---|
| Live-object list, and its count | `DAT_004a9b7c` / `DAT_004a9b82` |
| Mech list | `GlobalMechList` |
| Mission counters | `DAT_004a9ef4` |
| `SimTickDelta` | `DAT_004d3be8` |
| Generator state | `0x4d261d`; the [presentation generator](../retail/simulation/random-generator.md#the-presentation-generator) at `0x4d268f` feeds nothing the simulation holds |

## Tiers

Worth doing in this order; each stands on its own.

1. **Snapshot diff — an afternoon, no code.** Break once after mission spawn in any debugger, dump the object list, compare with the engine at the same point. Catches spawn placement, loadout, starting condition and type-record misreads — a large class no run has yet compared against retail — for almost no effort. Do this first whatever happens to the rest.
2. **Tick-hash harness — a couple of days.** The DLL, the engine-side emitter, and the diff. This is the piece that pays for itself.
3. **Per-subsystem field sets — ongoing.** The harness is only as good as the fields chosen for the hash. Pick them when porting a subsystem, while the RE is fresh.

## The real cost is not the harness

Making the two engines agree well enough for the diff to be *quiet* is the actual project. This engine already runs some things back to back where the original has separate dispatch passes — `MechObject.Tick` says so in as many words, and `HitTests.Raycast`'s ordering relative to the detection sweep is the same kind of choice. Ordering differences produce divergence even when both engines are individually correct, so early runs will be noisy and reconciling them is real work.

That is worth knowing up front, and it is not a reason to skip it: even a noisy diff that says "first divergence at tick 340, object 3" is a far stronger lead than anything playtesting gives. It also converts a [`../../ROADMAP.md`](../../ROADMAP.md) item — how many draws DBSIM has made before a given roll — from something to reason about into something to measure, since the generator state is one of the values the harness can dump.

## Prior art in this repo

`tools/scripts/es2_xref.py` and `tools/scripts/es2_fieldscan.py` answer the static versions of these questions — what references an address, what touches a field. They are the right tools for deciding *which* fields a subsystem's hash should cover, and for confirming a suspected divergence once the diff has pointed at one.

## Open

- **Open:** driving the same input. The simplest first scenario is one with no player input at all: an AI-only engagement, or the player's machine left stationary. Scripted input replay is a later problem and may not be needed for a long time.
- **Open:** how to launch a specific mission in retail without going through the shell.
- **Open:** injection mechanics. A DLL with a trampoline hook is the fast, reliable option. A debugger script needs no build step but breaks on every tick, which may be too slow to run whole missions; benchmark it.
