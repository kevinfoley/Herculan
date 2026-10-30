#!/usr/bin/env python3
"""Helpers for working down the function-naming backlog in the known_symbols_<binary>.json files.

Subcommands (BIN is a binary tools/scripts/retail_binaries.py registers: DBSIM, VSHELL, ES1DBSIM,
ES1GO or ES1ESCD). rtti, vtables, classes and stats read Borland class records and refuse ES1's Symantec C++
binaries. match pairs two PE images or two X-32 images, never one of each, since the two mask
address operands by different rules (insn_streams).

  triage BIN [--limit N] [--max-lines L] [--sort named|sites|callers|refs] [--all]
      The es2_unnamed_callees backlog with each target's decompile size and its own unnamed
      callee count, so short leaf functions can be taken first. Columns: named callers, direct
      call/branch sites in the whole binary, distinct calling functions (fns), and refs: every
      E8/E9 rel32 plus every stored dword pointer to the address anywhere in the image (the
      es2_xref.py sweep, so vtable slots, callback and factory tables count). --sort orders by
      named callers (default), by sites, by fns, or by refs. --all widens the backlog from
      callees of named functions to every unnamed function start in the dump.
  body BIN addr... [-d] [--full]
      Decompile bodies with known_symbols names substituted for FUN_/DAT_ labels, the header
      banner, blank lines and local declarations dropped (--full keeps them). -d appends the
      disassembly; where Ghidra has no function, a capstone disassembly of the image up to the
      next function entry, saying whose extent the address falls in.
  diff BIN addr BIN2 addr2
      Unified diff of two decompiled bodies with names substituted and the remaining FUN_/DAT_
      labels, locals and Ghidra variable numbers normalised: whether one binary's function is a
      copy of the other's, and where the two differ. An ES1 function against an ES2 one is headed
      with the cross-compiler match's similarity score.
  diff BIN BIN2 a:b [c:d ...]
      The same for several chosen pairs in one run (a:b+c:d also works), each headed with both
      names; the decompiles are read once.
  match BIN BIN2 [--range lo-hi] [--only a+b...] [--min N] [--fuzzy R] [--all]
      BIN's unnamed functions (every function with --all), in the range or the list, of at least N
      instructions (default 6)
      whose disassembly, with every address operand masked, equals a BIN2 function's; with
      --fuzzy R, the three closest named BIN2 functions at a difflib ratio of at least R when there
      is no exact match. Under a unique exact named match it lists the unnamed BIN callees and
      globals that pair, operand by operand, with named BIN2 ones. Confirm a candidate with diff.
  match ES1BIN ES2BIN[+ES2BIN] [--range lo-hi] [--only a+b...] [--min N] [--fuzzy R] [--all]
      Cross-compiler form: the ES1 image's unnamed functions (every one with --all; never the
      Symantec runtime es1_libmatch.py names) of at least N tokens (default 20), ranked against the
      named functions of the ES2 binaries by similarity of their normalised decompiles, since the
      two compilers' instructions never match. Names count only when both games use them; every
      other name reads as FUN_?/DAT_?. Each candidate line gives the size bucket (S under 50 tokens,
      M under 200, L) and the score that must reach its threshold (0.9, 0.8, 0.75; --fuzzy R sets one
      for all), the sequence and multiset ratios and shared names agreeing, the same name's hits
      in the other target, the best runner-up under another name (TIE within 0.02), and the diff
      command (--diff prints the diff itself instead, and the runner-up's when it ties). --only
      scores just the listed functions, below threshold too (marked '-' after the
      score). A score is a lead, never
      a name: siblings with one shape (FileRStream_Close, FileWStream_Close) and short dispatchers
      score as high as twins, and a body that inlines what the other calls scores low. ES1DBSIM's
      library region starts at __cinit (0005b68c).
  callers BIN addr... [-n CTX] [--max M]
      Every decompile line naming FUN_/DAT_<addr> (or its known name), with the enclosing function.
  precheck BIN addr...
      The known_symbols entry and every Herculan docs/src/tests mention of each address.
  sym BIN regex...
      known_symbols entries whose name or address matches, marked stamped (scope, via) or unstamped.
  funcs BIN lo [hi]
      The Ghidra function list from lo to hi (default lo + 0x400): size, known name, and marks for
      unnamed, low, stamped, and (X-32) a Ghidra function on the 0x90 padding before a prologue.
  rtti BIN vtable...
      Borland class of a vtable: typeinfo = dword at vt-0xc, size = dword ti+0, name = C string
      at ti + word[ti+6].
  vtables BIN [vtable|class-regex ...] [--unnamed]
      Every table in analysis_out/BIN_vtables_full.txt with its RTTI class and size, the
      functions that install it (constructors and destructors; a class is its last vtable write)
      and its slots, names taken from known_symbols rather than the dump. An unnamed slot is marked
      '*', and each slot carries the number of tables holding the same function (xN): a function
      shared by many tables is a base-class method. Arguments filter by table address or class
      name; --unnamed keeps only tables with an unnamed slot ('L' marks one that has a low-confidence
      entry already). "installed by" names each vtable store's Ghidra function, or the store's own
      address when no function extent holds it (stores are also found by a raw C7 scan, so ones in
      undisassembled bytes show). A closing line totals the unnamed functions.
  classes BIN [regex...] [--unnamed]
      Every Borland class record: size, bases with subobject offsets, primary vtable, the +0x28
      destructor and +0x14 operator delete with known_symbols names, marked like vtables slots.
      --unnamed keeps the records whose destructor or operator delete is unnamed.
  stats BIN [--list]
      The coverage table of Herculan/docs/herculan/plan-completing-coverage.md from the current
      dumps. --list adds every 55 8B EC prologue outside a function extent and every code-section
      gap between extents that is not all fill bytes (00/90/CC).
  insert batch.json [--write]
      Insert new entries into their binary's file in address order, validating schema, uniqueness,
      maybe_ vs medium, and control characters. Dry run without --write.
  edit edits.json [--write]
      [{"binary","address","field","value"} or {"binary","address","fields":{...}}]: rewrite that
      one entry's block, all fields of one edit before validating (so a low entry can take a
      confidence and a name together). "replace": [[old, new], ...], alone or beside them, edits
      the description in place instead of restating it: each old text must occur exactly once. A name change also renames the old name, or the FUN_/DAT_
      label of a newly named entry, elsewhere in the file (word boundary).
  relink batch.json [--bare] [--write]
      Rewrite FUN_/DAT_<addr> mentions of the batch's named entries in the descriptions of other
      entries of the same binary (insert does this itself). --bare also rewrites a bare address
      ("made by 00074f9c") to "Name (00074f9c)", listing each; insert reports how many there are.
      Stamps are kept, since the rewrite only names what the address denoted. insert, edit and
      relink undo a doubling they write ("Name (Name (00012345))" -> "Name (00012345)", "Name (Name)"
      -> "Name"), printing each, and print CHECK for a new "Name, Name (00012345)", which is a
      doubling unless both arguments really are that object. insert and edit print SHAPE for a
      description that writes a name with its address as "(Name, 00012345)" or "Name's (00012345)",
      which relink and the doubling checks do not recognise: write "Name (00012345)".
  mentions batch.json
      FUN_/DAT_/bare-address mentions of the batch's named addresses in docs/src/tests.
  fixrefs batch.json [--write]
      Rewrite `FUN_a` to `Name` (`a`) in docs and <c>FUN_a</c> / bare FUN_a in C#, and a bare
      backticked `a` to `Name` (`a`) in the docs of its binary's family (docs/es1/ for ES1DBSIM,
      ES1GO and ES1ESCD, docs/retail/ for DBSIM and VSHELL): the first mention in each paragraph, list item or
      table row, never one after a name or a binary's name, in a link, in an address column or in
      a code fence. Lists each rewrite per file, and the mentions it cannot rewrite safely (after
      another name, after a binary's name, an address another binary of the family also has, other
      shapes) for a manual edit.
  repl spec.json [--write]
      [[relpath, old, new, count]]: exact replacements asserting the count; BOM and newlines kept.
  apply BIN [--write] [--define [addr+addr...]]
      Run ES2ApplySymbolNames headless (-readOnly unless --write) and print only the summary,
      errors and warnings. --define first runs ES2DefineFunctionAt in the same session, on the
      listed addresses or, with none, on every named function entry that has no Ghidra function
      (one inside another function's extent is reported instead: that is a fixentry case).
  fixentry BIN addr[+addr...] [--write]
      Repair functions Ghidra starts past their prologue, or early on the fill before it
      (es2_late_entries.py finds both): one
      headless session runs ES2MergeFunctionAt <addr> auto for each true entry, then
      ES2ApplyStructures and ES2ApplySymbolNames (the merge drops the old functions' signatures
      and struct-typed parameters), then ES2CheckFunctionEntries. -readOnly unless --write; a
      FAILED or SHORT line in the dry run means do not write. Re-address the known_symbols entry
      first when the name sits on the late address.

Write batch/spec JSON files with the Write tool; heredocs eat backslashes.
"""

from __future__ import annotations

import bisect
import collections
import contextlib
import io
import json
import os
import pickle
import re
import shutil
import struct
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_symbols as S  # noqa: E402
import retail_binaries  # noqa: E402
import es2_unnamed_callees as uc  # noqa: E402

REPO = uc.REPO
ANALYSIS = uc.ANALYSIS
HDR = re.compile(r"^/\* =+\n   (\S+) @ ([0-9a-f]{8})[^\n]*\n   =+ \*/\n", re.M)
FIELDS = S.FIELDS
CACHE = os.path.join(tempfile.gettempdir(), "es2_naming_cache")
LABEL = re.compile(r"\b(?:thunk_)?(FUN|DAT|PTR|LAB|_DAT)_([0-9a-f]{8})\b")
DECL = re.compile(r"^  [A-Za-z_][\w ]*?[\w*]+ \**[A-Za-z_]\w*(?:\s*\[\w+\])?;(?:\s*/\*.*\*/)?$")


_PENDING = {}


def add_pending(batch, default_binary):
    """Treat a batch's entries as known_symbols entries for the rest of this process, without
    writing them: es1_naming.py twin reads a batch the session has not inserted yet."""
    for e in batch:
        b = e.get("binary", default_binary).upper()
        _PENDING.setdefault(b, {})[e["address"].lower()] = {**e, "binary": b}


def entries(binary=None):
    base = S.entries(binary)
    extra = [e for b, d in _PENDING.items() if binary is None or b == binary.upper() for e in d.values()]
    if not extra:
        return base
    over = {(e["binary"], e["address"].lower()) for e in extra}
    return [e for e in base
            if (e.get("binary", binary or "").upper(), e["address"].lower()) not in over] + extra


def names(binary):
    return {e["address"].lower(): e["name"] for e in entries(binary) if e.get("name")}


_BODIES, _XTOKENS = {}, {}


def bodies(binary):
    if binary not in _BODIES:
        _BODIES[binary] = _bodies(binary)
    return _BODIES[binary]


def _bodies(binary):
    src = os.path.join(ANALYSIS, f"{binary}_decomp_full.c")
    os.makedirs(CACHE, exist_ok=True)
    cache = os.path.join(CACHE, f"{binary}_bodies.pkl")
    if os.path.exists(cache) and os.path.getmtime(cache) > os.path.getmtime(src):
        with open(cache, "rb") as f:
            return pickle.load(f)
    with open(src, encoding="utf-8", errors="replace") as f:
        text = f.read()
    ms = list(HDR.finditer(text))
    d = {}
    for i, m in enumerate(ms):
        end = ms[i + 1].start() if i + 1 < len(ms) else len(text)
        d[m.group(2)] = text[m.start():end]
    with open(cache, "wb") as f:
        pickle.dump(d, f)
    return d


def compact(body, known):
    lines = body.splitlines()
    m = HDR.match(body)
    if m:
        lines = body[m.end():].splitlines()
    out = []
    in_plate = False
    for l in lines:
        if not out and not in_plate and l.startswith("/* ") and "*/" not in l:
            in_plate = True
            continue
        if in_plate:
            in_plate = "*/" not in l
            continue
        if not l.strip() or DECL.match(l) or l.startswith("/* WARNING: Globals starting"):
            continue
        out.append(l)
    text = "\n".join(out)
    return LABEL.sub(lambda m: known.get(m.group(2), m.group(0)), text)


def disasm(binary, addr):
    out, grab = [], False
    with open(os.path.join(ANALYSIS, f"{binary}_disasm_full.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            if not grab:
                if line.startswith("; FUNCTION ") and line.rstrip().endswith("@ " + addr):
                    grab = True
                    out.append(line)
            elif line.startswith("; FUNCTION ") or line.startswith(";====="):
                break
            else:
                out.append(line)
    return "".join(out)


def addrs(args):
    return [a.lower().zfill(8) for a in args]


def functions(binary):
    """{address: (Ghidra name, body bytes, extent end)} from analysis_out/BIN_functions.txt. The extent
    runs from the entry to the end of the body's last address range, so an inline jump table or other
    data between the body's ranges lies inside it; body bytes count the ranges alone. A list without
    the ranges column (an older dump) gives entry + body bytes."""
    out = {}
    with open(os.path.join(ANALYSIS, f"{binary}_functions.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            a, n, s, *rest = line.rstrip("\r\n").split("\t")
            end = int(rest[0].rsplit("-", 1)[1], 16) if rest else int(a, 16) + int(s)
            out[a.lower()] = (n, int(s), end)
    return out


class Extents:
    """Ghidra function extents, each entry .. the end of the body's last range (see functions). An
    extent that runs past the next entry (a body chunk placed after another function) is reported,
    since containing() then credits that function's bytes to it."""

    def __init__(self, binary):
        self.fns = functions(binary)
        self.starts = sorted(int(a, 16) for a in self.fns)
        for e, nxt in zip(self.starts, self.starts[1:]):
            if self.fns[f"{e:08x}"][2] > nxt:
                print(f"warning: {binary} function {e:08x}'s extent runs past the entry {nxt:08x}", file=sys.stderr)

    def containing(self, va):
        """(entry, offset) of the function whose extent holds va, or None."""
        i = bisect.bisect_right(self.starts, va) - 1
        if i < 0:
            return None
        e = self.starts[i]
        return (e, va - e) if va < self.fns[f"{e:08x}"][2] else None

    def next_start(self, va):
        i = bisect.bisect_right(self.starts, va)
        return self.starts[i] if i < len(self.starts) else None


def unnamed_label(name):
    return name.startswith(uc.UNNAMED_PREFIXES)


def image_refs(binary, targets):
    """address -> E8/E9 rel32 branches (any offset in a code section) plus stored little-endian
    dwords (any offset, any section) equal to it: es2_xref.py's sweep for many targets in one pass.
    Brute force, like es2_xref.py, so a byte run that merely looks like a branch can count."""
    import es2_xref as X
    image = X.Image(binary)
    want = {int(a, 16): a for a in targets}
    out = {}
    data = image.data
    for _n, va, _vs, raw_off, raw_size in image.executable():
        for i in range(raw_off, raw_off + raw_size - 5):
            if data[i] in (0xE8, 0xE9):
                t = va + (i - raw_off) + 5 + struct.unpack_from("<i", data, i + 1)[0]
                if t in want:
                    out[want[t]] = out.get(want[t], 0) + 1
    for i in range(len(data) - 3):
        t = struct.unpack_from("<I", data, i)[0]
        if t in want:
            out[want[t]] = out.get(want[t], 0) + 1
    return out


def cmd_triage(args):
    binary = args[0]
    limit = int(args[args.index("--limit") + 1]) if "--limit" in args else 0
    maxl = int(args[args.index("--max-lines") + 1]) if "--max-lines" in args else 10 ** 9
    sort = args[args.index("--sort") + 1] if "--sort" in args else "named"
    assert sort in ("named", "sites", "callers", "refs"), sort
    rows = uc.analyse(binary, uc.CONFIDENCE_RANK["high"], False, None)
    labels, branches = uc.parse_disasm(binary)
    if "--all" in args:
        known_fns = {a for a, e in uc.load_symbols(binary).items() if e.get("name")}
        seen = {r["address"] for r in rows}
        sites = {}
        for _, ss in branches.items():
            for _, k, t in ss:
                sites[t] = sites.get(t, 0) + 1
        for a, label in labels.items():
            if a.startswith("~") or a in known_fns or a in seen or not label.startswith(uc.UNNAMED_PREFIXES):
                continue
            rows.append({"address": a, "named_caller_count": 0, "named_callers": {},
                         "total_call_sites": sites.get(a, 0)})
    backlog = {r["address"] for r in rows}
    refs = image_refs(binary, backlog)
    for r in rows:
        r["refs"] = refs.get(r["address"], 0)
    # Distinct functions anywhere in the binary that CALL each backlog target.
    callers = {}
    for c, sites in branches.items():
        for _, k, t in sites:
            if k == "CALL" and t in backlog:
                callers.setdefault(t, set()).add(c)
    for r in rows:
        r["all_callers"] = len(callers.get(r["address"], ()))
    if sort == "sites":
        rows.sort(key=lambda r: (-r["total_call_sites"], -r["all_callers"], r["address"]))
    elif sort == "callers":
        rows.sort(key=lambda r: (-r["all_callers"], -r["total_call_sites"], r["address"]))
    elif sort == "refs":
        rows.sort(key=lambda r: (-r["refs"], -r["all_callers"], r["address"]))
    known = names(binary)
    d = bodies(binary)
    shown = 0
    print("address   named sites fns  refs lines unnamedCallees  callers")
    for r in rows:
        a = r["address"]
        n = len(compact(d[a], known).splitlines()) if a in d else -1
        if n > maxl:
            continue
        sub = sorted({t for _, k, t in branches.get(a, []) if k == "CALL" and t not in known})
        print(f"{a}  {r['named_caller_count']:>5} {r['total_call_sites']:>5} {r['all_callers']:>3} {r['refs']:>5} {n:>5} {len(sub):>3}"
              f"{'(' + ','.join(t for t in sub if t in backlog) + ')' if any(t in backlog for t in sub) else ''}"
              f"  {', '.join(sorted(r['named_callers'])[:3])}")
        shown += 1
        if limit and shown >= limit:
            break


def cmd_body(args):
    binary, rest = args[0], args[1:]
    dis, full = "-d" in rest, "--full" in rest
    known = names(binary)
    d = bodies(binary)
    ext = None
    for a in addrs(x for x in rest if not x.startswith("-")):
        if a not in d:
            print(f"### {a} not in decompile")
        else:
            print(f"### {a} {known.get(a, '')}")
            print(d[a] if full else compact(d[a], known))
        if dis:
            text = disasm(binary, a)
            if not text:
                ext = ext or Extents(binary)
                text = raw_disasm(binary, int(a, 16), known, ext)
            print(text)


def raw_disasm(binary, va, known, ext, limit=0x300):
    """Capstone disassembly of the image from va, for an address where Ghidra has no function: up to
    the next Ghidra function entry, or limit bytes. Ghidra's own listing at such an address may be
    undefined bytes, or instructions it never promoted to a function."""
    import capstone
    d, off = pe(binary)
    inside = ext.containing(va)
    nxt = ext.next_start(va)
    end = min(va + limit, nxt) if nxt else va + limit
    head = f"; capstone from {va:08x} to {end:08x}"
    if inside:
        e, o = inside
        head += f"; inside the extent of {known.get(f'{e:08x}', ext.fns[f'{e:08x}'][0])} ({e:08x}+0x{o:x})"
    if nxt and nxt == end:
        head += f"; next Ghidra function {known.get(f'{nxt:08x}', ext.fns[f'{nxt:08x}'][0])} ({nxt:08x})"
    out = [head]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    o = off(va)
    for i in md.disasm(d[o:o + end - va], va):
        line = f"{i.address:08x}   {i.bytes.hex():<20} {i.mnemonic} {i.op_str}"
        if i.mnemonic in ("call", "jmp") or i.mnemonic.startswith("j"):
            m = re.fullmatch(r"0x([0-9a-f]+)", i.op_str)
            if m and m.group(1).zfill(8) in known:
                line += f"   ; -> {known[m.group(1).zfill(8)]}"
        for m in re.finditer(r"0x([4][0-9a-f]{5})\b", i.op_str):
            if m.group(1).zfill(8) in known and "; ->" not in line:
                line += f"   ; {known[m.group(1).zfill(8)]}"
        out.append(line)
    return "\n".join(out)


def normalised(binary, addr, known=None):
    """Compact body with known names substituted, then every remaining FUN_/DAT_/PTR_/LAB_ label,
    stack-local name and the function's own name reduced to a placeholder, so two compiled copies
    of one function compare equal up to what they actually do. `known` overrides the binary's
    names, e.g. to give another binary's names to the addresses paired with them."""
    known = names(binary) if known is None else known
    body = bodies(binary).get(addr)
    if body is None:
        return None
    t = compact(body, known)
    t = re.sub(r"\bPTR_\w*?[0-9a-f]{8}\b", "PTR_?", t)
    t = re.sub(r"\b(?:thunk_)?(FUN|DAT|LAB|_DAT)_[0-9a-f]{8}\b", r"\1_?", t)
    # A string's label is its text and its address; the address is a relocated operand.
    t = re.sub(r"\b(s_\w*?)_[0-9a-f]{8}\b", r"\1_?", t)
    t = re.sub(r"\b(?:local|auStack|uStack|iStack|puStack|pcStack|in_stack)_[0-9a-f]+\b", "local_?", t)
    t = re.sub(r"\b(?:[a-z]{1,3}Var)\d+\b", "var?", t)
    own = known.get(addr)
    if own:
        t = re.sub(r"\b" + re.escape(own) + r"\b", "SELF", t)
    head, _, rest = t.partition("\n")
    head = re.sub(r"\b__(?:cdecl|stdcall|fastcall|thiscall) ", "", head).replace("FUN_?(", "SELF(", 1)
    return head + "\n" + rest


def cmd_diff(args):
    """diff BIN addr BIN2 addr2: unified diff of the two normalised bodies (empty when they match).
    diff BIN BIN2 a:b [c:d ...] (or a:b+c:d): the same for each pair, each headed with both names."""
    import difflib
    if len(args) >= 3 and args[1].upper() in retail_binaries.IDS:
        b1, b2 = args[0], args[1]
        k1, k2 = names(b1), names(b2)
        for pair in (p for arg in args[2:] for p in re.split(r"[+,]", arg) if p):
            x, _, y = pair.partition(":")
            x, y = x.lower().zfill(8), y.lower().zfill(8)
            print(f"===== {b1} {x} {k1.get(x, '')}  /  {b2} {y} {k2.get(y, '')}")
            cmd_diff([b1, x, b2, y])
            print()
        return
    (b1, a1, b2, a2) = args[:4]
    a1, a2 = a1.lower().zfill(8), a2.lower().zfill(8)
    t1, t2 = normalised(b1, a1), normalised(b2, a2)
    if t1 is None or t2 is None:
        print("not in decompile:", a1 if t1 is None else "", a2 if t2 is None else "")
        return
    if retail_binaries.get(b1).compiler != retail_binaries.get(b2).compiler:
        shared = shared_names(b1, [b2])
        score, seq, bag, agree, total = xscore(xmask(b1, shared)[a1], xmask(b2, shared)[a2], shared)
        print(f"cross-compiler similarity {score:.2f} (seq {seq:.2f} bag {bag:.2f}"
              + (f" names {agree}/{total}" if total else "") + f"; {xbucket(len(xtokens(b1)[a1]))[0]} bucket)")
    if t1 != t2 and "".join(t1.split()) == "".join(t2.split()):
        print(f"identical after normalisation, up to line breaks ({len(t1.splitlines())} lines)")
        return
    d = list(difflib.unified_diff(t1.splitlines(), t2.splitlines(), f"{b1} {a1}", f"{b2} {a2}", lineterm="", n=1))
    print("\n".join(d) if d else f"identical after normalisation ({len(t1.splitlines())} lines)")


def cmd_callers(args):
    binary, rest = args[0], args[1:]
    ctx = int(rest[rest.index("-n") + 1]) if "-n" in rest else 0
    mx = int(rest[rest.index("--max") + 1]) if "--max" in rest else 10 ** 9
    rest = [x for i, x in enumerate(rest) if not x.startswith("-") and (i == 0 or rest[i - 1] not in ("-n", "--max"))]
    known = names(binary)
    d = bodies(binary)
    for a in addrs(rest):
        alts = [r"(?:FUN|DAT|PTR|LAB|_DAT)_" + a]
        if a in known:
            alts.append(re.escape(known[a]))
        pat = re.compile(r"\b(?:" + "|".join(alts) + r")\b", re.I)
        total = 0
        print(f"##### {a} {known.get(a, '')}")
        for fa, body in d.items():
            if fa == a:
                continue
            lines = body.splitlines()
            for i, l in enumerate(lines):
                if pat.search(l):
                    total += 1
                    if total <= mx:
                        lo, hi = max(0, i - ctx), min(len(lines), i + ctx + 1)
                        print(f"  [{known.get(fa, fa)}] " + " | ".join(x.strip() for x in lines[lo:hi]))
        print(f"  total {total}" + (f" (showed {mx})" if total > mx else ""))


def repo_texts():
    files = {}
    for sub in ("docs", "src", "tests"):
        for dp, dn, fn in os.walk(os.path.join(REPO, "Herculan", sub)):
            dn[:] = [x for x in dn if x not in ("bin", "obj")]
            for f in fn:
                if f.endswith((".md", ".cs")):
                    p = os.path.join(dp, f)
                    with open(p, encoding="utf-8", errors="replace") as fh:
                        files[p] = fh.read().splitlines()
    return files


def cmd_precheck(args):
    binary = args[0]
    es = entries()
    texts = repo_texts()
    for a in addrs(args[1:]):
        print(f"== {a}")
        for e in es:
            if e["address"].lower() == a and e["binary"] == binary:
                print(f"  ENTRY [{e['confidence']}] {e.get('name')}: {e.get('description', '')[:240]}")
        pat = re.compile(r"(?<![0-9A-Za-z])" + a + r"(?![0-9A-Za-z])", re.I)
        for p, lines in texts.items():
            for i, l in enumerate(lines):
                if pat.search(l):
                    print(f"  {os.path.relpath(p, REPO)}:{i + 1}: {l.strip()[:220]}")


def stamp_mark(e):
    """'stamped <scope> <via>' for a stamped entry, 'unstamped' for an unstamped function, '' for data."""
    v = e.get("verified")
    if v:
        return f"stamped {v.get('scope', '?')} {v.get('via', '?')}"
    return "unstamped" if e["type"] == "function" else ""


def cmd_sym(args):
    binary = args[0]
    for p in args[1:]:
        r = re.compile(p, re.I)
        for e in entries():
            if e["binary"] == binary and (r.search(e.get("name") or "") or r.search(e["address"])):
                mark = stamp_mark(e)
                print(f"{e['address']} [{e['type']}/{e['confidence']}{'/' + mark if mark else ''}] "
                      f"{e.get('name')}: {e.get('description', '')}\n")


def cmd_funcs(args):
    """funcs BIN lo [hi]: the Ghidra function list from lo up to hi (default lo + 0x400)."""
    binary = args[0]
    lo = int(args[1], 16)
    hi = int(args[2], 16) if len(args) > 2 else lo + 0x400
    fns = functions(binary)
    es = {e["address"]: e for e in entries(binary)}
    img = retail_binaries.Image(binary)
    x32 = img.binary.container == retail_binaries.X32
    print("address   bytes  mark  name   ('*' unnamed, 'L' low entry, 'S' stamped, 'p' Ghidra function on "
          "the 0x90 padding before a prologue)")
    for a in sorted(fns):
        v = int(a, 16)
        if not lo <= v < hi:
            continue
        gname, size, _ = fns[a]
        e = es.get(a)
        name = e.get("name") if e else None
        pad = x32 and img.read(v, 1) == b"\x90" and img.entries_of(v)[-1] != v
        mark = ("p" if pad else "") + ("S" if e and e.get("verified") else
                                       " " if name else "L" if e else "*")
        print(f"{a}  {size:>5}  {mark:<4}  {name or gname}")


def pe(binary):
    """(the binary's file bytes, virtual address -> file offset or None), for any registered binary."""
    img = retail_binaries.Image(binary)
    return img.data, img.off_of


def borland(binary, what):
    """Refuse a subcommand that reads Borland class records on a binary another compiler built."""
    retail_binaries.require(binary, f"es2_naming.py {what}",
                            "it reads Borland class records and typeinfo, and ES1's vtables point to none",
                            compiler=retail_binaries.BORLAND)


def cmd_rtti(args):
    borland(args[0], "rtti")
    d, off = pe(args[0])
    for a in args[1:]:
        o = off(int(a, 16) - 0xc)
        ti = struct.unpack_from("<I", d, o)[0] if o is not None else 0
        to = off(ti)
        if to is None:
            print(a, "unmapped", hex(ti))
            continue
        size = struct.unpack_from("<I", d, to)[0]
        no = struct.unpack_from("<H", d, to + 6)[0]
        print(f"{a}: ti={ti:08x} size=0x{size:x} name={d[to + no:to + no + 64].split(b'\0')[0].decode('latin1')}")


VT_HEAD = re.compile(r"^=== (\S+) .*?@ ([0-9a-f]{8})\s+\((\d+) slots\) ===$")
VT_SLOT = re.compile(r"^  \+0x([0-9a-f]+) \([0-9a-f]{8}\): \S+ @ ([0-9a-f]{8})$")
VT_STORE = re.compile(r"^([0-9a-f]{8})\s+[0-9a-f]+\s+MOV dword ptr \[[^\]]*\],0x([0-9a-f]{6,8})\s*$")


def rtti_class(d, off, vt):
    """(name, size) of the Borland typeinfo at vt-0xc, or None."""
    o = off(vt - 0xc)
    if o is None:
        return None
    ti = struct.unpack_from("<I", d, o)[0]
    to = off(ti)
    if to is None or to + 8 > len(d):
        return None
    no = struct.unpack_from("<H", d, to + 6)[0]
    name = d[to + no:to + no + 64].split(b"\0")[0]
    if not re.fullmatch(rb"[A-Za-z_][\w<>,* ]{1,62}", name):
        return None
    return name.decode("latin1"), struct.unpack_from("<I", d, to)[0]


def is_typeinfo(d, off, a):
    """A Borland type descriptor at a, of any kind (class, struct, pointer): a printable C name at
    a + word[a+6]. A class's table can be followed by its pointer type's descriptor."""
    o = off(a)
    if o is None or o + 8 > len(d):
        return False
    no = struct.unpack_from("<H", d, o + 6)[0]
    if no not in (0x0c, 0x10, 0x20, 0x30):
        return False
    return re.match(rb"[A-Za-z_][\w<>,* ]{1,62}\0", d[o + no:o + no + 64]) is not None


def code_range(binary):
    """(lo, hi) of the code section; an X-32 image's file bytes, which hold its code and data."""
    img = retail_binaries.Image(binary)
    _name, va, vsize, _raw, raw_size = img.executable()[0]
    return va, va + (vsize if img.binary.container == retail_binaries.PE else raw_size)


def parse_vtables(binary):
    """{vt: [(offset, fn)]} from the dump."""
    out, cur = {}, None
    with open(os.path.join(ANALYSIS, f"{binary}_vtables_full.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            m = VT_HEAD.match(line.rstrip("\n"))
            if m:
                cur = out.setdefault(m.group(2), [])
            elif cur is not None and VT_SLOT.match(line.rstrip("\n")):
                s = VT_SLOT.match(line.rstrip("\n"))
                cur.append((int(s.group(1), 16), s.group(2)))
    return out


def disasm_stores(binary):
    """{stored dword immediate: [store sites]} from the disasm dump's MOV dword ptr [..],imm lines."""
    stores = {}
    with open(os.path.join(ANALYSIS, f"{binary}_disasm_full.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            if "MOV dword ptr [" in line:
                m = VT_STORE.match(line)
                if m:
                    stores.setdefault(m.group(2).zfill(8), []).append(m.group(1))
    return stores


def build_tables(binary):
    """({vt: [(offset, fn)]}, {vt: note}, RTTI vtable set): the dump's tables merged with the RTTI
    vtables read from the image, as cmd_vtables lists them."""
    borland(binary, "vtables")
    d, off = pe(binary)
    lo, hi = code_range(binary)
    import es2_classes
    img = es2_classes.image(binary)
    recs = es2_classes.scan(img)
    rtti_vts = {f"{v:08x}" for vts in es2_classes.find_vtables(img, recs).values() for v in vts}
    dump = parse_vtables(binary)
    tables, notes = {}, {}
    # A dump table 4 bytes past an RTTI vtable lost its slot 0 to an undefined function there.
    for vt, slots in dump.items():
        prev = f"{int(vt, 16) - 4:08x}"
        if vt not in rtti_vts and prev in rtti_vts:
            s0 = f"{struct.unpack_from('<I', d, off(int(prev, 16)))[0]:08x}"
            tables[prev] = [(0, s0)] + [(o + 4, fn) for o, fn in slots]
            notes[prev] = f"dump has it at {vt}, without slot 0"
        else:
            tables[vt] = slots
    # RTTI vtables the dump's sweep missed, or typed there shorter than the image holds: read the
    # words while they point into code, up to the next table's typeinfo word or a type descriptor.
    bounds = {int(v, 16) - 0xc for v in rtti_vts}
    for vt in sorted(rtti_vts):
        p, slots = int(vt, 16), []
        while len(slots) < 64:
            w = struct.unpack_from("<I", d, off(p))[0]
            if not lo <= w < hi or w in recs or is_typeinfo(d, off, w):
                break
            slots.append((p - int(vt, 16), f"{w:08x}"))
            p += 4
            if p in bounds:
                break
        if vt not in tables:
            tables[vt] = slots
            notes[vt] = "not in the dump; slots read from the image"
        elif len(slots) > len(tables[vt]) and slots[:len(tables[vt])] == tables[vt]:
            notes[vt] = f"dump types {len(tables[vt])} slots; the rest read from the image"
            tables[vt] = slots
    # A dump table without RTTI that starts inside a longer RTTI table is that class's tail.
    for vt in [v for v in tables if v not in rtti_vts]:
        a = int(vt, 16)
        for r in rtti_vts:
            if r in tables and int(r, 16) < a < int(r, 16) + 4 * len(tables[r]):
                notes[r] = notes.get(r, "") + f"; absorbs the dump's {vt}"
                del tables[vt]
                break
    return tables, notes, rtti_vts


def raw_vt_stores(binary, vts):
    """{vt: [address of a C7 /0 store whose imm32 is vt]}, scanned over the code section's bytes at
    every offset, so a store in bytes Ghidra never disassembled is found too. Matches MOV [reg],imm32
    and MOV [reg+disp8],imm32 (C7 00..07 / C7 40..47, ESP/EBP forms aside)."""
    d, off = pe(binary)
    lo, hi = code_range(binary)
    want = {struct.pack("<I", int(v, 16)): v for v in vts}
    out = {}
    o0 = off(lo)
    for i in range(o0, o0 + (hi - lo) - 6):
        if d[i] != 0xC7:
            continue
        m = d[i + 1]
        if m < 0x08 and m not in (4, 5):
            k = d[i + 2:i + 6]
        elif 0x40 <= m < 0x48 and m != 0x44:
            k = d[i + 3:i + 7]
        else:
            continue
        if k in want:
            out.setdefault(want[k], []).append(lo + i - o0)
    return out


def installers(binary, vt, stores, raw, ext, known):
    """'installed by' text: each store's containing Ghidra function, or the store's own address
    when no function extent holds it."""
    sites = sorted(set(stores.get(vt, [])) | {f"{a:08x}" for a in raw.get(vt, [])})
    out = []
    for s in sites:
        c = ext.containing(int(s, 16))
        if c:
            f = f"{c[0]:08x}"
            item = f"{f} {known.get(f, '*')}"
        else:
            item = f"{s} (store outside any Ghidra function"
            i = bisect.bisect_left(ext.starts, int(s, 16)) - 1
            if i >= 0:
                prev = f"{ext.starts[i]:08x}"
                item += f"; follows {known.get(prev, ext.fns[prev][0])} ({prev})"
            item += ")"
        if item not in out:
            out.append(item)
    return out


def cmd_vtables(args):
    binary = args[0]
    only_unnamed = "--unnamed" in args
    filters = [x for x in args[1:] if not x.startswith("-")]
    known = names(binary)
    d, off = pe(binary)
    low = {e["address"] for e in entries(binary) if not e.get("name")}
    stores = disasm_stores(binary)
    ext = Extents(binary)
    entries_ = set(ext.fns)
    tables, notes, _ = build_tables(binary)
    raw = raw_vt_stores(binary, tables)
    share = {}
    for slots in tables.values():
        for fn in {fn for _, fn in slots}:
            share[fn] = share.get(fn, 0) + 1
    unnamed = set()
    for vt in sorted(tables):
        slots = tables[vt]
        cls = rtti_class(d, off, int(vt, 16))
        cname = f"{cls[0]} size=0x{cls[1]:x}" if cls else "(no RTTI)"
        if filters and not any(f.lower() == vt or (cls and re.search(f, cls[0], re.I)) for f in filters):
            continue
        miss = [fn for _, fn in slots if fn not in known]
        unnamed.update(miss)
        if only_unnamed and not miss:
            continue
        print(f"=== {vt} {cname}  ({len(slots)} slots, {len(set(miss))} unnamed)"
              + (f"  [{notes[vt]}]" if vt in notes else ""))
        print("  installed by: " + (", ".join(installers(binary, vt, stores, raw, ext, known))
                                    or "<no immediate store>"))
        for o, fn in slots:
            mark = " " if fn in known else ("L" if fn in low else "*" if fn in entries_ else "?")
            print(f"  +0x{o:03x} {mark} {fn} {known.get(fn, '')}  x{share[fn]}")
    if not filters:
        print(f"\n{len(tables)} tables, {len(unnamed)} distinct unnamed slot targets"
              " ('*' unnamed function, 'L' a low entry without a name, '?' no function starts there)")


block = S.block


def by_binary(items):
    """{binary: [item]} for batch entries or edits, each carrying its own "binary"."""
    out = {}
    for x in items:
        assert x["binary"] in S.BINARIES, x
        out.setdefault(x["binary"], []).append(x)
    return out

# 'Name (Name (00012345))' and 'Name (Name)': a renamed FUN_/DAT_ mention landing inside a parenthesis
# that already gave the address or the name. Always wrong, so undouble() rewrites it.
DOUBLED = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*) \(\1(?: \(([0-9a-f]{8})\))?\)")
# 'Name, Name (00012345)': a doubling too, or two arguments that really are the same object
# (MonoScreen_Ctor(Mono_Screen, Mono_Screen (000b5e70))), so undouble() only reports it.
REPEATED = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*), \1 \([0-9a-f]{8}\)")
# '(Name, 00012345)' and "Name's (00012345)": a name and its address written other than 'Name (00012345)',
# the one form relink_bare and the doubling checks recognise. Not after an identifier, which makes the
# first an argument list.
SHAPES = (re.compile(r"(?<![A-Za-z0-9_)])\(([A-Za-z_][A-Za-z0-9_]*), ([0-9a-f]{8})\)"),
          re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*)'s \(([0-9a-f]{8})\)"))


def shape_warnings(description):
    """[(text, 'Name (addr)')] for each SHAPES match whose name is a symbol (an underscore or a capital)."""
    return [(m.group(0), f"{m.group(1)} ({m.group(2)})") for rx in SHAPES for m in rx.finditer(description)
            if "_" in m.group(1) or any(c.isupper() for c in m.group(1))]


def print_shape_warnings(items):
    """SHAPE lines for batch entries or edits whose description holds a SHAPES form; advisory."""
    for e in items:
        d = e.get("description", e.get("value") if e.get("field") == "description" else
                  (e.get("fields") or {}).get("description", ""))
        d = " ".join([d or ""] + [new for _, new in e.get("replace") or []])
        for text, want in shape_warnings(d):
            print(f"  SHAPE {e['binary']} {e['address']} {e.get('name', '')}: '{text}' -> write '{want}'")


def undouble(before, after, binary):
    """Rewrite the DOUBLED forms a rename or relink wrote into after's descriptions, and report the
    REPEATED forms it wrote, leaving any already in before alone. Returns the new text."""
    was = {json.loads(m.group(0))["address"]: json.loads(m.group(0)).get("description", "")
           for m in S.ENTRY_BLOCK.finditer(before)}
    out, pos = [], 0
    for m in S.ENTRY_BLOCK.finditer(after):
        e = json.loads(m.group(0))
        d, old = e.get("description", ""), was.get(e["address"], "")

        def fix(h):
            if h.group(0) in old:
                return h.group(0)
            to = h.group(1) + (" (%s)" % h.group(2) if h.group(2) else "")
            print(f"  undoubled {binary} {e['address']} {e.get('name', '')}: {h.group(0)} -> {to}")
            return to

        d2 = DOUBLED.sub(fix, d)
        for h in REPEATED.finditer(d2):
            if h.group(0) not in old:
                print(f"  CHECK {binary} {e['address']} {e.get('name', '')}: '{h.group(0)}' "
                      "is a doubling unless both arguments are that object")
        if d2 != d:
            e["description"] = d2
            out.append(after[pos:m.start()])
            out.append(block(e))
            pos = m.end()
    out.append(after[pos:])
    return "".join(out)


def cmd_insert(args):
    with open(args[0], encoding="utf-8") as f:
        new = json.load(f)
    print_shape_warnings(new)
    texts = {}
    for binary, batch in by_binary(new).items():
        text, nl = S.read_text(binary)
        old = json.loads(text)["entries"]
        existing = {e["address"].lower() for e in old}
        taken = {e.get("name") for e in old if e.get("name")}
        for e in batch:
            assert set(e) <= set(FIELDS), e
            for k in ("address", "binary", "type", "confidence", "description", "source"):
                assert k in e, (k, e)
            assert re.fullmatch(r"[0-9a-f]{8}", e["address"]), e["address"]
            assert e["type"] in ("function", "data")
            assert e["address"] not in existing, ("duplicate address", e["address"])
            c = e["confidence"]
            assert c in ("high", "medium", "low")
            if c == "low":
                assert "name" not in e, e["address"]
            else:
                n = e["name"]
                assert re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", n), n
                assert n.startswith("maybe_") == (c == "medium"), (n, c)
                assert n not in taken, ("duplicate name", n)
                taken.add(n)
            existing.add(e["address"])
            for v in e.values():
                assert not re.search(r"[\x00-\x1f]", v), ("control char", e["address"])
        text2 = undouble(text, relink(S.insert_blocks(text, batch), batch), binary)
        texts[binary] = (text2, nl, len(S.check(binary, text2)["entries"]))
        _, bare = relink_bare(text2, binary, {e["address"]: e["name"] for e in batch if e.get("name")}, quiet=True)
        if bare:
            print(f"{binary}: {bare} bare mention(s) of the batch's addresses in other entries; "
                  "relink BATCH --bare lists and rewrites them")
    fn = sum(1 for e in new if e.get("name") and e["type"] == "function")
    dn = sum(1 for e in new if e.get("name") and e["type"] == "data")
    totals = ", ".join(f"{b} {n}" for b, (_, _, n) in sorted(texts.items()))
    print(f"{len(new)} entries ok ({fn} named functions, {dn} named data); totals {totals}")
    if "--write" in args:
        for binary, (text2, nl, _) in texts.items():
            S.write_text(binary, text2, nl)
        print("written")


def rename_mentions(text, binary, address, old, new):
    """Replace old with new in the description and source of the other entries of binary's file,
    whose text this is. Names are per binary, so the other binary may use the same name for its own
    function; mentions in its file are listed for a manual check instead of being rewritten."""
    rx = re.compile(r"\b" + re.escape(old) + r"\b")
    for b in S.BINARIES:
        if b != binary:
            for e in entries(b):
                if any(rx.search(e.get(k, "")) for k in ("description", "source")):
                    print(f"  check {b} {e['address']} {e.get('name')}: mentions {old}")
    out, pos, n = [], 0, 0
    for m in S.ENTRY_BLOCK.finditer(text):
        e = json.loads(m.group(0))
        if e["address"] == address:
            continue
        hits = [k for k in ("description", "source") if rx.search(e.get(k, ""))]
        if not hits:
            continue
        for k in hits:
            n += len(rx.findall(e[k]))
            e[k] = rx.sub(new, e[k])
        out.append(text[pos:m.start()])
        out.append(block(e))
        pos = m.end()
    out.append(text[pos:])
    if n:
        print("renamed", old, "->", new, ":", n, "mentions in other", binary, "entries")
    return "".join(out)


def relink(text, new):
    """Rewrite FUN_/DAT_/PTR_<addr> in same-binary descriptions to each newly named entry's name."""
    for e in new:
        if e.get("name"):
            prefix = "FUN_" if e["type"] == "function" else "DAT_"
            text = rename_mentions(text, e["binary"], e["address"], prefix + e["address"], e["name"])
    return text


BINARY_TAG = re.compile(r"\b(?:DBSIM|VSHELL|ES1DBSIM|ES1GO|ES1ESCD)(?:'s)? $")


def relink_bare(text, binary, named, quiet=False):
    """Rewrite a bare address of one of `named` ({address: name}) in the descriptions of binary's other
    entries to 'Name (address)'. An address in parentheses after a name ('Name (00012345)': the
    address's own name, or a word with an underscore or a capital) or after a binary's name
    ('DBSIM 00467a98'), or inside a longer
    token, is left alone; one in a plain parenthesised list ('filter (00012345, 00012346)') is
    rewritten. Prints each rewrite in context unless quiet. A stamp is kept: the rewrite names what
    the address already denoted and changes no claim."""
    if not named:
        return text, 0
    rx = re.compile(r"(?<![0-9A-Za-z_+])(" + "|".join(named) + r")(?![0-9A-Za-z_])")
    out, pos, count = [], 0, 0
    for m in S.ENTRY_BLOCK.finditer(text):
        e = json.loads(m.group(0))
        d = e.get("description", "")
        new, last, changed = [], 0, False
        for h in rx.finditer(d):
            a, before = h.group(1), d[:h.start()]
            word = re.search(r"(\S+) \($", before)
            w = word.group(1).strip("`'\"") if word else ""
            if (a == e["address"] or BINARY_TAG.search(before)
                    or (w and ("_" in w or w[0].isupper() or w == named[a]))):
                continue
            new.append(d[last:h.start()] + f"{named[a]} ({a})")
            last = h.end()
            changed = True
            count += 1
            if not quiet:
                print(f"  {binary} {e['address']} {e.get('name', '')}: ...{d[max(0, h.start() - 50):h.start()]}"
                      f"[{a} -> {named[a]} ({a})]{d[h.end():h.end() + 30]}...")
        if changed:
            e["description"] = "".join(new) + d[last:]
            out.append(text[pos:m.start()])
            out.append(block(e))
            pos = m.end()
    out.append(text[pos:])
    return "".join(out), count


def cmd_relink(args):
    with open(args[0], encoding="utf-8") as f:
        batch = json.load(f)
    texts = {}
    for binary, part in by_binary(batch).items():
        before, nl = S.read_text(binary)
        text = relink(before, part)
        if "--bare" in args:
            current = names(binary)
            text, n = relink_bare(text, binary, {e["address"]: current[e["address"]] for e in part
                                                 if e["address"] in current})
            print(f"{binary}: {n} bare mention(s)")
        text = undouble(before, text, binary)
        S.check(binary, text)
        texts[binary] = (text, nl)
    if "--write" in args:
        for binary, (text, nl) in texts.items():
            S.write_text(binary, text, nl)
        print("written")


def cmd_edit(args):
    with open(args[0], encoding="utf-8") as f:
        edits = json.load(f)
    print_shape_warnings(edits)
    texts = {}
    for binary, part in by_binary(edits).items():
        text, nl = S.read_text(binary)
        texts[binary] = (undouble(text, edit_text(text, part), binary), nl)
        print("ok", binary, len(S.check(binary, texts[binary][0])["entries"]))
    if "--write" in args:
        for binary, (text, nl) in texts.items():
            S.write_text(binary, text, nl)
        print("written")


def edit_fields(ed, entry):
    """The fields edit `ed` sets on `entry`, the known_symbols entry as it stands: its "fields" or its one
    "field"/"value", then each [old, new] pair of its "replace" applied to the description in order, each
    old text asserted to occur exactly once in the description as it then stands."""
    fields = dict(ed.get("fields") or ({ed["field"]: ed["value"]} if "field" in ed else {}))
    if ed.get("replace"):
        d = fields.get("description", entry.get("description", ""))
        for old, new in ed["replace"]:
            n = d.count(old)
            assert n == 1, (ed["binary"], ed["address"], "replace text occurs %d times" % n, old)
            d = d.replace(old, new)
        fields["description"] = d
    assert fields, (ed["binary"], ed["address"], "the edit sets nothing")
    return fields


def edit_text(text, edits):
    for ed in edits:
        pat = re.compile(r'    \{\n      "address": "' + ed["address"] + r'",\n      "binary": "' + ed["binary"]
                         + r'",\n(?:      .*\n)*?    \}', re.M)
        ms = list(pat.finditer(text))
        assert len(ms) == 1, (ed["address"], len(ms))
        m = ms[0]
        e = json.loads(m.group(0))
        old = e.get("name")
        fields = edit_fields(ed, e)
        for k, v in fields.items():
            assert k in FIELDS and k not in ("address", "binary", "verified"), k
            assert not re.search(r"[\x00-\x1f]", v)
            # A stamp vouches for the description it was checked against; new text needs a new stamp.
            if k == "description" and v != e.get(k) and e.pop("verified", None):
                print("stamp dropped", ed["address"], "- re-stamp with es2_stamp.py once the new text is checked")
            e[k] = v
        if e.get("name"):
            assert e["name"].startswith("maybe_") == (e["confidence"] == "medium"), (e["name"], e["confidence"])
        else:
            assert e["confidence"] == "low", ed["address"]
        text = text[:m.start()] + block(e) + text[m.end():]
        if old and e.get("name") and old != e["name"]:
            text = rename_mentions(text, ed["binary"], ed["address"], old, e["name"])
        elif not old and e.get("name"):
            prefix = "FUN_" if e["type"] == "function" else "DAT_"
            text = rename_mentions(text, ed["binary"], ed["address"], prefix + ed["address"], e["name"])
    return text


def cmd_mentions(args):
    named = batch_names(args[0])
    if not named:
        return
    pat = re.compile(r"(?<![0-9A-Za-z])(?:FUN_|DAT_|PTR_|LAB_|_DAT_)?(" + "|".join(named) + r")(?![0-9A-Za-z])")
    for p, lines in repo_texts().items():
        for i, l in enumerate(lines):
            for m in pat.finditer(l):
                print(f"{os.path.relpath(p, REPO)}:{i + 1}: [{m.group(0)} -> {named[m.group(1)]}] "
                      f"...{l[max(0, m.start() - 60):m.end() + 40]}...")


def batch_names(path):
    """address -> current known_symbols name for a batch's named entries. A batch file can hold a
    name that was later changed, and an address named in both binaries is ambiguous in prose, so
    such addresses are left out and reported."""
    with open(path, encoding="utf-8") as f:
        b = json.load(f)
    current = {(e["binary"], e["address"]): e.get("name") for e in entries()}
    by_addr = {}
    for e in entries():
        if e.get("name"):
            by_addr.setdefault(e["address"], set()).add(e["binary"])
    named = {}
    for e in b:
        n = current.get((e["binary"], e["address"]))
        if not n:
            continue
        if len(by_addr.get(e["address"], ())) > 1:
            print(f"  skip {e['address']}: named in both binaries")
            continue
        named[e["address"]] = n
    return named


DOC_FAMILY = {"ES1DBSIM": "es1", "ES1GO": "es1", "ES1ESCD": "es1", "DBSIM": "retail", "VSHELL": "retail"}
ADDR_COLUMN = re.compile(r"(?i)^(?:address|es1|es2|dbsim|vshell|es1dbsim|es1go|es1escd)$")
LIST_ITEM = re.compile(r"^\s*(?:[-*+]|\d+[.)])\s")
TABLE_SEP = re.compile(r"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$")


def table_cells(line):
    """The start offset of each cell of a markdown table row, split at unescaped pipes."""
    starts = [0]
    for m in re.finditer(r"(?<!\\)\|", line):
        starts.append(m.end())
    return starts[1:] if line.lstrip().startswith("|") else starts


def fix_bare_doc(text, named, binaries, ambiguous):
    """Rewrite a bare `a` of one of `named` ({address: name}) in a markdown doc to `Name` (`a`): the
    first in each paragraph, list item or table row, unless the name already came before it there.
    Left alone: an address the doc names somewhere already (`Name` (`a`), `Name`(`a`) or `Name`,
    `a`), since the doc then introduces it and the bare mentions are deliberate, an address after
    another backticked name or another binary's name ('ES1GO `a`'), a location ('the run at `a`':
    'at `Name` (`a`)' misreads), either end of a range (`a`–`b`), and one inside a link, in a table column headed Address, ES1, ES2 or a binary, or in a
    code fence. `binaries` gives each address's binary; `ambiguous` holds the
    addresses another binary of the doc's family also has a function or entry at. Returns the text,
    the rewrites and the mentions to edit by hand, each as (line number, address, context)."""
    rx = re.compile(r"`(" + "|".join(named) + r")`")
    introduced = {a for a, n in named.items()
                  if re.search(r"`" + re.escape(n) + r"`(?: ?\(|, )`" + a + "`", text)}
    lines = text.splitlines(keepends=True)
    out, done, manual = [], [], []
    fence, cols, unit = None, None, ""
    for i, line in enumerate(lines):
        s = line.strip()
        if fence:
            if s.startswith(fence):
                fence, unit = None, ""
            out.append(line)
            continue
        if s.startswith(("```", "~~~")):
            fence, unit = s[:3], ""
            out.append(line)
            continue
        row = s.startswith("|")
        if not row:
            cols = None
        if not s or row or s.startswith("#") or LIST_ITEM.match(line):
            unit = ""
        if row and TABLE_SEP.match(s):
            out.append(line)
            continue
        if row and i + 1 < len(lines) and TABLE_SEP.match(lines[i + 1].strip()):
            starts = table_cells(line)
            cols = {k for k, c in enumerate(starts)
                    if ADDR_COLUMN.match(line[c:starts[k + 1] - 1 if k + 1 < len(starts) else len(line)]
                                         .strip().strip("*`").strip())}
        links = [(m.start(), m.end()) for m in re.finditer(r"\[[^\]]*\]\([^)]*\)", line)]
        starts = table_cells(line) if row else []
        new, last = [], 0
        for m in rx.finditer(line):
            a, before = m.group(1), line[:m.start()]
            name = named[a]
            if any(lo <= m.start() < hi for lo, hi in links):
                continue
            if row and cols and bisect.bisect_right(starts, m.start()) - 1 in cols:
                continue
            ctx = f"...{line[max(0, m.start() - 50):m.start()]}[{m.group(0)}]{line[m.end():m.end() + 30].rstrip()}..."
            prev = re.search(r"`([^`]+)` ?\($", before)
            if prev or re.search(r"`" + re.escape(name) + r"`, $", before):
                if prev and prev.group(1) != name:
                    manual.append((i + 1, a, f"after `{prev.group(1)}`, not `{name}`: {ctx}"))
                unit += f"`{name}`"
                continue
            if (a in introduced or re.search(r"(?:\bat |`[–-])$", before)
                    or re.match(r"[–-]`", line[m.end():])):
                continue
            tag = BINARY_TAG.search(before)
            if tag:
                manual.append((i + 1, a, f"after a binary's name: {ctx}"))
                continue
            if f"`{name}`" in unit + before:
                continue
            if a in ambiguous:
                manual.append((i + 1, a, f"another {DOC_FAMILY[binaries[a]]} binary has {a} too: {ctx}"))
                continue
            new.append(line[last:m.start()] + f"`{name}` (`{a}`)")
            last = m.end()
            unit += f"`{name}`"
            done.append((i + 1, a, ctx))
        out.append("".join(new) + line[last:])
        unit += line
    return "".join(out), done, manual


def cmd_fixrefs(args):
    """Rewrite the usual reference shapes to a batch's new names in docs (.md) and C# (.cs):
    `FUN_a` -> `Name` (`a`) in markdown, <c>FUN_a</c> -> <c>Name</c> (<c>a</c>) and a bare FUN_a
    -> Name (a) in C#, and a bare `a` -> `Name` (`a`) in the docs of the address's binary family
    (docs/es1/ for ES1DBSIM, ES1GO and ES1ESCD, docs/retail/ for DBSIM and VSHELL; fix_bare_doc says which).
    Anything else naming the address is listed for a manual edit."""
    named = batch_names(args[0])
    if not named:
        return
    with open(args[0], encoding="utf-8") as f:
        binaries = {e["address"]: e["binary"].upper() for e in json.load(f) if e["address"] in named}
    alt = "|".join(named)
    md = re.compile(r"`(?:FUN|DAT|PTR)_(" + alt + r")`( \(`\1`\))?")
    cs_c = re.compile(r"<c>(?:FUN|DAT|PTR)_(" + alt + r")</c>( \(<c>\1</c>\))?")
    cs_bare = re.compile(r"(?<![0-9A-Za-z_])(?:FUN|DAT|PTR)_(" + alt + r")(?![0-9A-Za-z])( \(\1\))?")
    left = re.compile(r"(?<![0-9A-Za-z])(?:FUN_|DAT_|PTR_)?(" + alt + r")(?![0-9A-Za-z])")
    family_named, ambiguous = {}, set()
    for b in sorted(set(binaries.values())):
        fam = DOC_FAMILY.get(b)
        if not fam:
            continue
        family_named.setdefault(fam, {}).update({a: named[a] for a in named if binaries[a] == b})
        for other in (o for o, f in DOC_FAMILY.items() if f == fam and o != b):
            taken = set(functions(other)) | {e["address"].lower() for e in entries(other)}
            ambiguous |= {a for a in named if binaries[a] == b and a in taken}
    changed = rewrites = 0
    for p, _ in repo_texts().items():
        with open(p, "rb") as fh:
            raw = fh.read()
        bom = raw[:3] == b"\xef\xbb\xbf"
        t = (raw[3:] if bom else raw).decode("utf-8")
        rel = os.path.relpath(p, REPO)
        fam = next((f for f in family_named
                    if rel.replace("\\", "/").startswith(f"Herculan/docs/{f}/")), None) if p.endswith(".md") else None
        notes = []
        if p.endswith(".md"):
            t2 = md.sub(lambda m: f"`{named[m.group(1)]}` (`{m.group(1)}`)", t)
            if fam:
                t2, done, manual = fix_bare_doc(t2, family_named[fam], binaries, ambiguous)
                rewrites += len(done)
                notes = [f"  bare {rel}:{n}: [{a} -> {named[a]}] {ctx}" for n, a, ctx in done]
                notes += [f"  manual {rel}:{n}: [{a} -> {named[a]}] {why}" for n, a, why in manual]
            # Inside parentheses already: "(`Name` (`a`))" reads better as "(`Name`, `a`)".
            t2 = re.sub(r"\(`(\w+)` \(`(" + alt + r")`\)\)",
                        lambda m: f"(`{m.group(1)}`, `{m.group(2)}`)" if named[m.group(2)] == m.group(1) else m.group(0), t2)
        else:
            t2 = cs_c.sub(lambda m: f"<c>{named[m.group(1)]}</c> (<c>{m.group(1)}</c>)", t)
            t2 = cs_bare.sub(lambda m: f"{named[m.group(1)]} ({m.group(1)})", t2)
        if t2 != t:
            changed += 1
            print("fix", rel)
        for n in notes:
            print(n)
        if t2 != t and "--write" in args:
            with open(p, "wb") as fh:
                fh.write((b"\xef\xbb\xbf" if bom else b"") + t2.encode("utf-8"))
        for i, l in enumerate(t2.splitlines()):
            for m in left.finditer(l):
                if fam and m.group(1) in family_named[fam] and l[m.start() - 1:m.start()] == l[m.end():m.end() + 1] == "`":
                    continue      # fix_bare_doc's to rewrite, leave or list
                if p.endswith(".md") and re.search(r"\]\([^)\s]*$", l[:m.start()]):
                    continue      # a link target: the heading anchor of a named section
                if not re.search(re.escape(named[m.group(1)]), l):
                    print(f"  manual {os.path.relpath(p, REPO)}:{i + 1}: [{m.group(0)} -> {named[m.group(1)]}] "
                          f"...{l[max(0, m.start() - 60):m.end() + 40]}...")
    print(f"{rewrites} bare doc address(es) named;", changed, "files", "written" if "--write" in args else "(dry run)")


def cmd_repl(args):
    with open(args[0], encoding="utf-8") as f:
        spec = json.load(f)
    files = {}
    for rel, old, new, cnt in spec:
        p = os.path.join(REPO, rel)
        if p not in files:
            with open(p, "rb") as fh:
                raw = fh.read()
            bom = raw[:3] == b"\xef\xbb\xbf"
            files[p] = [bom, (raw[3:] if bom else raw).decode("utf-8")]
        t = files[p][1]
        assert t.count(old) == cnt, (rel, old, t.count(old))
        files[p][1] = t.replace(old, new)
    print("ok", len(spec), "replacements in", len(files), "files")
    if "--write" in args:
        for p, (bom, t) in files.items():
            with open(p, "wb") as fh:
                fh.write((b"\xef\xbb\xbf" if bom else b"") + t.encode("utf-8"))
        print("written")


def define_targets(binary, args):
    """Addresses for apply --define: the +-list after the flag, or else every named function entry
    of the binary's known_symbols file with no Ghidra function at its address in the function list.
    An address inside another function's extent is left out and reported, since creating a function
    there would cut into that body; a late entry is repaired with fixentry instead."""
    i = args.index("--define")
    if i + 1 < len(args) and re.fullmatch(r"[0-9a-fA-F]{8}([+,][0-9a-fA-F]{8})*", args[i + 1]):
        return [a.lower() for a in re.split(r"[+,]", args[i + 1])]
    ext = Extents(binary)
    known = names(binary)
    out = []
    for e in entries(binary):
        a = e["address"].lower()
        if e["type"] != "function" or not e.get("name") or a in ext.fns:
            continue
        c = ext.containing(int(a, 16))
        if c:
            f = f"{c[0]:08x}"
            print(f"  not defining {a} {e['name']}: inside {known.get(f, ext.fns[f][0])} ({f}+0x{c[1]:x})")
            continue
        out.append(a)
    return out


def cmd_apply(args):
    binary = args[0]
    b = retail_binaries.get(binary)
    cmd = [retail_binaries.GHIDRA, retail_binaries.GHIDRA_PROJECTS, b.project, "-process", b.program,
           "-noanalysis"] + ([] if "--write" in args else ["-readOnly"]) + [
        "-scriptPath", retail_binaries.GHIDRA_SCRIPTS]
    if "--define" in args:
        targets = define_targets(binary, args)
        print(f"defining {len(targets)} function(s) first: {' '.join(targets)}")
        if targets:
            cmd += ["-postScript", "ES2DefineFunctionAt"] + targets
    cmd += ["-postScript", "ES2ApplySymbolNames", S.path(binary)]
    r = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
    keep = re.compile(r"renamed|labeled|skipped|error|warn|exception|fail|summary|created |exists ", re.I)
    for l in (r.stdout + r.stderr).splitlines():
        if keep.search(l) and "bundle event for non-GhidraBundle" not in l:
            print(l.strip()[:300])
    print("exit", r.returncode, "(read-only)" if "--write" not in args else "(written)")


def cmd_fixentry(args):
    binary = args[0]
    addrs = [a.lower() for a in re.split(r"[+,]", args[1]) if a]
    assert all(re.fullmatch(r"[0-9a-f]{8}", a) for a in addrs), addrs
    write = "--write" in args
    scripts = retail_binaries.GHIDRA_SCRIPTS
    b = retail_binaries.get(binary)
    tmp = tempfile.mkdtemp(prefix="es2_fixentry_")
    try:
        cmd = [retail_binaries.GHIDRA, retail_binaries.GHIDRA_PROJECTS, b.project, "-process", b.program,
               "-noanalysis"] + ([] if write else ["-readOnly"]) + ["-scriptPath", scripts]
        for a in addrs:
            cmd += ["-postScript", "ES2MergeFunctionAt", a, "auto", os.path.join(tmp, f"merge_{a}.txt")]
        cmd += ["-postScript", "ES2ApplyStructures", os.path.join(scripts, "known_structs.json"),
                "-postScript", "ES2ApplySymbolNames", S.path(binary),
                "-postScript", "ES2CheckFunctionEntries", os.path.join(tmp, "check.txt")] + addrs
        r = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
        keep = re.compile(r"ES2Apply\w+\.java>.*(errors|summary|=)|ERROR|exception|fail", re.I)
        for l in (r.stdout + r.stderr).splitlines():
            if keep.search(l) and "bundle event for non-GhidraBundle" not in l:
                print(l.strip()[:300])
        bad = False
        for a in addrs:
            p = os.path.join(tmp, f"merge_{a}.txt")
            text = open(p, encoding="utf-8").read() if os.path.exists(p) else "FAILED: no merge output\n"
            bad |= bool(re.search(r"^(FAILED|SHORT)", text, re.M))
            print(f"== {a}\n{text.rstrip()}")
        p = os.path.join(tmp, "check.txt")
        print("== check\n" + (open(p, encoding="utf-8").read().rstrip() if os.path.exists(p) else "(no check output)"))
        print("exit", r.returncode, "(written)" if write else "(read-only)", "-- PROBLEMS ABOVE" if bad else "")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


INSN = re.compile(r"^([0-9a-f]{8})   [0-9a-f]+\s+(\S+)(?: (.*?))?\s*(?:;.*)?$")


PADDED = re.compile(r"0x([0-9a-f]{8})\b")
UNPADDED = re.compile(r"0x([0-9a-f]{1,7})\b")
ABS_MEM = re.compile(r"\[0x([0-9a-f]{8})\]")
BARE = re.compile(r"[0-9a-f]{8}")
# Functions start from address 0 in an X-32 image, so a small constant can equal one's entry; below
# this an unpadded immediate is never read as a function pointer.
X32_POINTER_FLOOR = 0x1000


def x32_data_start(src, mem_size):
    """The lowest address any instruction in an X-32 disasm dump reads or writes as an absolute memory
    operand: where the image's data begins, below which an unpadded immediate is taken for a constant."""
    lo = mem_size
    with open(src, encoding="utf-8", errors="replace") as f:
        for line in f:
            for m in ABS_MEM.finditer(line):
                v = int(m.group(1), 16)
                if v < lo:
                    lo = v
    return lo


def x32_function_of(image, v):
    """An X-32 address on the 0x90 padding before a prologue, as the prologue's address; any other
    address unchanged (retail_binaries.Image.entries_of)."""
    entries = image.entries_of(v)
    prologue = [a for a in entries if image.read(a, 3) == b"\x55\x8b\xec"]
    return prologue[0] if prologue else v


def insn_streams(binary):
    """{function address: [(insn address, normalised insn, absolute operand values)]} from the disasm
    dump. Every operand value that is an address in the image becomes 'A' and is kept, as 8 hex digits,
    in the third field, so a function compiled into two images compares equal whatever its callees,
    globals and jump targets were relocated to.

    In a PE image an address is any value in 0x400000..0x7fffff. An X-32 image starts at 0, where
    addresses and small constants share a range. There an address is:
      - what Ghidra prints padded to 8 digits (branch and call targets, absolute memory operands) or
        bare (a jump-table entry);
      - an unpadded immediate on a function's entry or the padding before it (a function pointer),
        or inside the function's own extent (a jump-table base, a handler address it pushes), at or
        above X32_POINTER_FLOOR;
      - an unpadded immediate between the start of the data (x32_data_start) and the end of the
        image (a pointer to a global or a vtable), unless it is a power of two or one less than one,
        which are taken for constants as es1_crossmatch.py takes them.
    Any other value stays a literal, which can only stop two twins from matching. A constant that
    falls in the data range and is masked could let two functions that differ only in it compare
    equal. A value on the padding before a prologue is recorded as the prologue (x32_function_of)."""
    b = retail_binaries.get(binary)
    src = b.dump("disasm_full.txt")
    cache = os.path.join(CACHE, f"{binary}_insns.pkl")
    os.makedirs(CACHE, exist_ok=True)
    if os.path.exists(cache) and os.path.getmtime(cache) > max(os.path.getmtime(src), os.path.getmtime(__file__)):
        with open(cache, "rb") as f:
            return pickle.load(f)
    if b.container == retail_binaries.PE:
        absv = re.compile(r"0x0*([4-7][0-9a-f]{5})\b")

        def mask(ops, _lo, _hi):
            return absv.sub("A", ops), [v.zfill(8) for v in absv.findall(ops)]
    else:
        image = b.image()
        mem_size = image.sections[0][2]
        data_start = x32_data_start(src, mem_size)
        fns = functions(binary)
        entries = {e for f in fns for e in image.entries_of(int(f, 16))}

        def mask(ops, lo, hi):
            vals = []
            # A jump-table entry: `addr  0005bd5c`, the address bare.
            if BARE.fullmatch(ops.strip()):
                v = int(ops, 16)
                if v < mem_size:
                    vals.append("%08x" % x32_function_of(image, v))
                    return "A", vals
                return ops, vals

            def padded(m):
                v = int(m.group(1), 16)
                if v >= mem_size:
                    return m.group(0)
                vals.append("%08x" % x32_function_of(image, v))
                return "A"

            def unpadded(m):
                v = int(m.group(1), 16)
                if v >= X32_POINTER_FLOOR and (v in entries or lo <= v <= hi):
                    vals.append("%08x" % x32_function_of(image, v))
                    return "A"
                if not data_start <= v < mem_size or v & (v - 1) == 0 or v & (v + 1) == 0:
                    return m.group(0)
                vals.append("%08x" % v)
                return "A"
            return UNPADDED.sub(unpadded, PADDED.sub(padded, ops)), vals
    d, cur, lo, hi = {}, None, 0, 0
    with open(src, encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("; FUNCTION "):
                a = line.rstrip().rsplit("@ ", 1)[1]
                cur = d.setdefault(a, [])
                lo = int(a, 16)
                hi = fns[a][2] if b.container != retail_binaries.PE and a in fns else lo
                continue
            m = INSN.match(line)
            # '??' lines are undefined bytes the dump lists inside a function's listing (after its
            # last RET, say), not instructions.
            if cur is None or not m or m.group(2) == "??":
                continue
            masked, vals = mask(m.group(3) or "", lo, hi)
            cur.append((m.group(1), m.group(2) + " " + masked, vals))
    with open(cache, "wb") as f:
        pickle.dump(d, f)
    return d


XTYPES = re.compile(r"\b(?:undefined\d*|u?int\d*|uint|u?short|u?char|byte|word|dword|u?long|bool|void|float"
                    r"|double|code|u?longlong|unsigned|signed)\b")
XTOKEN = re.compile(r"[A-Za-z_][\w?]*|0x[0-9a-f]+|\d+|==|!=|<=|>=|<<|>>|&&|\|\||->|[-+*/%&|^~!<>=(){}\[\];,.?:]")
# (token count below which, bucket, score threshold): a short body matches on its shape alone, so
# it needs a higher score to count.
XBUCKETS = ((50, "S", 0.9), (200, "M", 0.8), (None, "L", 0.75))
XSHINGLE = 4


def xtokens(binary):
    """_xtokens, read once per process (diff --pairs asks for it once per pair)."""
    if binary not in _XTOKENS:
        _XTOKENS[binary] = _xtokens(binary)
    return _XTOKENS[binary]


def _xtokens(binary):
    """{function: tokens} of every decompiled body for the cross-compiler match: normalised, with
    the binary's own names substituted, the signature line dropped, types and casts reduced to T,
    locals and register artefacts (extraout_EAX, unaff_ESI) to v, and parameters to p, since the two
    compilers' calling conventions number them differently (Ghidra gives some Borland __fastcall
    functions junk register parameters ahead of the real ones). xmask then reduces the names to the
    ones both games use."""
    src = os.path.join(ANALYSIS, f"{binary}_decomp_full.c")
    cache = os.path.join(CACHE, f"{binary}_xtokens.pkl")
    if os.path.exists(cache) and os.path.getmtime(cache) > max(os.path.getmtime(p) for p in (src, S.path(binary), __file__)):
        with open(cache, "rb") as f:
            return pickle.load(f)
    known = names(binary)
    out = {}
    for a in bodies(binary):
        t = normalised(binary, a, known).partition("\n")[2]
        t = XTYPES.sub("T", t)
        t = re.sub(r"\(T *\**\)", "", t)
        t = re.sub(r"\bT\b\s*\*+", "T", t)
        t = re.sub(r"\b(?:local_\?|var\?|extraout_\w+|in_\w+|unaff_\w+)", "v", t)
        t = re.sub(r"\bparam_\d+", "p", t)
        out[a] = XTOKEN.findall(t)
    os.makedirs(CACHE, exist_ok=True)
    with open(cache, "wb") as f:
        pickle.dump(out, f)
    return out


def xmask(binary, shared):
    """xtokens with every name of the binary that is not in `shared` (the names both games use)
    turned back into FUN_? or DAT_?, as an unnamed label reads: a name one game alone has carries no
    evidence about the other."""
    generic = {e["name"]: ("FUN_?" if e["type"] == "function" else "DAT_?")
               for e in entries(binary) if e.get("name") and e["name"] not in shared}
    return {a: [generic.get(t, t) for t in ts] for a, ts in xtokens(binary).items()}


def xscore(t1, t2, shared):
    """(score, sequence ratio, bag ratio, names agreeing, names in either) for two token lists. The
    sequence ratio is difflib's over the token order; the bag ratio is the same 2M/T over the token
    multisets, which a reordered or restructured body (an early return against an else) keeps. Their
    mean is scaled by the agreement of the shared callee and global names the two bodies use, from
    0.5 when none agree to 1 when all do; with no shared name in either body it is left as it is."""
    import difflib
    seq = difflib.SequenceMatcher(None, t1, t2, autojunk=False).ratio()
    c1, c2 = collections.Counter(t1), collections.Counter(t2)
    bag = 2 * sum((c1 & c2).values()) / (len(t1) + len(t2))
    n1 = collections.Counter(x for x in t1 if x in shared)
    n2 = collections.Counter(x for x in t2 if x in shared)
    tot = sum(n1.values()) + sum(n2.values())
    score = (seq + bag) / 2
    if tot:
        score *= 0.5 + 0.5 * (2 * sum((n1 & n2).values()) / tot)
    return score, seq, bag, len(set(n1) & set(n2)), len(set(n1) | set(n2))


def xbucket(n):
    for limit, label, threshold in XBUCKETS:
        if limit is None or n < limit:
            return label, threshold


def shared_names(b1, targets):
    one = set(names(b1).values())
    return one & set().union(*(set(names(t).values()) for t in targets))


def cmd_xmatch(args):
    """The cross-compiler form of match: BIN (an ES1 image) against one or more ES2 binaries, on
    normalised decompiles rather than masked disassembly, which Symantec's and Borland's code
    generation never share. Candidates come from an index of 4-token shingles over the named target
    functions (shingles in 300 or more functions are skipped as boilerplate); the 40 sharing the most
    are scored with xscore. One line per BIN function whose best score reaches its size bucket's
    threshold (XBUCKETS), or --fuzzy R for every bucket, best first, with the runner-up under another
    name, the same name's hits in the other targets, and the diff command that confirms or rejects it."""
    b1, targets = args[0], args[1].upper().split("+")
    retail_binaries.require(b1, "es2_naming.py match (cross-compiler)", "the cross-compiler match reads "
                            "an ES1 image against ES2's", compiler=retail_binaries.SYMANTEC)
    for t in targets:
        retail_binaries.require(t, "es2_naming.py match (cross-compiler)", "the targets are ES2's binaries",
                                compiler=retail_binaries.BORLAND)
    mn = int(args[args.index("--min") + 1]) if "--min" in args else 20
    fz = float(args[args.index("--fuzzy") + 1]) if "--fuzzy" in args else None
    lo, hi = 0, 1 << 32
    if "--range" in args:
        r = args[args.index("--range") + 1].split("-")
        lo, hi = int(r[0], 16), int(r[1], 16) if len(r) > 1 and r[1] else 1 << 32
    only = set(addrs(args[args.index("--only") + 1].split("+"))) if "--only" in args else None
    from es1_crossmatch import LIBMATCH_SOURCE
    e1 = {e["address"]: e for e in entries(b1)}
    img = retail_binaries.Image(b1)
    k1 = names(b1)
    shared = shared_names(b1, targets)
    t1s = xmask(b1, shared)
    t2s, k2 = {}, {}
    for t in targets:
        k2[t] = names(t)
        for a, ts in xmask(t, shared).items():
            if a in k2[t] and len(ts) >= mn:
                t2s[(t, a)] = ts
    index = collections.defaultdict(list)
    sh2 = {}
    for key, ts in t2s.items():
        sh2[key] = {hash(tuple(ts[i:i + XSHINGLE])) for i in range(len(ts) - XSHINGLE + 1)}
        for s in sh2[key]:
            index[s].append(key)
    rows, compared = [], collections.Counter()
    for a in sorted(t1s):
        if only is not None and a not in only:
            continue
        if only is None and (not lo <= int(a, 16) < hi or ("--all" not in args and a in k1)
                             or e1.get(a, {}).get("source") == LIBMATCH_SOURCE):
            continue
        ts = t1s[a]
        # A Ghidra function on the 0x90 padding before a prologue decompiles as the prologue's.
        if len(ts) < mn or (img.read(int(a, 16), 1) == b"\x90" and img.entries_of(int(a, 16))[-1] != int(a, 16)):
            continue
        label, threshold = xbucket(len(ts))
        compared[label] += 1
        sh = {hash(tuple(ts[i:i + XSHINGLE])) for i in range(len(ts) - XSHINGLE + 1)}
        cnt = collections.Counter()
        for s in sh:
            p = index.get(s)
            if p and len(p) < 300:
                cnt.update(p)
        best = {}
        for key, _c in cnt.most_common(40):
            if not 1 / 3 <= len(ts) / len(t2s[key]) <= 3:
                continue
            sc = xscore(ts, t2s[key], shared)
            nm = k2[key[0]][key[1]]
            best.setdefault(nm, []).append((sc, key))
        if not best:
            continue
        ranked = sorted(((max(h)[0], nm, sorted(h, reverse=True)) for nm, h in best.items()),
                        key=lambda x: -x[0][0])
        top = ranked[0]
        passes = top[0][0] >= (fz if fz is not None else threshold)
        if passes or only is not None:
            rows.append((a, len(ts), label, top, ranked[1] if len(ranked) > 1 else None, passes))
    rows.sort(key=lambda r: -r[3][0][0])
    for a, n, label, (sc, nm, hits), runner, passes in rows:
        score, seq, bag, agree, total = sc
        t, x = hits[0][1]
        mark = " " if a in k1 else "L" if a in e1 else "*"
        also = [f"{k[0]} {k[1]} {s[0]:.2f}" for s, k in hits[1:]]
        line = (f"{a} {mark} {label} {n:>5}  {score:.2f}{' ' if passes else '-'} {t} {x} {nm}  (seq {seq:.2f} bag {bag:.2f}"
                + (f" names {agree}/{total}" if total else "") + ")")
        if also:
            line += "  also " + ", ".join(also)
        if runner:
            line += f"  next {runner[0][0]:.2f} {runner[1]}" + ("  TIE" if score - runner[0][0] < 0.02 else "")
        print(line)
        if "--diff" not in args:
            print(f"    py tools/scripts/es2_naming.py diff {b1} {a} {t} {x}")
            continue
        # --diff: the diff itself under the line, and the runner-up's too when it ties.
        pairs = [(t, x)] + ([runner[2][0][1]] if runner and score - runner[0][0] < 0.02 else [])
        for pt, px in pairs:
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                cmd_diff([b1, a, pt, px])
            print(f"    --- diff {b1} {a} {pt} {px} ({k2[pt].get(px, px)})")
            for dl in buf.getvalue().splitlines():
                print("    " + dl)
    above = collections.Counter(r[2] for r in rows if r[5])
    ties = sum(1 for r in rows if r[5] and r[4] and r[3][0][0] - r[4][0][0] < 0.02)
    print(f"\n{b1} against {'+'.join(targets)}: {len(shared)} names shared; "
          + ", ".join(f"{lab} ({'<' + str(lim) if lim else '>=' + str(XBUCKETS[-2][0])} tokens, "
                      f"threshold {fz if fz is not None else th}) {above[lab]} of {compared[lab]}"
                      for lim, lab, th in XBUCKETS)
          + f"; {sum(above.values())} candidates, {ties} within 0.02 of a runner-up under another name")


def cmd_match(args):
    """match BIN BIN2 [--range lo-hi] [--only a+b...] [--min N] [--fuzzy R] [--all]: BIN's unnamed
    (with --all, every) function, in the range or the list, whose normalised
    instruction stream equals (or, with --fuzzy, is at least R similar to) a named BIN2 function's,
    with the BIN2 name and the callee/global names that pair up position by position. An ES1 BIN
    against ES2 targets goes to cmd_xmatch."""
    import difflib
    b1, b2 = args[0], args[1]
    if retail_binaries.get(b1).compiler != retail_binaries.get(b2.split("+")[0]).compiler:
        return cmd_xmatch(args)
    if retail_binaries.get(b1).container != retail_binaries.get(b2).container:
        raise SystemExit("es2_naming.py match pairs two images of one container format: a PE's and an "
                         "X-32 image's address operands are masked by different rules (insn_streams)")
    mn = int(args[args.index("--min") + 1]) if "--min" in args else 6
    fz = float(args[args.index("--fuzzy") + 1]) if "--fuzzy" in args else None
    k1, k2 = names(b1), names(b2)
    s1, s2 = insn_streams(b1), insn_streams(b2)
    by_key = {}
    for a, ins in s2.items():
        by_key.setdefault(tuple(i[1] for i in ins), []).append(a)
    lo, hi = 0, 1 << 32
    if "--range" in args:
        r = args[args.index("--range") + 1].split("-")
        lo, hi = int(r[0], 16), int(r[1], 16) if len(r) > 1 and r[1] else 1 << 32
    only = set(addrs(args[args.index("--only") + 1].split("+"))) if "--only" in args else None
    todo = [a for a in sorted(s1) if ("--all" in args or a not in k1) and len(s1[a]) >= mn
            and (a in only if only is not None else lo <= int(a, 16) < hi)]
    for a in todo:
        key = tuple(i[1] for i in s1[a])
        hits = [(x, 1.0) for x in by_key.get(key, [])]
        if not hits and fz:
            best = []
            for x, ins in s2.items():
                if x not in k2 or abs(len(ins) - len(key)) > len(key) * (1 - fz) + 1:
                    continue
                sm = difflib.SequenceMatcher(None, key, [i[1] for i in ins], autojunk=False)
                if sm.real_quick_ratio() >= fz and sm.quick_ratio() >= fz and sm.ratio() >= fz:
                    best.append((x, sm.ratio()))
            hits = sorted(best, key=lambda h: -h[1])[:3]
        if not hits:
            continue
        named = [h for h in hits if h[0] in k2]
        tag = "EXACT" if hits[0][1] == 1.0 else f"{hits[0][1]:.2f}"
        print(f"{a} {k1.get(a, '')} ({len(key)} insns) {tag}: "
              + ", ".join(f"{k2.get(x, 'FUN_' + x)} ({x}{'' if r == 1.0 else f' {r:.2f}'})" for x, r in hits[:4])
              + (f" +{len(hits) - 4}" if len(hits) > 4 else ""))
        if len(named) == 1 and named[0][1] == 1.0:
            x = named[0][0]
            pairs, clash, lo, hi = {}, set(), s1[a][0][0], s1[a][-1][0]
            for (_, _, v1), (_, _, v2) in zip(s1[a], s2[x]):
                for p, q in zip(v1, v2):
                    if lo <= p <= hi:
                        continue
                    if p not in k1 and q in k2:
                        pairs[p] = q
                    elif p in k1 and q in k2 and k1[p] != k2[q]:
                        clash.add(f"{k1[p]} vs {k2[q]}")
            for p, q in sorted(pairs.items()):
                print(f"    {p} <- {k2[q]} ({q})")
            for c in sorted(clash):
                print(f"    differs: {c}")


def class_records(binary):
    """(Image, {record VA: record}, {record VA: [primary vtables]}) from es2_classes."""
    import es2_classes
    img = es2_classes.image(binary)
    recs = es2_classes.scan(img)
    return img, recs, es2_classes.find_vtables(img, recs)


def fn_mark(a, known, low, fns):
    """' ' named, 'L' low entry without a name, '*' unnamed Ghidra function, '?' no function there."""
    return " " if a in known else "L" if a in low else "*" if a in fns else "?"


def cmd_classes(args):
    """classes BIN [regex...] [--unnamed]: every class record, one per line, with its destructor and
    operator delete marked as cmd_vtables marks slots."""
    binary = args[0]
    only_unnamed = "--unnamed" in args
    filters = [re.compile(x, re.I) for x in args[1:] if not x.startswith("-")]
    known = names(binary)
    low = {e["address"] for e in entries(binary) if not e.get("name")}
    fns = functions(binary)
    _, recs, vts = class_records(binary)
    layouts, _ = struct_layouts(binary)
    shown = 0
    for va in sorted(recs, key=lambda v: (recs[v]["name"].lower(), v)):
        r = recs[va]
        if filters and not any(f.search(r["name"]) for f in filters):
            continue
        slots = []
        for label, a in (("dtor", r["dtor"]), ("opdel", r["opdel"])):
            if a:
                h = f"{a:08x}"
                slots.append((label, h, fn_mark(h, known, low, fns)))
        if only_unnamed and not any(m != " " for _, _, m in slots):
            continue
        bases = ",".join((recs[b]["name"] if b in recs else f"{b:08x}") + (f"@+0x{o:x}" if o else "")
                         for b, o in r["bases"]) or "-"
        vt = ",".join(f"{v:08x}" for v in vts.get(va, [])) or "-"
        line = (f"{r['name']:<28} rec={va:08x} size=0x{r['size']:<5x} "
                f"vptr={'+0x%x' % r['vptr'] if r['vptr'] >= 0 else '-':<6} base={bases} vt={vt}")
        for label, h, m in slots:
            line += f" {label}={h}{m}{known.get(h, '')}"
        if r["name"] in layouts:
            line += " [layout]"
        print(line)
        shown += 1
    print(f"\n{shown} of {len(recs)} class records ('*' unnamed function, 'L' a low entry without a name, "
          "'?' no function starts there; [layout] has a known_structs.json struct of the class)")


def struct_layouts(binary):
    with open(os.path.join(REPO, "tools", "ghidra_scripts", "known_structs.json"), encoding="utf-8-sig") as f:
        ks = json.load(f)
    return {s.get("class", s["name"]) for s in ks["structs"] if s["binary"] == binary}, ks


def cmd_stats(args):
    """stats BIN [--list]: the coverage table in Herculan/docs/herculan/plan-completing-coverage.md.
    --list also prints the prologues outside any function and the non-padding gaps between them."""
    binary = args[0]
    borland(binary, "stats")
    ext = Extents(binary)
    fns = ext.fns
    d, off = pe(binary)
    lo, hi = code_range(binary)
    unnamed = {a for a, (n, _, _) in fns.items() if unnamed_label(n)}
    total_b = sum(s for _, s, _ in fns.values())
    un_b = sum(fns[a][1] for a in unnamed)
    o0 = off(lo)
    code = d[o0:o0 + (hi - lo)]
    prologues = [lo + m.start() for m in re.finditer(rb"\x55\x8b\xec", code)
                 if not ext.containing(lo + m.start())]
    # Gaps: code-section bytes outside every function extent, minus runs of fill bytes.
    gaps, cur = [], lo
    for e in ext.starts + [hi]:
        if e > cur and e <= hi:
            blob = d[off(cur):off(cur) + (e - cur)]
            if blob.strip(b"\x00\x90\xcc"):
                gaps.append((cur, e))
        if e < hi:
            cur = max(cur, fns[f"{e:08x}"][2])
    known = names(binary)
    tables, _, rtti_vts = build_tables(binary)
    slots = sum(len(s) for s in tables.values())
    un_slots = {fn for s in tables.values() for _, fn in s if fn not in known}
    typed = total_dump = 0
    with open(os.path.join(ANALYSIS, f"{binary}_vtables_full.txt"), encoding="utf-8", errors="replace") as f:
        section = 0
        for line in f:
            if line.startswith("SECTION "):
                section = int(line.split()[1])
            elif VT_HEAD.match(line.rstrip("\n")):
                total_dump += 1
                typed += section == 1
    _, recs, vts = class_records(binary)
    layout_names, ks = struct_layouts(binary)
    rec_names = {r["name"] for r in recs.values()}
    apps = sum(1 for a in ks["applications"] if a["binary"] == binary)
    with open(os.path.join(ANALYSIS, f"{binary}_decomp_full.c"), encoding="utf-8", errors="replace") as f:
        dats = set(re.findall(r"\bDAT_([0-9a-f]{8})\b", f.read()))
    named_data = sum(1 for e in entries(binary) if e["type"] == "data" and e.get("name"))
    rows = [
        ("Ghidra functions with a real name", f"{len(fns) - len(unnamed)} / {len(fns)} "
         f"({100 * (len(fns) - len(unnamed)) / len(fns):.1f}%)"),
        ("Code bytes inside unnamed functions", f"{100 * un_b / total_b:.1f}% ({un_b / 1024:.0f} KB)"),
        ("Unnamed functions under 100 bytes", f"{sum(1 for a in unnamed if fns[a][1] < 100)} of {len(unnamed)}"),
        ("`55 8B EC` prologues outside any Ghidra function", f"{len(prologues)}"),
        ("Code-section gaps outside any function, not all fill bytes", f"{len(gaps)} ({sum(e - s for s, e in gaps)} bytes)"),
        ("Vtables (with RTTI) / slots / unnamed slot targets",
         f"{len(tables)} ({len(set(tables) & rtti_vts)}) / {slots} / {len(un_slots)}"),
        ("Vtables with a typed slot shape in Ghidra", f"{typed} of the dump's {total_dump}"),
        ("RTTI class records (with a vtable)", f"{len(recs)} ({len(vts)})"),
        ("Class records with a `known_structs.json` layout", f"{len(layout_names & rec_names)}"),
        ("Function parameters typed with a struct", f"{apps}"),
        ("Distinct `DAT_` globals left in the decompile (named data entries)", f"{len(dats)} ({named_data})"),
    ]
    print(f"| | {binary} |\n|---|---|")
    for k, v in rows:
        print(f"| {k} | {v} |")
    if "--list" in args:
        print("\nprologues outside any function:")
        for p in prologues:
            print(f"  {p:08x}")
        print("gaps (start-end, bytes):")
        for s, e in gaps:
            print(f"  {s:08x}-{e:08x} {e - s}")


COMMANDS = {"match": cmd_match, "triage": cmd_triage, "body": cmd_body, "callers": cmd_callers, "precheck": cmd_precheck,
            "sym": cmd_sym, "funcs": cmd_funcs, "rtti": cmd_rtti, "vtables": cmd_vtables, "diff": cmd_diff, "insert": cmd_insert, "edit": cmd_edit,
            "mentions": cmd_mentions, "relink": cmd_relink, "fixrefs": cmd_fixrefs, "repl": cmd_repl, "apply": cmd_apply,
            "fixentry": cmd_fixentry, "stats": cmd_stats, "classes": cmd_classes}

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)
    COMMANDS[sys.argv[1]](sys.argv[2:])
