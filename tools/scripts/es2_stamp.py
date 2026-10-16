#!/usr/bin/env python3
"""Verification stamps on known_symbols entries: which functions have been settled, and how far.

A stamp records that a function's whole body was read and its known_symbols description checked
against it, so a later session can trust that description instead of re-reading the body. The
schema and the policy are in tools/ghidra_scripts/README.md, "Verification stamps".

A stamp pins the function's extent from the Ghidra function list -- its entry to the end of its
body's last address range, inline jump tables included -- and a hash of those bytes in the
binary's retail file, which tools/scripts/retail_binaries.py locates. `check` voids any stamp whose
extent has moved (a late-entry fix, a re-analysis) or whose bytes no longer hash the same (a
different build in that file), because the read it records was of other bytes.

`--via raw` stamps a straight-line stub that has no Ghidra function yet (a frameless vtable slot
target): its extent is decoded from the image up to the first RET. `check` verifies such a stamp
against that decode, listed as PENDING, until the function exists; from then on against the
function list, which voids the stamp if Ghidra's function spans other bytes.

Usage:
    python tools/scripts/es2_stamp.py stamp HddButton_OnClick --via decompile
    python tools/scripts/es2_stamp.py stamp 0044a178 --scope callees --via both
    python tools/scripts/es2_stamp.py stamp 00055d20+00055d50 00055dc8 --binary ES1DBSIM --via decompile
    python tools/scripts/es2_stamp.py stamp 0041b468 --via disasm \\
        --negative "The player's HERC starts each mission PASSIVE :: retail :: the user's play of the retail game"

Before writing a stamp, `stamp` reads the description sentence by sentence and refuses two shapes:
a sentence naming callers that does not say which search found them ("Callers found by es2_xref:
..."), and a sentence saying nothing or only something calls, reads, writes, references or reaches
something, or that code is unreachable, unused or dead, unless it is a listed --negative or names
the search it came from. Pass --read-callers when every caller a sentence names was read this
session, and --own-negatives when such a sentence describes this function's own code ("it makes no
other call"); both still print the sentences, so the choice is made looking at them.

A stamp vouches for what the function's own code does. A negative goes in it only when the data or a
retail observation settles it; a search that found nothing never does, because it is only "not found
by <search>" -- an Open item in the doc, never a fact.
    python tools/scripts/es2_stamp.py check            # every stamp, both binaries; exit 1 on any void
    python tools/scripts/es2_stamp.py lint             # stamped descriptions the stamp-time checks flag
    python tools/scripts/es2_stamp.py list
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys

import es2_symbols
import retail_binaries
from es2_xref import resolve
from retail_binaries import Image

SCOPES = ("body", "callees")
EVIDENCE = ("data", "retail")
VIA = ("decompile", "disasm", "both", "raw")
# The longest stub --via raw reads: a run of padding NOPs and a few instructions before the RET.
RAW_LIMIT = 32

# A sentence that says who calls the function. That is only ever known from a search or from reading
# the caller, so the sentence has to say which.
CALLER_RE = re.compile(r"\b(called (by|from)|calls? (it|this)|callers?|reached (only )?(from|through))\b", re.I)
# A sentence that states an absence or an exclusivity about other code -- who calls, reads, writes or
# reaches something -- which is the shape a search result takes. Negatives about the function's own
# body ("returns 0 when the index is not -1") are left alone.
NEGATIVE_RE = re.compile(
    r"\b(no|not|never|nothing|none|nobody|only|sole|solely)\b[^.;]{0,40}?"
    r"\b(calls?|called|callers?|reads?|readers?|writes?|writers?|references?|referenced|reach(es|ed)?|"
    r"reachable|uses?|used|xrefs?)\b"
    r"|\b(unreachable|unused|dead code|never runs|never fires)\b", re.I)
# A search phrased as one: it names the search, or says what was found by it.
SEARCH_RE = re.compile(r"\bfound by\b|\bnot found\b|\bsearch(ed)?\b|\b(es2_xref|es2_fieldscan|es2_naming|grep)\b", re.I)


def sentences(text: str) -> list[str]:
    """The description split at sentence ends and semicolons. A split inside a parenthesis is harmless:
    each piece is only matched, never rewritten."""
    return [p.strip() for p in re.split(r"(?<=[.;])\s+(?=[A-Z0-9`(\[])", text) if p.strip()]


def claim_findings(description: str, negatives: list[dict]) -> tuple[list[str], list[str]]:
    """The sentences that name callers without a search, and those with an unexplained negative."""
    claims = [n["claim"].lower() for n in negatives]
    callers, negs = [], []
    for s in sentences(description):
        if SEARCH_RE.search(s):
            continue
        if CALLER_RE.search(s):
            callers.append(s)
        elif NEGATIVE_RE.search(s) and not any(c and c in s.lower() for c in claims):
            negs.append(s)
    return callers, negs


def function_extents(binary: str) -> dict[int, int]:
    """address -> extent length, from the Ghidra function list export (address, name, body bytes,
    body address ranges per line). The extent runs from the entry to the end of the body's
    last range, so an inline jump table between two ranges is inside it and hashed with the code."""
    path = retail_binaries.get(binary).dump("functions.txt")
    if not os.path.exists(path):
        raise SystemExit("no function list at %s" % path)
    out = {}
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            parts = line.rstrip("\r\n").split("\t")
            if len(parts) != 4:
                raise SystemExit("%s has no ranges column; regenerate it with ghidra_full_decomp.py" % path)
            start = int(parts[0], 16)
            out[start] = int(parts[3].rsplit("-", 1)[1], 16) - start
    return out


def digest(image: Image, start: int, size: int) -> str:
    """The first 12 hex digits of the SHA-1 of the function's bytes, read through the section table."""
    for _name, va, vsize, raw_off, raw_size in image.sections:
        if va <= start and start + size <= va + min(vsize, raw_size):
            off = raw_off + (start - va)
            return hashlib.sha1(image.data[off:off + size]).hexdigest()[:12]
    raise SystemExit("%08x+%d is not inside one initialised section" % (start, size))


def raw_extent(image: Image, start: int) -> int | None:
    """The length of the straight-line stub at start, decoded from the image with capstone: its
    instructions up to and including the first RET. None when a branch, call or interrupt, or a byte
    capstone cannot decode, comes first, or there is no RET within RAW_LIMIT bytes. A stub entered
    only through a vtable slot has no Ghidra function until `es2_naming.py apply --define` makes
    one, and that function spans these same bytes, since the stub has no other flow."""
    import capstone
    off = image.off_of(start)
    if off is None:
        return None
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    end = start
    for i in md.disasm(image.data[off:off + RAW_LIMIT], start):
        if i.address != end:
            return None
        end = i.address + i.size
        if i.mnemonic == "ret":
            return end - start
        if i.mnemonic.startswith(("j", "call", "loop", "int", "iret", "ret", "hlt", "lcall", "ljmp")):
            return None
    return None


def parse_negative(text: str) -> dict:
    """'claim :: data|search :: how' -> {claim, evidence, how}."""
    parts = [p.strip() for p in text.split("::")]
    if len(parts) != 3 or parts[1] not in EVIDENCE or not all(parts):
        raise SystemExit("--negative wants 'claim :: %s :: how', got %r" % ("|".join(EVIDENCE), text))
    return {"claim": parts[0], "evidence": parts[1], "how": parts[2]}


def load(binary: str):
    """The file's header and entries, after checking that rewriting them reproduces the file exactly,
    so a stamp can never reformat or drop anything it did not mean to touch."""
    text, nl = es2_symbols.read_text(binary)
    header = es2_symbols.check(binary, text)
    if es2_symbols.render(header, header["entries"]) != text:
        raise SystemExit("%s does not round-trip through es2_symbols.render; refusing to rewrite it"
                         % es2_symbols.path(binary))
    return header, nl


def cmd_stamp(args) -> int:
    """Stamps every target that passes the checks, with the same options, in one write; a refused
    target is reported and left unstamped, and makes the exit status 1."""
    header, nl = load(args.binary)
    extents = function_extents(args.binary)
    image = Image(args.binary)
    negatives = [parse_negative(n) for n in args.negative]
    targets = [t for arg in args.target for t in arg.split("+") if t]
    refused_any = stamped = 0
    for t in targets:
        target = resolve(t, args.binary)
        entry = next((e for e in header["entries"] if int(e["address"], 16) == target), None)
        size = extents.get(target)
        # A target that cannot be stamped as asked is refused like a wording refusal, so one bad
        # address in a list leaves the rest of the list stamped.
        problem = None
        if entry is None:
            problem = "has no known_symbols entry; add one before stamping it"
        elif entry["type"] != "function":
            problem = "is a %s entry; stamps are for functions" % entry["type"]
        elif args.via == "raw":
            raw = raw_extent(image, target)
            if raw is None:
                problem = ("is not a straight-line stub ending in a RET within %d bytes; --via raw reads only "
                           "those (a stub that calls takes --via disasm once apply --define has made it)" % RAW_LIMIT)
            elif size is not None and size != raw:
                problem = "has a function-list extent of %d bytes, the stub %d; stamp it --via disasm" % (size, raw)
            else:
                size = raw
        elif size is None:
            problem = "is not a function start in the function list%s" % (
                "; a straight-line stub with no Ghidra function yet takes --via raw"
                if raw_extent(image, target) else "")
        if problem:
            print("%08x %s  REFUSED  %s" % (target, entry.get("name", "") if entry else "", problem))
            refused_any += 1
            continue

        stamp = {
            "bytes": "%08x+%d" % (target, size),
            "sha1": digest(image, target, size),
            "scope": args.scope,
        }
        callers, negs = claim_findings(entry.get("description", ""), negatives)
        refused = False
        for label, found, accepted, flag in (
                ("names callers without saying which search found them", callers, args.read_callers,
                 "--read-callers"),
                ("states a negative that is not a listed --negative", negs, args.own_negatives, "--own-negatives")):
            for s in found:
                print("%08x %s  %s  %s:\n    %s" % (target, entry.get("name", ""),
                                                   "accepted" if accepted else "REFUSED", label, s))
            if found and not accepted:
                refused = True
                print("  -> reword it as a search (\"found by <tool>\"), or pass %s if it is not one" % flag)
        if refused:
            refused_any += 1
            continue

        stamp["via"] = args.via
        if negatives:
            stamp["negatives"] = negatives
        entry["verified"] = stamp
        stamped += 1
        if len(targets) == 1:
            print("%08x  %s  stamped: %s" % (target, entry.get("name", ""), json.dumps(stamp, ensure_ascii=False)))

    if stamped:
        es2_symbols.check(args.binary, es2_symbols.render(header, header["entries"]))
        es2_symbols.write_text(args.binary, es2_symbols.render(header, header["entries"]), nl)
    if len(targets) > 1:
        print("%s: %d stamped, %d refused" % (args.binary, stamped, refused_any))
    return 1 if refused_any else 0


def cmd_check(args) -> int:
    """A --via raw stamp whose address has no Ghidra function yet is checked against the stub's
    extent decoded from the image instead, and listed as pending; once `apply --define` has made the
    function and the dumps are regenerated, it is checked against the function list like any other."""
    void = 0
    total = 0
    pending = 0
    for binary in (args.binary,) if args.binary else es2_symbols.BINARIES:
        stamped = [e for e in es2_symbols.entries(binary) if "verified" in e]
        if not stamped:
            continue
        extents = function_extents(binary)
        image = Image(binary)
        for e in stamped:
            total += 1
            v = e["verified"]
            start_hex, size_text = v["bytes"].split("+")
            start, size = int(start_hex, 16), int(size_text)
            problems = []
            if start != int(e["address"], 16):
                problems.append("stamp is for %s, entry is %s" % (start_hex, e["address"]))
            if v.get("scope") not in SCOPES:
                problems.append("scope %r is not one of %s" % (v.get("scope"), ", ".join(SCOPES)))
            if "via" in v and v["via"] not in VIA:
                problems.append("via %r is not one of %s" % (v["via"], ", ".join(VIA)))
            for n in v.get("negatives", []):
                if n.get("evidence") not in EVIDENCE or not n.get("claim") or not n.get("how"):
                    problems.append("malformed negative %r" % n)
            now = extents.get(start)
            if now is None and v.get("via") == "raw":
                now = raw_extent(image, start)
                if now == size and not problems and digest(image, start, size) == v["sha1"]:
                    pending += 1
                    print("PENDING  %s %s  raw stub, no Ghidra function yet (es2_naming.py apply --define)"
                          % (e["address"], e.get("name", "")))
                    continue
                if now != size:
                    problems.append("raw stub extent is now %s, stamped %d" % (now, size))
                elif digest(image, start, size) != v["sha1"]:
                    problems.append("bytes no longer hash to %s" % v["sha1"])
            elif now != size:
                problems.append("function extent is now %s, stamped %d"
                                % ("gone" if now is None else now, size))
            elif digest(image, start, size) != v["sha1"]:
                problems.append("bytes no longer hash to %s" % v["sha1"])
            if problems:
                void += 1
                print("VOID  %s %s  %s" % (e["address"], e.get("name", ""), "; ".join(problems)))
    print("%d stamp(s) checked, %d void" % (total, void) + (", %d pending a Ghidra function" % pending if pending else ""))
    return 1 if void else 0


def cmd_lint(args) -> int:
    """Stamped descriptions holding a sentence `stamp` would now stop on. Advisory: a stamp made with
    --read-callers or --own-negatives shows here too, and is fine when that was the right call."""
    flagged = 0
    for binary in (args.binary,) if args.binary else es2_symbols.BINARIES:
        for e in es2_symbols.entries(binary):
            v = e.get("verified")
            if not v:
                continue
            callers, negs = claim_findings(e.get("description", ""), v.get("negatives", []))
            if callers or negs:
                flagged += 1
                print("%s %s %s  (via %s)" % (binary, e["address"], e.get("name", ""), v.get("via", "unrecorded")))
                for s in callers:
                    print("    callers:  %s" % s)
                for s in negs:
                    print("    negative: %s" % s)
    print("%d stamped description(s) flagged" % flagged)
    return 1 if flagged else 0


def cmd_list(args) -> int:
    for binary in (args.binary,) if args.binary else es2_symbols.BINARIES:
        for e in es2_symbols.entries(binary):
            if "verified" in e:
                v = e["verified"]
                print("%s %-8s %s  %s%s" % (e["address"], v["scope"], binary, e.get("name", ""),
                                            "  (%d negative)" % len(v["negatives"]) if v.get("negatives") else ""))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("stamp", help="stamp function entries, replacing any stamp they had")
    s.add_argument("target", nargs="+",
                   help="hex address or known_symbols name; several, or a+b+c, take the same options")
    s.add_argument("--binary", default="DBSIM", choices=es2_symbols.BINARIES)
    s.add_argument("--scope", default="body", choices=SCOPES,
                   help="body: the function's own code; callees: also every callee the description relies on")
    s.add_argument("--negative", action="append", default=[],
                   help="'claim :: data|retail :: how' -- a negative claim the description makes that the data or a "
                        "retail observation settles. Never a search result: that is an Open item in the doc")
    s.add_argument("--via", required=True, choices=VIA,
                   help="how the body was read: the decompile, the disassembly, or both. A claim that rests on an "
                        "argument value, a register, or where one function ends wants the disassembly. raw: the "
                        "capstone disassembly of a straight-line stub up to its RET, which needs no Ghidra function, "
                        "so a stub can be stamped before apply --define creates one")
    s.add_argument("--read-callers", action="store_true",
                   help="every caller the description names was itself read this session, not found by a search")
    s.add_argument("--own-negatives", action="store_true",
                   help="the description's negative words describe this function's own code, not a search")
    s.set_defaults(fn=cmd_stamp)

    c = sub.add_parser("check", help="void any stamp whose function extent or bytes changed")
    c.add_argument("--binary", choices=es2_symbols.BINARIES)
    c.set_defaults(fn=cmd_check)

    t = sub.add_parser("lint", help="stamped descriptions the stamp-time claim checks flag")
    t.add_argument("--binary", choices=es2_symbols.BINARIES)
    t.set_defaults(fn=cmd_lint)

    l = sub.add_parser("list", help="list stamped entries")
    l.add_argument("--binary", choices=es2_symbols.BINARIES)
    l.set_defaults(fn=cmd_list)

    args = ap.parse_args()
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
