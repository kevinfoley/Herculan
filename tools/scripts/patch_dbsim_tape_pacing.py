"""Patch retail DBSIM.EXE so .TAP playback runs in real time.

Retail's mission loop skips Time_BeginSimTick (004677bc) while a tape plays, so frames come as fast
as the machine draws them (docs: Herculan/docs/retail/formats/tap-input-tape.md#timing). This patch hooks
the playback branch of Input_BuildPlayerDevice (0045a7f4) where it stores the frame's recorded
SimTickDelta, and spins on GetTickCount until that frame's time has passed:

- called from Sim_MainTick (return address 0045f47c): wait round(delta * 125 / 256) ms
- called from a panel loop: wait 6 ms, the rate the retail tapes' mouse timestamps show

The deadline accumulates in Time_BeginSimTick's own last-tick global 004d3bf8, so GetTickCount's
~16 ms granularity does not drift the average, and a tape that ends under -p hands over to the
normal 40 ms cap without a jump. When more than 127 ms behind (mission load, a slow frame) the
deadline resyncs to now instead of racing to catch up. Live play never reaches the hooked branch.

The routine lives in the zero padding between the last import thunk (004969ae) and the end of
CODE's raw data (00496a00).

    python tools/scripts/patch_dbsim_tape_pacing.py ES2/DBSIM.EXE            # patch in place
    python tools/scripts/patch_dbsim_tape_pacing.py ES2/DBSIM.EXE --revert   # restore original bytes
"""
import struct
import sys

IMAGE_BASE = 0x400000
HOOK = 0x0045AA75          # MOV AX,[ESP+0x62] ; MOV [EBX+0x2E],AX
HOOK_RETURN = 0x0045AA7E
CAVE = 0x004969B4
CAVE_END = 0x00496A00
GET_TICK_COUNT = 0x00496828  # import thunk
LAST_TICK = 0x004D3BF8

HOOK_ORIGINAL = bytes.fromhex('668b442462 6689432e')


def rel32(src_next, dst):
    return struct.pack('<i', dst - src_next)


def build_cave():
    c = bytearray()

    def emit(hexstr, *tail):
        c.extend(bytes.fromhex(hexstr))
        for t in tail:
            c.extend(t)

    emit('668b442462')                 # mov ax,[esp+0x62]      ; the displaced instructions
    emit('6689432e')                   # mov [ebx+0x2e],ax
    emit('60')                         # pushad
    emit('0fbfd8')                     # movsx ebx,ax
    emit('6bdb7d')                     # imul ebx,ebx,125
    emit('83eb80')                     # sub ebx,-128           ; round
    emit('c1fb08')                     # sar ebx,8              ; ebx = ms
    # return address of Input_BuildPlayerDevice: [esp + 0x20 (pushad) + 0xc8 (locals) + 0x10 (4 pushes)]
    # Its six callers' return addresses end in 7c only for Sim_MainTick's (0045f47c).
    emit('80bc24f80000007c')           # cmp byte [esp+0xf8],0x7c
    emit('7403')                       # je +3
    emit('6a06')                       # push 6
    emit('5b')                         # pop ebx                ; panel frame: 6 ms
    emit('be', struct.pack('<I', LAST_TICK))  # mov esi,LAST_TICK
    emit('031e')                       # add ebx,[esi]          ; ebx = deadline
    emit('e8', rel32(CAVE + len(c) + 5, GET_TICK_COUNT))
    emit('8bd0')                       # mov edx,eax
    emit('2bd3')                       # sub edx,ebx
    emit('83fa7f')                     # cmp edx,127
    emit('7e02')                       # jle +2
    emit('8bd8')                       # mov ebx,eax            ; far behind: resync
    spin = CAVE + len(c)
    emit('e8', rel32(CAVE + len(c) + 5, GET_TICK_COUNT))
    emit('3bc3')                       # cmp eax,ebx
    emit('7c', struct.pack('<b', spin - (CAVE + len(c) + 2)))  # jl spin
    emit('891e')                       # mov [esi],ebx
    emit('61')                         # popad
    emit('e9', rel32(CAVE + len(c) + 5, HOOK_RETURN))
    return bytes(c)


def main():
    path = sys.argv[1]
    revert = '--revert' in sys.argv[2:]
    d = bytearray(open(path, 'rb').read())

    pe = struct.unpack_from('<I', d, 0x3C)[0]
    nsec = struct.unpack_from('<H', d, pe + 6)[0]
    optsz = struct.unpack_from('<H', d, pe + 20)[0]
    sections = []
    for i in range(nsec):
        so = pe + 24 + optsz + i * 40
        _, vsz, va, rsz, rptr = struct.unpack_from('<8sIIII', d, so)
        sections.append((IMAGE_BASE + va, rsz, rptr))

    def off(va):
        for sva, rsz, rptr in sections:
            if sva <= va < sva + rsz:
                return rptr + va - sva
        raise ValueError('%x not in file' % va)

    cave = build_cave()
    assert CAVE + len(cave) <= CAVE_END, len(cave)
    hook = b'\xe9' + rel32(HOOK + 5, CAVE) + b'\x90' * (len(HOOK_ORIGINAL) - 5)

    h, cv = off(HOOK), off(CAVE)
    cur_hook = bytes(d[h:h + len(HOOK_ORIGINAL)])
    cur_cave = bytes(d[cv:cv + len(cave)])

    if revert:
        if cur_hook == HOOK_ORIGINAL:
            print('not patched; nothing to do')
            return
        assert cur_hook == hook and cur_cave == cave, 'unrecognised bytes; refusing'
        d[h:h + len(HOOK_ORIGINAL)] = HOOK_ORIGINAL
        d[cv:cv + len(cave)] = bytes(len(cave))
    else:
        if cur_hook == hook and cur_cave == cave:
            print('already patched')
            return
        assert cur_hook == HOOK_ORIGINAL, 'hook site is not the retail bytes: %s' % cur_hook.hex()
        assert d[cv:off(CAVE_END - 1) + 1] == bytes(CAVE_END - CAVE), 'cave is not empty'
        d[h:h + len(HOOK_ORIGINAL)] = hook
        d[cv:cv + len(cave)] = cave

    open(path, 'wb').write(d)
    print('%s: %s (%d-byte routine at %08x)' % (path, 'reverted' if revert else 'patched', len(cave), CAVE))


if __name__ == '__main__':
    main()
