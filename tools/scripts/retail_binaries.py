#!/usr/bin/env python3
"""The retail binaries the reverse-engineering tools read, and where everything about each one lives.

Every tool that takes a binary takes its identifier from here and gets from here the file to read,
how that file is laid out, which compiler built it, the Ghidra project and program holding it, its
known-symbols file and its `analysis_out/` dumps. A tool that depends on one container format or one
compiler calls `require`, which refuses any other binary with an error instead of letting the tool
return nothing for it, since an empty result from a tool that cannot read the binary looks exactly
like a real negative.

| Identifier | Game | Container | Compiler |
|---|---|---|---|
| `DBSIM`, `VSHELL` | ES2 | PE | Borland C++ |
| `ES1DBSIM`, `ES1GO`, `ES1ESCD` | ES1 | X-32 flat image | Symantec C++ |

The PE files are read as shipped from `ES2/`. The X-32 executables are read as the flat image
`es1_x32_extract.py` writes to `tools/analysis_out/<ID>` with `<ID>_layout.json` beside it
(Herculan/docs/es1/formats/x32-executable.md): loaded at address 0, no relocations and no section
table, so the image is one block of code and data together. The compilers' conventions are in
Herculan/docs/retail/formats/borland-rtti.md and Herculan/docs/es1/formats/compiler.md.

The retail files, the dumps and the Ghidra install and projects are gitignored, so they exist only in
the main checkout. `ASSETS` is that checkout, found through the `.git` file of a worktree, so a tool
run from a worktree reads the same files as one run from the main checkout. The knowledge files and
the Ghidra scripts are tracked and come from the checkout the tool runs from.

    python tools/scripts/retail_binaries.py          # the registry, with each file's presence
"""

from __future__ import annotations

import json
import os
import struct
import sys
from dataclasses import dataclass

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def main_checkout(repo: str) -> str:
    """The main checkout of the repository `repo` belongs to. A worktree's `.git` is a file naming
    its git directory, and that directory's `commondir` leads to the main checkout's `.git`."""
    dotgit = os.path.join(repo, ".git")
    if not os.path.isfile(dotgit):
        return repo
    with open(dotgit, encoding="utf-8") as f:
        line = f.read().strip()
    if not line.startswith("gitdir:"):
        return repo
    gitdir = os.path.normpath(os.path.join(repo, line[len("gitdir:"):].strip()))
    try:
        with open(os.path.join(gitdir, "commondir"), encoding="utf-8") as f:
            common = os.path.normpath(os.path.join(gitdir, f.read().strip()))
    except OSError:
        return repo
    return os.path.dirname(common)


ASSETS = main_checkout(REPO)
ANALYSIS = os.path.join(ASSETS, "tools", "analysis_out")
GHIDRA = os.path.join(ASSETS, "tools", "ghidra_12.1.2_PUBLIC", "support", "analyzeHeadless.bat")
GHIDRA_PROJECTS = os.path.join(ASSETS, "tools", "ghidra_project")
GHIDRA_SCRIPTS = os.path.join(REPO, "tools", "ghidra_scripts")

PE, X32 = "pe", "x32"
BORLAND, SYMANTEC = "borland", "symantec"


@dataclass(frozen=True)
class Binary:
    id: str
    game: str
    container: str
    compiler: str
    file: str       # relative to ASSETS
    project: str    # Ghidra project under GHIDRA_PROJECTS
    program: str    # program name in that project, which -process takes

    @property
    def path(self) -> str:
        return os.path.join(ASSETS, self.file)

    @property
    def symbols(self) -> str:
        return os.path.join(GHIDRA_SCRIPTS, f"known_symbols_{self.id.lower()}.json")

    def dump(self, suffix: str) -> str:
        """`analysis_out/<ID>_<suffix>`, e.g. dump("disasm_full.txt")."""
        return os.path.join(ANALYSIS, f"{self.id}_{suffix}")

    def image(self) -> "Image":
        return Image(self)


REGISTRY = {b.id: b for b in (
    Binary("DBSIM", "ES2", PE, BORLAND, os.path.join("ES2", "DBSIM.EXE"), "ES2Recon", "DBSIM.EXE"),
    Binary("VSHELL", "ES2", PE, BORLAND, os.path.join("ES2", "VSHELL.EXE"), "ES2Recon", "VSHELL.EXE"),
    Binary("ES1DBSIM", "ES1", X32, SYMANTEC, os.path.join("tools", "analysis_out", "ES1DBSIM"), "ES1Recon", "ES1DBSIM"),
    Binary("ES1GO", "ES1", X32, SYMANTEC, os.path.join("tools", "analysis_out", "ES1GO"), "ES1Recon", "ES1GO"),
    Binary("ES1ESCD", "ES1", X32, SYMANTEC, os.path.join("tools", "analysis_out", "ES1ESCD"), "ES1Recon", "ES1ESCD"),
)}
IDS = tuple(REGISTRY)


def get(binary: str) -> Binary:
    try:
        return REGISTRY[binary.upper()]
    except KeyError:
        raise SystemExit("unknown binary %r; the registry holds %s" % (binary, ", ".join(IDS)))


def require(binary: str | Binary, tool: str, why: str, *, container: str | None = None,
            compiler: str | None = None) -> Binary:
    """The binary, or exit with an error when its container or compiler is not the one `tool`
    understands. `why` says what the tool depends on."""
    b = binary if isinstance(binary, Binary) else get(binary)
    if (container and b.container != container) or (compiler and b.compiler != compiler):
        raise SystemExit("%s cannot read %s (%s, %s): %s"
                         % (tool, b.id, {PE: "PE", X32: "X-32 image"}[b.container], b.compiler, why))
    return b


class Image:
    """A binary's bytes, with the mapping between file offsets and virtual addresses.

    `sections` holds (name, va, vsize, raw_off, raw_size). A PE contributes its section table. An
    X-32 image has none, so it is one section, `image`, at address 0, whose file bytes are the
    image's first `raw_size` bytes of memory and whose memory runs on, zero-filled, to `vsize`."""

    def __init__(self, binary: Binary | str):
        self.binary = binary if isinstance(binary, Binary) else get(binary)
        self.path = self.binary.path
        if not os.path.exists(self.path):
            hint = " (run tools/scripts/es1_x32_extract.py)" if self.binary.container == X32 else ""
            raise SystemExit("no %s at %s%s" % (self.binary.id, self.path, hint))
        with open(self.path, "rb") as f:
            self.data = f.read()
        if self.binary.container == PE:
            self._read_pe()
        else:
            self._read_x32()

    def _read_pe(self) -> None:
        d = self.data
        lfanew = struct.unpack_from("<I", d, 0x3C)[0]
        if d[lfanew:lfanew + 4] != b"PE\0\0":
            raise SystemExit("%s is not a PE image" % self.path)
        count = struct.unpack_from("<H", d, lfanew + 6)[0]
        opt_size = struct.unpack_from("<H", d, lfanew + 20)[0]
        self.base = struct.unpack_from("<I", d, lfanew + 24 + 28)[0]
        table = lfanew + 24 + opt_size
        self.sections = []
        for i in range(count):
            off = table + i * 40
            name = d[off:off + 8].rstrip(b"\0").decode("latin1")
            vsize, va, raw_size, raw_off = struct.unpack_from("<IIII", d, off + 8)
            self.sections.append((name, self.base + va, vsize, raw_off, raw_size))
        # Named CODE by Borland, .text by everything else.
        self.code = [s for s in self.sections if s[0] in ("CODE", ".text")]

    def _read_x32(self) -> None:
        layout_path = self.path + "_layout.json"
        try:
            with open(layout_path, encoding="utf-8") as f:
                layout = json.load(f)
        except OSError:
            raise SystemExit("no layout at %s (run tools/scripts/es1_x32_extract.py)" % layout_path)
        if layout["image_file_size"] != len(self.data) or layout["image_base"] != 0:
            raise SystemExit("%s does not match %s: %d bytes at base %x, layout says %d at %x"
                             % (self.path, layout_path, len(self.data), 0, layout["image_file_size"],
                                layout["image_base"]))
        self.base = 0
        self.entry = layout["entry"]
        self.sections = [("image", 0, layout["image_memory_size"], 0, len(self.data))]
        # Code and data share the one block, so every byte is scanned as possible code.
        self.code = list(self.sections)

    def executable(self):
        """The sections that can hold code."""
        return self.code

    def section_of_offset(self, offset: int):
        """(section name, virtual address) of a file offset, or (None, None) outside every section."""
        for name, va, _vsize, raw_off, raw_size in self.sections:
            if raw_off <= offset < raw_off + raw_size:
                return name, va + (offset - raw_off)
        return None, None

    def va_of(self, offset: int):
        return self.section_of_offset(offset)[1]

    def off_of(self, va: int):
        """The file offset holding a virtual address, or None when no file byte does (outside every
        section, or in the zero-filled part past a section's file bytes)."""
        for _name, sva, _vsize, raw_off, raw_size in self.sections:
            if sva <= va < sva + raw_size:
                return raw_off + (va - sva)
        return None

    def read(self, va: int, size: int):
        """`size` file bytes from `va`, or None unless all of them lie in one section's file bytes."""
        off = self.off_of(va)
        if off is None or self.off_of(va + size - 1) != off + size - 1:
            return None
        return self.data[off:off + size]

    def dword(self, va: int):
        b = self.read(va, 4)
        return None if b is None else struct.unpack("<I", b)[0]

    def entries_of(self, target: int) -> list[int]:
        """Every address that enters the same function as `target`, `target` first.

        Symantec C++ code is also entered through the run of `0x90` padding before a `55 8B EC`
        prologue, by direct calls and by vtable slots alike, and the padding falls through to the
        prologue (Herculan/docs/es1/formats/compiler.md#vtables). For such a binary, a target on a
        prologue or on the padding before one gives the whole run and the prologue. Anything else,
        and any address in a Borland binary, is its own only entry."""
        if self.binary.compiler != SYMANTEC:
            return [target]
        prologue = target
        while self.read(prologue, 1) == b"\x90" and prologue - target < 16:
            prologue += 1
        if self.read(prologue, 3) != b"\x55\x8b\xec":
            return [target]
        start = prologue
        while start > 0 and self.read(start - 1, 1) == b"\x90" and prologue - start < 16:
            start -= 1
        return [target] + [a for a in range(start, prologue + 1) if a != target]


def main() -> int:
    print("assets from %s" % ASSETS)
    for b in REGISTRY.values():
        print("%-9s %s  %-3s %-8s %-6s %s:%s  %s%s" % (
            b.id, b.game, b.container, b.compiler, "found" if os.path.exists(b.path) else "MISSING",
            b.project, b.program, b.file, "" if os.path.exists(b.symbols) else "  (no symbols file)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
