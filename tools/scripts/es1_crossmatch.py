#!/usr/bin/env python3
"""Pair ES1 functions with ES2 functions: the candidate map of the ES1 plan's step 0.7.

Herculan/docs/herculan/plan-es1-support.md, step 0.7. ES1's Symantec C++ code and ES2's Borland
C++ code share no byte patterns, so a pairing rests on what the two compilers both preserve, read
from the analysis_out dumps (`<ID>_disasm_full.txt`) and the images:

  anchors      class     the persistent-class tables: a (tag, version, name) record in both
                         binaries pairs the two factory functions it points at
               string    a string of 6 or more characters that exactly one function references in
                         each binary
               constant  an immediate operand that is neither small, a power of two nor a mask, and
                         that exactly one function uses in each binary
               data      a referenced, initialised data block of 8 or 16 bytes, not text, that is
                         byte-identical and referenced by exactly one function in each binary
  propagation  callee    two paired functions' direct calls, aligned on the calls already paired:
                         where one gap between aligned calls holds exactly one unpaired callee on
                         each side, those two pair
               caller    two paired functions that each have exactly one unpaired direct caller
               vtable    two paired functions that each store the same number of vtables: their
                         tables pair in order and, when two tables have the same number of slots,
                         their slots pair by index

Anchors are taken together: a function claimed by two partners is a conflict and neither pairs.
Propagation runs in rounds from the anchors, each round taking the proposals of the pairs the round
before made, under the same rule; a proposal that contradicts an earlier pair is a conflict. Each
candidate keeps every piece of evidence found for it and its hop count from an anchor.

A candidate is a hypothesis about the ES1 function, never a name for it (plan step 1.2). ES1's
Symantec runtime functions that es1_libmatch.py names are neither candidates nor callees: ES2 links
Borland's runtime, and ES1's software floating point (`__DMUL`, ...) has no ES2 call to pair with.

Source modules come from the assert paths: each module's path strings lie in its own data, the
linker lays code and data out in one object order, and string literals stay with their object. Each
function's string references give the modules it can belong to; a monotone fit over the code order
assigns modules, and the functions between the last evidence for one module and the first for the
next are counted as that boundary's margin. A module with no asserts has no path string and is
counted with its neighbours' margin.

    py tools/scripts/es1_crossmatch.py                     # both ES1 binaries against DBSIM and VSHELL
    py tools/scripts/es1_crossmatch.py --binary ES1DBSIM --target DBSIM --list
    py tools/scripts/es1_crossmatch.py --binary ES1GO --show 00012ec7

Writes `analysis_out/<ES1 ID>_es2_candidates.json` (gitignored, like the dumps) unless --no-write.
"""
from __future__ import annotations

import argparse
import bisect
import collections
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_symbols  # noqa: E402
import retail_binaries as R  # noqa: E402

LIBMATCH_SOURCE = "Herculan/docs/es1/formats/compiler.md#identification"
FUNC = re.compile(r"^; FUNCTION (\S+) @ ([0-9a-f]{8})")
INSN = re.compile(r"^([0-9a-f]{8})   ([0-9a-f]+)\s+(\S+)(?: (.*?))?\s*(?:;.*)?$")
HEX = re.compile(r"0x([0-9a-f]+)")
CALL = re.compile(r"^0x([0-9a-f]+)$")
VSTORE = re.compile(r"^dword ptr \[[^\]]*[A-Z]{2}[^\]]*\],0x([0-9a-f]+)$")
STRING_MIN = 6        # characters in a string anchor
SMALL = 0x400         # immediates within +-SMALL are not constant anchors


class Func:
    __slots__ = ("start", "name", "size", "calls", "refs", "imms", "vstores", "rets")

    def __init__(self, start, name):
        self.start, self.name, self.size = start, name, 0
        self.calls, self.refs, self.imms, self.vstores, self.rets = [], [], [], [], set()


class Program:
    """One binary's functions, read from its disassembly dump, with calls, data references,
    immediates and vtable stores per function."""

    def __init__(self, bid: str):
        self.b = R.get(bid)
        self.id = bid
        self.img = R.Image(self.b)
        self.es1 = self.b.compiler == R.SYMANTEC
        self.funcs: dict[int, Func] = {}
        thunks = set()
        raw = collections.defaultdict(list)   # function -> [(mnemonic, operands)]
        cur = None
        with open(self.b.dump("disasm_full.txt"), encoding="utf-8", errors="replace") as f:
            for line in f:
                m = FUNC.match(line)
                if m:
                    cur = int(m.group(2), 16)
                    self.funcs[cur] = Func(cur, m.group(1))
                    if m.group(1).startswith("thunk_"):
                        thunks.add(cur)
                    continue
                if line.startswith(";="):
                    cur = None
                    continue
                m = INSN.match(line)
                if m and cur is not None:
                    a = int(m.group(1), 16)
                    fn = self.funcs[cur]
                    fn.size = max(fn.size, a + len(m.group(2)) // 2 - cur)
                    raw[cur].append((m.group(3), m.group(4) or ""))
        self.starts = set(self.funcs) - thunks
        self.thunks = thunks
        # Data: a PE's non-code sections; an X-32 image's bytes from its lowest absolute memory
        # operand on, since code and data share one block there.
        if self.es1:
            lows = [int(v, 16) for ins in raw.values() for _mn, ops in ins
                    for v in re.findall(r"\[0x([0-9a-f]+)\]", ops) if int(v, 16) >= 0x1000]
            self.data_lo = min(lows)
            self.data_hi = self.img.sections[0][2]
            self.file_hi = len(self.img.data)
        else:
            data = [s for s in self.img.sections if s not in self.img.code]
            self.data_lo = min(s[1] for s in data)
            self.data_hi = max(s[1] + s[2] for s in data)
            self.code_lo = min(s[1] for s in self.img.code)
            self.code_hi = max(s[1] + s[2] for s in self.img.code)
        self.image_lo = self.img.sections[0][1] if self.es1 else self.img.base
        self.image_hi = self.data_hi
        for start, ins in raw.items():
            fn = self.funcs[start]
            for mn, ops in ins:
                if mn == "CALL":
                    m = CALL.match(ops)
                    if m:
                        t = self.canon(int(m.group(1), 16))
                        if t is not None:
                            fn.calls.append(t)
                    continue
                if mn == "RET":
                    fn.rets.add(int(ops, 16) if ops.startswith("0x") else 0)
                m = VSTORE.match(ops) if mn == "MOV" else None
                if m and self.is_data(int(m.group(1), 16)):
                    fn.vstores.append(int(m.group(1), 16))
                # Operand values outside brackets are immediates; an all-hex bracket is a global.
                for v in re.findall(r"\[0x([0-9a-f]+)\]", ops):
                    fn.refs.append(int(v, 16))
                bare = re.sub(r"\[[^\]]*\]", "", ops)
                for v in HEX.findall(bare):
                    v = int(v, 16)
                    if self.is_data(v):
                        fn.refs.append(v)
                    elif not self.is_address(v):
                        fn.imms.append(v)
        # A stored value is a vtable when its slots read as one; an ES1 table runs to the next.
        self.vt_starts = sorted({v for fn in self.funcs.values() for v in fn.vstores})
        self.vtables = {}
        for v in self.vt_starts:
            sl = self._slots(v)
            if any(x is not None for x in sl):
                self.vtables[v] = sl
        for fn in self.funcs.values():
            fn.vstores = [v for v in fn.vstores if v in self.vtables]
        self.callers = collections.defaultdict(set)
        for fn in self.funcs.values():
            for c in fn.calls:
                self.callers[c].add(fn.start)
        names = {int(e["address"], 16): e for e in es2_symbols.entries(bid) if e.get("type") == "function"}
        self.names = {a: e["name"] for a, e in names.items() if e.get("name")}
        self.runtime = {a for a, e in names.items() if e.get("source") == LIBMATCH_SOURCE} if self.es1 else set()

    # -- addresses -------------------------------------------------------------------------------
    def canon(self, a):
        """The function an address enters: an ES1 call or slot into the 0x90 padding before a
        prologue enters the function at the prologue (compiler.md#vtables)."""
        if a in self.starts:
            return a
        if self.es1:
            p = a
            while self.img.read(p, 1) == b"\x90" and p - a < 4:
                p += 1
            if p in self.starts and self.img.read(p, 3) == b"\x55\x8b\xec":
                return p
        return None

    def is_data(self, v):
        return self.data_lo <= v < self.data_hi

    def is_address(self, v):
        if self.es1:
            return self.canon(v) is not None or self.is_data(v)
        return self.image_lo <= v < self.image_hi

    def symbol(self, a):
        return self.names.get(a) or self.funcs[a].name

    def candidate(self, a):
        """Whether a function can take part in a pairing at all."""
        if a not in self.starts or a in self.runtime:
            return False
        return a < self.data_lo if self.es1 else self.code_lo <= a < self.code_hi

    # -- data ------------------------------------------------------------------------------------
    def is_code(self, v):
        return 0 < v < self.data_lo if self.es1 else self.code_lo <= v < self.code_hi

    def _slots(self, v):
        """The slots of the vtable at v: each the function it enters, or None for a zero slot or one
        entering code Ghidra made no function of (ES1's default methods without a prologue,
        `xor eax,eax; ret 4`, are such). ES1's tables open with a zero dword, take their slots from
        +4, hold 0 in a slot (compiler.md#vtables) and run to the next stored table; Borland's take
        their slots from +0 and end at the first value that is not code, or at the next block's
        class-record pointer, which reads as code and is followed by two zero dwords
        (borland-rtti.md#vtable-block)."""
        out = []
        if self.es1:
            if self.img.dword(v) != 0:
                return []
            i = self.vt_starts.index(v)
            end = self.vt_starts[i + 1] if i + 1 < len(self.vt_starts) else v + 0x100
            a = v + 4
            while a + 4 <= min(end, v + 0x100):
                d = self.img.dword(a)
                if d is None or not (d == 0 or self.is_code(d)):
                    break
                out.append(None if d == 0 else self.canon(d))
                a += 4
            while out and out[-1] is None and self.img.dword(v + 4 * len(out)) == 0:
                out.pop()
            return out
        a = v
        while True:
            d = self.img.dword(a)
            if d is None or not self.is_code(d):
                return out
            if d not in self.starts and self.img.dword(a + 4) == 0 and self.img.dword(a + 8) == 0:
                return out
            out.append(self.canon(d))
            a += 4

    def strings(self, minimum):
        """{address: text} of the NUL-terminated printable runs in the data."""
        out = {}
        for _n, va, _vs, ro, rs in self.img.sections:
            lo = max(va, self.data_lo)
            hi = min(va + rs, self.data_hi)
            if lo >= hi:
                continue
            chunk = self.img.data[ro + lo - va:ro + hi - va]
            for m in re.finditer(rb"[ -~\t\r\n]{%d,}\x00" % minimum, chunk):
                out[lo + m.start()] = m.group()[:-1].decode("latin1")
        return out

    def class_records(self):
        """{(tag, version, class name): factory} from the persistent-class tables: 12-byte records of
        tag word, version word, factory pointer and class-name pointer."""
        out = {}
        d = self.img.data
        for _n, va, _vs, ro, rs in self.img.sections:
            for off in range(ro, ro + rs - 12):
                tag, ver, fac, name = struct.unpack_from("<HHII", d, off)
                if not (0 < tag < 0x400 and 0 < ver < 0x400) or not self.is_data(name):
                    continue
                f = self.canon(fac)
                s = self.img.read(name, 40)
                m = re.match(rb"([A-Za-z_]\w{2,})\x00", s or b"")
                if f is not None and m:
                    out[(tag, ver, m.group(1).decode())] = f
        return out


# -- anchors -------------------------------------------------------------------------------------
def pow2ish(v):
    v &= 0xFFFFFFFF
    neg = (-v) & 0xFFFFFFFF
    return any(x and (x & (x - 1) == 0 or (x + 1) & x == 0) for x in (v, neg))


def anchors(p1: Program, p2: Program):
    """[(es1, es2, evidence)] proposals from the four anchor kinds."""
    out = []
    c1, c2 = p1.class_records(), p2.class_records()
    for key in sorted(set(c1) & set(c2)):
        out.append((c1[key], c2[key], {"kind": "class", "class": key[2], "tag": key[0], "version": key[1]}))

    def single(index1, index2, kind, show):
        for key in index1.keys() & index2.keys():
            f1, f2 = index1[key], index2[key]
            if len(f1) == 1 and len(f2) == 1:
                out.append((next(iter(f1)), next(iter(f2)), {"kind": kind, "value": show(key)}))

    def index(p, values):
        ix = collections.defaultdict(set)
        for fn in p.funcs.values():
            if p.candidate(fn.start):
                for v in values(fn):
                    ix[v].add(fn.start)
        return ix

    s1, s2 = p1.strings(STRING_MIN), p2.strings(STRING_MIN)
    single(index(p1, lambda fn: {s1[r] for r in fn.refs if r in s1}),
           index(p2, lambda fn: {s2[r] for r in fn.refs if r in s2}), "string", lambda s: s)

    def distinctive(v):
        sv = v - (1 << 32) if v & 0x80000000 else v
        return abs(sv) > SMALL and not pow2ish(v)
    single(index(p1, lambda fn: {v for v in fn.imms if distinctive(v)}),
           index(p2, lambda fn: {v for v in fn.imms if distinctive(v)}), "constant", lambda v: f"0x{v:x}")

    def blocks(p, strings):
        refs = sorted({r for fn in p.funcs.values() for r in fn.refs})
        nxt = {a: b for a, b in zip(refs, refs[1:])}
        textual = set()
        for a, s in strings.items():
            textual.update(range(a, a + len(s) + 1))

        def key(r):
            span = nxt.get(r, r + 16) - r
            n = 16 if span >= 16 else 8 if span >= 8 else 0
            b = p.img.read(r, n) if n else None
            if not b or r in textual or len(set(b)) < 4:
                return None
            return b
        return lambda fn: {k for k in (key(r) for r in fn.refs) if k}
    single(index(p1, blocks(p1, s1)), index(p2, blocks(p2, s2)), "data", lambda b: b.hex())
    return out


# -- matching ------------------------------------------------------------------------------------
class Matcher:
    def __init__(self, p1: Program, p2: Program):
        self.p1, self.p2 = p1, p2
        self.m12: dict[int, int] = {}
        self.m21: dict[int, int] = {}
        self.evidence = collections.defaultdict(list)   # (a, b) -> [evidence]
        self.hop: dict[tuple, int] = {}
        self.conflicts = []

    def accept_round(self, proposals, hop):
        """Take a round's proposals: a function proposed with two partners in the round, or with a
        partner other than its existing one, pairs with neither; evidence for an existing pair is
        added to it."""
        by1, by2 = collections.defaultdict(set), collections.defaultdict(set)
        fresh = []
        for a, b, ev in proposals:
            if not (self.p1.candidate(a) and self.p2.candidate(b)):
                continue
            if self.m12.get(a) == b:
                if ev not in self.evidence[(a, b)]:
                    self.evidence[(a, b)].append(ev)
                continue
            if a in self.m12 or b in self.m21:
                self.conflicts.append({"es1": a, "es2": b, "evidence": ev,
                                       "es1_paired_with": self.m12.get(a), "es2_paired_with": self.m21.get(b)})
                continue
            fresh.append((a, b, ev))
            by1[a].add(b)
            by2[b].add(a)
        made = []
        for a, b, ev in fresh:
            if len(by1[a]) > 1 or len(by2[b]) > 1:
                self.conflicts.append({"es1": a, "es2": b, "evidence": ev, "rivals_es2": sorted(by1[a] - {b}),
                                       "rivals_es1": sorted(by2[b] - {a})})
                continue
            if (a, b) not in self.hop:
                self.m12[a], self.m21[b] = b, a
                self.hop[(a, b)] = hop
                made.append((a, b))
            self.evidence[(a, b)].append(ev)
        return made

    def callees(self, p, a):
        return [c for c in p.funcs[a].calls if p.candidate(c)]

    def propose(self, a, b):
        p1, p2 = self.p1, self.p2
        via = f"{a:08x}={b:08x}"
        out = []
        # Calls, aligned on the callees already paired (longest common subsequence).
        s1, s2 = self.callees(p1, a), self.callees(p2, b)
        n, m = len(s1), len(s2)
        if n and m and n * m <= 40000:
            L = [[0] * (m + 1) for _ in range(n + 1)]
            for i in range(n - 1, -1, -1):
                for j in range(m - 1, -1, -1):
                    L[i][j] = L[i + 1][j + 1] + 1 if self.m12.get(s1[i]) == s2[j] else max(L[i + 1][j], L[i][j + 1])
            i = j = 0
            cuts = []
            while i < n and j < m:
                if self.m12.get(s1[i]) == s2[j]:
                    cuts.append((i, j))
                    i += 1
                    j += 1
                elif L[i + 1][j] >= L[i][j + 1]:
                    i += 1
                else:
                    j += 1
            cuts.append((n, m))
            pi = pj = 0
            for gi, (ci, cj) in enumerate(cuts):
                u1 = {c for c in s1[pi:ci] if c not in self.m12}
                u2 = {c for c in s2[pj:cj] if c not in self.m21}
                if len(u1) == 1 and len(u2) == 1:
                    out.append((u1.pop(), u2.pop(), {"kind": "callee", "via": via, "gap": gi}))
                pi, pj = ci + 1, cj + 1
        # Callers.
        k1 = {c for c in p1.callers.get(a, ()) if p1.candidate(c) and c not in self.m12}
        k2 = {c for c in p2.callers.get(b, ()) if p2.candidate(c) and c not in self.m21}
        if len(k1) == 1 and len(k2) == 1:
            out.append((k1.pop(), k2.pop(), {"kind": "caller", "via": via}))
        # Vtables.
        v1, v2 = p1.funcs[a].vstores, p2.funcs[b].vstores
        if v1 and len(v1) == len(v2):
            for t1, t2 in zip(v1, v2):
                sl1, sl2 = p1.vtables[t1], p2.vtables[t2]
                if len(sl1) == len(sl2):
                    for k, (x, y) in enumerate(zip(sl1, sl2)):
                        out.append((x, y, {"kind": "vtable", "via": via, "es1_vtable": f"{t1:08x}",
                                           "es2_vtable": f"{t2:08x}", "slot": k}))
        return out

    def run(self):
        frontier = self.accept_round(anchors(self.p1, self.p2), 0)
        hop = 0
        while frontier:
            hop += 1
            props = [x for a, b in frontier for x in self.propose(a, b)]
            frontier = self.accept_round(props, hop)
        return self


# -- modules -------------------------------------------------------------------------------------
def modules(p: Program):
    """{function: module label}, the module names in order, and the functions whose evidence the
    fit overrules.

    Only the game objects are fitted: they lie below `__cinit`, which opens the library region,
    where the C runtime, the `util` modules and the class libraries are not laid out one object
    per path (compiler.md#libraries-against-game-code). The library region is one label.

    The assert path strings anchor each module in the initialised data. Every initialised data
    address a function references reads as the modules of the path strings on either side of it,
    and a function's evidence is what all its references agree on (their hull when a global of
    another module makes them disagree). A monotone fit over the code order, missing the fewest
    functions' evidence, gives each function every module some optimal fit assigns it: one is its
    module, a range a margin. Uninitialised data has no path string to read it by."""
    cinit = next(a for a, n in p.names.items() if n == "__cinit")
    paths = sorted((a, s) for a, s in p.strings(3).items()
                   if re.search(r"\.(cpp|c|asm)$", s, re.I) and "\\util\\" not in s.lower())
    order = []
    path_anchors = []
    for a, s in paths:
        name = s.split("\\")[-1].lower()
        if not order or order[-1] != name:
            if name in order:
                raise SystemExit(f"{p.id}: {name}'s path strings are not contiguous in the data")
            order.append(name)
        path_anchors.append((a, len(order) - 1))
    k = len(order)
    names = ["(before " + order[0] + ")"] + order + ["(after " + order[-1] + ")"]
    lo_data = path_anchors[0][0] - 0x1000
    fns = sorted(a for a in p.starts if a < cinit)
    refs = {a: sorted({r for r in p.funcs[a].refs if lo_data <= r < p.data_hi}) for a in fns}
    states = list(range(-1, k + 1))
    ks = [x[0] for x in path_anchors]

    def interval(r):
        if r >= p.file_hi:
            return (-1, k)
        i = bisect.bisect_left(ks, r)
        if i < len(ks) and ks[i] == r:
            return (path_anchors[i][1], path_anchors[i][1])
        return (path_anchors[i - 1][1] if i else -1, path_anchors[i][1] if i < len(ks) else k)
    ev = {}
    for a in fns:
        ivs = [interval(r) for r in refs[a]]
        ivs = [iv for iv in ivs if iv != (-1, k)]
        if ivs:
            lo, hi = max(i[0] for i in ivs), min(i[1] for i in ivs)
            ev[a] = (lo, hi) if lo <= hi else (min(i[0] for i in ivs), max(i[1] for i in ivs))

    def miss(a, s):
        return int(a in ev and not (ev[a][0] <= s <= ev[a][1]))
    n = len(fns)
    fwd = [[0] * len(states) for _ in range(n)]
    for i, a in enumerate(fns):
        best = 1 << 30
        for si, s in enumerate(states):
            best = min(best, fwd[i - 1][si] if i else 0)
            fwd[i][si] = best + miss(a, s)
    bwd = [[0] * len(states) for _ in range(n)]
    for i in range(n - 1, -1, -1):
        best = 1 << 30
        for si in range(len(states) - 1, -1, -1):
            best = min(best, bwd[i + 1][si] if i + 1 < n else 0)
            bwd[i][si] = best + miss(fns[i], states[si])
    opt = min(fwd[-1])
    span, violations = {}, []
    for i, a in enumerate(fns):
        ok = [s for si, s in enumerate(states) if fwd[i][si] + bwd[i][si] - miss(a, s) == opt]
        span[a] = (min(ok), max(ok))
        if a in ev and all(miss(a, s) for s in ok):
            violations.append(a)
    label = {a: names[span[a][0] + 1] if span[a][0] == span[a][1]
             else f"{names[span[a][0] + 1]} | {names[span[a][1] + 1]}" for a in fns}
    label.update({a: "(library region)" for a in p.starts if cinit <= a < p.data_lo})
    return label, names, violations


# -- report --------------------------------------------------------------------------------------
def plausibility(p1, p2, a, b):
    f1, f2 = p1.funcs[a], p2.funcs[b]
    return {"es1_bytes": f1.size, "es2_bytes": f2.size,
            "es1_calls": len(set(f1.calls)), "es2_calls": len(set(f2.calls)),
            "es1_ret": sorted(f1.rets), "es2_ret": sorted(f2.rets)}


def run(bid, targets, args):
    p1 = Program(bid)
    label, names, violations = modules(p1)
    results = {}
    for t in targets:
        p2 = Program(t)
        results[t] = (p2, Matcher(p1, p2).run())
    rows = []
    for a in sorted(x for x in p1.starts if x < p1.data_lo):
        ms = []
        for t, (p2, mt) in results.items():
            b = mt.m12.get(a)
            if b is None:
                continue
            ms.append({"binary": t, "address": f"{b:08x}", "symbol": p2.symbol(b),
                       "named": b in p2.names, "hop": mt.hop[(a, b)],
                       "evidence": mt.evidence[(a, b)], "plausibility": plausibility(p1, p2, a, b)})
        rows.append({"es1": f"{a:08x}", "es1_name": p1.symbol(a), "module": label[a],
                     "runtime": a in p1.runtime, "candidates": ms})
    conflicts = {t: [{k: (f"{v:08x}" if isinstance(v, int) else v) for k, v in c.items()} for c in mt.conflicts]
                 for t, (_p2, mt) in results.items()}

    # Per-module table.
    order = []
    for r in rows:
        if r["module"] not in order:
            order.append(r["module"])
    print(f"\n{bid}: {len(rows)} functions below the data; module order from the assert paths: "
          f"{', '.join(names[1:-1])}; {len(violations)} functions' string literals fall outside the fitted order")
    head = f"  {'module':34} {'fns':>5} {'rt':>4} " + " ".join(
        f"{t + ' any':>10} {'anch':>5} {'named':>6}" for t in targets) + f" {'either':>7}"
    print(head)
    tot = collections.Counter()
    for mod in order:
        rs = [r for r in rows if r["module"] == mod]
        c = collections.Counter()
        c["fns"], c["rt"] = len(rs), sum(r["runtime"] for r in rs)
        for r in rs:
            byt = {m["binary"]: m for m in r["candidates"]}
            for t in targets:
                if t in byt:
                    c[t] += 1
                    c[t + "a"] += byt[t]["hop"] == 0
                    c[t + "n"] += byt[t]["named"]
            c["either"] += bool(byt)
        tot.update(c)
        print(f"  {mod:34} {c['fns']:5} {c['rt']:4} " + " ".join(
            f"{c[t]:10} {c[t + 'a']:5} {c[t + 'n']:6}" for t in targets) + f" {c['either']:7}")
    print(f"  {'total':34} {tot['fns']:5} {tot['rt']:4} " + " ".join(
        f"{tot[t]:10} {tot[t + 'a']:5} {tot[t + 'n']:6}" for t in targets) + f" {tot['either']:7}")
    for t, (_p2, mt) in results.items():
        kinds = collections.Counter(e["kind"] for evs in mt.evidence.values() for e in evs)
        hops = collections.Counter(mt.hop.values())
        print(f"  {t}: {len(mt.m12)} pairs; evidence {dict(kinds)}; by hop {dict(sorted(hops.items()))}; "
              f"{len(mt.conflicts)} conflicting proposals")

    if args.list:
        for r in rows:
            for m in r["candidates"]:
                if args.target and m["binary"] not in args.target:
                    continue
                kinds = ",".join(sorted({e["kind"] for e in m["evidence"]}))
                print(f"  {r['es1']} {r['module']:20} -> {m['binary']} {m['address']} {m['symbol']:40} "
                      f"hop {m['hop']} [{kinds}]")
    for want in args.show or []:
        a = want.lower().zfill(8)
        for r in rows:
            if r["es1"] == a:
                print(json.dumps(r, indent=1))
        for t, cs in conflicts.items():
            for c in cs:
                if c["es1"] == a:
                    print(t, "conflict:", json.dumps(c))
    if not args.no_write:
        out = p1.b.dump("es2_candidates.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump({"binary": bid, "targets": targets,
                       "note": "Candidates are hypotheses, not names: Herculan/docs/herculan/plan-es1-support.md step 1.2.",
                       "functions": rows, "conflicts": conflicts}, f, indent=1)
        print(f"  wrote {out}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--binary", type=str.upper, action="append", choices=("ES1DBSIM", "ES1GO", "ES1ESCD", "ES1PLAYFLIC"),
                    help="pair only this binary (repeatable; default ES1DBSIM and ES1GO)")
    ap.add_argument("--target", type=str.upper, action="append", choices=("DBSIM", "VSHELL"))
    ap.add_argument("--list", action="store_true", help="print every candidate")
    ap.add_argument("--show", action="append", metavar="ADDR", help="print one ES1 function's record and conflicts")
    ap.add_argument("--no-write", action="store_true")
    a = ap.parse_args()
    for bid in a.binary or ("ES1DBSIM", "ES1GO"):
        R.require(bid, "es1_crossmatch.py", "it pairs ES1's Symantec C++ code with ES2's", compiler=R.SYMANTEC)
        run(bid, a.target or ["DBSIM", "VSHELL"], a)
    return 0


if __name__ == "__main__":
    sys.exit(main())
