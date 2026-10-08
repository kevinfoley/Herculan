#!/usr/bin/env python3
"""PreToolUse hook: reject a search whose result is cut by head/tail.

`grep X dump.txt | head` shows the first ten matches and says nothing about the
rest, so its output cannot support "these are all the callers" or "this field is
only read here". This hook denies a Bash or PowerShell pipeline in which a stage
that enumerates matches (grep, egrep, fgrep, rg, ag, ack, git grep, awk with a
pattern, findstr, Select-String) is followed later in the same pipeline by a
truncator (head, tail, `sed -n A,Bp`, `sed Nq`, `awk 'NR<=N'`,
`Select-Object -First/-Last/-Index`), with no `wc` in between.

It applies only where completeness matters: the RE and docs corpora listed in
SCOPE, in the main checkout and in every linked worktree, since the hook always
runs the main checkout's copy of this script. A search is in scope when a path
operand lies under one of them, when a
recursive search (rg, git grep, grep -r, findstr /s, or `find … | xargs grep`)
starts at a directory containing one, when the search reads stdin from an
in-scope file (`cat X | grep …`, `grep … < X`), or when it has no path at all and
the working directory is in scope. `cd` / `Set-Location` statements earlier in
the command move the working directory.

`# sample-ok` anywhere in the command opts out.

Wire up in .claude/settings.json:

    "PreToolUse": [
      { "matcher": "Bash|PowerShell",
        "hooks": [ { "type": "command",
                     "command": "python \"${CLAUDE_PROJECT_DIR:-.}/tools/scripts/truncated_enumeration_guard.py\"" } ] }
    ]

`--self-test` runs the built-in cases instead of reading a hook payload.
"""

from __future__ import annotations

import json
import os
import re
import shlex
import sys
from dataclasses import dataclass, field

import retail_binaries

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

SCOPE = [
    "tools/analysis_out",
    *(f"tools/ghidra_scripts/known_symbols_{b.lower()}.json" for b in retail_binaries.IDS),
    "Herculan/docs",
    "Herculan/src",
]

OPT_OUT = re.compile(r"#\s*sample-ok\b")

DENY_REASON = (
    "Truncated enumeration: this search result is cut by head/tail, so it cannot "
    "support a completeness claim. Count first (`| wc -l`), then read the whole "
    "result, or add `# sample-ok` if a sample is all you need."
)

STATEMENT_OPS = {";", "&&", "||", "&", "\n"}
PIPE_OPS = {"|", "|&"}
REDIRECT_OPS = {"<", ">", ">>", ">&", "&>", "<<", "<&", "&>>", ">|"}
OPERATORS = sorted(STATEMENT_OPS | PIPE_OPS | REDIRECT_OPS | {"(", ")"}, key=len, reverse=True)

# Stages that pass file content (or a file list) down a pipeline.
SOURCES = {
    "cat", "type", "gc", "get-content", "sed", "head", "tail", "tac", "sort", "uniq",
    "cut", "less", "more", "strings", "xxd", "find", "ls", "dir", "gci", "get-childitem",
}
# Sources that walk a directory tree.
RECURSIVE_SOURCES = {"find", "gci", "get-childitem", "dir", "ls"}
# A stage that turns an enumeration into a count, after which truncation is harmless.
RESETS = {"wc", "measure-object", "measure"}
CD = {"cd", "pushd", "set-location", "sl", "chdir"}

GREP_VALUE_SHORT = "efmABCdD"
GREP_VALUE_LONG = {
    "--regexp", "--file", "--max-count", "--after-context", "--before-context", "--context",
    "--include", "--exclude", "--exclude-dir", "--exclude-from", "--devices", "--directories",
    "--label", "--binary-files",
}
RG_VALUE_SHORT = "efgtTmABCMjr"
RG_VALUE_LONG = {
    "--regexp", "--file", "--glob", "--iglob", "--type", "--type-not", "--type-add",
    "--max-count", "--after-context", "--before-context", "--context", "--replace",
    "--max-columns", "--threads", "--max-depth", "--encoding", "--sort", "--sortr",
    "--colors", "--pre", "--ignore-file", "--path-separator", "--max-filesize",
}
SLS_SWITCHES = {
    "simplematch", "casesensitive", "allmatches", "notmatch", "list", "quiet", "raw",
    "noemphasis",
}


# --- tokenising ------------------------------------------------------------------------------


@dataclass
class Stage:
    args: list[str]
    stdin_paths: list[str] = field(default_factory=list)


def split_operators(tok: str) -> list[str]:
    out = []
    while tok:
        for op in OPERATORS:
            if tok.startswith(op):
                out.append(op)
                tok = tok[len(op):]
                break
        else:
            tok = tok[1:]  # stray punctuation
    return out


def lex(command: str, posix: bool) -> list[tuple[str, bool]] | None:
    """Tokens as (text, is_operator), quote-aware so `grep "a|b"` stays one token."""
    lexer = shlex.shlex(command, posix=posix, punctuation_chars="();<>|&\n")
    lexer.whitespace = " \t\r"
    lexer.whitespace_split = True
    out: list[tuple[str, bool]] = []
    try:
        for tok in lexer:
            if tok and all(c in "();<>|&\n" for c in tok):
                out.extend((op, True) for op in split_operators(tok))
            else:
                if not posix and len(tok) >= 2 and tok[0] in "'\"" and tok[-1] == tok[0]:
                    tok = tok[1:-1]
                out.append((tok, False))
    except ValueError:
        return None
    return out


def statements(command: str, posix: bool) -> list[list[Stage]] | None:
    """Each statement as a list of pipeline stages."""
    toks = lex(command, posix)
    if toks is None:
        return None
    result: list[list[Stage]] = []
    pipeline: list[Stage] = []
    stage = Stage([])
    i = 0
    while i < len(toks):
        text, is_op = toks[i]
        if not is_op:
            stage.args.append(text)
        elif text in REDIRECT_OPS:
            # `2>/dev/null`: the fd number is not an operand.
            if stage.args and stage.args[-1].isdigit() and text != "<":
                stage.args.pop()
            if i + 1 < len(toks) and not toks[i + 1][1]:
                if text == "<":
                    stage.stdin_paths.append(toks[i + 1][0])
                i += 1
        elif text in PIPE_OPS:
            pipeline.append(stage)
            stage = Stage([])
        elif text in STATEMENT_OPS:
            pipeline.append(stage)
            result.append([s for s in pipeline if s.args])
            pipeline, stage = [], Stage([])
        i += 1
    pipeline.append(stage)
    result.append([s for s in pipeline if s.args])
    return [p for p in result if p]


# --- paths -----------------------------------------------------------------------------------


def norm(path: str, cwd: str) -> str:
    path = path.replace("\\", "/")
    m = re.match(r"^/([a-zA-Z])(/|$)", path)  # Git Bash /e/... -> e:/...
    if m:
        path = f"{m.group(1)}:/{path[3:]}"
    if not os.path.isabs(path) and not re.match(r"^[a-zA-Z]:", path):
        path = os.path.join(cwd, path)
    return os.path.normcase(os.path.normpath(path))


def checkouts(root: str) -> list[str]:
    """Every working tree of this repository: the main checkout, then each linked worktree.

    Read from git's own registry, `.git/worktrees/<name>/gitdir`, which holds the path of the
    worktree's `.git` file; no git process runs on every tool call.
    """
    main = root
    git = os.path.join(root, ".git")
    if os.path.isfile(git):  # run from a linked worktree's copy
        with open(git, encoding="utf-8") as fh:
            m = re.match(r"gitdir:\s*(.+?)[\\/]+\.git[\\/]+worktrees[\\/]", fh.read().strip())
        if m:
            main = os.path.normpath(os.path.join(root, m.group(1)))
    roots = [main]
    registry = os.path.join(main, ".git", "worktrees")
    try:
        names = sorted(os.listdir(registry))
    except OSError:
        names = []
    for name in names:
        try:
            with open(os.path.join(registry, name, "gitdir"), encoding="utf-8") as fh:
                roots.append(os.path.dirname(os.path.join(registry, name, fh.read().strip())))
        except OSError:
            pass
    return roots


CHECKOUTS = checkouts(REPO_ROOT)
SCOPE_ROOTS = [norm(s, root) for root in CHECKOUTS for s in SCOPE]


def under(path: str, root: str) -> bool:
    return path == root or path.startswith(root.rstrip(os.sep) + os.sep)


def in_scope(path: str, cwd: str, recursive: bool) -> bool:
    p = norm(path, cwd)
    if any(under(p, r) for r in SCOPE_ROOTS):
        return True
    return recursive and any(under(r, p) for r in SCOPE_ROOTS)


# --- stage classification --------------------------------------------------------------------


@dataclass
class Enumeration:
    paths: list[str]
    recursive: bool
    reads_file_list: bool = False  # `xargs grep`: stdin names files rather than holding text


def command_name(args: list[str]) -> str:
    return os.path.basename(args[0].replace("\\", "/")).lower().removesuffix(".exe")


def grep_like_operands(args: list[str], value_short: str, value_long: set[str]) -> list[str]:
    """Non-option operands after dropping the pattern (unless -e/-f supplied it)."""
    operands: list[str] = []
    pattern_given = False
    end_opts = False
    i = 0
    while i < len(args):
        a = args[i]
        if not end_opts and a == "--":
            end_opts = True
        elif not end_opts and a.startswith("--"):
            name = a.split("=", 1)[0]
            if name in ("--regexp", "--file"):
                pattern_given = True
            if "=" not in a and name in value_long:
                i += 1
        elif not end_opts and a.startswith("-") and len(a) > 1:
            for k, ch in enumerate(a[1:], 1):
                if ch in value_short:
                    if ch in "ef":
                        pattern_given = True
                    if k == len(a) - 1:
                        i += 1  # value is the next token
                    break
        else:
            operands.append(a)
        i += 1
    return operands if pattern_given else operands[1:]


def grep_recursive(args: list[str]) -> bool:
    for a in args:
        if a == "--":
            break
        if a in ("--recursive", "--dereference-recursive"):
            return True
        if a.startswith("-") and not a.startswith("--"):
            for ch in a[1:]:
                if ch in GREP_VALUE_SHORT:
                    break  # the rest of the cluster is a value
                if ch in "rR":
                    return True
    return False


def awk_program(args: list[str]) -> tuple[str | None, list[str]]:
    """(program text or None when read from -f, file operands)."""
    i, program, from_file = 0, None, False
    while i < len(args):
        a = args[i]
        if a in ("-F", "-v"):
            i += 2
            continue
        if a == "-f":
            from_file = True
            i += 2
            continue
        if a.startswith("-") and len(a) > 1:
            i += 1
            continue
        break
    rest = args[i:]
    if from_file:
        return None, rest
    if not rest:
        return "", []
    program, files = rest[0], rest[1:]
    return program, files


def awk_has_pattern(program: str | None) -> bool:
    if program is None:
        return True
    head = program.split("{", 1)[0]
    return bool(head.strip()) or "{" not in program


def enumeration(stage: Stage) -> Enumeration | None:
    args = stage.args
    name = command_name(args)
    rest = args[1:]

    if name == "xargs":
        i = 0
        while i < len(rest) and rest[i].startswith("-"):
            i += 2 if rest[i] in ("-n", "-I", "-P", "-d", "-L", "-E", "-s", "-a") else 1
        if i >= len(rest):
            return None
        inner = enumeration(Stage(rest[i:]))
        if inner:
            inner.reads_file_list = True
        return inner

    if name in ("grep", "egrep", "fgrep"):
        return Enumeration(grep_like_operands(rest, GREP_VALUE_SHORT, GREP_VALUE_LONG), grep_recursive(rest))

    if name in ("rg", "ag", "ack"):
        return Enumeration(grep_like_operands(rest, RG_VALUE_SHORT, RG_VALUE_LONG), True)

    if name == "git":
        i = 0
        while i < len(rest) and rest[i].startswith("-"):
            i += 2 if rest[i] in ("-C", "-c") else 1
        if i < len(rest) and rest[i] == "grep":
            return Enumeration(grep_like_operands(rest[i + 1:], GREP_VALUE_SHORT, GREP_VALUE_LONG), True)
        return None

    if name == "awk" or name == "gawk":
        program, files = awk_program(rest)
        if awk_has_pattern(program):
            return Enumeration(files, False)
        return None

    if name == "findstr":
        opts = [a for a in rest if a.startswith("/")]
        operands = [a for a in rest if not a.startswith("/")]
        if not any(o[:3].lower() in ("/c:", "/g:") for o in opts):
            operands = operands[1:]
        return Enumeration(operands, any(o.lower() == "/s" for o in opts))

    if name in ("select-string", "sls"):
        positional, paths = [], []
        pattern_named = False
        i = 0
        while i < len(rest):
            a = rest[i]
            if a.startswith("-") and len(a) > 1:
                pname, _, attached = a[1:].partition(":")
                pname = pname.lower()
                if pname in SLS_SWITCHES:
                    i += 1
                    continue
                value = attached or (rest[i + 1] if i + 1 < len(rest) else "")
                if not attached:
                    i += 1
                if pname in ("path", "literalpath", "lp", "pspath") or (
                    len(pname) >= 3 and "literalpath".startswith(pname)
                ):
                    paths.extend(v for v in value.split(",") if v)
                elif len(pname) >= 3 and "pattern".startswith(pname):
                    pattern_named = True
                i += 1
                continue
            positional.append(a)
            i += 1
        # Positional order is Pattern, Path; a named -Pattern leaves Path first.
        for p in positional if pattern_named else positional[1:]:
            paths.extend(v for v in p.split(",") if v)
        return Enumeration(paths, False)

    return None


def is_truncator(stage: Stage) -> bool:
    args = stage.args
    name = command_name(args)
    rest = args[1:]
    if name in ("head", "tail"):
        return True
    if name == "sed":
        scripts = [a for a in rest if not a.startswith("-")]
        quiet = any(a == "-n" or (a.startswith("-") and not a.startswith("--") and "n" in a) for a in rest)
        for s in scripts:
            if re.fullmatch(r"\d+q", s):
                return True
            if quiet and re.fullmatch(r"\d+(,\d+)?p", s):
                return True
        return False
    if name in ("awk", "gawk"):
        program, _ = awk_program(rest)
        return bool(program and re.search(r"\bNR\s*(<=?|==)\s*\d", program))
    if name in ("select-object", "select"):
        for a in rest:
            if a.startswith("-") and len(a) > 1:
                pname = a[1:].split(":", 1)[0].lower()
                if "first".startswith(pname) or "last".startswith(pname) or (
                    len(pname) >= 2 and "index".startswith(pname)
                ):
                    return True
    return False


def source_in_scope(stage: Stage, cwd: str) -> bool:
    """An earlier stage feeding in-scope content or file names down the pipeline."""
    if any(in_scope(p, cwd, False) for p in stage.stdin_paths):
        return True
    name = command_name(stage.args)
    if name not in SOURCES:
        return False
    recursive = name in RECURSIVE_SOURCES
    operands = [a for a in stage.args[1:] if not a.startswith("-")]
    if recursive and not operands:
        operands = ["."]
    return any(in_scope(o, cwd, recursive) for o in operands)


def pipeline_denied(stages: list[Stage], cwd: str) -> bool:
    for i, stage in enumerate(stages):
        enum = enumeration(stage)
        if enum is None:
            continue
        truncated = False
        for later in stages[i + 1:]:
            if command_name(later.args) in RESETS:
                break
            if is_truncator(later):
                truncated = True
                break
        if not truncated:
            continue

        if any(in_scope(p, cwd, enum.recursive) for p in enum.paths + stage.stdin_paths):
            return True
        if not enum.paths and not stage.stdin_paths:
            if enum.recursive and not enum.reads_file_list and in_scope(".", cwd, True):
                return True
            if i == 0 and in_scope(".", cwd, False):
                return True
            if any(source_in_scope(s, cwd) for s in stages[:i]):
                return True
    return False


def apply_cd(stages: list[Stage], cwd: str) -> str:
    if len(stages) == 1 and command_name(stages[0].args) in CD:
        operands = [a for a in stages[0].args[1:] if not a.startswith("-")]
        if operands:
            return norm(operands[0], cwd)
    return cwd


def denied(command: str, tool: str, cwd: str) -> bool:
    if OPT_OUT.search(command):
        return False
    parsed = statements(command, posix=(tool != "PowerShell"))
    if parsed is None:
        return False  # unbalanced quotes; never block on a parse we do not trust
    for stages in parsed:
        if pipeline_denied(stages, cwd):
            return True
        cwd = apply_cd(stages, cwd)
    return False


# --- entry points ----------------------------------------------------------------------------


SELF_TESTS = [
    # (tool, command, cwd relative to repo root, expect_deny)
    ("Bash", 'grep -nE "…0x4d2540…" tools/analysis_out/DBSIM_disasm_full.txt | head', ".", True),
    ("Bash", 'grep -nE "mov.*0x4d2540|lea.*0x4d2540" tools/analysis_out/DBSIM_disasm_full.txt | head', ".", True),
    ("Bash", 'grep -nE "…0x4d2540…" tools/analysis_out/DBSIM_disasm_full.txt | wc -l', ".", False),
    ("Bash", 'grep -nE "…0x4d2540…" tools/analysis_out/DBSIM_disasm_full.txt | head # sample-ok', ".", False),
    ("Bash", "git log | head", ".", False),
    ("Bash", "ls tools/scripts | head", ".", False),
    ("Bash", "grep -n Mech_LocomotionTick Herculan/docs/x.md | head -5", ".", True),
    ("Bash", "cat tools/ghidra_scripts/known_symbols_dbsim.json | grep Mech | tail -3", ".", True),
    ("Bash", "grep -n 000015a4 tools/ghidra_scripts/known_symbols_es1dbsim.json | head", ".", True),
    ("Bash", "grep -n 000015a4 tools/analysis_out/ES1DBSIM_disasm_full.txt | head", ".", True),
    ("Bash", "rg Mech_LocomotionTick Reference/ | head", ".", False),
    ("Bash", "grep -o 'call [0-9a-f]*' tools/analysis_out/DBSIM_disasm_full.txt | sort | uniq -c", ".", False),
    ("Bash", "grep -n foo tools/analysis_out/a.txt | head -n 20", ".", True),
    ("Bash", "grep -n foo tools/analysis_out/a.txt | tail --lines=5", ".", True),
    ("Bash", "grep -n foo tools/analysis_out/a.txt | sed -n 1,40p", ".", True),
    ("Bash", "grep -n foo tools/analysis_out/a.txt | sed 20q", ".", True),
    ("Bash", "grep -n foo tools/analysis_out/a.txt | awk '{print $1}' | head", ".", True),
    ("Bash", "awk '/0x4d2540/' tools/analysis_out/a.txt | head", ".", True),
    ("Bash", "awk '{print $1}' tools/analysis_out/a.txt | head", ".", False),
    ("Bash", "git grep -n Mech | head", ".", True),
    ("Bash", "rg -n 0x4d2540 | head", ".", True),
    ("Bash", "rg -n 0x4d2540 | head", "tools/analysis_out", True),
    ("Bash", "grep -n 0x4d2540 DBSIM_disasm_full.txt | head", "tools/analysis_out", True),
    ("Bash", "cd tools/analysis_out && grep -n 0x4d2540 DBSIM_disasm_full.txt | head", ".", True),
    ("Bash", "grep -n foo < Herculan/docs/x.md | head", ".", True),
    ("Bash", "find Herculan/src -name '*.cs' | xargs grep -n Foo | head", ".", True),
    ("Bash", "grep -rn Foo tools/scripts | head", ".", False),
    ("Bash", "grep -n foo tools/analysis_out/a.txt 2>/dev/null | head; echo done", ".", True),
    ("Bash", "echo 'grep x tools/analysis_out/a.txt | head'", ".", False),
    ("Bash", "grep -rn Foo /e/ES2Stuff/Herculan/src | head -3", ".", True),
    ("PowerShell", "Select-String -Path tools\\analysis_out\\a.txt -Pattern '0x4d2540' | Select-Object -First 10", ".", True),
    ("PowerShell", "Select-String '0x4d2540' Herculan\\docs\\x.md | select -first 5", ".", True),
    ("PowerShell", "Get-Content tools\\ghidra_scripts\\known_symbols_vshell.json | Select-String Mech | Select-Object -Last 3", ".", True),
    ("PowerShell", "Select-String '0x4d2540' tools\\analysis_out\\a.txt | Measure-Object", ".", False),
    ("PowerShell", "Select-String Foo Reference\\notes.txt | Select-Object -First 3", ".", False),
    ("PowerShell", "git log | Select-Object -First 5", ".", False),
]


def worktree_tests() -> list[tuple[str, str, str, bool]]:
    """The scope cases again from inside a linked worktree, when one exists to test against."""
    if len(CHECKOUTS) < 2:
        print("skip linked-worktree cases: no linked worktree registered")
        return []
    wt = CHECKOUTS[1].replace("\\", "/")
    return [
        ("Bash", "grep -n Mech_LocomotionTick Herculan/docs/x.md | head -3", wt, True),
        ("Bash", "rg -n 0x4d2540 | head", wt + "/Herculan/src", True),
        ("Bash", f"grep -n foo {wt}/Herculan/docs/x.md | head", ".", True),
        ("Bash", "grep -rn Foo tools/scripts | head", wt, False),
    ]


def self_test() -> int:
    failures = 0
    cases = SELF_TESTS + worktree_tests()
    for tool, command, rel_cwd, expect in cases:
        got = denied(command, tool, os.path.join(REPO_ROOT, rel_cwd))
        mark = "ok  " if got == expect else "FAIL"
        failures += got != expect
        print(f"{mark} {'deny ' if expect else 'allow'} [{tool}, cwd={rel_cwd}] {command}")
    print(f"{len(cases) - failures}/{len(cases)} passed")
    return 1 if failures else 0


def main() -> int:
    if "--self-test" in sys.argv:
        return self_test()

    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        return 0  # never break the session over a malformed payload

    tool = payload.get("tool_name")
    if tool not in ("Bash", "PowerShell"):
        return 0

    command = payload.get("tool_input", {}).get("command", "")
    cwd = payload.get("cwd") or os.getcwd()

    if denied(command, tool, cwd):
        json.dump(
            {
                "hookSpecificOutput": {
                    "hookEventName": "PreToolUse",
                    "permissionDecision": "deny",
                    "permissionDecisionReason": DENY_REASON,
                }
            },
            sys.stdout,
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
