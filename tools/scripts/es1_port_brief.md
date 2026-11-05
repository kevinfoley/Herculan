# Port part @@PART@@ — @@TITLE@@

You are porting part @@PART@@ of the ES1 port on branch `features/es1`. Work only in the worktree `E:\ES2Stuff\.claude\worktrees\es1` (call `EnterWorktree` with that path first; never touch `E:\ES2Stuff` itself). Your scratchpad: `@@SCRATCH@@` (create it).

@@NOTES@@

## Read first
- `CLAUDE.md` (the worktree's), whole.
- `tools/scripts/es1_subagent_rules.md`, whole: the rules every ES1 subagent shares.
- `Herculan/docs/herculan/plan-es1-first-mission.md` "Rules".

## Code
Read each file you change whole before changing it; the rest only where the part touches it.

## Method
- Before writing each function, read its body with `py tools/scripts/es2_naming.py body @@BINARY@@ <address>...` (`-d` for the disassembly; the other ES1 binary's name for a function a doc names as that binary's), the functions one step needs in one call, and check the doc against it. Where they disagree the decompile wins: port what it shows and report it.
- **Stamp what you read.** For each function whose whole body you read, check its `known_symbols` description against the body, and put every detail you took from it that the description lacks into the description: an edits JSON in the scratchpad (`"replace": [[old, new], ...]`, or a batch JSON entry for an unnamed function) and its stamp line, in `@@SCRATCH@@\write_commands.txt` (the doc batch's procedure in `es1_batch_brief.md` gives the shape), run with `es1_batch.py run` at the part's commit. The doc batch then writes those details from the stamps without reading the bodies again.
- Write every other detail the docs do not state to `@@SCRATCH@@\@@PART@@_details.md`, one bullet each with its address: what no one function's description holds (a global's meaning, an order across functions, a proposed home for prose), and each disagreement with a doc. A doc batch verifies and writes them up from that file.
- Keep the layout and style of the code around you. Doc comments cite the retail symbol and address and link the doc section; an engine guard or invention says so. Every class under about 1500 lines.

## Limits
- Budget: about @@BUDGET@@ tool calls. Stop and report rather than improvise when the design does not hold.
- No git writes. No `known_symbols` command other than through `write_commands.txt` and `es1_batch.py run`, with the user's leave. Do not launch any app. Do not edit any doc, and do not run anything that writes `tools/analysis_out/` or a Ghidra project.
- Tests: write none. Name in your report any test that would catch a real future regression, with the code edit it catches.
- `dotnet build Herculan/HerculanEngine.sln` at 0 warnings; `dotnet test Herculan/HerculanEngine.sln` green (about 40 s; past a few minutes is a hang: report it).

## Report (at most 400 words)
1. Files changed or added, one line each.
2. The functions whose descriptions were edited and stamped, the path of the details file, how many details it holds, and each doc/decompile disagreement.
3. Every PLACEHOLDER added or retired, and what is left tagged for a later part.
