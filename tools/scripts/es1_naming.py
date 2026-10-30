#!/usr/bin/env python3
"""Naming helpers for ES1's Symantec C++ X-32 images (ES1DBSIM, ES1GO, ES1ESCD), beside es2_naming.py, whose
Borland-only subcommands (rtti, vtables, classes, stats) refuse them.

  vtables BIN [vtable|function|name-regex ...] [--unnamed]
      Every vtable the code stores into an object: the immediate of a MOV dword ptr [...],imm in the
      disasm dump, in the image's data, whose dword is 0 and whose next dword is a code address
      (Herculan/docs/es1/formats/compiler.md#vtables). Slots run from +4 to the first dword that is 0
      or outside the code; a slot on the 0x90 padding before a function is shown as the function.
      Each table lists the functions that store it (constructors and destructors) and each slot,
      marked '*' unnamed, 'L' low entry, 'S' stamped, '?' no Ghidra function (a frameless stub or a
      this-adjusting thunk Ghidra never made one of), with the number of tables sharing it (xN: a
      base-class method). Arguments filter by table address, by a function in a slot or storing the
      table, or by a regex over their names. A table stored only from bytes Ghidra never disassembled
      is not found, and a data block shaped like a table is listed as one.

  twin BIN BIN2 (addr[+addr...] | batch.json | a=b | a=b!)... --out twins.json [--by-order]
      For each named BIN function or data entry, its twin in BIN2, written as an insert batch:
        1. exact: BIN2's one unnamed function whose disassembly, address operands masked, equals
           it (es2_naming.py match's insn_streams);
        2. vtable: for a pair of X-32 images, the same slot of the BIN2 vtable whose other slots pair
           with this one's, when exact matching finds none or several;
        3. order: the function as far past the nearest pair before it, or as far short of the
           nearest pair after it, as in BIN (padding thunks aside), taken only when its decompile
           agrees; each round's pairs anchor the next;
        4. with --by-order, for code compiled differently (ES1GO's device code has no 0x90 padding):
           the function at the same position whatever its decompile, between the two nearest pairs
           when as many functions lie between them in both binaries, and otherwise counted from the
           pair before (or back from the pair after), labelled as a guess; every such pair is held
           with its diff, to accept with a=b!;
        a=b: given, for what those miss (ES1GO's game code is laid out apart from ES1DBSIM's).
      A batch.json's BIN entries count as named whether or not they are inserted yet, so a batch's
      twins can be prepared from its own JSON before any known_symbols write.
      An exact pair is held when one of its operands maps elsewhere than that address's name in
      both binaries, or, unnamed, than the majority of the other equal-code pairs (those already
      named alike included) send it: equal masked code is not enough for a lone load-and-return.
      Every pair is diffed with es2_naming.py diff's normalisation, BIN2's labels named through the
      pairs found. A pair whose decompiles differ is held back with the diff command to read; after
      reading it, a=b! accepts it as the same statements. An accepted pair gets BIN's description
      with BIN2's addresses: those its operands (exact) or its decompile's labels, in order, pair
      with, any function this run pairs and does not hold, and any BIN address already named alike
      in BIN2; an address tagged with another binary
      ("VSHELL 00405739", "VSHELL DVJoystick_TrackMax (00405b5d)") stays. A final "ES2's ..." sentence is dropped
      and the description closes with "The code of BIN's Name (addr), relocated operands aside."
      (disassembly equal) or "; the same statements as BIN's Name (addr) in different code." An entry
      whose description still names a BIN address, or whose twin is already named otherwise, goes
      to twins.held.json with its generated description for a hand fix. Run es2_naming.py insert on
      the output, then es2_stamp.py stamp with the printed address list.
"""

from __future__ import annotations

import bisect
import collections
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_naming as N  # noqa: E402
import retail_binaries  # noqa: E402

STORE = re.compile(r"^([0-9a-f]{8})\s+[0-9a-f]+\s+MOV dword ptr \[[^\]]*\],0x([0-9a-f]+)\s*$")
HEX8 = re.compile(r"(?<![0-9A-Za-z_+])([0-9a-f]{8})(?![0-9A-Za-z_])")
# The text before an address that names its binary: "VSHELL 00405739", "(VSHELL 00405739)",
# "VSHELL's 00405739", and with a name between, "VSHELL DVJoystick_TrackMax (00405b5d)".
TAGGED = re.compile(r"\b(DBSIM|VSHELL|ES1DBSIM|ES1GO|ES1ESCD)(?:'s)? (?:[A-Za-z_][\w:~]* \()?$")


def x32(binary):
    return retail_binaries.require(binary, "es1_naming.py", "it reads X-32 images' vtables and padding entries",
                                   container=retail_binaries.X32)


def entry_map(binary, image, fns):
    """{every address that enters a Ghidra function: the function's prologue or start}. The 0x90 run
    before a function enters it whether or not the function opens with a prologue: a frameless stub
    such as StreamIO_Flush is entered through its padding too."""
    out = {}
    for f in fns:
        v = int(f, 16)
        target = N.x32_function_of(image, v)
        for e in image.entries_of(v):
            out.setdefault(e, target)
        p = v
        while p > v - 16 and image.read(p - 1, 1) == b"\x90":
            p -= 1
            out.setdefault(p, target)
    return out


def find_vtables(binary, zero_led=False):
    """({vt: [slot function, from +4]}, {vt: [store addresses]}). With zero_led, a stored table whose
    first slots are 0 (pure virtuals, as DRAWABLE's in the ES1 simulator) is kept too, those slots as
    0; otherwise a table ends at its first zero slot and one that starts with a zero is skipped."""
    b = x32(binary)
    image = b.image()
    fns = N.functions(binary)
    enters = entry_map(binary, image, fns)
    stores = {}
    with open(b.dump("disasm_full.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            if "MOV dword ptr [" not in line:
                continue
            m = STORE.match(line)
            if m:
                stores.setdefault(int(m.group(2), 16), []).append(int(m.group(1), 16))
    code_end = N.x32_data_start(b.dump("disasm_full.txt"), image.sections[0][2])
    tables = {}
    for v in stores:
        if v < code_end or image.dword(v) != 0:
            continue
        slots = []
        while zero_led and len(slots) < 8 and image.dword(v + 4 + 4 * len(slots)) == 0:
            slots.append(0)
        lead = len(slots)
        while len(slots) < 128:
            w = image.dword(v + 4 + 4 * len(slots))
            if not w or w >= code_end:
                break
            slots.append(enters.get(w, w))
        if len(slots) > lead:
            tables[v] = slots
    return tables, {v: stores[v] for v in tables}, enters


def is_padding_thunk(image, a):
    """A Ghidra function started on the 0x90 run before a prologue."""
    return image.read(a, 1) == b"\x90" and image.entries_of(a)[-1] != a


def cmd_vtables(args):
    binary = args[0]
    only_unnamed = "--unnamed" in args
    filters = [x for x in args[1:] if not x.startswith("-")]
    known = N.names(binary)
    es = {e["address"]: e for e in N.entries(binary)}
    ext = N.Extents(binary)
    tables, stores, enters = find_vtables(binary)
    starts = set(enters.values())
    share = {}
    for slots in tables.values():
        for fn in set(slots):
            share[fn] = share.get(fn, 0) + 1
    shown = 0
    for vt in sorted(tables):
        slots = tables[vt]
        inst = sorted({ext.containing(s)[0] for s in stores[vt] if ext.containing(s)})
        hexes = {f"{x:08x}" for x in slots + inst} | {f"{vt:08x}"}
        if filters and not any(f.lower().zfill(8) in hexes
                               or any(re.search(f, known.get(f"{x:08x}", ""), re.I) for x in slots + inst)
                               for f in filters):
            continue
        miss = [x for x in slots if f"{x:08x}" not in known]
        if only_unnamed and not miss:
            continue
        shown += 1
        print(f"=== {vt:08x}  ({len(slots)} slots, {len(set(miss))} unnamed)")
        print("  stored by: " + ", ".join(f"{a:08x} {known.get(f'{a:08x}', '*')}" for a in inst))
        for i, x in enumerate(slots):
            h = f"{x:08x}"
            e = es.get(h)
            mark = ("S" if e and e.get("verified") else " " if h in known else "L" if e
                    else "*" if x in starts else "?")
            print(f"  +0x{4 + 4 * i:02x} {mark} {h} {known.get(h, '')}  x{share[x]}")
    print(f"\n{shown} of {len(tables)} tables")


def label_addresses(binary, addr, known):
    """The addresses a function's decompile names, in order: its FUN_/DAT_/PTR_ labels and the known
    names it uses, its own name aside."""
    body = N.bodies(binary).get(addr)
    if body is None:
        return None
    t = N.compact(body, {})
    by_name = {n: a for a, n in known.items() if a != addr}
    alts = [r"\b(?:thunk_)?(?:FUN|DAT|PTR|LAB|_DAT)_([0-9a-f]{8})\b"]
    if by_name:
        alts.append(r"\b(" + "|".join(re.escape(n) for n in sorted(by_name, key=len, reverse=True)) + r")\b")
    out = []
    for m in re.finditer("|".join(alts), t):
        out.append(m.group(1) if m.group(1) else by_name[m.group(2)])
    return out


def ordered(binary, image):
    """The binary's function entries in address order, padding thunks left out."""
    return [a for a in sorted(N.functions(binary)) if not is_padding_thunk(image, int(a, 16))]


def diff_status(b1, a1, b2, a2, known2=None):
    """'identical' or 'differs' (with the changed line count) for two decompiles, BIN2's labels named
    through known2 when given (its names plus BIN's for the addresses paired so far)."""
    t1, t2 = N.normalised(b1, a1), N.normalised(b2, a2, known2)
    if t1 is None or t2 is None:
        return "missing", 0
    if t1 == t2 or "".join(t1.split()) == "".join(t2.split()):
        return "identical", len(t1.splitlines())
    import difflib
    d = [l for l in difflib.unified_diff(t1.splitlines(), t2.splitlines(), lineterm="", n=0)
         if l[:1] in "+-" and not l.startswith(("+++", "---"))]
    return "differs", len(d)


def pair_vtables(t1, t2, pairs, k1, k2):
    """{BIN vt: BIN2 vt} for tables of one length whose slots pair best: a slot pair counts when the
    two functions are already paired or carry the same name."""
    out = {}
    by_len = {}
    for v, s in t2.items():
        by_len.setdefault(len(s), []).append(v)
    for v, s in t1.items():
        best, score = None, 0
        for w in by_len.get(len(s), []):
            n = sum(1 for x, y in zip(s, t2[w])
                    if pairs.get(f"{x:08x}") == f"{y:08x}"
                    or (k1.get(f"{x:08x}") and k1.get(f"{x:08x}") == k2.get(f"{y:08x}")))
            if n > score:
                best, score = w, n
        if best is not None:
            out[v] = best
    return out


def targets_of(args, binary):
    out = []
    for x in args:
        if x.endswith(".json"):
            with open(x, encoding="utf-8") as f:
                out += [e["address"] for e in json.load(f) if e.get("binary", binary) == binary]
        else:
            out += [a.lower().zfill(8) for a in x.split("+") if a]
    return out


def cmd_twin(args):
    b1, b2 = args[0], args[1]
    out_path = args[args.index("--out") + 1] if "--out" in args else None
    rest = [x for i, x in enumerate(args[2:], 2)
            if not x.startswith("--") and args[i - 1] != "--out" and "=" not in x]
    if not out_path:
        raise SystemExit("twin needs --out FILE for the BIN2 batch")
    for x in rest:
        if x.endswith(".json"):
            with open(x, encoding="utf-8") as f:
                N.add_pending([e for e in json.load(f) if e.get("binary", b1).upper() == b1.upper()], b1)
    es1 = {e["address"]: e for e in N.entries(b1)}
    k1, k2 = N.names(b1), N.names(b2)
    by_name2 = {n: a for a, n in k2.items()}
    targets = targets_of(rest, b1)
    for a in targets:
        if a not in k1:
            raise SystemExit(f"{a} has no name in {b1}")
    s1, s2 = N.insn_streams(b1), N.insn_streams(b2)
    by_key = {}
    for a, ins in s2.items():
        by_key.setdefault(tuple(i[1] for i in ins), []).append(a)
    pairs, how, addr_map = {}, {}, {}
    accepted = set()
    for x in args:
        if "=" in x and not x.startswith("--"):
            p, q = x.split("=")
            p, q = p.lower().zfill(8), q.rstrip("!").lower().zfill(8)
            pairs[p], how[p] = q, "given"
            if x.endswith("!"):
                accepted.add(p)
            if p not in targets:
                targets.append(p)

    def operand_map(a, b):
        for (_, _, v1), (_, _, v2) in zip(s1[a], s2[b]):
            for p, q in zip(v1, v2):
                addr_map.setdefault(p, q)

    def operand_pairs(a, b):
        return [(p, q) for (_, _, v1), (_, _, v2) in zip(s1[a], s2[b]) for p, q in zip(v1, v2)]

    fn_targets = [a for a in targets if es1[a]["type"] == "function"]
    # 1. exact. Every equal-code pair votes on where its operands go in BIN2; a pair named alike in
    # both votes too, and a name already in both settles the address. A new pair that sends an
    # operand elsewhere than the settled or majority answer is held: two lone load-and-returns of
    # different globals have equal masked code.
    votes, voters, conflicts = {}, {}, {}
    for a in fn_targets:
        if k1[a] in by_name2:
            b = by_name2[k1[a]]
            if a in s1 and b in s2 and [i[1] for i in s1[a]] == [i[1] for i in s2[b]]:
                voters[a] = b
            continue
        cands = [x for x in by_key.get(tuple(i[1] for i in s1.get(a, [])), []) if x not in k2] if a in s1 else []
        if a in pairs:
            if pairs[a] in cands:
                voters[a] = pairs[a]
            continue
        if len(cands) == 1:
            pairs[a], how[a] = cands[0], "exact"
            voters[a] = cands[0]
    for a, b in voters.items():
        for p, q in operand_pairs(a, b):
            votes.setdefault(p, collections.Counter())[q] += 1
    settled = {}
    for p, c in votes.items():
        if p in k1 and k1[p] in by_name2:
            settled[p] = by_name2[k1[p]]
        else:
            top = c.most_common(2)
            if len(top) == 1 or top[0][1] > top[1][1]:
                settled[p] = top[0][0]
    for a, b in voters.items():
        bad = [(p, q) for p, q in operand_pairs(a, b) if p in settled and settled[p] != q]
        if bad and how.get(a) == "exact" and a not in accepted:
            p, q = bad[0]
            conflicts[a] = (f"{a} {k1[a]} -> {b} (exact): its operand {p} is {q} here, but "
                            f"{settled[p]} by {'its name' if p in k1 and k1[p] in by_name2 else 'the other pairs'}; "
                            f"pass {a}={b}! only if the code is the twin after all")
            del pairs[a], how[a]
            continue
        for p, q in operand_pairs(a, b):
            addr_map.setdefault(p, settled.get(p, q))
    # anchors: everything named alike in both, plus this run's pairs
    anchors = {a: by_name2[n] for a, n in k1.items() if n in by_name2}
    anchors.update(pairs)
    # 2. vtable, for an X-32 pair
    both_x32 = all(retail_binaries.get(b).container == retail_binaries.X32 for b in (b1, b2))
    if both_x32:
        t1 = find_vtables(b1)[0]
        t2 = find_vtables(b2)[0]
        vmap = pair_vtables(t1, t2, anchors, k1, k2)
        for a in fn_targets:
            if a in pairs or k1[a] in by_name2 or a in conflicts:
                continue
            exact = set(by_key.get(tuple(i[1] for i in s1.get(a, [])), []))
            for v, s in t1.items():
                if v in vmap and int(a, 16) in s:
                    c = f"{t2[vmap[v]][s.index(int(a, 16))]:08x}"
                    if c not in k2 and (not exact or c in exact):
                        pairs[a], how[a] = c, f"vtable {v:08x}/{vmap[v]:08x}"
                        if c in exact:
                            operand_map(a, c)
                        break
        anchors.update(pairs)
    def known2():
        """BIN2's names, plus BIN's name on every BIN2 address paired so far."""
        k = dict(k2)
        for p, q in addr_map.items():
            if p in k1 and q not in k:
                k[q] = k1[p]
        for p, q in pairs.items():
            k.setdefault(q, k1[p])
        return k

    # 3. order: counted from the nearest pair before, or back from the nearest pair after; a
    # candidate is taken only when its decompile agrees, and new pairs anchor the next round.
    img1, img2 = retail_binaries.Image(b1), retail_binaries.Image(b2)
    o1, o2 = ordered(b1, img1), ordered(b2, img2)
    i1, i2 = {a: i for i, a in enumerate(o1)}, {a: i for i, a in enumerate(o2)}
    taken = set(pairs.values())
    while True:
        anc = sorted((a, b) for a, b in anchors.items() if a in i1 and b in i2)
        anc_keys = [a for a, _ in anc]
        found = 0
        for a in fn_targets:
            if a in pairs or k1[a] in by_name2 or a not in i1 or a in conflicts:
                continue
            j = bisect.bisect_left(anc_keys, a)
            tries = []
            if j > 0:
                lo1, lo2 = anc[j - 1]
                tries.append((i2[lo2] + i1[a] - i1[lo1], f"order after {k1.get(lo1, lo1)}"))
            if j < len(anc):
                hi1, hi2 = anc[j]
                tries.append((i2[hi2] - (i1[hi1] - i1[a]), f"order before {k1.get(hi1, hi1)}"))
            kk = known2()
            for pos, label in tries:
                if not 0 <= pos < len(o2):
                    continue
                c = o2[pos]
                if c in k2 or c in taken:
                    continue
                if diff_status(b1, a, b2, c, kk)[0] == "identical":
                    pairs[a], how[a] = c, label
                    anchors[a] = c
                    taken.add(c)
                    found += 1
                    break
        if not found:
            break
    # 4. --by-order, for what step 3 left: the function at the same position whatever its decompile.
    # Between two anchors whose intervals hold as many functions in both binaries, that position is
    # the twin's unless the code was reordered; where the counts differ (BIN2's code laid out apart, or
    # a function without a Ghidra function on one side) or one anchor is missing, the count from the
    # anchor before, else back from the one after, is only a guess. A pair whose decompile differs is
    # held below with its diff and taken only through a=b!; one that comes out identical once the
    # other pairs name its labels is accepted like step 3's.
    if "--by-order" in args:
        anc = sorted((a, b) for a, b in anchors.items() if a in i1 and b in i2)
        anc_keys = [a for a, _ in anc]
        for a in fn_targets:
            if a in pairs or k1[a] in by_name2 or a not in i1 or a in conflicts:
                continue
            j = bisect.bisect_left(anc_keys, a)
            lo = anc[j - 1] if j > 0 else None
            hi = anc[j] if j < len(anc) else None
            tries = []
            if lo and hi and i1[hi[0]] - i1[lo[0]] == i2[hi[1]] - i2[lo[1]]:
                tries.append((i2[lo[1]] + i1[a] - i1[lo[0]],
                              f"by order between {k1.get(lo[0], lo[0])} and {k1.get(hi[0], hi[0])}"))
            else:
                odd = " (the interval's lengths differ)" if lo and hi else ""
                if lo:
                    tries.append((i2[lo[1]] + i1[a] - i1[lo[0]], f"by order after {k1.get(lo[0], lo[0])}{odd}"))
                if hi:
                    tries.append((i2[hi[1]] - (i1[hi[0]] - i1[a]), f"by order before {k1.get(hi[0], hi[0])}{odd}"))
            for pos, label in tries:
                if 0 <= pos < len(o2) and o2[pos] not in k2 and o2[pos] not in taken:
                    pairs[a], how[a] = o2[pos], label
                    taken.add(o2[pos])
                    break
    # decompile label pairing for the pairs operands did not map
    batch, held, stamp = [], [], []
    for a in targets:
        e = es1[a]
        if e["type"] != "function":
            continue
        if k1[a] in by_name2:
            print(f"{a} {k1[a]}: already named in {b2} at {by_name2[k1[a]]}")
            continue
        if a in conflicts:
            held.append(conflicts[a])
            continue
        if a not in pairs:
            held.append(f"{a} {k1[a]}: no twin found")
            continue
        b = pairs[a]
        st, n = diff_status(b1, a, b2, b, known2())
        if st != "identical" and a not in accepted:
            held.append(f"{a} {k1[a]} -> {b} ({how[a]}): decompiles {st} ({n} lines); read "
                        f"es2_naming.py diff {b1} {a} {b2} {b}, and pass {a}={b}! when they are the same statements")
            continue
        if st != "identical":
            continue
        l1, l2 = label_addresses(b1, a, k1), label_addresses(b2, b, k2)
        if l1 and l2 and len(l1) == len(l2):
            for p, q in zip(l1, l2):
                addr_map.setdefault(p, q)
    unpaired = {h.split()[0] for h in held}
    side = []
    for a in targets:
        e = es1[a]
        if e["type"] == "function" and (k1[a] in by_name2 or a not in pairs or a in unpaired):
            continue
        if e["type"] == "function":
            b = pairs[a]
            exact = a in s1 and b in s2 and [i[1] for i in s1[a]] == [i[1] for i in s2[b]]
        else:
            if k1[a] in by_name2:
                print(f"{a} {k1[a]}: already named in {b2} at {by_name2[k1[a]]}")
                continue
            b = addr_map.get(a)
            if not b:
                held.append(f"{a} {k1[a]} (data): no operand or label of a paired function maps it")
                continue
            exact = True
        d = re.sub(r"\s*ES2's [^.]*\([^)]*\)[^.]*\.$", "", e.get("description", "")).rstrip()
        unmapped = []

        def sub(m, d=d):
            x = m.group(1)
            tag = TAGGED.search(d[:m.start()])
            # Another binary's address, or BIN2's own, stays; one tagged BIN is mapped like an untagged one.
            if (tag and tag.group(1) != b1.upper()) or x == a:
                return x
            if x in addr_map:
                return addr_map[x]
            if x in pairs and x not in unpaired:
                return pairs[x]
            if x in k1 and k1[x] in by_name2:
                return by_name2[k1[x]]
            unmapped.append(x)
            return x
        d = HEX8.sub(sub, d)
        if e["type"] == "function" and exact:
            d += f" The code of {b1}'s {k1[a]} ({a}), relocated operands aside."
        elif e["type"] == "function":
            d = d.rstrip(".") + f"; the same statements as {b1}'s {k1[a]} ({a}) in different code."
        else:
            d += f" {b1}'s {k1[a]} is at {a}."
        src = e.get("source", "")
        entry = {"address": b, "binary": b2, "type": e["type"], "confidence": e["confidence"],
                 "name": k1[a], "description": d,
                 "source": src if src.startswith("Herculan/") else f"{b2} decompile at {b}"}
        if unmapped or b in k2:
            held.append(f"{a} {k1[a]} -> {b}: " + (f"already named {k2[b]} in {b2}" if b in k2 else
                        f"description names {', '.join(sorted(set(unmapped)))}, which no pairing maps into {b2}; "
                        "fix it in the .held.json"))
            side.append(entry)
            continue
        batch.append(entry)
        if e["type"] == "function":
            stamp.append(b)
        print(f"{a} {k1[a]} -> {b}  {how.get(a, 'data')}")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(sorted(batch, key=lambda x: x["address"]), f, indent=2)
    if side:
        side_path = re.sub(r"(\.json)?$", ".held.json", out_path, count=1)
        with open(side_path, "w", encoding="utf-8") as f:
            json.dump(sorted(side, key=lambda x: x["address"]), f, indent=2)
        print(f"{len(side)} held entries with their generated descriptions in {side_path}")
    for h in held:
        print("HELD  " + h)
    print(f"\n{len(batch)} twins written to {out_path}, {len(held)} held back")
    if batch:
        print(f"next: es2_naming.py insert {out_path} --write")
    if stamp:
        print(f"      es2_stamp.py stamp {'+'.join(sorted(stamp))} --binary {b2} --via decompile")


COMMANDS = {"vtables": cmd_vtables, "twin": cmd_twin}

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)
    COMMANDS[sys.argv[1]](sys.argv[2:])
