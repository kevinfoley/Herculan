#!/usr/bin/env python3
"""Flag journal-style writing and append-only rot in reference documentation.

Reference docs state what is true now. They are not a record of how the project
arrived there — that is what git log is for. This linter catches the phrasings
that show a correction was narrated in place instead of applied, and open work
that is scattered through the body or named inconsistently: it belongs in one
final `## Open` section, as bullets labelled **Unported:** or **Open:**.

Usage:
    python tools/scripts/doc_lint.py                  # lint Herculan/docs and the known_*.json descriptions
    python tools/scripts/doc_lint.py PATH [PATH ...]  # lint specific files/dirs
    python tools/scripts/doc_lint.py --staged         # lint staged files only
    python tools/scripts/doc_lint.py --code           # also lint C# doc comments
    python tools/scripts/doc_lint.py --engine         # list each engine mention in a retail doc

Exit status is 1 when anything is flagged, so it works as a pre-commit hook.

A finding is not automatically wrong. A warning that protects a future reader
from re-deriving a disproven answer is worth keeping — but it belongs in a
"Rejected readings" table, phrased forward-looking ("the obvious reading is X;
it is actually Y"), not as a history of what this document used to say. To
silence a line deliberately, put `<!-- doc-lint: ok -->` at the end of it.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

# The checkout every relative path below is resolved against. The hook rebinds it to the checkout
# holding the edited file, which may be a worktree while this script runs from the main checkout.
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_TARGETS = [os.path.join("Herculan", "docs")]

# Files that are allowed to read as a log. Handoffs are explicitly ephemeral
# scratchpads, and the review reports are records of a point in time.
EXEMPT_BASENAMES = {
    "DOCS_REVIEW.md",
}
EXEMPT_PATTERNS = [
    re.compile(r"handoff[-_]", re.IGNORECASE),
]

SUPPRESS = re.compile(r"<!--\s*doc-lint:\s*ok\s*-->|doc-lint:\s*ok")

# The sanctioned home for a disproven reading. Its header necessarily contains
# "wrong", so exempt the table furniture rather than making authors suppress it.
ALLOWED = re.compile(
    r"^\s*\|\s*Reading\s*\|\s*Why it is wrong\s*\|\s*$"
    r"|^\s*\|[\s:|-]+\|\s*$",
)

# (id, severity, compiled pattern, why it is flagged)
RULES: list[tuple[str, str, re.Pattern[str], str]] = [
    (
        "dated-claim", "error",
        re.compile(r"\b20\d{2}-[01]\d-[0-3]\d\b"),
        "A date in a reference doc records when you learned something, not what is true. "
        "Put it in the commit message.",
    ),
    (
        "self-history", "error",
        re.compile(
            r"\b(?:previously\s+(?:stated|documented|listed|recorded|described|said|thought|assumed|called)"
            r"|an?\s+earlier\s+(?:pass|read|reading|note|revision|version|draft)"
            r"|earlier\s+(?:passes|readings|notes|revisions)\s+of\s+this"
            r"|this\s+(?:doc|document|file|section|class|comment)\s+(?:previously|used\s+to|once)"
            r"|used\s+to\s+(?:say|state|claim|call|read|assume|record))\b",
            re.IGNORECASE,
        ),
        "Narrates what this document used to say. Nobody reading now saw that version — state the "
        "current fact and delete the history.",
    ),
    (
        "supersede", "error",
        re.compile(
            r"\b(?:this\s+supersedes|supersed(?:es|ing)\s+(?:what|the\s+earlier)"
            r"|now\s+disproved|no\s+longer\s+a\s+guess|corrects\s+this\s+doc"
            r"|retroactively\s+confirms)\b",
            re.IGNORECASE,
        ),
        "Frames the text as a correction to a previous version rather than as a statement of fact.",
    ),
    (
        "was-wrong", "error",
        re.compile(
            # Past tense only. "X is wrong" is the forward-looking phrasing a rejected reading is
            # supposed to use, and is also how prose names a runtime fault or a wrong source.
            r"\b(?:(?:was|were)\s+(?:all\s+)?wrong"
            r"|got\s+(?:it\s+)?backwards"
            r"|had\s+the\s+sense\s+inverted"
            r"|turned\s+out\s+(?:to\s+be|not)"
            r"|why\s+this\s+wasn'?t\s+obvious)\b",
            re.IGNORECASE,
        ),
        "A record of a wrong turn. Keep it only as a forward-looking caution in a "
        "'Rejected readings' table.",
    ),
    (
        "solved-banner", "error",
        re.compile(r"^\s{0,3}#{1,6}\s.*\b(?:SOLVED|ANSWERED|FOUND|CONFIRMED)\b"),
        "Heading announces a discovery event. Name the subject instead — a reader wants to know "
        "what the section covers, not that you closed it.",
    ),
    (
        "question-heading", "warn",
        re.compile(r"^\s{0,3}#{1,6}\s.*\bQuestion\s+\d+\b", re.IGNORECASE),
        "Heading is numbered against an investigation, not a subject.",
    ),
    (
        "changelog-lede", "warn",
        re.compile(
            r"^\s*(?:\*\*)?(?:Since|Where\s+this\s+left\s+off|Closed\s+with\s+(?:it|them)"
            r"|Corrected\s+on\s+the\s+way|Not\s+built|Built\s+and\s+shipped)\b(?:\*\*)?\s*:",
            re.IGNORECASE,
        ),
        "Reads as a session changelog entry.",
    ),
    (
        "self-disclaimer", "warn",
        re.compile(
            r"\b(?:not\s+re-?verified|may\s+be\s+(?:less\s+complete|out\s+of\s+date|stale)"
            r"|check\s+.{0,40}\s+before\s+assuming)\b",
            re.IGNORECASE,
        ),
        "A section that disclaims its own accuracy should be fixed or deleted, not annotated.",
    ),
]

# Open work: the docs track it with exactly two terms, "unported" (a retail feature not yet in C#)
# and "open" (anything else unfinished), and only in a final `## Open` section, so one scroll to the
# bottom of a doc finds all of it. Markdown only; the status registers carry their own structure.
STATUS_EXEMPT_BASENAMES = {"KNOWN_ISSUES.md", "ROADMAP.md", "README.md"}
OPEN_HEADING = "## Open"
OPEN_ITEM = re.compile(r"^[-*]\s+\*\*(?:Unported|Open):\*\*\s")
HEADING = re.compile(r"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$")
LINK_TARGET = re.compile(r"\]\([^)]*\)")

# (id, pattern, why) — flagged anywhere in the doc, the Open section included.
STATUS_RULES: list[tuple[str, re.Pattern[str], str]] = [
    (
        "status-synonym",
        re.compile(
            r"\b(?:not\s+(?:yet\s+)?(?:ported|implemented|decoded|traced|identified)"
            r"|untraced|unresolved|un-?decoded|unimplemented|TODO|TBD|yet\s+to\s+be"
            r"|(?:is|are|remains?|still)\s+(?:unknown|unclear|not\s+(?:known|understood)))\b",
            re.IGNORECASE,
        ),
        "Open work uses two terms only: 'unported' for a retail feature not yet in C#, 'open' for "
        "anything else. Rephrase, and list the task in the doc's final '## Open' section.",
    ),
    (
        "status-heading",
        re.compile(
            r"^\s{0,3}#{1,6}\s+(?:Open\b(?!\s*$)|Not\s+ported|Unported|Unresolved|Remaining|Gaps?\b"
            r"|TODO|Known\s+open)",
            re.IGNORECASE,
        ),
        "Open work lives under one heading, exactly '## Open', as the doc's last section.",
    ),
]

# Flagged outside the Open section only: a hypothesis is a fine Open item, not a fine claim.
OUTSIDE_OPEN_RULES: list[tuple[str, re.Pattern[str], str]] = [
    (
        "hedge",
        re.compile(r"\b(?:plausibl[ey]|unproven|not\s+proven|unconfirmed|unverified)\b", re.IGNORECASE),
        "A hedge in the body is an open task in disguise. State what the evidence shows, and put "
        "what would settle the rest in '## Open'.",
    ),
    (
        "status-outside-open",
        re.compile(
            r"\b(?:unported|(?:still|remains?|left|is|are)\s+open"
            r"|open\s+(?:question|item|task|gap|problem)s?)\b",
            re.IGNORECASE,
        ),
        "Open work is listed in the final '## Open' section, not in body text. Move the task there; "
        "the body may link to it with [Open](#open).",
    ),
]

# Retail docs describe retail. What HERCULAN does — its types, its tweaks, where it departs from the
# original — lives in the C# that implements it, which cites the doc section, so an engine change
# never has to go looking for prose to update. Only these docs are about the engine itself.
ENGINE_DOC_BASENAMES = {
    "herculan-command-line.md", "KNOWN_ISSUES.md", "ROADMAP.md", "README.md", "cut-content.md", "key-bindings.md",
}
ENGINE_DOC_DIRS = [os.path.join("Herculan", "docs", "engine")]

ENGINE_RULES: list[tuple[str, re.Pattern[str], str]] = [
    (
        "engine-heading",
        re.compile(
            r"^\s{0,3}#{1,6}\s+(?:.*\bHERCULAN\b|Engine\s+(?:port|implementation|coverage|deviations?"
            r"|notes?|texturing)\b|Port\s+notes\b|Implementation(?:\s+status)?\s*$)",
            re.IGNORECASE,
        ),
        "A section about this engine's implementation in a retail doc. Move what it says into doc "
        "comments on the C# it describes, citing this doc's section, and delete it here.",
    ),
    (
        "engine-mention",
        re.compile(
            r"\b(?:HERCULAN|Herculan(?:\.\w+)+|this\s+engine|the\s+engine\b|ported\s+as"
            r"|Tweaks?\s+menu|TweakSetting\w*|Port\s+notes?:)",
            re.IGNORECASE,
        ),
        "A retail doc describing this engine. That goes stale whenever the engine changes: say it in "
        "a doc comment on the C# instead, citing this section, and keep the doc to retail.",
    ),
]

# The same rule catches what the markers above cannot: a retail doc naming a C# type or member bare.
# A field is described in the doc ("the paints-ground flag, `0x06`") and named only in code, so a C#
# rename never touches a doc. A backticked token is flagged when it is declared in one of these
# projects and is not also a retail name: a known_*.json name, the class prefix of one
# (`Text` from `Text_LoadDataFile`), or an identifier in a retail binary — the 3Space class names the
# HercWorks DTS model reuses are strings in DBSIM.EXE. The binaries are gitignored, so a checkout
# without ES2/ reports those few class names too.
CSHARP_SOURCE_DIRS = [os.path.join("Herculan", "src", p) for p in ("HercWorks.Core", "Herculan.Engine")]
RETAIL_JSON_FILES = [os.path.join("tools", "ghidra_scripts", f"known_{k}.json")
                     for k in ("symbols_dbsim", "symbols_vshell", "structs", "vtables")]
RETAIL_BINARIES = [os.path.join("ES2", f) for f in ("DBSIM.EXE", "VSHELL.EXE")]
CSHARP_TYPE_DECL = re.compile(r"\b(?:class|struct|record|enum|interface)\s+(?:struct\s+|class\s+)?([A-Za-z_]\w*)")
CSHARP_PUBLIC_MEMBER = re.compile(
    r"^\s*public\s+[^=;]*?\b([A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*(?:\{\s*(?:get|set|init)\b|=>|\(|=|;)")
CODE_SPAN = re.compile(r"`([^`\n]+)`")
DOTTED_IDENT = re.compile(r"(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*(?:\(\))?")
JSON_DESCRIPTION = re.compile(r'"description"\s*:\s*"((?:[^"\\]|\\.)*)"')
# A quoted span in a description is literal retail text ('hddclip', 'AutoRepair All Hercs'), which
# csharp-name does not read. A single quote opens a span only with no letter before it, so a
# possessive ("row's") never does.
JSON_QUOTED = re.compile(r"(?<!\w)'[^']*'(?!\w)|\\\"(?:(?!\\\").)*\\\"")
# engine-mention without "the engine", which in a plate comment means DBSIM's own 3D engine.
JSON_ENGINE_MENTION = re.compile(r"\b(?:HERCULAN|Herculan(?:\.\w+)+|this\s+engine|ported\s+as)", re.IGNORECASE)
# One English word (`Height`, `Data`, `Mech`) is as likely prose or a retail keyword as a C# name;
# only a compound — two words run together, or a word and a digit — is distinctive enough to flag.
# A letter and a number alone (`Q10`) is fixed-point notation.
COMPOUND = re.compile(r"[a-z][A-Z0-9_]|[0-9][A-Za-z_]|[A-Z]{2}[a-z]")
CSHARP_WHY = ("A retail doc naming a C# type or member. A rename then has to find this prose: describe "
              "the field or behaviour in words, and leave the name to the C# doc comment that cites "
              "this section.")

_name_sets: tuple[set[str], set[str], set[str]] | None = None


def name_sets() -> tuple[set[str], set[str], set[str]]:
    """(C# type names, all C# names, retail names), read once per run."""
    global _name_sets
    if _name_sets is not None:
        return _name_sets
    types: set[str] = set()
    names: set[str] = set()
    for src in CSHARP_SOURCE_DIRS:
        for root, dirs, files in os.walk(os.path.join(REPO_ROOT, src)):
            dirs[:] = [d for d in dirs if d not in {"obj", "bin"}]
            for name in files:
                if not name.endswith(".cs"):
                    continue
                with open(os.path.join(root, name), encoding="utf-8-sig", errors="replace") as fh:
                    for line in fh:
                        code = line.split("//")[0]
                        types.update(m.group(1) for m in CSHARP_TYPE_DECL.finditer(code))
                        m = CSHARP_PUBLIC_MEMBER.match(code)
                        if m:
                            names.add(m.group(1))
    names |= types

    import json

    retail: set[str] = set()

    def collect(node: object) -> None:
        if isinstance(node, dict):
            for key, value in node.items():
                if key == "name" and isinstance(value, str):
                    name = value.removeprefix("maybe_")
                    retail.add(name)
                    retail.add(name.split("_")[0])
                else:
                    collect(value)
        elif isinstance(node, list):
            for item in node:
                collect(item)

    for rel in RETAIL_JSON_FILES:
        try:
            with open(os.path.join(REPO_ROOT, rel), encoding="utf-8-sig") as fh:
                collect(json.load(fh))
        except (OSError, ValueError):
            pass
    for rel in RETAIL_BINARIES:
        path = os.path.join(REPO_ROOT, rel)
        if not os.path.isfile(path):
            path = os.path.join(main_checkout(), rel)
        try:
            with open(path, "rb") as fh:
                retail.update(m.decode() for m in re.findall(rb"[A-Za-z_]\w{2,}", fh.read()))
        except OSError:
            pass
    _name_sets = (types, names, retail)
    return _name_sets


def checkout_root(path: str) -> str | None:
    """The checkout holding path: the nearest directory with a .git entry (a worktree's is a file)."""
    d = os.path.dirname(os.path.abspath(path))
    while True:
        if os.path.exists(os.path.join(d, ".git")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            return None
        d = parent


def main_checkout() -> str:
    """The main checkout behind REPO_ROOT. A worktree lacks the gitignored ES2/ binaries."""
    out = subprocess.run(
        ["git", "rev-parse", "--path-format=absolute", "--git-common-dir"],
        cwd=REPO_ROOT, capture_output=True, text=True, check=False,
    ).stdout.strip()
    return os.path.dirname(os.path.normpath(out)) if out else REPO_ROOT


def csharp_mention(token: str) -> str | None:
    """The part of an identifier-shaped token that names C# rather than retail, if any."""
    if not DOTTED_IDENT.fullmatch(token):
        return None
    parts = token.removesuffix("()").split(".")
    # A file name: `MECHS.NAM`, `script.dat`.
    if len(parts) > 1 and len(parts[-1]) <= 4 and (parts[-1].isupper() or parts[-1].islower()):
        return None
    types, names, retail = name_sets()
    if len(parts) > 1 and parts[0] in types and parts[0] not in retail:
        return token
    for part in parts:
        if part in names and part not in retail and COMPOUND.search(part):
            return part
    return None


ENGINE_RULE_IDS = {rule_id for rule_id, _, _ in ENGINE_RULES} | {"csharp-name"}


def is_engine_doc(path: str) -> bool:
    if os.path.basename(path) in ENGINE_DOC_BASENAMES:
        return True
    rel = os.path.relpath(path, REPO_ROOT)
    return any(rel == d or rel.startswith(d + os.sep) for d in ENGINE_DOC_DIRS)


CODE_COMMENT = re.compile(r"^\s*(?:///|//|\*)")


def is_exempt(path: str) -> bool:
    base = os.path.basename(path)
    if base in EXEMPT_BASENAMES:
        return True
    return any(p.search(base) for p in EXEMPT_PATTERNS)


def iter_files(targets: list[str], include_code: bool) -> list[str]:
    exts = {".md"} | ({".cs"} if include_code else set())
    found: list[str] = []
    for target in targets:
        path = target if os.path.isabs(target) else os.path.join(REPO_ROOT, target)
        if os.path.isfile(path):
            if os.path.splitext(path)[1].lower() in exts or is_retail_json(path):
                found.append(path)
            continue
        for root, dirs, files in os.walk(path):
            dirs[:] = [d for d in dirs if d not in {"obj", "bin", ".git", "node_modules"}]
            for name in sorted(files):
                if os.path.splitext(name)[1].lower() in exts:
                    found.append(os.path.join(root, name))
    return [f for f in found if not is_exempt(f)]


def staged_files() -> list[str]:
    out = subprocess.run(
        ["git", "diff", "--cached", "--name-only", "--diff-filter=ACMR"],
        cwd=REPO_ROOT, capture_output=True, text=True, check=False,
    ).stdout.split("\n")
    paths = [os.path.join(REPO_ROOT, p) for p in out if p.strip()]
    return [p for p in paths if os.path.splitext(p)[1].lower() in {".md", ".cs"} or is_retail_json(p)]


def is_retail_json(path: str) -> bool:
    try:
        return os.path.relpath(os.path.join(REPO_ROOT, path), REPO_ROOT) in RETAIL_JSON_FILES
    except ValueError:  # another drive
        return False


def lint_json(lines: list[str]) -> list[tuple[int, str, str, str, str]]:
    """Rule 9 over the known_*.json descriptions, which become Ghidra plate comments on retail code."""
    hits = []
    for n, line in enumerate(lines, 1):
        m = JSON_DESCRIPTION.search(line)
        if not m or SUPPRESS.search(m.group(1)):
            continue
        text = m.group(1)
        engine = JSON_ENGINE_MENTION.search(text)
        if engine:
            hits.append((n, "engine-mention", "warn", engine.group(0).strip(), ENGINE_RULES[1][2]))
        unquoted = JSON_QUOTED.sub(" ", text)
        named = next(filter(None, (csharp_mention(t.group(0))
                                   for t in re.finditer(r"(?<![\w.])" + DOTTED_IDENT.pattern, unquoted))), None)
        if named:
            hits.append((n, "csharp-name", "warn", named, CSHARP_WHY))
    return hits


def lint_file(path: str, include_code: bool) -> list[tuple[int, str, str, str, str]]:
    is_cs = path.lower().endswith(".cs")
    try:
        with open(path, encoding="utf-8-sig") as fh:
            lines = fh.read().replace("\r\n", "\n").split("\n")
    except (OSError, UnicodeDecodeError):
        return []
    if path.lower().endswith(".json"):
        return lint_json(lines) if is_retail_json(path) else []

    check_status = not is_cs and os.path.basename(path) not in STATUS_EXEMPT_BASENAMES
    check_engine = not is_cs and not is_engine_doc(path)
    hits = []
    in_fence = False
    in_open = False
    open_line = 0
    for n, line in enumerate(lines, 1):
        stripped = line.strip()
        if not is_cs and (stripped.startswith("```") or stripped.startswith("~~~")):
            in_fence = not in_fence
            continue
        if check_status and not in_fence:
            h = HEADING.match(line)
            if h and len(h.group(1)) <= 2:
                if in_open:
                    hits.append((n, "open-not-last", "error", stripped,
                                 "'## Open' must be the doc's last section, so its tasks are always "
                                 "at the bottom. Move this section above it."))
                if stripped == OPEN_HEADING:
                    if open_line:
                        hits.append((n, "open-duplicate", "error", stripped,
                                     f"A second '## Open' section; merge it into the one at line {open_line}."))
                    open_line = open_line or n
                    in_open = True
                    continue
                in_open = False
            elif h and in_open:
                hits.append((n, "open-subheading", "error", stripped,
                             "The Open section is a flat list of labelled bullets; fold this "
                             "subheading into the items."))
            elif in_open and stripped and not line[0].isspace() and not OPEN_ITEM.match(line) \
                    and not SUPPRESS.search(line):
                hits.append((n, "open-item", "error", stripped[:60],
                             "Each Open item is a top-level bullet starting '**Unported:**' or "
                             "'**Open:**', so a grep across the docs lists every task."))
        if in_fence or SUPPRESS.search(line) or ALLOWED.match(line):
            continue
        # A link target is another doc's anchor, not this doc's wording.
        text = LINK_TARGET.sub("]()", line)
        if check_engine:
            # A heading is judged by engine-heading alone, so each section is flagged once.
            for rule_id, pattern, why in ENGINE_RULES[:1] if HEADING.match(line) else ENGINE_RULES[1:]:
                m = pattern.search(text)
                if m:
                    hits.append((n, rule_id, "warn", m.group(0).strip(), why))
            named = next(filter(None, (csharp_mention(m.group(1)) for m in CODE_SPAN.finditer(text))), None)
            if named:
                hits.append((n, "csharp-name", "warn", named, CSHARP_WHY))
        if check_status:
            # A heading is judged by status-heading alone, so '## Open questions' is flagged once.
            outside = [] if in_open or HEADING.match(line) else OUTSIDE_OPEN_RULES
            rules = STATUS_RULES + outside
            for rule_id, pattern, why in rules:
                m = pattern.search(text)
                if m:
                    hits.append((n, rule_id, "error", m.group(0).strip(), why))
        # In C# only look at comments, so identifiers and string literals are ignored.
        if is_cs and not CODE_COMMENT.match(line):
            continue
        for rule_id, severity, pattern, why in RULES:
            # Heading rules are markdown-only.
            if is_cs and rule_id in {"solved-banner", "question-heading"}:
                continue
            m = pattern.search(line)
            if m:
                hits.append((n, rule_id, severity, m.group(0).strip(), why))
    return hits


def hook_mode() -> int:
    """PostToolUse hook: read Claude Code's hook JSON on stdin, lint the edited file.

    Emits the hook's own JSON response so the findings are fed straight back to the
    model that just wrote them, while the edit itself still stands.
    """
    import json

    # Claude Code writes UTF-8; sys.stdin would decode it with the Windows codepage, so a written
    # line with an em-dash would never match the file and its findings would be dropped.
    try:
        payload = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    except (json.JSONDecodeError, ValueError):
        return 0

    tool_input = payload.get("tool_input") or {}
    tool_response = payload.get("tool_response") or {}
    path = tool_response.get("filePath") or tool_input.get("file_path") or ""
    if not path:
        return 0

    global REPO_ROOT
    REPO_ROOT = checkout_root(path) or REPO_ROOT

    norm = path.replace("\\", "/")
    if not (norm.lower().endswith(".md") and "/docs/" in norm.lower()) and not is_retail_json(path):
        return 0
    if is_exempt(path) or not os.path.isfile(path):
        return 0

    hits = lint_file(path, include_code=False)
    # Report only the engine mentions this edit wrote, so an edit is never made to carry a whole
    # file's cleanup, but nothing new gets added.
    written = [tool_input.get("new_string"), tool_input.get("content")]
    written += [e.get("new_string") for e in tool_input.get("edits") or [] if isinstance(e, dict)]
    touched = {ln.strip() for chunk in written if isinstance(chunk, str) for ln in chunk.split("\n")}
    touched.discard("")
    with open(path, encoding="utf-8-sig") as fh:
        file_lines = fh.read().replace("\r\n", "\n").split("\n")
    hits = [h for h in hits
            if h[1] not in ENGINE_RULE_IDS or file_lines[h[0] - 1].strip() in touched]
    if not hits:
        return 0

    rel = os.path.relpath(path, REPO_ROOT).replace("\\", "/")
    lines = [
        f"doc-lint flagged {len(hits)} issue(s) in {rel}. This is a reference document: it states "
        "what is true now, and the change history belongs in the commit message.",
        "",
    ]
    for n, rule_id, _severity, text, why in hits:
        lines.append(f"  {rel}:{n}  [{rule_id}]  \"{text}\"")
        lines.append(f"      {why}")
    lines += [
        "",
        "Fix these now. If a finding is a genuinely load-bearing caution — it stops a reader "
        "reaching a disproven conclusion on their own — keep it, reword it forward-looking "
        "(\"the obvious reading is X; it is actually Y\"), move it into that doc's "
        "'Rejected readings' table, and append <!-- doc-lint: ok --> to the line.",
        "Also check while you are in the file: does a later section now contradict an earlier "
        "one? Correct the earlier text rather than appending to the end.",
        "Open work (the status-*, hedge and open-* rules) goes in the final '## Open' section as "
        "bullets starting '**Unported:**' or '**Open:**'; the body states only what is known.",
        "Engine mentions (engine-*) do not belong in a retail doc: describe this engine's behaviour "
        "in a doc comment on the C# that implements it, citing the doc section.",
        "A C# name (csharp-name) does not belong in one either: describe the field or behaviour in "
        "words (\"the paints-ground flag, `0x06`\"), so a C# rename never has to touch the doc. If "
        "the token is a retail name that only happens to match one, add it to known_symbols_<binary>.json "
        "or known_structs.json if it belongs there, otherwise append <!-- doc-lint: ok -->.",
    ]

    json.dump({
        "systemMessage": f"doc-lint: {len(hits)} issue(s) in {rel}",
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": "\n".join(lines),
        },
    }, sys.stdout)
    return 0


def main() -> int:
    if "--hook" in sys.argv:
        return hook_mode()

    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="*",
                    help="files or directories (default: Herculan/docs and the known_*.json files)")
    ap.add_argument("--staged", action="store_true", help="lint staged files only")
    ap.add_argument("--code", action="store_true", help="also lint C# doc comments")
    ap.add_argument("--engine", action="store_true",
                    help="list engine mentions in retail docs one by one, not as a per-file count")
    ap.add_argument("--quiet", action="store_true", help="print only the summary")
    args = ap.parse_args()

    if args.staged:
        files = [f for f in staged_files() if not is_exempt(f)]
        if not args.code:
            files = [f for f in files if not f.lower().endswith(".cs")]
    else:
        files = iter_files(args.paths or DEFAULT_TARGETS + RETAIL_JSON_FILES, args.code)

    # Windows consoles default to a codepage that cannot render the messages below.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

    total = 0
    errors = 0
    dirty = 0
    engine_counts: list[tuple[str, int]] = []
    for path in files:
        hits = lint_file(path, args.code)
        if not args.engine:
            engine = [h for h in hits if h[1] in ENGINE_RULE_IDS]
            if engine:
                engine_counts.append((os.path.relpath(path, REPO_ROOT).replace("\\", "/"), len(engine)))
                hits = [h for h in hits if h[1] not in ENGINE_RULE_IDS]
        if not hits:
            continue
        dirty += 1
        rel = os.path.relpath(path, REPO_ROOT).replace("\\", "/")
        if not args.quiet:
            print(f"\n{rel}")
        for n, rule_id, severity, text, why in hits:
            total += 1
            if severity == "error":
                errors += 1
            if not args.quiet:
                print(f"  {rel}:{n}  [{severity}: {rule_id}]  \"{text}\"")
                print(f"      {why}")

    if engine_counts and not args.quiet:
        print(f"\nEngine mentions in retail docs ({sum(c for _, c in engine_counts)} across "
              f"{len(engine_counts)} file(s); --engine lists them):")
        for rel, count in sorted(engine_counts, key=lambda rc: -rc[1]):
            print(f"  {count:4}  {rel}")

    if total:
        print(f"\n{total} finding(s) across {dirty} file(s); {errors} error(s).")
        print("Reference docs state what is true now. Put the change history in the commit message.")
        print("A genuinely load-bearing caution belongs in a 'Rejected readings' table, phrased")
        print("forward-looking. To keep a specific line, append: <!-- doc-lint: ok -->")
    else:
        rest = "; engine mentions listed above" if engine_counts else ""
        print(f"doc-lint: clean ({len(files)} file(s) checked{rest}).")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
