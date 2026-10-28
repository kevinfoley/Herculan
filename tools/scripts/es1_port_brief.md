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
- Before writing each function, read its body in the dumps (`E:\ES2Stuff\tools\analysis_out\ES1DBSIM_decomp_full.c` and `ES1DBSIM_disasm_full.txt`; Grep for its address, then Read with an offset) and check the doc against it. Where they disagree the decompile wins: port what it shows and report it.
- Write every detail you took from the decompile that the docs do not state to `@@SCRATCH@@\@@PART@@_details.md`: one bullet per detail, with its address and any disagreement with the doc. A doc batch verifies and writes them up from that file.
- Keep the layout and style of the code around you. Doc comments cite the retail symbol and address and link the doc section; an engine guard or invention says so. Every class under about 1500 lines.

## Limits
- Budget: about @@BUDGET@@ tool calls. Stop and report rather than improvise when the design does not hold.
- No git writes. No `known_symbols` commands. Do not launch any app. Do not edit any doc, and do not run anything that writes `tools/analysis_out/` or a Ghidra project.
- Tests: write none. Name in your report any test that would catch a real future regression, with the code edit it catches.
- `dotnet build Herculan/HerculanEngine.sln` at 0 warnings; `dotnet test Herculan/HerculanEngine.sln` green (about 40 s; past a few minutes is a hang: report it).

## Report (at most 400 words)
1. Files changed or added, one line each.
2. The path of the details file, how many details it holds, and each doc/decompile disagreement.
3. Every PLACEHOLDER added or retired, and what is left tagged for a later part.
4. A short in-app checklist on a mission whose data makes each item cheap to see, each item checked against that mission's data first (`HercWorks.Query`).
