# ES1 subagent rules

The rules every ES1 subagent shares: RE batches, doc batches and port parts. The brief adds the part's own procedure and limits; where the two differ, the brief wins. `CLAUDE.md` holds the documentation rules and the reverse-engineering conventions and is not repeated here.

## Environment

- Work only in the worktree `E:\ES2Stuff\.claude\worktrees\es1` (branch `features/es1`). The gitignored assets live only in the main checkout: `E:\ES2Stuff\ES1\`, `ES2\`, `tools\analysis_out\` and the Ghidra project. The Python RE tools find them themselves; anything else uses the absolute path.
- The Bash tool is Git Bash. It eats one backslash level, even in `<<'EOF'` heredocs, and the worktree guard refuses heredoc or pipe input to `py`: write a script to the scratchpad with the Write tool and run it as a plain command. Join paths with `/`.
- In a Python edit script, `assert old in text` before every `str.replace` (a replace that matches nothing exits 0 and writes the file unchanged), open files with `encoding="utf-8"`, and keep the file's newline style and BOM. Exact text edits to tracked files go through `py tools/scripts/es2_naming.py repl spec.json`, which asserts the counts.
- Compare hex addresses as zero-padded strings in Python, never as numbers in `awk` (`00430e00` parses as scientific notation).

## Reading

- Read a file you will edit whole first: every doc you add to, every code file you change. A file you only consult gets the section the brief names, or a Grep and then a Read with an offset. Read the decompile and disassembly dumps the same way, never with a Bash dump. Read nothing twice.
- **Look up before deriving.** `tools/scripts/Check-Symbol.ps1 <address|name>` before describing any function or field: the dumps in `tools/analysis_out/` lag `known_symbols_*.json`, so a `FUN_` there may already be named, described and stamped. A stamped description is the answer; an unstamped one is a lead.
- **Never cut an enumeration with `head` or `tail`.** Count it with `| wc -l`, then read all of it.
- **Read retail files through `HercWorks.Core`.** Before any script that reads an install file or a VOL entry, grep `Herculan/src/HercWorks.Core/Data/File/**` for its extension and use that reader, through `HercWorks.Query` (`--help`) when a command fits. Never hand-decode a file Core reads; if Core reads it wrong, report that.
- Before writing a fact or an Open item, grep `Herculan/docs` for its addresses and names: another doc may already hold it or answer it.

## Negative claims

- A search finds; it never proves an absence. Write what was searched and what it found ("no caller found by `es2_xref`"), as an Open item, never as "nothing reads", "unused" or "dead". Any sentence with "no", "never", "only", "none" or "sole" is a negative: check its wording.
- Weigh the effort by what rests on it. A negative a port rests on gets `es2_xref.py`/`es2_fieldscan.py` and a positive control (the same search finds a sibling known to be used); one the data settles (a field −1 in every retail file) needs nothing more.
- The searches miss: decimal vtable offsets in the decompile (`*p + 100` is `+0x64`); int-indexed forms (`param_1[0x69]` is `+0x1a4`); a pointer rebased by `ADD` before the access; a global that is a field of a block written through the block's base (find the block, its static initialiser and every holder of the base); a function Ghidra starts late or never made (`es2_late_entries.py`, the bytes before the entry); a table filled at run time by a static initialiser; one writer of several (command line, prefs, init order).

## Reading the binary

- A branch is behaviour only if its guard can be true: read every condition guarding it and check the tested value is ever stored. A widget handler is behaviour only if a press can reach the widget (registration order, rect containment in the retail `.GAU`).
- Siblings differ: read the function the type registry or vtable actually points at. An object's class is its last vtable write, not its constructor's.
- A field the main path ignores has another reader before it is padding; a field can mean different things to different readers; check the base an offset is relative to. Publishing a value is not consuming it.
- For a function whose result a caller tests, read each exit's last write to `EAX`/`AX` in the disassembly: the decompile can drop a return value.
- **Code Ghidra left undefined** (a call or branch target inside no function, a gap past a function's end): `py tools/scripts/es2_naming.py body <binary> <address> -d` disassembles the image there with Capstone, up to the next Ghidra function entry. Read that, never a guess from the neighbours.
- **Timer counts are not milliseconds.** ES1's countdown count is 100/256 ms (≈ 0.39 ms; 10000 counts ≈ 3.9 s). ES2's is 0.49 ms (10000 ≈ 4.9 s); never convert an ES1 count with ES2's unit.
- If the reading says the player would see a bug, look for the missing caller among the unported modules before calling it a retail bug.

## Porting

- Port only what retail evidence shows: the binary, the game data, the manual or the user's retail observation. An unused field or frame is not evidence of what it was meant for.
- When an unknown is out of scope, put in a placeholder that is plainly not retail on screen, tagged `// PLACEHOLDER:` in the code, and report it for the doc's Open or ROADMAP's Unported list. Never record a placeholder as retail behaviour.
- Retail behaviour that the evidence establishes, the player can observe and is not an obvious bug is ported without asking. A renderer substitution, a retail bug or an engine-only choice with no retail answer is reported as a question, not shipped.
- ES1 docs describe retail, never the C#. Engine inventions are said to be so in the code comment.
- A class over about 1500 lines has too many responsibilities: report it and propose a split rather than add to it (partial classes are not a split).
- **Tests:** name the code edit a test would catch before proposing it. No asserted UI strings, no literal snapshots of retail bytes, no test that argues an RE reading (the doc owns that). Every wait in a test has a deadline; the suite takes about 40 s, so past a few minutes is a hang to report.

## Writing

- Facts, not stories: cut how something was found. One paragraph or bullet per line, never hard-wrapped. A section's lead sentence says what a player or reader would see, in everyday words, before any term the code introduced. American spelling in new text; leave old British text alone.
- Terms: squad (never "lance"), squadmate, HERC and HERCs in capitals, a missile is a missile (never a "round"; gun rounds stay rounds), turret in prose (torso in code names), hardpoint one word, LOD, the player pilots a HERC.
- Distances in a report give units and meters: 100,000 units (600 m), 1000 units = 6 m.

## Verifying

Check with the fewest cases that catch the mistake: the build, the tests and at most about 10 targeted cases. No random inputs, parameter sweeps, harnesses, rasterisers or simulations; a data question is one pass over the files through HercWorks.Core or HercWorks.Query. Stop and report on a refused tool or edit, unless a plain read-only command does the same job.
