# Sound samples and voice clips — `SIMSOUND.VOL`, `SIMVOICE.VOL`

The sample files DBSIM's sound layer plays: the catalog's effects in `SIMSOUND.VOL`'s two banks, and the squadmates' and the cockpit computer's speech in the `SIMVOICE` archives. `Sos_LoadWaveSample` (`00474254`) reads one when the `SFX` manager caches it ([`../simulation/audio.md`](../simulation/audio.md#loading-a-sample)). The catalog rows that name the effects are [`sounds-str.md`](sounds-str.md), and how a speech clip's name is built is [`../simulation/audio.md`](../simulation/audio.md#file-naming). Addresses are DBSIM virtual addresses.

## Sample format

`Sos_LoadWaveSample` reads the whole file. One starting `RIFF` is taken as a canonical WAV at fixed offsets — rate at `+0x18`, channels `+0x16`, bits `+0x22`, data from `+0x2c` — with the dword at `+0x28` less `0x2c` as its length, so a canonical file's last `0x2c` bytes of sound are not played. **Anything else plays as raw 8-bit unsigned mono at 11,025 Hz.**

## Sample banks

`Sound_ResolveSamplePath` (`00462238`) prefixes the catalog's filename with `HMI\` normally and `HMX\` in the low-memory mode. `SIMSOUND.VOL` carries both: 43 files under `hmi\` and 42 under `hmx\`. Every `hmx\` file is 8-bit mono 11,025 Hz. Of the 42 `hmi\` twins, 38 are 8-bit 22,050 Hz (twice the `hmx\` size); `TRGLOC`, `XPLMLT2` and `XPLMLT4` are 16-bit 22,050 Hz (four times); and `BACANN4` is 8-bit 11,025 Hz in both banks, the same size with different bytes.

**In v1.0, `EXPLO5.WAV` exists only in `hmi\`.** Catalog id `0x22` names it, so in low-memory mode that one sound fails to open. v1.10's `SIMPATCH.VOL` adds a copy under `hmx\`.

## Voice clips

`SIMVOICE.VOL` holds 147 `P*_*.WAV` and 66 `CVM_*.WAV`, the cockpit computer's own lines. `SIMSOUND.VOL`'s `snc\` holds the portraits' lip-sync scripts ([`snc-lip-sync.md`](snc-lip-sync.md)).

In the v1.0 install the three voice archives are byte-identical (7,042,407 bytes each), all labelled `SIMVOICE\`, and the v1.0 installer lists the other two as 4-byte files. v1.10's are recordings in their own language: `SIMVOICF.VOL` (7,261,567 bytes) and `SIMVOICG.VOL` (6,610,094 bytes), labelled `SIMVOICF\` and `SIMVOICG\`, with the same 213 entry names ([`../retail-builds.md`](../retail-builds.md)). Only the cockpit computer is translated: 57 of the 66 `CVM_*.WAV` in French and 53 in German differ from the English, and all 147 squadmate clips are the English recordings.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A `.wav` name resolves under one directory | It resolves under `HMI\` or `HMX\` depending on the low-memory flag, and the two banks are not identical — v1.0's `HMX\` has no `EXPLO5.WAV`. |
