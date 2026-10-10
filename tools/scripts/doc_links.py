#!/usr/bin/env python3
"""Resolve every cross-reference in the docs, so a rename cannot quietly orphan one.

`doc_lint.py` reads one file at a time and never follows a link. This follows all
of them: that a linked file still exists, and that a `#fragment` still names a
heading in it. Those are the two things a rename breaks silently -- the target
moves, the link keeps rendering, and nothing complains until a reader clicks it.

**The heading is the anchor.** Retitling `## Weapon energy arbitration --
FUN_004107e4` to name the symbol instead of the placeholder changes the anchor
every inbound link spelled out, in files the rename never touched. That is not
hypothetical: it is how three of this repo's links came to point at nothing, one
of them from a `FUN_` cleanup pass that renamed headings correctly and never
looked outward.

**So there is no --staged mode, deliberately.** The file that breaks is not the
file you edited. Checking only what you touched would report clean on exactly
the change that does the damage, which is worse than not checking at all. Every
run resolves the whole set.

Usage:
    python tools/scripts/doc_links.py                  # check the default doc set
    python tools/scripts/doc_links.py PATH [PATH ...]  # add files/dirs to the set
    python tools/scripts/doc_links.py --code           # also check paths named in C# comments
    python tools/scripts/doc_links.py --quiet          # summary only

Exit status is 1 when anything is broken, so it works as a pre-commit hook.

It checks relative links only. An `http(s)://` or `mailto:` target is somebody
else's to keep alive, and a bare `#fragment` is resolved against its own file.
"""

from __future__ import annotations

import argparse
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# The docs proper, plus the two status registers that link into them.
DEFAULT_TARGETS = [
    os.path.join("Herculan", "docs"),
    os.path.join("Herculan", "ROADMAP.md"),
    os.path.join("Herculan", "KNOWN_ISSUES.md"),
    "README.MD",
]

SKIP_DIRS = {"obj", "bin", ".git", "node_modules"}

# [label](target) and [label](target "title"), but not ![image](...) -- an image
# that fails to load is visible, so it is not the silent failure this is for.
LINK = re.compile(r"(?<!\!)\[(?:[^\]\[]|\[[^\]]*\])*\]\(\s*([^)\s]+)(?:\s+\"[^\"]*\")?\s*\)")

# A doc path written bare in a C# doc comment, e.g. docs/retail/simulation/mech-locomotion.md.
CODE_PATH = re.compile(r"\b((?:\.\./)*(?:Herculan/)?docs/[A-Za-z0-9_./-]+\.md)")

EXTERNAL = re.compile(r"^(?:[a-z][a-z0-9+.-]*:|//)", re.IGNORECASE)

ATX_HEADING = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
FENCE = re.compile(r"^\s*(```|~~~)")

# An inline code span renders as code, so a `[i](...)` inside one is not a link.
CODE_SPAN = re.compile(r"(`+)(?!`).+?(?<!`)\1(?!`)")


def slug(heading: str) -> str:
    """GitHub's anchor for a heading: the algorithm the rendered docs actually use.

    Inline markup is removed rather than transliterated -- `` `Mech_Tick` `` and
    ``**Mech_Tick**`` both anchor as ``mech_tick`` -- then anything that is not a
    letter, digit, space, hyphen or underscore is dropped and spaces become
    hyphens. Em dashes and parentheses therefore vanish, which is why
    ``## Reactor output rate -- `Mech_ComputeReactorRate` (`00417d08`)`` anchors
    as ``reactor-output-rate--mech_computereactorrate-00417d08``: the two spaces
    left either side of the dropped dash collapse into two hyphens, not one.
    """
    text = heading
    text = re.sub(r"!\[([^\]]*)\]\([^)]*\)", r"\1", text)      # images -> alt text
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)       # links -> label
    text = text.replace("`", "")
    # Only `*` and `~` are stripped as emphasis. An underscore inside a word is not
    # emphasis and survives into the anchor -- stripping it turns
    # #the-combat-reassess--mech_aicombatreassess into ...--mechaicombatreassess and
    # reports every symbol-named heading in the repo as a broken link.
    text = re.sub(r"[*~]", "", text)
    text = text.lower()
    text = re.sub(r"[^a-z0-9 \-_]", "", text)
    return text.strip().replace(" ", "-")


def anchors_of(path: str) -> set[str]:
    """Every fragment `path` offers, with GitHub's duplicate-heading suffixes."""
    found: set[str] = set()
    seen: dict[str, int] = {}
    in_fence = False
    try:
        lines = open(path, encoding="utf-8").read().split("\n")
    except (OSError, UnicodeDecodeError):
        return found

    for line in lines:
        if FENCE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        m = ATX_HEADING.match(line)
        if not m:
            continue
        base = slug(m.group(2))
        if not base:
            continue
        n = seen.get(base, 0)
        seen[base] = n + 1
        # GitHub appends -1, -2 ... to the second and later copies of a heading.
        found.add(base if n == 0 else f"{base}-{n}")

    # An explicit <a name="..."> or id="..." is a hand-written anchor; honour it.
    text = "\n".join(lines)
    found.update(re.findall(r"<a\s+[^>]*(?:name|id)=\"([^\"]+)\"", text))
    return found


def display(path: str) -> str:
    """Repo-relative if we can -- on Windows relpath raises across drive letters,
    which a target under a temp directory on another volume will hit."""
    try:
        return os.path.relpath(path, REPO_ROOT).replace("\\", "/")
    except ValueError:
        return path.replace("\\", "/")


def iter_files(targets: list[str], include_code: bool) -> list[str]:
    exts = {".md"} | ({".cs"} if include_code else set())
    found: list[str] = []
    for target in targets:
        path = target if os.path.isabs(target) else os.path.join(REPO_ROOT, target)
        if os.path.isfile(path):
            if os.path.splitext(path)[1].lower() in exts:
                found.append(path)
            continue
        for root, dirs, files in os.walk(path):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for name in sorted(files):
                if os.path.splitext(name)[1].lower() in exts:
                    found.append(os.path.join(root, name))
    return found


def check_target(base_dir: str, self_path: str, raw: str,
                 anchor_cache: dict[str, set[str]]) -> str | None:
    """Return why this link is broken, or None when it resolves.

    `base_dir` is what a relative target resolves against; `self_path` is the file
    a bare `#fragment` belongs to.
    """
    target, _, fragment = raw.partition("#")

    if target:
        resolved = os.path.normpath(os.path.join(base_dir, target))
        if not os.path.exists(resolved):
            return f"no such file: {target}"
    else:
        resolved = self_path                          # bare #fragment, same file

    if not fragment:
        return None
    if os.path.splitext(resolved)[1].lower() != ".md":
        return None                                  # fragments into non-markdown: not ours

    if resolved not in anchor_cache:
        anchor_cache[resolved] = anchors_of(resolved)
    if fragment.lower() not in {a.lower() for a in anchor_cache[resolved]}:
        where = target or "this file"
        return f"no heading in {where} anchors as #{fragment}"
    return None


def check_file(path: str, anchor_cache: dict[str, set[str]]) -> list[tuple[int, str, str]]:
    try:
        lines = open(path, encoding="utf-8").read().split("\n")
    except (OSError, UnicodeDecodeError):
        return []

    is_code = os.path.splitext(path)[1].lower() == ".cs"
    hits: list[tuple[int, str, str]] = []
    in_fence = False

    for n, line in enumerate(lines, 1):
        if not is_code:
            if FENCE.match(line):
                in_fence = not in_fence
                continue
            if in_fence:
                continue

        if is_code:
            # A C# doc comment names the doc as a bare path, not as a markdown link,
            # and never relative to the .cs. Both spellings are in use -- most write
            # "docs/retail/simulation/foo.md" as addressed from Herculan/, a few write
            # "Herculan/docs/retail/simulation/foo.md" as addressed from the repo root -- so
            # the base is chosen per path rather than fixed. Resolving every one
            # against Herculan/ reports the repo-root spelling as missing.
            raws = [m.group(1) for m in CODE_PATH.finditer(line)]
            base_dir = None  # per-path, below
        else:
            raws = [m.group(1) for m in LINK.finditer(CODE_SPAN.sub("", line))]
            base_dir = os.path.dirname(path)

        for raw in raws:
            if EXTERNAL.match(raw):
                continue
            here = base_dir
            if here is None:
                here = REPO_ROOT if raw.startswith("Herculan/")                     else os.path.join(REPO_ROOT, "Herculan")
            why = check_target(here, path, raw, anchor_cache)
            if why:
                hits.append((n, raw, why))
    return hits


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="*", help="extra files or directories to check")
    ap.add_argument("--code", action="store_true",
                    help="also check doc paths named in C# doc comments")
    ap.add_argument("--quiet", action="store_true", help="print only the summary")
    args = ap.parse_args()

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

    targets = list(args.paths) if args.paths else list(DEFAULT_TARGETS)
    if args.code and not args.paths:
        # The doc paths named in C# comments live in the engine sources, which are
        # not part of the doc set -- without this, --code walks the same .md files
        # and silently reports on nothing extra.
        targets.append(os.path.join("Herculan", "src"))
    files = [f for f in iter_files(targets, args.code) if os.path.exists(f)]

    anchor_cache: dict[str, set[str]] = {}
    total = 0
    dirty = 0
    for path in files:
        hits = check_file(path, anchor_cache)
        if not hits:
            continue
        dirty += 1
        rel = display(path)
        if not args.quiet:
            print(f"\n{rel}")
        for n, raw, why in hits:
            total += 1
            if not args.quiet:
                print(f"  {rel}:{n}  {raw}")
                print(f"      {why}")

    if total:
        print(f"\n{total} broken link(s) across {dirty} file(s).")
        print("A heading owns its anchor: retitling one breaks every inbound link, in files")
        print("the rename never touched. Fix the link, or restore the heading.")
        return 1

    print(f"doc-links: clean ({len(files)} file(s) checked).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
