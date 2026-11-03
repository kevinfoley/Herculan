namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// ES1's shell <c>BND\CTL_ALRT.BND</c>, which <c>CtlAlert_LoadBnd</c> (<c>ES1GO 00006024</c>) reads into
/// <c>CtlAlert_Bnd</c> (<c>0009a34c</c>): the controls panel's sizes and places, by widget, the buttons in widget order
/// and the readouts by row. Layout and values: docs/es1/formats/bnd-notes.md#bndalertbnd-and-bndctl_alrtbnd.
/// </summary>
public sealed class Es1CtlAlertBnd : Es1TuningView {
	/// <summary>The global's length, the retail file's.</summary>
	public const int Length = 134;

	public Es1CtlAlertBnd(byte[]? bytes) : base(bytes, Length) {
	}

	/// <summary>Offset 4, the panel's width.</summary>
	public short Width => Int16(4);

	/// <summary>Offset 6, the panel's height.</summary>
	public short Height => Int16(6);

	/// <summary>Offset 8, the title bar's margin, added to the title's width.</summary>
	public short TitleBarMargin => Int16(8);

	/// <summary>Offset <c>0xa</c>, the title bar's top.</summary>
	public short TitleTop => Int16(0xa);

	/// <summary>Offset <c>0xc</c>, the title bar's height.</summary>
	public short TitleHeight => Int16(0xc);

	/// <summary>Offset <c>0xe</c>, the title label's <i>y</i> offset.</summary>
	public short TitleOffsetY => Int16(0xe);

	/// <summary>Offset <c>0x10</c>, the buttons.</summary>
	public short ButtonCount => Int16(0x10);

	/// <summary>Offsets <c>0x12</c>, <c>0x20</c>, <c>0x2e</c> and <c>0x3c</c>: button <paramref name="index"/>'s <i>x</i>, <i>y</i>, width and height.</summary>
	public (short X, short Y, short Width, short Height) Button(int index) =>
		(Int16(0x12 + 2 * index), Int16(0x20 + 2 * index), Int16(0x2e + 2 * index), Int16(0x3c + 2 * index));

	/// <summary>Offset <c>0x4a</c>: button <paramref name="index"/>'s caption's <i>y</i> offset.</summary>
	public short CaptionOffsetY(int index) => Int16(0x4a + 2 * index);

	/// <summary>Offset <c>0x58</c>, the readouts.</summary>
	public short ReadoutCount => Int16(0x58);

	/// <summary>Offsets <c>0x5a</c>, <c>0x64</c>, <c>0x6e</c> and <c>0x78</c>: readout <paramref name="index"/>'s <i>x</i>, <i>y</i>, width and height.</summary>
	public (short X, short Y, short Width, short Height) Readout(int index) =>
		(Int16(0x5a + 2 * index), Int16(0x64 + 2 * index), Int16(0x6e + 2 * index), Int16(0x78 + 2 * index));

	/// <summary>Offsets <c>0x82</c> and <c>0x84</c>, the readouts' text offset.</summary>
	public (short X, short Y) ReadoutOffset => (Int16(0x82), Int16(0x84));
}
