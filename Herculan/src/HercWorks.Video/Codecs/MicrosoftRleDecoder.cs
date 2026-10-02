using HercWorks.Video.Avi;

namespace HercWorks.Video.Codecs;

/// <summary>
/// Microsoft RLE (<c>mrle</c>), 8 bits per pixel.
///
/// <para>The payload is a stream of two-byte opcodes over a palettised surface. A leading non-zero
/// byte is a run: that many pixels of the colour in the second byte. A leading zero makes the second
/// byte an escape — 0 ends the row, 1 ends the frame, 2 introduces a two-byte
/// (right, up) delta that skips pixels without writing them, and 3 or more starts a literal run of
/// that many palette indices, word-aligned.</para>
///
/// <para>Skipped pixels keep whatever the previous frame left there, which is the whole reason this
/// codec is interframe: the four <c>*_TH.AVI</c> thumbnails are mostly a still image with a small
/// animated region, and almost every frame after the first is deltas and end-of-row escapes.</para>
///
/// <para>Only 8bpp is handled. The 4bpp variant packs two indices per byte and appears nowhere in
/// the retail corpus, so it is rejected rather than guessed at.</para>
/// </summary>
internal sealed class MicrosoftRleDecoder : IVideoCodec {
	internal MicrosoftRleDecoder(AviVideoFormat format) {
		_format = format;
		_palette = PaletteReader.Read(format);
	}

	private readonly AviVideoFormat _format;
	private readonly byte[] _palette;

	public bool DecodeFrame(ReadOnlySpan<byte> packet, VideoFrame frame) {
		// An empty packet is a legitimate "nothing changed" frame, not an error.
		if (packet.Length == 0) {
			return true;
		}

		if (_format.BitCount != 8) {
			return false;
		}

		int x = 0;
		int row = 0;
		int at = 0;

		while (at + 1 < packet.Length) {
			byte count = packet[at];
			byte value = packet[at + 1];
			at += 2;

			if (count > 0) {
				WriteRun(frame, x, row, count, value);
				x = Step(x, count, frame.Width);
				continue;
			}

			switch (value) {
				case 0:
					// End of row. The cursor drops a line whether or not the row was filled.
					x = 0;
					row++;
					break;

				case 1:
					return true;

				case 2: {
					if (at + 1 >= packet.Length) {
						return false;
					}

					// Deltas move right and *up the picture*, which for bottom-up rows means
					// forward through the stream's row order — the same direction end-of-row moves.
					x = Step(x, packet[at], frame.Width);
					row += packet[at + 1];
					at += 2;
					break;
				}

				default: {
					int literal = value;
					if (at + literal > packet.Length) {
						return false;
					}

					for (int i = 0; i < literal; i++) {
						Plot(frame, x + i, row, packet[at + i]);
					}

					x = Step(x, literal, frame.Width);
					// Literal runs are padded to an even length.
					at += literal + (literal & 1);
					break;
				}
			}

			if (row > frame.Height) {
				// Past the bottom with more opcodes to come: malformed. Everything already drawn
				// stays, because the caller may still want to show it.
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Writes a run, stopping at the right edge. Every pixel past it would be clipped anyway, so this
	/// changes no output; it bounds the work a packet can ask for, which is otherwise 255 writes for
	/// every two bytes of input.
	/// </summary>
	private void WriteRun(VideoFrame frame, int x, int row, int count, byte index) {
		for (int i = 0; i < count && x + i < frame.Width; i++) {
			Plot(frame, x + i, row, index);
		}
	}

	/// <summary>
	/// Moves the cursor right, pinned at the frame's right edge. Nothing past the edge is drawn and
	/// only end-of-row brings the cursor back, so pinning it changes no output; it keeps a packet of
	/// millions of runs from wrapping the cursor past <c>int.MaxValue</c> back into the frame.
	/// </summary>
	private static int Step(int x, int by, int width) => Math.Min(x + by, width);

	/// <summary>
	/// Plots one palette index, converting the stream's row number to a frame row.
	///
	/// <para>MS-RLE rows run bottom-up unless the format header says otherwise, so stream row 0 is
	/// the bottom line of the picture.</para>
	/// </summary>
	private void Plot(VideoFrame frame, int x, int row, byte index) {
		int y = _format.TopDown ? row : frame.Height - 1 - row;
		int at = index * 4;
		if (at + 2 >= _palette.Length) {
			return;
		}

		frame.SetPixel(x, y, _palette[at], _palette[at + 1], _palette[at + 2]);
	}
}

/// <summary>
/// Turns a <c>BITMAPINFOHEADER</c> palette into RGB triples, one per index.
///
/// <para>DIB palettes are stored blue-first as BGRX quads. A file that declares 8bpp but carries a
/// short palette gets the entries it actually has and black for the rest, rather than a rejection:
/// two of the retail thumbnails only ever reference the low half of theirs.</para>
/// </summary>
internal static class PaletteReader {
	internal static byte[] Read(AviVideoFormat format) {
		var rgb = new byte[256 * 4];
		int entries = Math.Min(256, format.Palette.Length / 4);

		for (int i = 0; i < entries; i++) {
			rgb[(i * 4) + 0] = format.Palette[(i * 4) + 2];
			rgb[(i * 4) + 1] = format.Palette[(i * 4) + 1];
			rgb[(i * 4) + 2] = format.Palette[(i * 4) + 0];
			rgb[(i * 4) + 3] = 0xFF;
		}

		return rgb;
	}
}
