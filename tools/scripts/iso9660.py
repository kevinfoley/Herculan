"""Minimal ISO9660 reader for raw (2352-byte sector, Mode 1 or Mode 2 Form 1) or cooked (2048) images:
the data track only, primary volume descriptor, no Joliet or Rock Ridge.

Usage:
  iso9660.py IMAGE info               sector size and header skip
  iso9660.py IMAGE ls [PATH]          list one directory
  iso9660.py IMAGE tree               list every file with its size
  iso9660.py IMAGE cat PATH           write a file to stdout
  iso9660.py IMAGE get PATH DEST      copy a file to DEST

Git Bash rewrites a leading '/' in PATH; set MSYS_NO_PATHCONV=1 or omit the slash.
"""
import os
import struct
import sys


class Iso:
    def __init__(self, path):
        self.f = open(path, "rb")
        size = os.path.getsize(path)
        self.raw = size % 2352 == 0 and size % 2048 != 0
        if not self.raw:
            self.raw = self._probe_raw()
        self.sector = 2352 if self.raw else 2048
        self.skip = 0
        if self.raw:
            # Mode byte at offset 15 of the header: 1 -> 16-byte header, 2 -> 24 (form 1 subheader).
            self.f.seek(16 * 2352 + 15)
            mode = self.f.read(1)[0]
            self.skip = 16 if mode == 1 else 24
        pvd = self.read_sectors(16, 1)
        assert pvd[1:6] == b"CD001", "no primary volume descriptor"
        self.root = self._parse_record(pvd[156:156 + 34])

    def _probe_raw(self):
        self.f.seek(0)
        return self.f.read(12) == b"\x00" + b"\xff" * 10 + b"\x00"

    def read_sectors(self, lba, count):
        out = bytearray()
        for i in range(count):
            self.f.seek((lba + i) * self.sector + self.skip)
            out += self.f.read(2048)
        return bytes(out)

    def read_extent(self, lba, length):
        data = self.read_sectors(lba, (length + 2047) // 2048)
        return data[:length]

    @staticmethod
    def _parse_record(rec):
        lba = struct.unpack_from("<I", rec, 2)[0]
        length = struct.unpack_from("<I", rec, 10)[0]
        flags = rec[25]
        nlen = rec[32]
        name = rec[33:33 + nlen]
        if name == b"\x00":
            name = "."
        elif name == b"\x01":
            name = ".."
        else:
            name = name.decode("ascii", "replace").split(";")[0].rstrip(".")
        return {"lba": lba, "size": length, "dir": bool(flags & 2), "name": name}

    def listdir(self, entry):
        data = self.read_extent(entry["lba"], entry["size"])
        out = []
        pos = 0
        while pos < len(data):
            n = data[pos]
            if n == 0:
                pos = (pos // 2048 + 1) * 2048
                continue
            rec = self._parse_record(data[pos:pos + n])
            if rec["name"] not in (".", ".."):
                out.append(rec)
            pos += n
        return out

    def lookup(self, path):
        entry = self.root
        for part in [p for p in path.replace("\\", "/").split("/") if p]:
            match = [e for e in self.listdir(entry) if e["name"].upper() == part.upper()]
            if not match:
                raise FileNotFoundError(path)
            entry = match[0]
        return entry

    def walk(self, entry=None, prefix=""):
        entry = entry or self.root
        for e in self.listdir(entry):
            p = prefix + e["name"]
            if e["dir"]:
                yield p + "/", e
                yield from self.walk(e, p + "/")
            else:
                yield p, e

    def read(self, entry):
        return self.read_extent(entry["lba"], entry["size"])


def main():
    iso = Iso(sys.argv[1])
    cmd = sys.argv[2]
    if cmd == "info":
        print(f"raw={iso.raw} sector={iso.sector} skip={iso.skip}")
    elif cmd == "ls":
        for e in iso.listdir(iso.lookup(sys.argv[3] if len(sys.argv) > 3 else "/")):
            print(f"{'<DIR>' if e['dir'] else e['size']:>10}  {e['name']}")
    elif cmd == "tree":
        for p, e in iso.walk():
            print(f"{'' if e['dir'] else e['size']:>10}  {p}")
    elif cmd == "cat":
        sys.stdout.buffer.write(iso.read(iso.lookup(sys.argv[3])))
    elif cmd == "get":
        with open(sys.argv[4], "wb") as out:
            out.write(iso.read(iso.lookup(sys.argv[3])))


if __name__ == "__main__":
    main()
