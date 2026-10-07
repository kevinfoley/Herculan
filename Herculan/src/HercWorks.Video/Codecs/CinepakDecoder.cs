using HercWorks.Video.Avi;

namespace HercWorks.Video.Codecs;

/// <summary>
/// Cinepak (<c>cvid</c>). The bitstream is described in docs/retail/formats/avi-video.md#cinepak; this
/// class owns the codebook storage and the pixel writing.
///
/// <para>Only the 24-bit colour form is handled. The 8-bit palettised form and the luma-only
/// codebook chunks appear nowhere in the retail corpus, so <see cref="Create"/> refuses the one and
/// a packet carrying the other fails, rather than either being guessed at.</para>
/// </summary>
internal sealed class CinepakDecoder : IVideoCodec {
	private const int FrameHeader = 10;
	private const int StripHeader = 12;
	private const int ChunkHeader = 4;
	private const int MaxStrips = 32;
	private const int CodebookEntries = 256;

	/// <summary>Bytes per codebook entry in the stream: four luma, then the U and V chroma pair.</summary>
	private const int StreamEntryBytes = 6;

	/// <summary>Bytes per stored codebook entry: four pixels of RGB.</summary>
	private const int EntryBytes = 12;

	private CinepakDecoder(AviVideoFormat format) {
		_width = format.Width;
	}

	private readonly int _width;

	/// <summary>
	/// Each strip's V1 and V4 codebooks, kept across frames: a strip that loads no codebook, or only
	/// some entries, decodes against what that strip held last frame. Entries are stored already
	/// converted to RGB, four pixels each in the order top-left, top-right, bottom-left, bottom-right.
	/// </summary>
	private readonly byte[][] _v1 = NewCodebooks();

	private readonly byte[][] _v4 = NewCodebooks();

	/// <summary>Creates a decoder, or returns null for a format this class does not handle.</summary>
	internal static CinepakDecoder? Create(AviVideoFormat format) {
		if (format.BitCount != 24) {
			return null;
		}

		return new CinepakDecoder(format);
	}

	private static byte[][] NewCodebooks() {
		var books = new byte[MaxStrips][];
		for (int i = 0; i < MaxStrips; i++) {
			books[i] = new byte[CodebookEntries * EntryBytes];
		}

		return books;
	}

	public bool DecodeFrame(ReadOnlySpan<byte> packet, VideoFrame frame) {
		ArgumentNullException.ThrowIfNull(frame);

		// An empty packet is a legitimate "nothing changed" frame, not an error.
		if (packet.Length == 0) {
			return true;
		}

		if (packet.Length < FrameHeader) {
			return false;
		}

		bool inheritCodebooks = (packet[0] & 0x01) == 0;
		int strips = ReadU16(packet, 8);
		if (strips > MaxStrips) {
			return false;
		}

		int at = FrameHeader;
		int top = 0;

		for (int strip = 0; strip < strips; strip++) {
			if (at + StripHeader > packet.Length) {
				return false;
			}

			int stripLength = ReadU24(packet, at + 1);
			if (stripLength < StripHeader || at + stripLength > packet.Length) {
				return false;
			}

			// The strip's bottom field is its height; strips stack down the picture.
			int bottom = top + ReadU16(packet, at + 8);

			if (strip > 0 && inheritCodebooks) {
				_v1[strip - 1].CopyTo(_v1[strip], 0);
				_v4[strip - 1].CopyTo(_v4[strip], 0);
			}

			if (!DecodeStrip(packet.Slice(at + StripHeader, stripLength - StripHeader), strip, top, bottom, frame)) {
				return false;
			}

			at += stripLength;
			top = bottom;
		}

		return true;
	}

	private bool DecodeStrip(ReadOnlySpan<byte> data, int strip, int top, int bottom, VideoFrame frame) {
		int at = 0;

		while (at + ChunkHeader <= data.Length) {
			int id = data[at];
			int length = ReadU24(data, at + 1);
			if (length < ChunkHeader || at + length > data.Length) {
				return false;
			}

			ReadOnlySpan<byte> body = data.Slice(at + ChunkHeader, length - ChunkHeader);

			switch (id) {
				case 0x20 or 0x21:
					LoadCodebook(_v4[strip], id, body);
					break;
				case 0x22 or 0x23:
					LoadCodebook(_v1[strip], id, body);
					break;
				case 0x30 or 0x31 or 0x32:
					if (!DecodeVectors(body, id, strip, top, bottom, frame)) {
						return false;
					}

					break;
				default:
					return false;
			}

			at += length;
		}

		return at == data.Length;
	}

	/// <summary>
	/// Loads codebook entries, converting each to RGB. Bit 0 of the chunk id makes the load
	/// selective — a 32-bit flag word precedes each 32 entries, and a clear flag leaves that entry as
	/// it was. A load stops at the end of the chunk, however few entries that is.
	/// </summary>
	private static void LoadCodebook(byte[] book, int id, ReadOnlySpan<byte> data) {
		bool selective = (id & 0x01) != 0;
		uint flags = 0;
		uint mask = 0;
		int at = 0;

		for (int entry = 0; entry < CodebookEntries; entry++) {
			if (selective && (mask >>= 1) == 0) {
				if (at + 4 > data.Length) {
					return;
				}

				flags = ReadU32(data, at);
				at += 4;
				mask = 0x8000_0000;
			}

			if (selective && (flags & mask) == 0) {
				continue;
			}

			if (at + StreamEntryBytes > data.Length) {
				return;
			}

			int u = (sbyte)data[at + 4];
			int v = (sbyte)data[at + 5];
			int o = entry * EntryBytes;

			for (int pixel = 0; pixel < 4; pixel++, o += 3) {
				int y = data[at + pixel];
				book[o] = Clamp(y + (v * 2));
				book[o + 1] = Clamp(y - (u / 2) - v);
				book[o + 2] = Clamp(y + (u * 2));
			}

			at += StreamEntryBytes;
		}
	}

	/// <summary>
	/// Paints a strip's 4x4 blocks, left to right and top to bottom. <c>0x30</c> has one flag per
	/// block choosing V4 (set) or V1; <c>0x31</c> first has one flag per block choosing to update it
	/// (set) or leave it, then for an updated block a second flag from the same stream choosing V4 or
	/// V1; <c>0x32</c> has no flags and every block is V1.
	/// </summary>
	private bool DecodeVectors(ReadOnlySpan<byte> data, int id, int strip, int top, int bottom, VideoFrame frame) {
		byte[] v1 = _v1[strip];
		byte[] v4 = _v4[strip];
		bool inter = (id & 0x01) != 0;
		bool v1Only = (id & 0x02) != 0;
		uint flags = 0;
		uint mask = 0;
		int at = 0;

		for (int y = top; y < bottom; y += 4) {
			for (int x = 0; x < _width; x += 4) {
				if (inter && (mask >>= 1) == 0) {
					if (at + 4 > data.Length) {
						return false;
					}

					flags = ReadU32(data, at);
					at += 4;
					mask = 0x8000_0000;
				}

				if (inter && (flags & mask) == 0) {
					continue;
				}

				if (!v1Only && (mask >>= 1) == 0) {
					if (at + 4 > data.Length) {
						return false;
					}

					flags = ReadU32(data, at);
					at += 4;
					mask = 0x8000_0000;
				}

				if (v1Only || (flags & mask) == 0) {
					if (at + 1 > data.Length) {
						return false;
					}

					PaintV1(frame, x, y, v1, data[at] * EntryBytes);
					at++;
				} else {
					if (at + 4 > data.Length) {
						return false;
					}

					PaintV4(frame, x, y, v4, data.Slice(at, 4));
					at += 4;
				}
			}
		}

		return true;
	}

	/// <summary>A V1 block: one entry scaled up, each of its four pixels covering a 2x2 quarter of the block.</summary>
	private static void PaintV1(VideoFrame frame, int x, int y, byte[] book, int entry) {
		for (int row = 0; row < 4; row++) {
			for (int column = 0; column < 4; column++) {
				Plot(frame, x + column, y + row, book, entry + ((((row >> 1) * 2) + (column >> 1)) * 3));
			}
		}
	}

	/// <summary>A V4 block: four entries, one per 2x2 quarter in the order top-left, top-right, bottom-left, bottom-right.</summary>
	private static void PaintV4(VideoFrame frame, int x, int y, byte[] book, ReadOnlySpan<byte> entries) {
		for (int row = 0; row < 4; row++) {
			for (int column = 0; column < 4; column++) {
				int entry = entries[((row >> 1) * 2) + (column >> 1)] * EntryBytes;
				Plot(frame, x + column, y + row, book, entry + ((((row & 1) * 2) + (column & 1)) * 3));
			}
		}
	}

	/// <summary>
	/// Plots one pixel. Cinepak rows run top-down whatever the sign of the format header's height.
	/// </summary>
	private static void Plot(VideoFrame frame, int x, int y, byte[] book, int at) =>
		frame.SetPixel(x, y, book[at], book[at + 1], book[at + 2]);

	private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

	private static int ReadU16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];

	private static int ReadU24(ReadOnlySpan<byte> data, int at) => (data[at] << 16) | (data[at + 1] << 8) | data[at + 2];

	private static uint ReadU32(ReadOnlySpan<byte> data, int at) =>
		((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];
}
