
namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// A <c>.DBM</c> bitmap, or one frame of a <see cref="DynamixBitmapArray"/>. The file names no
/// palette; <see cref="Palette"/> is whichever <c>.DPL</c> a caller pairs with it. The record is the
/// envelope (<see cref="HeaderMagic"/>, then a <c>uint32</c> size of what follows), the 13-byte header
/// in property order from <see cref="Rows"/> to the extra count, the image data, then
/// <see cref="ExtraDwords"/>. See docs/retail/formats/dfn-hfn-dci.md, "The bitmap record".
/// </summary>
public class DynamixBitmap {
	/// <summary>
	/// Not in the file: a name the toolkit gives the bitmap for exporting it —
	/// <see cref="Io.Transform.Common.DynamixBitmapArrayTransformer"/> sets "_&lt;index&gt;" on each
	/// frame of a <c>.DBA</c>, and <see cref="Io.Read.DynFileReader"/> a loose <c>.DBM</c>'s own file
	/// name.
	/// </summary>
	public string? FileName { get; set; }

	/// <summary>
	/// Expected magic-byte header value; the bytes actually read from a given file are not
	/// retained, since the write path always emits this constant.
	/// </summary>
	public static readonly byte[] HeaderMagic = HercWorks.Core.Util.EndianOps.GetIntBEBytes(0x0E002800);

	/// <summary>Height in pixels.</summary>
	public short Rows { get; set; }

	/// <summary>Width in pixels, and the stride of an unpacked image's rows.</summary>
	public short Cols { get; set; }

	public byte BitsPerPixel { get; set; }

	/// <summary>Bits 0-3 the bitmap type, bit 4 opaque; kept whole so a write gives the byte back.</summary>
	public byte Flags { get; set; }

	public BitmapPacking Packing { get; set; }

	/// <summary>Length of <see cref="ImageData"/> as the header states it: the packed length when
	/// <see cref="Packing"/> is not <see cref="BitmapPacking.Raw"/>.</summary>
	public int ImageDataLen { get; set; }

	public DynamixPalette? Palette { get; set; }
	public byte[]? ImageData { get; set; }

	/// <summary>The dwords after the image data, as many as the header's extra count; empty when it is
	/// zero or negative.</summary>
	public uint[] ExtraDwords { get; set; } = [];
}

/// <summary>A bitmap's packing type. See docs/retail/formats/dfn-hfn-dci.md, "The bitmap record".</summary>
public enum BitmapPacking : byte {
	Raw = 0,
	Rle = 1,
	Lzh = 3,
}
