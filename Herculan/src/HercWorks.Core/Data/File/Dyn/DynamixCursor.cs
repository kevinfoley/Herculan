namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// FILE - a <c>.DCI</c> cursor, in both games: the click point, then the image as an ordinary bitmap record, inside the
/// resource envelope tagged <see cref="HeaderMagic"/> (docs/retail/formats/dfn-hfn-dci.md#dci--cursor-image). Read by
/// <see cref="Io.Transform.Common.DynamixCursorTransformer"/>.
/// </summary>
public class DynamixCursor {
	/// <summary>The envelope's tag, the cursor class's.</summary>
	public static readonly byte[] HeaderMagic = HercWorks.Core.Util.EndianOps.GetIntBEBytes(0x0B002800);

	/// <summary>The click point's x, from the image's left edge.</summary>
	public int HotspotX { get; set; }

	/// <summary>The click point's y, from the image's top edge.</summary>
	public int HotspotY { get; set; }

	/// <summary>The image.</summary>
	public DynamixBitmap Bitmap { get; set; } = new();

	/// <summary>
	/// The bytes past the envelope's end, which the cursor's load does not read: 96 in <c>PCURSOR.DCI</c>, none in the
	/// other files (docs/retail/formats/dfn-hfn-dci.md#open).
	/// </summary>
	public byte[] Trailing { get; set; } = [];
}
