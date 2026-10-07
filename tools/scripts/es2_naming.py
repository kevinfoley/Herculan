#!/usr/bin/env python3
"""Helpers for working down the function-naming backlog in the known_symbols_<binary>.json files.

Subcommands (BIN is DBSIM or VSHELL):

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
      copy of the other's, and where the two differ.
  match BIN BIN2 [--min N] [--fuzzy R] [--all]
      BIN's unnamed functions (every function with --all) of at least N instructions (default 6)
      whose disassembly, with every in-image operand value masked, equals a BIN2 function's; with
      --fuzzy R, the three closest named BIN2 functions at a difflib ratio of at least R when there
      is no exact match. Under a unique exact named match it lists the unnamed BIN callees and
      globals that pair, operand by operand, with named BIN2 ones. Confirm a candidate with diff.
  callers BIN addr... [-n CTX] [--max M]
      Every decompile line naming FUN_/DAT_<addr> (or its known name), with the enclosing function.
  precheck BIN addr...
      The known_symbols entry and every Herculan docs/src/tests mention of each address.
  sym BIN regex...
      known_symbols entries whose name or address matches.
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
      confidence and a name together). A name change also renames the old name, or the FUN_/DAT_
      label of a newly named entry, elsewhere in the file (word boundary).
  relink batch.json [--write]
      Rewrite FUN_/DAT_<addr> mentions of the batch's named entries in the descriptions of other
      entries of the same binary (insert does this itself).
  mentions batch.json
      FUN_/DAT_/bare-address mentions of the batch's named addresses in docs/src/tests.
  fixrefs batch.json [--write]
      Rewrite `FUN_a` to `Name` (`a`) in docs and <c>FUN_a</c> / bare FUN_a in C#, and list the
      mentions it cannot rewrite safely (bare addresses, other shapes) for a manual edit.
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
import es2_unnamed_callees as uc  # noqa: E402

REPO = uc.REPO
ANALYSIS = uc.ANALYSIS
HDR = re.compile(r"^/\* =+\n   (\S+) @ ([0-9a-f]{8})[^\n]*\n   =+ \*/\n", re.M)
FIELDS = S.FIELDS
CACHE = os.path.join(tempfile.gettempdir(), "es2_naming_cache")
LABEL = re.compile(r"\b(?:thunk_)?(FUN|DAT|PTR|LAB|_DAT)_([0-9a-f]{8})\b")
DECL = re.compile(r"^  [A-Za-z_][\w ]*?[\w*]+ \**[A-Za-z_]\w*(?:\s*\[\w+\])?;(?:\s*/\*.*\*/)?$")


def entries(binary=None):
    return S.entries(binary)


def names(binary):
    return {e["address"].lower(): e["name"] for e in entries(binary) if e.get("name")}


def bodies(binary):
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
    """{address: (Ghidra name, body bytes)} from analysis_out/BIN_functions.txt."""
    out = {}
    with open(os.path.join(ANALYSIS, f"{binary}_functions.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            a, n, s = line.rstrip("\r\n").split("\t")
            out[a.lower()] = (n, int(s))
    return out


class Extents:
    """Ghidra function extents, each taken as entry .. entry + body bytes. A body Ghidra split into
    several ranges is shorter than its span, so a byte past the first range reads as outside it; the
    stamps in es2_stamp.py make the same assumption."""

    def __init__(self, binary):
        self.fns = functions(binary)
        self.starts = sorted(int(a, 16) for a in self.fns)

    def containing(self, va):
        """(entry, offset) of the function whose extent holds va, or None."""
        i = bisect.bisect_right(self.starts, va) - 1
        if i < 0:
            return None
        e = self.starts[i]
        return (e, va - e) if va < e + self.fns[f"{e:08x}"][1] else None

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
    image = X.Image(X.BINARIES[binary])
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


def normalised(binary, addr):
    """Compact body with known names substituted, then every remaining FUN_/DAT_/PTR_/LAB_ label,
    stack-local name and the function's own name reduced to a placeholder, so two compiled copies
    of one function compare equal up to what they actually do."""
    known = names(binary)
    body = bodies(binary).get(addr)
    if body is None:
        return None
    t = compact(body, known)
    t = re.sub(r"\bPTR_\w*?[0-9a-f]{8}\b", "PTR_?", t)
    t = re.sub(r"\b(?:thunk_)?(FUN|DAT|LAB|_DAT)_[0-9a-f]{8}\b", r"\1_?", t)
    t = re.sub(r"\b(?:local|auStack|uStack|iStack|puStack|pcStack|in_stack)_[0-9a-f]+\b", "local_?", t)
    t = re.sub(r"\b(?:[a-z]{1,3}Var)\d+\b", "var?", t)
    own = known.get(addr)
    if own:
        t = re.sub(r"\b" + re.escape(own) + r"\b", "SELF", t)
    head, _, rest = t.partition("\n")
    head = re.sub(r"\b__(?:cdecl|stdcall|fastcall|thiscall) ", "", head).replace("FUN_?(", "SELF(", 1)
    return head + "\n" + rest


def cmd_diff(args):
    """diff BIN addr BIN2 addr2: unified diff of the two normalised bodies (empty when they match)."""
    import difflib
    (b1, a1, b2, a2) = args[:4]
    a1, a2 = a1.lower().zfill(8), a2.lower().zfill(8)
    t1, t2 = normalised(b1, a1), normalised(b2, a2)
    if t1 is None or t2 is None:
        print("not in decompile:", a1 if t1 is None else "", a2 if t2 is None else "")
        return
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


def cmd_sym(args):
    binary = args[0]
    for p in args[1:]:
        r = re.compile(p, re.I)
        for e in entries():
            if e["binary"] == binary and (r.search(e.get("name") or "") or r.search(e["address"])):
                print(f"{e['address']} [{e['type']}/{e['confidence']}] {e.get('name')}: {e.get('description', '')}\n")


def pe(binary):
    with open(os.path.join(REPO, "ES2", f"{binary}.EXE"), "rb") as f:
        d = f.read()
    o = struct.unpack_from("<I", d, 0x3c)[0]
    n = struct.unpack_from("<H", d, o + 6)[0]
    so = struct.unpack_from("<H", d, o + 20)[0]
    secs = []
    for i in range(n):
        vs, va, rs, ro = struct.unpack_from("<IIII", d, o + 24 + so + i * 40 + 8)
        secs.append((va + 0x400000, vs, ro, rs))

    def off(v):
        for va, vs, ro, rs in secs:
            if va <= v < va + max(vs, rs):
                return v - va + ro
        return None
    return d, off


def cmd_rtti(args):
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
    with open(os.path.join(REPO, "ES2", f"{binary}.EXE"), "rb") as f:
        d = f.read()
    o = struct.unpack_from("<I", d, 0x3c)[0]
    n = struct.unpack_from("<H", d, o + 6)[0]
    so = struct.unpack_from("<H", d, o + 20)[0]
    for i in range(n):
        s = o + 24 + so + i * 40
        vs, va = struct.unpack_from("<II", d, s + 8)
        if struct.unpack_from("<I", d, s + 36)[0] & 0x20:
            return va + 0x400000, va + 0x400000 + vs
    raise AssertionError("no code section")


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
    d, off = pe(binary)
    lo, hi = code_range(binary)
    import es2_classes
    img = es2_classes.Image(os.path.join(REPO, "ES2", f"{binary}.EXE"))
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


def cmd_insert(args):
    with open(args[0], encoding="utf-8") as f:
        new = json.load(f)
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
        text2 = relink(S.insert_blocks(text, batch), batch)
        texts[binary] = (text2, nl, len(S.check(binary, text2)["entries"]))
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


def cmd_relink(args):
    with open(args[0], encoding="utf-8") as f:
        batch = json.load(f)
    texts = {}
    for binary, part in by_binary(batch).items():
        text, nl = S.read_text(binary)
        text = relink(text, part)
        S.check(binary, text)
        texts[binary] = (text, nl)
    if "--write" in args:
        for binary, (text, nl) in texts.items():
            S.write_text(binary, text, nl)
        print("written")


def cmd_edit(args):
    with open(args[0], encoding="utf-8") as f:
        edits = json.load(f)
    texts = {}
    for binary, part in by_binary(edits).items():
        text, nl = S.read_text(binary)
        texts[binary] = (edit_text(text, part), nl)
        print("ok", binary, len(S.check(binary, texts[binary][0])["entries"]))
    if "--write" in args:
        for binary, (text, nl) in texts.items():
            S.write_text(binary, text, nl)
        print("written")


def edit_text(text, edits):
    for ed in edits:
        pat = re.compile(r'    \{\n      "address": "' + ed["address"] + r'",\n      "binary": "' + ed["binary"]
                         + r'",\n(?:      .*\n)*?    \}', re.M)
        ms = list(pat.finditer(text))
        assert len(ms) == 1, (ed["address"], len(ms))
        m = ms[0]
        e = json.loads(m.group(0))
        old = e.get("name")
        fields = ed.get("fields") or {ed["field"]: ed["value"]}
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


def cmd_fixrefs(args):
    """Rewrite the usual reference shapes to a batch's new names in docs (.md) and C# (.cs):
    `FUN_a` -> `Name` (`a`) in markdown, <c>FUN_a</c> -> <c>Name</c> (<c>a</c>) and a bare FUN_a
    -> Name (a) in C#. Anything else naming the address is listed for a manual edit."""
    named = batch_names(args[0])
    if not named:
        return
    alt = "|".join(named)
    md = re.compile(r"`(?:FUN|DAT|PTR)_(" + alt + r")`( \(`\1`\))?")
    cs_c = re.compile(r"<c>(?:FUN|DAT|PTR)_(" + alt + r")</c>( \(<c>\1</c>\))?")
    cs_bare = re.compile(r"(?<![0-9A-Za-z_])(?:FUN|DAT|PTR)_(" + alt + r")(?![0-9A-Za-z])( \(\1\))?")
    left = re.compile(r"(?<![0-9A-Za-z])(?:FUN_|DAT_|PTR_)?(" + alt + r")(?![0-9A-Za-z])")
    changed = 0
    for p, _ in repo_texts().items():
        with open(p, "rb") as fh:
            raw = fh.read()
        bom = raw[:3] == b"\xef\xbb\xbf"
        t = (raw[3:] if bom else raw).decode("utf-8")
        if p.endswith(".md"):
            t2 = md.sub(lambda m: f"`{named[m.group(1)]}` (`{m.group(1)}`)", t)
            # Inside parentheses already: "(`Name` (`a`))" reads better as "(`Name`, `a`)".
            t2 = re.sub(r"\(`(\w+)` \(`(" + alt + r")`\)\)",
                        lambda m: f"(`{m.group(1)}`, `{m.group(2)}`)" if named[m.group(2)] == m.group(1) else m.group(0), t2)
        else:
            t2 = cs_c.sub(lambda m: f"<c>{named[m.group(1)]}</c> (<c>{m.group(1)}</c>)", t)
            t2 = cs_bare.sub(lambda m: f"{named[m.group(1)]} ({m.group(1)})", t2)
        if t2 != t:
            changed += 1
            print("fix", os.path.relpath(p, REPO))
            if "--write" in args:
                with open(p, "wb") as fh:
                    fh.write((b"\xef\xbb\xbf" if bom else b"") + t2.encode("utf-8"))
        for i, l in enumerate(t2.splitlines()):
            for m in left.finditer(l):
                if not re.search(re.escape(named[m.group(1)]), l):
                    print(f"  manual {os.path.relpath(p, REPO)}:{i + 1}: [{m.group(0)} -> {named[m.group(1)]}] "
                          f"...{l[max(0, m.start() - 60):m.end() + 40]}...")
    print(changed, "files", "written" if "--write" in args else "(dry run)")


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
    bat = os.path.join(REPO, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
    cmd = [bat, os.path.join(REPO, "tools", "ghidra_project"), "ES2Recon", "-process", f"{binary}.EXE",
           "-noanalysis"] + ([] if "--write" in args else ["-readOnly"]) + [
        "-scriptPath", os.path.join(REPO, "tools", "ghidra_scripts")]
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
    scripts = os.path.join(REPO, "tools", "ghidra_scripts")
    bat = os.path.join(REPO, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
    tmp = tempfile.mkdtemp(prefix="es2_fixentry_")
    try:
        cmd = [bat, os.path.join(REPO, "tools", "ghidra_project"), "ES2Recon", "-process", f"{binary}.EXE",
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


def insn_streams(binary):
    """{function address: [(insn address, normalised insn, absolute operand values)]} from the disasm
    dump. Every operand value inside the image (0x400000..0x7fffff) becomes 'A' and is kept in the
    third field, so a function compiled into both EXEs compares equal whatever its callees, globals
    and jump targets were relocated to."""
    src = os.path.join(ANALYSIS, f"{binary}_disasm_full.txt")
    cache = os.path.join(CACHE, f"{binary}_insns.pkl")
    os.makedirs(CACHE, exist_ok=True)
    if os.path.exists(cache) and os.path.getmtime(cache) > max(os.path.getmtime(src), os.path.getmtime(__file__)):
        with open(cache, "rb") as f:
            return pickle.load(f)
    absv = re.compile(r"0x0*([4-7][0-9a-f]{5})\b")
    d, cur = {}, None
    with open(src, encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("; FUNCTION "):
                cur = d.setdefault(line.rstrip().rsplit("@ ", 1)[1], [])
                continue
            m = INSN.match(line)
            if cur is None or not m:
                continue
            ops = m.group(3) or ""
            vals = [v for v in absv.findall(ops)]
            cur.append((m.group(1), m.group(2) + " " + absv.sub("A", ops), vals))
    with open(cache, "wb") as f:
        pickle.dump(d, f)
    return d


def cmd_match(args):
    """match BIN BIN2 [--min N] [--fuzzy R] [--all]: BIN's unnamed functions whose normalised
    instruction stream equals (or, with --fuzzy, is at least R similar to) a named BIN2 function's,
    with the BIN2 name and the callee/global names that pair up position by position."""
    import difflib
    b1, b2 = args[0], args[1]
    mn = int(args[args.index("--min") + 1]) if "--min" in args else 6
    fz = float(args[args.index("--fuzzy") + 1]) if "--fuzzy" in args else None
    k1, k2 = names(b1), names(b2)
    s1, s2 = insn_streams(b1), insn_streams(b2)
    by_key = {}
    for a, ins in s2.items():
        by_key.setdefault(tuple(i[1] for i in ins), []).append(a)
    todo = [a for a in sorted(s1) if ("--all" in args or a not in k1) and len(s1[a]) >= mn]
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
                    p, q = p.zfill(8), q.zfill(8)
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
    img = es2_classes.Image(os.path.join(REPO, "ES2", f"{binary}.EXE"))
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
    ext = Extents(binary)
    fns = ext.fns
    d, off = pe(binary)
    lo, hi = code_range(binary)
    unnamed = {a for a, (n, _) in fns.items() if unnamed_label(n)}
    total_b = sum(s for _, s in fns.values())
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
            cur = max(cur, e + fns[f"{e:08x}"][1])
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
            "sym": cmd_sym, "rtti": cmd_rtti, "vtables": cmd_vtables, "diff": cmd_diff, "insert": cmd_insert, "edit": cmd_edit,
            "mentions": cmd_mentions, "relink": cmd_relink, "fixrefs": cmd_fixrefs, "repl": cmd_repl, "apply": cmd_apply,
            "fixentry": cmd_fixentry, "stats": cmd_stats, "classes": cmd_classes}

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)
    COMMANDS[sys.argv[1]](sys.argv[2:])
