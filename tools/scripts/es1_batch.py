"""Runs an ES1 naming batch's write commands, then applies, regenerates and checks.

A batch subagent cannot touch the known_symbols files, so it lists the writes in a command file
(handoff-es1-data-formats.md, "How the batches were run"). This runs that file:

1. Each `es2_naming.py insert|relink|edit|fixrefs` line, as a dry run and then with --write. A line
   may be given in either form or both; consecutive duplicates collapse. The doubling of a renamed
   name (`Name (Name (addr))`) is undone by es2_naming.py itself. fixrefs edits docs and C#, not
   known_symbols: run doc_lint.py over the docs it lists, since the edit hook does not see them.
2. Each `es1_naming.py twin` and `es2_stamp.py stamp` line as given. A stamp refusal stops the run:
   read the refused sentences, then rerun the line with the flag it needs (or reword) and resume.
3. Each `es2_naming.py apply BIN` line read-only, then with --write when the read-only run reported
   errors=0 and exit 0.
4. ghidra_full_decomp.py for every applied binary, stopping when it reports a known_symbols name that
   differs from the database, one with no function at its address, or a failed decompile.
5. The lines after a comment line containing "after regen" (stamps of defined stubs that make a call,
   which `--via raw` cannot read), then `es2_stamp.py check`.
6. The paths to stage, the untracked handoff docs left out.

Before step 1 (or the --from step) it runs `precheck` and stops with nothing written on a problem
(--no-precheck skips it). A refused stamp line also prints the line again with only the refused
targets, the rest of the line being stamped.

`precheck` runs es2_stamp.py's wording checks over each stamp line against the descriptions the
file's insert and edit JSONs will write (known_symbols for the rest), with that line's flags, reading
and writing no file but those: REFUSE for a sentence the stamp would stop on, RAW for a --via raw
target that is not a straight-line stub (it goes after the regeneration, --via disasm), UNUSED for a
--read-callers or --own-negatives no target of its line needs, and es2_naming.py's SHAPE warnings,
which --fix first rewrites in the batch JSONs. It also warns (STAMP, not a problem: the run goes on)
for an edit that changes a stamped entry's description, which drops the stamp, when no stamp line
after it names the entry. A subagent runs it on its own command file before handing it in.

Only those tools and subcommands run; any other line (grep, git, shell) is skipped with a note,
since the file is a subagent's output. Comment and blank lines are ignored.

`brief` writes the batch subagent's brief: tools/scripts/es1_batch_brief.md, the rules every batch
shares, with the batch number, range and scratch folder filled in and the batch's own section (what
the range holds, its leads, its Open items) taken from a notes file. --auto appends the leads the
tools give: `es1_boilerplate.py bounds` over the range with a 0x100 margin, the function counts,
`es2_naming.py match`'s candidates, and the docs that name one of the range's function addresses.
`brief N --doc` writes a doc batch's brief from the same template, with no range and none of the
range's tools: the notes name the port parts, their `<part>_details.md` files and the docs, and the
subagent verifies each detail in the dumps and writes it into the doc that owns it.
`port` writes a port part's brief the same way from tools/scripts/es1_port_brief.md. Both templates
send the subagent to tools/scripts/es1_subagent_rules.md for the rules every ES1 subagent shares.

A notes section whose `## Heading` the template also has (Read first, Code, Limits, Report) is
added to the end of the template's section; the rest of the notes goes where @@NOTES@@ stands.
A template's `<!-- tag -->` ... `<!-- /tag -->` blocks are kept for the brief they belong to
(`range` for a batch) and dropped otherwise.

Both gate the brief's fixed reading at READ_BUDGET (40 KB): each file its "Read first" bullets
name, a backticked path whole or by the quoted headings after it
(`plan.md` "Rules" "Decisions") or its `#anchor`. A doc the subagent edits is marked `(edit)` on its
bullet and a file in the scratch folder is the part's input; both are listed but not counted. Past
the budget, or on a file or heading not found, nothing is written; --over-budget "REASON" writes it
anyway and prints the reason, for the orchestrator's report.

`stamps` prints the stamp lines for the function entries of a batch's insert JSONs, for the command file: grouped
by binary and by how each entry's `source` says its body was read (the decompile, the disassembly, or both), with an
entry that has no Ghidra function as --via raw when it is a straight-line stub and --via disasm after the
'# after regen' line otherwise. It adds no flag; precheck names a line that needs --read-callers or --own-negatives.

`next` proposes the range: from START, whole object files as `es1_boilerplate.py bounds` places
them, until about --target functions, a file never split; it prints each file's counts and evidence
and the held rows (--held) inside.

Usage:
    python tools/scripts/es1_batch.py next START [--target 60] [--window HEX] [--held a+b]
    python tools/scripts/es1_batch.py brief N LO HI --scratch DIR [--binary ES1GO] [--notes NOTES.md] [--auto] [--out FILE] [--over-budget REASON]
    python tools/scripts/es1_batch.py brief N --doc --scratch DIR --notes NOTES.md [--binary ES1GO] [--out FILE] [--over-budget REASON]
    python tools/scripts/es1_batch.py port PART --title TITLE --scratch DIR [--binary ES1GO] [--notes NOTES.md] [--budget 150] [--out FILE] [--over-budget REASON]
    python tools/scripts/es1_batch.py run COMMANDS.txt [--from N] [--no-precheck]
    python tools/scripts/es1_batch.py precheck COMMANDS.txt [--fix]
    python tools/scripts/es1_batch.py list COMMANDS.txt
    python tools/scripts/es1_batch.py stamps BATCH.json [BATCH.json ...]
"""
from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SCRIPTS = REPO / "tools" / "scripts"
ALLOWED = {
    "es2_naming.py": {"insert", "relink", "edit", "fixrefs", "apply"},
    "es2_stamp.py": {"stamp"},
    "es1_naming.py": {"twin"},
}
WRITES = {"insert", "relink", "edit", "fixrefs"}
PYTHONS = {"py", "python", "python3", "py.exe", "python.exe"}


class Step:
    def __init__(self, kind, argv=None, line=0, text=""):
        self.kind = kind          # "write", "plain", "apply", "regen", "check"
        self.argv = argv or []    # script name + its arguments, without --write
        self.line = line
        self.text = text

    def label(self):
        if self.kind == "regen":
            return "ghidra_full_decomp.py " + " ".join("--binary " + b for b in self.argv)
        if self.kind == "check":
            return "es2_stamp.py check"
        suffix = {"write": "  (dry run, then --write)", "apply": "  (read-only, then --write)"}.get(self.kind, "")
        return " ".join(self.argv) + suffix


def parse(path):
    """The command file as steps, in the order the run takes them."""
    before, after, skipped = [], [], []
    applied = []
    phase = before
    for n, raw in enumerate(Path(path).read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line:
            continue
        if line.startswith("#"):
            if re.search(r"after\s+regen", line, re.I):
                phase = after
            continue
        toks = [t.strip("\"'") for t in shlex.split(line, posix=False)]
        if toks and toks[0].lower() in PYTHONS:
            toks = toks[1:]
        script = Path(toks[0]).name if toks else ""
        sub = toks[1] if len(toks) > 1 else ""
        if script not in ALLOWED or sub not in ALLOWED[script]:
            skipped.append((n, line))
            continue
        argv = [script] + [t for t in toks[1:] if t != "--write"]
        if sub in WRITES:
            kind = "write"
        elif sub == "apply":
            kind = "apply"
            if phase is after:
                raise SystemExit("line %d: an apply after the 'after regen' marker; applies come before the "
                                 "regeneration" % n)
            if argv[2] not in applied:
                applied.append(argv[2])
        else:
            kind = "plain"
        if phase and phase[-1].argv == argv and phase[-1].kind == kind:
            continue      # the dry run and --write forms of one command
        phase.append(Step(kind, argv, n, line))
    steps = before
    if applied:
        steps.append(Step("regen", applied))
    steps += after
    steps.append(Step("check"))
    return steps, skipped


def json_path(p):
    p = Path(p)
    return p if p.is_absolute() else REPO / p


def batch_items(steps):
    """The entries and edits of the file's insert and edit JSONs, as (subcommand, item) pairs."""
    out = []
    for s in steps:
        if s.kind == "write" and s.argv[0] == "es2_naming.py" and s.argv[1] in ("insert", "edit"):
            for e in json.loads(json_path(s.argv[2]).read_text(encoding="utf-8")):
                out.append((s.argv[1], e))
    return out


def fix_shapes(steps):
    """Rewrites the SHAPE forms in the file's insert and edit JSONs in place, printing each; returns
    how many. Those are the batch's scratch files, never a known_symbols file."""
    import es2_naming
    n = 0
    for s in steps:
        if not (s.kind == "write" and s.argv[0] == "es2_naming.py" and s.argv[1] in ("insert", "edit")):
            continue
        path = json_path(s.argv[2])
        data = json.loads(path.read_text(encoding="utf-8"))
        changed = False
        for e in data:
            holder, key = (e, "description") if "description" in e else \
                (e, "value") if e.get("field") == "description" else (e.get("fields") or {}, "description")
            # A replace pair's new text is written into the description too, so its forms are fixed alike.
            places = [(holder, key)] + [(pair, 1) for pair in e.get("replace") or []]
            for place, k in places:
                d = place.get(k) if isinstance(place, dict) else place[k]
                if not d:
                    continue
                fixed = d
                for text, want in es2_naming.shape_warnings(d):
                    fixed = fixed.replace(text, want)
                    print("  fixed %s %s: '%s' -> '%s'" % (e["binary"], e["address"], text, want))
                    n += 1
                if fixed != d:
                    place[k] = fixed
                    changed = True
        if changed:
            path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return n


def precheck(steps, start=1):
    """Runs es2_stamp.py's wording checks over each stamp line from step `start` on, against the
    descriptions the file's inserts and edits will write (the known_symbols text for the rest), with
    that line's flags. Reads known_symbols, writes nothing. Prints a REFUSE line for each sentence the
    stamp would stop on, UNUSED for a --read-callers or --own-negatives no target of its line needs, and
    es2_naming.py's SHAPE warnings; returns the number of REFUSE and target problems."""
    import es2_naming
    import es2_stamp
    import es2_symbols
    descs, names, types = {}, {}, {}
    items = batch_items(steps)
    current = {}
    for sub, e in items:
        key = (e["binary"], e["address"].lower())
        if sub == "insert":
            fields = e
        else:
            if e["binary"] not in current:
                current[e["binary"]] = {x["address"].lower(): x for x in es2_symbols.entries(e["binary"])}
            fields = es2_naming.edit_fields(e, current[e["binary"]].get(key[1], {}))
        if "description" in fields:
            descs[key] = fields["description"]
        if fields.get("name"):
            names[(e["binary"], fields["name"])] = key[1]
        if sub == "insert":
            types[key] = e["type"]
    es2_naming.print_shape_warnings([e for _, e in items])
    from retail_binaries import Image
    parser = es2_stamp.build_parser()
    known, images = {}, {}
    problems = 0
    for i, s in enumerate(steps, 1):
        if i < start or s.argv[:2] != ["es2_stamp.py", "stamp"]:
            continue
        a = parser.parse_args(s.argv[1:])
        b = a.binary
        if b not in known:
            ents = es2_symbols.entries(b)
            known[b] = ({e["address"].lower(): e for e in ents}, {e["name"]: e["address"].lower() for e in ents if e.get("name")})
        by_addr, by_name = known[b]
        if a.via == "raw" and b not in images:
            images[b] = (Image(b), es2_stamp.function_extents(b))
        negatives = [es2_stamp.parse_negative(n) for n in a.negative]
        used = {"callers": False, "negs": False}
        for t in (t for arg in a.target for t in arg.split("+") if t):
            addr = t.lower() if re.fullmatch(r"[0-9a-fA-F]{8}", t) else names.get((b, t)) or by_name.get(t)
            entry = by_addr.get(addr) if addr else None
            kind = types.get((b, addr)) or (entry or {}).get("type")
            if not addr or not kind:
                print("  step %d: %s %s has no entry in the file's inserts or in known_symbols" % (i, b, t))
                problems += 1
                continue
            if kind != "function":
                print("  step %d: %s %s is a %s entry; stamps are for functions" % (i, b, t, kind))
                problems += 1
                continue
            if a.via == "raw":
                image, extents = images[b]
                raw = es2_stamp.raw_extent(image, int(addr, 16))
                if raw is None:
                    print("  RAW step %d (line %d): %s %s is not a straight-line stub ending in a RET within %d "
                          "bytes; stamp it --via disasm after the '# after regen' line"
                          % (i, s.line, b, addr, es2_stamp.RAW_LIMIT))
                    problems += 1
                elif extents.get(int(addr, 16)) not in (None, raw):
                    print("  RAW step %d (line %d): %s %s has a Ghidra function of %d bytes, the stub %d; "
                          "stamp it --via disasm" % (i, s.line, b, addr, extents[int(addr, 16)], raw))
                    problems += 1
            callers, negs = es2_stamp.claim_findings(descs.get((b, addr), (entry or {}).get("description", "")), negatives)
            for found, flag, label, key in ((callers, a.read_callers, "--read-callers", "callers"),
                                            (negs, a.own_negatives, "--own-negatives", "negs")):
                if found:
                    used[key] = True
                    if not flag:
                        problems += 1
                        for sent in found:
                            print("  REFUSE step %d (line %d): %s %s needs %s or a reword:\n      %s"
                                  % (i, s.line, b, addr, label, sent))
        for key, flag, label in (("callers", a.read_callers, "--read-callers"), ("negs", a.own_negatives, "--own-negatives")):
            if flag and not used[key]:
                print("  UNUSED step %d (line %d): no target needs %s; drop it" % (i, s.line, label))
    return problems, dropped_stamps(steps, start)


def dropped_stamps(steps, start=1):
    """Prints a STAMP warning for each edit from step `start` on that changes a stamped entry's description, which
    makes `es2_naming.py edit` drop the stamp, when no stamp line after the edit names the entry (by address, or by
    its name before or after the edit). Returns how many: a warning, since an edit made without reading the body
    rightly leaves the entry unstamped."""
    import es2_naming
    import es2_stamp
    import es2_symbols
    parser = es2_stamp.build_parser()
    stamps = []
    for i, s in enumerate(steps, 1):
        if s.argv[:2] == ["es2_stamp.py", "stamp"]:
            a = parser.parse_args(s.argv[1:])
            stamps.append((i, a.binary, {t.lower() if re.fullmatch(r"[0-9a-fA-F]{8}", t) else t
                                         for arg in a.target for t in arg.split("+") if t}))
    known, n = {}, 0
    for i, s in enumerate(steps, 1):
        if i < start or s.kind != "write" or s.argv[:2] != ["es2_naming.py", "edit"]:
            continue
        for ed in json.loads(json_path(s.argv[2]).read_text(encoding="utf-8")):
            b, addr = ed["binary"], ed["address"].lower()
            if b not in known:
                known[b] = {e["address"].lower(): e for e in es2_symbols.entries(b)}
            entry = known[b].get(addr)
            if not entry or "verified" not in entry:
                continue
            fields = es2_naming.edit_fields(ed, entry)
            if fields.get("description", entry.get("description")) == entry.get("description"):
                continue
            keys = {addr, entry.get("name"), fields.get("name")} - {None, ""}
            if any(j > i and sb == b and keys & targets for j, sb, targets in stamps):
                continue
            print("  STAMP step %d (line %d): %s %s %s is stamped and the edit changes its description, which drops the "
                  "stamp; no later stamp line names it. Stamp it after the edit once its body is read, or leave it "
                  "unstamped on purpose." % (i, s.line, b, addr, entry.get("name", "")))
            n += 1
    return n


def stamp_retry_line(argv, refused):
    """The stamp line again with only the refused targets, its options kept."""
    takes_value = {"--binary", "--scope", "--via", "--negative"}
    opts, it = [], iter(argv[2:])
    for tok in it:
        if tok in takes_value:
            opts += [tok, shlex.quote(next(it, ""))]
        elif tok.startswith("--"):
            opts.append(tok)
    return " ".join(["py", "tools/scripts/es2_stamp.py", "stamp", "+".join(refused)] + opts)


def run(argv, write=False):
    script = SCRIPTS / argv[0]
    cmd = [sys.executable, str(script)] + argv[1:] + (["--write"] if write else [])
    r = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, errors="replace")
    return r.returncode, (r.stdout + r.stderr)


def show(out, limit=25):
    lines = [l for l in out.splitlines() if l.strip()]
    if len(lines) > limit:
        print("   ... (%d lines)" % len(lines))
        lines = lines[-limit:]
    for l in lines:
        print("   " + l[:300])


def apply_ok(code, out):
    return code == 0 and re.search(r"^exit 0 ", out, re.M) and not re.search(r"errors=[1-9]", out)


def regen_ok(out):
    bad = [l for l in out.splitlines()
           if "names differ from the database" in l or "have no function at their address" in l
           or re.search(r"\(([1-9]\d*) failed\)", l)]
    return not bad, bad


def stage_list():
    r = subprocess.run(["git", "status", "--porcelain"], cwd=REPO, capture_output=True, text=True)
    paths = [l[3:] for l in r.stdout.splitlines() if l[3:] and "handoff-" not in l]
    return paths


def cmd_list(args):
    steps, skipped = parse(args.file)
    for i, s in enumerate(steps, 1):
        print("%3d  %s" % (i, s.label()))
    for n, line in skipped:
        print("skip line %d: %s" % (n, line))
    return 0


def stamp_via(source):
    """The --via a batch entry's `source` names: both for the decompile and the disassembly, else the one it names;
    None when it names neither."""
    s = (source or "").lower()
    dec, dis = "decompile" in s, "disassembly" in s or "disasm" in s
    return "both" if dec and dis else "decompile" if dec else "disasm" if dis else None


def cmd_stamps(args):
    """Prints the stamp lines for the function entries of the batch JSONs, grouped by binary and by how each entry's
    `source` says it was read (stamp_via). An entry with no Ghidra function is a --via raw stamp when it is a straight-line
    stub, otherwise a --via disasm one after the '# after regen' line, as precheck's RAW check wants. Flags
    (--read-callers, --own-negatives) are left to the author: precheck names the lines that need one."""
    import es2_stamp
    from retail_binaries import Image
    groups, unread, extents, images = {}, [], {}, {}
    for path in args.files:
        for e in json.loads(json_path(path).read_text(encoding="utf-8")):
            if e.get("type") != "function":
                continue
            b, addr = e["binary"], e["address"].lower()
            via = stamp_via(e.get("source"))
            if via is None:
                unread.append((b, addr))
                continue
            if b not in extents:
                extents[b], images[b] = es2_stamp.function_extents(b), Image(b)
            after = False
            if int(addr, 16) not in extents[b]:
                if es2_stamp.raw_extent(images[b], int(addr, 16)) is not None:
                    via = "raw"
                else:
                    via, after = "disasm", True
            groups.setdefault((after, b, via), []).append(addr)
    order = {"raw": 0, "disasm": 1, "decompile": 2, "both": 3}
    for after in (False, True):
        keys = sorted((k for k in groups if k[0] == after), key=lambda k: (k[1], order[k[2]]))
        if keys and after:
            print("# after regen: defined stubs that make a call")
        for _, b, via in keys:
            print("# stamps: %s bodies read %s" % (b, {"raw": "as straight-line stubs", "disasm": "in the disassembly",
                                                       "decompile": "in the decompile",
                                                       "both": "in the decompile and the disassembly"}[via]))
            print("py tools/scripts/es2_stamp.py stamp %s --binary %s --via %s" % ("+".join(groups[(after, b, via)]), b, via))
    for b, addr in unread:
        print("# not stamped: %s %s, whose source names neither the decompile nor the disassembly" % (b, addr))
    return 0


def cmd_precheck(args):
    steps, _ = parse(args.file)
    if args.fix:
        print("%d SHAPE form(s) rewritten" % fix_shapes(steps))
    bad, warned = precheck(steps)
    print("%d problem(s), %d warning(s)" % (bad, warned))
    return 1 if bad else 0


def cmd_run(args):
    steps, skipped = parse(args.file)
    for n, line in skipped:
        print("skip line %d (not a write, stamp or apply): %s" % (n, line))
    if not args.no_precheck:
        print("precheck (es2_stamp's wording checks on the stamp lines, before any write):")
        bad, warned = precheck(steps, args.start)
        if bad:
            print("\nSTOPPED before step %d: %d precheck problem(s). Fix those lines (or reword the JSON) and "
                  "run again; nothing was written." % (args.start, bad))
            return 1
        print("   clean" + (", %d STAMP warning(s) above: those entries end the run unstamped" % warned if warned else ""))
    for i, s in enumerate(steps, 1):
        if i < args.start:
            continue
        print("\n[%d/%d] %s" % (i, len(steps), s.label()))
        if s.kind == "write":
            code, out = run(s.argv)
            if code:
                show(out, 60)
                return stop(i, "the dry run failed")
            code, out = run(s.argv, write=True)
            show(out, 60 if s.argv[1] == "fixrefs" else 8)      # fixrefs lists the mentions left for a hand edit
            if code or "written" not in out:
                return stop(i, "the write failed")
        elif s.kind == "apply":
            code, out = run(s.argv)
            show(out, 6)
            if not apply_ok(code, out):
                return stop(i, "the read-only apply reported errors")
            code, out = run(s.argv, write=True)
            show(out, 4)
            if not apply_ok(code, out):
                return stop(i, "the apply reported errors")
        elif s.kind == "regen":
            argv = ["ghidra_full_decomp.py"] + [x for b in s.argv for x in ("--binary", b)]
            code, out = run(argv)
            ok, bad = regen_ok(out)
            show("\n".join(l for l in out.splitlines() if l.startswith("===") or "manually named" in l), 20)
            if code or not ok:
                show("\n".join(bad) or out, 40)
                return stop(i, "the regeneration reported a problem")
        elif s.kind == "check":
            code, out = run(["es2_stamp.py", "check"])
            show(out, 15)
            if code:
                return stop(i, "a stamp is void")
        else:
            code, out = run(s.argv)
            show(out, 60 if code else 10)
            if code:
                refused = list(dict.fromkeys(re.findall(r"^([0-9a-f]{8}) .*\bREFUSED\b", out, re.M)))
                if s.argv[:2] == ["es2_stamp.py", "stamp"] and refused:
                    print("\n   The line's other targets are stamped. Replace line %d with the refused ones only, "
                          "adding the flag they need or rewording them:\n   %s"
                          % (s.line, stamp_retry_line(s.argv, refused)))
                return stop(i, "it was refused or failed; rerun that line with the change it needs")
    print("\nDone. To stage (handoff docs left out):")
    for p in stage_list():
        print("   " + p)
    return 0


def stop(i, why):
    print("\nSTOPPED at step %d: %s. Resume with --from %d (or --from %d to skip it)." % (i, why, i, i + 1))
    return 1


FUNC_LINE = re.compile(r"^([0-9a-f]{8})\s+\d+\s+(?:(\S+)\s+)?(\S+)$")


def cmd_next(args):
    """Proposes the next batch's range: object files from START, by `es1_boilerplate.py bounds`'
    start list (zero fill ends and destructor copies), taken whole until about --target functions.
    Prints each file's counts and evidence lines, the held rows inside, and the brief command."""
    binary, lo = args.binary, int(args.start, 16)
    hi = lo + int(args.window, 16)
    _, bounds = run(["es1_boilerplate.py", "bounds", binary, "%08x" % lo, "%08x" % hi])
    m = re.search(r"starts the zero fill and destructor copies place: ([0-9a-f, ]+)", bounds)
    starts = sorted({lo} | {int(a, 16) for a in (m.group(1).split(", ") if m else []) if int(a, 16) >= lo})
    # Each evidence line at the code address it is about: after "->" for a table entry, else its first.
    evidence = [(int(e.group(1), 16), l.strip()) for l in bounds.splitlines()
                for e in [re.search(r"-> ([0-9a-f]{8})", l) or re.search(r"\b([0-9a-f]{8})\b", l)]
                if e and not l.startswith(binary) and "starts the" not in l]
    _, funcs = run(["es2_naming.py", "funcs", binary, "%08x" % lo, "%08x" % hi])
    rows = [(int(r[0], 16), r[1] or "") for r in (m_.groups() for m_ in map(FUNC_LINE.match, funcs.splitlines()) if m_)
            if "p" not in (r[1] or "")]
    if len(starts) < 2:
        print("no object-file start found in %08x-%08x after %08x; widen --window" % (lo, hi, lo))
        return 1
    held = {int(h, 16) for h in args.held.split("+") if h} if args.held else set()
    # The target counts functions still to read (not stamped); a file with none ends the batch.
    files, total, why = [], 0, "the window ends"
    for a, b in zip(starts, starts[1:]):
        fs = [r for r in rows if a <= r[0] < b]
        n = sum("S" not in r[1] for r in fs)
        if files and n == 0:
            why = "the next file is all stamped"
            break
        if files and (total + n > args.target * 1.5 or total >= args.target):
            why = "the target is reached"
            break
        files.append((a, b - 1, len(fs), sum("*" in r[1] for r in fs), len(fs) - n))
        total += n
    end = files[-1][1]
    print("%s %08x-%08x: %d object file(s), %d functions to read, %d in all (padding entries left out); "
          "stopped because %s" % (binary, lo, end, len(files), total, sum(f[2] for f in files), why))
    for a, b, n, unnamed, stamped in files:
        print("  %08x-%08x  %3d functions, %3d unnamed, %3d stamped" % (a, b, n, unnamed, stamped))
        for at, line in evidence:
            if a <= at <= b and not line.startswith("zero fill"):
                print("      " + line)
    inside = sorted(h for h in held if lo <= h <= end)
    if inside:
        print("  held rows inside: " + " ".join("%08x" % h for h in inside))
    nxt = starts[len(files)] if len(files) < len(starts) else None
    print("next file after it: %s" % ("%08x" % nxt if nxt is not None else "past the window"))
    print("brief: py tools/scripts/es1_batch.py brief N %08x %08x --scratch <dir> --notes <notes> --auto" % (lo, end))
    return 0


def auto_leads(binary, lo, hi):
    """The mechanical part of a batch's notes: the object-file evidence over the range with a 0x100
    margin, its function counts, es2_naming.py match's candidates, and the docs that already name one of
    its function addresses. Leads only; the notes still give what the range is and its Open items."""
    a, b = int(lo, 16), int(hi, 16)
    out = ["**Leads the tools found** (`es1_batch.py brief --auto`; each is a lead to settle, not a fact):", ""]
    _, bounds = run(["es1_boilerplate.py", "bounds", binary, "%08x" % max(0, a - 0x100), "%08x" % (b + 0x100)])
    out += ["`es1_boilerplate.py bounds %s %08x %08x`:" % (binary, max(0, a - 0x100), b + 0x100), "```",
            *[l for l in bounds.splitlines() if l.strip() and not l.startswith("warning:")], "```", ""]
    _, funcs = run(["es2_naming.py", "funcs", binary, lo, hi])
    rows = [m.groups() for m in map(FUNC_LINE.match, funcs.splitlines()) if m]
    mark = lambda r, c: c in (r[1] or "")  # noqa: E731
    real = [r for r in rows if not mark(r, "p")]
    out += ["`es2_naming.py funcs`: %d functions (padding entries left out), %d unnamed, %d stamped."
            % (len(real), sum(mark(r, "*") for r in real), sum(mark(r, "S") for r in real)), ""]
    _, match = run(["es2_naming.py", "match", binary, "DBSIM+VSHELL", "--range", "%s-%s" % (lo, hi)])
    cands = [l.rstrip() for l in match.splitlines() if re.match(r"[0-9a-f]{8} ", l) or l.startswith(binary + " against")]
    out += ["`es2_naming.py match %s DBSIM+VSHELL --range %s-%s`:" % (binary, lo, hi), "```", *cands, "```", ""]
    addrs = {r[0] for r in real}
    hits = {}
    if addrs:
        rx = re.compile(r"(?<![0-9a-fA-F])(" + "|".join(sorted(addrs)) + r")(?![0-9a-fA-F])")
        for p in sorted((REPO / "Herculan" / "docs").rglob("*.md")):
            if p.name.startswith("handoff-"):
                continue
            found = sorted(set(rx.findall(p.read_text(encoding="utf-8", errors="replace"))))
            if found:
                hits[p.relative_to(REPO).as_posix()] = found
    out.append("Docs naming one of the range's function addresses (an Open item there may be this batch's to close):")
    out += ["- `%s`: %s" % (p, ", ".join("`%s`" % f for f in found)) for p, found in hits.items()] or ["- none"]
    return "\n".join(out)


READ_BUDGET = 40 * 1024
READ_EXTENSIONS = {"md", "cs", "py", "txt", "json", "c", "java", "ps1"}
READ_TOKEN = re.compile(r'`([^`]+)`|"([^"]+)"')


def keep_blocks(text, keep):
    """The template with each `<!-- tag -->` ... `<!-- /tag -->` block kept, without its marker lines, when its tag is
    in `keep`, and dropped otherwise."""
    return re.sub(r"^<!-- (\w+) -->\n(.*?)^<!-- /\1 -->\n", lambda m: m.group(2) if m.group(1) in keep else "",
                  text, flags=re.S | re.M)


def merge_notes(text, notes):
    """Moves each `## Heading` section of the notes whose heading the template also has (whole, or before a space or a
    parenthesis: "Report" joins "Report (at most 400 words)") to the end of the template's section, so a part's own
    read list, limits or report items join the shared ones. Returns the template and what is left of the notes for
    @@NOTES@@."""
    parts = re.split(r"^## (.+?)[ \t]*\n", notes, flags=re.M)
    rest = [parts[0].strip()]
    for heading, body in zip(parts[1::2], parts[2::2]):
        m = re.search(r"^## %s(?:[ \t(].*)?\n" % re.escape(heading), text, flags=re.M)
        if not m:
            rest.append("## %s\n\n%s" % (heading, body.strip()))
            continue
        nxt = re.compile(r"^## ", re.M).search(text, m.end())
        end = nxt.start() if nxt else len(text)
        text = text[:end].rstrip("\n") + "\n" + body.strip() + "\n\n" + text[end:].lstrip("\n")
    return text, "\n\n".join(p for p in rest if p)


def md_section(text, heading):
    """The text of the markdown section whose heading is `heading` (any level); see md_section_span."""
    span = md_section_span(text, heading)
    return span[2] if span else None


def md_section_span(text, heading):
    """(first line, last line, text) of the markdown section whose heading is `heading` (any level), from the heading
    to the next one of its level or higher, the lines numbered from 1 as the Read tool numbers them; a heading matches
    by its text with backticks and link targets removed, whole or followed by more words, or by its anchor. None when
    no heading matches."""
    def norm(s):
        s = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", s).replace("`", "")
        return re.sub(r"\s+", " ", s).strip().lower()

    def slug(s):
        return re.sub(r"[^\w\- ]", "", norm(s)).replace(" ", "-")

    lines = text.splitlines(keepends=True)
    heads, fence = [], False
    for i, line in enumerate(lines):
        if line.startswith("```"):
            fence = not fence
        elif not fence and (m := re.match(r"(#{1,6})\s+(.*?)\s*$", line)):
            heads.append((i, len(m.group(1)), m.group(2)))
    want = norm(heading)
    for k, (i, level, h) in enumerate(heads):
        if norm(h) == want or norm(h).startswith(want + " ") or slug(h) == heading.lower():
            end = next((j for j, lv, _ in heads[k + 1:] if lv <= level), len(lines))
            return i + 1, end, "".join(lines[i:end])
    return None


def read_list(brief):
    """The files the brief's "Read first" bullets name, as (path, sections, edit, base): each backticked path with the
    quoted section headings after it (or a `#fragment`), `(edit)` on its bullet for a doc read whole because it is
    edited, and the backticked folder (ending in a slash) an earlier token of the bullet named, for bare file names."""
    section = md_section(brief, "Read first") or ""
    items = []
    for line in section.splitlines():
        if not line.lstrip().startswith("- "):
            continue
        edit, base, current = "(edit)" in line, None, None
        for m in READ_TOKEN.finditer(line):
            tok, quoted = m.groups()
            if tok is None:
                if current is not None:
                    current[1].append(quoted)
            elif re.search(r"[\\/]$", tok):
                base = tok
            elif re.fullmatch(r"[^\s#]+\.([A-Za-z]\w*)(#[\w-]+)?", tok) and tok.split("#")[0].rsplit(".", 1)[1].lower() in READ_EXTENSIONS:
                path, _, frag = tok.partition("#")
                current = [path, [frag] if frag else [], edit, base]
                items.append(current)
    return items


def gate_reading(brief, scratch, over_budget):
    """Totals the brief's fixed reading: each file its read list names, whole or by its named sections. The brief's
    own size is printed beside it, not counted; a doc marked (edit) and a file in the scratch folder (the part's own
    input) are listed but not counted. Stops with nothing written when a file or section is not found, or when the
    total passes READ_BUDGET and no --over-budget reason is given. Returns {(path, heading): (first, last)}, the line
    range of each named section, for annotate_ranges."""
    total, ranges = 0, {}
    rows, errors = [("%5.1f KB" % (len(brief.encode("utf-8")) / 1024), "the brief itself (not counted)")], []
    scratch_dir = Path(scratch).resolve()
    for path, sections, edit, base in read_list(brief):
        p = Path(path)
        cands = [p] if p.is_absolute() else ([Path(base) / p] if base else []) + [REPO / p]
        f = next((c for c in cands if c.is_file()), None)
        if f is None:
            errors.append("no file %s" % path)
            continue
        if edit or scratch_dir in f.resolve().parents:
            rows.append(("     --", "%s (%s, not counted)" % (path, "edited" if edit else "the part's input")))
            continue
        text = f.read_text(encoding="utf-8", errors="replace")
        if not sections:
            size = len(text.encode("utf-8"))
            rows.append(("%5.1f KB" % (size / 1024), path + ", whole"))
        else:
            size = 0
            for s in sections:
                span = md_section_span(text, s)
                if span is None:
                    errors.append('no section "%s" in %s' % (s, path))
                    continue
                ranges[(path, s)] = span[:2]
                size += len(span[2].encode("utf-8"))
            rows.append(("%5.1f KB" % (size / 1024), "%s: %s" % (path, ", ".join('"%s"' % s for s in sections))))
        total += size
    for size, label in rows:
        print("  %s  %s" % (size, label))
    print("  %5.1f KB  fixed reading, budget %d KB" % (total / 1024, READ_BUDGET // 1024))
    if errors:
        raise SystemExit("not written: " + "; ".join(errors))
    if total > READ_BUDGET:
        if not over_budget:
            raise SystemExit("not written: the fixed reading is over the budget. Cut the read list (sections, not whole "
                             "docs; mark docs the part edits (edit)), or pass --over-budget \"<reason>\".")
        print("  over budget: " + over_budget)
    return ranges


def annotate_ranges(brief, ranges):
    """The brief with each section the "Read first" bullets name followed by its line range, `"Heading" (lines a–b)`,
    so a subagent reads it with an offset and a limit rather than the whole doc."""
    section = md_section(brief, "Read first")
    if not section or not ranges:
        return brief
    out = []
    for line in section.splitlines(keepends=True):
        if line.lstrip().startswith("- "):
            path = None

            def mark(m):
                # The same path test as read_list, so a backticked name that is not a file leaves the path as it was.
                nonlocal path
                tok, quoted = m.groups()
                key = None
                if tok is None:
                    key = (path, quoted) if path else None
                elif re.fullmatch(r"[^\s#]+\.([A-Za-z]\w*)(#[\w-]+)?", tok) and tok.split("#")[0].rsplit(".", 1)[1].lower() in READ_EXTENSIONS:
                    path, _, frag = tok.partition("#")
                    key = (path, frag) if frag else None
                span = ranges.get(key) if key else None
                return m.group(0) + (" (lines %d–%d)" % span if span else "")

            line = READ_TOKEN.sub(mark, line)
        out.append(line)
    return brief.replace(section, "".join(out), 1)


def write_brief(template, values, notes, keep, args):
    """Fills `template` (a file in tools/scripts) with its blocks for `keep`, the notes merged into it and `values`,
    gates its reading and writes it to --out or <scratch>/brief.md."""
    text = keep_blocks((SCRIPTS / template).read_text(encoding="utf-8"), keep)
    text, rest = merge_notes(text, notes)
    for k, v in list(values.items()) + [("NOTES", rest)]:
        text = text.replace("@@%s@@" % k, v)
    text = re.sub(r"\n{3,}", "\n\n", text)
    left = re.findall(r"@@\w+@@", text)
    if left:
        raise SystemExit("unfilled placeholders: " + " ".join(sorted(set(left))))
    text = annotate_ranges(text, gate_reading(text, args.scratch, args.over_budget))
    out =Path(args.out) if args.out else Path(args.scratch) / "brief.md"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    print("%s: %d words" % (out, len(text.split())))
    return 0


def notes_text(args):
    return Path(args.notes).read_text(encoding="utf-8").strip() if args.notes else ""


def cmd_brief(args):
    """Fills es1_batch_brief.md with the batch number, range, scratch folder and the batch's own notes; with --doc,
    its doc-batch blocks in place of the range's."""
    values = {"BATCH": str(args.batch), "SCRATCH": args.scratch.replace("/", "\\"), "BINARY": args.binary}
    notes = notes_text(args)
    if args.doc:
        if args.lo or args.auto:
            raise SystemExit("a --doc brief reads no range: drop LO HI and --auto")
        if not notes:
            raise SystemExit("a --doc brief needs --notes: the port parts, their details files and the docs")
        return write_brief("es1_batch_brief.md", values, notes, {"doc"}, args)
    if not args.hi:
        raise SystemExit("brief needs LO HI, or --doc for a doc batch")
    lo, hi = args.lo.lower().zfill(8), args.hi.lower().zfill(8)
    if args.auto:
        notes = (notes + "\n\n" if notes else "") + auto_leads(args.binary, lo, hi)
    return write_brief("es1_batch_brief.md", dict(values, LO=lo, HI=hi), notes, {"range"}, args)


def cmd_port(args):
    """Fills es1_port_brief.md with the part's name, title, binary, scratch folder, budget and its own notes."""
    return write_brief("es1_port_brief.md", {"PART": args.part, "TITLE": args.title, "BUDGET": str(args.budget),
                                             "BINARY": args.binary, "SCRATCH": args.scratch.replace("/", "\\")},
                       notes_text(args), set(), args)


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("brief", help="write a batch subagent's brief from es1_batch_brief.md")
    b.add_argument("batch", type=int)
    b.add_argument("lo", nargs="?", help="the range's first address (not with --doc)")
    b.add_argument("hi", nargs="?", help="the range's last address (not with --doc)")
    b.add_argument("--doc", action="store_true",
                   help="a doc batch's brief: no range, bounds, scan or match; the notes name the port parts' details files")
    b.add_argument("--scratch", required=True, help="the batch's scratch folder, named in the brief")
    b.add_argument("--notes", help="a file with the batch's own section: its range's contents, leads, docs to read")
    b.add_argument("--out", help="where to write the brief (default <scratch>/brief.md)")
    b.add_argument("--auto", action="store_true",
                   help="append the tools' leads: bounds, function counts, match candidates, docs naming the range")
    b.add_argument("--binary", default="ES1DBSIM", choices=("ES1DBSIM", "ES1GO", "ES1ESCD", "ES1PLAYFLIC"),
                   help="the binary the batch reads: the range's, whose dumps the brief names and --auto reads (default ES1DBSIM)")
    pt = sub.add_parser("port", help="write a port subagent's brief from es1_port_brief.md")
    pt.add_argument("part", help="the part's name, M1")
    pt.add_argument("--title", required=True, help="what the part ports, for the brief's heading")
    pt.add_argument("--scratch", required=True, help="the part's scratch folder, named in the brief")
    pt.add_argument("--notes", help="a file with the part's own section: context, the user's choices, ## Scope, and "
                                    "## Read first / ## Code / ## Limits / ## Report items to add to the template's")
    pt.add_argument("--budget", type=int, default=150, help="the tool-call budget (default 150)")
    pt.add_argument("--binary", default="ES1DBSIM", choices=("ES1DBSIM", "ES1GO", "ES1ESCD", "ES1PLAYFLIC"),
                    help="the binary the part ports from, whose dumps the brief names (default ES1DBSIM)")
    pt.add_argument("--out", help="where to write the brief (default <scratch>/brief.md)")
    for sp in (b, pt):
        sp.add_argument("--over-budget", metavar="REASON",
                        help="write the brief though its fixed reading passes the budget, for this reason")
    r = sub.add_parser("run", help="run the file's commands, then apply, regenerate and check")
    r.add_argument("file")
    r.add_argument("--from", dest="start", type=int, default=1, help="resume at this step number")
    r.add_argument("--no-precheck", action="store_true", help="skip the wording precheck of the stamp lines")
    nx = sub.add_parser("next", help="propose the next batch's range: whole object files from START to about --target functions")
    nx.add_argument("start", help="the address the batch starts at, a file's first byte")
    nx.add_argument("--target", type=int, default=60, help="about how many functions (default 60)")
    nx.add_argument("--window", default="10000", help="hex bytes past START to look for file starts (default 10000)")
    nx.add_argument("--binary", default="ES1DBSIM")
    nx.add_argument("--held", help="held rows to report when inside, a+b+c")
    pc = sub.add_parser("precheck", help="the stamp lines' wording checks against the batch JSONs; writes nothing")
    pc.add_argument("file")
    pc.add_argument("--fix", action="store_true", help="first rewrite the SHAPE forms in the batch JSONs in place")
    ls = sub.add_parser("list", help="print the steps the run would take, numbered")
    ls.add_argument("file")
    st = sub.add_parser("stamps", help="print the stamp lines for the batch JSONs' function entries, by how each was read")
    st.add_argument("files", nargs="+", metavar="BATCH.json")
    args = p.parse_args()
    return {"run": cmd_run, "list": cmd_list, "brief": cmd_brief, "port": cmd_port, "precheck": cmd_precheck,
            "next": cmd_next, "stamps": cmd_stamps}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
