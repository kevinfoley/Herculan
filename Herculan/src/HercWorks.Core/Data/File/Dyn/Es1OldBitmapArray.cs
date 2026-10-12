namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// FILE - a bitmap array whose items are old-format bitmaps, which ES1's programs load as no object: ES1's
/// <c>RES32\ICONS.DBA</c> in SHELL0.VOL, which no load was found for. The layout is read from the bytes alone
/// (docs/es1/formats/dfn-hfn-dci.md#the-old-bitmap-record). The array's envelope is ES2's
/// (docs/retail/formats/dfn-hfn-dci.md#the-shared-dynamix-resource-envelope). Read by
/// <see cref="Io.Transform.Common.Es1OldBitmapArrayTransformer"/>.
/// </summary>
public class Es1OldBitmapArray {
	/// <summary>The envelope's size of what follows it, as stored: 5,206 in <c>ICONS.DBA</c>, 528 more than the file holds.</summary>
	public int DeclaredSize { get; set; }

	/// <summary>The items, as many as the array's <c>int32</c> count.</summary>
	public Es1OldBitmap[] Items { get; set; } = [];
}

/// <summary>One old-format bitmap record (docs/es1/formats/dfn-hfn-dci.md#the-old-bitmap-record).</summary>
public class Es1OldBitmap {
	/// <summary>The record's size of what follows its 8-byte envelope, as stored; always 14 + 4 × the pixel count.</summary>
	public int DeclaredSize { get; set; }

	/// <summary>Rows of pixels.</summary>
	public short Rows { get; set; }

	/// <summary>Pixels in a row.</summary>
	public short Cols { get; set; }

	/// <summary>The byte at <c>+0x0c</c>, 8 in every item; what it is is Open.</summary>
	public byte Field0C { get; set; }

	/// <summary>The three bytes at <c>+0x0d</c>, zero in every item; what they are is Open.</summary>
	public byte[] Field0D { get; set; } = new byte[3];

	/// <summary><see cref="Rows"/> rows of <see cref="Cols"/> palette indices; the stored count is this length.</summary>
	public byte[] Pixels { get; set; } = [];

	/// <summary>
	/// OPAQUE - the bytes after the pixels to the record's declared end, or to the file's end for a record the file cuts
	/// short. Not image data; what they are is Open.
	/// </summary>
	public byte[] Tail { get; set; } = [];
}
