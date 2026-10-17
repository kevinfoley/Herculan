#!/usr/bin/env python3
"""Read a table out of a retail image with each pointer resolved, for vtables, label tables and
dispatch tables the dumps do not show whole.

  es2_peek.py BIN ADDR [COUNT]
      COUNT dwords from ADDR (without COUNT, up to the first 0 dword, at most 64), each with what it
      points at: its known_symbols name; the Ghidra function it enters, or lies inside at an offset;
      the function a run of 0x90 padding falls through to (Symantec, compiler.md#vtables); or the
      NUL-terminated text it points at when that is printable. A value that is none of these is left
      bare: it may be a number.
  es2_peek.py BIN ADDR COUNT --strings
      COUNT NUL-terminated strings from ADDR, one after another.
  es2_peek.py BIN ADDR COUNT --words | --bytes
      COUNT words or bytes from ADDR, in hex.

BIN is any binary retail_binaries.py registers (DBSIM, VSHELL, ES1DBSIM, ES1GO). Reads the image
file only: bytes past a section's file data (zero-filled memory) read as missing.
"""

from __future__ import annotations

import bisect
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import es2_naming as N  # noqa: E402
import retail_binaries  # noqa: E402

PRINTABLE = set(range(0x20, 0x7f)) | {0x09, 0x0a, 0x0d}


def text_at(img, va, limit=80):
    """The printable NUL-terminated text at va (at least 2 characters), or None."""
    out = bytearray()
    while len(out) < limit:
        b = img.read(va + len(out), 1)
        if b is None:
            return None
        if b == b"\0":
            return out.decode("latin1") if len(out) >= 2 else None
        if b[0] not in PRINTABLE:
            return None
        out += b
    return out.decode("latin1") + "..."


class Resolver:
    def __init__(self, binary):
        self.img = retail_binaries.Image(binary)
        self.known = {int(a, 16): n for a, n in N.names(binary).items()}
        fns = N.functions(binary)
        self.starts = sorted(int(a, 16) for a in fns)
        self.ends = {int(a, 16): v[2] for a, v in fns.items()}
        self.ghidra = {int(a, 16): v[0] for a, v in fns.items()}

    def name(self, a):
        return self.known.get(a) or self.ghidra.get(a)

    def describe(self, v):
        if v == 0:  # null, though an X-32 image has code at address 0
            return ""
        if v in self.known:
            return self.known[v]
        # A run of 0x90 padding falls through to the entry after it, framed or frameless.
        p = v
        while self.img.read(p, 1) == b"\x90" and p - v < 16:
            p += 1
        if p != v and self.name(p):
            return f"{self.name(p)} (through its padding)"
        i = bisect.bisect_right(self.starts, v) - 1
        if i >= 0:
            s = self.starts[i]
            if s == v:
                return self.name(s)
            # A small value inside the X-32 image's first function is a number far more often.
            if v < self.ends[s] and v >= 0x1000:
                return f"{self.name(s)}+0x{v - s:x}"
        t = text_at(self.img, v)
        if t is not None:
            return '"' + t.replace("\n", "\\n").replace("\r", "\\r") + '"'
        return ""


def main(argv):
    flags = [a for a in argv if a.startswith("--")]
    args = [a for a in argv if not a.startswith("--")]
    if len(args) < 2 or any(f not in ("--strings", "--words", "--bytes") for f in flags):
        raise SystemExit(__doc__)
    binary, va = args[0].upper(), int(args[1], 16)
    count = int(args[2], 0) if len(args) > 2 else None
    if flags and count is None:
        raise SystemExit("--strings, --words and --bytes need a COUNT")
    if "--strings" in flags:
        img = retail_binaries.Image(binary)
        for _ in range(count):
            out = bytearray()
            while True:
                b = img.read(va + len(out), 1)
                if b is None or b == b"\0":
                    break
                out += b
            print(f"{va:08x}  {out.decode('latin1')!r}")
            va += len(out) + 1
        return 0
    if "--words" in flags or "--bytes" in flags:
        img = retail_binaries.Image(binary)
        size = 2 if "--words" in flags else 1
        data = img.read(va, count * size)
        if data is None:
            raise SystemExit(f"{va:08x}+{count * size:#x} is not all in the image's file bytes")
        fmt = "<H" if size == 2 else "<B"
        vals = [struct.unpack_from(fmt, data, i * size)[0] for i in range(count)]
        for i in range(0, count, 16 // size):
            row = vals[i:i + 16 // size]
            print(f"{va + i * size:08x}  " + " ".join(f"{x:0{2 * size}x}" for x in row))
        return 0
    r = Resolver(binary)
    i = 0
    while count is None and i < 64 or count is not None and i < count:
        a = va + 4 * i
        v = r.img.dword(a)
        if v is None:
            print(f"{a:08x}  +0x{4 * i:02x}  (no file bytes)")
            break
        if count is None and v == 0:
            print(f"{a:08x}  +0x{4 * i:02x}  00000000  (end)")
            break
        print(f"{a:08x}  +0x{4 * i:02x}  {v:08x}  {r.describe(v)}".rstrip())
        i += 1
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main(sys.argv[1:]))
