using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Dyn;

/// <summary>
/// A <c>.DPL</c> palette: a header, the colour entries, and — in the theater palettes — a shade-ramp
/// table. Entries are <c>[R][G][B][flag]</c> with 6-bit channels; see
/// docs/retail/rendering/cockpit-canopy-palette.md for the file layout and
/// docs/retail/rendering/dts-texture-binding.md, "The .DPL shade-ramp table", for the tail.
/// </summary>
public class DynamixPalette {
	/// <summary>The 4-byte type marker every <c>.DPL</c> starts with.</summary>
	public static readonly byte[] Header = { 0x0F, 0x00, 0x28, 0x00 };

	public int ColorCount { get; set; }

	/// <summary>
	/// Not in the file: the factor the transformer scales the 6-bit channels up by on read and
	/// divides back out on write (4 unless the caller passes another).
	/// </summary>
	public int Scalar { get; set; } = 1;

	public int PaletteSizeByte { get; set; }
	public byte[]? RawIndexBytes { get; set; }
	public Dictionary<int, ColorBytes> Colors { get; set; } = new();

	/// <summary>
	/// Not in the file: the colour the toolkit's image export substitutes for palette index 0 when
	/// asked to key it out.
	/// </summary>
	public ColorBytes Index0AlphaKey { get; set; }

	/// <summary>
	/// The palette's <b>shade ramps</b>, from the file's tail after the colour entries: one per slot of
	/// the table, each a run of palette indices from darkest to brightest. A shaded surface's value
	/// names a ramp, not a colour, and the face's light level picks the step along it
	/// (<c>Palette_ShadeRampLookup</c>, <c>00430e34</c>). Layout and lookup are in
	/// docs/retail/rendering/dts-texture-binding.md, "The .DPL shade-ramp table" and "TSShadedPoly — shade-ramp
	/// number, per-face light, fixed .RMP row".
	///
	/// <para>Empty when the file carries no tail or a ramp count of zero.</para>
	/// </summary>
	public IReadOnlyList<short[]> ShadeRamps { get; set; } = Array.Empty<short[]>();

	/// <summary>
	/// The ramp table exactly as it was read, so the write path can put it back untouched rather
	/// than re-serialising a structure nothing in this project edits. Null when the file had none.
	/// </summary>
	public byte[]? ShadeRampBytes { get; set; }

	public DynamixPalette() {
		Index0AlphaKey = new ColorBytes(218, 164, 164, 255);
		Index0AlphaKey.SetColor(RgbaColor.FromArgb(255, 218, 164, 164));
	}

	public ColorBytes ColorAt(int idx) => Colors[idx];

	public int[] ToIntColorMap() {
		var cmap = new int[256];

		foreach (var shade in Colors.Keys) {
			cmap[shade] = Colors[shade].GetColor().ToArgb();
		}

		return cmap;
	}

	public byte[] ToByteArray() {
		var index = new List<byte>();

		foreach (var shade in Colors.Keys) {
			var arr = Colors[shade].Array;
			index.Add(arr[0]);
			index.Add(arr[1]);
			index.Add(arr[2]);
		}

		return index.ToArray();
	}
}
