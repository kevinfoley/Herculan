using HercWorks.Video.Riff;

namespace HercWorks.Video.Avi;

/// <summary>Which kind of data a stream carries. Anything else in the file is ignored.</summary>
public enum AviStreamKind {
	/// <summary>A stream this reader does not handle.</summary>
	Other,

	/// <summary><c>vids</c> — compressed video.</summary>
	Video,

	/// <summary><c>auds</c> — audio.</summary>
	Audio,
}

/// <summary>
/// One packet of stream data, located in the movie list.
/// </summary>
/// <param name="At">Offset of the packet's first byte within the file.</param>
/// <param name="Length">Length of the packet in bytes.</param>
/// <param name="IsKeyFrame">
/// Whether the index marked this packet as a key frame. False when the file has no index, in which
/// case a codec must fall back on what its own bitstream says.
/// </param>
public readonly record struct AviPacket(int At, int Length, bool IsKeyFrame);

/// <summary>
/// The parts of <c>BITMAPINFOHEADER</c> a decoder needs.
///
/// <para>Height is stored signed and is usually positive, which for a DIB means bottom-up row
/// order. The codecs here each state which convention they emit, and <see cref="AviFile"/> exposes
/// <see cref="TopDown"/> so a caller does not have to re-derive it from the sign.</para>
/// </summary>
public sealed record AviVideoFormat(
	int Width, int Height, bool TopDown, ushort BitCount, uint Compression, byte[] Palette);

/// <summary>
/// The parts of <c>WAVEFORMATEX</c> a decoder needs. Only uncompressed PCM is described here
/// because that is all the retail corpus uses.
/// </summary>
public sealed record AviAudioFormat(
	ushort FormatTag, int Channels, int SampleRate, ushort BitsPerSample);

/// <summary>
/// A parsed AVI file: its streams, and where each stream's packets sit.
///
/// <para>This deliberately does not use the <c>idx1</c> index to find packets. The index is
/// optional, several retail files disagree with it about chunk offsets, and — more to the point —
/// it is a table of offsets supplied by the file, so following it means trusting attacker-chosen
/// pointers. Walking the <c>movi</c> list instead means every packet's bounds were established by
/// the walk itself. The index is read only for its key-frame flags, and only for packets the walk
/// already found.</para>
///
/// <para>Parsing is total: a malformed file yields null from <see cref="Open"/>, and a file that is
/// well-formed up to a point yields the streams and packets found before the damage. Nothing here
/// throws on bad input.</para>
/// </summary>
public sealed class AviFile {
	private AviFile(byte[] bytes, VideoLimits limits) {
		Bytes = bytes;
		Limits = limits;
	}

	/// <summary>The whole file. Packet offsets index into this.</summary>
	public byte[] Bytes { get; }

	/// <summary>The ceilings this file was parsed under, and that its decoders should use.</summary>
	public VideoLimits Limits { get; }

	/// <summary>Frame period in microseconds, as <c>avih</c> declared it.</summary>
	public int MicrosecondsPerFrame { get; private set; }

	/// <summary>The video format, or null when the file has no video stream this reader understood.</summary>
	public AviVideoFormat? VideoFormat { get; private set; }

	/// <summary>The audio format, or null when there is no audio stream.</summary>
	public AviAudioFormat? AudioFormat { get; private set; }

	/// <summary>Video packets, in file order.</summary>
	public IReadOnlyList<AviPacket> VideoPackets => _videoPackets;

	/// <summary>Audio packets, in file order.</summary>
	public IReadOnlyList<AviPacket> AudioPackets => _audioPackets;

	/// <summary>Frames per second, derived from <see cref="MicrosecondsPerFrame"/>.</summary>
	public double FramesPerSecond =>
		MicrosecondsPerFrame > 0 ? 1_000_000.0 / MicrosecondsPerFrame : 15.0;

	private readonly List<AviPacket> _videoPackets = [];
	private readonly List<AviPacket> _audioPackets = [];

	/// <summary>
	/// Parses an AVI file held in memory, or returns null when it is not one, is larger than
	/// <paramref name="limits"/> allows, or declares a frame size outside those limits.
	/// </summary>
	public static AviFile? Open(byte[] bytes, VideoLimits? limits = null) {
		ArgumentNullException.ThrowIfNull(bytes);
		limits ??= VideoLimits.Default;

		if (bytes.LongLength > limits.MaxFileBytes) {
			return null;
		}

		if (!RiffReader.TryReadHeader(bytes, out RiffChunk riff)
			|| riff.ListType != RiffReader.FourCc('A', 'V', 'I', ' ')) {
			return null;
		}

		var file = new AviFile(bytes, limits);
		int riffEnd = riff.BodyAt + riff.BodyLength;

		// The header list and the movie list are siblings under RIFF. Read headers first so the
		// stream-number-to-kind mapping is known before the movie list is walked.
		foreach (RiffChunk chunk in RiffReader.Children(bytes, riff.BodyAt + 4, riffEnd)) {
			if (chunk.Id != RiffReader.FourCc('L', 'I', 'S', 'T')) {
				continue;
			}

			if (chunk.ListType == RiffReader.FourCc('h', 'd', 'r', 'l')) {
				file.ReadHeaderList(chunk);
			}
		}

		if (file.VideoFormat is null) {
			return null;
		}

		foreach (RiffChunk chunk in RiffReader.Children(bytes, riff.BodyAt + 4, riffEnd)) {
			if (chunk.Id == RiffReader.FourCc('L', 'I', 'S', 'T')
				&& chunk.ListType == RiffReader.FourCc('m', 'o', 'v', 'i')) {
				file.ReadMovieList(chunk);
			}
		}

		return file;
	}

	/// <summary>Reads a packet's bytes. The bounds were established when the packet was found.</summary>
	public ReadOnlySpan<byte> PacketData(AviPacket packet) =>
		Bytes.AsSpan(packet.At, packet.Length);

	/// <summary>
	/// Walks the <c>hdrl</c> list, picking up the main header and each stream's header and format.
	///
	/// <para>Stream numbers are assigned by position: the n-th <c>strl</c> in the file is stream n,
	/// which is what the <c>NNxx</c> chunk ids in the movie list refer to.</para>
	/// </summary>
	private void ReadHeaderList(RiffChunk hdrl) {
		int end = hdrl.BodyAt + hdrl.BodyLength;
		int streamIndex = 0;

		foreach (RiffChunk chunk in RiffReader.Children(Bytes, hdrl.BodyAt + 4, end)) {
			if (chunk.Id == RiffReader.FourCc('a', 'v', 'i', 'h') && chunk.BodyLength >= 4) {
				MicrosecondsPerFrame = RiffReader.ReadI32(Bytes, chunk.BodyAt);
			} else if (chunk.Id == RiffReader.FourCc('L', 'I', 'S', 'T')
				&& chunk.ListType == RiffReader.FourCc('s', 't', 'r', 'l')) {
				ReadStreamList(chunk, streamIndex);
				streamIndex++;
			}
		}

		StreamCount = streamIndex;
	}

	/// <summary>How many <c>strl</c> lists the header carried.</summary>
	public int StreamCount { get; private set; }

	private readonly Dictionary<int, AviStreamKind> _streamKinds = [];

	/// <summary>
	/// The streams <see cref="VideoFormat"/> and <see cref="AudioFormat"/> were read from, or -1.
	/// Only their packets are kept: a file can carry a second stream of either kind, and its packets
	/// are in another codec or another picture size.
	/// </summary>
	private int _videoStream = -1;

	private int _audioStream = -1;

	private void ReadStreamList(RiffChunk strl, int streamIndex) {
		int end = strl.BodyAt + strl.BodyLength;
		var kind = AviStreamKind.Other;

		foreach (RiffChunk chunk in RiffReader.Children(Bytes, strl.BodyAt + 4, end)) {
			if (chunk.Id == RiffReader.FourCc('s', 't', 'r', 'h') && chunk.BodyLength >= 4) {
				uint type = RiffReader.ReadU32(Bytes, chunk.BodyAt);
				kind = type == RiffReader.FourCc('v', 'i', 'd', 's') ? AviStreamKind.Video
					: type == RiffReader.FourCc('a', 'u', 'd', 's') ? AviStreamKind.Audio
					: AviStreamKind.Other;
				_streamKinds[streamIndex] = kind;
			} else if (chunk.Id == RiffReader.FourCc('s', 't', 'r', 'f')) {
				if (kind == AviStreamKind.Video && VideoFormat is null) {
					VideoFormat = ReadVideoFormat(chunk);
					_videoStream = VideoFormat is null ? -1 : streamIndex;
				} else if (kind == AviStreamKind.Audio && AudioFormat is null) {
					AudioFormat = ReadAudioFormat(chunk);
					_audioStream = AudioFormat is null ? -1 : streamIndex;
				}
			}
		}
	}

	/// <summary>
	/// Reads a <c>BITMAPINFOHEADER</c>, rejecting dimensions outside <see cref="Limits"/>.
	///
	/// <para>A negative height means top-down rows. It is negated here with an explicit guard rather
	/// than with <c>Math.Abs</c>, because <c>Math.Abs(int.MinValue)</c> throws — and a four-byte
	/// height field can hold exactly that.</para>
	/// </summary>
	private AviVideoFormat? ReadVideoFormat(RiffChunk strf) {
		if (strf.BodyLength < 40) {
			return null;
		}

		int width = RiffReader.ReadI32(Bytes, strf.BodyAt + 4);
		int rawHeight = RiffReader.ReadI32(Bytes, strf.BodyAt + 8);
		ushort bitCount = RiffReader.ReadU16(Bytes, strf.BodyAt + 14);
		uint compression = RiffReader.ReadU32(Bytes, strf.BodyAt + 16);

		bool topDown = rawHeight < 0;
		if (rawHeight == int.MinValue) {
			return null;
		}

		int height = topDown ? -rawHeight : rawHeight;
		if (!IsFrameSizeAllowed(width, height)) {
			return null;
		}

		// A palette, when present, follows the 40-byte header as BGRX quads. Only the entries that
		// actually fit in the chunk are taken; a header claiming 256 colours in a 40-byte chunk gets
		// however many are really there.
		int paletteAt = strf.BodyAt + 40;
		int paletteBytes = Math.Max(0, Math.Min(256 * 4, strf.BodyAt + strf.BodyLength - paletteAt));
		var palette = new byte[paletteBytes];
		Array.Copy(Bytes, paletteAt, palette, 0, paletteBytes);

		return new AviVideoFormat(width, height, topDown, bitCount, compression, palette);
	}

	private AviAudioFormat? ReadAudioFormat(RiffChunk strf) {
		if (strf.BodyLength < 16) {
			return null;
		}

		ushort formatTag = RiffReader.ReadU16(Bytes, strf.BodyAt);
		int channels = RiffReader.ReadU16(Bytes, strf.BodyAt + 2);
		int sampleRate = RiffReader.ReadI32(Bytes, strf.BodyAt + 4);
		ushort bits = RiffReader.ReadU16(Bytes, strf.BodyAt + 14);

		if (channels is < 1 or > 8 || sampleRate is < 1000 or > 192_000) {
			return null;
		}

		return new AviAudioFormat(formatTag, channels, sampleRate, bits);
	}

	/// <summary>
	/// Checks a frame size against every relevant limit, including the multiplication itself.
	///
	/// <para>The product is formed as <c>long</c> so that a width and height which each pass
	/// <see cref="VideoLimits.MaxDimension"/> cannot wrap to a small positive int when multiplied.</para>
	/// </summary>
	private bool IsFrameSizeAllowed(int width, int height) =>
		width > 0 && height > 0
		&& width <= Limits.MaxDimension && height <= Limits.MaxDimension
		&& (long)width * height <= Limits.MaxPixelsPerFrame;

	/// <summary>
	/// Walks the <c>movi</c> list, sorting packets into the video and audio lists by the stream
	/// number in their chunk id, and dropping those of any stream other than the two whose formats
	/// were taken.
	///
	/// <para>Packets are grouped into <c>rec&#160;</c> lists in some files and loose in others, so
	/// the walk descends through lists rather than assuming either layout.</para>
	/// </summary>
	private void ReadMovieList(RiffChunk movi) {
		int end = movi.BodyAt + movi.BodyLength;

		foreach (RiffChunk chunk in RiffReader.Leaves(
			Bytes, movi.BodyAt + 4, end, Limits.MaxRiffDepth)) {
			if (chunk.BodyLength > Limits.MaxChunkBytes) {
				continue;
			}

			if (!Classify(chunk.Id, out bool isVideo)) {
				continue;
			}

			var packet = new AviPacket(chunk.BodyAt, chunk.BodyLength, false);
			List<AviPacket> packets = isVideo ? _videoPackets : _audioPackets;
			if (packets.Count < Limits.MaxFrames) {
				packets.Add(packet);
			}
		}
	}

	/// <summary>
	/// Whether a movie-list chunk id is a packet of the video or the audio stream this file's formats were
	/// read from, and which. <see cref="AviStreamReader"/> sorts its packets by the same test.
	///
	/// <para>The two-letter suffix says what the payload is, and the stream header says what the stream
	/// is. Requiring both to agree keeps a mislabelled chunk out of the wrong list.</para>
	/// </summary>
	internal bool Classify(uint id, out bool isVideo) {
		if (!TryParsePacketId(id, out int stream, out isVideo)
			|| (stream != _videoStream && stream != _audioStream)) {
			return false;
		}

		_streamKinds.TryGetValue(stream, out AviStreamKind kind);
		return isVideo ? kind == AviStreamKind.Video : kind == AviStreamKind.Audio;
	}

	/// <summary>
	/// Decodes a movie-list chunk id of the form <c>NNxx</c>: two ASCII digits giving the stream
	/// number, then <c>dc</c> or <c>db</c> for video and <c>wb</c> for audio.
	/// </summary>
	private static bool TryParsePacketId(uint id, out int stream, out bool isVideo) {
		stream = 0;
		isVideo = false;

		char d0 = (char)(id & 0xFF);
		char d1 = (char)((id >> 8) & 0xFF);
		char s0 = (char)((id >> 16) & 0xFF);
		char s1 = (char)((id >> 24) & 0xFF);

		if (d0 is < '0' or > '9' || d1 is < '0' or > '9') {
			return false;
		}

		stream = ((d0 - '0') * 10) + (d1 - '0');

		if (s0 == 'd' && s1 is 'c' or 'b') {
			isVideo = true;
			return true;
		}

		if (s0 == 'w' && s1 == 'b') {
			isVideo = false;
			return true;
		}

		return false;
	}
}
