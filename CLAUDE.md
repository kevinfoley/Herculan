# HERCULAN / HercWorks

Reimplementation of Earthsiege 2 (1996) in C#, reverse-engineered from the retail `DBSIM.EXE` and
`VSHELL.EXE`. `HercWorks.*` is the data-file toolkit; `Herculan.Engine` is the game engine.

- `Herculan/docs/retail/formats/` — file formats
- `Herculan/docs/retail/simulation/` — simulation behaviour (DBSIM)
- `Herculan/docs/retail/shell/` — shell behaviour (VSHELL): campaign, career, armory, front end
- `Herculan/docs/herculan/planning.md` — architecture decisions and their rationale
- `Herculan/KNOWN_ISSUES.md` — retail bugs, and where this engine diverges from retail

Build: `dotnet build Herculan/HerculanEngine.sln` (engine) and `Herculan/HercWorksMDK.sln` (toolkit).
Tests: `dotnet test Herculan/HerculanEngine.sln`. Keep both at 0 warnings.

Questions about the retail missions — which place a structure, mech or flyer type, what tests or writes a campaign flag, what orders a group carries, per-row record counts — go to `Herculan/src/HercWorks.Query` (`--help`), not a throwaway scanner. It reads through HercWorks.Core, so a wrong answer is a Core bug to fix there.

## Reading files

Read whole files with the Read tool, never `cat`/`head -n <big>`/`sed -n 'A,Bp'` — it paginates, numbers lines, and is not re-sent when the same file is read again, whereas a Bash dump that overflows the output limit returns a useless preview and gets re-read in overlapping ranges at several times the cost. Bash is for greps, `find`, and short ranges of a file already read. `tools/scripts/big_file_read_guard.py` enforces this as a `PreToolUse` hook when it is wired into `.claude/settings.json`. `tools/scripts/truncated_enumeration_guard.py` is its sibling for completeness: it denies a grep/rg/Select-String over `tools/analysis_out/`, `known_symbols_*.json`, `Herculan/docs/` or `Herculan/src/` whose output is cut by `head`/`tail`, because a truncated search cannot back an "all callers"/"only reader" claim — count with `| wc -l` first, or append `# sample-ok` when a sample is genuinely all you need.

## Documentation rules

The docs state what is true now. How the project got there belongs in `git log`.

1. **Never narrate a correction.** No "previously stated", "an earlier pass of this doc", "corrected
   2026-08-21", "this supersedes", "now disproved", "SOLVED". When a finding invalidates existing
   text, edit that text; put what changed in the commit message.

2. **Re-read the whole doc before adding to it.** If what you are about to write contradicts a
   passage already there, fix that passage. Never let both stand.

3. **A doc-maintenance diff should usually contain deletions.** Purely additive means journal.

4. **No dates**, including "solved on" headers and status lines.

5. **One fact, one home.** The doc owns the RE evidence and derivation; the code comment owns the
   constants, the local behaviour, and a link to the doc section. Never both. The same holds between
   code layers: the `.msn` model owns a field's description, and a `script.dat`, UI or engine member
   carrying that datum uses `<inheritdoc cref="..."/>` and adds only what differs.

6. **Keep a disproven reading only when it protects a reader** — could someone reach that wrong
   conclusion independently (a symbol still misnamed in Ghidra, an obvious-but-false
   interpretation)? Then keep it forward-looking ("the obvious reading is X; it is actually Y
   because Z") in that doc's **Rejected readings** table. "This doc used to say X" fails the test.

7. **Verify status claims before repeating them.** "Unported" and "open" go stale silently; grep
   the named type first.

8. **Open work lives in one final `## Open` section**, the last section of the doc, as top-level
   bullets labelled `**Unported:**` (a retail feature this engine lacks) or `**Open:**` (anything
   else unfinished: incomplete RE, an unconfirmed reading, an unexplained field). Those are the only
   two status terms — never "not ported", "untraced", "unresolved", "undecoded". The body
   states what is known and may link to [Open](#open); it carries no tasks and no hedges
   ("plausibly", "unconfirmed") — a hypothesis is an Open item. `KNOWN_ISSUES.md`, `ROADMAP.md` and
   `README.md` keep their own structure.

9. **Retail docs describe retail.** `docs/retail/` says what the original does, not what HERCULAN
   does. The engine's types, file layout, tweaks and
   departures from retail go in doc comments on the C# that implements them, citing the doc
   section, so an engine change never has to hunt for prose to update (rule 5 decides which is
   which). A retail doc may link to an engine doc, but it does not name C# types. The engine docs are
   `docs/herculan/`, `KNOWN_ISSUES.md`, `ROADMAP.md` and `README.md`.

`tools/scripts/doc_lint.py` enforces 1, 4, 6 and 8, and runs automatically after any edit under
`Herculan/docs/` or to a `known_*.json`. `/doc-lint` runs it over the whole set. It cannot catch 2,
3, 5 or 7. It catches 9 by explicit markers ("HERCULAN", "this engine", `Herculan.*` namespaces,
"Engine port" headings) and by C# names (`csharp-name`): a backticked token declared as a type or
public member in `HercWorks.Core` or `Herculan.Engine` that is not also a retail name. Single-word
names (`Height`) pass, as does anything a `known_*.json` names, the class prefix of such a name, or
an identifier in the retail EXEs. Both checks also run over the `known_*.json` descriptions. A full
run shows its findings as a per-file count (`--engine` lists each one), and the edit hook flags only
the lines an edit wrote. "The engine" also matches a HERC's own engine component; mark such a line
`<!-- doc-lint: ok -->`.

`tools/scripts/doc_links.py` resolves every cross-reference — that the file exists and that a
`#fragment` still names a heading. **A heading owns its anchor**, so retitling one breaks inbound
links in files the rename never touched; that is why it has no `--staged` mode and always checks the
whole set. `--code` adds the doc paths named in C# doc comments.

Handoff docs (`docs/herculan/handoff-*.md`) are exempt: ephemeral scratchpads, never authoritative for
status. Drain them into topic docs and delete what you moved.

## Reverse-engineering conventions

- Addresses are virtual addresses in the named binary, bare hex (`0046e87c`). Name the binary when it
  is not DBSIM.
- Cite the original symbol alongside the port: `Mech_LocomotionTick (00416a04)`.
- After naming or porting a function, name the `FUN_` callees you actually read to understand it
  (`tools/scripts/es2_unnamed_callees.py --caller <Name>` lists them). Don't read further down to do
  it: a callee you can describe from its own body gets a name for what it does; one you can't gets no
  entry and stays in the backlog.
- **Stamp what you settle, and trust stamps.** When a task reads a function's whole body, check its `known_symbols` description against it, correct it, and stamp it with `tools/scripts/es2_stamp.py` (what a stamp covers: `tools/ghidra_scripts/README.md`, "Verification stamps"). A stamped description is the answer, and `Check-Symbol.ps1` shows it; it is not re-derived. An unstamped description, and doc prose, is a lead, re-read only when the current task depends on it, so the stamped set grows with the work rather than through an audit. A stamp never carries a search result: whatever its positive control, a sweep that found nothing is written as what was searched and found nothing ("no caller found by `es2_xref`"), an Open item, in the description and the doc alike. Run `es2_stamp.py check` after regenerating `tools/analysis_out/`. Before reporting a task, every function the task's doc lines, descriptions and code comments describe is one you read this session or one that is stamped; the doc-lint hook names the unstamped ones in each doc edit, which it sees only when the edit goes through Edit or Write, so a scripted doc edit is followed by a `doc_lint.py` run. When a body you read contradicts something you wrote earlier in the task, correct it then.
- How hard to chase a negative claim is a judgement on what overturning it would change. One a port rests on gets the sweep and positive control below; one the data already settles (a field that is -1 in every retail file) needs nothing more; one that turns up while reading for another reason is recorded when it does.
- Say plainly when something is this engine's invention rather than read from the binary, so it is
  not later mistaken for vanilla behaviour.
- **Never write "nothing calls this", "nothing reads this" or "dead code" from a failed grep.** A
  text search of the dumps misses references through data tables, and misses field accesses through
  Borland's `LEA`/`ADD`-rebase and stack-spill idioms. Two tools settle these, and both print their
  own caveats:
  - `tools/scripts/es2_xref.py ADDR|SymbolName` — every rel32 branch, stored pointer and vtable slot
    holding an address, over the whole PE. A clean sweep is what "unreachable" has to mean.
  - `tools/scripts/es2_fieldscan.py OFFSET [--range LO-HI]` — every access to a struct field,
    resolving those idioms. Reports the union of two alias passes because neither is sound alone.

  Both are null-result tools. A field that carries a meaningful value is never *proven* unread; say
  so rather than asserting it. Nor does a clean `es2_xref.py` sweep settle a function's
  reachability on its own: a C++ static initialiser can register one into a dispatch table that is
  empty in the image, so it has no `E8` caller, no `E9` tail jump and no raw dword occurrence
  anywhere in the file. `Subsystem_RunPhase` (`00401d94`) calls its table by phase id once a frame.
  Check for a nearby `RegisterSubsystemLoader` before concluding.
