namespace HercWorks.Video;

/// <summary>
/// The ceilings every parser and decoder in this assembly checks against before it allocates or
/// loops.
///
/// <para>A video file is the one kind of game asset that is plausibly obtained from somewhere other
/// than the user's own install — a mod, a fan re-release, a download. So nothing here trusts a
/// declared size. Each limit exists to bound a specific failure the formats otherwise allow:</para>
///
/// <list type="bullet">
/// <item>A frame's dimensions come from the stream, and every codec multiplies them out to size a
/// buffer. Unbounded, a four-byte field asks for a 16-exabyte allocation.</item>
/// <item>RIFF chunks nest, and a <c>LIST</c> may contain a <c>LIST</c>. Unbounded, a file that
/// nests a few thousand deep overflows the parse stack before any length check runs.</item>
/// <item>Indeo 3 splits a plane by recursive binary subdivision, with the split codes coming from
/// the bitstream. Unbounded, a stream of nothing but split codes recurses without limit.</item>
/// <item>Run-length opcodes in MS-RLE, MS Video 1 and Indeo 3 all advance a write cursor by a
/// count read from the stream. Bounding the cursor stops a malformed run writing past its row;
/// bounding the opcode count stops a run that never terminates.</item>
/// </list>
///
/// <para>The defaults are generous next to the retail corpus — the largest shipped file is
/// <c>CREDITS.AVI</c> at 576x360 and 10 MB — and are set to be uncontroversial for any plausible
/// replacement asset, not to be tight. A caller that knows better can pass its own.</para>
/// </summary>
public sealed record VideoLimits {
	/// <summary>The limits used when a caller does not supply any.</summary>
	public static VideoLimits Default { get; } = new();

	/// <summary>
	/// Largest accepted frame width or height, in pixels. 4096 is far above anything a 1996 AVI
	/// carries while still leaving a decoded RGBA frame under 64 MB.
	/// </summary>
	public int MaxDimension { get; init; } = 4096;

	/// <summary>
	/// Largest accepted pixel count per frame, checked as width*height so that an extreme aspect
	/// ratio cannot slip past <see cref="MaxDimension"/> — 4096x4096 passes both, 4096x4095 passes
	/// the first but is what this one is here to size.
	/// </summary>
	public int MaxPixelsPerFrame { get; init; } = 4096 * 4096;

	/// <summary>
	/// Largest accepted file held in memory (<see cref="Avi.AviFile.Open"/>), in bytes. A streamed movie
	/// (<see cref="MoviePlayback.Open(Stream, VideoLimits?)"/>) is never held whole, and has no such limit.
	/// </summary>
	public long MaxFileBytes { get; init; } = 256L * 1024 * 1024;

	/// <summary>
	/// How far a streamed movie is read ahead of playback, in bytes of packets waiting to be played. Four
	/// megabytes is about twenty seconds of the retail cutscenes, which need about 175 KB a second, and a
	/// packet larger than this on its own is still taken.
	/// </summary>
	public int ReadAheadBytes { get; init; } = 4 * 1024 * 1024;

	/// <summary>
	/// Largest accepted header region of a streamed movie: everything before its first <c>movi</c> list,
	/// which the reader holds to parse. Retail files carry about 4 KB there.
	/// </summary>
	public int MaxHeaderBytes { get; init; } = 1024 * 1024;

	/// <summary>Largest accepted single compressed chunk, in bytes.</summary>
	public int MaxChunkBytes { get; init; } = 32 * 1024 * 1024;

	/// <summary>
	/// Largest accepted number of frames in a stream. Bounds the index this assembly builds while
	/// walking the movie list, which is the one allocation that grows with chunk count rather than
	/// with file size.
	/// </summary>
	public int MaxFrames { get; init; } = 1_000_000;

	/// <summary>How deep <c>LIST</c> chunks may nest before the file is rejected.</summary>
	public int MaxRiffDepth { get; init; } = 16;

	/// <summary>
	/// How deep Indeo 3's cell-splitting tree may go before the frame is rejected. A plane is at
	/// most <see cref="MaxDimension"/> across and a cell bottoms out at 4 pixels, so ten levels is
	/// already past what a well-formed stream can use; twenty is slack on top of that.
	/// </summary>
	public int MaxCellDepth { get; init; } = 20;
}
