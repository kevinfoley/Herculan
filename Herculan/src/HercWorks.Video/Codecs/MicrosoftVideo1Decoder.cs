using HercWorks.Video.Avi;

namespace HercWorks.Video.Codecs;

/// <summary>
/// Microsoft Video 1 (<c>CRAM</c>/<c>msvc</c>), 16 bits per pixel. The bitstream is described in
/// docs/retail/formats/avi-video.md#microsoft-video-1; this class owns only the pixel writing.
///
/// <para>Only the 16-bit form is handled. The 8-bit palettised form appears nowhere in the retail
/// corpus, so <see cref="Create"/> refuses it rather than guessing at it, as it does a picture whose
/// sides are not whole blocks.</para>
/// </summary>
internal sealed class MicrosoftVideo1Decoder : IVideoCodec {
	private const int Block = 4;

	private MicrosoftVideo1Decoder(AviVideoFormat format) {
		_topDown = format.TopDown;
		_blocksWide = format.Width / Block;
		_blocksHigh = format.Height / Block;
	}

	private readonly bool _topDown;
	private readonly int _blocksWide;
	private readonly int _blocksHigh;

	/// <summary>Creates a decoder, or returns null for a format this class does not handle.</summary>
	internal static MicrosoftVideo1Decoder? Create(AviVideoFormat format) {
		if (format.BitCount != 16 || format.Width % Block != 0 || format.Height % Block != 0) {
			return null;
		}

		return new MicrosoftVideo1Decoder(format);
	}

	public bool DecodeFrame(ReadOnlySpan<byte> packet, VideoFrame frame) {
		ArgumentNullException.ThrowIfNull(frame);

		// An empty packet is a legitimate "nothing changed" frame, not an error.
		if (packet.Length == 0) {
			return true;
		}

		Span<ushort> colours = stackalloc ushort[8];
		int total = _blocksWide * _blocksHigh;
		int at = 0;
		int block = 0;

		while (block < total) {
			if (at + 2 > packet.Length) {
				return false;
			}

			ushort code = ReadWord(packet, at);
			at += 2;

			if ((code & 0xFC00) == 0x8400) {
				// Skip run: the blocks keep what the previous frame left there.
				block += code - 0x8400;
				continue;
			}

			if ((code & 0x8000) != 0) {
				// One colour over the whole block.
				FillBlock(frame, block, code);
				block++;
				continue;
			}

			// Two or eight colours, chosen per pixel by the 16 flag bits in the code.
			if (at + 4 > packet.Length) {
				return false;
			}

			colours[0] = ReadWord(packet, at);
			colours[1] = ReadWord(packet, at + 2);
			at += 4;

			if ((colours[0] & 0x8000) != 0) {
				if (at + 12 > packet.Length) {
					return false;
				}

				for (int i = 2; i < 8; i++) {
					colours[i] = ReadWord(packet, at);
					at += 2;
				}

				PaintBlock(frame, block, code, colours, quadrants: true);
			} else {
				PaintBlock(frame, block, code, colours, quadrants: false);
			}

			block++;
		}

		return true;
	}

	private static ushort ReadWord(ReadOnlySpan<byte> packet, int at) => (ushort)(packet[at] | (packet[at + 1] << 8));

	private void FillBlock(VideoFrame frame, int block, ushort colour) {
		for (int row = 0; row < Block; row++) {
			for (int column = 0; column < Block; column++) {
				Plot(frame, block, column, row, colour);
			}
		}
	}

	/// <summary>
	/// Paints a flagged block. Flag bit <c>n</c> is pixel <c>n</c> in stream order — four to a row,
	/// bottom row first — and a set bit picks the pair's first colour. With
	/// <paramref name="quadrants"/>, each 2x2 quadrant has its own pair: bottom-left 0-1,
	/// bottom-right 2-3, top-left 4-5, top-right 6-7.
	/// </summary>
	private void PaintBlock(VideoFrame frame, int block, ushort flags, ReadOnlySpan<ushort> colours, bool quadrants) {
		for (int row = 0; row < Block; row++) {
			for (int column = 0; column < Block; column++, flags >>= 1) {
				int pair = quadrants ? ((row & 2) << 1) + (column & 2) : 0;
				Plot(frame, block, column, row, colours[pair + ((flags & 1) ^ 1)]);
			}
		}
	}

	/// <summary>
	/// Plots one RGB555 pixel of a block. Blocks run left to right along a block row, and block rows
	/// and the pixel rows within a block run bottom-up unless the format header says otherwise.
	/// </summary>
	private void Plot(VideoFrame frame, int block, int column, int row, ushort colour) {
		int x = ((block % _blocksWide) * Block) + column;
		int streamRow = ((block / _blocksWide) * Block) + row;
		int y = _topDown ? streamRow : frame.Height - 1 - streamRow;

		frame.SetPixel(x, y, Expand(colour >> 10), Expand(colour >> 5), Expand(colour));
	}

	/// <summary>Widens a 5-bit channel to 8 by repeating its top bits, so white stays 255.</summary>
	private static byte Expand(int channel) {
		channel &= 0x1F;
		return (byte)((channel << 3) | (channel >> 2));
	}
}
