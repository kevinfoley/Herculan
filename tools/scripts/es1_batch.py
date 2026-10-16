"""Runs an ES1 naming batch's write commands, then applies, regenerates and checks.

A batch subagent cannot touch the known_symbols files, so it lists the writes in a command file
(handoff-es1-data-formats.md, "How the batches were run"). This runs that file:

1. Each `es2_naming.py insert|relink|edit` line, as a dry run and then with --write. A line may be
   given in either form or both; consecutive duplicates collapse. The doubling of a renamed name
   (`Name (Name (addr))`) is undone by es2_naming.py itself.
2. Each `es1_naming.py twin` and `es2_stamp.py stamp` line as given. A stamp refusal stops the run:
   read the refused sentences, then rerun the line with the flag it needs (or reword) and resume.
3. Each `es2_naming.py apply BIN` line read-only, then with --write when the read-only run reported
   errors=0 and exit 0.
4. ghidra_full_decomp.py for every applied binary, stopping when it reports a known_symbols name that
   differs from the database, one with no function at its address, or a failed decompile.
5. The lines after a comment line containing "after regen" (stamps of defined stubs that make a call,
   which `--via raw` cannot read), then `es2_stamp.py check`.
6. The paths to stage, the untracked handoff docs left out.

Only those tools and subcommands run; any other line (grep, git, shell) is skipped with a note,
since the file is a subagent's output. Comment and blank lines are ignored.

Usage:
    python tools/scripts/es1_batch.py run COMMANDS.txt [--from N]
    python tools/scripts/es1_batch.py list COMMANDS.txt
"""
from __future__ import annotations

import argparse
import re
import shlex
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SCRIPTS = REPO / "tools" / "scripts"
ALLOWED = {
    "es2_naming.py": {"insert", "relink", "edit", "apply"},
    "es2_stamp.py": {"stamp"},
    "es1_naming.py": {"twin"},
}
WRITES = {"insert", "relink", "edit"}
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


def cmd_run(args):
    steps, skipped = parse(args.file)
    for n, line in skipped:
        print("skip line %d (not a write, stamp or apply): %s" % (n, line))
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
            show(out, 8)
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
                return stop(i, "it was refused or failed; rerun that line with the change it needs")
    print("\nDone. To stage (handoff docs left out):")
    for p in stage_list():
        print("   " + p)
    return 0


def stop(i, why):
    print("\nSTOPPED at step %d: %s. Resume with --from %d (or --from %d to skip it)." % (i, why, i, i + 1))
    return 1


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run", help="run the file's commands, then apply, regenerate and check")
    r.add_argument("file")
    r.add_argument("--from", dest="start", type=int, default=1, help="resume at this step number")
    ls = sub.add_parser("list", help="print the steps the run would take, numbered")
    ls.add_argument("file")
    args = p.parse_args()
    return {"run": cmd_run, "list": cmd_list}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
