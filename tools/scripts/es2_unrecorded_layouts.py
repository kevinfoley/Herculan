#!/usr/bin/env python3
"""List struct layouts and vtables the docs describe that the known_*.json files do not record.

The three companion files in tools/ghidra_scripts/ own what gets applied to Ghidra;
the docs own the evidence. They drift: a doc grows a complete offset table and nobody
copies it into known_structs.json, or a vtable address is named in prose and never
reaches known_vtables.json. This finds those leads.

It is a LEAD FINDER, not a verdict. "Fully understood" is inferred from how the doc
reads (named rows, no hedges, contiguous offsets), and a high score means "worth
reading", not "correct". It only ever says "described in docs, absent from the JSON".
It never says a layout is unused or unreferenced -- that is a different claim with
its own tools (es2_xref.py, es2_fieldscan.py).

Structs: a markdown table whose first column is a byte offset (`+0x1c`, `0x1c-0x1f`)
is a candidate layout. It counts as RECORDED when its heading or the prose above it
names a struct in known_structs.json, or when most of its offsets are fields of one.
Formats under docs/retail/formats/ usually describe an on-disk record rather than an
in-memory object; they are listed, tagged [file], since a file record can still be
worth a Ghidra type.

Vtables: an 8-hex address written next to the word "vtable" (or a `...Vtable`
identifier) that is in no `instances` list and is not a known function, plus any
`...Vtable` identifier that matches no instance label or definition name.

Usage:
    python tools/scripts/es2_unrecorded_layouts.py                 # both passes
    python tools/scripts/es2_unrecorded_layouts.py --structs
    python tools/scripts/es2_unrecorded_layouts.py --vtables
    python tools/scripts/es2_unrecorded_layouts.py --min-score 60  # struct pass cutoff
    python tools/scripts/es2_unrecorded_layouts.py --all           # include recorded tables too
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

import es2_symbols

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOCS = os.path.join(REPO_ROOT, "Herculan", "docs")
GHIDRA = os.path.join(REPO_ROOT, "tools", "ghidra_scripts")

OFFSET_HEADER = re.compile(r"offset|^off\b|^byte|^at$|^position|^\+|^where$", re.IGNORECASE)
HEDGES = re.compile(
    r"\b(plausibly|probably|possibly|unconfirmed|unclear|unknown|not established|"
    r"not yet|guess|presumably|likely|maybe|undecoded|untraced)\b|\?\s*$",
    re.IGNORECASE,
)
OFFSET_CELL = re.compile(
    r"^`?\s*\+?(0x[0-9a-fA-F]+|\d+)\s*(?:(?:-|–|—|\.\.)\s*\+?(0x[0-9a-fA-F]+|\d+))?\s*`?$"
)
ADDR = re.compile(r"\b(00[0-9a-f]{6})\b")
VTABLE_ID = re.compile(r"\b([A-Z]\w*Vtable)\b")
# "vtable" then an address a few tokens later, or an address then "vtable" -- not across a sentence.
VT_ADDR_AFTER = re.compile(r"[Vv]table\w*[^.|;\n]{0,50}?`?\b(00[0-9a-f]{6})\b")
VT_ADDR_BEFORE = re.compile(r"\b(00[0-9a-f]{6})\b`?[^.|;\n]{0,20}?\b(?:[A-Za-z]+ )?[Vv]table\b")


def load_json(name):
    with open(os.path.join(GHIDRA, name), encoding="utf-8-sig") as f:
        return json.load(f)


def parse_offset(cell):
    m = OFFSET_CELL.match(cell.strip())
    if not m:
        return None
    try:
        lo = int(m.group(1), 0)
        hi = int(m.group(2), 0) if m.group(2) else lo
    except ValueError:  # a zero-padded address such as 00476014 is not an offset
        return None
    if hi >= 0x10000:   # a struct field offset; larger is an address or a file position
        return None
    return (lo, hi) if hi >= lo else (lo, lo)


def md_files():
    for root, dirs, files in os.walk(DOCS):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin", ".git")]
        for fn in sorted(files):
            # Handoff docs are exempt scratchpads, never authoritative.
            if fn.endswith(".md") and not fn.startswith("handoff-"):
                yield os.path.join(root, fn)


def split_row(line):
    cells = [c.strip() for c in line.strip().strip("|").split("|")]
    return cells


class Table:
    def __init__(self, path, line, heading, prose, rows, header):
        self.path, self.line, self.heading, self.prose, self.rows = path, line, heading, prose, rows
        self.header = header

    def is_layout(self):
        """An offset column, not an id column: the header names it, or the row count of a
        `+0x..` first column says so. Tables of ids, states and buttons also start with a number."""
        return bool(OFFSET_HEADER.search(self.header))

    def offsets(self):
        return [r[0] for r in self.rows]


def extract_tables(path):
    """Yield tables whose first column is an offset, with the heading and prose above them."""
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
    heading, prose = "", []
    i = 0
    in_fence = False
    while i < len(lines):
        ln = lines[i]
        if ln.lstrip().startswith("```"):
            in_fence = not in_fence
        elif not in_fence:
            if ln.startswith("#"):
                heading, prose = ln.lstrip("# ").strip(), []
            elif ln.startswith("|"):
                start = i
                block = []
                while i < len(lines) and lines[i].startswith("|"):
                    block.append(lines[i])
                    i += 1
                rows = []
                for b in block:
                    cells = split_row(b)
                    off = parse_offset(cells[0]) if cells else None
                    if off is not None:
                        rows.append((off, cells))
                if rows:
                    header = split_row(block[0])[0] if block else ""
                    yield Table(path, start + 1, heading, " ".join(prose[-6:]), rows, header)
                continue
            elif ln.strip():
                prose.append(ln.strip())
        i += 1


STOPWORDS = {"that", "this", "with", "from", "then", "when", "byte", "word", "field", "read", "reads", "written",
             "write", "each", "into", "only", "also", "same", "which", "what", "their", "there", "does", "have",
             "used", "uses", "maybe", "value", "record", "block"}
# struct name -> {offset: stems of the words its field name and description use}
FIELD_WORDS = {}


def word_stems(text):
    """Lower-cased 4-letter stems of the words in `text`, camelCase split, stopwords dropped."""
    text = re.sub(r"([a-z])([A-Z])", lambda m: m.group(1) + " " + m.group(2), text)
    return {w[:4] for w in re.findall(r"[a-zA-Z]{4,}", text.lower()) if w not in STOPWORDS}


def build_struct_index():
    data = load_json("known_structs.json")
    for s in data["structs"]:
        FIELD_WORDS[s["name"]] = {
            f["offset"]: word_stems(f.get("name", "") + " " + f.get("description", "")[:100]) for f in s["fields"]
        }
    return {s["name"]: ({fld["offset"] for fld in s["fields"]}, s.get("size")) for s in data["structs"]}


SIZE_MENTION = re.compile(r"(?:\b(\d+)[- ]bytes?\b|\b(0x[0-9a-f]+)\s+bytes?\b|`(0x[0-9a-f]+)`\s*(?:bytes?)?)", re.IGNORECASE)


def stated_sizes(text):
    out = set()
    for m in SIZE_MENTION.finditer(text):
        tok = m.group(1) or m.group(2) or m.group(3)
        try:
            out.add(int(tok, 0))
        except ValueError:
            pass
    return out


def meanings_agree(tbl, name):
    """Offsets and size can coincide by chance (two unrelated 10-byte records); field meanings cannot.
    Half of the offsets the table shares with the struct must have a word in common with that field."""
    words = FIELD_WORDS[name]
    shared = hits = 0
    for (lo, _), cells in tbl.rows:
        if lo in words:
            shared += 1
            if word_stems(" ".join(cells[1:])) & words[lo]:
                hits += 1
    return shared > 0 and hits / shared >= 0.5


def struct_recorded(tbl, index):
    """Return (name, how) when the table is already covered, else None.

    Offsets +0/+4/+8 exist in nearly every struct, so overlap alone is weak evidence and only
    counts alongside a stated size that matches the struct's. Otherwise the closest struct is
    shown as a hint and the table stays in the list.
    """
    text = f"{tbl.heading} {tbl.prose}"
    for name in index:
        if re.search(rf"\b{re.escape(name)}\b", text):
            return name, "named"
    starts = {lo for (lo, _), _ in tbl.rows}
    sizes = stated_sizes(f"{tbl.heading} {tbl.prose}")
    best = None
    for name, (offs, size) in index.items():
        shared = len(starts & offs)
        frac = shared / len(starts)
        ok = shared >= 4 and frac >= 0.8 and size in sizes and meanings_agree(tbl, name)
        if ok and (best is None or frac > best[1]):
            best = (name, frac)
    if best:
        return best[0], f"{best[1]:.0%} of offsets match"
    return None


def closest_struct(tbl, index):
    """The known struct sharing the most offsets with the table, as a triage hint."""
    starts = {lo for (lo, _), _ in tbl.rows}
    name, (offs, _) = max(index.items(), key=lambda kv: len(starts & kv[1][0]))
    shared = len(starts & offs)
    return (name, shared, len(starts)) if shared else None


WIDTH_TYPES = [
    (re.compile(r"\b(?:int|i|u)64\b", re.I), 8),
    (re.compile(r"\b(?:int|i|u)32\b|\bint\b|\bptr\b|\bdword\b|\bfloat\b", re.I), 4),
    (re.compile(r"\b(?:int|i|u)16\b|\bshort\b|\bword\b", re.I), 2),
    (re.compile(r"\b(?:int|i|u)8\b|\bbyte\b|\bchar\b", re.I), 1),
]
MULTIPLIER = re.compile(r"\bx\s*(\d+)\b|\((\d+)\s+(?:shorts|ints|bytes)\)|\b(\d+)\s+shorts\b", re.I)


def row_width(off, cells):
    """Byte width a row claims, or None when the row does not say. A range (`0x8-0x30`) states its own;
    otherwise the type cell does (`i16 x10`). None means the row is taken to run up to the next one, so
    an unparsed type can never manufacture a gap."""
    lo, hi = off
    if hi > lo:
        return hi - lo + 1
    text = " ".join(cells[1:3])
    if len(cells) > 1 and re.search(r"[+\[]", cells[1]):  # `int16 len + char[len]` is variable, not 2 bytes
        return None
    for rx, w in WIDTH_TYPES:
        if rx.search(text):
            m = MULTIPLIER.search(text)
            return w * int(next(g for g in m.groups() if g)) if m else w
    return None


def layout_size(tbl):
    """The record size the heading (else the nearby prose) states, if it fits the table's offsets."""
    last = max(lo for (lo, _), _ in tbl.rows)
    for text in (tbl.heading, tbl.prose):
        fits = sorted(s for s in stated_sizes(text) if s > last)
        if fits:
            return fits[0]
    return None


def score_table(tbl):
    """0-100. Reasons are returned so a reader can judge the number.

    Half the score is how much the table says (rows, named, unhedged); the other half is whether it
    accounts for the whole record. The second needs a stated size: without one a table cannot be shown
    complete, so it is capped well below the top however tidy it looks.
    """
    reasons = []
    n = len(tbl.rows)
    hedged = sum(1 for _, cells in tbl.rows if any(HEDGES.search(c) for c in cells[1:]))
    named = sum(1 for _, cells in tbl.rows if [c for c in cells[1:] if c and not HEDGES.search(c)])
    # A link to the doc's Open section, in a row or just above the table, says the author is unfinished.
    openlink = bool(re.search(r"\]\(#open\)", tbl.prose + " " + " ".join(" ".join(c) for _, c in tbl.rows)))
    content = min(n, 12) / 12 * 20 + (named / n) * 15 + (1 - hedged / n) * 15
    reasons.append(f"{n} rows, {hedged} hedged")

    rows = sorted(tbl.rows, key=lambda r: r[0])
    overlaps = sum(1 for a, b in zip(rows, rows[1:]) if a[0][1] >= b[0][0])
    size = layout_size(tbl)
    if overlaps:
        fullness = 0.0
        reasons.append(f"{overlaps} rows overlap (a union, not a struct)")
    elif size is None:
        fullness = 0.4
        reasons.append("no stated size, so completeness cannot be shown")
    else:
        unaccounted = 0
        for (off, cells), nxt in zip(rows, rows[1:] + [None]):
            w = row_width(off, cells)
            if nxt is not None:
                if w is not None:
                    unaccounted += max(0, nxt[0][0] - (off[0] + w))
            else:
                unaccounted += max(0, size - (off[0] + (w if w is not None else 2)))
        if unaccounted <= 1:  # one byte of slack for a last row whose type did not parse
            unaccounted = 0
        fullness = max(0.0, 1 - unaccounted / size)
        reasons.append(f"size {size:#x} stated, {unaccounted} bytes unaccounted")
    score = content + fullness * 50
    if size is None or overlaps:
        score = min(score, 75)
    if openlink:
        score -= 15
        reasons.append("Open link")
    return max(0, round(score)), "; ".join(reasons)


IN_MEMORY = re.compile(r"in memory|\bDAT_[0-9a-f]{8}|\bmech\+0x|\bobj\+0x", re.IGNORECASE)


def is_file_only(tbl):
    """A table under docs/retail/formats/ is an on-disk record unless the doc says it is also held in memory
    (a Ghidra type applies to the in-memory copy, not to bytes in a file)."""
    rel = os.path.relpath(tbl.path, REPO_ROOT).replace("\\", "/")
    return "/formats/" in rel and not IN_MEMORY.search(f"{tbl.heading} {tbl.prose}")


def struct_pass(args):
    index = build_struct_index()
    found, hidden, skipped_files = [], [], 0
    for path in md_files():
        for tbl in extract_tables(path):
            if not args.include_files and is_file_only(tbl):
                skipped_files += 1
                continue
            if len(tbl.rows) < args.min_rows or not (tbl.is_layout() or args.include_ids):
                continue
            rec = struct_recorded(tbl, index)
            score, why = score_table(tbl)
            if rec and not args.all:
                hidden.append((tbl, rec))
                continue
            found.append((score, tbl, rec, why))
    found.sort(key=lambda t: -t[0])
    print(f"== Struct candidates (tables with >= {args.min_rows} offset rows, score >= {args.min_score}) ==")
    shown = 0
    for score, tbl, rec, why in found:
        if score < args.min_score:
            continue
        shown += 1
        rel = os.path.relpath(tbl.path, REPO_ROOT).replace("\\", "/")
        kind = "[file] " if "/formats/" in rel else ""
        tag = f"  (recorded as {rec[0]}: {rec[1]})" if rec else ""
        print(f"{score:3d}  {kind}{rel}:{tbl.line}  §{tbl.heading}{tag}")
        print(f"       {why}")
        near = closest_struct(tbl, index)
        if near and not rec:
            print(f"       closest known struct: {near[0]} ({near[1]}/{near[2]} offsets shared)")
    print(f"-- {shown} shown, {len(found) - shown} below the cutoff, of {len(found)} unrecorded tables")
    for tbl, rec in hidden:
        rel = os.path.relpath(tbl.path, REPO_ROOT).replace("\\", "/")
        print(f"   hidden as recorded ({rec[0]}, {rec[1]}): {rel}:{tbl.line}")
    if skipped_files:
        print(f"   {skipped_files} on-disk format tables skipped (--include-files to list them)")
    print()


def vtable_pass(args):
    vt = load_json("known_vtables.json")["vtables"]
    sym = es2_symbols.entries()
    recorded_addr = {i["address"].lower().lstrip("0") for d in vt for i in d["instances"]}
    labels = {i["label"] for d in vt for i in d["instances"]} | {d["name"] for d in vt}
    function_addr = {e["address"].lower().lstrip("0") for e in sym if e.get("type") == "function"}
    addr_hits, id_hits = {}, {}
    for path in md_files():
        rel = os.path.relpath(path, REPO_ROOT).replace("\\", "/")
        with open(path, encoding="utf-8") as f:
            for n, ln in enumerate(f.read().splitlines(), 1):
                for rx in (VT_ADDR_AFTER, VT_ADDR_BEFORE):
                    for m in rx.finditer(ln):
                        a = m.group(1).lower()
                        key = a.lstrip("0")
                        if key in recorded_addr or key in function_addr:
                            continue
                        addr_hits.setdefault(a, []).append((f"{rel}:{n}", ln.strip()[:110]))
                for m in VTABLE_ID.finditer(ln):
                    if m.group(1) not in labels:
                        id_hits.setdefault(m.group(1), []).append(f"{rel}:{n}")
    print("== Vtable addresses named beside 'vtable' in docs, in no instances list, not a known function ==")
    for a, where in sorted(addr_hits.items()):
        print(f"{a}  x{len(where)}  {where[0][0]}")
        print(f"       {where[0][1]}")
    print(f"-- {len(addr_hits)} addresses\n")
    print("== `...Vtable` identifiers in docs matching no instance label or definition name ==")
    for name, where in sorted(id_hits.items()):
        print(f"{name}  x{len(where)}  first: {where[0]}")
    print(f"-- {len(id_hits)} identifiers")
    print("Cross-check a hit with `es2_classes.py --vtables` (Borland descriptor records) before recording it.")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--structs", action="store_true")
    ap.add_argument("--vtables", action="store_true")
    ap.add_argument("--min-score", type=int, default=50)
    ap.add_argument("--min-rows", type=int, default=4)
    ap.add_argument("--include-files", action="store_true",
                    help="also list docs/formats tables not described as held in memory")
    ap.add_argument("--include-ids", action="store_true",
                    help="also consider tables whose first column is not headed as an offset")
    ap.add_argument("--all", action="store_true", help="include tables already recorded")
    args = ap.parse_args()
    both = not (args.structs or args.vtables)
    if args.structs or both:
        struct_pass(args)
    if args.vtables or both:
        vtable_pass(args)
    return 0


if __name__ == "__main__":
    sys.exit(main())
