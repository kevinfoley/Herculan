#!/usr/bin/env python3
"""Match Symantec C++ 6.1's DOS-extender runtime libraries against the ES1 images, function by function.

ES1's executables link a later revision of Symantec C++'s X-32 runtime than the 6.1 release on the
CD in `Reference/` (Herculan/docs/es1/formats/compiler.md#identification), so a library function
can be named in an image only where 6.1's code for it occurs there whole. This reads the two
libraries, `SDX.LIB` and `X386.LIB`, as OMF, takes each public's code up to the next public in its
segment (and each COMDAT) as one function, drops trailing `0x90` alignment, masks every byte a
fixup relocates, and searches each image for the whole masked body. A function with fewer than 8
unmasked bytes is not searched. A module both libraries hold counts once.

A match names a function only when it is unambiguous: one address in the image, claimed by no
other library function (compiler.md lists the matches that are not). Each unambiguous
match is then placed against the dumps, `analysis_out/<ID>_functions.txt` and `_disasm_full.txt`:

  function   Ghidra has a function starting at the address.
  define     the address is in bytes Ghidra never disassembled; `es2_naming.py apply --define`
             creates the function before the name is applied.
  inside     the address is an instruction of other code; left for a repair, not named.

`--batch OUT` writes the named matches not yet in the known-symbols files as an
`es2_naming.py insert` batch and prints the `--define` list for each binary. A match is `high`
when it has at least 16 unmasked bytes, or when another function of its library module matches at
the distance the library puts between them; otherwise `medium`.

The libraries are extracted once from `Reference/Symantec CPP Pro 6.1 (ISO).7z` with 7-Zip into
`tools/analysis_out/sc61/`, gitignored like the CD itself, and checked against their SHA-1.

    py tools/scripts/es1_libmatch.py                       # summary per binary
    py tools/scripts/es1_libmatch.py --list                # every match
    py tools/scripts/es1_libmatch.py --batch batch.json    # symbols for es2_naming.py insert
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_symbols  # noqa: E402
import retail_binaries as R  # noqa: E402

CD = os.path.join(R.ASSETS, "Reference", "Symantec CPP Pro 6.1 (ISO).7z")
CD_ISO = os.path.join("Symantec CPP Pro 6.1 (ISO)", "SCPP610.iso")
CACHE = os.path.join(R.ANALYSIS, "sc61")
LIBS = {  # name: (path in the ISO, SHA-1)
    "SDX.LIB": (os.path.join("SC", "LIB", "SDX.LIB"), "2e0e08f7af6275954a061ffd9e28282ffb93f209"),
    "X386.LIB": (os.path.join("SC", "SRC", "DOS386", "X386.LIB"), "8bd06fef4957656ebab9e729d7b108a8a31c8e92"),
}
MIN_FIXED = 8          # unmasked bytes below which a function is not searched
HIGH_FIXED = 16        # unmasked bytes from which a lone match is high confidence
SOURCE = "Herculan/docs/es1/formats/compiler.md#identification"


def seven_zip() -> str:
    for p in (shutil.which("7z"), shutil.which("7za"), r"C:\Program Files\7-Zip\7z.exe"):
        if p and os.path.exists(p):
            return p
    sys.exit("7-Zip not found; it extracts the libraries from " + CD)


def libraries() -> dict[str, bytes]:
    """The two libraries, extracted from the CD into CACHE on first use."""
    if not all(os.path.exists(os.path.join(CACHE, n)) for n in LIBS):
        if not os.path.exists(CD):
            sys.exit("no Symantec C++ 6.1 CD at " + CD)
        exe = seven_zip()
        os.makedirs(CACHE, exist_ok=True)
        with tempfile.TemporaryDirectory() as tmp:
            subprocess.run([exe, "e", CD, CD_ISO, "-o" + tmp, "-y"], check=True, stdout=subprocess.DEVNULL)
            iso = os.path.join(tmp, os.path.basename(CD_ISO))
            subprocess.run([exe, "e", iso] + [p for p, _ in LIBS.values()] + ["-o" + CACHE, "-y"],
                           check=True, stdout=subprocess.DEVNULL)
    out = {}
    for name, (_, sha1) in LIBS.items():
        data = open(os.path.join(CACHE, name), "rb").read()
        if hashlib.sha1(data).hexdigest() != sha1:
            sys.exit(f"{os.path.join(CACHE, name)} is not 6.1's {name}")
        out[name] = data
    return out


def _index(b: bytes, p: int) -> tuple[int, int]:
    v = b[p]
    return (((v & 0x7f) << 8) | b[p + 1], p + 2) if v & 0x80 else (v, p + 1)


def parse_lib(d: bytes) -> list[dict]:
    """An OMF library's modules: name, segment names, segment bytes and fixed-up offsets, publics
    and COMDATs."""
    assert d[0] == 0xF0, "not an OMF library"
    page = struct.unpack_from("<H", d, 1)[0] + 3
    p, mods, cur = page, [], None
    while p < len(d):
        rt, rl = d[p], struct.unpack_from("<H", d, p + 1)[0]
        body, nxt = d[p + 3:p + 3 + rl - 1], p + 3 + rl
        if rt == 0xF1:                                   # library end, dictionary follows
            break
        if rt == 0x80:                                   # THEADR
            cur = dict(name=body[1:1 + body[0]].decode("latin1"), lnames=[None], segnames=[None], segs={},
                       masks=collections.defaultdict(set), pubs=[], comdat=[], last=None)
            mods.append(cur)
        elif cur is None:
            pass
        elif rt in (0x96, 0xCA):                         # LNAMES, LLNAMES
            q = 0
            while q < len(body):
                n = body[q]
                cur["lnames"].append(body[q + 1:q + 1 + n].decode("latin1"))
                q += 1 + n
        elif rt in (0x98, 0x99):                         # SEGDEF
            q = 1 + (3 if body[0] >> 5 == 0 else 0) + (4 if rt == 0x99 else 2)
            ni, q = _index(body, q)
            cur["segnames"].append(cur["lnames"][ni])
            cur["segs"][len(cur["segnames"]) - 1] = bytearray()
        elif rt in (0x90, 0x91):                         # PUBDEF
            _, q = _index(body, 0)
            s, q = _index(body, q)
            if s == 0:
                q += 2
            while q < len(body):
                n = body[q]
                name = body[q + 1:q + 1 + n].decode("latin1")
                q += 1 + n
                off = struct.unpack_from("<I" if rt == 0x91 else "<H", body, q)[0]
                q += 4 if rt == 0x91 else 2
                _, q = _index(body, q)
                cur["pubs"].append((name, s, off))
        elif rt in (0xA0, 0xA1):                         # LEDATA
            s, q = _index(body, 0)
            off = struct.unpack_from("<I" if rt == 0xA1 else "<H", body, q)[0]
            q += 4 if rt == 0xA1 else 2
            data = body[q:]
            buf = cur["segs"].setdefault(s, bytearray())
            if len(buf) < off + len(data):
                buf.extend(b"\0" * (off + len(data) - len(buf)))
            buf[off:off + len(data)] = data
            cur["last"] = ("seg", s, off)
        elif rt in (0xC2, 0xC3):                         # COMDAT
            flags, attr = body[0], body[1]
            q = 3
            off = struct.unpack_from("<I" if rt == 0xC3 else "<H", body, q)[0]
            q += 4 if rt == 0xC3 else 2
            _, q = _index(body, q)
            if attr & 0x0f == 0:
                _, q = _index(body, q)
                s, q = _index(body, q)
                if s == 0:
                    q += 2
            ni, q = _index(body, q)
            name, data = cur["lnames"][ni], body[q:]
            if flags & 1 and cur["comdat"] and cur["comdat"][-1]["name"] == name:
                c = cur["comdat"][-1]
            else:
                c = dict(name=name, data=bytearray(), mask=set())
                cur["comdat"].append(c)
            if len(c["data"]) < off + len(data):
                c["data"].extend(b"\0" * (off + len(data) - len(c["data"])))
            c["data"][off:off + len(data)] = data
            cur["last"] = ("comdat", c, off)
        elif rt in (0x9C, 0x9D):                         # FIXUPP: mask the bytes each fixup writes
            q = 0
            while q < len(body):
                b0 = body[q]
                if not b0 & 0x80:                        # THREAD subrecord
                    method = (b0 >> 2) & 7
                    q += 1
                    if (b0 >> 6) & 1 == 0:
                        q = _index(body, q)[1] if method & 3 < 3 else q + 2
                    elif method < 3:
                        q = _index(body, q)[1]
                    continue
                loc = (b0 >> 2) & 0xf
                dro = ((b0 & 3) << 8) | body[q + 1]
                fd = body[q + 2]
                q += 3
                if not fd & 0x80 and (fd >> 4) & 7 < 3:
                    q = _index(body, q)[1]
                if not fd & 0x08:
                    q = _index(body, q)[1] if fd & 3 < 3 else q + 2
                if not fd & 0x04:
                    q += 4 if rt == 0x9D else 2
                width = {0: 1, 1: 2, 2: 2, 3: 4, 4: 1, 5: 2, 9: 4, 11: 6, 13: 4}.get(loc, 4)
                kind, tgt, base = cur["last"]
                span = range(base + dro, base + dro + width)
                (cur["masks"][tgt] if kind == "seg" else tgt["mask"]).update(span)
        elif rt in (0x8A, 0x8B):                         # MODEND: modules are page-aligned
            p = (nxt + page - 1) // page * page
            continue
        p = nxt
    return mods


def functions(mods: list[dict]):
    """(module, name, segment key, offset in segment, code, masked offsets) per code public and COMDAT."""
    for m in mods:
        for c in m["comdat"]:
            yield m["name"], c["name"], None, None, bytes(c["data"]), set(c["mask"])
        byseg = collections.defaultdict(list)
        for name, s, off in m["pubs"]:
            if s and any(k in (m["segnames"][s] or "").upper() for k in ("CODE", "TEXT")):
                byseg[s].append((off, name))
        for s, lst in byseg.items():
            lst.sort()
            buf = m["segs"].get(s, b"")
            for i, (off, name) in enumerate(lst):
                end = lst[i + 1][0] if i + 1 < len(lst) else len(buf)
                yield (m["name"], name, (m["name"], s), off, bytes(buf[off:end]),
                       {x - off for x in m["masks"][s] if off <= x < end})


def library_functions() -> list[dict]:
    seen, out = set(), []
    for data in libraries().values():
        for mod, name, seg, off, code, mask in functions(parse_lib(data)):
            module = mod.replace("/", "\\").split("\\")[-1]
            if (module, name) in seen:
                continue
            seen.add((module, name))
            code = code.rstrip(b"\x90")
            n = len(code)
            fixed = n - sum(1 for x in mask if x < n)
            out.append(dict(module=module, name=name, seg=seg, off=off, size=n, fixed=fixed,
                            pattern=None if fixed < MIN_FIXED else re.compile(
                                b"".join(b"." if i in mask else re.escape(code[i:i + 1]) for i in range(n)), re.DOTALL)))
    return out


def symbol_name(public: str) -> str:
    """The C name: one leading underscore dropped from a C public (`_memset`), names that begin
    `__` kept, and the `@` that ends a compiler helper's name (`__DADD@`) dropped, since the
    known-symbols files take identifiers."""
    return (public[1:] if public.startswith("_") and not public.startswith("__") else public).rstrip("@")


FUNC = re.compile(r"^; FUNCTION \S+ @ ([0-9a-f]{8})")
INSN = re.compile(r"^([0-9a-f]{8}) {3}[0-9a-f]")


def placements(binary: R.Binary) -> tuple[set[int], set[int]]:
    """Function starts, and addresses holding an instruction, from the disassembly dump."""
    starts, insns = set(), set()
    with open(binary.dump("disasm_full.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            m = FUNC.match(line)
            if m:
                starts.add(int(m.group(1), 16))
                continue
            m = INSN.match(line)
            if m:
                insns.add(int(m.group(1), 16))
    return starts, insns


def match(binary: R.Binary, lib: list[dict]) -> list[dict]:
    data = R.Image(binary).data          # an X-32 image's file bytes sit at address 0
    hits = [dict(f, addrs=[m.start() for m in f["pattern"].finditer(data)]) for f in lib if f["pattern"]]
    hits = [h for h in hits if h["addrs"]]
    claims = collections.Counter(a for h in hits for a in h["addrs"])
    starts, insns = placements(binary)
    bias = collections.Counter((h["seg"], h["addrs"][0] - h["off"]) for h in hits
                               if h["seg"] and len(h["addrs"]) == 1)
    for h in hits:
        a = h["addrs"][0]
        if len(h["addrs"]) > 1 or claims[a] > 1:
            h["place"] = "ambiguous"
            continue
        h["place"] = "function" if a in starts else "inside" if a in insns else "define"
        corroborated = h["seg"] is not None and bias[(h["seg"], a - h["off"])] > 1
        h["confidence"] = "high" if h["fixed"] >= HIGH_FIXED or corroborated else "medium"
    return hits


def entry(binary: R.Binary, h: dict) -> dict:
    name = symbol_name(h["name"])
    shown = h["name"] if h["name"].endswith("@") else name
    return {"address": f"{h['addrs'][0]:08x}", "binary": binary.id, "type": "function",
            "confidence": h["confidence"],
            "name": name if h["confidence"] == "high" else "maybe_" + name,
            "description": (f"Symantec C++ runtime `{shown}`, from `{h['module']}`: Symantec C++ 6.1's library code "
                            f"for it matches here whole, relocated operands masked, and the search over the image "
                            f"finds it at this address only. The match covers {h['size']} bytes from here."),
            "source": SOURCE}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--binary", type=str.upper, action="append", choices=("ES1DBSIM", "ES1GO", "ES1ESCD"),
                    help="match only this binary (repeatable; default ES1DBSIM and ES1GO)")
    ap.add_argument("--list", action="store_true", help="print every match")
    ap.add_argument("--batch", metavar="OUT", help="write the new symbols as an es2_naming.py insert batch")
    a = ap.parse_args()
    lib = library_functions()
    searched = sum(1 for f in lib if f["pattern"])
    print(f"{len(lib)} library functions, {searched} with at least {MIN_FIXED} unmasked bytes searched")
    batch = []
    for bid in a.binary or ("ES1DBSIM", "ES1GO"):
        binary = R.require(bid, "es1_libmatch.py", "it matches Symantec C++ libraries", compiler=R.SYMANTEC)
        hits = match(binary, lib)
        known = {int(e["address"], 16): e.get("name") for e in es2_symbols.entries() if e.get("binary") == bid}
        places = collections.Counter(h["place"] for h in hits)
        new, same, other, define = [], 0, [], []
        for h in hits:
            if h["place"] not in ("function", "define"):
                continue
            e = entry(binary, h)
            a_ = h["addrs"][0]
            if a_ not in known:
                new.append(e)
                if h["place"] == "define":
                    define.append(e["address"])
            elif known[a_] == e["name"]:
                same += 1
            else:
                other.append(f"{e['address']} {e['name']} (recorded as {known[a_]})")
        print(f"{bid}: {len(hits)} match whole; {places['function']} at a function, {places['define']} in "
              f"undisassembled bytes, {places['inside']} inside other code, {places['ambiguous']} ambiguous. "
              f"Named: {same} recorded, {len(new)} new, {len(other)} recorded under another name")
        for line in other:
            print("  " + line)
        if a.list:
            for h in sorted(hits, key=lambda h: h["addrs"][0]):
                conf = f" {h['confidence']}" if "confidence" in h else ""
                print(f"  {','.join('%08x' % x for x in h['addrs']):18} {h['place']:9}{conf:7} {h['module']:14} "
                      f"{h['name']} ({h['size']} bytes, {h['fixed']} unmasked)")
        if a.batch and define:
            print(f"  apply --define {'+'.join(sorted(define))}")
        batch += new
    if a.batch:
        with open(a.batch, "w", encoding="utf-8") as f:
            json.dump(sorted(batch, key=lambda e: (e["binary"], e["address"])), f, indent=1)
        print(f"wrote {len(batch)} entries to {a.batch}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
