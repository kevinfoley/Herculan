#!/usr/bin/env python3
"""PostToolUse hook: make Claude account for every "this engine's choice" it writes.

The phrase is meant to mark a departure from retail that the user and Claude agreed on. Claude has
also used it to label a decision it made alone, after the fact, which hides the decision from the
user. Whenever an edit adds the phrase, this hook stops Claude and asks it to say which case it is:
an agreed decision needs nothing more; a unilateral one must be reported to the user with a
question about what to do, before the work goes on.

It checks the text an edit writes (Write content, Edit/MultiEdit new_string, NotebookEdit source)
and the command text of Bash and PowerShell calls, which is how a scripted edit would write it. An
Edit whose old_string already held the phrase as many times is not flagged, so moving or rewording
around an existing occurrence stays quiet.

Wire up in .claude/settings.json:

    "PostToolUse": [
      { "matcher": "Write|Edit|MultiEdit|NotebookEdit|Bash|PowerShell",
        "hooks": [ { "type": "command",
                     "command": "python \"${CLAUDE_PROJECT_DIR:-.}/tools/scripts/engine_choice_guard.py\"" } ] }
    ]

`--self-test` runs the built-in cases instead of reading a hook payload.
"""

from __future__ import annotations

import json
import re
import sys

# "this engine's choice", "the engine's own choice", "HERCULAN's choice". The apostrophe may be
# curly or shell-escaped (`'"'"'`, `\'`), and the phrase may wrap onto a following comment line.
_APOS = r"""['’`"\\]{1,5}"""
_GAP = r"(?:\s*(?:///?|\*|#|--)?\s*)"
PHRASE = re.compile(rf"\b(?:(?:this|the){_GAP}engine|HERCULAN){_APOS}s{_GAP}(?:own{_GAP})?choice\b",
                    re.IGNORECASE)


def count(text: str | None) -> int:
    return len(PHRASE.findall(text or ""))


def added_snippets(tool: str, tool_input: dict) -> list[str]:
    """Lines carrying a newly written occurrence of the phrase."""
    pairs: list[tuple[str, str]] = []  # (new text, old text)
    if tool == "Write":
        pairs.append((tool_input.get("content", ""), ""))
    elif tool == "Edit":
        pairs.append((tool_input.get("new_string", ""), tool_input.get("old_string", "")))
    elif tool == "MultiEdit":
        for e in tool_input.get("edits", []) or []:
            pairs.append((e.get("new_string", ""), e.get("old_string", "")))
    elif tool == "NotebookEdit":
        pairs.append((tool_input.get("new_source", ""), ""))
    elif tool in ("Bash", "PowerShell"):
        pairs.append((tool_input.get("command", ""), ""))

    snippets: list[str] = []
    for new, old in pairs:
        if count(new) <= count(old):
            continue
        flat = re.sub(r"[ \t]*\r?\n[ \t]*(?:///?|\*|#|--)?[ \t]*", " ", new)
        for m in PHRASE.finditer(flat):
            lo, hi = max(0, m.start() - 120), min(len(flat), m.end() + 120)
            snippets.append(("…" if lo else "") + flat[lo:hi].strip() + ("…" if hi < len(flat) else ""))
    return snippets


def message(path: str, snippets: list[str]) -> str:
    where = f" in {path}" if path else ""
    quoted = "\n".join(f"  > {s}" for s in snippets)
    return (
        f"You just wrote \"this engine's choice\"{where}:\n{quoted}\n\n"
        "Before doing anything else, decide honestly which of these it is:\n"
        "  (a) a decision the user made or agreed to: in this conversation, or recorded where you "
        "can point to it (a commit, a doc, a memory note, KNOWN_ISSUES/ROADMAP). Name where.\n"
        "  (b) a decision you made yourself: a substitution, default, simplification or "
        "departure from retail the user never approved.\n"
        "If (b): stop the task now and tell the user plainly. Say what you decided, where it is, "
        "what retail does (or that retail evidence is missing), and the alternatives; then ask "
        "what to do. Do not build further on it until they answer. Do not paper over it by "
        "rewording the comment.\n"
        "If (a): carry on, and cite where it was agreed in your report.\n"
        "If you cannot tell, treat it as (b)."
    )


def evaluate(payload: dict) -> dict | None:
    tool = payload.get("tool_name", "")
    tool_input = payload.get("tool_input") or {}
    snippets = added_snippets(tool, tool_input)
    if not snippets:
        return None
    path = tool_input.get("file_path") or tool_input.get("notebook_path") or ""
    return {"decision": "block", "reason": message(path, snippets)}


def self_test() -> int:
    cases = [
        ({"tool_name": "Write", "tool_input": {"file_path": "a.cs", "content": "// Fades in over 0.5 s: this engine's choice.\n"}}, True),
        ({"tool_name": "Write", "tool_input": {"file_path": "a.cs", "content": "// Retail fades in over 0.5 s.\n"}}, False),
        ({"tool_name": "Edit", "tool_input": {"file_path": "a.cs", "old_string": "x", "new_string": "/// The Engine’s own\n/// choice."}}, True),
        ({"tool_name": "Edit", "tool_input": {"file_path": "a.cs", "old_string": "this engine's choice; y", "new_string": "this engine's choice; z"}}, False),
        ({"tool_name": "MultiEdit", "tool_input": {"file_path": "a.cs", "edits": [{"old_string": "a", "new_string": "b"}, {"old_string": "c", "new_string": "HERCULAN's choice"}]}}, True),
        ({"tool_name": "Bash", "tool_input": {"command": "sed -i 's/x/this engine'\"'\"'s choice/' a.cs"}}, True),
        ({"tool_name": "Bash", "tool_input": {"command": "grep -rn 'engine choices' ."}}, False),
        ({"tool_name": "Read", "tool_input": {"file_path": "this engine's choice"}}, False),
    ]
    failed = 0
    for i, (payload, expect) in enumerate(cases):
        got = evaluate(payload) is not None
        if got != expect:
            failed += 1
            print(f"case {i}: expected {'flag' if expect else 'pass'}, got {'flag' if got else 'pass'}")
    print(f"{len(cases) - failed}/{len(cases)} passed")
    return 1 if failed else 0


def main() -> int:
    if "--self-test" in sys.argv:
        return self_test()
    try:
        # Claude Code writes UTF-8; sys.stdin would decode it with the Windows codepage.
        payload = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    except (ValueError, UnicodeDecodeError):
        return 0
    result = evaluate(payload)
    if result:
        sys.stdout.reconfigure(encoding="utf-8")
        json.dump(result, sys.stdout)
    return 0


if __name__ == "__main__":
    sys.exit(main())
