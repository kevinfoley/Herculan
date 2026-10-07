# Handoff — editing and saving missions from the HERCULAN Editor

Feasibility and scope for two features in `Herculan.Engine.Host.Editor`:

1. Moving a herc to a new starting position.
2. Saving the edited mission back to `script.dat`.

## Headline

Neither is a from-scratch build. The `script.dat` writer already exists and is complete: `ScriptDatTransformer.Write` covers all 13 blocks and round-trips all 10 real files byte-exact (see [`../retail/formats/script-dat.md`](../retail/formats/script-dat.md)), and `HercWorks.UI.MissionScriptForm` already drives it through an open/edit/Save As UI. What is missing is that the 3D editor is a separate application that discards the parsed document.

Both features are viable. The scope concentrates in one place — how a per-object position is represented — plus one unanswered RE question.

## What already exists

- `HercWorks.UI.MissionScriptForm.OnSaveAs` — a working save path, with VOL entry-prefix preservation and an advisory cross-block ref-range validator.
- `MissionLoader.AddRoster` — `Coordinate(script, record.PositionRef) ?? OffsetFromGroup(...)`. The engine already honours a roster record's own position override, matching `Mech_AttachToGroup` (`00417aa8`). The move mechanism is implemented; nothing writes to it.
- `MissionPlacement.Kind` + `SlotIndex` address a block-7/8/9 record exactly, and `GroupIndex` addresses block 11. The editor's selection already carries enough to find the record to edit.
- `Camera.ViewportPointToRay` and `WireframeRenderer` — what a drag gizmo would be built from.

## Gaps

### 1. The editor drops the source document

`MissionLoader.Load` parses a `ScriptDat`, builds a `Mission`, and lets the `ScriptDat` fall out of scope. Nothing downstream can write back. Carrying it on `Mission` (or returning a pair) is small work, but touches `Mission`'s 15-argument constructor and `MissionScene`.

### 2. Positions are indirect

An object does not hold a position; it holds a `PositionRef` index into block 1. Every roster record across all 10 retail samples carries `-1` there, so every object inherits its group's point plus a formation offset. Moving one herc means giving it a coordinate of its own. Two routes:

- **Append** a block-1 entry and point the record at it. Index-safe: every other block indexes block 1 by array position, so appending at the end shifts nothing. This is why `MissionScriptForm` refuses add/remove elsewhere.
- **Mutate** the coordinate the object already resolves to. Only correct when nothing else references it, which needs a refcount across blocks 1/3/4/7/8/9/11.

Recommended: reuse an identical existing point, otherwise append; refcount before ever mutating in place.

### 3. Three consequences of appending

- Block 1's extent frames the heads-down map (see [`../retail/formats/heads-down-display.md`](../retail/formats/heads-down-display.md), and `MissionBox`). A point outside the current bounding box silently rescales the player's in-game map. Warn on it.
- **Open question: file length.** Retail files are exactly 13,520 bytes; the transformer writes unpadded by design. Growing block 1 grows the content. Whether `DBSim_LoadScriptDat` (`00424308`) reads into a fixed 13,520-byte buffer is not established. If it does, growth past that overflows in the retail exe. Check this in Ghidra before shipping any write that adds records — it gates the append design.
- **The override path has never been observed live.** It is RE-derived and engine-implemented, but no retail file exercises it. Confirming it means writing one and running retail DBSIM.

### 4. Moved is not the same as stays there

`MissionPlacement` carries `FormationOffset` even when the record supplies its own position, and AI followers hold formation on their leader every tick. For a non-leader group member the edit therefore sets a spawn position the AI may walk out of. Correct behaviour, but it reads as wrong if the UI does not say so.

### 5. Live scene refresh

`items[]` in the editor's `Program.cs` is a flat array with transforms baked once at `window.Load`, with the pick spheres alongside it. Moving an object needs both updated plus a terrain-height query for the new ground level. Cheap, but needs a `SceneObject` to item-slot index that does not exist yet.

### 6. No round-trip test

`tests/` has `MecFileRoundTripTests` and `PlayerSaveRoundTripTests`; `script.dat` has none — its byte-exact claim lives only in the format doc. Adding a second application that writes this format without that test beneath it is the riskiest part of the job.

## Scope

| Slice | Size |
|---|---|
| Carry `ScriptDat` through to the editor | small |
| Save button, backup/temp-file write | small |
| `script.dat` round-trip test over the 10 real files | small |
| Position write-back: reuse/append, refcount, bounding-box warning | medium — the bulk of it |
| Numeric X/Y/Z entry in the Properties panel | small |
| Drag gizmo (ray-versus-ground-plane, handles) | medium |
| Live scene and pick-sphere refresh | small–medium |
| Ghidra: does DBSIM read into a fixed buffer? | unknown — gates the append design |

## Suggested sequencing

Round-trip test, then carry the document through, then numeric entry plus save — which proves the whole chain end to end with almost no UI — then the drag gizmo on top. Saving is the easier half; the move is where the design work is.
