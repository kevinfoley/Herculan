using HercWorks.Video.Avi;
using HercWorks.Video.Codecs.Indeo3;
using HercWorks.Video.Codecs.Indeo4;
using HercWorks.Video.Riff;

namespace HercWorks.Video.Codecs;

/// <summary>
/// A frame decoder for one compression fourcc.
///
/// <para>Every codec here is interframe, so the contract is "update <paramref name="frame"/> in
/// place" rather than "return a new frame": a packet may say nothing more than which blocks changed,
/// and the rest of the picture has to still be there from last time.</para>
/// </summary>
public interface IVideoCodec {
	/// <summary>
	/// Applies one compressed packet to <paramref name="frame"/>.
	///
	/// <para>Returns false when the packet is malformed. A false return means the frame may have
	/// been partly updated — decoders write as they parse and do not roll back — so a player should
	/// keep showing it rather than treat it as garbage, and stop feeding the stream.</para>
	/// </summary>
	bool DecodeFrame(ReadOnlySpan<byte> packet, VideoFrame frame);
}

/// <summary>Picks the decoder for a stream's compression fourcc.</summary>
public static class CodecRegistry {
	/// <summary>Indeo Video 3.2, as <c>IV32</c>. 80 of the 93 retail files.</summary>
	public static uint Indeo3 { get; } = RiffReader.FourCc('I', 'V', '3', '2');

	/// <summary>Microsoft Video 1, as lowercase <c>msvc</c>, which is how the retail files spell it.</summary>
	public static uint MicrosoftVideo1 { get; } = RiffReader.FourCc('m', 's', 'v', 'c');

	/// <summary>Microsoft Video 1, as uppercase <c>MSVC</c>.</summary>
	public static uint MicrosoftVideo1Upper { get; } = RiffReader.FourCc('M', 'S', 'V', 'C');

	/// <summary>Microsoft Video 1, as <c>CRAM</c> — the same bitstream under its older name.</summary>
	public static uint MicrosoftVideo1Cram { get; } = RiffReader.FourCc('C', 'R', 'A', 'M');

	/// <summary>
	/// Microsoft RLE, as the numeric <c>BI_RLE8</c>.
	///
	/// <para>The four thumbnail files are listed as <c>mrle</c> in their stream header, but that is
	/// the VfW <em>handler</em> name; the format header they are actually decoded from carries the
	/// DIB compression constant 1. Matching on the fourcc alone silently misses all four.</para>
	/// </summary>
	public static uint MicrosoftRle8 { get; } = 1;

	/// <summary>Cinepak, as <c>cvid</c>.</summary>
	public static uint Cinepak { get; } = RiffReader.FourCc('c', 'v', 'i', 'd');

	/// <summary>Indeo Video 4.1, as <c>IV41</c>.</summary>
	public static uint Indeo4 { get; } = RiffReader.FourCc('I', 'V', '4', '1');

	/// <summary>
	/// Creates a decoder for <paramref name="format"/>, or returns null when its compression is one
	/// this assembly does not implement.
	///
	/// <para>An uncompressed stream (<c>BI_RGB</c>, compression 0) is treated as MS-RLE's
	/// absolute-mode cousin only when it is 8-bit; other depths return null rather than being
	/// guessed at.</para>
	/// </summary>
	public static IVideoCodec? Create(AviVideoFormat format, VideoLimits limits) {
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(limits);

		uint cc = format.Compression;

		if (cc == MicrosoftRle8) {
			return new MicrosoftRleDecoder(format);
		}

		if (cc == Indeo3) {
			return Indeo3Decoder.Create(format, limits);
		}

		if (cc == MicrosoftVideo1 || cc == MicrosoftVideo1Upper || cc == MicrosoftVideo1Cram) {
			return MicrosoftVideo1Decoder.Create(format);
		}

		if (cc == Cinepak) {
			return CinepakDecoder.Create(format);
		}

		if (cc == Indeo4) {
			return Indeo4Decoder.Create(format, limits);
		}

		return null;
	}
}
