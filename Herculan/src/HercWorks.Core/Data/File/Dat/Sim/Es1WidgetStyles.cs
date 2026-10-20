using System.Buffers.Binary;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's <c>STYLES.DAT</c> (<c>dat\</c> in <c>SIMVOL0.VOL</c> and <c>ZONES.VOL</c>, <c>cfg\</c> in
/// <c>SHELL0.VOL</c>): an <c>int16</c> count, then that many 64-byte widget style records, which both programs' loaders
/// read whole. Layout and loads: docs/es1/formats/styles-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1WidgetStylesTransformer"/>.
/// </summary>
public class Es1WidgetStyles {
	public Es1WidgetStyle[] Styles { get; set; } = [];
}

/// <summary>
/// One style record, a bevelled frame's colors and corner pieces as the simulator's <c>CornerFrame_Draw</c>
/// (<c>ES1DBSIM 00044954</c>) reads them (docs/es1/formats/styles-dat.md#the-layout). The bytes are kept whole and the
/// fields read from them.
/// </summary>
public class Es1WidgetStyle {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x40;

	/// <summary>The rings' edges in a ring's order: left, top, right, bottom.</summary>
	public const int EdgesPerRing = 4;

	public byte[] Bytes { get; set; } = new byte[Length];

	/// <summary><c>+0</c>: the inside's color.</summary>
	public int InsideColor => Dword(0);

	/// <summary>
	/// Dword <paramref name="index"/> of the record, as <c>CornerFrame_DrawEdgeRing</c> indexes it: 0 the inside's
	/// color, 1–4 the outer ring's left, top, right and bottom edge colors, 5–8 the middle ring's and 9–12 the inner
	/// ring's, 0 for no edge.
	/// </summary>
	public int Dword(int index) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(index * 4, 4));

	/// <summary>
	/// <c>+0x34</c>–<c>+0x3a</c>: the <c>dba\corners</c> frame of corner <paramref name="corner"/>, 0 the top left, 1 the
	/// top right, 2 the bottom left and 3 the bottom right.
	/// </summary>
	public short CornerFrame(int corner) => BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(0x34 + corner * 2, 2));

	/// <summary><c>+0x3c</c>: a corner frame's width.</summary>
	public short CornerWidth => BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(0x3c, 2));

	/// <summary><c>+0x3e</c>: a corner frame's height.</summary>
	public short CornerHeight => BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(0x3e, 2));
}
