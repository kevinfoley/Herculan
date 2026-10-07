"""Extract ESTEXT.BIN out of LANG0.VOL and dump all its strings with their indices.

VOL container and .BIN string table per docs/retail/formats/weapons-dat.md; the .BIN header layout is
confirmed here by asserting 8 + count*2 + poolSize == file length, which the doc says holds for
all six files.
"""
import struct

VOL = r"D:\Documents\TEMP\HERCULAN\ES2\VOL\LANG0.VOL"
OUT = (r"C:\Users\Kevin\AppData\Local\Temp\claude"
       r"\d--Documents-TEMP-HERCULAN\4335b954-34b4-4cc7-b9a3-523782b4e51d\scratchpad\estext.txt")


def vol_entries(path):
    b = open(path, "rb").read()
    assert b[:4] == b"VOLN", b[:4]
    dir_size = struct.unpack_from("<H", b, 0x0A)[0]
    o = 0x0C + dir_size
    count = struct.unpack_from("<H", b, o)[0]
    o += 6
    out = {}
    for _ in range(count):
        name = b[o:o + 13].split(b"\0")[0].decode("ascii", "replace")
        offset = struct.unpack_from("<I", b, o + 14)[0]
        o += 18
        flag, size, date, time = struct.unpack_from("<BIHH", b, offset)
        out[name.upper()] = b[offset + 9:offset + 9 + size]
    return out


def bin_strings(blob):
    count, pool_size = struct.unpack_from("<II", blob, 0)
    offsets = [struct.unpack_from("<H", blob, 8 + i * 2)[0] for i in range(count)]
    pool = 8 + count * 2
    assert pool + pool_size == len(blob), f"{pool} + {pool_size} != {len(blob)}"
    out = []
    for off in offsets:
        start = pool + off
        end = blob.index(b"\0", start)
        out.append(blob[start:end].decode("latin-1"))
    return count, out


entries = vol_entries(VOL)
print("LANG0.VOL entries:", ", ".join(sorted(entries)))

blob = entries["ESTEXT.BIN"]
count, strings = bin_strings(blob)
print(f"ESTEXT.BIN: {len(blob)} bytes, {count} entries")

with open(OUT, "w", encoding="utf-8") as f:
    for i, s in enumerate(strings):
        f.write(f"{i:5d}  0x{i:03x}  {s}\n")
print("wrote", OUT)

for i in list(range(0x68, 0x6E)) + [0xC8, 0x42, 0x43]:
    print(f"  [0x{i:02x}] {strings[i]!r}")
