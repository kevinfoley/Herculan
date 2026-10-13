namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// A <c>.DFN</c> or <c>.HFN</c> bitmap font, both games' (docs/retail/formats/dfn-hfn-dci.md#layout,
/// docs/es1/formats/dfn-hfn-dci.md#fonts). Each header field is kept as read and each array as stored, so a written font gives
/// back its bytes. Read by <see cref="Io.Transform.Common.DynamixFontTransformer"/>.
/// </summary>
public class DynamixFont {
	/// <summary>The envelope's <c>uint32</c> size of what follows it.</summary>
	public int DeclaredSize { get; set; }

	/// <summary>The header's <c>mapCount</c>; <see cref="Map"/> holds that many entries when <see cref="FirstCharCode"/> is -1.</summary>
	public short MapCount { get; set; }

	/// <summary>Character code of glyph 0.</summary>
	public short FirstCharCode { get; set; }

	/// <summary>Every glyph's height in rows, at <c>0x0e</c>.</summary>
	public short CellHeight { get; set; }

	/// <summary>-1 when the file carries <see cref="Widths"/>.</summary>
	public short WidthsFlag { get; set; }

	/// <summary>The cell height's second copy, at <c>0x12</c>.</summary>
	public short CellHeightRepeat { get; set; }

	public short Baseline { get; set; }

	public short BitsPerPixel { get; set; }

	/// <summary>The header short at <c>0x18</c>.</summary>
	public short Field18 { get; set; }

	/// <summary>The height a label centres by.</summary>
	public short InkHeight { get; set; }

	/// <summary>The <c>arrayCount</c> x 16 bytes the readers move ahead of the pool; its length / 16 is the header's <c>arrayCount</c>.</summary>
	public byte[] ArrayBlock { get; set; } = [];

	/// <summary>The glyph pool: each glyph <c>width x cellHeight</c> bytes, row by row, one palette index per pixel.</summary>
	public byte[] Pool { get; set; } = [];

	/// <summary>Each glyph's start in <see cref="Pool"/>; its length is the header's <c>glyphCount</c>.</summary>
	public int[] Offsets { get; set; } = [];

	/// <summary>Each glyph's width, its advance; null when <see cref="WidthsFlag"/> is not -1.</summary>
	public byte[]? Widths { get; set; }

	/// <summary>The character map; null when <see cref="FirstCharCode"/> is not -1.</summary>
	public short[]? Map { get; set; }

	/// <summary>
	/// Bytes past the walk, kept so a written font gives back its bytes: the envelope's rounding to an even length
	/// (docs/retail/formats/dfn-hfn-dci.md#the-shared-dynamix-resource-envelope).
	/// </summary>
	public byte[] Tail { get; set; } = [];
}
