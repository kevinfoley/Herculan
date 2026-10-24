#!/usr/bin/env python3
"""List the XMLDoc and comment blocks in C# source that run longer than a given number of lines.

A block is a run of consecutive full-line comments of one kind:

- ``xmldoc``: lines starting with ``///``
- ``comment``: lines starting with ``//`` (but not ``///``)
- ``block``: a ``/* ... */`` comment that starts a line, counted from its opening line to its closing line

A blank line, a code line, or a change of kind ends the block. Comments that trail code on the same line are not counted. Lines inside multi-line string literals are not recognised as such, so a raw or verbatim string whose lines start with ``//`` would be counted as a comment.

Usage:
    python tools/scripts/cs_long_comments.py PATH [PATH ...] [--max-lines N] [--kind KIND] [--sort lines]

PATH is a .cs file or a folder searched recursively; bin/ and obj/ folders are skipped. Every block longer than N lines (default 20) is printed as ``file:start-end  lines  kind``, with paths relative to the current directory.
"""

from __future__ import annotations

import argparse
import os
import sys

SKIP_DIRS = {'bin', 'obj', '.vs', '.git'}
KINDS = ('xmldoc', 'comment', 'block')


def line_kind(stripped: str) -> str | None:
    if stripped.startswith('///'):
        return 'xmldoc'
    if stripped.startswith('//'):
        return 'comment'
    return None


def find_blocks(lines: list[str]) -> list[tuple[int, int, str]]:
    """Return (start, end, kind) for every comment block, 1-based and inclusive."""
    blocks: list[tuple[int, int, str]] = []
    run_kind: str | None = None
    run_start = 0
    i = 0
    n = len(lines)

    def close_run(end: int):
        nonlocal run_kind
        if run_kind is not None:
            blocks.append((run_start, end, run_kind))
            run_kind = None

    while i < n:
        stripped = lines[i].strip()
        if stripped.startswith('/*'):
            close_run(i)
            start = i + 1
            j = i
            # The closing */ may sit on the opening line itself, after the /*.
            rest = stripped[2:]
            while '*/' not in rest:
                j += 1
                if j >= n:
                    break
                rest = lines[j]
            end = min(j, n - 1) + 1
            blocks.append((start, end, 'block'))
            i = end
            continue
        kind = line_kind(stripped)
        if kind != run_kind:
            close_run(i)
            if kind is not None:
                run_kind = kind
                run_start = i + 1
        i += 1
    close_run(n)
    return blocks


def iter_cs_files(targets: list[str]):
    for target in targets:
        if os.path.isfile(target):
            yield target
        elif os.path.isdir(target):
            for dirpath, dirnames, filenames in os.walk(target):
                dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
                for name in sorted(filenames):
                    if name.endswith('.cs'):
                        yield os.path.join(dirpath, name)
        else:
            print(f"cs-long-comments: no such file or directory: {target}", file=sys.stderr)
            sys.exit(2)


def display_path(path: str) -> str:
    # relpath fails across Windows drives; fall back to the absolute path.
    try:
        return os.path.relpath(path)
    except ValueError:
        return os.path.abspath(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('paths', nargs='+', help='.cs files or folders to scan recursively')
    parser.add_argument('--max-lines', type=int, default=20, help='report blocks longer than this many lines (default 20)')
    parser.add_argument('--kind', choices=KINDS + ('all',), default='all', help='only report blocks of this kind')
    parser.add_argument('--sort', choices=('file', 'lines'), default='file', help='order by file and line (default) or by block length, longest first')
    args = parser.parse_args()

    hits: list[tuple[str, int, int, str]] = []
    files = 0
    for path in iter_cs_files(args.paths):
        files += 1
        with open(path, encoding='utf-8-sig', errors='replace') as f:
            lines = f.read().splitlines()
        for start, end, kind in find_blocks(lines):
            if end - start + 1 > args.max_lines and args.kind in ('all', kind):
                hits.append((display_path(path), start, end, kind))

    if args.sort == 'lines':
        hits.sort(key=lambda h: h[1] - h[2])
    for rel, start, end, kind in hits:
        print(f"{rel}:{start}-{end}  {end - start + 1}  {kind}")
    print(f"cs-long-comments: {files} file(s) scanned, {len(hits)} block(s) over {args.max_lines} lines", file=sys.stderr)


if __name__ == '__main__':
    main()
