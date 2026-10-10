# `DATA\SOUND.CFG` — sound driver settings

Plain INI, section `[Sound]`, read with `GetPrivateProfileString` by `Sfx_ReadConfig` (`00463698`) into the [`SFX` manager](../simulation/audio.md#the-sfx-manager)'s config block at `+0x24`. VSHELL's `Sfx_Construct` (`0042bf3d`) reads it the same way, except that it reads `Driver` and then stores 1 whatever the file says. How the backend opens its driver from the stored values is [`../simulation/audio.md`](../simulation/audio.md#opening-the-digital-driver). Addresses are DBSIM virtual addresses unless VSHELL is named.

## Keys

| Key | Values | Stored |
|---|---|---|
| `Driver` | `DirectSound`, compared case-insensitively, gives 2; anything else, the shipped `MME` included, 1 | `+0x2a` |
| `Buffers` | `atol`; 1-64, else 5 | `+0x30` |
| `Rate` | `atol` of 11 gives `0x10`, anything else `0x20` | `+0x24` |
| `Width` | `Mono`, compared case-insensitively, gives 4; anything else 8 | `+0x28` |

`+0x26` is fixed at 1 and `+0x32` at `0x200`. A key the file lacks reads as the empty string, which gives the second value in each row, and so does a missing file. `atol` stops at the first non-digit, so `Rate = 11 kHz` is 11 and `Rate = 11025` is not.
