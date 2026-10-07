#!/usr/bin/env python3
"""Watch one structure's component damage in a running retail DBSIM.EXE, hit by hit.

Opens the game read-only (ReadProcessMemory; it never writes, pauses or injects),
finds every structure object in memory, picks one, and logs each change to its
component damage. A logged change is exactly what one hit wrote, after the
difficulty scale, which is how docs/retail/formats/proj-dat.md#lookup measured which
PROJ.DAT record a shot applies.

Finding the objects: structures live in a pool whose stride is 0x26d, so they are
not 4-byte aligned; the scan looks for one of Base_Construct's five type vtables
at any byte offset and keeps a hit only when its typeRecord (+0x1f2) lands on a
BASES.DAT row. All of a mission's structures should turn up, one per live
script.dat base record.

Fields read (tools/analysis_out/DBSIM_structs_full.txt, StructureObject):
  obj+0x26   int32 x, y, z     obj+0x99  byte destroyed
  obj+0x1f2  -> BASES.DAT row: +0x12 component count, +0x14 -> 0x1e-byte component
             templates whose +0 short is the component's maximum
  obj+0x201  -> short per component, 1 while it stands
  obj+0x205  -> 11 bytes per component, +0 short damage

How Base_ApplyDamage (00404d70) writes: damage accumulates; reaching the maximum,
or the random roll it makes once a hit carries the total past half, sets the
total to the maximum and clears the alive flag. The log marks that as KILLED,
and the change it shows is then not the hit's damage.

Usage (with the mission running):
    python tools/scripts/es2_watch_structure.py --list
    python tools/scripts/es2_watch_structure.py --type 19 --near 1005948 1076656 39168
    python tools/scripts/es2_watch_structure.py --object 0x2af2210

Stop it with Ctrl+C. Windows only.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import math
import struct
import subprocess
import sys
import time

STRUCTURE_VTABLES = (0x00497784, 0x00497818, 0x004978AC, 0x00497940, 0x004979D4)
BASES_TABLE_PTR = 0x004A9620  # -> BASES.DAT rows, 0x3c bytes each
BASES_ROW_SIZE = 0x3C
STRUCTURE_SIZE = 0x26D
MISSION_DIFFICULTY = 0x004A9EE0
DIFFICULTY_NAMES = ('ROOKIE', 'REGULAR', 'VETERAN', 'ELITE')
SIDE0_DAMAGE_SCALE = (3500, 2800, 2100, 1400)  # DamageScaleBySide0Difficulty (0049a73c), Q10

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
MEM_COMMIT = 0x1000
PAGE_READABLE = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80
PAGE_GUARD = 0x100


class MemoryBasicInformation(ctypes.Structure):
    _fields_ = [
        ('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p),
        ('AllocationProtect', wt.DWORD), ('PartitionId', wt.WORD), ('RegionSize', ctypes.c_size_t),
        ('State', wt.DWORD), ('Protect', wt.DWORD), ('Type', wt.DWORD),
    ]


kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.ReadProcessMemory.argtypes = [
    wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
kernel32.VirtualQueryEx.argtypes = [
    wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(MemoryBasicInformation), ctypes.c_size_t]


class GameUnreadable(Exception):
    """A read failed: the game has exited, or the mission's memory has been freed."""


class Game:
    def __init__(self):
        out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq DBSIM.EXE', '/FO', 'CSV', '/NH'],
            capture_output=True, text=True).stdout
        pids = [int(line.split('","')[1]) for line in out.splitlines() if line.lower().startswith('"dbsim.exe"')]
        if len(pids) != 1:
            sys.exit(f'expected one DBSIM.EXE running, found {len(pids)}')
        self.pid = pids[0]
        self.handle = kernel32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, self.pid)
        if not self.handle:
            sys.exit(f'OpenProcess failed: error {ctypes.get_last_error()}')

    def read(self, address, size):
        buffer = ctypes.create_string_buffer(size)
        got = ctypes.c_size_t()
        if not kernel32.ReadProcessMemory(self.handle, address, buffer, size, ctypes.byref(got)) or got.value != size:
            return None
        return buffer.raw

    def read_or_raise(self, address, size):
        data = self.read(address, size)
        if data is None:
            raise GameUnreadable(address)
        return data

    def u32(self, address):
        return struct.unpack('<I', self.read_or_raise(address, 4))[0]

    def s16(self, address):
        return struct.unpack('<h', self.read_or_raise(address, 2))[0]

    def regions(self):
        """Yields (base, bytes) for every committed, readable region below 2 GB."""
        address = 0
        info = MemoryBasicInformation()
        while address < 0x7FFF0000 and kernel32.VirtualQueryEx(self.handle, address, ctypes.byref(info), ctypes.sizeof(info)):
            base, size = info.BaseAddress or 0, info.RegionSize
            if info.State == MEM_COMMIT and info.Protect & PAGE_READABLE and not info.Protect & PAGE_GUARD:
                data = self.read(base, size)
                if data:
                    yield base, data
            address = base + size


def find_structures(game, bases_table):
    found = []
    for base, data in game.regions():
        for vtable in STRUCTURE_VTABLES:
            pattern = struct.pack('<I', vtable)
            offset = data.find(pattern)
            while offset != -1:
                if offset + STRUCTURE_SIZE <= len(data):
                    row = struct.unpack_from('<I', data, offset + 0x1F2)[0] - bases_table
                    if 0 <= row < 256 * BASES_ROW_SIZE and row % BASES_ROW_SIZE == 0:
                        position = struct.unpack_from('<3i', data, offset + 0x26)
                        found.append((base + offset, row // BASES_ROW_SIZE, position))
                offset = data.find(pattern, offset + 1)
    return sorted(found)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--list', action='store_true', help='list every structure and exit')
    parser.add_argument('--type', type=int, help='only consider this BASES.DAT type')
    parser.add_argument('--near', type=int, nargs=3, metavar=('X', 'Y', 'Z'), help='pick the structure nearest this point')
    parser.add_argument('--object', type=lambda v: int(v, 0), help='watch the structure at this address')
    parser.add_argument('--interval', type=float, default=0.002, help='seconds between polls (default 0.002)')
    args = parser.parse_args()

    game = Game()
    bases_table = game.u32(BASES_TABLE_PTR)
    difficulty = game.s16(MISSION_DIFFICULTY)
    if 0 <= difficulty < len(SIDE0_DAMAGE_SCALE):
        print(f'DBSIM pid {game.pid}; difficulty {difficulty} ({DIFFICULTY_NAMES[difficulty]}): '
            f'a player shot is scaled by {SIDE0_DAMAGE_SCALE[difficulty]}/1024', flush=True)
    else:
        print(f'DBSIM pid {game.pid}; difficulty reads {difficulty}, not 0-3', flush=True)

    structures = find_structures(game, bases_table)
    if args.list or not (args.near or args.object):
        for address, type_index, position in structures:
            print(f'  {address:#x}  type {type_index:3}  at {position}')
        print(f'{len(structures)} structures')
        return

    candidates = [s for s in structures if args.type is None or s[1] == args.type]
    if args.object is not None:
        candidates = [s for s in candidates if s[0] == args.object]
    if not candidates:
        sys.exit('no structure matches')
    address, type_index, position = (min(candidates, key=lambda s: math.dist(s[2], args.near))
        if args.near else candidates[0])
    where = f', {math.dist(position, args.near):.0f} units from the point given' if args.near else ''
    print(f'watching {address:#x}: BASES.DAT type {type_index} at {position}{where}', flush=True)

    row = game.u32(address + 0x1F2)
    count = game.s16(row + 0x12)
    templates = game.u32(row + 0x14)
    alive = game.u32(address + 0x201)
    states = game.u32(address + 0x205)
    maxima = [game.s16(templates + i * 0x1E) for i in range(count)]

    def snapshot():
        return (tuple(game.s16(states + i * 11) for i in range(count)),
            tuple(game.s16(alive + i * 2) for i in range(count)),
            game.read(address + 0x99, 1)[0])

    def settled():
        # The game writes a kill's total and its alive flag a few instructions apart; a poll that
        # lands between them would split one event into two lines, so read until two agree.
        previous = snapshot()
        while True:
            current = snapshot()
            if current == previous:
                return current
            previous = current

    damage, standing, destroyed = settled()
    for i in range(count):
        print(f'  component {i}: damage {damage[i]} of {maxima[i]}, {"standing" if standing[i] else "destroyed"}', flush=True)
    print(f'  structure destroyed: {destroyed}; watching, Ctrl+C to stop', flush=True)

    start = time.perf_counter()
    try:
        while True:
            if snapshot() != (damage, standing, destroyed):
                new_damage, new_standing, new_destroyed = settled()
                t = time.perf_counter() - start
                for i in range(count):
                    if new_damage[i] != damage[i] or new_standing[i] != standing[i]:
                        killed = standing[i] and not new_standing[i]
                        print(f'{t:9.3f}s  component {i}: {damage[i]} -> {new_damage[i]} '
                            f'({new_damage[i] - damage[i]:+d}) of {maxima[i]}{"  KILLED" if killed else ""}', flush=True)
                if new_destroyed != destroyed:
                    print(f'{t:9.3f}s  structure destroyed: {new_destroyed}', flush=True)
                damage, standing, destroyed = new_damage, new_standing, new_destroyed
            time.sleep(args.interval)
    except KeyboardInterrupt:
        pass
    except GameUnreadable as error:
        print(f'{time.perf_counter() - start:9.3f}s  stopped: the game can no longer be read at {error.args[0]:#x} '
            '(it has exited, or the mission has ended)', flush=True)


if __name__ == '__main__':
    main()
