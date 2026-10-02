# Video playback

How HERCULAN decodes and plays the game's AVI cutscenes. The files themselves are [`formats/avi-video.md`](../formats/avi-video.md), and the Indeo codecs are [`formats/indeo3.md`](../formats/indeo3.md) and [`formats/indeo4.md`](../formats/indeo4.md).

Decoding is `HercWorks.Video`, a standalone assembly with no dependencies, and `Herculan.Engine.Video.MoviePlayer`, the seam that puts its frames on a GL texture and its sound through the engine's audio backend. Nothing calls into Video for Windows or any system codec; the decoding is HERCULAN's own.

## Audio

Tracks are decoded whole rather than streamed. The longest in the corpus is under a megabyte once widened, so streaming would be machinery with nothing to show for it.

A stereo track is folded to mono before it reaches the engine, because `Herculan.Engine.Audio.WaveSample` is mono and the backend pans at play time — it has nowhere to put a second channel. This is a loss against retail, which played the intro in stereo; the fix belongs in `IAudioBackend`, not here.

## Playback

`MoviePlayback` owns the clock. The caller advances it by a delta and it decodes however many frames that crossed, returning whether the frame buffer changed so a host can skip re-uploading a texture on the many ticks that fall inside one frame's interval.

Frames are decoded in sequence and never skipped, even when several fall due at once. Each frame is the previous one plus a delta, so skipping corrupts everything after it — a host that falls behind drops presentation, not decoding.

`MoviePlayer` adds the engine side: a `GpuTexture` updated in place when the frame's revision changes, and the soundtrack started once through `IAudioBackend` as a single sample. Video is the clock and audio free-runs; nothing re-syncs them mid-playback, which is also what the original did.

The front end plays its movies through the same `MoviePlayer` (`Herculan.Engine.Shell.ShellMovieRun`).

## Looking at one

`--movie` plays a single cutscene instead of running a mission or the front end:

```
dotnet run --project Herculan/src/Herculan.Engine.Host -- --movie ALPH_TH.AVI
```

It takes a path, or a bare name — with or without the extension — to look up in the install's `AVI` folder. `--screenshot <file>` captures a frame and exits, `--silent` skips audio. The frame is letterboxed at its own aspect ratio and sampled nearest-neighbour.

A file whose codec has no decoder reports what the container says and what the compression is, and exits without opening a window, because that is the answer to "why will this not play".

This exists because a video decoder cannot be validated by a unit test alone. A codec that is subtly wrong — rows inverted, a block quadrant transposed, a colour channel swapped — still returns frames and still passes any test that only checks it did not throw. The failure is visual, so the check has to be.

## Security posture

These files are the one class of game asset a user might obtain from somewhere other than the user's own install, so `HercWorks.Video` is built to parse hostile input:

- It builds without `AllowUnsafeBlocks` and takes no `PackageReference` and no `ProjectReference`. There is no transitive code to audit, and the CLR's bounds checks are not given up for speed.
- Nothing in it opens a file, resolves a path, starts a process, or reflects. It is handed bytes and returns pixels.
- Every declared length is treated as a claim to verify. A length that is negative, that overflows when added to the cursor, or that runs past the enclosing chunk ends the walk rather than being followed.
- `VideoLimits` bounds frame dimensions, pixel count per frame, file size, chunk size, frame count, RIFF nesting depth and, for Indeo 3, cell recursion depth. Indeo 4 needs no limit of its own: its loops run over the picture's macroblocks and a block's 64 coefficients, and every motion-compensated read is checked against the decoder's own buffers. Frame area is computed as `long` so two dimensions that each pass the per-axis cap cannot wrap when multiplied.
- MS-RLE, Microsoft Video 1 and Cinepak write pixels through one clip, `VideoFrame.SetPixel`, so a malformed run cannot reach another row or past the buffer. The Indeo decoders work in their own plane buffers and convert to the frame at the end: Indeo 3 checks each cell and motion vector against the plane before it writes, and Indeo 4 checks every motion-compensated read and writes only inside each plane's padded rows. Run and skip counts come straight out of the bitstream and are never trusted as bounds.
- Decoding work is bounded by the picture's size and the packet's length, never by a count read from the stream alone: every loop either consumes input or moves forward through the picture or a block. A Cinepak strip can carry several vector chunks and repaint itself once for each, so the work grows with the packet, at up to 16 pixel writes per input byte.
- Malformed input returns null or false. No decoder is meant to throw on bad data; as a backstop, `MoviePlayback.Advance` treats an exception from a decoder as a failed frame and keeps it in `DecodeException`, so a missed check ends one cutscene rather than the host. The front end reports the failure and moves to the next movie.

Two checks sit outside the assembly, at the points where its input arrives and its output leaves:

- The hosts read a movie through `MovieHost.ReadMovieFile`, which refuses a file larger than `VideoLimits.MaxFileBytes` before allocating anything, rather than reading it whole and leaving the parser to reject it.
- `GpuTexture` refuses a pixel buffer too short for the size it is given. GL reads `width * height * 4` bytes through a raw pointer, and a movie frame's size comes from the file.

`tools/scripts/indeo4_reference`, which checks the Indeo 4 decoder against the retail `IR41_32.DLL`, loads that DLL natively and feeds it the movie — the exposure everything above avoids. It is a development tool in neither solution, for use on the retail files only.

The suite covers these directly: every prefix of a valid file is parsed to prove truncation is safe at any length, a chunk is rewritten to claim `0x7FFFFFFF` bytes, and every prefix of an MS-RLE opcode stream and of a Microsoft Video 1 packet is decoded. The Indeo 3, Indeo 4 and Cinepak decoders are fed truncated and randomly damaged packets, Indeo 4 single bit flips as well, and none may throw. The Indeo 4 tests and one of the Indeo 3 tests run over the retail files and pass vacuously without an install. The tests call the decoders directly, so the backstop in `MoviePlayback.Advance` cannot hide a fault from them.

## Indeo output

Both Indeo decoders convert their decoded planes to RGBA with chroma sampled nearest-neighbour, using ITU-R BT.601 at studio range. The matrix is this engine's choice, not one read from `IR32_32.DLL` or `IR41_32.DLL`.

The Indeo 4 decoder (`HercWorks.Video.Codecs.Indeo4.Indeo4Decoder`) matches `IR41_32.DLL`'s YUV planes sample for sample, and keeps the codec's own sample order in its buffers to do so; see its class comment. It decodes the subset of the format `ES2DROP3.AVI` uses and refuses the rest, listed in [`formats/indeo4.md`](../formats/indeo4.md#open).

## Open

- **Open:** a stereo path through `IAudioBackend`, so a stereo cutscene track plays in stereo as it does in retail.
- **Open:** MS-RLE and Microsoft Video 1 have truncation tests but no random-damage tests, and no damage test is coverage-guided: each feeds seeded random damage, which reaches only the paths that damage happens to.
