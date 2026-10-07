# HercWorks MDK — C# / WinForms port (beta)

Porting [herc-works-mdk](https://github.com/Subject9x/herc-works-mdk) (Earthsiege 2
modding toolkit, MIT licensed) from Java to C#/.NET 8 + WinForms. Original project is
310 Java files / ~30,000 lines across four modules — this is being ported in stages
rather than all at once.

Bugs found in the original Java source during porting are fixed rather than carried over — see
"Notes on the port" below for the ones found so far. Retail Earthsiege 2 bugs are the only thing
kept bug-compatible, since HERCULAN Engine (the separate game-engine reimplementation, not this
toolkit) needs to match retail behaviour; those are listed in
[`KNOWN_ISSUES.md`](KNOWN_ISSUES.md), which records outstanding issues only. [`ROADMAP.md`](ROADMAP.md)
is the separate register for what the engine does not implement yet.

## Status

| Module | Java LOC | Status |
|---|---|---|
| ES2Vol → `HercWorks.Vol` | ~1,300 | **Done** — `.vol` archive reader/writer |
| ES2Core → `HercWorks.Core` | ~18,000 | **Done** — data model, `io/read`, `io/write`, and all three `io/transform` families |
| ES2TransferApi | ~8,700 | **In progress** — the DTO layer only (`Dto/File`, `Dto/Struct/Dbsim`) |
| ES2Excavator (CLI) | ~2,100 | Not started (logic will become WinForms menu actions) |
| WinForms UI | n/a | **In progress** — VOL browsing, plus editors for the hangar, squad, campaign resources, herc and weapon stats and mission scripts, and image/model viewers |

### ES2Core progress detail

**`io/read/` and `io/write/` — both complete.**
- `io/read`: `DynFileReader` (load/parse DPL/DBM/DBA files), `DatFileReader` (parses `InitHerc`
  stats out of `.DAT` files), and `VolFileReader` — a near-duplicate of
  `HercWorks.Vol.Io.VolFileReader` in the Java original (same situation as `io/write`'s
  `VolFileWriter` below), ported as a thin delegating wrapper rather than re-implementing the
  same logic twice. The Java original also carried several debug-only/fully-commented-out
  methods here that added no behavior and weren't ported.
- `io/write`: `VolFileCompiler` (compiles a **brand-new** VOL from scratch, computing fresh
  offsets/sizes — distinct from the "strict" round-trip writer) and `VolFileWriter` — again a
  near-duplicate of `HercWorks.Vol.Io.VolFileWriter`, ported as a thin delegating wrapper.
  `DynFileWriter` (exports DBM images to PNG/BMP via `System.Drawing.Bitmap`, and DBM objects back
  to `.DBM` bytes) lives in `HercWorks.UI` instead, not `HercWorks.Core.Io.Write` — it's an MDK
  export feature the engine port will never call, and `Core` otherwise has no `System.Drawing`
  dependency at all.

**Bugs found in the Java original** are listed under [Notes on the port](#notes-on-the-port) below, which says for each whether it was fixed or ported literally.

**`io/transform/dbsim` (22 files) and `io/transform/shell` (10 files) — both ported.**

### ES2Core progress detail

**`data/file/*` — complete (102 of 102 files).** This is the full per-file-type data model layer:
every DBSIM/VSHELL game file type has a corresponding C# class, organized to mirror the Java
package structure:
- Top-level: `StringFile`, `StringBinaryFile`, `FileClassDefs` (the file-type registry tying
  everything together — see below)
- `bnd/` (5), `cfg/` (6) — config/binding files
- `dat/shell/` (14), `dat/sim/` (7) — herc armory/repair UI data, career missions, weapons list,
  herc simulation stats, beam/missile/projectile data, zone data
- `dbsim/` (8) — flight model, gun layout, collision, damage, paper-doll UI, world data
- `dts/` + 4 subpackages (27) — the ThreeSpace 3D model format: object headers, base
  part/group/poly hierarchy, animation sequences, BSP tree nodes, part types
- `dyn/` (5) — Dynamix bitmap/bitmap-array/palette/grid-shape/3D-model file types
- `gau/` (10) — HUD widget config (panels, buttons, labels, meters, weapon panels)
- `msn/` + `msn/script` (14) — mission files, map objects/coords, unit/entity spawn data, the six
  `UnkEntity*` byte-layout classes, mission strings
- `sav/` (3) — player save file, mission strings, .mec file

**`io/transform/common/` — complete (7 of 7 files).** The byte<->object transformers for file
types shared across DBSIM/VSHELL: `DynamixBitmapTransformer`, `DynamixPaletteTransformer`,
`DynamixBitmapArrayTransformer`, `BinStringFileTransformer`, `MissionStringFileTransformer`,
`MissionFileTransformer`, `PlayerSaveTransform`.

**`io/transform/ThreeSpaceByteTransformer`** — the shared byte-cursor base class every
reader/writer/transformer extends — was ported earlier. Applying the verified `Bytes`-library
semantics (see below) turned up two of the same "name says one thing, does another" pattern:
- `IndexSegmentLE()` calls `.byteOrder(LE).array()` — since `.array()` ignores that tag, this
  method is **byte-identical to `IndexSegment()`** despite its name. Ported literally.
- `PeekAt()` didn't read anything in the Java original — it just returned `index + at` as a number.
  Fixed rather than carried over; see [Notes on the port](#notes-on-the-port).
- By contrast, `IndexShortLE`/`IndexShort`/`IndexIntLE` (call `.toShort()`/`.toInt()`, not just
  `.array()`) and `WriteIntLE`/`WriteShortLE` (call `.reverse()`) genuinely are correct
  endian-aware reads/writes — confirmed by the same source-level check.

**Design notes:**
- Java's "rich enum" pattern (id/name/lookup-table, common throughout this codebase) has no
  direct C# equivalent — ported as sealed classes with static readonly instances (same
  `GetById`/`GetByName`-style API), not a literal `enum`. `HWidgetId` needed an extra `Name`
  property to stand in for Java's `.name()` (the enum constant's own identifier), since several
  `toString()` overrides display it. `Values()` was added to `WeaponLUT`, `HercLUT`,
  `HercExternals`, `HercInternals`, and `MissileType` to stand in for Java's `.values()`, needed
  once transformer code started iterating over all constants.
- `java.awt.Point`/`Dimension` → `System.Drawing.Point`/`Size`; `java.awt.Color` →
  `System.Drawing.Color`; Apache Commons Math's `Vector3D` → `System.Numerics.Vector3`.
- `FileClassDefs.cs` needed explicit `using` type-aliases (`T_ArmHerc`, etc.) because, like the
  Java original, every static field is named identically to the class type it points to —
  without the aliases, `typeof(ArmHerc)` inside the `ArmHerc` field's own initializer would be
  ambiguous.
- Replaced the earlier placeholder `DynamixBitmapArray` stub (written ahead of that package
  existing, several rounds back) with the real port once `data/file/dyn/` was reached.

**`io/transform/dbsim` (22 files) and `io/transform/shell` (10 files) — both complete**, in the same
byte-parsing category as `transform/common` above. With `io/read` and `io/write` done, that is all of
ES2Core.

## Structure

```
HercWorksMDK.sln
src/
  HercWorks.Vol/         class library — ported ES2Vol module
    FileType.cs
    DataFile.cs
    VolEntry.cs
    VolDir.cs
    Voln.cs
    Io/VolFileReader.cs
    Io/VolFileWriter.cs
    Util/ByteOps.cs       little-endian byte helpers (replaces the Java favre 'Bytes' lib)
  HercWorks.Disc/         class library — reads CD images in place: .iso, raw .bin, cue sheets with
                          audio tracks; the ISO 9660 (and Joliet) file system and Red Book PCM
  HercWorks.UI/           WinForms shell (net8.0-windows)
    Program.cs
    MainForm.cs
  HercWorks.Query/        console tool — searches the retail .MSN missions through HercWorks.Core:
                          which missions place a structure, mech or flyer type, what tests or
                          writes a campaign flag, and what orders each group carries (`--help` lists
                          the commands)
tests/
  HercWorks.Vol.Tests/    xUnit round-trip test against a hand-built synthetic .vol
  HercWorks.Query.Tests/  the queries against the retail missions, when ES2/ is present
  HercWorks.Disc.Tests/   hand-built and deliberately corrupted images, and the v1.10 disc image when present
```

## Building

Requires the .NET 8 SDK (Visual Studio 2022 17.8+, or `dotnet` CLI). Open
`HercWorksMDK.sln` in Visual Studio, or:

```
dotnet build
dotnet test
dotnet run --project src/HercWorks.UI
```

Both solutions build clean (0 warnings) and the test suites pass.

## Notes on the port

- **Byte handling**: the Java code used the `at.favre.lib.bytes` library with explicit
  `.byteOrder(...)` tags. All VOL numeric fields are little-endian on disk; this port
  uses a small `ByteOps` helper instead of `System.BitConverter` so the endianness is
  explicit rather than depending on host byte order.
- **Round-trip fidelity**: like the original, the "strict" writer (`PackVolToFileStrict`)
  writes header/count fields back out from the parsed object's numeric values, but
  writes file-list entries and file data back out from the *exact raw bytes* that were
  read — matching the original Java javadoc's intent ("write it back out EXACTLY as it
  was loaded").
- **Bug fix**: the original Java used `File.pathSeparator` (`;` on Windows) instead of
  `File.separator` when joining folder/file paths, which would have produced invalid
  paths. This port uses `Path.Combine` instead.
- **Bug fix**: the original Java's `VolFileCompiler.compile()` wrote the freshly-compiled VOL
  to a hardcoded developer path (`E:\ES2_OS\dev\earthsiege2\VOL`). `VolFileCompiler.Compile()`
  takes the output path as a parameter instead.
- **Bug fix**: the original Java's `ThreeSpaceByteTransformer.peekAt(int)` returned `index + at` —
  an offset, not the byte at that offset. `PeekAt` now actually dereferences it.
- **Bug fix**: the original Java's `DatFileReader.replaceDatBytes(newData, targetFile)` ignored its
  `newData` parameter entirely, splicing the file's existing header onto its own existing raw bytes
  unchanged. `ReplaceDatBytes` now splices `newData` in.
- **Bug fix**: the original Java's `InitHerc.header` was built as the 8 ASCII bytes of the string
  `"661FAF55"` rather than the 4 hex-decoded bytes `66 1F AF 55` (`Bytes.from("661FAF55",
  StandardCharsets.UTF_8)` — the same mistake `DynamixPalette.Header` had). `InitHerc.Header` now
  holds the decoded bytes. Neither the Java nor the C# field is actually read or written anywhere,
  so this has no behavioral effect today.
- **`byte` vs `sbyte`**: Java's `byte` is signed; all values actually used here (directory
  counts/indices) are small and non-negative, so this port uses C#'s unsigned `byte`
  throughout without any behavior difference.

## Next steps

ES2Core is fully ported. The remaining work is ES2TransferApi (~8,700 lines, JSON DTO layer — the
DTO classes are started, the transfer logic is not) and ES2Excavator (~2,100 lines, CLI logic to
become WinForms menu actions).
