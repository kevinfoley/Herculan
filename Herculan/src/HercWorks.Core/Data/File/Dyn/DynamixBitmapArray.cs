using HercWorks.Core.Util;

namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// A <c>.DBA</c> bitmap array — also <c>.HBA</c>, <c>.HB0</c>-<c>.HB2</c> and <c>.DB0</c>-<c>.DB2</c>:
/// a short header, then one <see cref="DynamixBitmap"/> per frame. See docs/retail/formats/dfn-hfn-dci.md,
/// "The shared "Dynamix resource" envelope".
/// </summary>
public class DynamixBitmapArray {
	/// <summary>
	/// Raw little-endian size bytes as read from the file. Kept because
	/// <see cref="Io.Transform.Common.DynamixBitmapArrayTransformer.Write"/> writes them back
	/// exactly as stored rather than recomputing them — see the note in that method.
	/// </summary>
	public byte[]? FileSize { get; set; }

	/// <summary>
	/// Expected magic-byte header value; the bytes actually read from a given file are not
	/// retained, since the write path always emits this constant.
	/// </summary>
	public static readonly byte[] HeaderMagic = EndianOps.GetIntBEBytes(0x01002800);

	/// <summary>The number of frames that follow the header.</summary>
	public short FrameCount { get; set; }

	/// <summary>Meaning not established; read and written back verbatim.</summary>
	public short ArrayCols { get; set; }

	public DynamixBitmap[]? Images { get; set; }

	/// <summary>
	/// Bytes past the last of the <see cref="FrameCount"/> frames, kept verbatim so a write gives the
	/// file back; null when the frames end the file. See docs/retail/formats/dfn-hfn-dci.md, "The
	/// shared "Dynamix resource" envelope".
	/// </summary>
	public byte[]? TrailingBytes { get; set; }

	/// <summary>Not in the file: a <c>.DBA</c> names no palette, so a caller pairs one with it.</summary>
	public DynamixPalette? Palette { get; set; }
}
