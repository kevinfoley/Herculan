#!/usr/bin/env python3
"""Names ES1's per-object-file boilerplate mechanically: the copies of a class's inline functions that
each Symantec C++ object file carries (the run of four ConfigPart functions that opens many files, lone
HeapPtr_Dtor copies, the GLRegion destructors, the CTL no-op slots), and the frameless stubs its
vtables hold (Herculan/docs/es1/formats/compiler.md#vtables). A batch runs it first over its range and
names by hand only what it leaves.

  scan BIN LO HI --module TAG[@ADDR] [--module TAG@ADDR ...] [--out batch.json] [--source TEXT] [--fresh]

Units, over LO..HI inclusive: every Ghidra function entry (not the 0x90 padding thunks before a
prologue), every vtable slot target with no Ghidra function (a frameless stub, entered at its first
byte or at its leading NOPs), and code in the bytes no function extent covers (after a final RET,
before zero fill). Each unit is decoded with capstone and reduced to a key: leading and trailing NOPs
dropped, branches inside the unit as offsets, calls and jumps out of it as the function they enter (a
padding entry as its prologue), and every operand that is a vtable (a dword 0 followed by a code
address) as VT, since a module's copy stores its own copies of the tables. Everything else, globals
included, must be equal.

  (a) A unit whose key equals a named function's (outside the range) is a copy. Names have their
      module tag taken off (HeapPtr_DtorPanelCopy is HeapPtr_Dtor's copy for Panel), and the proposal
      is <base><TAG>Copy, citing the base's original when one is named, else its lowest-address copy.
  (b) A stub (at most four instructions after its NOPs, the last a RET, the others neither branching
      nor writing memory) matches many names by bytes alone, so it is named through its vtable slot:
      a table holding, in the same slot, a named function with the stub's key, and whose other slots,
      over the ones both tables have, pair (the same function, or equal keys) more often than they
      differ (a derived class overrides some), gives the base; pairings through a slot that is not a
      stub, or a function both tables share, rank first. A stub no table pairs is listed unnamed with
      its pre-filled description and the named functions of the same bytes. ES1's vtables pair with
      ES2's through no existing tool (es1_naming.py pairs X-32 tables only), so no ES2 slot name is
      consulted.
A unit of either kind that its slot pairs with a named one takes that base before its bytes'. A
non-stub unit whose matches carry two bases, and a unit whose best pairings do, are reported as ties.

--module TAG names the range's object file, or TAG@ADDR one starting at ADDR, for a range spanning
several. --out writes the proposals as an insert-ready batch (confidence high) and the unnamed stubs
to <out>.unnamed.json for the batch to name; both carry --source (a doc section) before the
disassembly reference. Printed after them: the --define list (units with no Ghidra function) and the
es2_stamp.py commands, --via raw for those. --fresh treats the range's own entries as unnamed and
compares each proposal with the name it has: a positive control over a range already named.
It writes nothing under tools/ghidra_scripts/.

  bounds BIN LO HI

Lists, in address order, the evidence compiler.md#where-object-files-meet-in-the-code places object
files by: a RET followed by 1-3 zero bytes outside every function extent up to a 4-byte boundary;
each copy of HeapPtr_Dtor (the run's 40-byte destructor), lone or opening a run with the ConfigPart_Dtor
and TSPartBase_Dtor copies after it; the static constructor and exit table entries that point into
the range (the tables' bounds read from Runtime_RunStaticCtors and __dodtors); and the source paths
the range's code pushes for its asserts. Each line is a lead for the batch's "Where the object file
starts and ends", not a boundary.
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es1_naming as V  # noqa: E402
import es2_naming as N  # noqa: E402
import es2_stamp  # noqa: E402
import es2_symbols as S  # noqa: E402
import retail_binaries  # noqa: E402

HEX = re.compile(r"0x[0-9a-f]+")
BRANCH = ("j", "call", "loop")
STUB_INSNS = 4
SWEEP_LIMIT = 0x800
NOP_WORDS = {1: "a NOP", 2: "two NOPs", 3: "three NOPs"}


class Image:
    """The binary's bytes, functions, vtables and names, with a key per decoded unit."""

    def __init__(self, binary, fresh_range=None):
        import capstone
        self.b = retail_binaries.require(binary, "es1_boilerplate.py", "it reads Symantec C++ X-32 images' "
                                         "vtables and padding entries", container=retail_binaries.X32)
        self.binary = self.b.id
        self.img = self.b.image()
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.fns = {int(a, 16): v for a, v in N.functions(self.binary).items()}
        self.ext = N.Extents(self.binary)
        self.mem = self.img.sections[0][2]
        tables, _stores, self.enters = V.find_vtables(self.binary)
        self.data_start = N.x32_data_start(self.b.dump("disasm_full.txt"), self.mem)
        self.entries = {int(e["address"], 16): e for e in S.entries(self.binary)}
        self.fresh = fresh_range
        self.known = {a: e["name"] for a, e in self.entries.items() if e.get("name") and not self.in_fresh(a)}
        self.tables = self.read_tables(tables)
        self.keys = {}

    def in_fresh(self, a):
        return self.fresh is not None and self.fresh[0] <= a <= self.fresh[1]

    def code(self, w):
        """A dword that can be a vtable slot: an entry of a function or of the padding before one, or a
        straight-line stub."""
        return w < self.data_start and (w in self.fns or w in self.enters or es2_stamp.raw_extent(self.img, w))

    def read_tables(self, found):
        """{vt: [raw slot dwords from +4]}: find_vtables's tables read on past a zero slot (a pure
        virtual) up to the next table, while each dword is 0 or code; trailing zeros dropped."""
        starts = sorted(found)
        out = {}
        for i, v in enumerate(starts):
            nxt = starts[i + 1] if i + 1 < len(starts) else v + 4 + 4 * 64
            slots, p = [], v + 4
            while p + 4 <= nxt and len(slots) < 64:
                w = self.img.dword(p)
                if w is None or (w and not (len(slots) < len(found[v]) or self.code(w))):
                    break
                slots.append(w)
                p += 4
            while slots and not slots[-1]:
                slots.pop()
            out[v] = slots
        return out

    def is_vtable(self, v):
        if v in self.tables:
            return True
        if not self.data_start <= v < self.mem or self.img.dword(v) != 0:
            return False
        w = self.img.dword(v + 4)
        return bool(w) and w < self.data_start

    def resolve(self, t):
        """The function an address enters: the prologue for its padding, a function a slot enters past
        its leading NOPs, else the address itself."""
        if t in self.fns:
            return t
        if t in self.enters:
            return self.enters[t]
        c = self.ext.containing(t)
        if c and self.img.read(c[0], c[1]) == b"\x90" * c[1]:
            return c[0]
        return t

    def sweep(self, start):
        """End of the code from start: the first RET or JMP past every forward conditional branch's
        target, or None when it runs into zero fill or bytes capstone cannot decode."""
        off = self.img.off_of(start)
        if off is None:
            return None
        far, at = start, start
        for i in self.md.disasm(self.img.data[off:off + SWEEP_LIMIT], start):
            if i.address != at or i.bytes == b"\x00\x00":
                return None
            at = i.address + i.size
            if i.mnemonic.startswith(("j", "loop")) and i.mnemonic != "jmp" and HEX.fullmatch(i.op_str):
                t = int(i.op_str, 16)
                if start < t < start + SWEEP_LIMIT and t > far:
                    far = t
            if i.mnemonic in ("ret", "jmp") and at > far:
                return at
        return None

    def end_of(self, a):
        if a in self.fns:
            return self.fns[a][2]
        return es2_stamp.raw_extent(self.img, a) and a + es2_stamp.raw_extent(self.img, a) or self.sweep(a)

    def insns(self, a, end=None):
        end = end or self.end_of(a)
        if not end:
            return None
        return list(self.md.disasm(self.img.read(a, end - a) or b"", a))

    def key(self, a, end=None):
        """(key tuple, instructions with the NOPs dropped) of the unit at a, cached; (None, None) when
        it does not decode."""
        if a in self.keys:
            return self.keys[a]
        end = end or self.end_of(a)
        ins = self.insns(a, end)
        if not ins:
            self.keys[a] = (None, None)
            return self.keys[a]
        body = list(ins)
        while body and body[0].mnemonic == "nop":
            body.pop(0)
        while body and body[-1].mnemonic == "nop":
            body.pop()
        first = body[0].address if body else a
        out = []
        for i in body:
            op = i.op_str
            if i.mnemonic.startswith(BRANCH) and HEX.fullmatch(op):
                t = int(op, 16)
                op = "L+%x" % (t - first) if a <= t < end else "F:%08x" % self.resolve(t)
            else:
                op = HEX.sub(lambda m: "VT" if self.is_vtable(int(m.group(0), 16)) else m.group(0), op)
            out.append(i.mnemonic + " " + op)
        self.keys[a] = (tuple(out), ins)
        return self.keys[a]


def is_stub(key):
    """At most STUB_INSNS instructions, the last a RET, the others neither branching nor writing memory."""
    return (key is not None and 0 < len(key) <= STUB_INSNS and key[-1].startswith("ret")
            and not any(k.startswith(BRANCH) or re.match(r"\S+ (?:\w+ ptr )?\[", k) for k in key[:-1]))


def split_scores(names):
    """{copy name: base}: each '<base><Tag>Copy' name split where the base recurs with the most tags
    and the tag with the most bases; a tag must start with a capital, recur under two bases, and leave
    a base with a method part ('Class_Method'). A name no split fits is its own base less 'Copy'."""
    copies = [n for n in names if n.endswith("Copy") and len(n) > 4]
    originals = {n for n in names if not n.endswith("Copy")}
    splits = {}
    for n in copies:
        stem = n[:-4]
        splits[n] = [(stem[:i], stem[i:]) for i in range(1, len(stem))
                     if stem[i].isupper() and "_" in stem[:i] and stem[:i].split("_", 1)[1]]
    by_tag, by_base = collections.defaultdict(set), collections.defaultdict(set)
    for n, ss in splits.items():
        for b, t in ss:
            by_tag[t].add(b)
            by_base[b].add(t)
    out = {}
    for n, ss in splits.items():
        good = [(len(by_base[b]) + len(by_tag[t]) + (b in originals), b) for b, t in ss
                if len(by_tag[t]) >= 2 or len(by_base[b]) >= 2]
        out[n] = max(good)[1] if good else n[:-4]
    return out


def instr_text(i):
    op = re.sub(r"0X([0-9A-F]+)", lambda m: "0x" + m.group(1).lower(), i.op_str.upper().replace(", ", ","))
    return (i.mnemonic.upper() + " " + op).strip()


def stub_summary(ins):
    nops = 0
    while nops < len(ins) and ins[nops].mnemonic == "nop":
        nops += 1
    rest = [instr_text(i) for i in ins[nops:]]
    body = rest[0] if len(rest) == 1 else ", ".join(rest[:-1]) + " and " + rest[-1]
    if nops:
        word = NOP_WORDS.get(nops, f"{nops} NOPs")
        text = f"{word} and {body}" if len(rest) == 1 else f"{word}, then {body}"
    else:
        text = body
    if [i.mnemonic + " " + i.op_str for i in ins[nops:-1]] == ["xor eax, eax"]:
        text += ": returns 0"
    return text


def module_of(modules, a):
    tag = modules[0][0]
    for t, start in modules:
        if start <= a:
            tag = t
    return tag


def scan(args):
    lo, hi = int(args.lo, 16), int(args.hi, 16)
    x = Image(args.binary, (lo, hi) if args.fresh else None)
    img = x.img
    modules = []
    for m in args.module:
        t, _, at = m.partition("@")
        modules.append((t, int(at, 16) if at else lo))
    modules.sort(key=lambda m: m[1])
    names = x.known
    base_of = split_scores(set(names.values()))
    base = lambda n: base_of.get(n, n)  # noqa: E731

    # Units: Ghidra functions, slot targets with no function, code in uncovered bytes.
    units = {}
    for a, (_g, _s, end) in x.fns.items():
        if lo <= a <= hi and not V.is_padding_thunk(img, a):
            units[a] = end
    slots_of = collections.defaultdict(list)
    for vt, slots in x.tables.items():
        for k, w in enumerate(slots):
            if w:
                slots_of[x.resolve(w)].append((vt, 4 + 4 * k, w))
    raw = sorted({w for w in slots_of if lo <= w <= hi and w not in units and not x.ext.containing(w)})
    for w in raw:
        prev = max((u for u in units if u < w), default=None)
        if prev is not None and units[prev] is None and img.read(prev, w - prev) == b"\x90" * (w - prev):
            for s in slots_of.pop(w):
                slots_of[prev].append(s)
            continue
        units[w] = None
    for w in [u for u, e in units.items() if e is None]:
        units[w] = x.end_of(w)
    covered = sorted((a, e) for a, e in units.items() if e)
    covered += [(a, x.fns[a][2]) for a in x.fns if lo <= a <= hi and V.is_padding_thunk(img, a)]
    covered.sort()
    gaps, p = [], lo
    for a, e in covered + [(hi + 1, hi + 1)]:
        while p < a:
            byte = img.read(p, 1)
            if byte not in (b"\x00", b"\x90"):
                end = x.sweep(p)
                gaps.append((p, end))
                if not end:
                    break
                units[p] = end
                p = end
                continue
            p += 1
        p = max(p, e)

    # The pool: named functions' keys.
    pool = collections.defaultdict(list)
    for a, n in names.items():
        if x.entries[a]["type"] != "function" or V.is_padding_thunk(img, a):
            continue
        k, _ = x.key(a)
        if k:
            pool[k].append(a)

    def pairings(a, k):
        """[(sibling table, its function in the same slot)] for unit a's slots, per the docstring's (b).
        Over the slots both tables have (a derived class's table runs on past its base's), each other
        slot pairs when it holds the same function in both, or two functions with one key; 0 in both
        is neutral, and anything else is an override. A table pairs when more slots pair than are
        overridden; the best rank wins: a pair through a slot that is not a stub or a function both
        tables share first, then the most pairs net of overrides."""
        ranked = []
        for vt, off, _w in slots_of.get(a, []):
            mine = x.tables[vt]
            j0 = (off - 4) // 4
            for wt, theirs in x.tables.items():
                n = min(len(mine), len(theirs))
                if wt == vt or j0 >= n:
                    continue
                f = x.resolve(theirs[j0]) if theirs[j0] else 0
                if f not in names or x.key(f)[0] != k:
                    continue
                anchor, pairs, overrides = False, 0, 0
                for j in range(n):
                    if j == j0:
                        continue
                    p_, q_ = (x.resolve(mine[j]) if mine[j] else 0), (x.resolve(theirs[j]) if theirs[j] else 0)
                    if p_ == q_:
                        pairs += bool(p_)
                        anchor = anchor or bool(p_)
                        continue
                    kp, kq = (x.key(p_)[0] if p_ else None), (x.key(q_)[0] if q_ else None)
                    if kp is None or kp != kq:
                        overrides += 1
                        continue
                    pairs += 1
                    anchor = anchor or not is_stub(kp)
                if pairs > overrides:
                    ranked.append(((anchor, pairs - overrides), wt, f))
        if not ranked:
            return []
        best = max(r[0] for r in ranked)
        return [(wt, f) for r, wt, f in ranked if r == best]

    def table_text(vt):
        n = names.get(vt) if x.entries.get(vt, {}).get("type") == "data" else None
        return f"{n} ({vt:08x})" if n else f"the vtable {vt:08x}"

    def slot_text(a):
        parts = []
        for vt, off, w in sorted(slots_of.get(a, [])):
            t = f"slot +{off if off < 10 else hex(off)} of {table_text(vt)}"
            if w < a:
                t += f" (entered at its padding {w:08x})"
            elif w > a:
                t += f" (entered at {w:08x})"
            parts.append(t)
        return ", and ".join(parts)

    def vt_operands(ins):
        return [int(m, 16) for i in ins for m in HEX.findall(i.op_str)
                if not i.mnemonic.startswith(BRANCH) and x.is_vtable(int(m, 16))]

    def describe(a, ins, ref, stub):
        rdesc = x.entries[ref].get("description", "")
        es2 = re.search(r"ES2's \w+ \(DBSIM [0-9a-f]{8}\)", rdesc)
        slots = slot_text(a)
        if stub:
            d = f"Frameless: {stub_summary(ins)}."
            if slots:
                d += f" {slots[0].upper() + slots[1:]};"
                d += f" the code of {names[ref]} ({ref:08x})."
            else:
                d += f" The code of {names[ref]} ({ref:08x})."
        else:
            sig = re.match(r"(?:Frameless: )?(\([^)]*\)(?: in stack order)?(?:, callee-cleaned)?)", rdesc)
            d = (sig.group(1) + ": " if sig else "") + (slots + ": " if slots else "")
            d += f"byte for byte the code of {names[ref]} ({ref:08x}), relocated operands aside"
            mine, theirs = vt_operands(ins), vt_operands(x.key(ref)[1])
            diff = [(p_, q_) for p_, q_ in zip(mine, theirs) if p_ != q_]
            if diff:
                d += (", storing " + " and ".join(f"{p_:08x}" for p_, _ in diff) + " where that stores "
                      + " and ".join(f"{q_:08x}" for _, q_ in diff))
            d = d[0].upper() + d[1:] + "."
        if es2:
            d += f" {es2.group(0)}."
        return d

    def source(a):
        return (args.source + "; " if args.source else "") + f"{x.binary} disassembly at {a:08x}"

    proposals, unnamed, ties, notes = [], [], [], []
    taken = set(names.values())
    result = {}
    for a in sorted(units):
        k, ins = x.key(a, units[a])
        if k is None:
            notes.append(f"{a:08x} does not decode")
            continue
        if a in names:
            result[a] = ("named", names[a])
            continue
        stub = is_stub(k)
        matches = [m for m in pool.get(k, []) if m != a]
        bases = sorted({base(names[m]) for m in matches})
        pick, why = None, None
        pairs = pairings(a, k)
        pbases = sorted({base(names[f]) for _, f in pairs})
        if len(pbases) == 1:
            pick, why = pbases[0], "slot of " + ", ".join(f"{wt:08x}" for wt, _ in pairs[:3])
        elif len(pbases) > 1:
            ties.append(f"{a:08x}: its slot pairs with tables naming {', '.join(pbases)}")
        elif not stub and len(bases) == 1:
            pick, why = bases[0], "bytes"
        elif not stub and len(bases) > 1:
            ties.append(f"{a:08x}: the code of " + ", ".join(f"{names[m]} ({m:08x})" for m in matches[:6]))
        tag = module_of(modules, a)
        if pick:
            cands = [m for m in matches if base(names[m]) == pick]
            ref = next((m for m in cands if names[m] == pick), min(cands))
            name = pick + tag + "Copy"
            if name in taken:
                notes.append(f"{a:08x}: {name} is already taken in {x.binary} (a second copy in one object file is named by hand)")
                continue
            taken.add(name)
            e = {"address": f"{a:08x}", "binary": x.binary, "type": "function", "confidence": "high",
                 "name": name, "description": describe(a, ins, ref, stub), "source": source(a)}
            proposals.append(e)
            result[a] = ("proposed", name, why)
        elif stub:
            d = f"Frameless: {stub_summary(ins)}." + (f" {slot_text(a)[0].upper() + slot_text(a)[1:]}." if slots_of.get(a) else "")
            unnamed.append({"address": f"{a:08x}", "binary": x.binary, "type": "function", "confidence": "high",
                            "description": d, "source": source(a)})
            same = ", ".join(f"{names[m]} ({m:08x})" for m in matches[:4]) + (f" and {len(matches) - 4} more" if len(matches) > 4 else "")
            result[a] = ("unnamed", same)
        elif a not in x.fns:
            what = "code outside every Ghidra function extent, not a copy"
            if len(k) == 2 and re.fullmatch(r"add dword ptr \[esp \+ 4\], (-?0x[0-9a-f]+|-?\d+)", k[0]) and k[1].startswith("jmp F:"):
                t = int(k[1][6:], 16)
                what = (f"a this-adjusting thunk with no Ghidra function: adds {k[0].rsplit(', ', 1)[1]} to the "
                        f"object and jumps to {names.get(t, f'{t:08x}')}" + (f" ({t:08x})" if t in names else ""))
            notes.append(f"{a:08x}-{units[a]:08x}: {what}")

    # Report.
    print(f"{x.binary} {lo:08x}-{hi:08x}: {len(units)} units, {len(proposals)} proposed, "
          f"{len(unnamed)} unnamed stubs, {len(ties)} ties")
    for e in proposals:
        print(f"  {e['address']} {e['name']}  [{result[int(e['address'], 16)][2]}]\n      {e['description']}")
    for e in unnamed:
        same = result[int(e["address"], 16)][1]
        print(f"  {e['address']} (unnamed stub)  {e['description']}" + (f"\n      same bytes as {same}" if same else ""))
    for t in ties:
        print("  TIE " + t)
    for n in notes:
        print("  NOTE " + n)
    for g, end in gaps:
        if not end:
            print(f"  NOTE {g:08x}: uncovered bytes that do not decode as code")

    if args.fresh:
        print("\ncontrol against the existing entries:")
        tally = collections.Counter()
        for a in sorted(units):
            e = x.entries.get(a)
            have = e.get("name") if e else None
            r = result.get(a)
            got = r[1] if r and r[0] == "proposed" else None
            if got and got == have:
                tally["match"] += 1
                print(f"  MATCH   {a:08x} {have}")
            elif got:
                tally["differ"] += 1
                print(f"  DIFFER  {a:08x} proposed {got}, entry {have or '(none)'}")
            elif r and r[0] == "unnamed" and not (have or "").endswith("Copy"):
                tally["stub left to name"] += 1
                print(f"  STUB    {a:08x} left unnamed, entry {have or '(none)'}")
            elif have and have.endswith("Copy"):
                tally["missed"] += 1
                print(f"  MISSED  {a:08x} {have}" + (f"  ({r[0]}{': same bytes as ' + r[1] if r[0] == 'unnamed' and r[1] else ''})" if r else ""))
        print("  " + ", ".join(f"{k} {v}" for k, v in sorted(tally.items())))

    define = sorted(int(e["address"], 16) for e in proposals + unnamed if int(e["address"], 16) not in x.fns)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(proposals, f, indent=2, ensure_ascii=False)
        print(f"\n{len(proposals)} proposals written to {args.out}")
        if unnamed:
            side = re.sub(r"(\.json)?$", ".unnamed.json", args.out, count=1)
            with open(side, "w", encoding="utf-8") as f:
                json.dump(unnamed, f, indent=2, ensure_ascii=False)
            print(f"{len(unnamed)} unnamed stubs, to name before inserting, in {side}")
    if define:
        print(f"define: py tools/scripts/es2_naming.py apply {x.binary} --define {'+'.join(f'{a:08x}' for a in define)}")
    framed = [e["address"] for e in proposals if int(e["address"], 16) in x.fns]
    rawd = [e["address"] for e in proposals if int(e["address"], 16) not in x.fns]
    if framed:
        print(f"stamp:  py tools/scripts/es2_stamp.py stamp {'+'.join(framed)} --binary {x.binary} --via disasm")
    if rawd:
        print(f"stamp:  py tools/scripts/es2_stamp.py stamp {'+'.join(rawd)} --binary {x.binary} --via raw")
    if unnamed:
        print("        (and the unnamed stubs once named: --via raw where no Ghidra function starts, else --via disasm)")


RUN_BASES = ("HeapPtr_Dtor", "ConfigPart_Dtor", "TSPartBase_Dtor")
SOURCE_PATH = re.compile(rb"[\w:\\./-]+\.(?:cpp|c)\Z", re.I)


def table_bounds(x, name):
    """(low, high) of the pointer table the named walk runs over: the MOV reg,imm and CMP reg,imm in
    its first instructions (Runtime_RunStaticCtors, compiler.md#static-construction-and-exit). None
    when the binary has no such name or the walk is not of that shape."""
    a = next((a for a, n in x.known.items() if n == name), None)
    if a is None:
        return None
    imms = []
    for i in x.md.disasm(x.img.read(a, 32) or b"", a):
        if i.mnemonic in ("mov", "cmp") and HEX.search(i.op_str) and "[" not in i.op_str:
            imms.append(int(HEX.search(i.op_str).group(0), 16))
        if len(imms) == 2:
            return min(imms), max(imms)
    return None


def bounds(args):
    """The evidence compiler.md#where-object-files-meet-in-the-code places object files by, over a range."""
    lo, hi = int(args.lo, 16), int(args.hi, 16)
    x = Image(args.binary)
    img = x.img
    events = []   # (address, kind, text)

    # 1. Zero fill after a return: 1-3 zero bytes outside every function extent, up to a 4-byte
    #    boundary, right after the bytes of a RET or RET n. A zero inside an instruction is in an extent.
    for p in range(lo, hi + 2):
        if p % 4 or p == 0:
            continue
        n = 0
        while n < 3 and img.read(p - n - 1, 1) == b"\x00" and not x.ext.containing(p - n - 1):
            n += 1
        if not n:
            continue
        z = p - n
        if img.read(z - 1, 1) == b"\xc3":
            ret, at = "RET", z - 1
        elif img.read(z - 3, 1) == b"\xc2":
            ret, at = "RET %#x" % int.from_bytes(img.read(z - 2, 2), "little"), z - 3
        else:
            continue
        events.append((p, "zero fill", f"{ret} at {at:08x}, then {n} zero byte{'s' * (n > 1)} to {p:08x}"))

    # 2. Copies of the ConfigPart run's destructors: a lone HeapPtr_Dtor copy opens a file, as does a run.
    base_of = split_scores(set(x.known.values()))
    run_keys = {}
    for a, n in x.known.items():
        b = base_of.get(n, n)
        if b in RUN_BASES and x.entries[a]["type"] == "function":
            k, _ = x.key(a)
            if k:
                run_keys.setdefault(k, b)
    copies = []
    # A run's other destructors are looked for past hi too, so a run that opens at the range's end shows.
    for a in sorted(f for f in x.fns if lo <= f <= hi + 0x100 and not V.is_padding_thunk(img, f)):
        k, _ = x.key(a)
        if k in run_keys:
            copies.append((a, run_keys[k]))
    for i, (a, b) in enumerate(copies):
        if b != "HeapPtr_Dtor" or a > hi:
            continue
        after = [c for c in copies[i + 1:] if c[0] - a < 0x100]
        rest = [c for c in after if c[1] != "HeapPtr_Dtor"][:2]
        what = ("opening a run: " + ", ".join(f"{bb} copy {aa:08x}" for aa, bb in rest)) if rest and rest[0][0] - a < 0x60 \
            else "lone" + (f" (the run's other destructors follow at " + ", ".join(f"{aa:08x}" for aa, _ in rest) + ")" if rest else "")
        name = x.known.get(a)
        events.append((a, "destructor", f"{a:08x} HeapPtr_Dtor copy{' ' + name if name else ''}, {what}"))

    # 3. Static constructor and exit table entries pointing into the range.
    for label, walk in (("static ctor", "Runtime_RunStaticCtors"), ("exit", "__dodtors")):
        tb = table_bounds(x, walk)
        if not tb:
            if walk == "Runtime_RunStaticCtors":
                print(f"  (no {walk} in {x.binary}'s names: the static constructor table is not read)")
            continue
        t_lo, t_hi = tb
        if label == "exit":
            ctor = table_bounds(x, "Runtime_RunStaticCtors")
            t_lo = ctor[1] if ctor else t_lo
        for e in range(t_lo, t_hi, 4):
            w = img.dword(e)
            if w and lo <= w <= hi:
                end = x.end_of(w)
                n = x.known.get(w)
                events.append((w, label, f"{label} table entry {e:08x} -> {w:08x}{' ' + n if n else ''}"
                                         + (f", code to {end:08x}" if end else "")))

    # 4. Source paths the range's code pushes (an assert names its object file).
    seen = set()
    for a in sorted(f for f in x.fns if lo <= f <= hi):
        ins = x.insns(a, x.fns[a][2]) or []
        for i in ins:
            if i.mnemonic != "push" or not HEX.fullmatch(i.op_str):
                continue
            t = int(i.op_str, 16)
            if t < x.data_start or t in seen:
                continue
            raw = img.read(t, 80) or b""
            s = raw.split(b"\x00", 1)[0]
            if SOURCE_PATH.search(s):
                seen.add(t)
                events.append((i.address, "source path", f"{i.address:08x} pushes {t:08x} "
                                                         f"\"{s.decode('latin-1')}\""))

    print(f"{x.binary} {lo:08x}-{hi:08x}: object-file boundary evidence (compiler.md#where-object-files-meet-in-the-code)")
    order = {"zero fill": 0, "static ctor": 1, "exit": 2, "destructor": 3, "source path": 4}
    for a, kind, text in sorted(events, key=lambda e: (e[0], order[e[1]])):
        print(f"  {kind:<12} {text}")
    starts = sorted({a for a, kind, _ in events if kind in ("zero fill", "destructor")})
    if starts:
        print("  starts the zero fill and destructor copies place: " + ", ".join(f"{a:08x}" for a in starts))
    print("  (a lead per line, not a boundary: read compiler.md's rules; a file can open with no copy, and "
          "a static initializer is not always a file's last code)")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    bp = sub.add_parser("bounds", help="list the evidence object-file boundaries are placed by, over a range")
    bp.add_argument("binary", type=str.upper, choices=[b for b in retail_binaries.IDS
                                                       if retail_binaries.get(b).container == retail_binaries.X32])
    bp.add_argument("lo")
    bp.add_argument("hi", help="last address of the range, inclusive")
    bp.set_defaults(fn=bounds)
    s = sub.add_parser("scan", help="propose names for the boilerplate in a range")
    s.add_argument("binary", type=str.upper, choices=[b for b in retail_binaries.IDS
                                                      if retail_binaries.get(b).container == retail_binaries.X32])
    s.add_argument("lo")
    s.add_argument("hi", help="last address of the range, inclusive")
    s.add_argument("--module", action="append", required=True,
                   help="TAG for the range's object file, or TAG@ADDR for one starting at ADDR")
    s.add_argument("--out", help="write the insert-ready batch here (and <out>.unnamed.json)")
    s.add_argument("--source", default="", help="the doc section for each entry's source field")
    s.add_argument("--fresh", action="store_true",
                   help="treat the range's entries as unnamed and compare the proposals with them")
    s.set_defaults(fn=scan)
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args.fn(args)


if __name__ == "__main__":
    main()
