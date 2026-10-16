using System.Buffers.Binary;
using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;
using HercWorks.Video.Codecs.Indeo3;
using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// The Indeo 3 frame decoder: headers, the cell tree, the cell data, and robustness against
/// damaged streams.
///
/// <para>The hand-built frame here is the smallest the codec allows, 16x16, and repeats the
/// real-frame values <see cref="Indeo3CodebookTests.RowDeltaReproducesKnownFrameValues"/> pins, this
/// time through the whole decoder: the tree, the dyad byte order, the quad split and the null-line
/// escapes all have to agree for those rows to come out as 10 and 8.</para>
/// </summary>
public sealed class Indeo3DecoderTests {
	private const int Side = 16;

	/// <summary>
	/// Luma for the hand-built frame. Tree bits <c>10 11</c> (intra, then coded cell), then the cell:
	/// mode 0 with codebook 0, a dyad code <c>6C</c> with its partner <c>6C</c> for row 0, the quad
	/// code <c>D3</c> for row 1, <c>FD</c> to repeat row 1 down rows 2 and 3, and <c>FB 0F</c> to fill
	/// the other fifteen blocks with a null delta from the prediction row.
	/// </summary>
	private static readonly byte[] LumaPlane = [0, 0, 0, 0, 0xB0, 0x00, 0x6C, 0x6C, 0xD3, 0xFD, 0xFB, 0x0F];

	/// <summary>A 4x4 chroma plane: one intra cell, one block, a null delta over all four lines.</summary>
	private static readonly byte[] ChromaPlane = [0, 0, 0, 0, 0xB0, 0x00, 0xFD];

	/// <summary>
	/// The hand-built frame's pixels, through the whole decoder. Row 0 of the first block is 10,
	/// rows 1 to 3 are 8, and that 8 carries down the column of blocks below it; everything else is
	/// the prediction row's 0x40. With neutral chroma, doubled to 8 bits and converted at studio range,
	/// those are 5, 0 and 130.
	/// </summary>
	[Fact]
	public void DecodesAHandBuiltIntraFrame() {
		VideoFrame frame = Decode(BuildFrame(LumaPlane, ChromaPlane, ChromaPlane), out bool ok);

		Assert.True(ok);
		for (int x = 0; x < 4; x++) {
			Assert.Equal(5, Red(frame, x, 0));
			Assert.Equal(0, Red(frame, x, 1));
			Assert.Equal(0, Red(frame, x, 3));
		}

		// The blocks beside the first predict from the prediction row; the blocks below it predict
		// from its bottom row, and carry the 8 down.
		Assert.Equal(130, Red(frame, 4, 0));
		Assert.Equal(130, Red(frame, 15, 15));
		Assert.Equal(0, Red(frame, 0, 4));
		Assert.Equal(0, Red(frame, 3, 15));

		// Neutral chroma: grey.
		Assert.Equal(Red(frame, 15, 15), frame.Rgba[(((15 * Side) + 15) * 4) + 1]);
		Assert.Equal(Red(frame, 15, 15), frame.Rgba[(((15 * Side) + 15) * 4) + 2]);
	}

	/// <summary>
	/// The frame header's checksum is the XOR of the other three fields with <c>FRMH</c>. A frame
	/// whose checksum disagrees is rejected rather than decoded.
	/// </summary>
	[Fact]
	public void RejectsABadFrameChecksum() {
		byte[] packet = BuildFrame(LumaPlane, ChromaPlane, ChromaPlane);
		packet[8] ^= 1;

		Decode(packet, out bool ok);
		Assert.False(ok);
	}

	/// <summary>Only bitstream version 0x20 is Indeo 3.2.</summary>
	[Fact]
	public void RejectsAnotherBitstreamVersion() {
		byte[] packet = BuildFrame(LumaPlane, ChromaPlane, ChromaPlane);
		packet[16] = 0x1F;

		Decode(packet, out bool ok);
		Assert.False(ok);
	}

	/// <summary>
	/// A bitstream of exactly 16 bytes is a sync frame: it decodes successfully and changes nothing.
	/// </summary>
	[Fact]
	public void ASyncFrameChangesNothing() {
		IVideoCodec codec = CreateCodec();
		var frame = new VideoFrame(Side, Side);
		Assert.True(codec.DecodeFrame(BuildFrame(LumaPlane, ChromaPlane, ChromaPlane), frame));
		byte[] before = (byte[])frame.Rgba.Clone();

		byte[] sync = new byte[32];
		BinaryPrimitives.WriteUInt16LittleEndian(sync.AsSpan(16), 0x20);
		BinaryPrimitives.WriteUInt32LittleEndian(sync.AsSpan(20), 16 * 8);
		WriteFrameHeader(sync, 16);

		Assert.True(codec.DecodeFrame(sync, frame));
		Assert.Equal(before, frame.Rgba);
	}

	/// <summary>
	/// A plane offset that points outside the frame is rejected before any plane is read. The
	/// offsets are stream-supplied <c>uint32</c> values.
	/// </summary>
	[Fact]
	public void RejectsAPlaneOffsetOutsideTheFrame() {
		byte[] packet = BuildFrame(LumaPlane, ChromaPlane, ChromaPlane);
		BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16 + 16), 0xFFFF_FFF0);

		Decode(packet, out bool ok);
		Assert.False(ok);
	}

	/// <summary>
	/// Every prefix of a valid frame decodes or is rejected; none throws. The frame header's own
	/// size field is left claiming the full length, as a truncated file would.
	/// </summary>
	[Fact]
	public void SurvivesEveryTruncation() {
		byte[] full = BuildFrame(LumaPlane, ChromaPlane, ChromaPlane);
		IVideoCodec codec = CreateCodec();

		for (int length = 0; length <= full.Length; length++) {
			var frame = new VideoFrame(Side, Side);
			Assert.Null(Record.Exception(() => codec.DecodeFrame(full.AsSpan(0, length), frame)));
		}
	}

	/// <summary>
	/// Random damage to the plane data — tree codes, cell descriptors, VQ codes, escape counts,
	/// motion vectors — never throws. The headers are left intact so the damage reaches the cell
	/// decoder instead of being turned away at the door.
	/// </summary>
	[Fact]
	public void SurvivesCorruptedPlaneData() {
		byte[] original = BuildFrame(LumaPlane, ChromaPlane, ChromaPlane);
		IVideoCodec codec = CreateCodec();
		var random = new Random(1996);

		for (int trial = 0; trial < 5000; trial++) {
			byte[] packet = (byte[])original.Clone();
			int hits = 1 + random.Next(4);
			for (int i = 0; i < hits; i++) {
				packet[64 + random.Next(packet.Length - 64)] = (byte)random.Next(256);
			}

			var frame = new VideoFrame(Side, Side);
			Assert.Null(Record.Exception(() => codec.DecodeFrame(packet, frame)));
		}
	}

	/// <summary>
	/// The same, over real frames: the start of the intro, where frames switch between the two
	/// buffers and inter cells carry motion vectors. Passes vacuously without the retail file.
	/// </summary>
	[Fact]
	public void SurvivesCorruptedRetailFrames() {
		string? path = RetailFiles.Find(Path.Combine("AVI", "INTR_PT1.AVI"));
		if (path is null) {
			return;
		}

		AviFile avi = AviFile.Open(File.ReadAllBytes(path))!;
		var random = new Random(2);

		for (int trial = 0; trial < 50; trial++) {
			IVideoCodec codec = CodecRegistry.Create(avi.VideoFormat!, avi.Limits)!;
			var frame = new VideoFrame(avi.VideoFormat!.Width, avi.VideoFormat.Height);

			for (int index = 0; index < 8; index++) {
				byte[] packet = avi.PacketData(avi.VideoPackets[index]).ToArray();
				for (int i = 0; i < 8; i++) {
					packet[64 + random.Next(packet.Length - 64)] = (byte)random.Next(256);
				}

				Assert.Null(Record.Exception(() => codec.DecodeFrame(packet, frame)));
			}
		}
	}

	/// <summary>
	/// The whole of <c>INTR_PT1.AVI</c> decodes without a malformed frame, and frames 0 and 400 match
	/// digests of this decoder's output. The digests pin the output against regression; what they
	/// were checked against is a look at the frames themselves. Passes vacuously without the retail
	/// file.
	/// </summary>
	[Fact]
	public void DecodesTheRetailIntro() {
		string? path = RetailFiles.Find(Path.Combine("AVI", "INTR_PT1.AVI"));
		if (path is null) {
			return;
		}

		MoviePlayback playback = MoviePlayback.Open(File.ReadAllBytes(path))!;
		Assert.Equal(604, playback.FrameCount);

		playback.Advance(TimeSpan.FromTicks(1));
		Assert.Equal(0, playback.CurrentFrameIndex);
		Assert.Equal(0x8A73D62AC736DDA5UL, Digest(playback.Frame.Rgba));

		// A Herc among flames, deep in a run of inter frames.
		playback.Advance(playback.FrameInterval * 400);
		Assert.Equal(400, playback.CurrentFrameIndex);
		Assert.Equal(0x8080F98512A13B34UL, Digest(playback.Frame.Rgba));

		playback.Advance(playback.Duration!.Value);
		Assert.False(playback.HasFailed);
		Assert.Equal(603, playback.CurrentFrameIndex);
	}

	/// <summary>The requantisation table matches the retail DLL. Passes vacuously without it.</summary>
	[Fact]
	public void RequantTableMatchesRetailDll() {
		byte[] table = Indeo3Requant.Table;

		// Row 0 is the step-2 staircase; rows 3 and 4 start at 4 because the division truncates.
		Assert.Equal(new byte[] { 0, 2, 2, 4, 4, 6, 6, 8 }, table[..8]);
		Assert.Equal(4, table[3 * 128]);
		Assert.Equal(4, table[4 * 128]);

		string? dll = RetailFiles.Find(Path.Combine("INDEO", "IR32_32.DLL"));
		if (dll is null) {
			return;
		}

		byte[] image = File.ReadAllBytes(dll);
		int at = RetailFiles.VirtualToFileOffset(image, Indeo3Requant.TableVirtualAddress);
		Assert.Equal(image[at..(at + table.Length)], table);
	}

	/// <summary>Dimensions a 4x4-block codec cannot tile are refused at creation.</summary>
	[Fact]
	public void RefusesDimensionsThatAreNotWholeBlocks() {
		Assert.Null(Indeo3Decoder.Create(Format(18, 16), VideoLimits.Default));
		Assert.Null(Indeo3Decoder.Create(Format(16, 12), VideoLimits.Default));
		Assert.NotNull(Indeo3Decoder.Create(Format(16, 16), VideoLimits.Default));
	}

	private static AviVideoFormat Format(int width, int height) =>
		new(width, height, false, 24, CodecRegistry.Indeo3, []);

	private static IVideoCodec CreateCodec() => Indeo3Decoder.Create(Format(Side, Side), VideoLimits.Default)!;

	private static VideoFrame Decode(byte[] packet, out bool ok) {
		var frame = new VideoFrame(Side, Side);
		ok = CreateCodec().DecodeFrame(packet, frame);
		return frame;
	}

	/// <summary>
	/// Assembles a key frame into buffer 0: the frame header, the 48-byte bitstream header, then the
	/// planes in the order the retail files use, U, V, Y.
	/// </summary>
	private static byte[] BuildFrame(byte[] y, byte[] u, byte[] v) {
		const int header = 48;
		int uAt = header;
		int vAt = uAt + u.Length;
		int yAt = vAt + v.Length;
		int dataBytes = yAt + y.Length;

		byte[] packet = new byte[16 + dataBytes];
		Span<byte> bs = packet.AsSpan(16);
		BinaryPrimitives.WriteUInt16LittleEndian(bs, 0x20);
		BinaryPrimitives.WriteUInt16LittleEndian(bs[2..], 0x0005);
		BinaryPrimitives.WriteUInt32LittleEndian(bs[4..], (uint)dataBytes * 8);
		BinaryPrimitives.WriteUInt16LittleEndian(bs[12..], Side);
		BinaryPrimitives.WriteUInt16LittleEndian(bs[14..], Side);
		BinaryPrimitives.WriteUInt32LittleEndian(bs[16..], (uint)yAt);
		BinaryPrimitives.WriteUInt32LittleEndian(bs[20..], (uint)vAt);
		BinaryPrimitives.WriteUInt32LittleEndian(bs[24..], (uint)uAt);
		u.CopyTo(bs[uAt..]);
		v.CopyTo(bs[vAt..]);
		y.CopyTo(bs[yAt..]);

		WriteFrameHeader(packet, dataBytes);
		return packet;
	}

	private static void WriteFrameHeader(byte[] packet, int dataBytes) {
		const uint frameNumber = 0;
		BinaryPrimitives.WriteUInt32LittleEndian(packet, frameNumber);
		BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), frameNumber ^ (uint)dataBytes ^ 0x4652_4D48);
		BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), (uint)dataBytes);
	}

	private static ulong Digest(byte[] bytes) {
		ulong hash = 0xcbf2_9ce4_8422_2325;
		foreach (byte b in bytes) {
			hash = (hash ^ b) * 0x0000_0100_0000_01b3;
		}

		return hash;
	}

	private static int Red(VideoFrame frame, int x, int y) => frame.Rgba[((y * frame.Width) + x) * 4];
}
