#!/usr/bin/env python3
"""The known-symbols files, one per binary: tools/ghidra_scripts/known_symbols_<binary>.json.

Every Python tool finds, reads and writes them through this module, so their location and layout
live in one place. The schema is in tools/ghidra_scripts/README.md, "Known symbols".

The layout the writers keep: UTF-8 with a BOM, CRLF line ends, a header object holding
schema_version, _readme and entries, and each entry a block of FIELDS in that order, indented four
spaces, in ascending address order. Address order is what lets two branches that both add symbols
insert at different places in the file instead of conflicting at its top.
"""

from __future__ import annotations

import json
import os
import re

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPTS = os.path.join(REPO, "tools", "ghidra_scripts")
BINARIES = ("DBSIM", "VSHELL")
FIELDS = ["address", "binary", "type", "confidence", "name", "description", "source", "signature", "verified"]
BOM = b"\xef\xbb\xbf"
ENTRY_BLOCK = re.compile(r"    \{\n(?:      .*\n)*?    \}")
ENTRY_ADDRESS = re.compile(r'^      "address": "([0-9a-fA-F]{8})"', re.M)


def path(binary: str) -> str:
    assert binary in BINARIES, binary
    return os.path.join(SCRIPTS, f"known_symbols_{binary.lower()}.json")


PATHS = [path(b) for b in BINARIES]


def entries(binary: str | None = None) -> list[dict]:
    """One binary's entries, or both binaries' (DBSIM first) when binary is None."""
    out = []
    for b in BINARIES if binary is None else (binary,):
        with open(path(b), encoding="utf-8-sig") as f:
            out += json.load(f)["entries"]
    return out


def read_text(binary: str) -> tuple[str, str]:
    """(the file without its BOM, newlines normalised to \\n; the file's own newline)."""
    with open(path(binary), "rb") as f:
        raw = f.read()
    assert raw[:3] == BOM, "expected BOM"
    text = raw[3:].decode("utf-8")
    nl = "\r\n" if "\r\n" in text else "\n"
    if nl == "\r\n":
        assert text.count("\r\n") == text.count("\n"), "mixed newlines"
    return text.replace("\r\n", "\n"), nl


def write_text(binary: str, text: str, nl: str = "\r\n") -> None:
    with open(path(binary), "wb") as f:
        f.write(BOM + text.replace("\n", nl).encode("utf-8"))


def block(e: dict) -> str:
    """One entry as the file holds it, without the separating comma."""
    ordered = {k: e[k] for k in FIELDS if k in e}
    return "\n".join("    " + l for l in json.dumps(ordered, indent=2, ensure_ascii=False).split("\n"))


def render(header: dict, es: list[dict]) -> str:
    """A whole file: header keys other than entries, then the entries as blocks in address order."""
    head = json.dumps({k: v for k, v in header.items() if k != "entries"}, indent=2, ensure_ascii=False)
    body = ",\n".join(block(e) for e in sorted(es, key=lambda e: e["address"].lower()))
    return head[:-2] + ',\n  "entries": [\n' + body + "\n  ]\n}\n"


def insert_blocks(text: str, new: list[dict]) -> str:
    """Place each new entry's block before the first existing entry with a higher address, or
    after the last one."""
    for e in sorted(new, key=lambda e: e["address"].lower()):
        a = e["address"].lower()
        blocks = list(ENTRY_BLOCK.finditer(text))
        assert blocks, "no entries to anchor on"
        after = next((m for m in blocks if ENTRY_ADDRESS.search(m.group(0)).group(1).lower() > a), None)
        if after:
            text = text[:after.start()] + block(e) + ",\n" + text[after.start():]
        else:
            end = blocks[-1].end()
            text = text[:end] + ",\n" + block(e) + text[end:]
    return text


def check(binary: str, text: str) -> dict:
    """Parse one file's text and assert its invariants: every entry belongs to the file's binary,
    addresses ascend with no duplicate, and no name is used twice."""
    d = json.loads(text)
    es = d["entries"]
    wrong = [e["address"] for e in es if e["binary"] != binary]
    assert not wrong, (f"entries of another binary in the {binary} file", wrong[:10])
    addrs = [e["address"].lower() for e in es]
    for a, b in zip(addrs, addrs[1:]):
        assert a != b, ("duplicate address", binary, a)
        assert a < b, ("out of address order", binary, a, b)
    nk = [e["name"] for e in es if e.get("name")]
    dup = sorted({n for n in nk if nk.count(n) > 1}) if len(nk) != len(set(nk)) else []
    assert not dup, ("duplicate name", binary, dup)
    return d
